// Assets/Scripts/Editor/ConstructorCallesAssets.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONSTRUCTOR DE CALLES v2 — red viaria completa con rotondas y marcas
//
//  Genera 4 mallas separadas desde roads_unity.json (2.003 segmentos OSM):
//
//  1. ASFALTO     — cinta para cada segmento con anchura real por tipo
//                   (motorway 12m → residential 5m → pedestrian 2.5m)
//  2. SEÑALIZACIÓN — línea de centro discontinua (primarias/secundarias) y
//                    continua (autovía mediana)
//  3. ACERAS      — franja de hormigón a ambos lados de calles peatonales
//                    y residenciales
//  4. ROTONDAS    — los segmentos cerrados (inicio≈fin) se detectan como
//                    rotondas y se renderizan como anillo relleno con isla
//                    central verde
//
//  Todo static → 4 draw calls para toda la red viaria de Alsasua.
//  Menú: Tools/Alsasua/Mundo/🛣️ Construir Calles + Autovía (full, v2)
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ConstructorCallesAssets
{
    const string JSON          = "Assets/AlsasuaData/roads_unity.json";
    const string RAIZ          = "Calles_Asset";
    const string MAT_ASFALTO   = "Assets/Materials/Roads/M_Asfalto_Carretera.mat";
    const string MAT_SEÑAL     = "Assets/Materials/Roads/M_Lineas_Carretera.mat";    // marcas viales existentes
    const string MAT_ACERA     = "Assets/Materials/Roads/M_Arcen_Hormigon.mat";      // arcén como fallback de acera
    const string MAT_ROTONDA   = "Assets/Materials/Roads/M_Asfalto_Carretera.mat";   // mismo asfalto (distinto GO)
    const string MAT_ISLA      = "Assets/Materials/Roads/M_Isla_Rotonda.mat";        // verde; si falta → fallback asfalto
    const float  Y_OFFSET      = 0.06f;   // sobre el terreno, evita z-fighting
    const float  Y_SEÑAL       = 0.08f;   // marcas por encima del asfalto
    const float  Y_ACERA       = 0.10f;   // acera ligeramente elevada
    const float  Y_ROTONDA     = 0.07f;
    const float  DIST_ROTONDA  = 4f;      // distancia start-end para detectar circuito cerrado

    [System.Serializable] class Pt  { public float x, z; }
    [System.Serializable] class Seg { public long id; public string type, junction, name; public float width; public bool oneway; public Pt[] points; }
    [System.Serializable] class Wrap { public Seg[] items; }

    // ── Anchuras por tipo OSM ──────────────────────────────────────────────
    static float AnchoPorTipo(Seg s)
    {
        return s.type switch
        {
            "motorway" or "trunk" or "motorway_link" or "trunk_link" => 12f,
            "primary"  or "primary_link"                              => 9f,
            "secondary"                                               => 7.5f,
            "tertiary"                                                => 6.5f,
            "residential" or "living_street" or "unclassified"       => 5f,
            "service"                                                 => 4f,
            "pedestrian" or "footway" or "path" or "steps"           => 2.5f,
            _ => (s.width >= 2f && s.width <= 14f) ? s.width : 5f,
        };
    }

    static bool EsAutovia(Seg s) => s.type is "motorway" or "trunk" or "motorway_link" or "trunk_link";
    static bool EsPrimaria(Seg s) => s.type is "primary" or "primary_link" or "secondary";
    static bool EsPeatonal(Seg s) => s.type is "pedestrian" or "footway" or "path" or "steps";
    static bool EsResidencial(Seg s) => s.type is "residential" or "living_street";

    // Un segmento es rotonda si está marcado como junction=roundabout O si es
    // un circuito cerrado (primer punto ≈ último punto dentro de DIST_ROTONDA m).
    static bool EsRotonda(Seg s)
    {
        if (s.junction == "roundabout" || s.type == "roundabout") return true;
        if (s.points == null || s.points.Length < 4) return false;
        var p0 = s.points[0]; var pN = s.points[^1];
        float dx = (p0.x - pN.x), dz = (p0.z - pN.z);
        return dx * dx + dz * dz < DIST_ROTONDA * DIST_ROTONDA;
    }

    // ── Menú principal ─────────────────────────────────────────────────────
    [MenuItem("Tools/Alsasua/Mundo/🛣️ Construir Calles + Autovía (full, v2)", priority = 12)]
    public static void Construir()
    {
        string ruta = Path.GetFullPath(Path.Combine(Application.dataPath, "..", JSON));
        if (!File.Exists(ruta)) { EditorUtility.DisplayDialog("Calles v2", $"No existe:\n{JSON}", "Vale"); return; }

        // Materiales (los que no existan se sustituyen por un material magenta de fallback)
        var matAsfalto = CargarMat(MAT_ASFALTO);
        var matSeñal   = CargarMat(MAT_SEÑAL,   matAsfalto);
        var matAcera   = CargarMat(MAT_ACERA,   matAsfalto);
        var matRotonda = CargarMat(MAT_ROTONDA,  matAsfalto);
        var matIsla    = CargarMat(MAT_ISLA,     matAsfalto);

        Wrap w;
        try { w = JsonUtility.FromJson<Wrap>("{\"items\":" + File.ReadAllText(ruta) + "}"); }
        catch (System.Exception e) { EditorUtility.DisplayDialog("Calles v2", $"JSON error: {e.Message}", "Vale"); return; }
        if (w?.items == null || w.items.Length == 0) { EditorUtility.DisplayDialog("Calles v2", "JSON sin segmentos.", "Vale"); return; }

        if (!EditorUtility.DisplayDialog("Construir Calles v2",
            $"{w.items.Length} segmentos → asfalto + señalización + aceras + rotondas.\n" +
            "4 mallas static (4 draw calls para toda la red vial). ¿Continuar?", "Construir", "Cancelar"))
            return;

        var terrain = Terrain.activeTerrain;
        _terrenos = Terrain.activeTerrains;   // mosaico V2: todos los tiles colocados

        // Buffers por malla
        var (vA, tA, uA) = Buffers();   // asfalto
        var (vS, tS, uS) = Buffers();   // señalización
        var (vC, tC, uC) = Buffers();   // aceras (curb/pavement)
        var (vR, tR, uR) = Buffers();   // rotondas (anillo)
        var (vI, tI, uI) = Buffers();   // isla rotonda (disco central)

        int nSeg = 0, nRotondas = 0;
        try
        {
            foreach (var s in w.items)
            {
                nSeg++;
                if (nSeg % 50 == 0 && EditorUtility.DisplayCancelableProgressBar(
                    "Calles v2", $"Segmento {nSeg}/{w.items.Length}…", nSeg / (float)w.items.Length)) break;
                if (s.points == null || s.points.Length < 2) continue;

                if (EsRotonda(s))
                {
                    nRotondas++;
                    GenerarRotonda(s, terrain, vR, tR, uR, vI, tI, uI);
                    continue;
                }

                float half  = AnchoPorTipo(s) * 0.5f;
                float vAcum = 0f;
                float halfAcera = 0.8f;   // ancho acera

                for (int i = 0; i < s.points.Length - 1; i++)
                {
                    var a2 = World(s.points[i]);
                    var b2 = World(s.points[i + 1]);
                    Vector2 dir = b2 - a2;
                    float len = dir.magnitude;
                    if (len < 0.05f) continue;
                    dir /= len;
                    Vector2 perp = new Vector2(-dir.y, dir.x);

                    float ya = Y(terrain, a2, Y_OFFSET);
                    float yb = Y(terrain, b2, Y_OFFSET);

                    // ── Asfalto ──────────────────────────────────────────
                    float vNext = vAcum + len / (half * 2f);
                    AñadirQuad(vA, tA, uA,
                        a2 - perp * half, a2 + perp * half,
                        b2 - perp * half, b2 + perp * half,
                        ya, yb, vAcum, vNext);

                    // ── Aceras (residencial + peatonal) ───────────────────
                    if (EsResidencial(s) || EsPeatonal(s))
                    {
                        float yaCurb = Y(terrain, a2, Y_ACERA);
                        float ybCurb = Y(terrain, b2, Y_ACERA);
                        // Acera izquierda
                        AñadirQuad(vC, tC, uC,
                            a2 - perp * (half + halfAcera), a2 - perp * half,
                            b2 - perp * (half + halfAcera), b2 - perp * half,
                            yaCurb, ybCurb, vAcum, vNext);
                        // Acera derecha
                        AñadirQuad(vC, tC, uC,
                            a2 + perp * half, a2 + perp * (half + halfAcera),
                            b2 + perp * half, b2 + perp * (half + halfAcera),
                            yaCurb, ybCurb, vAcum, vNext);
                    }

                    // ── Señalización (línea central) ──────────────────────
                    if (EsAutovia(s) || EsPrimaria(s))
                    {
                        float yaS = Y(terrain, a2, Y_SEÑAL);
                        float ybS = Y(terrain, b2, Y_SEÑAL);
                        float lineaW = EsAutovia(s) ? 0.15f : 0.08f;
                        // Autovía: línea continua; primaria/secundaria: solo si el segmento
                        // modulo permite (simula rayas discontinuas sin subdividir la malla).
                        bool dibujar = EsAutovia(s) || ((i % 3) != 2); // ~2/3 visible = raya discont.
                        if (dibujar)
                            AñadirQuad(vS, tS, uS,
                                a2 - perp * lineaW, a2 + perp * lineaW,
                                b2 - perp * lineaW, b2 + perp * lineaW,
                                yaS, ybS, vAcum, vNext);

                        // Autovía: líneas de borde blancas CONTINUAS a ambos lados
                        // (detalle que faltaba — una autovía real siempre las lleva).
                        if (EsAutovia(s))
                        {
                            float borde = Mathf.Max(0.3f, half - 0.30f);
                            const float w2 = 0.12f;
                            AñadirQuad(vS, tS, uS,
                                a2 - perp * (borde + w2), a2 - perp * (borde - w2),
                                b2 - perp * (borde + w2), b2 - perp * (borde - w2),
                                yaS, ybS, vAcum, vNext);
                            AñadirQuad(vS, tS, uS,
                                a2 + perp * (borde - w2), a2 + perp * (borde + w2),
                                b2 + perp * (borde - w2), b2 + perp * (borde + w2),
                                yaS, ybS, vAcum, vNext);
                        }
                    }

                    vAcum = vNext;
                }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        // ── Crear GameObjects ──────────────────────────────────────────────
        var raizAnt = GameObject.Find(RAIZ);
        if (raizAnt != null) Object.DestroyImmediate(raizAnt);
        var raiz = new GameObject(RAIZ);

        var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI;

        CrearMallaGO("Asfalto",        raiz, vA, tA, uA, matAsfalto, flags);
        CrearMallaGO("Señalizacion",   raiz, vS, tS, uS, matSeñal,   flags);
        CrearMallaGO("Aceras",         raiz, vC, tC, uC, matAcera,   flags);
        CrearMallaGO("Rotondas",       raiz, vR, tR, uR, matRotonda, flags);
        CrearMallaGO("Isla_Rotondas",  raiz, vI, tI, uI, matIsla,    flags);

        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        int totalVerts = vA.Count + vS.Count + vC.Count + vR.Count + vI.Count;
        Debug.Log($"[Calles v2] ✅ {nSeg} segmentos ({nRotondas} rotondas) → {totalVerts} verts · 5 mallas · asfalto+señal+acera+rotondas. 1 draw call por malla.");
        EditorUtility.DisplayDialog("Calles v2",
            $"✅ Red viaria completa:\n" +
            $"  • {nSeg - nRotondas} segmentos de calle\n" +
            $"  • {nRotondas} rotondas detectadas\n" +
            $"  • Aceras en calles residenciales/peatonales\n" +
            $"  • Marcas viales en primarias y autovías\n\n" +
            "5 mallas static. Raíz 'Calles_Asset'.", "Genial");
    }

    // ── Rotonda: anillo de asfalto + disco de isla ─────────────────────────
    static void GenerarRotonda(Seg s, Terrain terrain,
        List<Vector3> vR, List<int> tR, List<Vector2> uR,
        List<Vector3> vI, List<int> tI, List<Vector2> uI)
    {
        // Centroide de la polilínea
        Vector2 c = Vector2.zero;
        foreach (var p in s.points) c += World(p);
        c /= s.points.Length;

        // Radio exterior ≈ mitad de la mayor distancia al centroide
        float radioMax = 0f;
        foreach (var p in s.points)
        {
            float d = (World(p) - c).magnitude;
            if (d > radioMax) radioMax = d;
        }
        radioMax = Mathf.Max(radioMax, 4f); // mínimo 4m
        float radioIsla    = Mathf.Max(1.5f, radioMax * 0.4f);  // isla: 40% del radio
        float radioAsfalto = radioMax + AnchoPorTipo(s) * 0.5f; // borde exterior

        float yC = Y(terrain, c, Y_ROTONDA);
        int pasos = 24; // segmentos del círculo

        // Anillo (asfalto): radioIsla → radioAsfalto
        GenerarAnillo(c, yC, radioIsla, radioAsfalto, pasos, vR, tR, uR);
        // Disco (isla verde): 0 → radioIsla
        GenerarDisco(c, yC + 0.02f, radioIsla * 0.95f, pasos, vI, tI, uI);
    }

    static void GenerarAnillo(Vector2 c, float y, float rInt, float rExt, int n,
        List<Vector3> v, List<int> t, List<Vector2> u)
    {
        int base0 = v.Count;
        for (int i = 0; i <= n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f;
            float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
            float uv = i / (float)n;
            v.Add(new Vector3(c.x + cos * rInt, y, c.y + sin * rInt));
            v.Add(new Vector3(c.x + cos * rExt, y, c.y + sin * rExt));
            u.Add(new Vector2(0f, uv));
            u.Add(new Vector2(1f, uv));
        }
        for (int i = 0; i < n; i++)
        {
            int b = base0 + i * 2;
            t.Add(b); t.Add(b + 2); t.Add(b + 1);
            t.Add(b + 1); t.Add(b + 2); t.Add(b + 3);
        }
    }

    static void GenerarDisco(Vector2 c, float y, float r, int n,
        List<Vector3> v, List<int> t, List<Vector2> u)
    {
        int centroIdx = v.Count;
        v.Add(new Vector3(c.x, y, c.y));
        u.Add(new Vector2(0.5f, 0.5f));
        for (int i = 0; i <= n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f;
            v.Add(new Vector3(c.x + Mathf.Cos(a) * r, y, c.y + Mathf.Sin(a) * r));
            u.Add(new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f));
            if (i > 0)
            {
                t.Add(centroIdx);
                t.Add(centroIdx + i);
                t.Add(centroIdx + i + 1);
            }
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    static (List<Vector3>, List<int>, List<Vector2>) Buffers() =>
        (new List<Vector3>(1 << 14), new List<int>(1 << 15), new List<Vector2>(1 << 14));

    static Vector2 World(Pt p) => new Vector2(p.x + GeoDataAlsasua.OX, p.z + GeoDataAlsasua.OZ);

    // Mosaico V2: tiles del terreno cacheados; altura sobre el tile que CONTIENE el punto
    // (Terrain.activeTerrain con 48 tiles devuelve uno arbitrario / 0 fuera de su tile).
    static Terrain[] _terrenos;
    static float AlturaEnMosaico(Terrain[] ts, float x, float z)
    {
        if (ts == null) return 0f;
        for (int i = 0; i < ts.Length; i++)
        {
            var t = ts[i];
            if (t == null || t.terrainData == null) continue;
            var pos = t.transform.position; var s = t.terrainData.size;
            if (x >= pos.x && x < pos.x + s.x && z >= pos.z && z < pos.z + s.z)
                return pos.y + t.SampleHeight(new Vector3(x, 0f, z));
        }
        return 0f;
    }

    static float Y(Terrain t, Vector2 p, float offset) =>
        AlturaEnMosaico(_terrenos ?? Terrain.activeTerrains, p.x, p.y) + offset;

    static void AñadirQuad(
        List<Vector3> v, List<int> t, List<Vector2> u,
        Vector2 aL, Vector2 aR, Vector2 bL, Vector2 bR,
        float ya, float yb, float v0, float v1)
    {
        int b = v.Count;
        v.Add(new Vector3(aL.x, ya, aL.y)); v.Add(new Vector3(aR.x, ya, aR.y));
        v.Add(new Vector3(bL.x, yb, bL.y)); v.Add(new Vector3(bR.x, yb, bR.y));
        u.Add(new Vector2(0, v0)); u.Add(new Vector2(1, v0));
        u.Add(new Vector2(0, v1)); u.Add(new Vector2(1, v1));
        t.Add(b); t.Add(b + 2); t.Add(b + 1);
        t.Add(b + 1); t.Add(b + 2); t.Add(b + 3);
    }

    static void CrearMallaGO(string nombre, GameObject padre,
        List<Vector3> v, List<int> t, List<Vector2> u,
        Material mat, StaticEditorFlags flags)
    {
        if (v.Count == 0) return;
        Directory.CreateDirectory("Assets/CiudadHorneada/Meshes");
        var mesh = new Mesh { name = nombre, indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.SetUVs(0, u);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(
            $"Assets/CiudadHorneada/Meshes/{nombre.ToLower()}.asset"));

        var go = new GameObject(nombre);
        go.transform.SetParent(padre.transform, true);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        go.isStatic = true;
        GameObjectUtility.SetStaticEditorFlags(go, flags);
    }

    static Material CargarMat(string ruta, Material fallback = null)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (m != null) return m;
        if (fallback != null) { Debug.LogWarning($"[Calles v2] Material no encontrado: {ruta} → usando fallback."); return fallback; }
        // Crear material por defecto magenta para señalar ausencia
        var mg = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        mg.color = Color.magenta;
        return mg;
    }

    [MenuItem("Tools/Alsasua/Mundo/↩️ Limpiar Calles", priority = 13)]
    static void Limpiar()
    {
        var raiz = GameObject.Find(RAIZ);
        if (raiz != null) { Object.DestroyImmediate(raiz); Debug.Log("[Calles v2] Raíz eliminada."); }
    }
}
