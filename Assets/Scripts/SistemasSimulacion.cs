// SistemasSimulacion.cs — Simulación AAA+
// SistemaTrafico · SistemaVegetacion · SistemaAtmosfera · SistemaFauna · SistemaMultitud · SistemaParanoia

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// ─────────────────────────────────────────────────────────────────────────────
//  SISTEMA TRÁFICO — vehículos reales de Alsasua (Trabant, Retro, Tractor…)
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaTrafico : MonoBehaviour
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

    IEnumerator SpawnCochesIniciales()
    {
        // Posiciones de calles desde roads_unity.json (usando RoadData si disponible)
        var terrain = Terrain.activeTerrain;
        if (terrain == null) yield break;

        // Spawn en grid alrededor del centro
        int spawned = 0;
        for (int i = 0; i < maxVehiculos * 3 && spawned < maxVehiculos; i++)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist  = Random.Range(30f, radioActivo);
            float ox = GeoDataAlsasua.OX, oz = GeoDataAlsasua.OZ;
            float wx = ox + Mathf.Cos(angle) * dist;
            float wz = oz + Mathf.Sin(angle) * dist;
            float wy = terrain.SampleHeight(new Vector3(wx, 0, wz))
                     + terrain.transform.position.y;

            var pref = (prefabCamion != null && Random.value < 0.15f) ? prefabCamion : prefabCoche;
            var go   = Instantiate(pref, new Vector3(wx, wy, wz),
                                   Quaternion.Euler(0, Random.Range(0f, 360f), 0), _parent);
            go.name = $"Coche_{spawned}";
            // Desactivar physics para props estáticos de ambiente
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>())
                rb.isKinematic = true;
            _activos.Add(go);
            spawned++;
            if (i % 5 == 0) yield return null;
        }
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
public class SistemaFauna : MonoBehaviour
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
        float ox = 1918f, oz = 8570f;

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
public class SistemaMultitud : MonoBehaviour
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
        float ox = 1918f, oz = 8570f;
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
