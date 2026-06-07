// Assets/Scripts/Editor/GeneradorCarreteras.cs
// ═══════════════════════════════════════════════════════════════════════════
//  Tools → Alsasua → 🛣 Generar Carreteras Zone_01
//
//  Lee roads_unity.json (2.003 segmentos OSM) y genera meshes de carretera
//  estáticos con asfalto, bordillos y marcas viales — todo procedural.
//
//  Resultado: calles negras de Alsasua visibles desde el primer frame.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public static class GeneradorCarreteras
{
    private const string JSON_PATH   = "Assets/AlsasuaData/roads_unity.json";
    private const string ZONA_NOMBRE = "Zone_01_HerrikoPlaza_Centro";
    private const float  CENTRO_X    = GeoDataAlsasua.OX;
    private const float  CENTRO_Z    = GeoDataAlsasua.OZ;
    private const float  RADIO       = 380f;
    private const float  Y_OFFSET    = 0.04f;  // sobre el terreno, evita z-fighting

    [MenuItem("Tools/Alsasua/Mundo/🛣 Generar Carreteras", priority = 13)]
    public static void Generar()
    {
        string rutaAbs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", JSON_PATH));
        if (!File.Exists(rutaAbs))
        { EditorUtility.DisplayDialog("Error", $"No se encontró:\n{rutaAbs}", "OK"); return; }

        var zona = GameObject.Find(ZONA_NOMBRE);
        if (zona == null)
        { EditorUtility.DisplayDialog("Error", "Zone_01 no existe en escena.", "OK"); return; }

        bool activa = zona.activeSelf; zona.SetActive(true);
        LimpiarCarreteras(zona);

        // Materiales
        var matAsfalto  = CrearMatAsfalto();
        var matBordillo = CrearMatBordillo();
        var matCamino   = CrearMatCamino();

        // Contenedor
        var contenedor = new GameObject("Carreteras");
        Undo.RegisterCreatedObjectUndo(contenedor, "Crear carreteras");
        contenedor.transform.SetParent(zona.transform, false);
        contenedor.isStatic = true;

        var segmentos = ParsearSegmentos(File.ReadAllText(rutaAbs));
        int generados = 0;

        // Agrupar por tipo para batching
        var batches = new Dictionary<string, (List<Vector3> verts, List<int> tris, List<Vector2> uvs)>
        {
            ["asfalto"]  = (new(), new(), new()),
            ["bordillo"] = (new(), new(), new()),
            ["camino"]   = (new(), new(), new()),
        };

        EditorUtility.DisplayProgressBar("Generando carreteras", "...", 0f);

        for (int i = 0; i < segmentos.Count; i++)
        {
            var seg = segmentos[i];
            if (seg.pts.Count < 2) continue;

            // Filtrar por zona
            float dx = seg.pts[0].x - CENTRO_X, dz = seg.pts[0].z - CENTRO_Z;
            if (dx*dx + dz*dz > RADIO * RADIO * 1.5f) continue;

            EditorUtility.DisplayProgressBar("Generando carreteras",
                $"{i}/{segmentos.Count}", (float)i/segmentos.Count);

            string batch = TipoBatch(seg.tipo);
            var (verts, tris, uvs) = batches[batch];

            AnadirCinta(seg, verts, tris, uvs);
            if (batch == "asfalto") AnadirBordillos(seg, batches["bordillo"]);
            generados++;
        }

        // Crear un GO por batch
        foreach (var kv in batches)
        {
            if (kv.Value.verts.Count == 0) continue;
            Material mat = kv.Key == "asfalto"  ? matAsfalto
                         : kv.Key == "bordillo" ? matBordillo : matCamino;
            var go = new GameObject($"Batch_{kv.Key}");
            go.transform.SetParent(contenedor.transform, false);
            go.isStatic = true;
            go.layer    = 0;
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial     = mat;
            mr.shadowCastingMode  = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows     = true;
            mf.sharedMesh = BuildMesh($"Road_{kv.Key}", kv.Value.verts, kv.Value.tris, kv.Value.uvs);

            // Collider en asfalto para que el coche no se hunda
            if (kv.Key == "asfalto")
                go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        }

        EditorUtility.ClearProgressBar();
        zona.SetActive(activa);
        EditorUtility.SetDirty(zona);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        Debug.Log($"[Carreteras] ✅ {generados} segmentos generados en Zone_01.");
        EditorUtility.DisplayDialog("Carreteras generadas",
            $"✅ {generados} segmentos de calle reales de Alsasua.\n\n" +
            "Las calles negras ya son visibles en Scene View.", "OK");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GENERACIÓN DE MESH DE CINTA
    // ─────────────────────────────────────────────────────────────────────────

    static void AnadirCinta(SegmentoVia seg,
                             List<Vector3> verts, List<int> tris, List<Vector2> uvs)
    {
        float w = seg.ancho * 0.5f;
        float uAcum = 0f;

        // Suavizar waypoints OSM con Catmull-Rom antes de generar la malla
        var pts = seg.pts.Count >= 4
            ? IntegradorMatematicas.SuavizarCarretera(seg.pts, divisiones: 6)
            : seg.pts;

        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector3 p0 = pts[i], p1 = pts[i + 1];
            p0.y += Y_OFFSET; p1.y += Y_OFFSET;

            // Elevar al terreno si está disponible
            if (Terrain.activeTerrain != null)
            {
                p0.y = Terrain.activeTerrain.SampleHeight(new Vector3(p0.x, 0, p0.z)) + Y_OFFSET;
                p1.y = Terrain.activeTerrain.SampleHeight(new Vector3(p1.x, 0, p1.z)) + Y_OFFSET;
            }

            Vector3 dir   = (p1 - p0);
            float   len   = dir.magnitude;
            if (len < 0.01f) continue;
            Vector3 right = Vector3.Cross(dir.normalized, Vector3.up).normalized;

            int v0 = verts.Count;
            verts.Add(p0 + right * w);
            verts.Add(p0 - right * w);
            verts.Add(p1 - right * w);
            verts.Add(p1 + right * w);

            float u0 = uAcum / seg.ancho, u1 = (uAcum + len) / seg.ancho;
            uvs.Add(new Vector2(0, u0)); uvs.Add(new Vector2(1, u0));
            uvs.Add(new Vector2(1, u1)); uvs.Add(new Vector2(0, u1));
            uAcum += len;

            tris.AddRange(new[]{ v0, v0+1, v0+2,  v0, v0+2, v0+3 });
        }
    }

    // Bordillo: tira de 0.12m a cada lado de la calle, elevada 0.08m
    static void AnadirBordillos(SegmentoVia seg,
        (List<Vector3> verts, List<int> tris, List<Vector2> uvs) batch)
    {
        float w = seg.ancho * 0.5f + 0.06f;
        const float bw = 0.12f, bh = 0.08f;

        foreach (int lado in new[]{ -1, 1 })
        {
            float uAcum = 0f;
            for (int i = 0; i < seg.pts.Count - 1; i++)
            {
                Vector3 p0 = seg.pts[i], p1 = seg.pts[i + 1];
                if (Terrain.activeTerrain != null)
                {
                    p0.y = Terrain.activeTerrain.SampleHeight(new Vector3(p0.x, 0, p0.z)) + Y_OFFSET;
                    p1.y = Terrain.activeTerrain.SampleHeight(new Vector3(p1.x, 0, p1.z)) + Y_OFFSET;
                }
                Vector3 dir = p1 - p0; float len = dir.magnitude; if (len < 0.01f) continue;
                Vector3 right = Vector3.Cross(dir.normalized, Vector3.up).normalized * lado;

                int v0 = batch.verts.Count;
                batch.verts.Add(p0 + right * w);
                batch.verts.Add(p0 + right * (w + bw));
                batch.verts.Add(p1 + right * (w + bw));
                batch.verts.Add(p1 + right * w);
                batch.verts.Add(p0 + right * w         + Vector3.up * bh);
                batch.verts.Add(p0 + right * (w + bw)  + Vector3.up * bh);
                batch.verts.Add(p1 + right * (w + bw)  + Vector3.up * bh);
                batch.verts.Add(p1 + right * w         + Vector3.up * bh);

                float u1 = uAcum / bw;
                for (int k = 0; k < 8; k++) batch.uvs.Add(new Vector2(k < 4 ? 0 : 1, u1));
                uAcum += len;

                // Top
                batch.tris.AddRange(new[]{ v0+4,v0+5,v0+6, v0+4,v0+6,v0+7 });
                // Cara exterior
                batch.tris.AddRange(new[]{ v0+1,v0+5,v0+2, v0+2,v0+5,v0+6 });
                // Cara interior
                batch.tris.AddRange(new[]{ v0+0,v0+3,v0+4, v0+3,v0+7,v0+4 });
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  MATERIALES
    // ─────────────────────────────────────────────────────────────────────────

    static Material CrearMatAsfalto()
    {
        var m = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"))
        { name = "Mat_Asfalto" };
        SetColor(m, "_BaseColor", new Color(0.12f, 0.12f, 0.12f));
        SetFloat(m, "_Smoothness", 0.08f);
        SetFloat(m, "_Metallic",   0f);
        // Intento cargar textura de asfalto si existe
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
            "Assets/_ExtractedAssets/Textures/Asfalto_4K/Asfalto_4K.fbx");
        if (tex != null && m.HasProperty("_BaseColorMap")) m.SetTexture("_BaseColorMap", tex);
        return m;
    }

    static Material CrearMatBordillo()
    {
        var m = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"))
        { name = "Mat_Bordillo" };
        SetColor(m, "_BaseColor", new Color(0.78f, 0.76f, 0.72f));
        SetFloat(m, "_Smoothness", 0.1f);
        return m;
    }

    static Material CrearMatCamino()
    {
        var m = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"))
        { name = "Mat_Camino" };
        SetColor(m, "_BaseColor", new Color(0.45f, 0.38f, 0.28f));
        SetFloat(m, "_Smoothness", 0.04f);
        return m;
    }

    static void SetColor(Material m, string p, Color c) { if (m.HasProperty(p)) m.SetColor(p, c); else m.color = c; }
    static void SetFloat(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }

    // ─────────────────────────────────────────────────────────────────────────
    //  PARSEO
    // ─────────────────────────────────────────────────────────────────────────

    class SegmentoVia { public float ancho; public string tipo; public List<Vector3> pts = new(); }

    static List<SegmentoVia> ParsearSegmentos(string json)
    {
        var lista = new List<SegmentoVia>();
        int pos = 0;
        while (pos < json.Length)
        {
            int ini = json.IndexOf('{', pos); if (ini < 0) break;
            int depth = 0, fin = ini;
            for (int k = ini; k < json.Length; k++)
            {
                if (json[k]=='{' || json[k]=='[') depth++;
                else if (json[k]=='}'||json[k]==']') depth--;
                if (depth == 0) { fin = k; break; }
            }
            string obj = json.Substring(ini, fin - ini + 1);
            var s = new SegmentoVia
            {
                ancho = LeerF(obj, "w"),
                tipo  = LeerS(obj, "t")
            };
            // Parsear pts [x,y,z, x,y,z, ...]
            int pi = obj.IndexOf("\"pts\":["); if (pi >= 0)
            {
                pi = obj.IndexOf('[', pi); int pf = obj.IndexOf(']', pi);
                var nums = obj.Substring(pi+1, pf-pi-1).Split(',');
                for (int k = 0; k + 2 < nums.Length; k += 3)
                    if (float.TryParse(nums[k].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                        float.TryParse(nums[k+1].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                        float.TryParse(nums[k+2].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float z))
                        s.pts.Add(new Vector3(x, y, z));
            }
            if (s.pts.Count >= 2) lista.Add(s);
            pos = fin + 1;
        }
        return lista;
    }

    static string TipoBatch(string tipo)
    {
        if (tipo == null) return "asfalto";
        if (tipo.Contains("urbana") || tipo.Contains("calzada") || tipo.Contains("pavimentada"))
            return "asfalto";
        if (tipo.Contains("Camino") || tipo.Contains("camino") || tipo.Contains("no pavimentada"))
            return "camino";
        return "asfalto";
    }

    static float LeerF(string json, string key)
    {
        int ki = json.IndexOf($"\"{key}\":"); if (ki<0) return 0f;
        ki += key.Length+3; int fin=ki;
        while (fin<json.Length && (char.IsDigit(json[fin])||json[fin]=='.'||json[fin]=='-')) fin++;
        return float.TryParse(json.Substring(ki,fin-ki).Trim(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;
    }

    static string LeerS(string json, string key)
    {
        int ki = json.IndexOf($"\"{key}\":\""); if (ki<0) return "";
        ki += key.Length+4; int fin = json.IndexOf('"', ki);
        return fin<0 ? "" : json.Substring(ki, fin-ki);
    }

    static Mesh BuildMesh(string name, List<Vector3> v, List<int> t, List<Vector2> uv)
    {
        var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(v); m.SetTriangles(t, 0); m.SetUVs(0, uv);
        m.RecalculateNormals(); m.RecalculateBounds(); m.Optimize(); return m;
    }

    static void LimpiarCarreteras(GameObject zona)
    {
        var t = zona.transform.Find("Carreteras");
        if (t != null) Undo.DestroyObjectImmediate(t.gameObject);
    }
}
