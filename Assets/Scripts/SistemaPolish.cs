// Assets/Scripts/SistemaPolish.cs
// ═══════════════════════════════════════════════════════════════════════════
//  POLISH & FEEL — efectos de cámara y feedback visual AAA
//
//  • Screen shake paramétrico (trauma decay)
//  • Aberración cromática al recibir daño
//  • Blur de velocidad (Motion Blur dinámico en HDRP)
//  • Slow motion al entrar en vehículo
//  • Vignette de daño (rojo pulsante)
//  • Flash de sirena en wanted alto
//  • Zoom de cámara al apuntar
//  • Hit stop (freeze frame 2-3 frames en impacto crítico)
//
//  Uso: SistemaPolish.Shake(intensidad); SistemaPolish.FlashDano();
//  Singleton accedido via instancia estática.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

[DefaultExecutionOrder(50)]
public class SistemaPolish : MonoBehaviour
{
    public static SistemaPolish I { get; private set; }

    // ── HDRP Volume ───────────────────────────────────────────────────────
    Volume          _volume;
    ChromaticAberration _chromatic;
    Vignette        _vignette;
    MotionBlur      _motionBlur;
    LensDistortion  _lensDistortion;
    Bloom           _bloom;

    // ── Screen shake (trauma system) ──────────────────────────────────────
    float  _trauma;          // 0-1, decae con el tiempo
    float  _shakeMaxAngle = 3f;
    float  _shakeMaxOffset = 0.12f;
    float  _shakeSeed;
    Camera _cam;
    Vector3 _camPosOrigen;
    Quaternion _camRotOrigen;

    // ── Aberración cromática ───────────────────────────────────────────────
    float _chromaticTarget;
    float _chromaticCurrent;

    // ── Vignette de daño ──────────────────────────────────────────────────
    float _vignetteIntensTarget;
    float _vignetteIntensBase   = 0.22f;
    Color _vignetteColorBase    = new Color(0.05f, 0.03f, 0.03f);
    Color _vignetteColorDano    = new Color(0.8f,  0.05f, 0.05f);

    // ── Slow motion ───────────────────────────────────────────────────────
    float _timeScaleTarget = 1f;
    float _timeScaleVel;

    // ── Hit stop ──────────────────────────────────────────────────────────
    float _hitStopTimer;

    // ── Flash sirena ─────────────────────────────────────────────────────
    float _sirenTimer;
    bool  _sirenActive;
    Light _sirenLight;

    // ── Blur de velocidad ─────────────────────────────────────────────────
    float _velocidadActual;

    // ════════════════════════════════════════════════════════════════════════
    //  BOOT
    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (I != null && I != this) { Destroy(this); return; }
        I = this;
        _shakeSeed = Random.Range(0f, 100f);
    }

    void Start()
    {
        _cam = Camera.main;
        AplicarConfigGraficos();
        InicializarVolume();
        InicializarSirena();
        SuscribirEventos();

        // ── Sistemas visuales AAA adicionales ─────────────────────────────
        // SistemaVolumenHDRP se gestiona solo (DefaultExecutionOrder -80)
        // ConversorMaterialesHDRP y OptimizadorVisualHDRP también son auto-singletons.
        // SistemaPolish garantiza que el Volume de polish coexiste con el Volume HDRP
        // ajustando la prioridad para no pisar los efectos de atmósfera.
        if (_volume != null) _volume.priority = 5f; // más bajo que SistemaVolumenHDRP (10/11)
    }

    static void AplicarConfigGraficos()
    {
        RenderSettings.fog        = true;
        RenderSettings.fogDensity = 0.0012f;
        RenderSettings.fogColor   = new Color(0.72f, 0.75f, 0.80f);
        RenderSettings.fogMode    = FogMode.ExponentialSquared;
        QualitySettings.shadowDistance  = 300f;
        QualitySettings.shadowCascades  = 4;
        QualitySettings.lodBias         = 2.5f;
        QualitySettings.maximumLODLevel = 0;
    }

    void InicializarVolume()
    {
        // Buscar volume global o crear uno
        _volume = FindFirstObjectByType<Volume>();
        if (_volume == null)
        {
            var go = new GameObject("PostProcesoPolish");
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.profile  = ScriptableObject.CreateInstance<VolumeProfile>();
        }

        var profile = _volume.profile;
        if (!profile.TryGet(out _chromatic))    _chromatic    = profile.Add<ChromaticAberration>(true);
        if (!profile.TryGet(out _vignette))     _vignette     = profile.Add<Vignette>(true);
        if (!profile.TryGet(out _motionBlur))   _motionBlur   = profile.Add<MotionBlur>(true);
        if (!profile.TryGet(out _lensDistortion)) _lensDistortion = profile.Add<LensDistortion>(true);
        if (!profile.TryGet(out _bloom))        _bloom        = profile.Add<Bloom>(true);

        // Valores base
        _chromatic.intensity.Override(0f);
        _vignette.intensity.Override(_vignetteIntensBase);
        _vignette.color.Override(_vignetteColorBase);
        _motionBlur.intensity.Override(0f);
        _motionBlur.sampleCount = 8;
        _lensDistortion.intensity.Override(0f);
        _bloom.intensity.Override(0.6f);
    }

    void InicializarSirena()
    {
        var go = new GameObject("LuzSirena");
        go.transform.SetParent(transform);
        _sirenLight = go.AddComponent<Light>();
        _sirenLight.type      = LightType.Point;
        _sirenLight.range     = 40f;
        _sirenLight.intensity = 0f;
        _sirenLight.color     = Color.blue;
        _sirenLight.shadows   = LightShadows.None;
        go.SetActive(false);
    }

    void SuscribirEventos()
    {
        ControladorJugador.OnDanoRecibido     += OnJugadorDano;
        GameManagerAltsasua.OnEstrellasCambia += OnWantedCambia;
        ControladorVehiculoJugador.OnJugadorEntro += OnEntroVehiculo;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  UPDATE
    // ════════════════════════════════════════════════════════════════════════

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        ActualizarShake(dt);
        ActualizarChromatic(dt);
        ActualizarVignette(dt);
        ActualizarTimeScale(dt);
        ActualizarHitStop(dt);
        ActualizarSirena(dt);
        ActualizarMotionBlur();
    }

    // ── Screen shake ──────────────────────────────────────────────────────

    void ActualizarShake(float dt)
    {
        if (_cam == null || _trauma <= 0f) return;

        _trauma = Mathf.MoveTowards(_trauma, 0f, dt * 1.8f);
        float s = _trauma * _trauma; // cuadrático — más suave

        float t = Time.unscaledTime * 24f + _shakeSeed;
        float ox = (Mathf.PerlinNoise(t,       0f) * 2f - 1f) * _shakeMaxOffset * s;
        float oy = (Mathf.PerlinNoise(t + 10f, 0f) * 2f - 1f) * _shakeMaxOffset * s;
        float ra = (Mathf.PerlinNoise(t + 20f, 0f) * 2f - 1f) * _shakeMaxAngle  * s;

        _cam.transform.localPosition = _camPosOrigen + new Vector3(ox, oy, 0f);
        var euler = _camRotOrigen.eulerAngles;
        _cam.transform.localRotation = Quaternion.Euler(euler.x, euler.y, euler.z + ra);
    }

    // ── Chromatic aberration ───────────────────────────────────────────────

    void ActualizarChromatic(float dt)
    {
        _chromaticCurrent = Mathf.MoveTowards(_chromaticCurrent, _chromaticTarget, dt * 4f);
        _chromaticTarget  = Mathf.MoveTowards(_chromaticTarget,  0f,               dt * 3f);
        if (_chromatic != null) _chromatic.intensity.Override(_chromaticCurrent);
    }

    // ── Vignette ──────────────────────────────────────────────────────────

    void ActualizarVignette(float dt)
    {
        // Pulso rojo al recibir daño
        _vignetteIntensTarget = Mathf.MoveTowards(_vignetteIntensTarget, _vignetteIntensBase, dt * 2.5f);
        float intens  = Mathf.Lerp(_vignetteIntensBase, _vignetteIntensTarget,
                        Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f)) * 0.3f + 0.7f);
        float t = Mathf.InverseLerp(_vignetteIntensBase, 0.7f, _vignetteIntensTarget);
        Color col = Color.Lerp(_vignetteColorBase, _vignetteColorDano, t);
        if (_vignette != null)
        {
            _vignette.intensity.Override(intens);
            _vignette.color.Override(col);
        }
    }

    // ── Time scale ────────────────────────────────────────────────────────

    void ActualizarTimeScale(float dt)
    {
        if (_hitStopTimer > 0f) return; // hitStop tiene prioridad
        Time.timeScale = Mathf.SmoothDamp(Time.timeScale, _timeScaleTarget, ref _timeScaleVel, 0.12f, 10f, dt);
        if (Mathf.Abs(Time.timeScale - _timeScaleTarget) < 0.01f) Time.timeScale = _timeScaleTarget;
    }

    // ── Hit stop ──────────────────────────────────────────────────────────

    void ActualizarHitStop(float dt)
    {
        if (_hitStopTimer <= 0f) return;
        _hitStopTimer -= dt;
        if (_hitStopTimer <= 0f) Time.timeScale = _timeScaleTarget;
    }

    // ── Sirena ────────────────────────────────────────────────────────────

    void ActualizarSirena(float dt)
    {
        if (!_sirenActive || _sirenLight == null) return;
        _sirenTimer += dt * 3f;
        float pulse = Mathf.Abs(Mathf.Sin(_sirenTimer * Mathf.PI));
        _sirenLight.intensity = pulse * 3000f; // HDRP usa lux
        _sirenLight.color     = (_sirenTimer % 2f) < 1f ? Color.blue : Color.red;
        // Seguir al jugador
        var j = AltsasuCore.Jugador;
        if (j != null) _sirenLight.transform.position = j.position + Vector3.up * 8f;
    }

    // ── Motion blur dinámico ──────────────────────────────────────────────

    void ActualizarMotionBlur()
    {
        if (_motionBlur == null) return;
        var coche = FindFirstObjectByType<ControladorVehiculoJugador>();
        if (coche != null && coche.JugadorDentro)
        {
            var rb = coche.GetComponent<Rigidbody>();
            float spd = rb != null ? rb.linearVelocity.magnitude : 0f;
            float blur = Mathf.InverseLerp(10f, 60f, spd) * 0.35f;
            _motionBlur.intensity.Override(blur);
        }
        else
        {
            _motionBlur.intensity.Override(0f);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  API ESTÁTICA — llamada desde otros sistemas
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Sacudida de cámara. intensidad 0-1 (0.2=disparo, 0.5=explosión cercana, 1=explosión directa)</summary>
    public static void Shake(float intensidad)
    {
        if (I == null) return;
        I._trauma = Mathf.Clamp01(I._trauma + intensidad);
        // Guardar posición origen en el primer shake del frame
        if (I._cam != null)
        {
            I._camPosOrigen = I._cam.transform.localPosition;
            I._camRotOrigen = I._cam.transform.localRotation;
        }
    }

    /// <summary>Flash de aberración cromática (daño recibido).</summary>
    public static void FlashDano(float intensidad = 1f)
    {
        if (I == null) return;
        I._chromaticTarget       = Mathf.Clamp01(intensidad);
        I._vignetteIntensTarget  = Mathf.Lerp(0.4f, 0.75f, intensidad);
        Shake(intensidad * 0.3f);
    }

    /// <summary>Freeze frame — pausa el juego 2-4 frames (impacto crítico).</summary>
    public static void HitStop(float duracion = 0.06f)
    {
        if (I == null) return;
        Time.timeScale  = 0.05f;
        I._hitStopTimer = duracion;
    }

    /// <summary>Slow motion al entrar en vehículo.</summary>
    void OnEntroVehiculo(ControladorVehiculoJugador _) => SlowMoEntradaVehiculo();

    public static void SlowMoEntradaVehiculo()
    {
        if (I == null) return;
        I._timeScaleTarget = 0.35f;
        // Volver a normal después de 0.6 s (real time)
        I.Invoke(nameof(RestaurarTimeScale), 0.6f);
        I._lensDistortion?.intensity.Override(-0.15f);
        I.Invoke(nameof(RestaurarLens), 0.5f);
    }

    void RestaurarTimeScale() => _timeScaleTarget = 1f;
    void RestaurarLens() => _lensDistortion?.intensity.Override(0f);

    /// <summary>
    /// Activa modo tormenta (niebla densa, HDRI tormenta).
    /// Llama al SistemaVolumenHDRP si está disponible.
    /// </summary>
    public static void SetTormenta(bool activo)
    {
        SistemaVolumenHDRP.SetTormenta(activo);
    }

    /// <summary>
    /// Activa Depth of Field de francotirador.
    /// Llama al SistemaVolumenHDRP si está disponible.
    /// </summary>
    public static void SetDoFSniper(bool activo, float focusDist = 40f)
    {
        SistemaVolumenHDRP.SetDoFSniper(activo, focusDist);
    }

    /// <summary>Activa/desactiva la luz de sirena (wanted ≥ 2).</summary>
    public static void SetSirena(bool activo)
    {
        if (I == null || I._sirenLight == null) return;
        I._sirenActive = activo;
        I._sirenLight.gameObject.SetActive(activo);
        if (!activo) I._sirenLight.intensity = 0f;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CALLBACKS DE EVENTOS
    // ════════════════════════════════════════════════════════════════════════

    void OnJugadorDano(int cantidad)
    {
        float norm = Mathf.Clamp01(cantidad / 30f);
        FlashDano(norm);
        Shake(norm * 0.25f);
        if (cantidad >= 25) HitStop(0.05f);
    }

    void OnWantedCambia(int nivel)
    {
        SetSirena(nivel >= 2);
        if (nivel >= 3) Shake(0.15f);
    }

    void OnDestroy()
    {
        ControladorJugador.OnDanoRecibido     -= OnJugadorDano;
        GameManagerAltsasua.OnEstrellasCambia -= OnWantedCambia;
        ControladorVehiculoJugador.OnJugadorEntro -= OnEntroVehiculo;
    }
}
