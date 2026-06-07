// Assets/Scripts/SistemaVientoVegetacion.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA VIENTO → VEGETACIÓN
//
//  Conecta la fuerza/dirección de viento de SistemaClima con:
//    · Un WindZone direccional  → mueve árboles GreenForest/SpeedTree y los
//      árboles/detalle del Terrain de Unity.
//    · El "waving grass" del Terrain  → ondea la hierba.
//    · Variables globales de shader (_Wind, _WindStrength)  → para shaders
//      de vegetación custom y para que las partículas (humo, hojas) se
//      curven con el viento vía el módulo External Forces.
//
//  Antes de esto, el viento de SistemaClima sólo modificaba Physics.gravity
//  de los proyectiles: la vegetación estaba completamente estática.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[DefaultExecutionOrder(-40)]
public class SistemaVientoVegetacion : MonoBehaviour
{
    public static SistemaVientoVegetacion Instance { get; private set; }

    [Header("Ajuste de respuesta")]
    [Tooltip("Multiplicador de la fuerza de viento de SistemaClima (0-10) hacia el WindZone.")]
    public float escalaWindZone = 0.18f;
    [Tooltip("Suavizado del cambio de viento (mayor = más lento/orgánico).")]
    public float suavizado = 1.5f;

    SistemaClima _clima;
    WindZone     _windZone;
    Terrain      _terrain;

    float   _fuerzaActual;
    Vector3 _dirActual = new(1, 0, 0.3f);

    static readonly int ID_Wind         = Shader.PropertyToID("_Wind");
    static readonly int ID_WindStrength = Shader.PropertyToID("_WindStrength");

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        _clima = FindFirstObjectByType<SistemaClima>();

        // WindZone: reutilizar uno existente o crear uno direccional.
        _windZone = FindFirstObjectByType<WindZone>();
        if (_windZone == null)
        {
            var go = new GameObject("WindZone_Altsasu");
            go.transform.SetParent(transform, false);
            _windZone = go.AddComponent<WindZone>();
            _windZone.mode = WindZoneMode.Directional;
        }

        _terrain = Terrain.activeTerrain;
        if (_terrain != null && _terrain.terrainData != null)
        {
            var td = _terrain.terrainData;
            td.wavingGrassSpeed  = 0.6f;
            td.wavingGrassAmount = 0.4f;
            td.wavingGrassTint   = new Color(0.85f, 0.88f, 0.78f, 1f);
        }

        AlsasuaLogger.Info("Viento", $"Vegetación conectada al viento (WindZone={_windZone != null}, Terrain={_terrain != null}).");
    }

    void Update()
    {
        // Objetivo de viento desde el clima (si no hay clima, brisa suave por defecto)
        float fuerzaBase = _clima != null ? _clima.fuerzaViento : 1f;

        // Ráfagas: dos octavas de Perlin → el viento "respira" y nunca es plano.
        float t = Time.time;
        float rafaga = (Mathf.PerlinNoise(t * 0.25f, 0.7f) - 0.5f) * 2f      // lenta, amplia
                     + (Mathf.PerlinNoise(t * 1.3f, 3.1f) - 0.5f) * 0.6f;     // rápida, fina
        float fuerzaObj = Mathf.Max(0f, fuerzaBase + rafaga * (0.4f + fuerzaBase * 0.25f));

        // La dirección también oscila ligeramente con la ráfaga
        Vector3 dirObj  = _clima != null ? _clima.direccionViento : new Vector3(1, 0, 0.3f);
        float giro = (Mathf.PerlinNoise(t * 0.4f, 9.2f) - 0.5f) * 25f;        // ±12.5°
        dirObj = Quaternion.Euler(0, giro, 0) * dirObj;
        if (dirObj.sqrMagnitude < 0.001f) dirObj = Vector3.right;
        dirObj.y = 0f;
        dirObj.Normalize();

        // Suavizado temporal para que las ráfagas no sean instantáneas
        float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.05f, suavizado));
        _fuerzaActual = Mathf.Lerp(_fuerzaActual, fuerzaObj, k);
        _dirActual    = Vector3.Slerp(_dirActual, dirObj, k);
        if (_dirActual.sqrMagnitude < 0.001f) _dirActual = Vector3.right;

        // ── WindZone (árboles y detalle del terreno) ─────────────────────────
        if (_windZone != null)
        {
            _windZone.transform.rotation = Quaternion.LookRotation(_dirActual, Vector3.up);
            _windZone.windMain           = _fuerzaActual * escalaWindZone;
            _windZone.windTurbulence     = _fuerzaActual * escalaWindZone * 0.6f;
            _windZone.windPulseMagnitude = 0.4f + _fuerzaActual * 0.05f;
            _windZone.windPulseFrequency = 0.08f;
        }

        // ── Hierba del Terrain ───────────────────────────────────────────────
        if (_terrain != null && _terrain.terrainData != null)
        {
            var td = _terrain.terrainData;
            td.wavingGrassStrength = Mathf.Clamp01(0.1f + _fuerzaActual * 0.08f);
            td.wavingGrassSpeed    = 0.4f + _fuerzaActual * 0.06f;
        }

        // ── Variables globales de shader (vegetación custom + partículas) ────
        // xyz = dirección, w = fuerza normalizada 0-1
        Shader.SetGlobalVector(ID_Wind, new Vector4(_dirActual.x, _dirActual.y, _dirActual.z,
                                                    Mathf.Clamp01(_fuerzaActual / 10f)));
        Shader.SetGlobalFloat(ID_WindStrength, _fuerzaActual);
    }

    /// <summary>Fuerza de viento suavizada actual (0-10). La usan humo, hojas, etc.</summary>
    public float FuerzaActual => _fuerzaActual;
    /// <summary>Dirección de viento suavizada actual (normalizada, horizontal).</summary>
    public Vector3 DireccionActual => _dirActual;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
