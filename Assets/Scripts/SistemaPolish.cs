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
//  [NUEVO] Auto-focus DoF — raycast al centro de pantalla cada 0.2 s
//  [NUEVO] Sprint blur    — LensDistortion leve al correr
//  [NUEVO] Explosion bloom burst — boost temporal de bloom en explosión
//  [NUEVO] Rain screen    — aberración cromática + distorsión al llover
//  [NUEVO] Mouse chromatic — aberración proporcional a velocidad del ratón
//  [NUEVO] Aiming vignette — vignette oscura al apuntar con RMB
//  [NUEVO] Explosion light — pool de 4 luces de punto en explosiones
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
    Light                 _sirenLight;
    HDAdditionalLightData _hdSiren;

    // ── Blur de velocidad ─────────────────────────────────────────────────
    float _velocidadActual;

    // ── Auto-focus DoF ────────────────────────────────────────────────────
    float _dofFocusDistTarget = 80f;
    float _dofFocusCurrent    = 80f;
    float _timerAutoFocus;
    const float AUTOFOCUS_INTERVAL = 0.2f;

    // ── Sprint blur ───────────────────────────────────────────────────────
    float _sprintBlurTarget;
    float _sprintBlurCurrent;
    ControladorJugador  _jugadorCache;
    CharacterController _ccCache;       // CRÍTICO: cache de CC — evita GetComponent cada frame

    // ── Explosion bloom burst ─────────────────────────────────────────────
    float _bloomBurstTimer;
    float _bloomBase = 0.6f;
    const float BLOOM_EXPLOSION = 3.5f;
    const float BLOOM_BURST_DUR = 0.8f;

    // ── Rain screen effect ────────────────────────────────────────────────
    float _rainChromatic;
    float _rainDistortion;

    // ── Mouse chromatic ───────────────────────────────────────────────────
    float _mouseChromaticTarget;

    // ── Aiming vignette ───────────────────────────────────────────────────
    bool  _estaApuntando;
    float _vignetteAimTarget;

    // ── Explosion lights (pool de 4) ──────────────────────────────────────
    Light[]                 _explosionLights;
    HDAdditionalLightData[] _explosionLightsHD;
    float[]                 _explosionLightTimers;
    int                     _explosionLightIdx;
    const int   EXPLOSION_LIGHT_POOL = 4;
    const float EXPLOSION_LIGHT_DUR  = 0.35f;

    // OPT: referencia cacheada al coche del jugador — evita FindFirstObjectByType (O(n))
    // en ActualizarMotionBlur() cada frame. Ahorro estimado: ~0.3-0.8 ms/frame con 50+ objetos.
    ControladorVehiculoJugador _cocheJugadorCache;

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

        InicializarLucesExplosion();

        AltsasuCore.OnJugadorSpawned += t =>
        {
            _jugadorCache = t?.GetComponent<ControladorJugador>();
            _ccCache      = t?.GetComponent<CharacterController>();
        };
        if (AltsasuCore.Jugador != null)
        {
            _jugadorCache = AltsasuCore.Jugador.GetComponent<ControladorJugador>();
            _ccCache      = AltsasuCore.Jugador.GetComponent<CharacterController>();
        }
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
        _sirenLight.type    = LightType.Point;
        _sirenLight.range   = 40f;
        _sirenLight.color   = Color.blue;
        _sirenLight.shadows = LightShadows.None;
        _hdSiren = go.AddComponent<HDAdditionalLightData>();
        _hdSiren.SetIntensity(0f, LightUnit.Lux);
        go.SetActive(false);
    }

    void SuscribirEventos()
    {
        ControladorJugador.OnDanoRecibido     += OnJugadorDano;
        GameManagerAltsasua.OnEstrellasCambia += OnWantedCambia;
        ControladorVehiculoJugador.OnJugadorEntro += OnEntroVehiculo;
        // OPT: cachear referencia al entrar y limpiarla al salir — elimina FindFirstObjectByType cada frame
        ControladorVehiculoJugador.OnJugadorEntro  += OnCocheEntro;
        ControladorVehiculoJugador.OnJugadorSalio  += OnCocheSalio;
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
        ActualizarAutoFocus(dt);
        ActualizarSprintBlur(dt);
        ActualizarExplosionBloom(dt);
        ActualizarRainEffect(dt);
        ActualizarMouseChromatic(dt);
        ActualizarAimingVignette(dt);
        AplicarVignetteUnificada();   // BUG FIX #2: escritura única al volumen
        ActualizarExplosionLights(dt);
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

    // BUG FIX #2: ActualizarVignette ya NO escribe al volumen directamente.
    // Solo actualiza el estado interno. La escritura ocurre una sola vez
    // en AplicarVignetteUnificada() que combina TODAS las fuentes sin conflicto.
    void ActualizarVignette(float dt)
    {
        _vignetteIntensTarget = Mathf.MoveTowards(_vignetteIntensTarget, _vignetteIntensBase, dt * 2.5f);
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
        if (_hdSiren != null) _hdSiren.SetIntensity(pulse * 3000f, LightUnit.Lux);
        else _sirenLight.intensity = pulse * 3000f;
        _sirenLight.color     = (_sirenTimer % 2f) < 1f ? Color.blue : Color.red;
        // Seguir al jugador
        var j = AltsasuCore.Jugador;
        if (j != null) _sirenLight.transform.position = j.position + Vector3.up * 8f;
    }

    // ── Motion blur dinámico ──────────────────────────────────────────────

    void ActualizarMotionBlur()
    {
        if (_motionBlur == null) return;
        // OPT: usa referencia cacheada en lugar de FindFirstObjectByType (O(n) escena)
        // Ahorro estimado: ~0.3-0.8 ms/frame → ~18-48 ms/s de CPU liberado.
        if (_cocheJugadorCache != null && _cocheJugadorCache.JugadorDentro)
        {
            var rb = _cocheJugadorCache.GetComponent<Rigidbody>();
            float spd = rb != null ? rb.linearVelocity.magnitude : 0f;
            float blur = Mathf.InverseLerp(10f, 60f, spd) * 0.35f;
            _motionBlur.intensity.Override(blur);
        }
        else
        {
            _motionBlur.intensity.Override(0f);
        }
    }


    // ── Vignette unificada (BUG FIX #2) ──────────────────────────────────
    // Única fuente de verdad para _vignette.intensity y .color.
    // Combina: base + daño (pulsante) + apuntado.
    // Antes había dos métodos escribiendo a la misma propiedad → race condition.
    void AplicarVignetteUnificada()
    {
        if (_vignette == null) return;
        // Contribución de daño (pulso rojo)
        float t      = Mathf.InverseLerp(_vignetteIntensBase, 0.7f, _vignetteIntensTarget);
        float danoPulse = Mathf.Lerp(_vignetteIntensBase, _vignetteIntensTarget,
                          Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f)) * 0.3f + 0.7f);
        // Contribución de apuntado (oscurece bordes)  
        float aimContrib = _vignetteIntensBase + _vignetteAimTarget;
        // Contribución de lluvia (vignette sutil acuática)
        float rainContrib = _vignetteIntensBase + _rainChromatic * 0.15f;
        // Winner: la fuente más intensa domina, sin conflicto
        float finalIntens = Mathf.Max(danoPulse, aimContrib, rainContrib);
        Color finalColor  = Color.Lerp(_vignetteColorBase, _vignetteColorDano, t);
        _vignette.intensity.Override(finalIntens);
        _vignette.color.Override(finalColor);
    }

    // ── Auto-focus DoF ─────────────────────────────────────────────────────

    void ActualizarAutoFocus(float dt)
    {
        if (_cam == null) return;
        _timerAutoFocus -= dt;
        if (_timerAutoFocus <= 0f)
        {
            _timerAutoFocus = AUTOFOCUS_INTERVAL;
            var ray  = _cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int mask = ~LayerMask.GetMask("Player", "Ignore Raycast");
            float dist = Physics.Raycast(ray, out var hit, 500f, mask) ? hit.distance : 200f;
            _dofFocusDistTarget = Mathf.Clamp(dist, 1f, 500f);
        }
        _dofFocusCurrent = Mathf.Lerp(_dofFocusCurrent, _dofFocusDistTarget, dt * 5f);
        SistemaVolumenHDRP.SetFocusDistance(_dofFocusCurrent);
    }

    // ── Sprint blur ────────────────────────────────────────────────────────

    void ActualizarSprintBlur(float dt)
    {
        if (_lensDistortion == null) return;
        float velocidad = 0f;
        // CRÍTICO: _ccCache cacheado en Start → 0 alloc/frame
        if (_jugadorCache != null && (_cocheJugadorCache == null || !_cocheJugadorCache.JugadorDentro))
            velocidad = _ccCache != null ? _ccCache.velocity.magnitude : 0f;
        float target = Mathf.InverseLerp(5f, 9f, velocidad) * -0.08f;
        _sprintBlurCurrent = Mathf.MoveTowards(_sprintBlurCurrent, target, dt * 1.5f);
        _sprintBlurTarget  = target;
        if (Mathf.Abs(_lensDistortion.intensity.value) < 0.2f)
            _lensDistortion.intensity.Override(_sprintBlurCurrent);
    }

    // ── Explosion bloom burst ──────────────────────────────────────────────

    void ActualizarExplosionBloom(float dt)
    {
        if (_bloom == null || _bloomBurstTimer <= 0f) return;
        _bloomBurstTimer -= dt;
        float t     = 1f - Mathf.Clamp01(_bloomBurstTimer / BLOOM_BURST_DUR);
        float curva = Mathf.Pow(1f - t, 1.5f);
        _bloom.intensity.Override(Mathf.Lerp(_bloomBase, BLOOM_EXPLOSION, curva));
        if (_bloomBurstTimer <= 0f) _bloom.intensity.Override(_bloomBase);
    }

    // ── Rain screen effect ─────────────────────────────────────────────────

    void ActualizarRainEffect(float dt)
    {
        float humedad = SistemaCharcos.Instance != null ? SistemaCharcos.Instance.Humedad : 0f;
        float targetRainChromatic = humedad * 0.28f;
        _rainChromatic = Mathf.MoveTowards(_rainChromatic, targetRainChromatic, dt * 0.8f);
        float targetRainDist = humedad * 0.06f;
        _rainDistortion = Mathf.MoveTowards(_rainDistortion, targetRainDist, dt * 0.5f);
        if (_chromaticCurrent < _rainChromatic && _chromatic != null)
            _chromatic.intensity.Override(Mathf.Max(_chromaticCurrent, _rainChromatic));
        if (_lensDistortion != null && Mathf.Abs(_sprintBlurCurrent) < 0.01f
            && _lensDistortion.intensity.value >= -0.01f)
            _lensDistortion.intensity.Override(_rainDistortion);
    }

    // ── Mouse-speed chromatic aberration ──────────────────────────────────

    void ActualizarMouseChromatic(float dt)
    {
        if (_chromatic == null) return;
        var m = UnityEngine.InputSystem.Mouse.current;
        if (m == null) return;
        float speed = m.delta.ReadValue().magnitude;
        float spike = Mathf.InverseLerp(40f, 160f, speed) * 0.22f;
        _mouseChromaticTarget = Mathf.Max(_mouseChromaticTarget, spike);
        _mouseChromaticTarget = Mathf.MoveTowards(_mouseChromaticTarget, 0f, dt * 3.5f);
        if (_mouseChromaticTarget > _chromaticCurrent)
            _chromatic.intensity.Override(_mouseChromaticTarget);
    }

    // ── Aiming vignette ────────────────────────────────────────────────────

    // BUG FIX #2: solo actualiza estado interno.
    void ActualizarAimingVignette(float dt)
    {
        var m = UnityEngine.InputSystem.Mouse.current;
        _estaApuntando = m != null && m.rightButton.isPressed
                         && Cursor.lockState == CursorLockMode.Locked;
        float targetExtra = _estaApuntando ? 0.10f : 0f;
        _vignetteAimTarget = Mathf.MoveTowards(_vignetteAimTarget, targetExtra, dt * 6f);
    }

    // ── Explosion lights (pool) ────────────────────────────────────────────

    void InicializarLucesExplosion()
    {
        _explosionLights      = new Light[EXPLOSION_LIGHT_POOL];
        _explosionLightsHD    = new HDAdditionalLightData[EXPLOSION_LIGHT_POOL];
        _explosionLightTimers = new float[EXPLOSION_LIGHT_POOL];
        for (int i = 0; i < EXPLOSION_LIGHT_POOL; i++)
        {
            var go = new GameObject($"ExplosionLight_{i}");
            go.transform.SetParent(transform);
            var l  = go.AddComponent<Light>();
            l.type = LightType.Point; l.range = 25f;
            l.color = new Color(1.0f, 0.65f, 0.20f);
            l.shadows = LightShadows.None;
            var hd = go.AddComponent<HDAdditionalLightData>();
            hd.SetIntensity(0f, LightUnit.Lumen);
            hd.volumetricDimmer = 0.8f;
            _explosionLights[i]   = l;
            _explosionLightsHD[i] = hd;
            go.SetActive(false);
        }
    }

    void ActualizarExplosionLights(float dt)
    {
        for (int i = 0; i < EXPLOSION_LIGHT_POOL; i++)
        {
            if (_explosionLightTimers[i] <= 0f) continue;
            _explosionLightTimers[i] -= dt;
            float t = Mathf.Clamp01(_explosionLightTimers[i] / EXPLOSION_LIGHT_DUR);
            _explosionLightsHD[i]?.SetIntensity(Mathf.Pow(t, 0.4f) * 80000f, LightUnit.Lumen);
            if (_explosionLightTimers[i] <= 0f)
                _explosionLights[i].gameObject.SetActive(false);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  API ESTÁTICA — llamada desde otros sistemas
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Activa una luz de punto naranja en la posición de la explosión.
    /// </summary>
    public static void ExplosionLight(Vector3 posicion)
    {
        if (I == null) return;
        int idx = I._explosionLightIdx % EXPLOSION_LIGHT_POOL;
        I._explosionLightIdx++;
        var l = I._explosionLights?[idx];
        if (l == null) return;
        l.transform.position = posicion + Vector3.up * 1.5f;
        l.gameObject.SetActive(true);
        I._explosionLightTimers[idx] = EXPLOSION_LIGHT_DUR;
    }

    /// <summary>
    /// Burst de bloom + luz de punto — llamar desde SistemaExplosion.
    /// </summary>
    public static void ExplosionBloom(float intensidad = 1f)
    {
        if (I == null) return;
        I._bloomBurstTimer = BLOOM_BURST_DUR * Mathf.Clamp01(intensidad);
        Shake(intensidad * 0.4f);
        // Activar lens dirt proporcional a la intensidad — vuelve a 0 cuando el bloom cae
        SistemaVolumenHDRP.SetLensDirt(intensidad);
        I.Invoke(nameof(DesactivarLensDirt), BLOOM_BURST_DUR * 0.9f);
    }

    void DesactivarLensDirt() => SistemaVolumenHDRP.SetLensDirt(0f);

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

    // OPT: cachear/descachear referencia al coche para ActualizarMotionBlur
    void OnCocheEntro(ControladorVehiculoJugador coche) { _cocheJugadorCache = coche; }
    void OnCocheSalio(ControladorVehiculoJugador _)     { _cocheJugadorCache = null; }

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
        if (!activo) { if (I._hdSiren != null) I._hdSiren.SetIntensity(0f, LightUnit.Lux); else I._sirenLight.intensity = 0f; }
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
        ControladorVehiculoJugador.OnJugadorEntro -= OnCocheEntro;
        ControladorVehiculoJugador.OnJugadorSalio -= OnCocheSalio;
    }
}
