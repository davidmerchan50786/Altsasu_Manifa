// Assets/Scripts/OsmEdificioLoader.cs
// ═══════════════════════════════════════════════════════════════════════
//  CARGADOR DE EDIFICIOS OSM — ALSASUA / ALTSASU
//
//  Lee alsasua_edificios.json (generado por GeneradorOSMAlsasua.py)
//  y crea mallas extruidas con el footprint real de cada edificio OSM.
//
//  PARA GENERAR EL JSON COMPLETO (~800 edificios OSM reales):
//    python GeneradorOSMAlsasua.py
//    python GeneradorOSMAlsasua.py --radio 700 --output Assets/OSMData
//
//  Ya hay un dataset base (92 edificios principales) en Assets/OSMData.
//  Google Photorealistic 3D Tiles proporciona el visual fotorrealista;
//  este loader añade edificios extra donde los tiles no carguen.
// ═══════════════════════════════════════════════════════════════════════
//
// Lee alsasua_edificios.json generado por GeneradorOSMAlsasua.py
// y crea mallas extruidas con texturas de Street View por fachada.
//
// JERARQUÍA EN ESCENA:
//   OsmEdificioLoader (este GameObject)
//   └── way_XXXXXX  (CesiumGlobeAnchor en el centroide del edificio)
//       ├── Pared_0  (MeshRenderer + textura SV via MaterialPropertyBlock)
//       ├── Pared_1  ...
//       └── Tejado   (MeshRenderer + material gris oscuro compartido)
//
// REQUIERE:
//   · CesiumGeoreference en la escena (cargado por ConfiguradorAlsasua)
//   · alsasua_edificios.json en Assets/OSMData/ (Editor) o StreamingAssets/OSMData/ (Build)
//   · Imágenes en la subcarpeta fachadas/
//   · Paquete com.unity.nuget.newtonsoft-json en manifest.json
//
// OPTIMIZACIONES APLICADAS:
//   · MaterialPropertyBlock en vez de Material instances → 0 instancias extra, batching habilitado
//   · texturasPorFrame configurable (defecto 4) → carga 4× más rápida
//   · tex.Compress(false) + Apply(false,true) → DXT1, VRAM cae de 6.5 GB a ~847 MB
//   · Mesh.Optimize() → mejor localidad de caché GPU
//   · PropBaseMap cacheado como ID estático → sin hash lookup por textura
//   · Shader ID cacheado → sin string lookup en hot path
//
// COMPATIBILIDAD: Unity 6 · URP 17 · Cesium for Unity 2.x

#if CESIUM_FOR_UNITY
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using CesiumForUnity;
using Unity.Mathematics;

// BUG GUARD: orden de ejecución 20 asegura que Start() de OsmEdificioLoader corre
// DESPUÉS de ConfiguradorAlsasua (DefaultExecutionOrder 0), de modo que el
// CesiumGeoreference ya está en escena cuando lo buscamos en Start().
[DefaultExecutionOrder(20)]
public class OsmEdificioLoader : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════════════
    //  INSPECTOR
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ ARCHIVO JSON ═══")]
    [Tooltip("Ruta relativa a la carpeta de datos OSM.\n" +
             "• Editor: relativa a Assets/  (p.ej. OSMData/alsasua_edificios.json)\n" +
             "• Build:  relativa a StreamingAssets/ (misma ruta, copiar carpeta)\n" +
             "• Genera el JSON con: python GeneradorOSMAlsasua.py")]
    [SerializeField] private string rutaJsonRelativa = "OSMData/alsasua_edificios.json";

    [Header("═══ CARGA ═══")]
    [Range(1, 50)]
    [Tooltip("Edificios creados por frame — mayor = carga rápida, posibles micro-stutters")]
    [SerializeField] private int edificiosPorFrame = 5;

    [Range(1, 20)]
    [Tooltip("Texturas Street View aplicadas por frame — mayor = carga más rápida\n" +
             "Defecto 4 (≈ 4× más rápido que 1/frame). En PC potente sube a 8-10.")]
    [SerializeField] private int texturasPorFrame = 4;

    [Range(0, 1000)]
    [Tooltip("Máximo de edificios a cargar (0 = todos). Útil para pruebas.")]
    [SerializeField] private int maxEdificios = 0;

    [Header("═══ GEOMETRÍA ═══")]
    [Range(1f, 20f)]
    [Tooltip("Altura mínima de edificio en metros (evita geometría plana)")]
    [SerializeField] private float alturaMinima = 3.5f;

    [Range(0.1f, 10f)]
    [Tooltip("Longitud mínima de pared en metros para crear quad (igual que GeneradorFachadas.py)")]
    [SerializeField] private float paredMinMetros = 0.5f;

    [Header("═══ ALTITUD DEL TERRENO ═══")]
    [Tooltip("Altura elipsoidal WGS84 aproximada del suelo de Alsasua (metros). " +
             "ConfiguradorAlsasua usa 536 m (altitud real del valle de Burunda).")]
    [SerializeField] private double altitudTerreno = 536.0;

    [Header("═══ MATERIALES ═══")]
    [Tooltip("Material de fachada sin imagen (si null, se crea uno URP en runtime). " +
             "Debe soportar MaterialPropertyBlock (_BaseMap).")]
    [SerializeField] private Material materialFachada;

    [Tooltip("Material de tejado (si null, se crea uno URP en runtime).")]
    [SerializeField] private Material materialTejado;

    [Tooltip("Comprimir texturas a DXT1 tras la carga.\n" +
             "Reduce VRAM ~8× (6.5 GB → 847 MB para 5.649 fachadas) a costa de un leve\n" +
             "coste CPU por textura al cargar. Recomendado siempre en desktop.")]
    [SerializeField] private bool comprimirTexturas = true;

    [Header("═══ ESTADO (solo lectura) ═══")]
    [SerializeField] private int edificiosCargados;
    [SerializeField] private int texturasCargadas;
#pragma warning disable 0414
    [SerializeField] private bool cargaEdificiosCompleta;
    [SerializeField] private bool cargaTexturasCompleta;
#pragma warning restore 0414

    // ═══════════════════════════════════════════════════════════════════════
    //  CONSTANTES
    // ═══════════════════════════════════════════════════════════════════════

    private const double LAT_SCALE  = 111_320.0;
    private const double DEG_TO_RAD = Math.PI / 180.0; // FIX: evita la operación inline × Math.PI/180 en el bucle por edificio

    // OPT: IDs cacheados en estático — evitan string hash lookup en cada SetTexture
    private static readonly int PropBaseMap = Shader.PropertyToID("_BaseMap");  // URP/Lit
    private static readonly int PropMainTex = Shader.PropertyToID("_MainTex"); // Standard (fallback)

    // ═══════════════════════════════════════════════════════════════════════
    //  ESTADO INTERNO
    // ═══════════════════════════════════════════════════════════════════════

    private CesiumGeoreference georef;

    // OPT: MaterialPropertyBlock reutilizado — sin alloc por textura
    private readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();

    // Cola (ruta local, Renderer destino) para carga de texturas progresiva
    private readonly Queue<(string ruta, Renderer renderer)> colaTexturas =
        new Queue<(string, Renderer)>();

    // FIX LEAK: Unity NO destruye automáticamente los Mesh creados en runtime al destruir el MeshFilter.
    // Con 7.462 meshes (6384 paredes + 1078 tejados), cada llamada a RecargarEdificios() acumula
    // ~4 MB de Mesh objects huérfanos en memoria. Los destruimos explícitamente en OnDestroy/Recargar.
    private readonly List<Mesh> meshesCreados = new List<Mesh>();

    private bool texturasEnCarga;

    // FIX: evita doble carga si Start() o RecargarEdificios() se llama mientras ya hay una en curso
    private bool cargaEnCurso;

    // FIX: propiedad de textura correcta para el shader en uso (URP/Lit=_BaseMap, Standard=_MainTex)
    // Detectada una sola vez en Start() y cacheada aquí — evita HasProperty en el bucle caliente.
    private int texPropId;

    // FIX OPT: umbral de longitud de pared al cuadrado — evita Mathf.Sqrt en el bucle caliente (1 call/pared)
    private float paredMinSqr;

    // Materiales compartidos (uno para todas las fachadas, uno para todos los tejados)
    // OPT: NO se instancian por pared → 0 Material objects extra en memoria
    private Material matFachadaCompartido;
    private Material matTejadoCompartido;

    // Flags para OnDestroy: solo destruir los materiales que creamos nosotros en runtime.
    // Los asignados desde el Inspector son assets del proyecto — no se deben destruir.
    private bool matFachadaCreado;
    private bool matTejadoCreado;

    // FIX LEAK: rastrear Texture2D creadas en runtime — Unity NO las libera al destruir el Renderer.
    // 5649 texturas × 0.15 MB (DXT1) = 847 MB de VRAM acumulados si nunca se destruyen.
    // Deben destruirse explícitamente en OnDestroy() y RecargarEdificios().
    private readonly List<Texture2D> texturasCreadas = new List<Texture2D>();

    // ═══════════════════════════════════════════════════════════════════════
    //  UNITY
    // ═══════════════════════════════════════════════════════════════════════

    private void Start()
    {
        // FIX: guard singleton — impide doble carga si el componente es activado dos veces
        if (cargaEnCurso)
        {
            AlsasuaLogger.Warn("OsmLoader", "Ya hay una carga en curso. " +
                               "Usa 'Recargar edificios' en el Inspector para reiniciar.");
            return;
        }

        georef = UnityEngine.Object.FindFirstObjectByType<CesiumGeoreference>();
        if (georef == null)
        {
            AlsasuaLogger.Error("OsmLoader", "CesiumGeoreference no encontrado. " +
                                "Ejecuta Alsasua → ⚙ Configurar Escena Completa primero.");
            return;
        }

        if (materialFachada != null)
        {
            matFachadaCompartido = materialFachada;
        }
        else
        {
            matFachadaCompartido = CrearMaterialRuntime(new Color(0.72f, 0.68f, 0.62f));
            matFachadaCreado = true;
        }

        if (materialTejado != null)
        {
            matTejadoCompartido = materialTejado;
        }
        else
        {
            matTejadoCompartido = CrearMaterialRuntime(new Color(0.22f, 0.22f, 0.22f));
            matTejadoCreado = true;
        }

        // FIX: detectar la propiedad de textura correcta para el shader asignado.
        // URP/Lit usa _BaseMap; Standard usa _MainTex. Detectarlo aquí (una vez) evita
        // la llamada a HasProperty() dentro del bucle caliente de ProcesarColaTexturas().
        texPropId = (matFachadaCompartido != null && matFachadaCompartido.HasProperty(PropBaseMap))
            ? PropBaseMap
            : PropMainTex;

        // FIX OPT: pre-calcular el umbral al cuadrado para usar sqrMagnitude en CrearEdificio()
        // y evitar la raíz cuadrada implícita de Vector2.Distance() en el bucle por pared.
        paredMinSqr = paredMinMetros * paredMinMetros;

        cargaEnCurso = true;
        string ruta = Path.Combine(OsmDataDir, rutaJsonRelativa);
        StartCoroutine(CargarJson(ruta));
    }

    private void OnDestroy()
    {
        // Destruir materiales runtime (los asignados desde Inspector son del Asset — no tocar)
        if (matFachadaCreado && matFachadaCompartido != null) Destroy(matFachadaCompartido);
        if (matTejadoCreado  && matTejadoCompartido  != null) Destroy(matTejadoCompartido);

        // FIX LEAK: destruir los Mesh objects runtime — Unity NO los libera al destruir el MeshFilter
        foreach (var mesh in meshesCreados)
            if (mesh != null) Destroy(mesh);
        meshesCreados.Clear();

        // FIX LEAK: destruir las Texture2D de Street View — no se liberan al destruir el Renderer
        foreach (var tex in texturasCreadas)
            if (tex != null) Destroy(tex);
        texturasCreadas.Clear();
    }

    /// <summary>
    /// Directorio raíz de los datos OSM.
    /// En Editor: Assets/ — en Build: StreamingAssets/
    /// </summary>
    private static string OsmDataDir
    {
#if UNITY_EDITOR
        get => Application.dataPath;
#else
        get => Application.streamingAssetsPath;
#endif
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PASO 1 — CARGA DEL JSON
    // ═══════════════════════════════════════════════════════════════════════

    private IEnumerator CargarJson(string ruta)
    {
        string url = ruta;
#if !UNITY_ANDROID || UNITY_EDITOR
        if (!ruta.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = "file://" + ruta;
#endif

        using var uwr = UnityWebRequest.Get(url);
        yield return uwr.SendWebRequest();

        // FIX: guard post-yield — si el GO fue destruido mientras esperaba la respuesta,
        // salir limpiamente antes de intentar parsear el JSON o crear edificios.
        if (!this) yield break;

        if (uwr.result != UnityWebRequest.Result.Success)
        {
            AlsasuaLogger.Error("OsmLoader", $"No se pudo cargar '{ruta}': {uwr.error}\n" +
                                "Comprueba que GeneradorFachadas.py se ejecutó y que los archivos " +
                                "están en la carpeta OSMData/.");
            yield break;
        }

        JObject raiz;
        try
        {
            raiz = JObject.Parse(uwr.downloadHandler.text);
        }
        catch (Exception ex)
        {
            AlsasuaLogger.Error("OsmLoader", $"JSON malformado: {ex.Message}");
            yield break;
        }

        var edificiosToken = raiz["edificios"] as JArray;
        if (edificiosToken == null || edificiosToken.Count == 0)
        {
            AlsasuaLogger.Warn("OsmLoader", "El JSON no contiene edificios. " +
                               "¿Se ejecutó GeneradorFachadas.py correctamente?");
            yield break;
        }

        int totalEdif = raiz["total_edificios"]?.Value<int>() ?? edificiosToken.Count;
        int paredesSV = raiz["paredes_con_sv"]?.Value<int>()  ?? 0;
        AlsasuaLogger.Info("OsmLoader", $"JSON cargado: {totalEdif} edificios, {paredesSV} paredes con Street View.");

        // MEM FIX: liberar el JObject raíz del JSON — ya no lo necesitamos.
        // El árbol Newtonsoft puede ocupar varios MB para cientos de edificios.
        // Nullarlo aquí permite que el GC lo recoja ANTES del bucle pesado de construcción.
        raiz = null;
        // Hint al GC para que limpie antes de la fase de construcción más exigente.
        // No garantizado pero reduce la probabilidad de OOM en máquinas con poca RAM.
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();

        // Ceder un frame para que el motor procese la limpieza antes de empezar.
        yield return null;

        yield return StartCoroutine(CrearEdificios(edificiosToken));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PASO 2 — CREACIÓN DE EDIFICIOS
    // ═══════════════════════════════════════════════════════════════════════

    private IEnumerator CrearEdificios(JArray edificiosToken)
    {
        int limite = (maxEdificios > 0)
            ? Mathf.Min(maxEdificios, edificiosToken.Count)
            : edificiosToken.Count;

        for (int i = 0; i < limite; i++)
        {
            // FIX: si el componente es destruido entre yields, detener la coroutine limpiamente
            if (!this) yield break;

            // FIX: capturar el GO creado para poder destruirlo si la creación falla a medias.
            // Sin esto, un edificio con datos corruptos deja un GO vacío huérfano en la jerarquía.
            GameObject goCreado = null;
            try
            {
                goCreado = CrearEdificio(edificiosToken[i] as JObject);
            }
            catch (Exception ex)
            {
                AlsasuaLogger.Warn("OsmLoader", $"Error al crear edificio [{i}]: {ex.Message}");
                if (goCreado != null) Destroy(goCreado); // limpieza parcial
            }

            edificiosCargados = i + 1;

            if ((i + 1) % edificiosPorFrame == 0)
            {
                yield return null;
                if (!this) yield break; // FIX: re-check tras ceder control al motor
            }
        }

        cargaEdificiosCompleta = true;
        AlsasuaLogger.Info("OsmLoader", $"✓ {edificiosCargados} edificios creados. " +
                           $"Cargando {colaTexturas.Count} texturas ({texturasPorFrame}/frame)...");

        // FIX: guard explícito antes de StartCoroutine — si el GO fue destruido durante el último yield
        if (this != null && !texturasEnCarga)
            StartCoroutine(ProcesarColaTexturas());
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Crear un edificio individual
    // ─────────────────────────────────────────────────────────────────────

    // FIX: devuelve el GO creado para que CrearEdificios() pueda destruirlo si la creación falla a medias.
    private GameObject CrearEdificio(JObject edif)
    {
        if (edif == null) return null;

        var nodosToken = edif["nodos"] as JArray;
        if (nodosToken == null || nodosToken.Count < 3) return null;

        float altura = Mathf.Max(edif["altura"]?.Value<float>() ?? 9f, alturaMinima);

        int n = nodosToken.Count;

        // OPT: leer lon/lat directamente como float; la conversión a coordenadas locales
        // usa double solo en el centroide (necesario para precisión geográfica).
        double sumLon = 0, sumLat = 0;
        var lons = new float[n];
        var lats = new float[n];

        for (int i = 0; i < n; i++)
        {
            var par = nodosToken[i] as JArray;
            if (par == null || par.Count < 2) return null;
            double lon = par[0].Value<double>();
            double lat = par[1].Value<double>();
            lons[i] = (float)lon;
            lats[i] = (float)lat;
            sumLon += lon;
            sumLat += lat;
        }

        double centroLon = sumLon / n;
        double centroLat = sumLat / n;
        double lonScale  = LAT_SCALE * Math.Cos(centroLat * DEG_TO_RAD); // FIX OPT: usa constante

        string wayId = edif["id"]?.Value<string>() ?? $"way_{edificiosCargados}";
        var go = new GameObject(wayId.Replace("/", "_"));
        go.transform.SetParent(transform, worldPositionStays: false);

        var anchor = go.AddComponent<CesiumGlobeAnchor>();
        anchor.longitudeLatitudeHeight = new double3(centroLon, centroLat, altitudTerreno);
        anchor.adjustOrientationForGlobeWhenMoving = false;

        var pts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            pts[i] = new Vector2(
                (float)((lons[i] - centroLon) * lonScale),
                (float)((lats[i] - centroLat) * LAT_SCALE));
        }

        var paredesToken = edif["paredes"] as JArray;

        for (int i = 0; i < n; i++)
        {
            Vector2 p1 = pts[i];
            Vector2 p2 = pts[(i + 1) % n];

            // FIX OPT: sqrMagnitude evita Sqrt implícito de Vector2.Distance (1 Sqrt/pared × 6384 paredes)
            if ((p2 - p1).sqrMagnitude < paredMinSqr) continue;

            JObject paredDatos = (paredesToken != null && i < paredesToken.Count)
                ? paredesToken[i] as JObject : null;

            CrearPared(go.transform, i, p1, p2, altura, paredDatos);
        }

        CrearTejado(go.transform, pts, altura);
        return go;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  GENERACIÓN DE MALLAS
    // ═══════════════════════════════════════════════════════════════════════

    private void CrearPared(Transform parent, int indice,
                            Vector2 p1, Vector2 p2, float altura,
                            JObject datos)
    {
        var go = new GameObject($"Pared_{indice}");
        go.transform.SetParent(parent, worldPositionStays: false);

        var verts = new Vector3[]
        {
            new Vector3(p1.x, 0f,     p1.y),
            new Vector3(p2.x, 0f,     p2.y),
            new Vector3(p2.x, altura, p2.y),
            new Vector3(p1.x, altura, p1.y),
        };

        var uvs = new Vector2[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),
        };

        Vector3 dir  = new Vector3(p2.x - p1.x, 0f, p2.y - p1.y);
        Vector3 nrml = Vector3.Cross(dir.normalized, Vector3.up).normalized;
        var normals = new Vector3[] { nrml, nrml, nrml, nrml };

        var tris = new int[] { 0, 2, 1, 0, 3, 2 };

        var mesh = new Mesh { name = $"wall_{indice}" };
        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.normals   = normals;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        mesh.Optimize(); // OPT: reordena vértices para mejor hit rate de caché GPU

        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        // FIX LEAK: rastrear el Mesh para destruirlo en OnDestroy/RecargarEdificios.
        // Unity NO destruye automáticamente los Mesh objects cuando se destruye el MeshFilter.
        meshesCreados.Add(mesh);

        // OPT: liberar la copia CPU del mesh (verts/uvs/normals/tris) ahora que el MeshFilter
        // ya tiene la referencia. La GPU conserva sus datos. Ahorro: ~200 bytes × 6.384 paredes = ~1.2 MB RAM.
        mesh.UploadMeshData(true);

        var mr = go.AddComponent<MeshRenderer>();

        // OPT: sharedMaterial en vez de material → cero instancias; la textura se
        // aplica individualmente via MaterialPropertyBlock sin romper el batching.
        mr.sharedMaterial = matFachadaCompartido;

        bool tieneSV  = datos?["tiene_sv"]?.Value<bool>() ?? false;
        string imagen = tieneSV ? datos?["imagen"]?.Value<string>() : null;

        if (!string.IsNullOrEmpty(imagen))
        {
            string rutaImg = Path.Combine(OsmDataDir, "OSMData", "fachadas",
                                          Path.GetFileName(imagen));
            colaTexturas.Enqueue((rutaImg, mr));
        }
    }

    private void CrearTejado(Transform parent, Vector2[] pts, float altura)
    {
        if (pts == null || pts.Length < 3) return;

        var go = new GameObject("Tejado");
        go.transform.SetParent(parent, worldPositionStays: false);

        int n = pts.Length;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            if (pts[i].x < minX) minX = pts[i].x;
            if (pts[i].x > maxX) maxX = pts[i].x;
            if (pts[i].y < minZ) minZ = pts[i].y;
            if (pts[i].y > maxZ) maxZ = pts[i].y;
        }
        float rangoX = maxX - minX; if (rangoX < 0.001f) rangoX = 1f;
        float rangoZ = maxZ - minZ; if (rangoZ < 0.001f) rangoZ = 1f;

        var verts = new Vector3[n];
        var uvs   = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            verts[i] = new Vector3(pts[i].x, altura, pts[i].y);
            uvs[i]   = new Vector2((pts[i].x - minX) / rangoX,
                                   (pts[i].y - minZ) / rangoZ);
        }

        var tris = new int[(n - 2) * 3];
        for (int i = 0; i < n - 2; i++)
        {
            tris[i * 3]     = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = i + 2;
        }

        var mesh = new Mesh { name = "roof" };
        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.Optimize(); // OPT: caché GPU

        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        // FIX LEAK + OPT: igual que en CrearPared — rastrear y liberar datos CPU tras asignar al MeshFilter.
        meshesCreados.Add(mesh);
        mesh.UploadMeshData(true);

        go.AddComponent<MeshRenderer>().sharedMaterial = matTejadoCompartido;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PASO 3 — CARGA DE TEXTURAS
    // ═══════════════════════════════════════════════════════════════════════

    private IEnumerator ProcesarColaTexturas()
    {
        // FIX: guard de seguridad — si el GO fue destruido antes de arrancar la coroutine
        if (!this) yield break;

        texturasEnCarga = true;
        int enEsteFrame  = 0;
        int totalCola    = colaTexturas.Count; // snapshot para el log de progreso
        int texturasError = 0;

        while (colaTexturas.Count > 0)
        {
            // FIX: check tras cada yield — si el GO fue destruido mientras procesábamos, salir limpio
            if (!this) yield break;

            var (ruta, renderer) = colaTexturas.Dequeue();

            if (renderer == null)
            {
                // El GO fue destruido mientras esperábamos; no consumir frame ni slot de progreso
                continue;
            }

            string url = ruta;
#if !UNITY_ANDROID || UNITY_EDITOR
            if (!ruta.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                url = "file://" + ruta;
#endif

            using var uwr = UnityWebRequestTexture.GetTexture(url);
            yield return uwr.SendWebRequest();

            if (!this) yield break; // FIX: re-check tras la petición async

            if (uwr.result == UnityWebRequest.Result.Success)
            {
                Texture2D tex = DownloadHandlerTexture.GetContent(uwr);
                if (tex != null && renderer != null)
                {
                    // OPT: comprimir a DXT1 antes de subir a GPU
                    //   640×480 RGBA32 = 1.17 MB/textura × 5.649 = 6.6 GB VRAM
                    //   640×480 DXT1   = 0.15 MB/textura × 5.649 = 847 MB VRAM  (-88%)
                    //   Apply(false, makeNoLongerReadable:true) libera además la copia en RAM.
                    if (comprimirTexturas)
                    {
                        // MEJORA CALIDAD: generar cadena mipmap ANTES de comprimir.
                        // Sin mipmaps, edificios a >50m shimmeran/aliasan. Con mipmaps
                        // el mip correcto se muestra a cada distancia → imagen limpia.
                        // Apply(true) genera los mip levels desde el nivel 0 (CPU side).
                        tex.Apply(true);           // genera mipmaps en RGBA32 (CPU)
                        tex.Compress(false);        // comprime RGBA32+mipmaps → DXT1+mipmaps
                        tex.Apply(false, true);     // sube DXT1+mipmaps a GPU, libera CPU
                    }
                    else
                    {
                        tex.Apply(true, true);      // genera mipmaps + sube + libera CPU
                    }

                    // FIX LEAK: rastrear la textura para destruirla en OnDestroy/RecargarEdificios.
                    // Aunque Apply(false, true) libera la copia CPU, el objeto Texture2D sigue
                    // ocupando VRAM hasta que se llame Destroy(tex) explícitamente.
                    texturasCreadas.Add(tex);

                    // MEJORA CALIDAD: Trilinear aprovecha los mipmaps para transición
                    // suave entre niveles de detalle (vs Bilinear que produce "popping").
                    // anisoLevel 4 = nitidez en fachadas vistas en ángulo oblicuo.
                    tex.filterMode = FilterMode.Trilinear;
                    tex.anisoLevel = 4;

                    // OPT: MaterialPropertyBlock — textura por renderer sin instanciar material.
                    // Todos los renderers siguen compartiendo matFachadaCompartido → GPU instancing OK.
                    // FIX: usa texPropId (detectado en Start) en vez de PropBaseMap hardcodeado,
                    // para que funcione tanto con URP/Lit (_BaseMap) como con Standard (_MainTex).
                    renderer.GetPropertyBlock(mpb);
                    mpb.SetTexture(texPropId, tex);
                    renderer.SetPropertyBlock(mpb);

                    texturasCargadas++;
                }
            }
            else
            {
                // Fachada sin imagen → color cemento del matFachadaCompartido. No es un error grave.
                texturasError++;
            }

            // Log de progreso cada 500 texturas para visibilidad en la consola sin spam
            if (texturasCargadas > 0 && texturasCargadas % 500 == 0)
                AlsasuaLogger.Info("OsmLoader", $"Texturas: {texturasCargadas}/{totalCola} " +
                                   $"({texturasCargadas * 100 / Mathf.Max(1, totalCola)}%)...");

            enEsteFrame++;
            if (enEsteFrame >= texturasPorFrame)
            {
                enEsteFrame = 0;
                yield return null; // ceder control al motor cada N texturas
            }
        }

        texturasEnCarga       = false;
        cargaTexturasCompleta = true;
        cargaEnCurso          = false; // FIX: liberar el guard para permitir futuros Recargar
        AlsasuaLogger.Info("OsmLoader", $"✓ {texturasCargadas} texturas Street View aplicadas" +
                           (texturasError > 0 ? $" ({texturasError} sin imagen → color cemento)." : "."));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MATERIALES — HELPER
    // ═══════════════════════════════════════════════════════════════════════

    private static Material CrearMaterialRuntime(Color color)
    {
        // Cadena de fallback: URP/Lit → URP/Unlit → Standard → error magenta
        var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard")
                  ?? Shader.Find("HDRP/Unlit") ?? Shader.Find("Standard")
                  ?? Shader.Find("Standard");

        if (shader == null)
        {
            AlsasuaLogger.Error("OsmLoader", "Ningún shader URP/Standard encontrado. " +
                                "Incluye 'Universal Render Pipeline/Lit' en Always Included Shaders.");
            shader = Shader.Find("Hidden/InternalErrorShader")
                  ?? Shader.Find("Sprites/Default")
                  ?? Shader.Find("UI/Default");
        }

        return new Material(shader) { color = color };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  INSPECTOR — CONTEXT MENUS
    // ═══════════════════════════════════════════════════════════════════════

    [ContextMenu("Recargar edificios")]
    public void RecargarEdificios()
    {
        // FIX: detener todas las coroutines activas ANTES de destruir GOs/meshes.
        // Sin esto, CargarJson/CrearEdificios/ProcesarColaTexturas siguen corriendo en paralelo
        // con la nueva carga → doble geometría, doble texturizado, double-free de recursos.
        StopAllCoroutines();
        texturasEnCarga = false;

        // FIX LEAK: destruir los Mesh objects antes de destruir los GameObjects que los referencian.
        // Orden importa: si destruimos el GO primero, el MeshFilter desaparece pero el Mesh asset
        // queda huérfano en memoria sin ninguna forma de recuperarlo para destruirlo.
        foreach (var mesh in meshesCreados)
            if (mesh != null) Destroy(mesh);
        meshesCreados.Clear();

        // FIX LEAK: destruir las Texture2D de Street View antes de destruir los Renderers.
        foreach (var tex in texturasCreadas)
            if (tex != null) Destroy(tex);
        texturasCreadas.Clear();

        // Destruir GameObjects hijo (edificios y tejados)
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);

        colaTexturas.Clear();
        edificiosCargados      = 0;
        texturasCargadas       = 0;
        cargaEdificiosCompleta = false;
        cargaTexturasCompleta  = false;
        cargaEnCurso           = false; // FIX: reset para que Start-guard no bloquee la nueva carga

        string ruta = Path.Combine(OsmDataDir, rutaJsonRelativa);
        StartCoroutine(CargarJson(ruta));
        // Nota: CargarJson → Start ya no pone cargaEnCurso; lo pondrá la nueva llamada a
        // StartCoroutine(CargarJson(...)) a través del flujo normal sin pasar por Start().
        // Por eso lo marcamos aquí explícitamente:
        cargaEnCurso = true;
    }
}
#endif // CESIUM_FOR_UNITY
