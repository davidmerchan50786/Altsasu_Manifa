// Assets/Scripts/Editor/ConstructorCiudadAssets.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONSTRUCTOR DE CIUDAD CON ASSETS — edificios reales en los footprints OSM
//
//  Sustituye los edificios PROCEDURALES (cajas grises generadas en runtime) por
//  PREFABS de asset (HousePack, VillagePack, lisbon_building…), colocados en los
//  1.030 footprints reales de Alsasua (buildings_unity.json), escalados a las
//  dimensiones reales de la parcela (OBB) y a la altura real (campo `height`).
//
//  Por footprint:
//    · centroide + orientación (arista más larga) → caja orientada (OBB)
//    · ancho/fondo de la OBB + altura real → escala del prefab
//    · prefab elegido por tamaño/altura (casa baja vs bloque alto)
//    · snap al terreno (Terrain.activeTerrain) y rotación a la calle
//  Salida: raíz "Edificios_Asset", marcada static (batching/occlusion/GI).
//
//  Verificable en EDITOR (no Play → no se cuelga). Reversible: menú Limpiar.
//  NOTA: para que estos SUSTITUYAN a los procedurales en Play hay que desactivar
//  la generación procedural de edificios (paso de integración aparte).
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ConstructorCiudadAssets
{
    const string JSON      = "Assets/AlsasuaData/buildings_unity.json";
    const string RAIZ      = "Edificios_Asset";

    // ── Catálogo de prefabs por categoría ────────────────────────────────────
    // Los que no existan en el proyecto se ignoran automáticamente (Cargar() los filtra).

    static readonly string[] CASAS = {   // residencial 1-2 plantas
        "Assets/HousePack/Perfabs/House1.prefab",
        "Assets/HousePack/Perfabs/House2.prefab",
        "Assets/HousePack/Perfabs/House3.prefab",
        "Assets/HousePack/Perfabs/House4.prefab",
        "Assets/HousePack/Perfabs/House5.prefab",
        "Assets/HousePack/Perfabs/House6.prefab",
        "Assets/HousePack/Perfabs/House7.prefab",
        "Assets/HousePack/Perfabs/House8.prefab",
        "Assets/HousePack/Perfabs/House9.prefab",
        "Assets/HousePack/Perfabs/House10.prefab",
        "Assets/VillagePack/OldHouse/OldHousePrefab.prefab",
        "Assets/Resources/Prefabs/Edificios/House_Prefab.prefab",
        "Assets/Resources/Prefabs/Edificios/House_Green_Prefab.prefab",
        "Assets/ALP_Assets/country house01/Models/House_Prefab.prefab",
        // Polygon City Free Pack — casas y comercios pequeños
        "Assets/Wand and Circles/Polygon City Free Pack/Prefabs/Buildings/Building_A.prefab",
        "Assets/Wand and Circles/Polygon City Free Pack/Prefabs/Buildings/Building_B.prefab",
        "Assets/Wand and Circles/Polygon City Free Pack/Prefabs/Buildings/Building_C.prefab",
        "Assets/Wand and Circles/Polygon City Free Pack/Prefabs/Buildings/Building_D.prefab",
        "Assets/Wand and Circles/Polygon City Free Pack/Prefabs/Buildings/House_01.prefab",
        "Assets/Wand and Circles/Polygon City Free Pack/Prefabs/Buildings/House_02.prefab",
    };

    static readonly string[] BLOQUES = { // residencial 3+ plantas / bloque urbano
        "Assets/Prefabs/FBX/Edificios/lisbon_building.prefab",
        "Assets/Prefabs/FBX/Edificios/lisbon_building_2.prefab",
        "Assets/VillagePack/BigHouse/BigHousePrefab.prefab",
        "Assets/Wand and Circles/Polygon City Free Pack/Prefabs/Buildings/Apartment_01.prefab",
        "Assets/Wand and Circles/Polygon City Free Pack/Prefabs/Buildings/Apartment_02.prefab",
        "Assets/Wand and Circles/Polygon City Free Pack/Prefabs/Buildings/Apartment_03.prefab",
        "Assets/Wand and Circles/Polygon City Free Pack/Prefabs/Buildings/Office_01.prefab",
    };

    static readonly string[] INDUSTRIALES = { // naves industriales, hangares, almacenes
        "Assets/Wand and Circles/Polygon City Free Pack/Prefabs/Buildings/Garage_01.prefab",
        "Assets/Wand and Circles/Polygon City Free Pack/Prefabs/Buildings/Hangar_01.prefab",
        "Assets/Wand and Circles/Polygon City Free Pack/Prefabs/Buildings/Warehouse_01.prefab",
        // Fallback: bloques escalados horizontalmente si no existen los anteriores
    };

    static readonly string[] COMERCIALES = { // tiendas, oficinas
        "Assets/Wand and Circles/Polygon City Free Pack/Prefabs/Buildings/Shop_01.prefab",
        "Assets/Wand and Circles/Polygon City Free Pack/Prefabs/Buildings/Shop_02.prefab",
        "Assets/Wand and Circles/Polygon City Free Pack/Prefabs/Buildings/GasStation_01.prefab",
    };

    static readonly string[] RUINAS = {  // post-apocalíptico: estructuras derruidas
        "Assets/Prefabs/FBX/Edificios/destroyedWalls3.prefab",
        "Assets/Prefabs/FBX/Edificios/destroyedWalls_UV1.prefab",
        "Assets/Prefabs/FBX/Edificios/destroyedWalls_UV2.prefab",
        // Ruinas variadas con más piezas si existen
        "Assets/Wand and Circles/Polygon City Free Pack/Prefabs/Buildings/Building_Destroyed_01.prefab",
        "Assets/Wand and Circles/Polygon City Free Pack/Prefabs/Buildings/Building_Destroyed_02.prefab",
    };

    const float PORCENTAJE_RUINAS     = 0.25f; // ~25% post-apocalíptico
    const float PORCENTAJE_INDUSTRIAL = 0.12f; // ~12% industrial (polígonos)
    const float PORCENTAJE_COMERCIAL  = 0.10f; // ~10% comercial (centro)

    [System.Serializable] class Vert { public float x, z; }
    [System.Serializable] class Edif { public long id; public string type, name; public int levels; public float height; public Vert[] vertices; }
    [System.Serializable] class Wrap { public Edif[] items; }

    // Altura tile-aware del mosaico V2: SampleHeight sobre el tile que CONTIENE (x,z),
    // no sobre Terrain.activeTerrain (con 48 tiles devuelve uno arbitrario / 0 fuera de su tile).
    static float AlturaEnMosaico(Terrain[] ts, float x, float z)
    {
        for (int i = 0; i < ts.Length; i++)
        {
            var t = ts[i];
            if (t == null || t.terrainData == null) continue;
            var p = t.transform.position; var s = t.terrainData.size;
            if (x >= p.x && x < p.x + s.x && z >= p.z && z < p.z + s.z)
                return p.y + t.SampleHeight(new Vector3(x, 0f, z));
        }
        return 0f; // fuera del mosaico
    }

    [MenuItem("Tools/Alsasua/Mundo/🏙️ Construir Edificios de Asset (footprints reales)", priority = 10)]
    public static void Construir()
    {
        string ruta = Path.GetFullPath(Path.Combine(Application.dataPath, "..", JSON));
        if (!File.Exists(ruta)) { EditorUtility.DisplayDialog("Edificios de Asset", $"No existe {JSON}", "Vale"); return; }

        var casas        = Cargar(CASAS);
        var bloques      = Cargar(BLOQUES);
        var industriales = Cargar(INDUSTRIALES);
        var comerciales  = Cargar(COMERCIALES);
        var ruinas       = Cargar(RUINAS);
        // Fallback: si categorías especializadas están vacías, usar bloques como comodín
        if (industriales.Count == 0) industriales = bloques;
        if (comerciales.Count  == 0) comerciales  = casas.Count > 0 ? casas : bloques;
        if (casas.Count == 0 && bloques.Count == 0 && ruinas.Count == 0)
        { EditorUtility.DisplayDialog("Edificios de Asset", "No se cargó ningún prefab de edificio. Revisa las rutas en CASAS/BLOQUES/RUINAS.", "Vale"); return; }

        Wrap w;
        try { w = JsonUtility.FromJson<Wrap>("{\"items\":" + File.ReadAllText(ruta) + "}"); }
        catch (System.Exception e) { EditorUtility.DisplayDialog("Edificios de Asset", $"Error parseando JSON: {e.Message}", "Vale"); return; }
        if (w?.items == null || w.items.Length == 0) { EditorUtility.DisplayDialog("Edificios de Asset", "JSON sin edificios.", "Vale"); return; }

        if (!EditorUtility.DisplayDialog("Construir Edificios de Asset",
            $"Voy a colocar {w.items.Length} edificios en los footprints reales:\n" +
            $"  · {casas.Count} modelos de casa\n" +
            $"  · {bloques.Count} modelos de bloque\n" +
            $"  · {industriales.Count} modelos industriales\n" +
            $"  · {comerciales.Count} modelos comerciales\n" +
            $"  · {ruinas.Count} modelos de ruina (~{PORCENTAJE_RUINAS*100:F0}% post-apoc)\n\n" +
            "Clasificación automática por tipo OSM + tamaño de parcela.\n" +
            "Raíz 'Edificios_Asset' static. Reversible (menú Limpiar).\n¿Continuar?", "Construir", "Cancelar"))
            return;

        // Medir bounds de cada prefab una vez (para escalar a las dimensiones objetivo).
        var bounds = new Dictionary<GameObject, Vector3>();
        foreach (var p in casas)        bounds[p] = MedirBounds(p);
        foreach (var p in bloques)      bounds[p] = MedirBounds(p);
        foreach (var p in industriales) { if (!bounds.ContainsKey(p)) bounds[p] = MedirBounds(p); }
        foreach (var p in comerciales)  { if (!bounds.ContainsKey(p)) bounds[p] = MedirBounds(p); }
        foreach (var p in ruinas)       bounds[p] = MedirBounds(p);

        var raizAnt = GameObject.Find(RAIZ);
        if (raizAnt != null) Object.DestroyImmediate(raizAnt);
        var raiz = new GameObject(RAIZ);
        var matsInstancia = new HashSet<Material>();   // GPU instancing en materiales repetidos (AAA)

        var terrains = Terrain.activeTerrains;   // mosaico V2: todos los tiles colocados
        int colocados = 0, idx = 0;
        try
        {
            foreach (var e in w.items)
            {
                idx++;
                if (idx % 20 == 0 && EditorUtility.DisplayCancelableProgressBar(
                    "Edificios de Asset", $"Colocando {idx}/{w.items.Length}…", idx / (float)w.items.Length)) break;
                if (e.vertices == null || e.vertices.Length < 3) continue;

                // OBB: centroide, orientación (arista más larga), ancho/fondo.
                Vector2 c = Vector2.zero;
                for (int i = 0; i < e.vertices.Length; i++) c += new Vector2(e.vertices[i].x, e.vertices[i].z);
                c /= e.vertices.Length;

                float mejorLen = 0f, ang = 0f;
                for (int i = 0; i < e.vertices.Length - 1; i++)
                {
                    var a = new Vector2(e.vertices[i].x, e.vertices[i].z);
                    var b = new Vector2(e.vertices[i + 1].x, e.vertices[i + 1].z);
                    float len = (b - a).sqrMagnitude;
                    if (len > mejorLen) { mejorLen = len; ang = Mathf.Atan2(b.y - a.y, b.x - a.x); }
                }
                float cos = Mathf.Cos(-ang), sin = Mathf.Sin(-ang);
                float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
                for (int i = 0; i < e.vertices.Length; i++)
                {
                    float dx = e.vertices[i].x - c.x, dz = e.vertices[i].z - c.y;
                    float u = dx * cos - dz * sin, v = dx * sin + dz * cos;
                    if (u < minU) minU = u; if (u > maxU) maxU = u;
                    if (v < minV) minV = v; if (v > maxV) maxV = v;
                }
                float ancho = Mathf.Max(3f, maxU - minU);
                float fondo = Mathf.Max(3f, maxV - minV);
                float altura = Mathf.Max(3f, e.height > 0 ? e.height : e.levels * 3.1f);

                // ── Clasificación por tipo OSM + geometría ─────────────────
                // Prioridad: ruina → industrial → comercial → bloque → casa
                uint hash = (uint)e.id;
                bool esRuina     = ruinas.Count > 0       && (hash % 100u) < (uint)(PORCENTAJE_RUINAS     * 100f);
                bool esIndustrial= !esRuina && industriales.Count > 0
                                   && ((e.type == "industrial" || e.type == "warehouse" || e.type == "garage"
                                        || (ancho * fondo) >= 400f)   // nave grande
                                       || (hash % 100u) < (uint)(PORCENTAJE_INDUSTRIAL * 100f));
                bool esComercial = !esRuina && !esIndustrial && comerciales.Count > 0
                                   && (e.type == "commercial" || e.type == "retail" || e.type == "office"
                                       || (hash % 100u) < (uint)((PORCENTAJE_INDUSTRIAL + PORCENTAJE_COMERCIAL) * 100f));
                bool esBloque    = !esRuina && !esIndustrial && !esComercial
                                   && ((altura >= 9f || (ancho * fondo) >= 220f) && bloques.Count > 0);

                List<GameObject> lista =
                    esRuina      ? ruinas :
                    esIndustrial ? industriales :
                    esComercial  ? comerciales :
                    esBloque     ? bloques :
                    casas.Count > 0 ? casas : bloques;

                if (lista == null || lista.Count == 0)
                    lista = casas.Count > 0 ? casas : (bloques.Count > 0 ? bloques : ruinas);
                var prefab = lista[(int)(hash % (uint)lista.Count)];

                Vector3 size = bounds[prefab];
                if (size.x < 0.01f || size.y < 0.01f || size.z < 0.01f) size = Vector3.one;

                // Los footprints son RELATIVOS al origen (Herriko Plaza). World = + OX/OZ
                // (igual que ConstruirEdificio procedural: v + GeoDataAlsasua.OX/OZ).
                float wx = c.x + GeoDataAlsasua.OX, wz = c.y + GeoDataAlsasua.OZ;
                float y = AlturaEnMosaico(terrains, wx, wz);

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, raiz.transform);
                go.transform.position = new Vector3(wx, y, wz);
                go.transform.rotation = Quaternion.Euler(0f, -ang * Mathf.Rad2Deg, 0f);
                go.transform.localScale = new Vector3(ancho / size.x, altura / size.y, fondo / size.z);
                go.isStatic = true;
                // GPU instancing en los materiales (muchos prefabs comparten material → 1 draw call).
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                        if (m != null && !m.enableInstancing && matsInstancia.Add(m)) { m.enableInstancing = true; EditorUtility.SetDirty(m); }
                colocados++;
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        // Marcar static recursivo para batching/occlusion/GI.
        var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI;
        foreach (var t in raiz.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log($"[CiudadAssets] ✅ {colocados} edificios de asset colocados en footprints reales (raíz '{RAIZ}', static). " +
                  (terrains.Length == 0 ? "⚠ Sin Terrain en escena → Y=0. Construye el Mosaico V2 primero." : ""));
        EditorUtility.DisplayDialog("Edificios de Asset",
            $"✅ {colocados} edificios colocados en '{RAIZ}'.\n\n" +
            (terrains.Length == 0 ? "⚠ No había Terrain en el editor → quedaron a Y=0.\n\n" : "") +
            "Siguiente: desactivar la generación procedural de edificios para que estos la sustituyan en Play.", "Genial");
    }

    [MenuItem("Tools/Alsasua/Mundo/↩️ Limpiar Edificios de Asset", priority = 11)]
    static void Limpiar()
    {
        var raiz = GameObject.Find(RAIZ);
        if (raiz != null) { Object.DestroyImmediate(raiz); Debug.Log("[CiudadAssets] Raíz 'Edificios_Asset' eliminada."); }
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

    static Vector3 MedirBounds(GameObject prefab)
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var rends = inst.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) { Object.DestroyImmediate(inst); return Vector3.one; }
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        Object.DestroyImmediate(inst);
        return b.size == Vector3.zero ? Vector3.one : b.size;
    }
}
