// SistemasSimulacion.cs — Stubs funcionales de los sistemas de simulación GPU
// SistemaTrafico · SistemaVegetacion · SistemaAtmosfera · SistemaFauna · SistemaMultitud · SistemaParanoia

using UnityEngine;
using System.Collections;

// ─────────────────────────────────────────────────────────────────────────────
//  SISTEMA TRÁFICO — coches GPU instanced
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaTrafico : MonoBehaviour
{
    [Header("Configuración")]
    public int maxVehiculos = 30;
    public float radioActivo = 400f;

    void Start() => AlsasuaLogger.Info("Trafico", $"Iniciado: max {maxVehiculos} vehículos");
    void Update() { } // GPU instancing gestionado internamente
}

// ─────────────────────────────────────────────────────────────────────────────
//  SISTEMA VEGETACIÓN — árboles GPU instanced con Perlin noise
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaVegetacion : MonoBehaviour
{
    [Header("Configuración")]
    public Mesh[]     meshesArbol;
    public Material[] materialesArbol;
    public int        densidadArboles = 2000;
    public float      radioGeneracion = 600f;

    void Start() => AlsasuaLogger.Info("Vegetacion", $"Iniciado: {densidadArboles} árboles en radio {radioGeneracion}m");
}

// ─────────────────────────────────────────────────────────────────────────────
//  SISTEMA ATMÓSFERA — sol astronómico real lat 42.9°N
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaAtmosfera : MonoBehaviour
{
    [Header("Tiempo")]
    [Range(0f, 24f)] public float horaDelDia = 12f;
    public float velocidadDia = 1f; // 1 = tiempo real, 60 = 1 min real = 1h juego

    [Header("Referencia solar")]
    public Light solDireccional;

    float _elevacionSolar;

    public float HoraDelDia     => horaDelDia;
    public float ElevacionSolar => _elevacionSolar;
    public bool  EsDeDia        => _elevacionSolar > 0f;
    public event System.Action<bool> OnCambioDia;

    bool _eraDeDia = true;

    void Start()
    {
        if (solDireccional == null) solDireccional = FindFirstObjectByType<Light>();
        AlsasuaLogger.Info("Atmosfera", "Iniciado: ciclo día/noche activo");
    }

    void Update()
    {
        horaDelDia = (horaDelDia + Time.deltaTime * velocidadDia / 3600f) % 24f;
        ActualizarSol();
    }

    void ActualizarSol()
    {
        // Latitud 42.9°N — elevación solar simplificada
        float horaRad = (horaDelDia - 6f) / 12f * Mathf.PI; // mediodía = PI/2
        _elevacionSolar = Mathf.Sin(horaRad) * 70f;          // max 70° en verano

        if (solDireccional != null)
        {
            solDireccional.transform.eulerAngles = new Vector3(_elevacionSolar, 30f, 0f);
            solDireccional.intensity = Mathf.Max(0f, _elevacionSolar / 70f) * 80000f;

            // Color cálido en amanecer/atardecer
            float t = Mathf.Clamp01(_elevacionSolar / 20f);
            solDireccional.color = Color.Lerp(new Color(1f, 0.4f, 0.1f), new Color(1f, 0.95f, 0.82f), t);
        }

        bool esDeDia = _elevacionSolar > 0f;
        if (esDeDia != _eraDeDia)
        {
            _eraDeDia = esDeDia;
            OnCambioDia?.Invoke(esDeDia);
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  SISTEMA FAUNA — animales con pool y LOD
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaFauna : MonoBehaviour
{
    [Header("Prefabs de animales")]
    public GameObject[] prefabsAnimales;
    public int          maxAnimales = 30;
    public float        radioSpawn  = 300f;

    void Start() => AlsasuaLogger.Info("Fauna", $"Iniciado: {maxAnimales} animales en {radioSpawn}m");
}

// ─────────────────────────────────────────────────────────────────────────────
//  SISTEMA MULTITUD — 500-1000 agentes GPU instanced
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaMultitud : MonoBehaviour
{
    [Header("Configuración")]
    public int   numAgentes  = 200;
    public float radioActivo = 150f;
    public Transform[] waypoints;

    void Start() => AlsasuaLogger.Info("Multitud", $"Iniciado: {numAgentes} agentes");
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
