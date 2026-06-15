// Assets/Scripts/AlsasuaTreeStreamer.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ÁRBOL STREAMER AAA — posiciona árboles en XYZ real desde lidar_trees.json
//  + relleno procedural en masas_forestales sin datos individuales.
//
//  Especie por bioma:
//    · Roble/Haya  — laderas norte (aspect > 0.3, pendiente 15-40°)
//    · Pino        — zonas altas S (>650m, aspect < 0.1)
//    · Chopo/Sauce — junto a ríos (distancia < 12m a cauce)
//    · Mixto       — resto de masas forestales
//
//  LOD distances (configurable desde Inspector):
//    LOD5 800m — impostores / billboard
//    LOD4 500m — malla muy baja
//    LOD3 250m — malla baja
//    LOD2 120m — malla media
//    LOD1  50m — malla alta
//    LOD0  20m — malla completa full
//
//  Burst IJobParallelFor + NativeArray persistentes — zero alloc en Update
//
//  Capa de árboles de rango medio (30-800m):
//    - SistemaVegetacion gestiona hierba/arbustos GPU instanced (< 30m)
//    - AlsasuaTreeStreamer gestiona árboles individuales prefab (30-800m)
//    - Cesium Google Tiles gestiona bosques lejanos (> 800m)
// ═══════════════════════════════════════════════════════════════════════════

using System;                 // MemoryExtensions.AsSpan
using System.Collections;
using System.Collections.Generic;
using System.IO;
// PERF: System.Linq eliminado — era usado solo en un Count(predicate) reemplazado por loop manual
using System.Threading.Tasks;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

public class AlsasuaTreeStreamer : MonoBehaviour
{
    // ── Prefabs por especie ────────────────────────────────────────────────
    [Header("Prefabs por especie (GreenForest)")]
    [Tooltip("Prefabs genéricos — fallback cuando no hay prefab específico")]
    public GameObject[] treePrefabs;
    [Tooltip("Roble / Haya — laderas norte, húmedo")]
    public GameObject[] prefabsRoble;
    [Tooltip("Pino — zonas altas y laderas sur secas")]
    public GameObject[] prefabsPino;
    [Tooltip("Chopo / Sauce — riberas de río")]
    public GameObject[] prefabsRibera;

    // ── LOD distances (Unity LODGroup) ─────────────────────────────────────
    [Header("LOD Distances")]
    [Tooltip("LOD0 full mesh hasta X m")]
    public float lod0Dist = 20f;
    [Tooltip("LOD1 alta calidad")]
    public float lod1Dist = 50f;
    [Tooltip("LOD2 media")]
    public float lod2Dist = 120f;
    [Tooltip("LOD3 baja")]
    public float lod3Dist = 250f;
    [Tooltip("LOD4 muy baja")]
    public float lod4Dist = 500f;
    [Tooltip("LOD5 billboard/impostor")]
    public float lod5Dist = 800f;

    [Header("Radio de streaming")]
    [Tooltip("Distancia a la que se instancian árboles (m). Alias: radioStreaming.")]
    public float radioVisible  = 800f;

    [Tooltip("Distancia a la que se destruyen árboles fuera de rango (m). Debe ser > radioVisible.")]
    public float radioDestroir = 950f;

    [Tooltip("Distancia mínima — árboles más cercanos los gestiona SistemaVegetacion.")]
    public float radioMinimo   = 30f;

    [Tooltip("Máximo de árboles instanciados simultáneamente.")]
    public int maxArboles = 300;

    // Alias de compatibilidad con código antiguo
    public float radioStreaming { get => radioVisible; set => radioVisible = value; }

    [Header("Datos OSM")]
    [Tooltip("Ruta al JSON con posiciones de árboles OSM.\n" +
             "Generado por DescargarDatosAlsasua.ps1 → Assets/AlsasuaData/trees_unity.json")]
    public string rutaJSON = "Assets/AlsasuaData/trees_unity.json";

    // ── Relleno procedural en masas forestales ─────────────────────────────
    [Header("Relleno Procedural (masas_forestales)")]
    [Tooltip("Añadir árboles procedurales en zonas de bosque sin datos individuales")]
    public bool rellenoProcedural = true;
    [Tooltip("Densidad procedural: árboles por hectárea (10000m²)")]
    [Range(10f, 300f)]
    public float densidadPorHa = 80f;

    // ── Estado interno ─────────────────────────────────────────────────────
    readonly List<GameObject> _instancias = new();
    readonly List<Vector3>    _posiciones  = new();   // XZ en Unity, Y ignorado en Job
    readonly List<int>        _especies    = new();   // índice de especie por posición
    Transform                 _jugador;
    bool                      _cargado;

    // BUG FIX: referencias a corrutinas persistentes para poder cancelarlas en OnDestroy.
    // Sin guardar la referencia StopCoroutine() no puede detener la corrutina específica
    // y tras Destroy() la corrutina sigue ejecutando un frame más accediendo a datos
    // ya liberados (_pool, _nativeInit, NativeArrays disposed) → NullRef / SIGSEGV.
    Coroutine _crInicializar;
    Coroutine _crBucleSteaming;

    // ── Auto-pausa por sobrecarga de frame (Director de Simulación) ──────────
    // Cuando el FactorCarga del orquestador baja del umbral de pausa, este productor
    // OPCIONAL frena su ritmo de instanciación (más intervalo, menos árboles/ciclo)
    // para devolver presupuesto al hilo principal. Histéresis para no parpadear.
    // Si el orquestador no existe (null), _degradado nunca se activa → ritmo normal.
    IGlobalSimulationOrchestrator _orquestador;
    System.Action<float>          _onFactorCarga;
    bool                          _degradado;

    // ── Constantes coordenadas — fuente única: GeoDataAlsasua ─────────────
    const float E_ORIG   = (float)GeoDataAlsasua.UTM_E_ORIGIN;
    const float N_ORIG   = (float)GeoDataAlsasua.UTM_N_ORIGIN;
    const float UNITY_OX = GeoDataAlsasua.UNITY_OX;
    const float UNITY_OZ = GeoDataAlsasua.UNITY_OZ;
    const float Z_MIN    = GeoDataAlsasua.Z_MIN;

    // ── Índices de especie ─────────────────────────────────────────────────
    const int ESP_GENERICO = 0;
    const int ESP_ROBLE    = 1;
    const int ESP_PINO     = 2;
    const int ESP_RIBERA   = 3;

    // ── Polígonos de masas forestales (para relleno procedural) ───────────
    List<Vector2[]> _bosquesPoligonos = new();

    const string PATH_LIDAR_TREES = "Assets/AlsasuaData/lidar_trees.json";
    const string PATH_TREES_UNITY = "Assets/AlsasuaData/trees_unity.json";
    const string PATH_BOSQUES     = "Assets/AlsasuaData/masas_forestales.geojson";

    // ── Pool de árboles ────────────────────────────────────────────────────
    readonly List<GameObject> _pool = new();
    /// Pools por especie para evitar instanciación en caliente de prefabs específicos.
    readonly Dictionary<string, Queue<GameObject>> _poolEspecies = new();
    [Tooltip("Tamaño inicial del pool genérico (pre-calentado en Awake).")]
    public int tamañoPool = 150;
    [Tooltip("Instancias precalentadas por cada especie específica (Roble/Pino/Ribera).")]
    public int tamañoPoolEspecie = 50;

    // PERF: buffer de candidatos reutilizable — evita new List<int>(MAX_CANDIDATOS) en cada
    // ciclo de BucleSteaming. ~1 alloc/ciclo eliminado (0.5-2 allocs/seg × sesión larga).
    readonly List<int> _candidatosBuffer = new List<int>(MAX_CANDIDATOS);

    // ──────────────────────────────────────────────────────────────────────

    void Awake()
    {
        PreCalentarPool();
        PreCalentarPoolEspecies();
    }

    void Start()
    {
        SuscribirDegrade();
        _crInicializar = StartCoroutine(InicializarAsync());
    }

    // ── Hookup con el Director de Simulación ────────────────────────────────
    void SuscribirDegrade()
    {
        _orquestador = ServiceLocator.Get<IGlobalSimulationOrchestrator>();
        if (_orquestador == null) return;   // sin director → ritmo normal de siempre

        var cfg = GlobalSimulationOrchestrator.Instancia?.Config;
        float pausa   = cfg?.productoresPausaFactor   ?? 0.85f;
        float reanuda = cfg?.productoresReanudaFactor ?? 0.95f;

        _onFactorCarga = factor =>
        {
            // Histéresis: solo entra en degradado por debajo de 'pausa' y solo
            // sale por encima de 'reanuda'; entre ambos mantiene el estado actual.
            if (!_degradado && factor < pausa)        _degradado = true;
            else if (_degradado && factor > reanuda)  _degradado = false;
        };
        _orquestador.OnFactorCargaCambia += _onFactorCarga;
        // Estado inicial coherente con el factor actual (por si arranca ya cargado).
        _onFactorCarga(_orquestador.FactorCarga);
    }

    IEnumerator InicializarAsync()
    {
        // Esperar a que el terreno LIDAR esté listo antes de clasificar especies.
        // Sin este guard ClasificarEspecie() recibe terrain=null y todos los árboles
        // quedan como ESP_GENERICO (sin pinos en montaña ni choperas en ribera).
        float tw = 0f;
        while (tw < 30f)
        {
            var svc = ServiceLocator.Get<ITerrainService>();
            if (svc != null && svc.EstaListo) break;
            if (svc == null && Terrain.activeTerrain != null) break; // escena legacy
            tw += 0.5f;
            yield return new WaitForSeconds(0.5f);
        }
        if (ServiceLocator.Get<ITerrainService>() == null && Terrain.activeTerrain == null)
            AlsasuaLogger.Warn("TreeStreamer", "Terreno no disponible tras 30s — clasificación de especie sin bioma.");

        // Cargar polígonos de masas forestales para relleno procedural
        yield return StartCoroutine(CargarBosquesGeojson());

        bool hayLIDAR = TieneArbolesLIDAR();
        if (hayLIDAR)
        {
            AlsasuaLogger.Info("TreeStreamer",
                "Árboles LIDAR detectados (lidar_trees.json) — usando coordenadas UTM reales.");
            yield return StartCoroutine(CargarPosicionesLIDARAsync());
        }
        else
        {
            CargarPosicionesOSM();
        }

        // Relleno procedural en masas forestales sin datos individuales
        if (rellenoProcedural && _bosquesPoligonos.Count > 0)
            yield return StartCoroutine(GenerarRellenoProcedural());

        // PERF: eliminadas 3 llamadas LINQ Count(predicate) → loop manual único (~3 iteraciones/elemento evitadas)
        // _especies.Count(e=>e==X) recorría la lista completa 3 veces. Un solo bucle hace lo mismo en O(n).
        int cntRoble = 0, cntPino = 0, cntRibera = 0;
        for (int i = 0; i < _especies.Count; i++)
        {
            int e = _especies[i];
            if (e == ESP_ROBLE)  cntRoble++;
            else if (e == ESP_PINO)   cntPino++;
            else if (e == ESP_RIBERA) cntRibera++;
        }
        AlsasuaLogger.Info("TreeStreamer",
            $"{_posiciones.Count} árboles totales ({cntRoble} roble, {cntPino} pino, {cntRibera} ribera)");

        _crBucleSteaming = StartCoroutine(BucleSteaming());
    }

    static bool TieneArbolesLIDAR()
    {
        string p = Path.Combine(Application.dataPath.Replace("Assets", ""), PATH_LIDAR_TREES);
        return File.Exists(p) && new FileInfo(p).Length > 500;
    }

    // ── Carga bosques geojson para relleno procedural ──────────────────────
    IEnumerator CargarBosquesGeojson()
    {
        string fullPath = Path.Combine(Application.dataPath.Replace("Assets",""), PATH_BOSQUES);
        if (!File.Exists(fullPath)) yield break;

        var task = Task.Run(() => ParsearPoligonosBosques(fullPath));
        while (!task.IsCompleted) yield return null;
        if (task.IsCompletedSuccessfully)
        {
            _bosquesPoligonos = task.Result;
            AlsasuaLogger.Info("TreeStreamer",$"{_bosquesPoligonos.Count} polígonos de bosque cargados");
        }
    }

    static List<Vector2[]> ParsearPoligonosBosques(string path)
    {
        var result = new List<Vector2[]>();
        try
        {
            string json = File.ReadAllText(path);
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            int searchPos = 0;
            while (true)
            {
                int coordIdx = json.IndexOf("\"coordinates\"", searchPos);
                if (coordIdx < 0) break;
                int bracketStart = json.IndexOf('[', coordIdx + 13);
                if (bracketStart < 0) { searchPos = coordIdx + 14; continue; }

                var pts = ExtraerPuntosUTMToUnity(json, bracketStart, ci);
                if (pts != null && pts.Length >= 3) result.Add(pts);
                searchPos = bracketStart + 1;
            }
        }
        catch { }
        return result;
    }

    static Vector2[] ExtraerPuntosUTMToUnity(string json, int startBracket,
        System.Globalization.CultureInfo ci)
    {
        var pts = new List<Vector2>();
        int depth = 0, i = startBracket;
        while (i < json.Length)
        {
            char c = json[i];
            if (c == '[') { depth++; i++; continue; }
            if (c == ']') { depth--; if (depth <= 0) break; i++; continue; }
            if (c == '-' || char.IsDigit(c))
            {
                int end = i;
                while (end < json.Length && (json[end]=='-'||json[end]=='.'||char.IsDigit(json[end]))) end++;
                if (!float.TryParse(json.AsSpan(i, end-i), System.Globalization.NumberStyles.Float, ci, out float eu))
                { i++; continue; }
                i = end;
                while (i < json.Length && (json[i]==','||json[i]==' ')) i++;
                end = i;
                while (end < json.Length && (json[end]=='-'||json[end]=='.'||char.IsDigit(json[end]))) end++;
                if (!float.TryParse(json.AsSpan(i, end-i), System.Globalization.NumberStyles.Float, ci, out float nu))
                { i++; continue; }
                i = end;
                pts.Add(new Vector2((eu - E_ORIG) + UNITY_OX, (nu - N_ORIG) + UNITY_OZ));
                while (i < json.Length && json[i]!='['&&json[i]!=']'&&json[i]!='-'&&!char.IsDigit(json[i])) i++;
            }
            else i++;
        }
        return pts.Count >= 3 ? pts.ToArray() : null;
    }

    // ── Carga LIDAR async con clasificación por especie ───────────────────
    IEnumerator CargarPosicionesLIDARAsync()
    {
        string p = Path.Combine(Application.dataPath.Replace("Assets",""), PATH_LIDAR_TREES);
        var task = Task.Run(() => {
            string json = File.ReadAllText(p);
            return JsonHelper.ParseArray<LIDARTreeData>(json);
        });
        while (!task.IsCompleted) yield return null;
        if (!task.IsCompletedSuccessfully) yield break;

        var arr     = task.Result;
        var terrain = Terrain.activeTerrain;
        if (arr == null) yield break;

        int batch = 0;
        foreach (var t in arr)
        {
            // lidar_trees.json: coordenadas Unity absolutas (OX=1918, OZ=8570 ya incluidos)
            float ux = t.x, uz = t.z;
            _posiciones.Add(new Vector3(ux, 0f, uz));

            // Clasificar especie por bioma
            int especie = ClasificarEspecie(ux, uz, terrain);
            _especies.Add(especie);

            if (++batch >= 500) { batch = 0; yield return null; }
        }

        _cargado = true;
        AlsasuaLogger.Info("TreeStreamer",
            $"{_posiciones.Count} árboles LIDAR cargados con clasificación de especie");
    }

    [System.Serializable] class LIDARTreeData { public float x, z, altura, radio; }

    // ── Relleno procedural en masas forestales ─────────────────────────────
    IEnumerator GenerarRellenoProcedural()
    {
        if (_bosquesPoligonos.Count == 0) yield break;

        var terrain  = Terrain.activeTerrain;
        var rng      = new System.Random(1337);
        // Calcular bbox de cada polígono y sembrar puntos
        int añadidos = 0;
        long celdas  = 0;   // celdas de grid evaluadas (diagnóstico del coste)
        float areaPorArbol = 10000f / densidadPorHa; // m²/árbol

        // Densidad en px de muestreo aleatorio: sqrt(area) ≈ spacing
        float spacing = Mathf.Sqrt(areaPorArbol);

        // FIX CUELGUE (2026-06-15): el doble bucle nx×nz solo hacía yield ENTRE polígonos.
        // Una masa forestal grande (sierras, bbox de km²) a spacing ~11 m son cientos de
        // miles/millones de PuntoEnPoligono en UN frame → hilo principal pegado minutos
        // (era el "No responde" tras cargar los árboles LIDAR). Ahora: presupuesto de
        // tiempo DENTRO del bucle (yield cada ~2 ms) + tope total → reparte sin congelar.
        const int   MAX_RELLENO   = 30000;  // candidatos de relleno; el streamer ya samplea por distancia
        const float MS_PRESUPUESTO = 2f;    // ms/frame de trabajo síncrono
        float t0 = Time.realtimeSinceStartup;
        int   desdeChequeo = 0;

        foreach (var poly in _bosquesPoligonos)
        {
            if (añadidos >= MAX_RELLENO) break;

            // Bounding box del polígono
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var v in poly)
            {
                if (v.x < minX) minX = v.x; if (v.x > maxX) maxX = v.x;
                if (v.y < minZ) minZ = v.y; if (v.y > maxZ) maxZ = v.y;
            }

            float w = maxX - minX, h = maxZ - minZ;
            if (w < 1f || h < 1f) continue;

            // Grid jittered dentro del polígono
            int nx = Mathf.Max(1, Mathf.CeilToInt(w / spacing));
            int nz = Mathf.Max(1, Mathf.CeilToInt(h / spacing));

            for (int iz = 0; iz < nz && añadidos < MAX_RELLENO; iz++)
            for (int ix = 0; ix < nx; ix++)
            {
                // Presupuesto por frame: no más de ~2 ms síncronos seguidos (chequeo cada
                // 256 celdas para que el propio Time.realtimeSinceStartup no domine).
                if (++desdeChequeo >= 256)
                {
                    desdeChequeo = 0;
                    if ((Time.realtimeSinceStartup - t0) * 1000f >= MS_PRESUPUESTO)
                    {
                        yield return null;
                        t0 = Time.realtimeSinceStartup;
                    }
                }
                celdas++;

                float ux = minX + (ix + (float)rng.NextDouble()) * spacing;
                float uz = minZ + (iz + (float)rng.NextDouble()) * spacing;

                if (!PuntoEnPoligono(ux, uz, poly)) continue;

                // Verificar si ya hay un árbol LIDAR cercano (radio 4m)
                bool yaOcupado = false;
                for (int k = Mathf.Max(0, _posiciones.Count - 20); k < _posiciones.Count; k++)
                {
                    float dx = _posiciones[k].x - ux, dz = _posiciones[k].z - uz;
                    if (dx*dx + dz*dz < 16f) { yaOcupado = true; break; }
                }
                if (yaOcupado) continue;

                _posiciones.Add(new Vector3(ux, 0f, uz));
                _especies.Add(ClasificarEspecie(ux, uz, terrain));
                añadidos++;
            }
        }

        AlsasuaLogger.Info("TreeStreamer",
            $"Relleno procedural: +{añadidos} árboles en {_bosquesPoligonos.Count} masas forestales " +
            $"({celdas} celdas evaluadas{(añadidos >= MAX_RELLENO ? ", TOPE alcanzado" : "")})");
    }

    // ── Clasificar especie por bioma ───────────────────────────────────────
    int ClasificarEspecie(float ux, float uz, Terrain terrain)
    {
        // 1. Junto a ríos → chopo/sauce
        if (GeneradorRiosYPuentes.Instance != null)
        {
            float d = GeneradorRiosYPuentes.Instance.DistanciaAlRio(ux, uz);
            if (d >= 0f && d < 12f) return ESP_RIBERA;
        }

        // Tile correcto del mosaico (o el terreno único / legacy del parámetro).
        var tile = TerrenoGlobal.TerrainEn(new Vector3(ux, 0f, uz));
        if (tile == null) tile = terrain;
        if (tile == null) return ESP_GENERICO;

        var td = tile.terrainData;
        Vector3 tp = tile.transform.position;
        // Normalizar RESPECTO AL TILE (con transform.position — el bug anterior
        // asumía terrain en el origen y desplazaba toda la clasificación)
        float nx = Mathf.Clamp01((ux - tp.x) / td.size.x);
        float nz = Mathf.Clamp01((uz - tp.z) / td.size.z);

        float altReal   = tp.y + td.GetInterpolatedHeight(nx, nz) + Z_MIN;
        float pendiente = td.GetSteepness(nx, nz);

        // Aspecto norte
        Vector3 normal = td.GetInterpolatedNormal(nx, nz);
        float aspectN  = Mathf.Clamp01(-normal.z);

        // 2. Laderas norte, pendiente media → roble/haya
        if (pendiente > 12f && pendiente < 40f && aspectN > 0.3f && altReal < 800f)
            return ESP_ROBLE;

        // 3. Zonas altas (>650m) o laderas sur → pino
        if (altReal > 650f || (pendiente > 10f && aspectN < 0.1f))
            return ESP_PINO;

        return ESP_GENERICO;
    }

    // ── Seleccionar prefab según especie ───────────────────────────────────
    GameObject SeleccionarPrefab(int especie)
    {
        GameObject[] arr = especie switch
        {
            ESP_ROBLE  => (prefabsRoble  != null && prefabsRoble.Length  > 0) ? prefabsRoble  : treePrefabs,
            ESP_PINO   => (prefabsPino   != null && prefabsPino.Length   > 0) ? prefabsPino   : treePrefabs,
            ESP_RIBERA => (prefabsRibera != null && prefabsRibera.Length > 0) ? prefabsRibera : treePrefabs,
            _          => treePrefabs,
        };
        if (arr == null || arr.Length == 0) return null;
        return arr[UnityEngine.Random.Range(0, arr.Length)];
    }

    static bool PuntoEnPoligono(float wx, float wz, Vector2[] poly)
    {
        bool inside = false;
        int n = poly.Length;
        for (int i = 0, j = n-1; i < n; j = i++)
        {
            if (((poly[i].y > wz) != (poly[j].y > wz)) &&
                (wx < (poly[j].x - poly[i].x) * (wz - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x))
                inside = !inside;
        }
        return inside;
    }

    void CargarPosicionesOSM()
    {
        var ta = Resources.Load<TextAsset>(
            System.IO.Path.ChangeExtension(rutaJSON, null)
                .Replace("Assets/Resources/", "")
                .Replace("Assets/", ""));

        if (ta == null)
        {
            // Intentar carga directa via File.ReadAllText en editor
#if UNITY_EDITOR
            if (System.IO.File.Exists(rutaJSON))
            {
                ParseJSON(System.IO.File.ReadAllText(rutaJSON));
                return;
            }
#endif
            AlsasuaLogger.Warn("TreeStreamer",
                $"No se pudo cargar {rutaJSON}. Streaming desactivado.");
            return;
        }
        ParseJSON(ta.text);
    }

    void ParseJSON(string json)
    {
        try
        {
            var wrapper = JsonUtility.FromJson<TreesWrapper>(
                "{\"items\":" + json + "}");
            foreach (var t in wrapper.items)
                _posiciones.Add(new Vector3(t.x, t.y, t.z));
            _cargado = true;
            AlsasuaLogger.Info("TreeStreamer",
                $"{_posiciones.Count} árboles OSM cargados.");
        }
        catch (System.Exception e)
        {
            AlsasuaLogger.Error("TreeStreamer", $"Error parsing trees JSON: {e.Message}");
        }
    }

    // ── Pool ──────────────────────────────────────────────────────────────

    void PreCalentarPool()
    {
        if (treePrefabs == null || treePrefabs.Length == 0) return;
        for (int i = 0; i < tamañoPool; i++)
        {
            var prefab = treePrefabs[i % treePrefabs.Length];
            if (prefab == null) continue;
            var go = Instantiate(prefab, transform);
            go.SetActive(false);
            go.name = $"Arbol_Pool_{i}";
            _pool.Add(go);
        }
    }

    /// Pre-calienta pools de 50 instancias por cada especie específica (Roble, Pino, Ribera).
    void PreCalentarPoolEspecies()
    {
        PreCalentarPoolEspecie("Roble", prefabsRoble);
        PreCalentarPoolEspecie("Pino",  prefabsPino);
        PreCalentarPoolEspecie("Ribera",prefabsRibera);
    }

    void PreCalentarPoolEspecie(string clave, GameObject[] prefabs)
    {
        if (prefabs == null || prefabs.Length == 0) return;
        if (!_poolEspecies.ContainsKey(clave))
            _poolEspecies[clave] = new Queue<GameObject>(tamañoPoolEspecie);

        var cola = _poolEspecies[clave];
        for (int i = 0; i < tamañoPoolEspecie; i++)
        {
            var prefab = prefabs[i % prefabs.Length];
            if (prefab == null) continue;
            var go = Instantiate(prefab, transform);
            go.SetActive(false);
            go.name = $"Arbol_{clave}_Pool_{i}";
            cola.Enqueue(go);
        }
    }

    /// Clave de pool para una especie (debe coincidir con PreCalentarPoolEspecie).
    static string ClaveEspecie(int especie) => especie switch
    {
        ESP_ROBLE  => "Roble",
        ESP_PINO   => "Pino",
        ESP_RIBERA => "Ribera",
        _          => null,
    };

    GameObject AlquilarArbol(GameObject prefab, Vector3 pos, Quaternion rot, int especie = ESP_GENERICO)
    {
        GameObject go = null;

        // 1. Intentar pool de especie específica primero
        string clave = ClaveEspecie(especie);
        if (clave != null && _poolEspecies.TryGetValue(clave, out var cola))
        {
            while (cola.Count > 0)
            {
                go = cola.Dequeue();
                if (go != null) break;
            }
        }

        // 2. Fallback al pool genérico
        if (go == null)
        {
            string prefabNombre = prefab.name;
            for (int i = _pool.Count - 1; i >= 0; i--)
            {
                if (_pool[i] == null) { _pool.RemoveAt(i); continue; }
                if (!_pool[i].activeInHierarchy)
                {
                    if (_pool[i].name.Contains(prefabNombre) || go == null)
                        go = _pool[i];
                }
            }
        }

        // 3. Pool exhausto — instanciar nuevo
        if (go == null)
        {
            go = Instantiate(prefab, transform);
            _pool.Add(go);
        }

        go.transform.SetPositionAndRotation(pos, rot);
        go.SetActive(true);
        return go;
    }

    void DevolverArbol(GameObject go, int especie = ESP_GENERICO)
    {
        if (go == null) return;
        go.SetActive(false);

        // Devolver al pool de especie si corresponde
        string clave = ClaveEspecie(especie);
        if (clave != null && _poolEspecies.TryGetValue(clave, out var cola))
            cola.Enqueue(go);
        // Si es genérico ya está en _pool (sigue accesible como inactivo)
    }

    void OnDestroy()
    {
        // BUG FIX: cancelar corrutinas persistentes antes de liberar NativeArrays.
        // Si se destruye el componente mientras BucleSteaming está en yield return null,
        // Unity reiniciará la corrutina en el siguiente frame y accederá a NativeArrays
        // ya liberados → SIGSEGV / NativeArray disposed exception.
        if (_crBucleSteaming != null) StopCoroutine(_crBucleSteaming);
        if (_crInicializar   != null) StopCoroutine(_crInicializar);

        // Desuscribir del Director de Simulación (evita callback sobre objeto muerto).
        if (_orquestador != null && _onFactorCarga != null)
            _orquestador.OnFactorCargaCambia -= _onFactorCarga;
        _orquestador   = null;
        _onFactorCarga = null;

        if (_nativeInit)
        {
            if (_posicionesNative.IsCreated)  _posicionesNative.Dispose();
            if (_naPosInst.IsCreated)         _naPosInst.Dispose();
            if (_naDestruir.IsCreated)        _naDestruir.Dispose();
            if (_naPosExist.IsCreated)        _naPosExist.Dispose();
            if (_naResultadoRango.IsCreated)  _naResultadoRango.Dispose();
            if (_naPosCand.IsCreated)         _naPosCand.Dispose();
            if (_naOcupado.IsCreated)         _naOcupado.Dispose();
        }

        foreach (var go in _pool)
            if (go != null) Destroy(go);
        _pool.Clear();
    }

    // ── NativeArrays persistentes — se asignan una vez, se reutilizan cada ciclo ──
    NativeArray<float3> _posicionesNative;   // posiciones de árboles del JSON
    NativeArray<float3> _naPosInst;          // posiciones de instancias activas  (max = maxArboles)
    NativeArray<byte>   _naDestruir;         // máscara destrucción               (max = maxArboles)
    NativeArray<float3> _naPosExist;         // posiciones existentes para ocupación (max = maxArboles+1)
    NativeArray<int>    _naResultadoRango;   // resultado filtro de rango         (= posicionesNative.Length)
    NativeArray<float3> _naPosCand;          // posiciones de candidatos           (max = MAX_CANDIDATOS)
    NativeArray<byte>   _naOcupado;          // máscara ocupación                  (max = MAX_CANDIDATOS)
    bool                _nativeInit;
    const int           MAX_CANDIDATOS = 200;

    void InicializarNative()
    {
        if (_nativeInit || _posiciones.Count == 0) return;

        _posicionesNative  = new NativeArray<float3>(_posiciones.Count, Allocator.Persistent);
        for (int i = 0; i < _posiciones.Count; i++)
            _posicionesNative[i] = new float3(_posiciones[i].x, 0f, _posiciones[i].z);

        // Arrays de trabajo persistentes — tamaño máximo fijo, sin realloc en cada ciclo
        int capInst = Mathf.Max(maxArboles + 1, 1);
        _naPosInst        = new NativeArray<float3>(capInst,              Allocator.Persistent);
        _naDestruir       = new NativeArray<byte>  (capInst,              Allocator.Persistent);
        _naPosExist       = new NativeArray<float3>(capInst,              Allocator.Persistent);
        _naResultadoRango = new NativeArray<int>   (_posicionesNative.Length, Allocator.Persistent);
        _naPosCand        = new NativeArray<float3>(MAX_CANDIDATOS,        Allocator.Persistent);
        _naOcupado        = new NativeArray<byte>  (MAX_CANDIDATOS,        Allocator.Persistent);

        _nativeInit = true;
    }

    IEnumerator BucleSteaming()
    {
        while (true)
        {
            // Intervalo adaptativo: 0.5s si hay árboles a <150m, 2s para distancias mayores
            float intervalo = 2f;
            if (_jugador != null)
            {
                Vector3 posJchk = _jugador.position;
                foreach (var inst in _instancias)
                {
                    if (inst == null) continue;
                    float dx = inst.transform.position.x - posJchk.x;
                    float dz = inst.transform.position.z - posJchk.z;
                    if (dx * dx + dz * dz < 150f * 150f) { intervalo = 0.5f; break; }
                }
            }
            // Frame sobrecargado: cuadruplica el intervalo entre ciclos de streaming
            // para no instanciar nada nuevo mientras el motor recupera presupuesto.
            // Lo de DESTRUIR (FASE 1) sí sigue, porque alivia, no carga.
            if (_degradado) intervalo *= 4f;

            yield return new WaitForSeconds(intervalo);

            if (!_cargado || treePrefabs == null || treePrefabs.Length == 0) continue;

            if (_jugador == null)
            {
                var jGO = GameObject.FindGameObjectWithTag("Player");
                if (jGO != null) _jugador = jGO.transform;
                else continue;
            }

            // Inicializar NativeArray la primera vez
            if (!_nativeInit) InicializarNative();

            Vector3 posJ3 = _jugador.position;
            float3  posJ  = new float3(posJ3.x, 0f, posJ3.z);

            // ── FASE 1: Burst — marcar instancias a destruir ──────────────
            _instancias.RemoveAll(inst => inst == null);

            int nInst = _instancias.Count;
            if (nInst > 0)
            {
                // Rellenar solo los primeros nInst elementos del array persistente
                for (int i = 0; i < nInst; i++)
                {
                    var p = _instancias[i].transform.position;
                    _naPosInst[i] = new float3(p.x, 0f, p.z);
                }

                var jobDestruir = new JobMarcarArbolesADestruir
                {
                    posicionesInstancias = _naPosInst,
                    posJugador           = posJ,
                    radioDestruirSq      = radioDestroir * radioDestroir,
                    aDestruir            = _naDestruir,
                };
                jobDestruir.Schedule(nInst, 64).Complete();

                for (int i = nInst - 1; i >= 0; i--)
                    if (_naDestruir[i] == 1) { DevolverArbol(_instancias[i]); _instancias.RemoveAt(i); }
            }

            if (_instancias.Count >= maxArboles) continue;

            // ── FASE 2: Burst — filtrar posiciones en rango ───────────────
            var jobRango = new JobFiltrarArbolesEnRango
            {
                posiciones  = _posicionesNative,
                posJugador  = posJ,
                radioMin    = radioMinimo,
                radioMax    = radioVisible,
                radioMaxSq  = radioVisible * radioVisible,
                radioMinSq  = radioMinimo  * radioMinimo,
                resultado   = _naResultadoRango,
            };
            jobRango.Schedule(_posicionesNative.Length, 128).Complete();

            // ── FASE 3: Burst — comprobar ocupación (anti-duplicado) ──────
            // PERF: reutilizar _candidatosBuffer (campo) en lugar de new List<int>() cada ciclo.
            // Elimina ~1 alloc/ciclo (~0.5-2 allocations/seg) y la GC pressure acumulada. (~eliminados ~200B GC/ciclo)
            _candidatosBuffer.Clear();
            var candidatos = _candidatosBuffer;
            for (int i = 0; i < _naResultadoRango.Length && candidatos.Count < MAX_CANDIDATOS; i++)
                if (_naResultadoRango[i] >= 0) candidatos.Add(_naResultadoRango[i]);

            if (candidatos.Count == 0) continue;

            // Rellenar posiciones de instancias existentes en array persistente
            nInst = _instancias.Count;
            for (int i = 0; i < nInst; i++)
            {
                var p = _instancias[i].transform.position;
                _naPosExist[i] = new float3(p.x, 0f, p.z);
            }

            int nCand = candidatos.Count;
            for (int i = 0; i < nCand; i++)
                _naPosCand[i] = _posicionesNative[candidatos[i]];

            var jobOcup = new JobComprobarOcupacion
            {
                candidatos           = _naPosCand,
                posicionesExistentes = _naPosExist,
                radioOcupacionSq     = 9f,
                ocupado              = _naOcupado,
            };
            jobOcup.Schedule(nCand, 32).Complete();

            // ── FASE 4: Instanciar en hilo principal (required by Unity) ──
            // Degradado: tope de 1 árbol nuevo por ciclo (el ciclo además es 4× más
            // espaciado) → la carga procedural casi se pausa sin congelarse del todo.
            int presupuestoInst = _degradado ? 1 : int.MaxValue;
            int instanciados = 0;
            var terrain = Terrain.activeTerrain;
            for (int i = 0; i < nCand; i++)
            {
                if (_instancias.Count >= maxArboles) break;
                if (instanciados >= presupuestoInst) break;
                if (_naOcupado[i] == 1) continue;

                int posIdx = candidatos[i];
                var pos = _posicionesNative[posIdx];
                float y = TerrenoGlobal.AlturaMundo(pos.x, pos.z);
                if (y <= 0f && terrain == null) y = 240f;

                // Seleccionar prefab según especie clasificada
                int especie = (posIdx < _especies.Count) ? _especies[posIdx] : ESP_GENERICO;
                var prefab  = SeleccionarPrefab(especie);
                if (prefab == null) continue;

                var go = AlquilarArbol(
                    prefab,
                    new Vector3(pos.x, y, pos.z),
                    Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0));
                go.isStatic = false;
                _instancias.Add(go);
                instanciados++;

                yield return null;
            }
            // Sin Dispose — los arrays son Persistent y se reutilizan en el siguiente ciclo
        }
    }

    // ── Clases de deserialización ──────────────────────────────────────────

    [System.Serializable] class TreeEntry  { public float x, y, z; }
    [System.Serializable] class TreesWrapper { public List<TreeEntry> items; }
}

// GeoDataAlsasua movida a Assets/Scripts/GeoDataAlsasua.cs
