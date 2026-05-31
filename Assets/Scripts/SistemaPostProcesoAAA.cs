// Assets/Scripts/SistemaPostProcesoAAA.cs
// ═══════════════════════════════════════════════════════════════════════════
//  POST-PROCESADO DINÁMICO AAA — Alsasua GTA
//
//  Ajusta el perfil de post-procesado HDRP en tiempo real según:
//   · Hora del día   (amanecer cálido / mediodía neutro / noche azul)
//   · Estado del clima (lluvia fría / sol cálido / tormenta oscura)
//   · Eventos de gameplay (daño → viñeta roja / explosión → destello naranja)
//   · Nivel de paranoia (alta paranoia → desaturación + viñeta morada)
//
//  Requiere: Volume en escena con un VolumeProfile HDRP asignado.
//  DefaultExecutionOrder(-80) — después de SistemaAtmosfera (-50) y antes de todos.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

[DefaultExecutionOrder(-80)]
public class SistemaPostProcesoAAA : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────
    public static SistemaPostProcesoAAA Instance { get; private set; }

    // ── Referencias ───────────────────────────────────────────────────────
    [Header("Referencias")]
    public Volume volumenGlobal;

    // ── Perfiles de clima (se generan automáticamente si son null) ────────
    [Header("Configuración base")]
    [Range(0f, 2f)] public float intensidadBloom    = 0.25f;
    [Range(0f, 4f)] public float intensidadSSAO     = 1.5f;
    [Range(0f, 1f)] public float intensidadViñeta   = 0.22f;
    [Range(0f, 1f)] public float intensidadGrano    = 0.07f;

    // ── Estado interno ────────────────────────────────────────────────────
    VolumeProfile _perfil;

    // Overrides cacheados
    Tonemapping             _tone;
    ColorAdjustments        _color;
    Bloom                   _bloom;
    Vignette                _vignet;
    DepthOfField            _dof;
    MotionBlur              _mb;
    FilmGrain               _grain;
    ScreenSpaceAmbientOcclusion        _ssao;
    Fog                     _fog;
    ShadowsMidtonesHighlights _smh;

    // Estado de gameplay
    float _dañoTimer;      // 0-1 intensidad del flash de daño
    float _explTimer;      // flash de explosión
    float _paranoiaLevel;

    // Referencias a otros sistemas
    SistemaAtmosfera     _atmos;
    SistemaClima         _clima;
    SistemaApoyoPopular  _apoyo;

    // Targets suaves (lerp hacia ellos)
    float _targetExposure    = 0f;
    float _targetContrast    = 10f;
    float _targetSaturation  = 5f;
    Color _targetColorFilter = Color.white;
    float _targetBloom       = 0.25f;
    float _targetFog         = 600f;

    // =========================================================================
    //  LIFECYCLE
    // =========================================================================

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        BuscarOCrearVolumen();
        ObtenerOverrides();
        _atmos = FindFirstObjectByType<SistemaAtmosfera>();
        _clima = FindFirstObjectByType<SistemaClima>();
        _apoyo = SistemaApoyoPopular.Instance;

        AplicarBase();
    }

    void Update()
    {
        _atmos ??= FindFirstObjectByType<SistemaAtmosfera>();
        _clima ??= FindFirstObjectByType<SistemaClima>();
        _apoyo ??= SistemaApoyoPopular.Instance;

        ActualizarPorHora();
        ActualizarPorClima();
        ActualizarPorGameplay();
        AplicarSuave();
    }

    // =========================================================================
    //  SETUP
    // =========================================================================

    void BuscarOCrearVolumen()
    {
        volumenGlobal ??= FindFirstObjectByType<Volume>();
        if (volumenGlobal == null)
        {
            var go = new GameObject("PP_Volume_Global");
            volumenGlobal = go.AddComponent<Volume>();
            volumenGlobal.isGlobal = true;
            volumenGlobal.priority = 10f;
        }

        if (volumenGlobal.profile == null)
            volumenGlobal.profile = ScriptableObject.CreateInstance<VolumeProfile>();

        _perfil = volumenGlobal.profile;
    }

    void ObtenerOverrides()
    {
        if (!_perfil.TryGet(out _tone))    _tone  = _perfil.Add<Tonemapping>(true);
        if (!_perfil.TryGet(out _color))   _color = _perfil.Add<ColorAdjustments>(true);
        if (!_perfil.TryGet(out _bloom))   _bloom = _perfil.Add<Bloom>(true);
        if (!_perfil.TryGet(out _vignet))  _vignet= _perfil.Add<Vignette>(true);
        if (!_perfil.TryGet(out _dof))     _dof   = _perfil.Add<DepthOfField>(true);
        if (!_perfil.TryGet(out _mb))      _mb    = _perfil.Add<MotionBlur>(true);
        if (!_perfil.TryGet(out _grain))   _grain = _perfil.Add<FilmGrain>(true);
        if (!_perfil.TryGet(out _ssao))    _ssao  = _perfil.Add<ScreenSpaceAmbientOcclusion>(true);
        if (!_perfil.TryGet(out _fog))     _fog   = _perfil.Add<Fog>(true);
        if (!_perfil.TryGet(out _smh))     _smh   = _perfil.Add<ShadowsMidtonesHighlights>(true);
    }

    void AplicarBase()
    {
        // ACES Tonemapping — estándar GTA V / RDR2 / Cyberpunk
        _tone.mode.overrideState = true;
        _tone.mode.value = TonemappingMode.ACES;

        // SSAO
        _ssao.intensity.overrideState = true;
        _ssao.intensity.value = intensidadSSAO;
        _ssao.radius.overrideState = true;
        _ssao.radius.value = 0.4f;

        // Bloom suave (no HDR agresivo)
        _bloom.threshold.overrideState = true; _bloom.threshold.value = 0.85f;
        _bloom.scatter.overrideState   = true; _bloom.scatter.value   = 0.6f;
        _bloom.tint.overrideState      = true; _bloom.tint.value      = new Color(1f, 0.96f, 0.87f);

        // Vignette
        _vignet.intensity.overrideState  = true; _vignet.intensity.value  = intensidadViñeta;
        _vignet.smoothness.overrideState = true; _vignet.smoothness.value = 0.45f;
        _vignet.rounded.overrideState    = true; _vignet.rounded.value    = true;

        // Film Grain
        _grain.type.overrideState       = true; _grain.type.value       = FilmGrainLookup.Thin1;
        _grain.intensity.overrideState  = true; _grain.intensity.value  = intensidadGrano;
        _grain.response.overrideState   = true; _grain.response.value   = 0.8f;

        // Motion Blur suave
        _mb.intensity.overrideState = true; _mb.intensity.value = 0.2f;

        // Niebla volumétrica — valle vasco
        _fog.enabled.overrideState     = true; _fog.enabled.value     = true;
        _fog.meanFreePath.overrideState= true; _fog.meanFreePath.value= 600f;
        _fog.baseHeight.overrideState  = true; _fog.baseHeight.value  = 250f;
        _fog.maximumHeight.overrideState=true; _fog.maximumHeight.value=700f;
        _fog.albedo.overrideState      = true; _fog.albedo.value      = new Color(0.82f, 0.85f, 0.90f);

        // Sombras / Medios / Luces (grado cinematográfico base)
        _smh.shadows.overrideState     = true; _smh.shadows.value     = new Vector4(0.96f, 0.97f, 1.02f, 0f);
        _smh.midtones.overrideState    = true; _smh.midtones.value    = new Vector4(1.0f,  0.99f, 0.97f, 0f);
        _smh.highlights.overrideState  = true; _smh.highlights.value  = new Vector4(1.02f, 1.0f,  0.95f, 0f);

        // DoF sutil (enfoca a 20m)
        _dof.focusMode.overrideState   = true; _dof.focusMode.value   = DepthOfFieldMode.Manual;
        _dof.nearFocusStart.overrideState = true; _dof.nearFocusStart.value = 0f;
        _dof.nearFocusEnd.overrideState   = true; _dof.nearFocusEnd.value   = 0.5f;
        _dof.farFocusStart.overrideState  = true; _dof.farFocusStart.value  = 80f;
        _dof.farFocusEnd.overrideState    = true; _dof.farFocusEnd.value    = 200f;
    }

    // =========================================================================
    //  ACTUALIZACIÓN POR HORA DEL DÍA
    // =========================================================================

    void ActualizarPorHora()
    {
        float hora = _atmos != null ? _atmos.HoraDelDia : 12f;

        // Amanecer (5-8h): cálido, rosado, baja exposición
        if (hora >= 5f && hora < 8f)
        {
            float t = Mathf.InverseLerp(5f, 8f, hora);
            _targetExposure    = Mathf.Lerp(-1.2f, 0f, t);
            _targetSaturation  = Mathf.Lerp(-10f, 5f, t);
            _targetColorFilter = Color.Lerp(new Color(1f, 0.7f, 0.5f), new Color(1f, 0.97f, 0.93f), t);
            _targetFog         = Mathf.Lerp(250f, 600f, t); // más niebla al amanecer
        }
        // Mediodía (10-15h): neutro, limpio
        else if (hora >= 10f && hora < 15f)
        {
            _targetExposure    = 0.15f;
            _targetSaturation  = 8f;
            _targetColorFilter = new Color(1f, 0.98f, 0.95f);
            _targetFog         = 700f;
        }
        // Atardecer (17-20h): naranja/dorado
        else if (hora >= 17f && hora < 20f)
        {
            float t = Mathf.InverseLerp(17f, 20f, hora);
            _targetExposure    = Mathf.Lerp(0.1f, -0.6f, t);
            _targetSaturation  = Mathf.Lerp(10f, 2f, t);
            _targetColorFilter = Color.Lerp(new Color(1f, 0.82f, 0.55f), new Color(0.6f, 0.4f, 0.7f), t);
            _targetFog         = Mathf.Lerp(700f, 350f, t);
        }
        // Noche (21-4h): azul, oscuro
        else if (hora >= 21f || hora < 5f)
        {
            _targetExposure    = -1.8f;
            _targetSaturation  = -15f;
            _targetColorFilter = new Color(0.7f, 0.8f, 1.0f);
            _targetFog         = 300f;
        }
        // Transiciones (8-10, 15-17, 20-21)
        else
        {
            _targetExposure    = 0f;
            _targetSaturation  = 5f;
            _targetColorFilter = Color.white;
            _targetFog         = 600f;
        }
    }

    // =========================================================================
    //  ACTUALIZACIÓN POR CLIMA
    // =========================================================================

    void ActualizarPorClima()
    {
        if (_clima == null) return;

        switch (_clima.climaActual)
        {
            case SistemaClima.EstadoClima.Sol:
                _targetBloom       = 0.35f;
                _targetSaturation += 5f;
                break;

            case SistemaClima.EstadoClima.LluviaLigera:
                _targetSaturation -= 10f;
                _targetColorFilter = Color.Lerp(_targetColorFilter, new Color(0.85f, 0.88f, 0.95f), 0.4f);
                _targetFog        *= 0.6f;
                _targetBloom       = 0.15f;
                break;

            case SistemaClima.EstadoClima.Tormenta:
                _targetSaturation -= 20f;
                _targetExposure   -= 0.4f;
                _targetColorFilter = Color.Lerp(_targetColorFilter, new Color(0.75f, 0.80f, 0.88f), 0.6f);
                _targetFog        *= 0.35f;
                _targetBloom       = 0.1f;
                break;

            case SistemaClima.EstadoClima.Niebla:
                _targetFog        *= 0.2f;
                _targetSaturation -= 15f;
                _targetBloom       = 0.08f;
                break;

            case SistemaClima.EstadoClima.NieveLigera:
                _targetSaturation -= 8f;
                _targetColorFilter = Color.Lerp(_targetColorFilter, Color.white, 0.3f);
                _targetBloom       = 0.2f;
                break;

            default:
                _targetBloom = intensidadBloom;
                break;
        }
    }

    // =========================================================================
    //  ACTUALIZACIÓN POR GAMEPLAY
    // =========================================================================

    void ActualizarPorGameplay()
    {
        // Paranoia
        if (_apoyo != null)
        {
            float p = _apoyo.paranoia / 100f;
            _paranoiaLevel = Mathf.Lerp(_paranoiaLevel, p, Time.deltaTime * 2f);
            if (_paranoiaLevel > 0.6f)
            {
                _targetSaturation -= _paranoiaLevel * 20f;
                _vignet.color.overrideState = true;
                _vignet.color.value = Color.Lerp(Color.black, new Color(0.5f, 0f, 0.5f), (_paranoiaLevel - 0.6f) / 0.4f);
            }
            else
            {
                _vignet.color.overrideState = false;
            }
        }

        // Flash de daño (rojo)
        if (_dañoTimer > 0f)
        {
            _dañoTimer -= Time.deltaTime * 3f;
            float d = Mathf.Clamp01(_dañoTimer);
            _vignet.intensity.value = Mathf.Max(intensidadViñeta, d * 0.7f);
            _vignet.color.overrideState = true;
            _vignet.color.value = Color.Lerp(Color.black, new Color(0.8f, 0f, 0f), d);
        }
        else
        {
            _vignet.intensity.value = intensidadViñeta;
        }

        // Flash de explosión (naranja destello momentáneo)
        if (_explTimer > 0f)
        {
            _explTimer -= Time.deltaTime * 4f;
            _color.postExposure.value = Mathf.Lerp(_targetExposure, _targetExposure + 2f, _explTimer);
        }
    }

    // =========================================================================
    //  APLICAR SUAVE (lerp)
    // =========================================================================

    void AplicarSuave()
    {
        float dt = Time.deltaTime;
        float sp = dt * 1.5f; // velocidad de transición (1.5 unidades/seg)

        // Color Adjustments
        _color.postExposure.overrideState  = true;
        _color.contrast.overrideState      = true;
        _color.saturation.overrideState    = true;
        _color.colorFilter.overrideState   = true;

        _color.postExposure.value  = Mathf.Lerp(_color.postExposure.value,  _targetExposure,    sp);
        _color.contrast.value      = Mathf.Lerp(_color.contrast.value,      _targetContrast,    sp);
        _color.saturation.value    = Mathf.Lerp(_color.saturation.value,    Mathf.Clamp(_targetSaturation, -30f, 20f), sp);
        _color.colorFilter.value   = Color.Lerp(_color.colorFilter.value,   _targetColorFilter, sp);

        // Bloom
        _bloom.intensity.overrideState = true;
        _bloom.intensity.value = Mathf.Lerp(_bloom.intensity.value, _targetBloom, sp);

        // Niebla
        _fog.meanFreePath.value = Mathf.Lerp(_fog.meanFreePath.value, Mathf.Max(80f, _targetFog), sp * 0.5f);
    }

    // =========================================================================
    //  API PÚBLICA
    // =========================================================================

    public static void FlashDaño(float intensidad = 1f)
    {
        if (Instance != null) Instance._dañoTimer = intensidad;
    }

    public static void FlashExplosion()
    {
        if (Instance != null) Instance._explTimer = 1f;
    }

    public static void SetDoFTarget(Transform objetivo, bool activo)
    {
        if (Instance == null || Instance._dof == null) return;
        Instance._dof.focusMode.value = activo ? DepthOfFieldMode.UsePhysicalCamera : DepthOfFieldMode.Manual;
    }

    static new T FindFirstObjectByType<T>() where T : UnityEngine.Object
        => UnityEngine.Object.FindFirstObjectByType<T>();
}
