// SistemasSimulacion.cs — Simulación AAA+
// NOTA: SistemaTrafico y SistemaFauna renombrados a *Legacy para evitar
// colisión con los sistemas completos en SistemaTrafico.cs y SistemaFauna.cs.
// SistemaVegetacion · SistemaAtmosfera · SistemaMultitud · SistemaParanoia siguen activos.

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// ─────────────────────────────────────────────────────────────────────────────
//  SISTEMA TRÁFICO LEGACY — sustituido por SistemaTrafico.cs (roads + semáforos)
//  Renombrado para evitar colisión. No instanciar — usar SistemaTrafico.
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaTraficoLegacy : MonoBehaviour
{
    [Header("Configuración")]
    public int   maxVehiculos = 20;
    public float radioActivo  = 350f;
    public float velocidadMedia = 8f;  // m/s en calles residenciales

    [Header("Prefabs (auto-asignados por ConfiguradorAssetsAAA)")]
    public GameObject prefabCoche;
    public GameObject prefabCamion;

    readonly List<GameObject> _activos = new();
    Transform _parent;

    IEnumerator Start()
    {
        yield return new WaitForSeconds(3f); // esperar terreno + edificios
        _parent = new GameObject("Trafico_Coches").transform;
        _parent.SetParent(transform, false);

        // Cargar prefabs del ConfiguradorAssetsAAA si no asignados
        var cfg = ConfiguradorAssetsAAA.Instance;
        if (prefabCoche  == null) prefabCoche  = cfg?.prefabCocheCivil  ?? cfg?.prefabCocheRetro;
        if (prefabCamion == null) prefabCamion = cfg?.prefabTractor     ?? cfg?.prefabApisonadora;

        if (prefabCoche == null) { AlsasuaLogger.Warn("Trafico", "Sin prefab de coche"); yield break; }

        // Spawn inicial: coches aparcados como props en calles
        yield return StartCoroutine(SpawnCochesIniciales());

        AlsasuaLogger.Info("Trafico", $"Trafico AAA+: {_activos.Count} vehículos");
    }

    // ── Datos de carreteras (roads_unity.json) ──────────────────────────────
    [System.Serializable] class Punto { public float x, z; }
    [System.Serializable] class Road  { public long id; public string type; public bool oneway; public Punto[] points; }
    [System.Serializable] class RoadList { public Road[] items; }

    static readonly System.Collections.Generic.HashSet<string> TIPOS_CONDUCIBLES = new()
    { "motorway","trunk","primary","secondary","tertiary","unclassified","residential","living_street","service","road" };

    // MEJORA (auditoría): tráfico que CIRCULA sobre las calles reales en vez de
    // props estáticos. Carga roads_unity.json, crea waypoints por calle y asigna
    // la ruta a un VehiculoNPC (que ya sabe seguir waypoints y frenar).
    IEnumerator SpawnCochesIniciales()
    {
        string path = System.IO.Path.Combine("Assets", "AlsasuaData", "roads_unity.json");
        if (!System.IO.File.Exists(path))
        {
            AlsasuaLogger.Warn("Trafico", "roads_unity.json no encontrado; sin tráfico.");
            yield break;
        }

        RoadList rl = null;
        try { rl = JsonUtility.FromJson<RoadList>("{\"items\":" + System.IO.File.ReadAllText(path) + "}"); }
        catch (System.Exception e) { AlsasuaLogger.Warn("Trafico", "roads_unity.json ilegible: " + e.Message); yield break; }
        if (rl?.items == null) yield break;

        Vector3 centro = new(GeoDataAlsasua.OX, 0, GeoDataAlsasua.OZ);
        int spawned = 0;

        foreach (var road in rl.items)
        {
            if (spawned >= maxVehiculos) break;
            if (road?.points == null || road.points.Length < 2) continue;
            if (string.IsNullOrEmpty(road.type) || !TIPOS_CONDUCIBLES.Contains(road.type)) continue;

            // Sólo calles que pasan cerca de la zona jugable
            Vector3 p0 = GeoDataAlsasua.OSMaUnityConAltura(road.points[0].x, road.points[0].z);
            if (GeoDataAlsasua.Dist2D(p0, centro) > radioActivo * 2.5f) continue;

            // Construir waypoints a lo largo de la calle
            var rutaParent = new GameObject($"Ruta_{road.id}").transform;
            rutaParent.SetParent(_parent, false);
            var ruta = new System.Collections.Generic.List<Transform>(road.points.Length);
            foreach (var pt in road.points)
            {
                var wp = new GameObject("wp").transform;
                wp.SetParent(rutaParent, false);
                wp.position = GeoDataAlsasua.OSMaUnityConAltura(pt.x, pt.z) + Vector3.up * 0.2f;
                ruta.Add(wp);
            }
            if (ruta.Count < 2) { Destroy(rutaParent.gameObject); continue; }

            var pref = (prefabCamion != null && Random.value < 0.15f) ? prefabCamion : prefabCoche;
            var go = Instantiate(pref, ruta[0].position + Vector3.up * 0.4f, Quaternion.identity, _parent);
            go.name = $"Coche_{spawned}";
            var npc = go.GetComponent<VehiculoNPC>();
            if (npc == null) npc = go.AddComponent<VehiculoNPC>();
            npc.AsignarRuta(ruta, true);   // recorre la calle en bucle
            _activos.Add(go);
            spawned++;
            if (spawned % 4 == 0) yield return null;
        }

        AlsasuaLogger.Info("Trafico", $"Tráfico circulando: {spawned} coches sobre calles reales.");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  SISTEMA VEGETACIÓN — wrapper para PosicionadorPrecisionUrbana + GreenForest
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaVegetacion : MonoBehaviour
{
    [Header("Configuración fallback (si no hay LIDAR)")]
    public int   densidadArboles = 2000;
    public float radioGeneracion = 600f;

    void Start() => AlsasuaLogger.Info("Vegetacion",
        PosicionadorPrecisionUrbana.Instance != null
            ? "PosicionadorPrecisionUrbana gestiona la vegetación LIDAR"
            : $"Stub: {densidadArboles} árboles en radio {radioGeneracion}m");
}

// ─────────────────────────────────────────────────────────────────────────────
//  SISTEMA ATMÓSFERA — sol astronómico + audio ambiente dinámico
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaAtmosfera : MonoBehaviour
{
    [Header("Tiempo")]
    [Range(0f, 24f)] public float horaDelDia    = 10f;
    public float velocidadDia = 1f;

    [Header("Referencia solar")]
    public Light solDireccional;

    [Header("Audio ambiente (auto desde ConfiguradorAssetsAAA)")]
    public AudioClip audioAmbiente;
    public AudioClip audioAmbienteLluvia;
    public AudioClip audioAmbienteTormenta;

    AudioSource _src;
    float _elevacionSolar;
    bool  _eraDeDia = true;

    public float HoraDelDia     => horaDelDia;
    public float ElevacionSolar => _elevacionSolar;
    public bool  EsDeDia        => _elevacionSolar > 0f;
    public event System.Action<bool> OnCambioDia;

    void Start()
    {
        if (solDireccional == null)
            solDireccional = FindFirstObjectByType<Light>();

        // Auto-asignar audio desde ConfiguradorAssetsAAA
        var cfg = ConfiguradorAssetsAAA.Instance;
        if (cfg != null)
        {
            if (audioAmbiente         == null) audioAmbiente         = cfg.ambientePajaros ?? cfg.ambienteExterior;
            if (audioAmbienteLluvia   == null) audioAmbienteLluvia   = cfg.ambienteNocheRain;
            if (audioAmbienteTormenta == null) audioAmbienteTormenta = cfg.ambienteTormenta;
        }

        // Iniciar audio ambiente
        if (audioAmbiente != null)
        {
            _src = gameObject.AddComponent<AudioSource>();
            _src.clip   = audioAmbiente;
            _src.loop   = true;
            _src.volume = 0.3f;
            _src.spatialBlend = 0f;  // 2D
            _src.Play();
        }

        AlsasuaLogger.Info("Atmosfera",
            $"Iniciado: hora={horaDelDia:F1}h, audio={audioAmbiente?.name ?? "ninguno"}");
    }

    void Update()
    {
        horaDelDia = (horaDelDia + Time.deltaTime * velocidadDia / 3600f) % 24f;
        ActualizarSol();
    }

    void ActualizarSol()
    {
        float horaRad   = (horaDelDia - 6f) / 12f * Mathf.PI;
        _elevacionSolar = Mathf.Sin(horaRad) * 70f;

        if (solDireccional != null)
        {
            solDireccional.transform.eulerAngles =
                new Vector3(_elevacionSolar, 30f, 0f);
            solDireccional.intensity =
                Mathf.Max(0f, _elevacionSolar / 70f) * 80000f;

            float t = Mathf.Clamp01(_elevacionSolar / 20f);
            solDireccional.color = Color.Lerp(
                new Color(1f, 0.4f, 0.1f),
                new Color(1f, 0.95f, 0.82f), t);
        }

        bool esDeDia = _elevacionSolar > 0f;
        if (esDeDia != _eraDeDia)
        {
            _eraDeDia = esDeDia;
            OnCambioDia?.Invoke(esDeDia);
        }
    }

    /// Cambiar el clip de ambiente según el clima.
    public void SetClimaAudio(bool lluvia, bool tormenta)
    {
        if (_src == null) return;
        var cfg   = ConfiguradorAssetsAAA.Instance;
        var clip  = cfg != null
            ? cfg.GetAmbienteClima(lluvia, tormenta)
            : (tormenta ? audioAmbienteTormenta : lluvia ? audioAmbienteLluvia : audioAmbiente);
        if (clip == null || _src.clip == clip) return;
        _src.clip   = clip;
        _src.volume = tormenta ? 0.6f : lluvia ? 0.45f : 0.3f;
        _src.Play();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  SISTEMA FAUNA — lobo en periferia, pastor alemán en el pueblo
// ─────────────────────────────────────────────────────────────────────────────
// ─────────────────────────────────────────────────────────────────────────────
//  SISTEMA FAUNA LEGACY — sustituido por SistemaFauna.cs (NavMesh + quality tier)
//  Renombrado para evitar colisión. No instanciar — usar SistemaFauna.
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaFaunaLegacy : MonoBehaviour
{
    [Header("Prefabs (auto desde ConfiguradorAssetsAAA)")]
    public GameObject prefabLobo;
    public GameObject prefabPerro;
    public int maxAnimales = 8;
    public float radioSpawn = 250f;

    IEnumerator Start()
    {
        yield return new WaitForSeconds(5f);
        var cfg = ConfiguradorAssetsAAA.Instance;
        if (prefabLobo  == null) prefabLobo  = cfg?.prefabLobo;
        if (prefabPerro == null) prefabPerro = cfg?.prefabPerro;

        var terrain = Terrain.activeTerrain;
        int spawned = 0;
        float ox = GeoDataAlsasua.OX, oz = GeoDataAlsasua.OZ;

        // Lobos en la periferia (> 150m del centro)
        if (prefabLobo != null)
        {
            for (int i = 0; i < 3; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                float d = Random.Range(160f, radioSpawn);
                float wx = ox + Mathf.Cos(a) * d;
                float wz = oz + Mathf.Sin(a) * d;
                float wy = terrain != null
                    ? terrain.SampleHeight(new Vector3(wx,0,wz)) + terrain.transform.position.y
                    : 240f;
                var go = Instantiate(prefabLobo, new Vector3(wx,wy,wz),
                    Quaternion.Euler(0, Random.Range(0f,360f), 0), transform);
                go.name = $"Lobo_{i}";
                spawned++;
            }
        }

        // Perros cerca del casco urbano (< 80m del centro)
        if (prefabPerro != null)
        {
            for (int i = 0; i < 5; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                float d = Random.Range(20f, 80f);
                float wx = ox + Mathf.Cos(a) * d;
                float wz = oz + Mathf.Sin(a) * d;
                float wy = terrain != null
                    ? terrain.SampleHeight(new Vector3(wx,0,wz)) + terrain.transform.position.y
                    : 240f;
                var go = Instantiate(prefabPerro, new Vector3(wx,wy,wz),
                    Quaternion.Euler(0, Random.Range(0f,360f), 0), transform);
                go.name = $"Perro_{i}";
                spawned++;
            }
        }

        AlsasuaLogger.Info("Fauna", $"Fauna AAA+: {spawned} animales (Wolf={prefabLobo!=null}, Dog={prefabPerro!=null})");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  SISTEMA MULTITUD — NPCs MeshyAI caminando por el pueblo
// ─────────────────────────────────────────────────────────────────────────────
// Legacy — sustituido por SistemaSpawnCiviles.cs (pool + NavMesh + QualityTier + Director)
public class SistemaMultitudLegacy : MonoBehaviour
{
    [Header("Configuración")]
    public int   numAgentes   = 40;
    public float radioActivo  = 120f;

    [Header("Prefabs (auto desde ConfiguradorAssetsAAA)")]
    public GameObject   prefabNPC;
    public GameObject[] prefabsNPC;

    readonly List<GameObject> _npcs = new();
    Transform _parent;

    IEnumerator Start()
    {
        yield return new WaitForSeconds(6f); // después del terreno, edificios y calles
        _parent = new GameObject("Multitud_NPCs").transform;
        _parent.SetParent(transform, false);

        var cfg = ConfiguradorAssetsAAA.Instance;
        if (prefabNPC  == null) prefabNPC  = cfg?.GetPrefabCivil();
        if (prefabsNPC == null || prefabsNPC.Length == 0) prefabsNPC = cfg?.prefabsCivil;

        if (prefabNPC == null && (prefabsNPC == null || prefabsNPC.Length == 0))
        {
            AlsasuaLogger.Warn("Multitud", "Sin prefabs NPC");
            yield break;
        }

        var terrain = Terrain.activeTerrain;
        float ox = GeoDataAlsasua.OX, oz = GeoDataAlsasua.OZ;
        int spawned = 0;

        for (int i = 0; i < numAgentes * 3 && spawned < numAgentes; i++)
        {
            float a  = Random.Range(0f, Mathf.PI * 2f);
            float d  = Random.Range(10f, radioActivo);
            float wx = ox + Mathf.Cos(a) * d;
            float wz = oz + Mathf.Sin(a) * d;
            float wy = terrain != null
                ? terrain.SampleHeight(new Vector3(wx, 0, wz)) + terrain.transform.position.y
                : 540f;

            // Alternar entre prefabs disponibles
            GameObject pref = prefabsNPC?.Length > 0
                ? prefabsNPC[spawned % prefabsNPC.Length]
                : prefabNPC;
            if (pref == null) continue;

            var go = Instantiate(pref, new Vector3(wx, wy, wz),
                Quaternion.Euler(0, Random.Range(0f, 360f), 0), _parent);
            go.name = $"NPC_{spawned}_{pref.name}";
            // Añadir NavMeshAgent para movimiento
            if (go.GetComponent<UnityEngine.AI.NavMeshAgent>() == null)
            {
                var nav = go.AddComponent<UnityEngine.AI.NavMeshAgent>();
                nav.speed        = Random.Range(0.8f, 1.4f);
                nav.radius       = 0.3f;
                nav.height       = 1.8f;
                nav.stoppingDistance = 0.3f;
            }
            _npcs.Add(go);
            spawned++;
            if (spawned % 8 == 0) yield return null;
        }

        AlsasuaLogger.Info("Multitud", $"Multitud AAA+: {spawned} NPCs MeshyAI");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  SISTEMA PARANOIA — nivel de paranoia global independiente
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaParanoia : MonoBehaviour
{
    public static SistemaParanoia Instance { get; private set; }
    [Range(0f, 100f)] public float paranoia = 0f;

    void Awake() { if (Instance && Instance != this) { Destroy(this); return; } Instance = this; }

    public void SumarParanoia(float v) => paranoia = Mathf.Clamp(paranoia + v, 0f, 100f);
    public void RestarParanoia(float v) => paranoia = Mathf.Clamp(paranoia - v, 0f, 100f);
}
