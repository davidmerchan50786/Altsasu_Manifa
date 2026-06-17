// Assets/Scripts/Editor/ConstructorCallesAssets.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONSTRUCTOR DE CALLES — red viaria completa de Alsasua + autovía
//
//  Lee los ~2.003 segmentos de roads_unity.json (polilíneas OSM con tipo/anchura)
//  y genera la cinta de asfalto a lo largo de cada uno, con la anchura real (campo
//  `width`, o por tipo), pegada al terreno, usando tu material M_Asfalto_Carretera.
//  Fusiona TODO en una sola malla (UInt32) → 1 draw call para toda la red.
//
//  Tipos → anchura (m): motorway/trunk/primary (autovía) ancho; tertiary/residential
//  medio; service/path/pedestrian estrecho. Usa el `width` del dato si es razonable.
//
//  Verificable en EDITOR (no Play). Reversible: menú Limpiar. Raíz "Calles_Asset" static.
//  NOTA: EasyRoads3D no expone API por código en este proyecto (DLL/Resources) → se usa
//  malla procedural con el material de asfalto real, que es fiable y barato.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ConstructorCallesAssets
{
    const string JSON = "Assets/AlsasuaData/roads_unity.json";
    const string RAIZ = "Calles_Asset";
    const string MAT_ASFALTO = "Assets/Materials/Roads/M_Asfalto_Carretera.mat";
    const float  Y_OFFSET = 0.06f;   // sobre el terreno, evita z-fighting

    [System.Serializable] class Pt { public float x, z; }
    [System.Serializable] class Seg { public long id; public string type, name; public float width; public bool oneway; public Pt[] points; }
    [System.Serializable] class Wrap { public Seg[] items; }

    static float AnchoPorTipo(Seg s)
    {
        switch (s.type)
        {
            case "motorway": case "trunk": case "motorway_link": case "trunk_link": return 12f; // autovía
            case "primary":  case "primary_link":  return 9f;
            case "secondary": return 7.5f;
            case "tertiary":  return 6.5f;
            case "residential": case "living_street": case "unclassified": return 5f;
            case "service":   return 4f;
            case "pedestrian": case "footway": case "path": case "steps": return 2.5f;
            default:
                return (s.width >= 2f && s.width <= 14f) ? s.width : 5f;  // dato si es razonable
        }
    }

    [MenuItem("Tools/Alsasua/Mundo/🛣️ Construir Calles + Autovía (full, asfalto real)", priority = 10)]
    static void Construir()
    {
        string ruta = Path.GetFullPath(Path.Combine(Application.dataPath, "..", JSON));
        if (!File.Exists(ruta)) { EditorUtility.DisplayDialog("Calles", $"No existe {JSON}", "Vale"); return; }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MAT_ASFALTO);
        if (mat == null) { EditorUtility.DisplayDialog("Calles", $"No se encontró el material:\n{MAT_ASFALTO}", "Vale"); return; }

        Wrap w;
        try { w = JsonUtility.FromJson<Wrap>("{\"items\":" + File.ReadAllText(ruta) + "}"); }
        catch (System.Exception e) { EditorUtility.DisplayDialog("Calles", $"Error parseando JSON: {e.Message}", "Vale"); return; }
        if (w?.items == null || w.items.Length == 0) { EditorUtility.DisplayDialog("Calles", "JSON sin segmentos.", "Vale"); return; }

        if (!EditorUtility.DisplayDialog("Construir Calles + Autovía",
            $"Voy a generar {w.items.Length} segmentos de calle/autovía como UNA malla de asfalto " +
            "pegada al terreno (1 draw call). Raíz 'Calles_Asset' static.\n¿Continuar?", "Construir", "Cancelar"))
            return;

        var terrain = Terrain.activeTerrain;
        var verts = new List<Vector3>(1 << 16);
        var tris  = new List<int>(1 << 17);
        var uvs   = new List<Vector2>(1 << 16);
        int seg = 0, quads = 0;

        try
        {
            foreach (var s in w.items)
            {
                seg++;
                if (seg % 50 == 0 && EditorUtility.DisplayCancelableProgressBar(
                    "Calles", $"Segmento {seg}/{w.items.Length}…", seg / (float)w.items.Length)) break;
                if (s.points == null || s.points.Length < 2) continue;

                float half = AnchoPorTipo(s) * 0.5f;
                float vAcum = 0f;

                for (int i = 0; i < s.points.Length - 1; i++)
                {
                    Vector2 a = new Vector2(s.points[i].x, s.points[i].z);
                    Vector2 b = new Vector2(s.points[i + 1].x, s.points[i + 1].z);
                    Vector2 dir = b - a;
                    float len = dir.magnitude;
                    if (len < 0.05f) continue;
                    dir /= len;
                    Vector2 perp = new Vector2(-dir.y, dir.x) * half;

                    Vector2 aL = a - perp, aR = a + perp, bL = b - perp, bR = b + perp;
                    float ya = (terrain != null ? terrain.SampleHeight(new Vector3(a.x, 0, a.y)) : 0f) + Y_OFFSET;
                    float yb = (terrain != null ? terrain.SampleHeight(new Vector3(b.x, 0, b.y)) : 0f) + Y_OFFSET;

                    int baseI = verts.Count;
                    verts.Add(new Vector3(aL.x, ya, aL.y));
                    verts.Add(new Vector3(aR.x, ya, aR.y));
                    verts.Add(new Vector3(bL.x, yb, bL.y));
                    verts.Add(new Vector3(bR.x, yb, bR.y));

                    float vNext = vAcum + len / (half * 2f);   // tiling: ~cuadrado
                    uvs.Add(new Vector2(0f, vAcum)); uvs.Add(new Vector2(1f, vAcum));
                    uvs.Add(new Vector2(0f, vNext)); uvs.Add(new Vector2(1f, vNext));
                    vAcum = vNext;

                    tris.Add(baseI); tris.Add(baseI + 2); tris.Add(baseI + 1);
                    tris.Add(baseI + 1); tris.Add(baseI + 2); tris.Add(baseI + 3);
                    quads++;
                }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        if (verts.Count == 0) { EditorUtility.DisplayDialog("Calles", "No se generó geometría.", "Vale"); return; }

        var raizAnt = GameObject.Find(RAIZ);
        if (raizAnt != null) Object.DestroyImmediate(raizAnt);

        var mesh = new Mesh { name = "CallesAlsasua", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        Directory.CreateDirectory("Assets/CiudadHorneada/Meshes");
        AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath("Assets/CiudadHorneada/Meshes/calles_alsasua.asset"));

        var go = new GameObject(RAIZ);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        go.isStatic = true;
        GameObjectUtility.SetStaticEditorFlags(go,
            StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
            StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI);

        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log($"[Calles] ✅ Red viaria: {seg} segmentos → {quads} quads en 1 malla ({verts.Count} verts), 1 draw call, material asfalto real. " +
                  (terrain == null ? "⚠ Sin Terrain en editor → Y=0." : ""));
        EditorUtility.DisplayDialog("Calles",
            $"✅ {quads} tramos de calle/autovía en 1 sola malla (1 draw call), asfalto real.\n" +
            (terrain == null ? "⚠ No había Terrain → a Y=0.\n" : "") +
            "Raíz 'Calles_Asset'.", "Genial");
    }

    [MenuItem("Tools/Alsasua/Mundo/↩️ Limpiar Calles", priority = 11)]
    static void Limpiar()
    {
        var raiz = GameObject.Find(RAIZ);
        if (raiz != null) { Object.DestroyImmediate(raiz); Debug.Log("[Calles] Raíz 'Calles_Asset' eliminada."); }
    }
}
