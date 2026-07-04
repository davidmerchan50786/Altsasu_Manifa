// Assets/Scripts/Editor/ConstructorZonasEspeciales.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONSTRUCTOR ZONAS ESPECIALES — zonas verdes, plazas, cementerio, huertas
//
//  Lee greenspaces_unity.json (273 zonas) + plazas_unity.json (5 plazas) y
//  triangula cada polígono para generar una malla con el color correcto:
//
//   park / garden / pedestrian  → verde parque / piedra paving
//   orchard / allotments        → verde huerta oscuro
//   cemetery                    → gris claro (cementerio)
//   forest / wood / scrub       → verde bosque oscuro
//   grass / grassland           → verde claro
//   farmland                    → amarillo tierra
//   pitch                       → verde deportivo intenso
//   recreation_ground           → verde medio
//
//  Cada tipo tiene su propio sub-objeto para poder asignarle material distinto.
//  Altura corregida con V3 bilineal exacto.
//  Menú: Tools/Alsasua/Mundo/🌿 Construir Zonas Especiales (parques, cementerio…)
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ConstructorZonasEspeciales
{
    const string JSON_GREEN  = "Assets/AlsasuaData/greenspaces_unity.json";
    const string JSON_PLAZAS = "Assets/AlsasuaData/plazas_unity.json";
    const string RAIZ        = "ZonasEspeciales_Asset";
    const float  Y_OFFSET    = 0.04f; // ligeramente por encima del terreno

    // ── Colores por tipo (HDRP BaseColor) ────────────────────────────────
    static readonly Dictionary<string, Color> COLORES = new()
    {
        ["park"]              = new Color(0.35f, 0.62f, 0.28f),
        ["garden"]            = new Color(0.30f, 0.58f, 0.25f),
        ["pedestrian"]        = new Color(0.75f, 0.72f, 0.65f), // adoquín
        ["orchard"]           = new Color(0.22f, 0.48f, 0.18f),
        ["allotments"]        = new Color(0.25f, 0.50f, 0.15f),
        ["cemetery"]          = new Color(0.70f, 0.72f, 0.68f),
        ["forest"]            = new Color(0.12f, 0.38f, 0.10f),
        ["wood"]              = new Color(0.10f, 0.33f, 0.08f),
        ["scrub"]             = new Color(0.42f, 0.55f, 0.20f),
        ["grass"]             = new Color(0.45f, 0.70f, 0.22f),
        ["grassland"]         = new Color(0.48f, 0.72f, 0.25f),
        ["farmland"]          = new Color(0.68f, 0.65f, 0.35f),
        ["pitch"]             = new Color(0.15f, 0.65f, 0.15f),
        ["recreation_ground"] = new Color(0.30f, 0.60f, 0.20f),
    };

    // ── Estructuras ───────────────────────────────────────────────────────
    [System.Serializable]
    class Zona { public long osm_id; public float x, z; public string type, name, surface; public float[] poly; }

    // Heightmap V3
    static MuestreadorHeightmapV3 _v3; static bool _v3Init;
    static MuestreadorHeightmapV3 V3
    {
        get { if (_v3Init) return _v3; _v3Init = true; var m = new MuestreadorHeightmapV3(); if (m.Cargar()) _v3 = m; return _v3; }
    }
    static float Altura(float x, float z) { if (V3 != null && V3.EnRango(x, z)) return V3.AlturaMundo(x, z) + Y_OFFSET; return Y_OFFSET; }

    [MenuItem("Tools/Alsasua/Mundo/🌿 Construir Zonas Especiales (parques, cementerio…)", priority = 16)]
    static void Construir()
    {
        // Cargar zonas verdes
        string rutaG = Path.GetFullPath(Path.Combine(Application.dataPath, "..", JSON_GREEN));
        string rutaP = Path.GetFullPath(Path.Combine(Application.dataPath, "..", JSON_PLAZAS));
        if (!File.Exists(rutaG)) { EditorUtility.DisplayDialog("Zonas", $"No existe {JSON_GREEN}", "Vale"); return; }

        var zonas = new List<Zona>();
        try
        {
            // Greenspaces (array raíz)
            string jsonG = File.ReadAllText(rutaG);
            // El archivo es un array JSON → envolver para JsonUtility
            var wg = JsonUtility.FromJson<Wrap>("{\"items\":" + jsonG + "}");
            if (wg?.items != null) zonas.AddRange(wg.items);
        }
        catch (System.Exception e) { Debug.LogWarning($"[ZonasEspeciales] greenspaces error: {e.Message}"); }

        try
        {
            if (File.Exists(rutaP))
            {
                string jsonP = File.ReadAllText(rutaP);
                var wp = JsonUtility.FromJson<Wrap>("{\"items\":" + jsonP + "}");
                if (wp?.items != null) zonas.AddRange(wp.items);
            }
        }
        catch (System.Exception e) { Debug.LogWarning($"[ZonasEspeciales] plazas error: {e.Message}"); }

        if (zonas.Count == 0) { EditorUtility.DisplayDialog("Zonas", "Sin zonas encontradas.", "Vale"); return; }

        if (!EditorUtility.DisplayDialog("Zonas Especiales",
            $"{zonas.Count} zonas (parques, huertas, cementerio, bosques…).\n" +
            "¿Generar mallas con color por tipo?", "Construir", "Cancelar"))
            return;

        // Agrupar por tipo para un sub-objeto + material por tipo
        var porcTipo = new Dictionary<string, (List<Vector3> v, List<int> t, List<Vector2> u)>();

        int n = 0;
        try
        {
            foreach (var z in zonas)
            {
                n++;
                if (n % 30 == 0 && EditorUtility.DisplayCancelableProgressBar(
                    "Zonas Especiales", $"Zona {n}/{zonas.Count}…", n / (float)zonas.Count)) break;
                if (z.poly == null || z.poly.Length < 6) continue;

                string tipo = string.IsNullOrEmpty(z.type) ? "grass" : z.type;
                if (!porcTipo.ContainsKey(tipo))
                    porcTipo[tipo] = (new List<Vector3>(256), new List<int>(512), new List<Vector2>(256));

                var (vL, tL, uL) = porcTipo[tipo];
                TriangularPoligono(z.poly, vL, tL, uL);
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        // Crear raíz en escena
        var raizAnt = GameObject.Find(RAIZ);
        if (raizAnt != null) Object.DestroyImmediate(raizAnt);
        var raiz = new GameObject(RAIZ);

        var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI;

        Directory.CreateDirectory("Assets/CiudadHorneada/Meshes");

        foreach (var kv in porcTipo)
        {
            var (vL, tL, uL) = kv.Value;
            if (vL.Count == 0) continue;
            Color col = COLORES.TryGetValue(kv.Key, out var c) ? c : new Color(0.4f, 0.6f, 0.3f);
            var mat = CrearMaterial(kv.Key, col);
            CrearGO(kv.Key, raiz, vL, tL, uL, mat, flags);
        }

        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        EditorUtility.DisplayDialog("Zonas Especiales ✅",
            $"{n} zonas procesadas en {porcTipo.Count} tipos:\n" +
            string.Join(", ", porcTipo.Keys) + "\n\n" +
            "Raíz 'ZonasEspeciales_Asset' static.", "Genial");
    }

    // ── Triangulación de polígono (fan desde centroide) ───────────────────
    // poly format: [x1, z1, x2, z2, ...] (2 floats por vértice)
    static void TriangularPoligono(float[] poly, List<Vector3> v, List<int> t, List<Vector2> u)
    {
        int nv = poly.Length / 2;
        if (nv < 3) return;

        // Centroide
        float cx = 0, cz = 0;
        for (int i = 0; i < nv; i++) { cx += poly[i * 2]; cz += poly[i * 2 + 1]; }
        cx /= nv; cz /= nv;

        int base0 = v.Count;
        v.Add(new Vector3(cx, Altura(cx, cz), cz));
        u.Add(new Vector2(0.5f, 0.5f));

        // Vértices del contorno
        float invN = 1f / nv;
        for (int i = 0; i < nv; i++)
        {
            float px = poly[i * 2], pz = poly[i * 2 + 1];
            v.Add(new Vector3(px, Altura(px, pz), pz));
            u.Add(new Vector2(i * invN, 0f));
        }

        // Triángulos en abanico desde centroide
        for (int i = 0; i < nv; i++)
        {
            t.Add(base0);
            t.Add(base0 + 1 + i);
            t.Add(base0 + 1 + (i + 1) % nv);
        }
    }

    static Material CrearMaterial(string tipo, Color color)
    {
        string path = $"Assets/CiudadHorneada/Meshes/M_Zona_{tipo}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"))
            { name = $"M_Zona_{tipo}", color = color };
        m.enableInstancing = true;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void CrearGO(string tipo, GameObject padre, List<Vector3> v, List<int> t, List<Vector2> u,
        Material mat, StaticEditorFlags flags)
    {
        var mesh = new Mesh { name = $"Zona_{tipo}", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.SetUVs(0, u);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(
            $"Assets/CiudadHorneada/Meshes/zona_{tipo}.asset"));
        var go = new GameObject($"Zona_{tipo}");
        go.transform.SetParent(padre.transform, true);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        go.isStatic = true;
        GameObjectUtility.SetStaticEditorFlags(go, flags);
    }

    [System.Serializable] class Wrap { public Zona[] items; }

    [MenuItem("Tools/Alsasua/Mundo/↩️ Limpiar Zonas Especiales", priority = 17)]
    static void Limpiar() { var r = GameObject.Find(RAIZ); if (r != null) Object.DestroyImmediate(r); }
}
#endif
