#if UNITY_EDITOR
// Assets/Scripts/Editor/GeneradorEdificiosOSM.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GENERADOR DE EDIFICIOS OSM REALES — estilo Cesium "white blocks"
//
//  Lee buildings_unity.json (datos OSM reales de Altsasua) y extrude cada
//  edificio como un bloque 3D con su footprint REAL, su altura REAL, y
//  posicionado sobre el terrain con altura correcta.
//
//  Esto reemplaza los edificios procedurales simples por la geometría OSM
//  real — exactamente como hace Cesium con sus "OSM Buildings".
//
//  MENÚ: Altsasu GTA → Territorio Real → ★ Generar Edificios OSM Reales
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class GeneradorEdificiosOSM
{
    const string JSON_PATH = "Assets/AlsasuaData/buildings_unity.json";
    const float  ALTURA_MIN     = 3f;   // mínimo: 1 planta
    const float  ALTURA_DEFAULT = 9f;   // 3 plantas si OSM no la define

    static Terrain _terrain;
    static Material _matBlanco, _matCascoViejo, _matIndustrial, _matIglesia, _matComercial;

    // MENÚ-LEGACY: [MenuItem("Altsasu GTA/Territorio Real/★ Generar Edificios OSM Reales", false, 3)]
    public static void Generar()
    {
        _terrain = Object.FindFirstObjectByType<Terrain>();
        if (_terrain == null)
        {
            EditorUtility.DisplayDialog("Sin terrain",
                "Crea primero el terrain:\nAltsasu GTA → Territorio Real → ★ Crear Terrain + Ortofoto",
                "OK"); return;
        }

        var jsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(JSON_PATH);
        if (jsonAsset == null)
        {
            EditorUtility.DisplayDialog("Sin datos OSM",
                $"No se encuentra {JSON_PATH}", "OK"); return;
        }

        // Limpiar edificios OSM antiguos — sin Undo (buffer demasiado grande con miles de hijos)
        var antiguo = GameObject.Find("Edificios_OSM_Reales");
        if (antiguo != null) Object.DestroyImmediate(antiguo);

        CrearMateriales();

        var padre = new GameObject("Edificios_OSM_Reales");
        Undo.RegisterCreatedObjectUndo(padre, "Edificios OSM");

        int generados = 0, errores = 0;
        try
        {
            var root = JArray.Parse(jsonAsset.text);
            int total = root.Count;

            for (int i = 0; i < total; i++)
            {
                if (i % 50 == 0)
                    EditorUtility.DisplayProgressBar("Edificios OSM",
                        $"Procesando {i}/{total}...", (float)i / total);

                try
                {
                    var b = root[i];
                    float cx = b["x"]?.Value<float>() ?? 0f;
                    float cz = b["z"]?.Value<float>() ?? 0f;
                    float alturaEdif = b["height"]?.Value<float>() ?? ALTURA_DEFAULT;
                    if (alturaEdif < ALTURA_MIN) alturaEdif = ALTURA_MIN;

                    string tipo = b["type"]?.Value<string>()
                                ?? b["building"]?.Value<string>()
                                ?? "yes";

                    var poly = b["poly"] as JArray;
                    if (poly == null || poly.Count < 6) continue; // mínimo 3 puntos = 6 valores

                    var puntos2D = new List<Vector2>();

                    // Detectar formato: array plano [x,z,x,z] o array de arrays [[x,z]]
                    if (poly[0] is JArray)
                    {
                        // Formato [[x,z], [x,z], ...]
                        foreach (var p in poly)
                        {
                            var pa = p as JArray;
                            if (pa != null && pa.Count >= 2)
                                puntos2D.Add(new Vector2(pa[0].Value<float>(), pa[1].Value<float>()));
                        }
                    }
                    else
                    {
                        // Formato plano [x1,z1,x2,z2,x3,z3,...] (el real de buildings_unity.json)
                        for (int k = 0; k + 1 < poly.Count; k += 2)
                        {
                            float px = poly[k].Value<float>();
                            float pz = poly[k + 1].Value<float>();
                            puntos2D.Add(new Vector2(px, pz));
                        }
                        // Eliminar el último punto si es duplicado del primero (polígono cerrado)
                        if (puntos2D.Count > 3 &&
                            Vector2.Distance(puntos2D[0], puntos2D[puntos2D.Count - 1]) < 0.001f)
                            puntos2D.RemoveAt(puntos2D.Count - 1);
                    }

                    if (CrearEdificioExtruido(cx, cz, puntos2D, alturaEdif, tipo, padre.transform))
                        generados++;
                }
                catch (System.Exception e)
                {
                    errores++;
                    if (errores < 5) Debug.LogWarning($"[OSM] Edificio {i}: {e.Message}");
                }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        // Guardar escena
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log($"[OSM] ✓ {generados} edificios OSM reales generados ({errores} errores).");
        EditorUtility.DisplayDialog("✅ Edificios OSM",
            $"Generados {generados} edificios OSM reales sobre el terrain.\n\n" +
            "Cada edificio tiene su footprint y altura real de OpenStreetMap.",
            "OK");
    }

    // =========================================================================
    //  EXTRUSIÓN DE UN EDIFICIO
    // =========================================================================

    static bool CrearEdificioExtruido(float worldX, float worldZ, List<Vector2> puntosRel,
                                        float altura, string tipo, Transform padre)
    {
        if (puntosRel.Count < 3) return false;

        // 1. Altura del terreno en el centro del edificio
        float yBase = _terrain.SampleHeight(new Vector3(worldX, 0, worldZ));

        // 2. Mesh extruida del cuerpo
        var mesh = ExtruirPoligono(puntosRel, altura);
        if (mesh == null) return false;

        // 3. GameObject raíz
        var root = new GameObject($"OSM_{tipo}");
        root.transform.SetParent(padre);
        root.transform.position = new Vector3(worldX, yBase, worldZ);

        // 4. Cuerpo principal con material según tipo
        var cuerpo = new GameObject("Cuerpo");
        cuerpo.transform.SetParent(root.transform);
        cuerpo.transform.localPosition = Vector3.zero;

        var mf = cuerpo.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = cuerpo.AddComponent<MeshRenderer>();
        mr.sharedMaterial = ElegirMaterial(tipo);

        if (altura > 4f)
        {
            var mc = cuerpo.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
        }

        // 5. Tejado (sloped Spanish tile o flat según tipo)
        AñadirTejado(root.transform, puntosRel, altura, tipo);

        // 6. Ventanas procedurales en las fachadas
        if (altura >= 4f && puntosRel.Count <= 12) // solo edificios sencillos
            AñadirVentanas(root.transform, puntosRel, altura);

        // 7. Chimenea ocasional
        if (Random.value < 0.2f && altura >= 6f)
            AñadirChimenea(root.transform, puntosRel, altura);

        return true;
    }

    static void AñadirTejado(Transform root, List<Vector2> contorno, float altura, string tipo)
    {
        bool esViejo = tipo.ToLower().Contains("historic") ||
                       tipo.ToLower().Contains("residential") ||
                       string.IsNullOrEmpty(tipo) || tipo == "yes";

        // Bounding box del contorno para calcular tejado
        Vector2 min = contorno[0], max = contorno[0];
        foreach (var p in contorno)
        {
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        Vector2 centro = (min + max) * 0.5f;
        Vector2 tamaño = max - min;

        var tejado = new GameObject("Tejado");
        tejado.transform.SetParent(root);
        tejado.transform.localPosition = new Vector3(centro.x, altura + 0.05f, centro.y);

        if (esViejo)
        {
            // Tejado a 2 aguas estilo español (rojo teja)
            var prisma = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prisma.name = "Aguas";
            prisma.transform.SetParent(tejado.transform);
            prisma.transform.localPosition = new Vector3(0, tamaño.y * 0.25f, 0);
            prisma.transform.localScale    = new Vector3(tamaño.x + 0.6f, tamaño.y * 0.5f, tamaño.y + 0.6f);
            prisma.transform.localRotation = Quaternion.Euler(0, 0, 45f);
            // Recortar la parte de abajo (estirar más alto y mover arriba)
            prisma.transform.localScale = new Vector3(tamaño.x + 0.6f, tamaño.y * 0.35f, tamaño.y + 0.6f);
            prisma.GetComponent<Renderer>().sharedMaterial = MatTejaRoja();
            Object.DestroyImmediate(prisma.GetComponent<Collider>());
        }
        else
        {
            // Tejado plano con borde
            var plano = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plano.name = "Plano";
            plano.transform.SetParent(tejado.transform);
            plano.transform.localPosition = new Vector3(0, 0.15f, 0);
            plano.transform.localScale = new Vector3(tamaño.x + 0.3f, 0.3f, tamaño.y + 0.3f);
            plano.GetComponent<Renderer>().sharedMaterial = MatTejadoPlano();
            Object.DestroyImmediate(plano.GetComponent<Collider>());
        }
    }

    static void AñadirVentanas(Transform root, List<Vector2> contorno, float altura)
    {
        int plantas = Mathf.Max(1, Mathf.RoundToInt(altura / 3.2f));
        int n = contorno.Count;

        var matVent = MatVentana();

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector2 a = contorno[i];
            Vector2 b = contorno[j];
            float ancho = Vector2.Distance(a, b);
            if (ancho < 2f) continue;

            Vector2 dir = (b - a).normalized;
            Vector2 perp = new Vector2(dir.y, -dir.x); // hacia afuera

            int ventanasPorPlanta = Mathf.Max(1, Mathf.FloorToInt(ancho / 2.5f));

            for (int p = 0; p < plantas; p++)
            {
                float yPlanta = (p + 0.5f) * 3.2f;
                if (yPlanta > altura - 0.5f) break;

                for (int v = 0; v < ventanasPorPlanta; v++)
                {
                    float t = (v + 0.5f) / ventanasPorPlanta;
                    Vector2 posPared = Vector2.Lerp(a, b, t);

                    var ven = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    ven.name = $"Ven_{p}_{v}";
                    ven.transform.SetParent(root);
                    ven.transform.localPosition = new Vector3(
                        posPared.x + perp.x * 0.05f,
                        yPlanta,
                        posPared.y + perp.y * 0.05f);
                    ven.transform.localRotation = Quaternion.LookRotation(
                        new Vector3(perp.x, 0, perp.y), Vector3.up);
                    ven.transform.localScale = new Vector3(1.0f, 1.3f, 0.05f);
                    ven.GetComponent<Renderer>().sharedMaterial = matVent;
                    Object.DestroyImmediate(ven.GetComponent<Collider>());
                }
            }
        }
    }

    static void AñadirChimenea(Transform root, List<Vector2> contorno, float altura)
    {
        Vector2 min = contorno[0], max = contorno[0];
        foreach (var p in contorno) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        Vector2 centro = Vector2.Lerp(min, max, Random.Range(0.3f, 0.7f));

        var ch = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ch.name = "Chimenea";
        ch.transform.SetParent(root);
        ch.transform.localPosition = new Vector3(centro.x, altura + 1.2f, centro.y);
        ch.transform.localScale    = new Vector3(0.6f, 1.8f, 0.6f);
        ch.GetComponent<Renderer>().sharedMaterial = MatChimenea();
        Object.DestroyImmediate(ch.GetComponent<Collider>());
    }

    // ── Materiales adicionales ────────────────────────────────────────────
    static Material _matTejaRoja, _matTejadoPlano, _matVentana, _matChimenea;
    static Material MatTejaRoja()
    {
        if (_matTejaRoja == null) {
            _matTejaRoja = CrearMat(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"),
                new Color(0.62f, 0.30f, 0.20f), "Mat_TejaRoja");
        }
        return _matTejaRoja;
    }
    static Material MatTejadoPlano()
    {
        if (_matTejadoPlano == null) {
            _matTejadoPlano = CrearMat(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"),
                new Color(0.30f, 0.30f, 0.30f), "Mat_TejadoPlano");
        }
        return _matTejadoPlano;
    }
    static Material MatVentana()
    {
        if (_matVentana == null) {
            var m = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
            m.SetColor("_BaseColor", new Color(0.25f, 0.40f, 0.55f));
            m.SetColor("_Color",     new Color(0.25f, 0.40f, 0.55f));
            m.SetFloat("_Smoothness", 0.85f);
            m.SetFloat("_Metallic",   0.3f);
            _matVentana = m;
        }
        return _matVentana;
    }
    static Material MatChimenea()
    {
        if (_matChimenea == null) {
            _matChimenea = CrearMat(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"),
                new Color(0.50f, 0.42f, 0.36f), "Mat_Chimenea");
        }
        return _matChimenea;
    }

    // =========================================================================
    //  MESH EXTRUSION
    // =========================================================================

    static Mesh ExtruirPoligono(List<Vector2> contorno, float altura)
    {
        // Asegurar orientación CCW (counter-clockwise) para normales correctas
        if (Area(contorno) < 0f) contorno.Reverse();

        int n = contorno.Count;
        var verts = new List<Vector3>();
        var tris  = new List<int>();
        var uvs   = new List<Vector2>();

        // ── Suelo (no se ve pero da volumen) ──
        // omitido por rendimiento (no se ve desde arriba)

        // ── Techo ──
        var indicesTecho = TriangularPoligono(contorno);
        if (indicesTecho == null) return null;

        int offsetTecho = verts.Count;
        foreach (var p in contorno)
        {
            verts.Add(new Vector3(p.x, altura, p.y));
            uvs.Add(new Vector2(p.x * 0.1f, p.y * 0.1f));
        }
        foreach (var idx in indicesTecho) tris.Add(idx + offsetTecho);

        // ── Paredes laterales ──
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector2 a = contorno[i];
            Vector2 b = contorno[j];

            int v = verts.Count;
            verts.Add(new Vector3(a.x, 0,       a.y));
            verts.Add(new Vector3(b.x, 0,       b.y));
            verts.Add(new Vector3(a.x, altura,  a.y));
            verts.Add(new Vector3(b.x, altura,  b.y));

            float anchoPared = Vector2.Distance(a, b);
            uvs.Add(new Vector2(0, 0));
            uvs.Add(new Vector2(anchoPared * 0.2f, 0));
            uvs.Add(new Vector2(0, altura * 0.2f));
            uvs.Add(new Vector2(anchoPared * 0.2f, altura * 0.2f));

            // Dos triángulos por pared
            tris.Add(v);     tris.Add(v + 2); tris.Add(v + 1);
            tris.Add(v + 1); tris.Add(v + 2); tris.Add(v + 3);
        }

        var mesh = new Mesh();
        if (verts.Count >= 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static float Area(List<Vector2> poly)
    {
        float a = 0f;
        for (int i = 0; i < poly.Count; i++)
        {
            int j = (i + 1) % poly.Count;
            a += (poly[j].x - poly[i].x) * (poly[j].y + poly[i].y);
        }
        return -a * 0.5f;
    }

    /// <summary>Triangulación ear-clipping simple para polígonos convexos o ligeramente cóncavos.</summary>
    static List<int> TriangularPoligono(List<Vector2> poly)
    {
        var result = new List<int>();
        var indices = new List<int>();
        for (int i = 0; i < poly.Count; i++) indices.Add(i);

        int seguridad = poly.Count * 3;
        while (indices.Count > 3 && seguridad-- > 0)
        {
            bool encontrada = false;
            for (int i = 0; i < indices.Count; i++)
            {
                int prev = indices[(i - 1 + indices.Count) % indices.Count];
                int curr = indices[i];
                int next = indices[(i + 1) % indices.Count];

                Vector2 a = poly[prev], b = poly[curr], c = poly[next];

                // Comprobar si es oreja convexa
                float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                if (cross < 0) continue;

                // Comprobar que ningún otro vértice está dentro
                bool valido = true;
                for (int k = 0; k < indices.Count; k++)
                {
                    if (k == (i - 1 + indices.Count) % indices.Count ||
                        k == i || k == (i + 1) % indices.Count) continue;
                    if (PuntoEnTriangulo(poly[indices[k]], a, b, c)) { valido = false; break; }
                }
                if (!valido) continue;

                result.Add(prev); result.Add(curr); result.Add(next);
                indices.RemoveAt(i);
                encontrada = true;
                break;
            }
            if (!encontrada) break;
        }
        if (indices.Count == 3)
        {
            result.Add(indices[0]); result.Add(indices[1]); result.Add(indices[2]);
        }
        return result.Count > 0 ? result : null;
    }

    static bool PuntoEnTriangulo(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
        float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
        float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
        bool neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        bool pos = (d1 > 0) || (d2 > 0) || (d3 > 0);
        return !(neg && pos);
    }

    // =========================================================================
    //  MATERIALES
    // =========================================================================

    static void CrearMateriales()
    {
        var sh = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");

        _matBlanco     = CrearMat(sh, new Color(0.90f, 0.88f, 0.82f), "Mat_OSM_Blanco");
        _matCascoViejo = CrearMat(sh, new Color(0.80f, 0.70f, 0.55f), "Mat_OSM_CascoViejo");
        _matIndustrial = CrearMat(sh, new Color(0.62f, 0.64f, 0.66f), "Mat_OSM_Industrial");
        _matIglesia    = CrearMat(sh, new Color(0.88f, 0.84f, 0.72f), "Mat_OSM_Iglesia");
        _matComercial  = CrearMat(sh, new Color(0.75f, 0.78f, 0.82f), "Mat_OSM_Comercial");
    }

    static Material CrearMat(Shader sh, Color color, string nombre)
    {
        var m = new Material(sh) { name = nombre };
        m.SetColor("_BaseColor", color);
        m.SetColor("_Color",     color);
        m.SetFloat("_Smoothness", 0.1f);
        m.SetFloat("_Metallic",   0f);
        return m;
    }

    static Material ElegirMaterial(string tipo)
    {
        if (string.IsNullOrEmpty(tipo)) return _matBlanco;
        tipo = tipo.ToLower();
        if (tipo.Contains("church") || tipo.Contains("chapel") || tipo.Contains("religious")) return _matIglesia;
        if (tipo.Contains("industrial") || tipo.Contains("warehouse") || tipo.Contains("factory")) return _matIndustrial;
        if (tipo.Contains("retail") || tipo.Contains("commercial") || tipo.Contains("supermarket")) return _matComercial;
        if (tipo.Contains("historic") || tipo.Contains("manor") || tipo.Contains("monument")) return _matCascoViejo;
        return _matBlanco;
    }
}
#endif
