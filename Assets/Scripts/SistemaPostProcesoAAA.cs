// Assets/Scripts/SistemaPostProcesoAAA.cs
// ═══════════════════════════════════════════════════════════════════════════
//  POST-PROCESO DINÁMICO AAA
//
//  Vive en un Volume propio (prioridad 8, entre Polish=5 y HDRP día=10)
//  y gestiona todos los efectos de post-proceso que dependen del estado
//  del juego en tiempo real:
//
//  FLASHES
//    · FlashExplosion()  — spike de Bloom + aberración cromática intensa
//    · FlashDisparo()    — destello sutil de cañón en el frame del disparo
//
//  AUTOFOCUS DOF
//    · Raycast al centro de pantalla cada 150ms
//    · Lerp suave de focusDistance — las cosas cercanas enfocadas tienen
//      el fondo difuminado de forma natural sin configuración manual
//
//  COLOR GRADING POR ESTADO
//    · Normal     — neutro
//    · WantedAlto — tinte rojo + contraste elevado (urgencia)
//    · VidaBaja   — desaturado + oscuro (agotamiento)
//    · Lluvia     — tinte frío azulado + desaturado
//    · Victoria   — saturación alta + levemente dorado
//
//  Todos los efectos son aditivos sobre el Volume de SistemaVolumenHDRP
//  (que define los valores base) — no los sobreescriben.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

[DefaultExecutionOrder(-70)] // antes que SistemaPolish(-) pero después de SistemaVolumenHDRP(-80)
public class SistemaPostProcesoAAA : MonoBehaviour
{
    public static SistemaPostProcesoAAA Instance { get; private set; }

    // ── Volume propio ─────────────────────────────────────────────────────
    Volume          _vol;
    VolumeProfile   _perfil;

    // ── Overrides ─────────────────────────────────────────────────────────
    Bloom               _bloom;
    ChromaticAberration _chromatic;
    ColorAdjustments    _colorAdj;
    Vignette            _vignette;
    DepthOfField        _dof;

    // ── Bloom flash ───────────────────────────────────────────────────────
    float _bloomFlashActual;
    float _bloomFlashTarget;
    const float BLOOM_BASE = 0f;   // este volumen suma 0 en estado neutral

    // ── Aberración ────────────────────────────────────────────────────────
    float _caTarget;
    float _caCurrent;

    // ── Autofocus ─────────────────────────────────────────────────────────
    Camera _cam;
    float  _focusDistancia = 20f;
    Coroutine _crAutofocus;

    // ── Color grading por estado ───────────────────────────────────────────
    public enum EstadoGrading { Normal, WantedAlto, VidaBaja, Lluvia, Victoria }
    EstadoGrading _estadoActual = EstadoGrading.Normal;

    struct ConfigGrading
    {
        public float postExposure, contraste, saturacion;
        public Color filtro;
    }

    static readonly ConfigGrading[] GRADING = {
        // Normal
        new ConfigGrading { postExposure=0f,   contraste=0f,  saturacion=0f,   filtro=Color.white },
        // WantedAlto
        new ConfigGrading { postExposure=0.2f, contraste=18f, saturacion=-12f, filtro=new Color(1f, 0.82f, 0.78f) },
        // VidaBaja
        new ConfigGrading { postExposure=-0.6f,contraste=10f, saturacion=-40f, filtro=new Color(0.88f, 0.86f, 0.90f) },
        // Lluvia
        new ConfigGrading { postExposure=-0.3f,contraste=8f,  saturacion=-18f, filtro=new Color(0.82f, 0.88f, 1.0f)  },
        // Victoria
        new ConfigGrading { postExposure=0.4f, contraste=-5f, saturacion=20f,  filtro=new Color(1.0f,  0.97f, 0.85f) },
    };

    // estado de transición de color grading
    float _gradingT;   // 0=origen, 1=destino
    ConfigGrading _gradingOrigen, _gradingDestino;

    // ── Vignette de vida baja ─────────────────────────────────────────────
    float _vignetteTarget;

    // ─────────────────────────────────────────────────────────────────────
    //  BOOT
    // ─────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        _cam = Camera.main;
        CrearVolumen();
        SuscribirEventos();
        _crAutofocus = StartCoroutine(BucleAutofocus());
    }

    void CrearVolumen()
    {
        var go = new GameObject("Volume_PostProcesoAAA");
        go.transform.SetParent(transform, false);
        _vol = go.AddComponent<Volume>();
        _vol.isGlobal = true;
        _vol.priority = 8f;   // entre Polish(5) y HDRP_Dia(10)
        _vol.weight   = 1f;

        _perfil = ScriptableObject.CreateInstance<VolumeProfile>();
        _vol.profile = _perfil;

        // Bloom: empieza en 0 — solo activo durante flashes
        _bloom = _perfil.Add<Bloom>(true);
        _bloom.intensity.Override(0f);
        _bloom.scatter.Override(0.75f);
        _bloom.tint.Override(Color.white);

        // Chromatic aberration: empieza en 0
        _chromatic = _perfil.Add<ChromaticAberration>(true);
        _chromatic.intensity.Override(0f);

        // Color adjustments: empieza neutro (no afecta nada)
        _colorAdj = _perfil.Add<ColorAdjustments>(true);
        _colorAdj.postExposure.Override(0f);
        _colorAdj.contrast.Override(0f);
        _colorAdj.colorFilter.Override(Color.white);
        _colorAdj.saturation.Override(0f);

        // Vignette: empieza en 0
        _vignette = _perfil.Add<Vignette>(true);
        _vignette.intensity.Override(0f);
        _vignette.color.Override(new Color(0.6f, 0f, 0f));
        _vignette.smoothness.Override(0.4f);

        // DoF: empieza desactivado — se activa cuando el autofocus encuentra diferencia real
        _dof = _perfil.Add<DepthOfField>(true);
        _dof.focusMode.Override(DepthOfFieldMode.UsePhysicalCamera);
        _dof.active = false;  // el volumen HDRP ya tiene DoF; este solo lo afina

        _gradingOrigen  = GRADING[(int)EstadoGrading.Normal];
        _gradingDestino = GRADING[(int)EstadoGrading.Normal];
    }

    void SuscribirEventos()
    {
        GameManagerAltsasua.OnEstrellasCambia    += OnWanted;
        ControladorJugador.OnDanoRecibido        += OnDano;
        SistemaApoyoPopular.OnApoyoCambia        += OnApoyo;
        SistemaMisiones.OnMisionCompletada       += OnMisionCompletada;
    }

    void OnDestroy()
    {
        GameManagerAltsasua.OnEstrellasCambia    -= OnWanted;
        ControladorJugador.OnDanoRecibido        -= OnDano;
        SistemaApoyoPopular.OnApoyoCambia        -= OnApoyo;
        SistemaMisiones.OnMisionCompletada       -= OnMisionCompletada;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  UPDATE
    // ─────────────────────────────────────────────────────────────────────

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // Bloom flash — decae rápidamente
        _bloomFlashActual = Mathf.Lerp(_bloomFlashActual, _bloomFlashTarget, dt * 12f);
        _bloomFlashTarget = Mathf.MoveTowards(_bloomFlashTarget, BLOOM_BASE, dt * 6f);
        if (_bloom != null) _bloom.intensity.Override(_bloomFlashActual);

        // Chromatic aberration
        _caCurrent = Mathf.MoveTowards(_caCurrent, _caTarget, dt * 3.5f);
        _caTarget  = Mathf.MoveTowards(_caTarget,  0f,        dt * 2f);
        if (_chromatic != null) _chromatic.intensity.Override(_caCurrent);

        // Transición de color grading
        if (_gradingT < 1f)
        {
            _gradingT = Mathf.MoveTowards(_gradingT, 1f, dt * 1.5f);
            AplicarGrading(Mathf.SmoothStep(0f, 1f, _gradingT));
        }

        // Vignette de vida baja
        float vigActual = _vignette != null ? _vignette.intensity.value : 0f;
        vigActual = Mathf.MoveTowards(vigActual, _vignetteTarget, dt * 2f);
        if (_vignette != null) _vignette.intensity.Override(vigActual);
    }

    void AplicarGrading(float t)
    {
        if (_colorAdj == null) return;
        float pe  = Mathf.Lerp(_gradingOrigen.postExposure, _gradingDestino.postExposure, t);
        float con = Mathf.Lerp(_gradingOrigen.contraste,    _gradingDestino.contraste,    t);
        float sat = Mathf.Lerp(_gradingOrigen.saturacion,   _gradingDestino.saturacion,   t);
        Color fil = Color.Lerp(_gradingOrigen.filtro,       _gradingDestino.filtro,       t);
        _colorAdj.postExposure.Override(pe);
        _colorAdj.contrast.Override(con);
        _colorAdj.saturation.Override(sat);
        _colorAdj.colorFilter.Override(fil);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  AUTOFOCUS DOF
    // ─────────────────────────────────────────────────────────────────────

    IEnumerator BucleAutofocus()
    {
        var esperaFija = new WaitForSeconds(0.15f);
        while (true)
        {
            yield return esperaFija;
            if (_cam == null) { _cam = Camera.main; continue; }

            // Raycast desde el centro de la cámara
            var ray = new Ray(_cam.transform.position, _cam.transform.forward);
            float dist = Physics.Raycast(ray, out var hit, 200f) ? hit.distance : 80f;

            // Lerp suave — evita saltos bruscos al cambiar de objetivo
            _focusDistancia = Mathf.Lerp(_focusDistancia, dist, 0.25f);

            // Aplicar al volumen HDRP principal via API pública
            SistemaVolumenHDRP.SetFocusDistance(_focusDistancia);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  API PÚBLICA — FLASHES
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Flash de explosión: spike de Bloom intenso + aberración cromática.
    /// Llamado por SistemaExplosion.Explotar().
    /// </summary>
    public static void FlashExplosion()
    {
        if (Instance == null) return;
        // Bloom en este volumen local (rápido, para el frame inmediato)
        Instance._bloomFlashTarget = Mathf.Max(Instance._bloomFlashTarget, 4.5f);
        Instance._caTarget         = Mathf.Max(Instance._caTarget, 0.85f);
        // Bloom en el volumen HDRP principal (más duradero, persistente 400ms)
        SistemaVolumenHDRP.BloomFlash(3.5f);
        SistemaPolish.Shake(0.45f);
    }

    /// <summary>
    /// Destello sutil al disparar (muzzle flash en post-proceso).
    /// Bloom muy corto — solo dura 1-2 frames.
    /// </summary>
    public static void FlashDisparo()
    {
        if (Instance == null) return;
        Instance._bloomFlashTarget = Mathf.Max(Instance._bloomFlashTarget, 0.6f);
        Instance._caTarget         = Mathf.Max(Instance._caTarget, 0.12f);
        SistemaVolumenHDRP.BloomFlash(0.4f); // muy sutil en el volumen principal
    }

    // ─────────────────────────────────────────────────────────────────────
    //  API PÚBLICA — COLOR GRADING
    // ─────────────────────────────────────────────────────────────────────

    public static void CambiarEstado(EstadoGrading nuevo)
    {
        if (Instance == null || Instance._estadoActual == nuevo) return;
        Instance._gradingOrigen  = Instance.GRADING_Actual();
        Instance._gradingDestino = GRADING[(int)nuevo];
        Instance._gradingT       = 0f;
        Instance._estadoActual   = nuevo;
    }

    ConfigGrading GRADING_Actual()
    {
        // Leer estado actual interpolado en lugar del destino para transiciones encadenadas
        if (_colorAdj == null) return GRADING[(int)EstadoGrading.Normal];
        return new ConfigGrading
        {
            postExposure = _colorAdj.postExposure.value,
            contraste    = _colorAdj.contrast.value,
            saturacion   = _colorAdj.saturation.value,
            filtro       = _colorAdj.colorFilter.value,
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    //  MANEJADORES DE EVENTO
    // ─────────────────────────────────────────────────────────────────────

    void OnWanted(int nivel)
    {
        if (nivel >= 3)
            CambiarEstado(EstadoGrading.WantedAlto);
        else if (_estadoActual == EstadoGrading.WantedAlto)
            CambiarEstado(EstadoGrading.Normal);
    }

    void OnDano(int cantidad)
    {
        // Si la vida baja del 30% → grading de vida baja
        var ctrl = AltsasuCore.Jugador?.GetComponent<ControladorJugador>();
        if (ctrl == null) return;
        float ratio = ctrl.RatioVida;
        if (ratio < 0.30f)
        {
            CambiarEstado(EstadoGrading.VidaBaja);
            // Vignette pulsante proporcional a la vida perdida
            _vignetteTarget = Mathf.Lerp(0.45f, 0.15f, ratio / 0.30f);
        }
        else if (_estadoActual == EstadoGrading.VidaBaja)
        {
            CambiarEstado(EstadoGrading.Normal);
            _vignetteTarget = 0f;
        }
    }

    void OnApoyo(float apoyo)
    {
        // Si el apoyo es muy bajo → tinte frío (movimiento en peligro)
        // Solo si no hay un estado de mayor prioridad activo
        if (_estadoActual == EstadoGrading.Normal || _estadoActual == EstadoGrading.Lluvia)
        {
            if (apoyo < 20f) CambiarEstado(EstadoGrading.Lluvia); // frío/amenazante
            else if (_estadoActual == EstadoGrading.Lluvia && apoyo > 35f)
                CambiarEstado(EstadoGrading.Normal);
        }
    }

    void OnMisionCompletada(string nombre)
    {
        // Flash dorado momentáneo al completar una misión
        StartCoroutine(FlashVictoriaMomentaneo());
    }

    IEnumerator FlashVictoriaMomentaneo()
    {
        var anterior = _estadoActual;
        CambiarEstado(EstadoGrading.Victoria);
        yield return new WaitForSecondsRealtime(2.5f);
        CambiarEstado(anterior == EstadoGrading.Victoria ? EstadoGrading.Normal : anterior);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  GRADING EXTERNO — clima
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Llamado por SistemaClima cuando cambia el estado del tiempo.</summary>
    public static void SetClimaGrading(SistemaClima.EstadoClima clima)
    {
        if (Instance == null) return;
        // No pisar estados de juego de mayor prioridad
        if (Instance._estadoActual == EstadoGrading.WantedAlto ||
            Instance._estadoActual == EstadoGrading.VidaBaja) return;

        bool esFrio = clima == SistemaClima.EstadoClima.LluviaLigera
                   || clima == SistemaClima.EstadoClima.Tormenta
                   || clima == SistemaClima.EstadoClima.Niebla;

        CambiarEstado(esFrio ? EstadoGrading.Lluvia : EstadoGrading.Normal);
    }
}
