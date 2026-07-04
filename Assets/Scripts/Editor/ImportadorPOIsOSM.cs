// Assets/Scripts/Editor/ImportadorPOIsOSM.cs
// ═══════════════════════════════════════════════════════════════════════════
//  IMPORTADOR POIs OSM — bares, tiendas, iglesias, cuartel, ayuntamiento…
//
//  PREREQUISITO: ejecutar primero Tools/DescargarPOIsAlsasua.py para generar
//  Assets/AlsasuaData/pois_unity.json desde la Overpass API.
//
//  Este tool crea marcadores visuales en escena para cada POI:
//  · Un cubo coloreado por categoría (bar=rojo, iglesia=blanco, policía=azul…)
//  · Escala diferente por importancia
//  · Snap al terreno con V3
//  · Nombre legible en el Inspector
//
//  Los marcadores son estáticos y muy baratos (batching). En Play se pueden
//  sustituir por prefabs reales via ConfiguradorAssetsAAA.
//
//  Menú: Tools/Alsasua/Mundo/📍 Importar POIs (bares, iglesias, cuarteles…)
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ImportadorPOIsOSM
{
    const string JSON = "Assets/AlsasuaData/pois_unity.json";
    const string RAIZ = "POIs_OSM";

    [System.Serializable]
    class POI
    {
        public long   osm_id;
        public string nombre, categoria;
        public float  x, z;
    }
    [System.Serializable] class Wrap { public POI[] items; }

    // Color + tamaño por categoría
    static readonly Dictionary<string, (Color col, Vector3 sz)> ESTILOS = new()
    {
        ["bar"]              = (new Color(0.85f, 0.20f, 0.15f), new Vector3(3, 3, 3)),
        ["restaurante"]      = (new Color(0.90f, 0.45f, 0.10f), new Vector3(3, 3, 3)),
        ["comida_rapida"]    = (new Color(0.95f, 0.70f, 0.05f), new Vector3(2, 2, 2)),
        ["iglesia"]          = (new Color(0.95f, 0.95f, 0.90f), new Vector3(4, 8, 4)),
        ["escuela"]          = (new Color(0.20f, 0.50f, 0.85f), new Vector3(6, 4, 6)),
        ["banco"]            = (new Color(0.10f, 0.55f, 0.25f), new Vector3(3, 3, 3)),
        ["farmacia"]         = (new Color(0.15f, 0.70f, 0.25f), new Vector3(3, 4, 3)),
        ["hospital"]         = (new Color(0.95f, 0.95f, 0.95f), new Vector3(8, 5, 8)),
        ["gasolinera"]       = (new Color(0.85f, 0.75f, 0.05f), new Vector3(4, 3, 4)),
        ["policia"]          = (new Color(0.10f, 0.15f, 0.75f), new Vector3(5, 5, 5)),
        ["ayuntamiento"]     = (new Color(0.65f, 0.55f, 0.85f), new Vector3(8, 6, 8)),
        ["bomberos"]         = (new Color(0.85f, 0.15f, 0.10f), new Vector3(5, 4, 5)),
        ["correos"]          = (new Color(0.85f, 0.55f, 0.10f), new Vector3(3, 3, 3)),
        ["biblioteca"]       = (new Color(0.45f, 0.25f, 0.65f), new Vector3(5, 4, 5)),
        ["mercado"]          = (new Color(0.75f, 0.60f, 0.20f), new Vector3(6, 4, 6)),
        ["supermercado"]     = (new Color(0.20f, 0.65f, 0.30f), new Vector3(8, 4, 8)),
        ["hotel"]            = (new Color(0.50f, 0.40f, 0.70f), new Vector3(5, 6, 5)),
        ["monumento"]        = (new Color(0.70f, 0.60f, 0.45f), new Vector3(2, 5, 2)),
        ["historico"]        = (new Color(0.65f, 0.55f, 0.40f), new Vector3(4, 4, 4)),
        ["polideportivo"]    = (new Color(0.20f, 0.75f, 0.55f), new Vector3(8, 4, 8)),
        ["centro_civico"]    = (new Color(0.40f, 0.60f, 0.75f), new Vector3(6, 4, 6)),
        ["poi_generico"]     = (new Color(0.55f, 0.55f, 0.55f), new Vector3(2, 2, 2)),
    };

    static MuestreadorHeightmapV3 _v3; static bool _v3Init;
    static MuestreadorHeightmapV3 V3
    { get { if (_v3Init) return _v3; _v3Init = true; var m = new MuestreadorHeightmapV3(); if (m.Cargar()) _v3 = m; return _v3; } }

    static float Altura(float x, float z)
    {
        if (V3 != null && V3.EnRango(x, z)) return V3.AlturaMundo(x, z);
        return 0f;
    }

    [MenuItem("Tools/Alsasua/Mundo/📍 Importar POIs (bares, iglesias, cuarteles…)", priority = 22)]
    static void Importar()
    {
        string ruta = Path.GetFullPath(Path.Combine(Application.dataPath, "..", JSON));
        if (!File.Exists(ruta))
        {
            EditorUtility.DisplayDialog("POIs",
                $"No existe {JSON}.\n\n" +
                "Primero ejecuta:\n  python Tools/DescargarPOIsAlsasua.py\n\n" +
                "Esto descarga los POIs (bares, tiendas, iglesias…) de OpenStreetMap.",
                "Entendido");
            return;
        }

        // JsonUtility necesita el array envuelto
        string jsonText = File.ReadAllText(ruta);
        POI[] pois;
        try
        {
            // El JSON es un array directo (no objeto) → envolvemos
            var wrap = JsonUtility.FromJson<Wrap>("{\"items\":" + jsonText + "}");
            pois = wrap?.items ?? System.Array.Empty<POI>();
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog("POIs", $"Error parseando JSON: {e.Message}", "Vale");
            return;
        }

        if (pois.Length == 0) { EditorUtility.DisplayDialog("POIs", "Sin POIs en el archivo.", "Vale"); return; }

        if (!EditorUtility.DisplayDialog("Importar POIs",
            $"{pois.Length} POIs de OpenStreetMap:\nbares, iglesias, cuarteles, tiendas, farmacia, ayuntamiento…\n\n" +
            "Se crean marcadores coloreados por categoría. ¿Continuar?",
            "Importar", "Cancelar"))
            return;

        var raizAnt = GameObject.Find(RAIZ);
        if (raizAnt != null) Object.DestroyImmediate(raizAnt);
        var raiz = new GameObject(RAIZ);

        // Caché de materiales para no crear uno por POI
        var mats = new Dictionary<string, Material>();
        int n = 0;

        foreach (var poi in pois)
        {
            n++;
            if (n % 50 == 0 && EditorUtility.DisplayCancelableProgressBar(
                "POIs", $"{n}/{pois.Length}…", n / (float)pois.Length)) break;

            string cat = poi.categoria ?? "poi_generico";
            if (!ESTILOS.TryGetValue(cat, out var estilo))
                estilo = ESTILOS["poi_generico"];

            float y = Altura(poi.x, poi.z) + estilo.sz.y * 0.5f;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = string.IsNullOrEmpty(poi.nombre)
                ? $"{cat}_{poi.osm_id}"
                : $"{cat}_{poi.nombre}";
            go.transform.SetParent(raiz.transform);
            go.transform.position = new Vector3(poi.x, y, poi.z);
            go.transform.localScale = estilo.sz;

            // Material compartido por categoría
            if (!mats.TryGetValue(cat, out var mat))
            {
                mat = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"))
                    { name = $"M_POI_{cat}", color = estilo.col };
                mat.enableInstancing = true;
                mats[cat] = mat;
            }
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true;
        }

        EditorUtility.ClearProgressBar();

        // Resumen
        var conteo = new Dictionary<string, int>();
        foreach (var p in pois)
        {
            string c = p.categoria ?? "generico";
            conteo[c] = conteo.TryGetValue(c, out int v) ? v + 1 : 1;
        }
        var resumen = new System.Text.StringBuilder();
        foreach (var kv in conteo)
            if (kv.Value > 0) resumen.AppendLine($"  {kv.Key}: {kv.Value}");

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        EditorUtility.DisplayDialog("POIs ✅",
            $"{n} POIs colocados en escena:\n{resumen}\n" +
            "Raíz 'POIs_OSM'. Los colores identifican categoría.\n" +
            "En Play se pueden sustituir por prefabs reales.", "Genial");
    }

    [MenuItem("Tools/Alsasua/Mundo/↩️ Limpiar POIs", priority = 23)]
    static void Limpiar() { var r = GameObject.Find(RAIZ); if (r != null) Object.DestroyImmediate(r); }
}
#endif
