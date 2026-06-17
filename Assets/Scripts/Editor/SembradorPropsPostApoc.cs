// Assets/Scripts/Editor/SembradorPropsPostApoc.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SEMBRADOR DE PROPS POST-APOCALÍPTICOS — coches abandonados + barreras
//
//  Esparce coches abandonados (Urban American Assets) y barreras de hormigón/metal
//  (Abandoned World) a lo largo de las calles (roads_unity.json), para vender el
//  ambiente post-apocalíptico.
//
//  TÉCNICAS AAA (estilo GTA VI) "carga solo lo cercano":
//    · Raíz "Props_PostApoc" → la gestiona StreamerMundoEstatico por DISTANCIA
//      (cerca = render; lejos = forceRenderingOff = 0 draw call). No está en su denylist.
//    · static (batching/occlusion) + GPU INSTANCING en los materiales → 600 coches
//      repetidos cuestan casi como 1 en draw calls.
//    · El GobernadorRender encoge el radio bajo presión de GPU → menos props activos.
//  Resultado: puedes sembrar cientos y solo se dibujan los del entorno del jugador.
//
//  Verificable en EDITOR (no Play). Reversible: menú Limpiar. Snap al terreno si existe.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class SembradorPropsPostApoc
{
    const string JSON = "Assets/AlsasuaData/roads_unity.json";
    const string RAIZ = "Props_PostApoc";
    const float  PROB_POR_PUNTO = 0.10f;   // prob. de colocar prop en cada vértice de calle
    const int    MAX_PROPS      = 700;     // tope (se streamean, pero acotamos el peso de escena)
    const float  Y_OFFSET       = 0.05f;

    static readonly string[] VEHICULOS = {
        "Assets/Urban American Assets/Props/Vehicles/Car1.prefab",
        "Assets/Urban American Assets/Props/Vehicles/Car2.prefab",
        "Assets/Urban American Assets/Props/Vehicles/Suv.prefab",
        "Assets/Urban American Assets/Props/Vehicles/Van.prefab",
        "Assets/Urban American Assets/Props/Vehicles/Wagon.prefab",
        "Assets/Urban American Assets/Props/Vehicles/Taxi.prefab",
        "Assets/Urban American Assets/Props/Vehicles/City Bus.prefab",
        "Assets/Urban American Assets/Props/Vehicles/Big Rig.prefab",
    };
    static readonly string[] BARRERAS = {
        "Assets/Abandoned World/Metal and Concrete Barrier/Meshes/Concrete_Barrier_1.fbx",
        "Assets/Abandoned World/Metal and Concrete Barrier/Meshes/Concrete_Barrier_2.fbx",
        "Assets/Abandoned World/Metal and Concrete Barrier/Meshes/Concrete_Barrier_3.fbx",
        "Assets/Abandoned World/Metal and Concrete Barrier/Meshes/Metal_Barrier_1.fbx",
        "Assets/Abandoned World/Metal and Concrete Barrier/Meshes/Metal_Barrier_2.fbx",
    };

    [System.Serializable] class Pt { public float x, z; }
    [System.Serializable] class Seg { public long id; public float width; public Pt[] points; }
    [System.Serializable] class Wrap { public Seg[] items; }

    [MenuItem("Tools/Alsasua/Mundo/🚗 Sembrar Coches Abandonados + Barreras (post-apoc)", priority = 12)]
    static void Sembrar()
    {
        string ruta = Path.GetFullPath(Path.Combine(Application.dataPath, "..", JSON));
        if (!File.Exists(ruta)) { EditorUtility.DisplayDialog("Props post-apoc", $"No existe {JSON}", "Vale"); return; }

        var coches   = Cargar(VEHICULOS);
        var barreras = Cargar(BARRERAS);
        if (coches.Count == 0 && barreras.Count == 0)
        { EditorUtility.DisplayDialog("Props post-apoc", "No se cargó ningún prefab/FBX de coche o barrera. Revisa las rutas.", "Vale"); return; }

        Wrap w;
        try { w = JsonUtility.FromJson<Wrap>("{\"items\":" + File.ReadAllText(ruta) + "}"); }
        catch (System.Exception e) { EditorUtility.DisplayDialog("Props post-apoc", $"Error JSON: {e.Message}", "Vale"); return; }
        if (w?.items == null) { EditorUtility.DisplayDialog("Props post-apoc", "JSON vacío.", "Vale"); return; }

        if (!EditorUtility.DisplayDialog("Sembrar props post-apoc",
            $"Voy a esparcir coches abandonados ({coches.Count} tipos) y barreras ({barreras.Count}) por las calles " +
            $"(máx {MAX_PROPS}). Raíz 'Props_PostApoc' (static + instancing) → se streamea por distancia.\n¿Continuar?",
            "Sembrar", "Cancelar")) return;

        var raizAnt = GameObject.Find(RAIZ);
        if (raizAnt != null) Object.DestroyImmediate(raizAnt);
        var raiz = new GameObject(RAIZ);

        var terrain = Terrain.activeTerrain;
        var matsInstancia = new HashSet<Material>();
        int n = 0, seg = 0;
        var rnd = new System.Random(12345);   // determinista entre ejecuciones

        try
        {
            foreach (var s in w.items)
            {
                seg++;
                if (n >= MAX_PROPS) break;
                if (s.points == null || s.points.Length < 2) continue;
                if (seg % 50 == 0 && EditorUtility.DisplayCancelableProgressBar(
                    "Props post-apoc", $"Sembrando… {n} props", seg / (float)w.items.Length)) break;

                float half = Mathf.Max(2f, s.width) * 0.5f;
                for (int i = 0; i < s.points.Length; i++)
                {
                    if (n >= MAX_PROPS) break;
                    if (rnd.NextDouble() > PROB_POR_PUNTO) continue;

                    // World (RELATIVO → +OX/OZ) + desplazamiento lateral aleatorio dentro de la calzada.
                    float wx = s.points[i].x + GeoDataAlsasua.OX;
                    float wz = s.points[i].z + GeoDataAlsasua.OZ;
                    float lat = (float)(rnd.NextDouble() * 2 - 1) * half * 0.7f;
                    // perpendicular aproximada usando el siguiente punto
                    int j = Mathf.Min(i + 1, s.points.Length - 1);
                    Vector2 dir = new Vector2(s.points[j].x - s.points[i].x, s.points[j].z - s.points[i].z);
                    if (dir.sqrMagnitude < 0.01f) dir = Vector2.right;
                    dir.Normalize();
                    Vector2 perp = new Vector2(-dir.y, dir.x) * lat;
                    wx += perp.x; wz += perp.y;

                    bool barrera = barreras.Count > 0 && (coches.Count == 0 || rnd.NextDouble() < 0.35);
                    var lista = barrera ? barreras : coches;
                    var prefab = lista[rnd.Next(lista.Count)];

                    float y = (terrain != null ? terrain.SampleHeight(new Vector3(wx, 0, wz)) : 0f) + Y_OFFSET;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, raiz.transform);
                    go.transform.position = new Vector3(wx, y, wz);
                    // Rotación: coches mirando la calle + ligera inclinación de "abandonado".
                    float yaw = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + (float)(rnd.NextDouble() * 40 - 20);
                    float tilt = barrera ? 0f : (float)(rnd.NextDouble() * 10 - 5);
                    go.transform.rotation = Quaternion.Euler(tilt, yaw, tilt * 0.5f);
                    go.isStatic = true;

                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                        foreach (var m in r.sharedMaterials)
                            if (m != null && !m.enableInstancing && matsInstancia.Add(m)) { m.enableInstancing = true; EditorUtility.SetDirty(m); }
                    n++;
                }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI;
        foreach (var t in raiz.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);

        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log($"[PropsPostApoc] ✅ {n} props sembrados (coches abandonados + barreras) en '{RAIZ}', static + instancing " +
                  $"en {matsInstancia.Count} materiales. Se streamean por distancia (StreamerMundoEstatico). " +
                  (terrain == null ? "⚠ Sin Terrain → Y=0." : ""));
        EditorUtility.DisplayDialog("Props post-apoc",
            $"✅ {n} props (coches/barreras) sembrados en '{RAIZ}'.\n" +
            "Static + GPU instancing + streaming por distancia (solo se dibujan los cercanos).", "Genial");
    }

    [MenuItem("Tools/Alsasua/Mundo/↩️ Limpiar Props post-apoc", priority = 13)]
    static void Limpiar()
    {
        var raiz = GameObject.Find(RAIZ);
        if (raiz != null) { Object.DestroyImmediate(raiz); Debug.Log("[PropsPostApoc] Raíz 'Props_PostApoc' eliminada."); }
    }

    static List<GameObject> Cargar(string[] rutas)
    {
        var l = new List<GameObject>(rutas.Length);
        foreach (var r in rutas)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(r);
            if (p != null) l.Add(p);
        }
        return l;
    }
}
