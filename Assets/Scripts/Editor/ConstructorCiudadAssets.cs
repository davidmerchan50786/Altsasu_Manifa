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

    // Catálogo de prefabs por categoría (se cargan los que existan; los que falten se ignoran).
    static readonly string[] CASAS = {   // 1-2 plantas, footprint pequeño/medio
        "Assets/HousePack/Perfabs/House1.prefab","Assets/HousePack/Perfabs/House2.prefab",
        "Assets/HousePack/Perfabs/House3.prefab","Assets/HousePack/Perfabs/House4.prefab",
        "Assets/HousePack/Perfabs/House5.prefab","Assets/HousePack/Perfabs/House6.prefab",
        "Assets/HousePack/Perfabs/House7.prefab","Assets/HousePack/Perfabs/House8.prefab",
        "Assets/HousePack/Perfabs/House9.prefab","Assets/HousePack/Perfabs/House10.prefab",
        "Assets/VillagePack/OldHouse/OldHousePrefab.prefab",
        "Assets/Resources/Prefabs/Edificios/House_Prefab.prefab",
    };
    static readonly string[] BLOQUES = { // 3+ plantas, footprint grande → bloque urbano
        "Assets/Prefabs/FBX/Edificios/lisbon_building.prefab",
        "Assets/Prefabs/FBX/Edificios/lisbon_building_2.prefab",
        "Assets/VillagePack/BigHouse/BigHousePrefab.prefab",
    };

    [System.Serializable] class Vert { public float x, z; }
    [System.Serializable] class Edif { public long id; public string type, name; public int levels; public float height; public Vert[] vertices; }
    [System.Serializable] class Wrap { public Edif[] items; }

    [MenuItem("Tools/Alsasua/Mundo/🏙️ Construir Edificios de Asset (footprints reales)", priority = 8)]
    static void Construir()
    {
        string ruta = Path.GetFullPath(Path.Combine(Application.dataPath, "..", JSON));
        if (!File.Exists(ruta)) { EditorUtility.DisplayDialog("Edificios de Asset", $"No existe {JSON}", "Vale"); return; }

        var casas   = Cargar(CASAS);
        var bloques = Cargar(BLOQUES);
        if (casas.Count == 0 && bloques.Count == 0)
        { EditorUtility.DisplayDialog("Edificios de Asset", "No se cargó ningún prefab de edificio. Revisa las rutas en CASAS/BLOQUES.", "Vale"); return; }

        Wrap w;
        try { w = JsonUtility.FromJson<Wrap>("{\"items\":" + File.ReadAllText(ruta) + "}"); }
        catch (System.Exception e) { EditorUtility.DisplayDialog("Edificios de Asset", $"Error parseando JSON: {e.Message}", "Vale"); return; }
        if (w?.items == null || w.items.Length == 0) { EditorUtility.DisplayDialog("Edificios de Asset", "JSON sin edificios.", "Vale"); return; }

        if (!EditorUtility.DisplayDialog("Construir Edificios de Asset",
            $"Voy a colocar {w.items.Length} edificios de asset en los footprints reales " +
            $"({casas.Count} prefabs casa, {bloques.Count} bloque), escalados a parcela+altura.\n\n" +
            "Crea la raíz 'Edificios_Asset' (static). Reversible (menú Limpiar).\n¿Continuar?", "Construir", "Cancelar"))
            return;

        // Medir bounds de cada prefab una vez (para escalar a las dimensiones objetivo).
        var bounds = new Dictionary<GameObject, Vector3>();
        foreach (var p in casas)   bounds[p] = MedirBounds(p);
        foreach (var p in bloques) bounds[p] = MedirBounds(p);

        var raizAnt = GameObject.Find(RAIZ);
        if (raizAnt != null) Object.DestroyImmediate(raizAnt);
        var raiz = new GameObject(RAIZ);

        var terrain = Terrain.activeTerrain;
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

                // Elegir prefab: bloque si alto/grande, si no casa.
                bool esBloque = (altura >= 9f || (ancho * fondo) >= 220f) && bloques.Count > 0;
                var lista = esBloque ? bloques : (casas.Count > 0 ? casas : bloques);
                var prefab = lista[(int)((uint)e.id % (uint)lista.Count)];

                Vector3 size = bounds[prefab];
                if (size.x < 0.01f || size.y < 0.01f || size.z < 0.01f) size = Vector3.one;

                // Los footprints son RELATIVOS al origen (Herriko Plaza). World = + OX/OZ
                // (igual que ConstruirEdificio procedural: v + GeoDataAlsasua.OX/OZ).
                float wx = c.x + GeoDataAlsasua.OX, wz = c.y + GeoDataAlsasua.OZ;
                float y = terrain != null ? terrain.SampleHeight(new Vector3(wx, 0, wz)) : 0f;

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, raiz.transform);
                go.transform.position = new Vector3(wx, y, wz);
                go.transform.rotation = Quaternion.Euler(0f, -ang * Mathf.Rad2Deg, 0f);
                go.transform.localScale = new Vector3(ancho / size.x, altura / size.y, fondo / size.z);
                go.isStatic = true;
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
                  (terrain == null ? "⚠ Sin Terrain activo en editor → Y=0 (se ajustará en Play)." : ""));
        EditorUtility.DisplayDialog("Edificios de Asset",
            $"✅ {colocados} edificios colocados en '{RAIZ}'.\n\n" +
            (terrain == null ? "⚠ No había Terrain en el editor → quedaron a Y=0.\n\n" : "") +
            "Siguiente: desactivar la generación procedural de edificios para que estos la sustituyan en Play.", "Genial");
    }

    [MenuItem("Tools/Alsasua/Mundo/↩️ Limpiar Edificios de Asset", priority = 9)]
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
