// Assets/Scripts/Editor/ConstructorViasPeatonales.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONSTRUCTOR VÍAS PEATONALES — footways, paths, steps, tracks de Alsasua
//
//  Lee footways_unity.json (206 segmentos) y genera cintas de ancho real:
//    pedestrian  → 2.5 m (calles peatonales, ej. Nafarroa Kalea)
//    footway     → 1.5 m (aceras separadas)
//    path        → 1.0 m (senderos)
//    steps       → 1.5 m (escaleras — misma cinta, diferente textura)
//    track       → 2.0 m (pistas de tierra, acceso a huertas)
//
//  Material: M_Arcen_Hormigon para peatonal/footway/steps, M_Tierra para path/track.
//  Altura corregida con V3.
//  Menú: Tools/Alsasua/Mundo/🚶 Construir Vías Peatonales
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ConstructorViasPeatonales
{
    const string JSON       = "Assets/AlsasuaData/footways_unity.json";
    const string RAIZ       = "ViasPeatonales_Asset";
    const string MAT_PEAT   = "Assets/Materials/Roads/M_Arcen_Hormigon.mat";
    const string MAT_TIERRA = "Assets/Materials/Roads/M_Asfalto_Carretera.mat"; // fallback
    const float  Y_OFFSET   = 0.07f;

    [System.Serializable] class Pt  { public float x, z; }
    [System.Serializable] class Seg { public long osm_id; public string type, surface; public float width; public float[] pts; }
    [System.Serializable] class Wrap { public Seg[] items; }

    static MuestreadorHeightmapV3 _v3; static bool _v3Init;
    static MuestreadorHeightmapV3 V3
    { get { if (_v3Init) return _v3; _v3Init = true; var m = new MuestreadorHeightmapV3(); if (m.Cargar()) _v3 = m; return _v3; } }

    static float Y(float x, float z)
    {
        if (V3 != null && V3.EnRango(x, z)) return V3.AlturaMundo(x, z) + Y_OFFSET;
        foreach (var t in Terrain.activeTerrains)
        {
            if (t == null) continue;
            var p = t.transform.position; var s = t.terrainData.size;
            if (x >= p.x && x < p.x + s.x && z >= p.z && z < p.z + s.z)
                return p.y + t.SampleHeight(new Vector3(x, 0, z)) + Y_OFFSET;
        }
        return Y_OFFSET;
    }

    static float AnchoPorTipo(Seg s)
    {
        if (s.width >= 0.5f && s.width <= 8f) return s.width;
        return s.type switch
        {
            "pedestrian" => 2.5f,
            "footway"    => 1.5f,
            "steps"      => 1.5f,
            "path"       => 1.0f,
            "track"      => 2.0f,
            _            => 1.5f,
        };
    }

    static bool EsTierra(Seg s) => s.type is "path" or "track";

    [MenuItem("Tools/Alsasua/Mundo/🚶 Construir Vías Peatonales", priority = 18)]
    static void Construir()
    {
        string ruta = Path.GetFullPath(Path.Combine(Application.dataPath, "..", JSON));
        if (!File.Exists(ruta)) { EditorUtility.DisplayDialog("Peatonal", $"No existe {JSON}", "Vale"); return; }

        Wrap w;
        try { w = JsonUtility.FromJson<Wrap>("{\"items\":" + File.ReadAllText(ruta) + "}"); }
        catch (System.Exception e) { EditorUtility.DisplayDialog("Peatonal", $"JSON: {e.Message}", "Vale"); return; }
        if (w?.items == null) { EditorUtility.DisplayDialog("Peatonal", "Sin datos.", "Vale"); return; }

        var matPeat   = AssetDatabase.LoadAssetAtPath<Material>(MAT_PEAT)
                     ?? AssetDatabase.LoadAssetAtPath<Material>(MAT_TIERRA)
                     ?? new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard")) { color = new Color(0.72f, 0.70f, 0.65f) };
        var matTierra = AssetDatabase.LoadAssetAtPath<Material>(MAT_TIERRA) ?? matPeat;

        var (vP, tP, uP) = Buf(); // pavimento
        var (vT, tT, uT) = Buf(); // tierra

        int ns = 0;
        try
        {
            foreach (var s in w.items)
            {
                ns++;
                if (ns % 40 == 0 && EditorUtility.DisplayCancelableProgressBar(
                    "Peatonal", $"{ns}/{w.items.Length}…", ns / (float)w.items.Length)) break;

                // pts puede ser [x,y,z,x,y,z,...] (triplets) o [x,z,x,z,...] (pairs)
                // Detectar: si w.items[0].pts.Length % 2 == 0 → pairs, % 3 == 0 → triplets
                int step = (s.pts.Length % 3 == 0 && s.pts.Length % 2 != 0) ? 3 : 2;
                if (s.pts == null || s.pts.Length < step * 2) continue;

                float half = AnchoPorTipo(s) * 0.5f;
                var (vL, tL, uL) = EsTierra(s) ? (vT, tT, uT) : (vP, tP, uP);
                float vAcum = 0f;
                int puntos = s.pts.Length / step;

                for (int i = 0; i < puntos - 1; i++)
                {
                    float ax = s.pts[i * step], az = step == 3 ? s.pts[i * step + 2] : s.pts[i * step + 1];
                    int j = i + 1;
                    float bx = s.pts[j * step], bz = step == 3 ? s.pts[j * step + 2] : s.pts[j * step + 1];

                    Vector2 a2 = new(ax, az), b2 = new(bx, bz);
                    Vector2 dir = b2 - a2; float len = dir.magnitude;
                    if (len < 0.05f) continue;
                    dir /= len;
                    Vector2 perp = new Vector2(-dir.y, dir.x) * half;

                    float ya = Y(ax, az), yb = Y(bx, bz);
                    float vN = vAcum + len / Mathf.Max(0.5f, half * 2f);
                    AñadirQuad(vL, tL, uL, a2 - perp, a2 + perp, b2 - perp, b2 + perp, ya, yb, vAcum, vN);
                    vAcum = vN;
                }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        var raizAnt = GameObject.Find(RAIZ);
        if (raizAnt != null) Object.DestroyImmediate(raizAnt);
        var raiz = new GameObject(RAIZ);

        var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI;

        Directory.CreateDirectory("Assets/CiudadHorneada/Meshes");
        CrearGO("Peatonal_Pavimento", raiz, vP, tP, uP, matPeat,   flags);
        CrearGO("Peatonal_Tierra",    raiz, vT, tT, uT, matTierra, flags);

        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        EditorUtility.DisplayDialog("Vías Peatonales ✅",
            $"{ns} segmentos peatonales:\n" +
            $"  • {vP.Count / 4} tramos de pavimento\n" +
            $"  • {vT.Count / 4} tramos de tierra/sendero\n" +
            "Raíz 'ViasPeatonales_Asset' static.", "Genial");
    }

    static (List<Vector3>, List<int>, List<Vector2>) Buf() =>
        (new List<Vector3>(1 << 13), new List<int>(1 << 14), new List<Vector2>(1 << 13));

    static void AñadirQuad(List<Vector3> v, List<int> t, List<Vector2> u,
        Vector2 aL, Vector2 aR, Vector2 bL, Vector2 bR, float ya, float yb, float v0, float v1)
    {
        int b = v.Count;
        v.Add(new Vector3(aL.x, ya, aL.y)); v.Add(new Vector3(aR.x, ya, aR.y));
        v.Add(new Vector3(bL.x, yb, bL.y)); v.Add(new Vector3(bR.x, yb, bR.y));
        u.Add(new Vector2(0, v0)); u.Add(new Vector2(1, v0));
        u.Add(new Vector2(0, v1)); u.Add(new Vector2(1, v1));
        t.Add(b); t.Add(b + 2); t.Add(b + 1);
        t.Add(b + 1); t.Add(b + 2); t.Add(b + 3);
    }

    static void CrearGO(string nombre, GameObject padre, List<Vector3> v, List<int> t, List<Vector2> u,
        Material mat, StaticEditorFlags flags)
    {
        if (v.Count == 0) return;
        var mesh = new Mesh { name = nombre, indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.SetUVs(0, u);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(
            $"Assets/CiudadHorneada/Meshes/peat_{nombre.ToLower()}.asset"));
        var go = new GameObject(nombre);
        go.transform.SetParent(padre.transform, true);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        go.isStatic = true;
        GameObjectUtility.SetStaticEditorFlags(go, flags);
    }

    [MenuItem("Tools/Alsasua/Mundo/↩️ Limpiar Vías Peatonales", priority = 19)]
    static void Limpiar() { var r = GameObject.Find(RAIZ); if (r != null) Object.DestroyImmediate(r); }
}
#endif
