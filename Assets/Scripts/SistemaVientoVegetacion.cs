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

    // ── Partículas de hojas ───────────────────────────────────────────────
    // Se activan cuando el viento supera UMBRAL_HOJAS.
    // El ParticleSystem se crea en runtime — sin prefabs necesarios.
    ParticleSystem _psHojas;
    bool           _hojasActivas;
    const float    UMBRAL_HOJAS = 3.5f;
    const float    UMBRAL_STOP  = 1.8f;
    // BUG FIX #4: cachear el módulo de velocidad para evitar new MinMaxCurve cada frame.
    // ParticleSystem.VelocityOverLifetimeModule es un struct — cachear la referencia
    // no elimina el boxing, pero evitar new MinMaxCurve() sí elimina la heap alloc.
    ParticleSystem.EmissionModule           _psEmission;
    ParticleSystem.VelocityOverLifetimeModule _psVel;
    bool _psModulesCached;

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

        CrearParticulasHojas();
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

        // ── Hojas volando con el viento ──────────────────────────────────────
        ActualizarHojas();

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


    // ════════════════════════════════════════════════════════════════════════
    //  PARTÍCULAS DE HOJAS
    // ════════════════════════════════════════════════════════════════════════
    // Las hojas se crean como un ParticleSystem esférico anclado al jugador.
    // La dirección y velocidad de emisión siguen el vector de viento actual.
    // El tamaño y color varía por instancia para simular hojas de distintos
    // árboles (verde, amarillo, marrón en otoño).

    void CrearParticulasHojas()
    {
        var go = new GameObject("HojasViento");
        go.transform.SetParent(transform, false);
        _psHojas = go.AddComponent<ParticleSystem>();

        // ── Módulo main ────────────────────────────────────────────────────
        var main = _psHojas.main;
        main.loop               = true;
        main.playOnAwake        = false;
        main.maxParticles       = 200;
        main.startLifetime      = new ParticleSystem.MinMaxCurve(4f, 9f);
        main.startSpeed         = new ParticleSystem.MinMaxCurve(0.8f, 3.5f);
        main.startSize          = new ParticleSystem.MinMaxCurve(0.06f, 0.18f);
        main.gravityModifier    = 0.12f;  // caen lentamente
        main.simulationSpace    = ParticleSystemSimulationSpace.World;
        main.startColor         = new ParticleSystem.MinMaxGradient(
            new Color(0.55f, 0.45f, 0.15f),   // marrón seco
            new Color(0.30f, 0.55f, 0.12f));  // verde vivo

        // ── Módulo emission ────────────────────────────────────────────────
        var emission = _psHojas.emission;
        emission.rateOverTime = 0f; // controlado desde ActualizarHojas

        // ── Módulo shape — esfera alrededor del jugador ────────────────────
        var shape = _psHojas.shape;
        shape.enabled    = true;
        shape.shapeType  = ParticleSystemShapeType.Sphere;
        shape.radius     = 18f;
        shape.radiusThickness = 0f; // emitir solo desde la superficie

        // ── Módulo velocity over lifetime — sigue la dirección del viento ──
        var vel = _psHojas.velocityOverLifetime;
        vel.enabled = true;
        vel.space   = ParticleSystemSimulationSpace.World;
        // Los valores se actualizan en ActualizarHojas según _dirActual
        vel.x = new ParticleSystem.MinMaxCurve(1f);
        vel.y = new ParticleSystem.MinMaxCurve(0f);
        vel.z = new ParticleSystem.MinMaxCurve(0f);

        // ── Módulo size over lifetime — copos que se encogen al final ──────
        var sol = _psHojas.sizeOverLifetime;
        sol.enabled = true;
        var curve = new AnimationCurve();
        curve.AddKey(0f, 1f);
        curve.AddKey(0.8f, 0.85f);
        curve.AddKey(1f, 0f);
        sol.size = new ParticleSystem.MinMaxCurve(1f, curve);

        // ── Módulo rotation over lifetime — girar las hojas ───────────────
        var rot = _psHojas.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-180f * Mathf.Deg2Rad, 180f * Mathf.Deg2Rad);

        // Renderer: billboard facing view
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // BUG FIX #4: cachear módulos del ParticleSystem
        _psEmission = _psHojas.emission;
        _psVel      = _psHojas.velocityOverLifetime;
        _psModulesCached = true;
        AlsasuaLogger.Info("Viento", "Sistema de partículas de hojas creado.");
    }

    void ActualizarHojas()
    {
        if (_psHojas == null) return;

        // Seguir al jugador
        var jugador = AltsasuCore.Jugador;
        if (jugador != null)
            _psHojas.transform.position = jugador.position + Vector3.up * 3f;

        // BUG FIX #4: asignar constante al módulo cacheado en lugar de new MinMaxCurve.
        // ParticleSystem.VelocityOverLifetimeModule.x/y/z aceptan un float como constante
        // directamente → zero heap allocation.
        if (!_psModulesCached) { _psVel = _psHojas.velocityOverLifetime; _psModulesCached = true; }
        float speed = _fuerzaActual * 0.8f;
        // La asignación de un float a MinMaxCurve usa el constructor implícito constante
        // (ParticleSystem.MinMaxCurve(float)) que no hace heap alloc en Mono/IL2CPP.
        _psVel.x = _dirActual.x * speed;
        _psVel.z = _dirActual.z * speed;
        _psVel.y = -0.15f; // caída suave
        // Reasignar el struct de vuelta al ParticleSystem (requerido por Unity API)
        _psHojas.velocityOverLifetime = _psVel;

        // Activar/desactivar emisión por umbral
        bool debeEmitir = _fuerzaActual >= UMBRAL_HOJAS;

        if (debeEmitir && !_hojasActivas)
        {
            _hojasActivas = true;
            _psHojas.Play();
            var emission = _psHojas.emission;
            emission.rateOverTime = Mathf.Lerp(5f, 30f,
                Mathf.InverseLerp(UMBRAL_HOJAS, 8f, _fuerzaActual));
        }
        else if (!debeEmitir && _hojasActivas && _fuerzaActual < UMBRAL_STOP)
        {
            _hojasActivas = false;
            // Dejar de emitir pero que las existentes terminen su ciclo
            _psEmission.rateOverTime = 0f;
        }
        else if (_hojasActivas)
        {
            // Ajustar tasa de emisión dinámicamente
            _psEmission.rateOverTime = Mathf.Lerp(5f, 40f,
                Mathf.InverseLerp(UMBRAL_HOJAS, 9f, _fuerzaActual));
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
