// Assets/Scripts/ControladorPostProcesado.cs
// Post-procesado fotorrealista para Alsasua Simulator (URP)
// Efectos: ACES Tonemapping · Color Grading · White Balance · Bloom ·
//          Vignette · Depth of Field · Motion Blur · Film Grain ·
//          Shadows/Midtones/Highlights · Chromatic Aberration

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Camera))]
public class ControladorPostProcesado : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════════════
    //  REFERENCIAS
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ VOLUME URP ═══")]
    [Tooltip("GameObject con el componente Volume Global de la escena")]
    [SerializeField] private Volume volumenGlobal;

    // ═══════════════════════════════════════════════════════════════════════
    //  TONEMAPPING (lo más importante para aspecto fotográfico)
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ TONEMAPPING ═══")]
    [Tooltip("ACES = aspecto de cine. Neutral = más fiel al color original.")]
    [SerializeField] private TonemappingMode modoTonemapping = TonemappingMode.ACES;

    // ═══════════════════════════════════════════════════════════════════════
    //  COLOR GRADING
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ COLOR GRADING ═══")]
    [Range(-100f, 100f)]
    [Tooltip("Temperatura — negativo=frío/azulado (clima norteño Alsasua), positivo=cálido")]
    [SerializeField] private float temperaturaColor = -10f;

    [Range(-100f, 100f)]
    [SerializeField] private float tintColor = 3f;

    [Range(0f, 200f)]
    [Tooltip("Saturación: 100 = natural, >100 = más vívido")]
    [SerializeField] private float saturacion = 90f;

    [Range(-100f, 100f)]
    [Tooltip("Contraste: +10 = ligeramente más dramático")]
    [SerializeField] private float contraste = 12f;

    [Range(-5f, 5f)]
    [Tooltip("Exposición post-proceso (stops)")]
    [SerializeField] private float exposicion = 0f;

    // ═══════════════════════════════════════════════════════════════════════
    //  SHADOWS / MIDTONES / HIGHLIGHTS
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ SHADOWS / MIDTONES / HIGHLIGHTS ═══")]
    [Tooltip("Tinte en las sombras (azulado para exteriores)")]
    [SerializeField] private Vector4 sombras   = new Vector4(0.97f, 0.97f, 1.03f, 0f);
    [Tooltip("Tinte en los medios tonos")]
    [SerializeField] private Vector4 mediosTonos = new Vector4(1f, 1f, 1f, 0f);
    [Tooltip("Tinte en las luces altas (ligeramente cálido)")]
    [SerializeField] private Vector4 lucesAltas = new Vector4(1.02f, 1.01f, 0.98f, 0f);

    // ═══════════════════════════════════════════════════════════════════════
    //  BLOOM
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ BLOOM ═══")]
    [Range(0f, 1f)]
    [Tooltip("Umbral de brillo — 0.9 = sólo las partes muy brillantes")]
    [SerializeField] private float bloomThreshold = 0.9f;

    [Range(0f, 2f)]
    [Tooltip("Intensidad del bloom — mantener bajo para realismo")]
    [SerializeField] private float bloomIntensidad = 0.25f;

    [Range(0f, 1f)]
    [SerializeField] private float bloomScatter = 0.65f;

    // ═══════════════════════════════════════════════════════════════════════
    //  VIGNETTE
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ VIGNETTE ═══")]
    [Range(0f, 1f)]
    [Tooltip("Oscurecimiento de los bordes — 0.25 es muy sutil y realista")]
    [SerializeField] private float vignetteIntensidad = 0.25f;

    [Range(0f, 1f)]
    [SerializeField] private float vignetteSuavizado = 0.45f;

    // ═══════════════════════════════════════════════════════════════════════
    //  DEPTH OF FIELD
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ DEPTH OF FIELD ═══")]
    [Tooltip("Activa desenfoque de profundidad (realismo de lente)")]
    [SerializeField] private bool dofActivo = true;

    [Range(100f, 10000f)]
    [Tooltip("Distancia de enfoque desde la cámara (metros)")]
    [SerializeField] private float dofFocusDistance = 800f;

    [Range(0f, 10f)]
    [Tooltip("Apertura de lente — mayor = más desenfoque en el fondo")]
    [SerializeField] private float dofApertura = 2.5f;

    // ═══════════════════════════════════════════════════════════════════════
    //  MOTION BLUR
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ MOTION BLUR ═══")]
    [Tooltip("Activa desenfoque de movimiento cuando la cámara gira rápido")]
    [SerializeField] private bool motionBlurActivo = true;

    [Range(0f, 0.5f)]
    [Tooltip("Intensidad del motion blur — 0.15 = sutil y realista")]
    [SerializeField] private float motionBlurIntensidad = 0.15f;

    // ═══════════════════════════════════════════════════════════════════════
    //  FILM GRAIN
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ FILM GRAIN ═══")]
    [Tooltip("Añade un grano de película muy sutil para aspecto cinematográfico")]
    [SerializeField] private bool grainActivo = true;

    [Range(0f, 0.5f)]
    [Tooltip("Intensidad del grano — 0.05 es casi imperceptible")]
    [SerializeField] private float grainIntensidad = 0.06f;

    // ═══════════════════════════════════════════════════════════════════════
    //  CHROMATIC ABERRATION
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ CHROMATIC ABERRATION ═══")]
    [Range(0f, 0.5f)]
    [Tooltip("Separación de colores en los bordes (lente imperfecta) — mantener < 0.1")]
    [SerializeField] private float chromaticIntensidad = 0.04f;

    // ═══════════════════════════════════════════════════════════════════════
    //  ANTI-ALIASING
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ ANTI-ALIASING ═══")]
    [SerializeField] private AntialiasingMode modoAA = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

    // ═══════════════════════════════════════════════════════════════════════
    //  SINCRONÍA CON HORA DEL DÍA
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ CICLO DÍA/NOCHE ═══")]
    [Tooltip("Sincroniza la temperatura de color con SistemaAtmosfera")]
    [SerializeField] private bool sincronizarConAtmosfera = true;

    // ═══════════════════════════════════════════════════════════════════════
    //  ESTADO INTERNO
    // ═══════════════════════════════════════════════════════════════════════

    private Camera                        cam;
    private UniversalAdditionalCameraData cameraData;
    private SistemaAtmosfera              atmosfera;
    private SistemaClima                  sistemaClima; // BUG 29 FIX: único escritor de whiteBalance.temperature

    // Efectos del volume
    private Tonemapping                 tonemapping;
    private ColorAdjustments            colorAdjustments;
    private WhiteBalance                whiteBalance;
    private ShadowsMidtonesHighlights   smh;
    private Bloom                       bloom;
    private Vignette                    vignette;
    private DepthOfField                dof;
    private MotionBlur                  motionBlur;
    private FilmGrain                   filmGrain;
    private ChromaticAberration         chromaticAberration;

    // ═══════════════════════════════════════════════════════════════════════
    //  UNITY
    // ═══════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        cam          = GetComponent<Camera>();
        cameraData   = cam.GetUniversalAdditionalCameraData();
        atmosfera    = Object.FindFirstObjectByType<SistemaAtmosfera>();
        sistemaClima = Object.FindFirstObjectByType<SistemaClima>(); // BUG 29 FIX
    }

    private void Start()
    {
        ConfigurarCamara();

        if (volumenGlobal == null)
        {
            // Buscar automáticamente
            volumenGlobal = Object.FindFirstObjectByType<Volume>();
            if (volumenGlobal == null)
            {
                Debug.LogWarning("[PostProcesado] No hay Volume en la escena. " +
                                 "Ejecuta Alsasua → ⚙ Configurar Escena Completa.");
                return;
            }
        }

        ObtenerEfectos();
        AplicarTodos();
    }

    private void Update()
    {
        // BUG 29 FIX: punto único de escritura de whiteBalance.temperature.
        AplicarTemperaturaWhiteBalance();

        // MEJORA: bloom y grano dinámicos según hora del día.
        // Animados suavemente en Update para no saltar bruscamente.
        if (sincronizarConAtmosfera && atmosfera != null)
        {
            AplicarBloomDinamico();
            AplicarGrainDinamico();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CONFIGURACIÓN DE CÁMARA
    // ═══════════════════════════════════════════════════════════════════════

    private void ConfigurarCamara()
    {
        if (cameraData == null) return;

        // SMAA High = antialiasing sin artefactos de TAA para escenas estáticas
        cameraData.antialiasing        = modoAA;
        cameraData.antialiasingQuality = AntialiasingQuality.High;

        // Habilitar post-procesado en esta cámara
        cameraData.renderPostProcessing = true;

        // MEJORA Z-BUFFER: ratio near/far = 1:600000 causaba z-fighting en edificios.
        // 0.25m near + 150km far = ratio 1:600000→1:600000... espera → 0.25/150000 = 1:600000
        // Mejor: 0.3m / 120000m = 1:400000 → buena precisión en el rango 1-1000m del jugador.
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane  = 120000f; // 120 km cubre toda el área de Cesium visible + margen
        cam.usePhysicalProperties = false;

        Debug.Log("[PostProcesado] Cámara configurada.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  OBTENER / CREAR EFECTOS DEL VOLUME
    // ═══════════════════════════════════════════════════════════════════════

    private void ObtenerEfectos()
    {
        var p = volumenGlobal.profile;

        ObtenerOCrear(ref tonemapping);
        ObtenerOCrear(ref colorAdjustments);
        ObtenerOCrear(ref whiteBalance);
        ObtenerOCrear(ref smh);
        ObtenerOCrear(ref bloom);
        ObtenerOCrear(ref vignette);
        ObtenerOCrear(ref dof);
        ObtenerOCrear(ref motionBlur);
        ObtenerOCrear(ref filmGrain);
        ObtenerOCrear(ref chromaticAberration);

        void ObtenerOCrear<T>(ref T efecto) where T : VolumeComponent
        {
            if (!p.TryGet(out efecto))
                efecto = p.Add<T>(true);
        }

        Debug.Log("[PostProcesado] Efectos obtenidos del Volume profile.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  APLICAR TODOS LOS EFECTOS
    // ═══════════════════════════════════════════════════════════════════════

    private void AplicarTodos()
    {
        AplicarTonemapping();
        AplicarColorGrading();
        AplicarWhiteBalance();
        AplicarSMH();
        AplicarBloom();
        AplicarVignette();
        AplicarDOF();
        AplicarMotionBlur();
        AplicarFilmGrain();
        AplicarChromaticAberration();
        Debug.Log("[PostProcesado] ✓ Todos los efectos aplicados (modo fotorrealista).");
    }

    // ───────────────────────────────────────────────────────────────────────

    private void AplicarTonemapping()
    {
        if (tonemapping == null) return;
        tonemapping.active = true;
        tonemapping.mode.value         = modoTonemapping;
        tonemapping.mode.overrideState = true;
    }

    private void AplicarColorGrading()
    {
        if (colorAdjustments == null) return;
        colorAdjustments.active = true;

        Set(colorAdjustments.postExposure, exposicion);
        Set(colorAdjustments.contrast,     contraste);
        Set(colorAdjustments.saturation,   saturacion - 100f); // URP: -100 a +100
        Set(colorAdjustments.colorFilter,  Color.white);
    }

    private void AplicarWhiteBalance()
    {
        if (whiteBalance == null) return;
        whiteBalance.active = true;

        Set(whiteBalance.temperature, temperaturaColor);
        Set(whiteBalance.tint,        tintColor);
    }

    private void AplicarSMH()
    {
        if (smh == null) return;
        smh.active = true;

        // Sombras ligeramente azuladas (exterior, cielo)
        smh.shadows.value         = sombras;
        smh.shadows.overrideState = true;

        smh.midtones.value         = mediosTonos;
        smh.midtones.overrideState = true;

        smh.highlights.value         = lucesAltas;
        smh.highlights.overrideState = true;
    }

    private void AplicarBloom()
    {
        if (bloom == null) return;
        bloom.active = true;

        Set(bloom.threshold, bloomThreshold);
        Set(bloom.intensity,  bloomIntensidad);
        Set(bloom.scatter,    bloomScatter);
    }

    private void AplicarVignette()
    {
        if (vignette == null) return;
        vignette.active = true;

        Set(vignette.intensity,  vignetteIntensidad);
        Set(vignette.smoothness, vignetteSuavizado);
        vignette.rounded.value         = true;
        vignette.rounded.overrideState = true;
    }

    private void AplicarDOF()
    {
        if (dof == null) return;
        dof.active = dofActivo;

        // Modo Bokeh = más realista (más GPU). Gaussian = más rápido.
        dof.mode.value         = DepthOfFieldMode.Bokeh;
        dof.mode.overrideState = true;

        Set(dof.focusDistance,  dofFocusDistance);
        Set(dof.aperture,       dofApertura);

        // Longitud focal equivalente a un teleobjetivo 50mm para dron
        dof.focalLength.value         = 50f;
        dof.focalLength.overrideState = true;
    }

    private void AplicarMotionBlur()
    {
        if (motionBlur == null) return;
        motionBlur.active = motionBlurActivo;

        motionBlur.mode.value         = MotionBlurMode.CameraAndObjects;
        motionBlur.mode.overrideState = true;

        motionBlur.quality.value         = MotionBlurQuality.High;
        motionBlur.quality.overrideState = true;

        Set(motionBlur.intensity,   motionBlurIntensidad);
        Set(motionBlur.clamp,       0.05f);
    }

    private void AplicarFilmGrain()
    {
        if (filmGrain == null) return;
        filmGrain.active = grainActivo;

        filmGrain.type.value         = FilmGrainLookup.Thin1;
        filmGrain.type.overrideState = true;

        Set(filmGrain.intensity,  grainIntensidad);
        Set(filmGrain.response,   0.8f);
    }

    private void AplicarChromaticAberration()
    {
        if (chromaticAberration == null) return;
        chromaticAberration.active = true;

        Set(chromaticAberration.intensity, chromaticIntensidad);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SINCRONÍA CON HORA DEL DÍA
    // ═══════════════════════════════════════════════════════════════════════

    // BUG 29 FIX: punto único de escritura en whiteBalance.temperature.
    // Combina temperatura de hora del día + offset meteorológico de SistemaClima.
    private void AplicarTemperaturaWhiteBalance()
    {
        if (whiteBalance == null) return;

        // Temperatura base: valor del Inspector (fallback si no hay sincronía con atmósfera)
        float temp = temperaturaColor;

        if (sincronizarConAtmosfera && atmosfera != null)
        {
            float hora = atmosfera.HoraDelDia;
            // Temperatura de color por hora (igual que el sol real):
            // Amanecer/atardecer: cálido  (+40 a +60)
            // Mediodía despejado: ligeramente frío (-10 a -20)
            // Noche: frío azulado (-40 a -60)
            if      (hora >= 5f  && hora < 8f)  temp = Mathf.Lerp(60f,  15f,  (hora - 5f)  / 3f);  // Amanecer
            else if (hora >= 8f  && hora < 17f) temp = Mathf.Lerp(15f, -10f,  (hora - 8f)  / 9f);  // Día
            else if (hora >= 17f && hora < 21f) temp = Mathf.Lerp(-10f, 50f,  (hora - 17f) / 4f);  // Atardecer
            else                                temp = -40f;                                          // Noche
        }

        // Sumar el offset meteorológico de SistemaClima.
        // Ejemplo: Lluvia → -25, Tormenta → -35, Despejado → 0.
        // SistemaClima.TemperaturaActual se actualiza suavemente durante sus transiciones.
        if (sistemaClima != null)
            temp += sistemaClima.TemperaturaActual;

        whiteBalance.temperature.value         = Mathf.Clamp(temp, -100f, 100f);
        whiteBalance.temperature.overrideState = true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  EFECTOS DINÁMICOS (animados en Update según SistemaAtmosfera)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ajusta el bloom suavemente según la posición del sol:
    ///   · Día (sol alto):       threshold base, intensidad base (sutil)
    ///   · Amanecer/atardecer:   threshold más bajo, intensidad mayor (halos dorados)
    ///   · Noche:                threshold bajo, intensidad alta (halos de luces)
    /// </summary>
    private void AplicarBloomDinamico()
    {
        if (bloom == null) return;
        float elev = atmosfera.ElevacionSolar;

        // factorHorizon sube cuando el sol está cerca del horizonte (±30°)
        float factorHorizon = Mathf.Clamp01(1f - Mathf.Abs(elev) / 30f);
        // factorNoche sube solo cuando el sol está bajo el horizonte
        float factorNoche   = Mathf.Clamp01(-elev / 12f);

        // Targets: noche y amanecer/atardecer tienen bloom más intenso
        float targetThreshold = Mathf.Lerp(bloomThreshold,
                                            Mathf.Lerp(0.78f, 0.65f, factorNoche),
                                            factorHorizon);
        float targetIntensidad = Mathf.Lerp(bloomIntensidad,
                                             Mathf.Lerp(0.45f, 0.65f, factorNoche),
                                             factorHorizon);

        // Transición muy suave (0.4 unidades/seg) para que no se note el cambio
        const float SPEED = 0.4f;
        Set(bloom.threshold, Mathf.Lerp(bloom.threshold.value, targetThreshold, Time.deltaTime * SPEED));
        Set(bloom.intensity,  Mathf.Lerp(bloom.intensity.value,  targetIntensidad,  Time.deltaTime * SPEED));
    }

    /// <summary>
    /// Simula el aumento de ISO de una cámara real en condiciones de poca luz:
    /// el grano sube gradualmente desde el ocaso hasta el amanecer.
    /// </summary>
    private void AplicarGrainDinamico()
    {
        if (filmGrain == null || !grainActivo) return;
        float elev = atmosfera.ElevacionSolar;

        // factorNoche: 0=pleno día, 1=medianoche
        float factorNoche  = Mathf.Clamp01(-elev / 12f);
        float targetGrain  = Mathf.Lerp(grainIntensidad, grainIntensidad * 2.8f, factorNoche);

        Set(filmGrain.intensity, Mathf.Lerp(filmGrain.intensity.value, targetGrain, Time.deltaTime * 0.6f));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    // Setters genéricos con override automático
    private static void Set(FloatParameter p,  float  v) { p.value = v; p.overrideState = true; }
    private static void Set(ColorParameter p,  Color  v) { p.value = v; p.overrideState = true; }

    // ═══════════════════════════════════════════════════════════════════════
    //  PRESETS (botones en Inspector → clic derecho sobre el script)
    // ═══════════════════════════════════════════════════════════════════════

    [ContextMenu("Preset: Día nublado (típico Alsasua)")]
    private void PresetNublado()
    {
        temperaturaColor = -20f; saturacion = 75f; contraste = 8f;
        bloomIntensidad  = 0.1f; vignetteIntensidad = 0.3f;
        grainIntensidad  = 0.08f;
        AplicarTodos();
    }

    [ContextMenu("Preset: Día soleado de verano")]
    private void PresetSoleado()
    {
        temperaturaColor = 15f; saturacion = 100f; contraste = 18f;
        bloomIntensidad  = 0.3f; vignetteIntensidad = 0.2f;
        grainIntensidad  = 0.04f;
        AplicarTodos();
    }

    [ContextMenu("Preset: Amanecer / Atardecer")]
    private void PresetAtardecer()
    {
        temperaturaColor = 50f; saturacion = 95f; contraste = 15f;
        bloomIntensidad  = 0.5f; vignetteIntensidad = 0.35f;
        grainIntensidad  = 0.07f;
        AplicarTodos();
    }

    [ContextMenu("Preset: Noche")]
    private void PresetNoche()
    {
        temperaturaColor = -50f; saturacion = 60f; contraste = 20f;
        bloomIntensidad  = 0.8f; vignetteIntensidad = 0.5f;
        grainIntensidad  = 0.15f;
        AplicarTodos();
    }

    [ContextMenu("Aplicar configuración actual")]
    public void AplicarConfiguracion()
    {
        if (volumenGlobal != null)
        {
            ObtenerEfectos();
            AplicarTodos();
        }
    }
}
