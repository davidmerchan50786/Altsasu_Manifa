// Assets/Scripts/SistemaAtmosfera.cs
// Sistema de atmósfera realista para Alsasua Simulator
// - Posición solar calculada astronómicamente (lat 42.9016° N — Herriko Plaza, Altsasu)
// - Ciclo día/noche con colores correctos
// - Niebla atmosférica exponencial adaptada a la hora
// - Iluminación ambiente dinámica

using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Simula el sol, la niebla y la iluminación ambiente de Alsasua con cálculo astronómico real.
/// Ejecutar antes que otros scripts (DefaultExecutionOrder -50).
/// </summary>
[DefaultExecutionOrder(-50)]
public class SistemaAtmosfera : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════════════
    //  TIEMPO
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ TIEMPO Y FECHA ═══")]
    [Range(0f, 24f)]
    [Tooltip("Hora solar actual (0 = medianoche, 12 = mediodía)")]
    [SerializeField] private float horaDelDia = 12f;

    [Range(1, 365)]
    [Tooltip("Día del año (1=1 Ene, 172=21 Jun verano, 355=21 Dic invierno)")]
    [SerializeField] private int diaDelAnio = 172;   // Solsticio de verano → máxima luz

    [SerializeField]
    [Tooltip("Animar el paso del tiempo automáticamente")]
    private bool tiempoAnimado = false;

    [Range(1f, 7200f)]
    [Tooltip("Velocidad del tiempo: 3600 = 1 hora real dura 1 segundo")]
    [SerializeField] private float velocidadTiempo = 120f;   // 1 min real = 2 h juego

    // ═══════════════════════════════════════════════════════════════════════
    //  SOL
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ SOL ═══")]
    [Tooltip("La Directional Light de la escena (se detecta automáticamente si está vacío)")]
    [SerializeField] private Light luzSolar;

    [Range(0f, 8f)]
    [Tooltip("Intensidad máxima al mediodía despejado")]
    [SerializeField] private float intensidadMaximaSol = 1.8f;

    [Range(0f, 0.5f)]
    [Tooltip("Luz residual nocturna (luna/estrellas)")]
    [SerializeField] private float intensidadNoche = 0.04f;

    [Tooltip("Degradado de color solar: noche→amanecer→mediodía→atardecer→noche")]
    [SerializeField] private Gradient colorDelSol;

    // ═══════════════════════════════════════════════════════════════════════
    //  NIEBLA ATMOSFÉRICA
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ NIEBLA ATMOSFÉRICA ═══")]
    [SerializeField] private bool nieblaActiva = true;

    [Tooltip("Color base de la niebla (azul-gris del País Vasco)")]
    [SerializeField] private Color colorNieblaBase = new Color(0.74f, 0.80f, 0.87f);

    [Range(0.000001f, 0.002f)]
    [Tooltip("Densidad exponencial — 0.00015 = niebla ligera, 0.001 = niebla densa")]
    [SerializeField] private float densidadNiebla = 0.00018f;

    // ═══════════════════════════════════════════════════════════════════════
    //  ILUMINACIÓN AMBIENTE
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ ILUMINACIÓN AMBIENTE ═══")]
    [Tooltip("Degradado de color ambiente a lo largo del día")]
    [SerializeField] private Gradient colorAmbiente;

    [Range(0f, 3f)]
    [Tooltip("Multiplicador de la intensidad del color ambiente. 1.1 = ligeramente más brillante que el gradiente base.")]
    [SerializeField] private float intensidadAmbiente = 1.1f;

    // ═══════════════════════════════════════════════════════════════════════
    //  PROPIEDADES PÚBLICAS (para otros scripts)
    // ═══════════════════════════════════════════════════════════════════════

    public float HoraDelDia    => horaDelDia;
    public float ElevacionSolar => elevacionSolar;
    public bool  EsDeDia       => elevacionSolar > 0f;

    /// <summary>
    /// Se dispara cuando el sol cruza el horizonte (amanecer o anochecer).
    /// El parámetro bool es <c>true</c> si el nuevo estado es de día, <c>false</c> si es de noche.
    /// </summary>
    public event System.Action<bool> OnCambioDia;

    // ═══════════════════════════════════════════════════════════════════════
    //  ESTADO INTERNO
    // ═══════════════════════════════════════════════════════════════════════

    // Latitud de Alsasua / Altsasu (Herriko Plaza) en radianes
    // GeoDataAlsasua.LAT_CENTRO = 42.9016° N
    private const float LAT_RAD = 42.9016f * Mathf.Deg2Rad;

    private float elevacionSolar;  // grados sobre el horizonte
    private float azimutSolar;     // grados (0=N, 90=E, 180=S, 270=O)

    // PERF FIX: throttle del recálculo de la atmósfera cuando el tiempo está animado.
    // CalcularPosicionSolar() ejecuta 6+ funciones trig por frame → 360+ trig/seg.
    // La posición solar cambia tan despacio que 2 actualizaciones/seg son imperceptibles
    // (con velocidadTiempo=120, 0.5s real = 1 minuto de juego = 0.5° de arco solar).
    // horaDelDia sigue avanzando cada frame para precisión; solo el render se limita.
    private float _timerAtmosfera    = 0f;
    private const float INTERVALO_ATM = 0.5f; // recalcular 2 veces/segundo

    // Estado previo de día/noche para detectar transiciones y disparar OnCambioDia.
    private bool _eraDeDia = true;

    // ═══════════════════════════════════════════════════════════════════════
    //  UNITY
    // ═══════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (luzSolar == null)
            luzSolar = BuscarLuzDirectional();

        if (colorDelSol  == null || colorDelSol.colorKeys.Length  < 3) colorDelSol  = GradienteSolar();
        if (colorAmbiente == null || colorAmbiente.colorKeys.Length < 3) colorAmbiente = GradienteAmbiente();
    }

    private void Start()
    {
        ConfigurarNiebla();
        ActualizarAtmosfera();
    }

    private void Update()
    {
        // BUG 25 FIX: solo llamar ActualizarAtmosfera() cuando el tiempo avanza.
        // Con tiempoAnimado=false, la hora no cambia → los valores de la atmósfera
        // tampoco cambian → recalcular sol/niebla/ambiente cada frame era trabajo perdido.
        // El estado inicial ya se aplica en Start() y OnValidate() responde al Inspector.
        if (tiempoAnimado)
        {
            // Avanzar el reloj cada frame (necesario para precisión temporal)
            horaDelDia = (horaDelDia + Time.deltaTime * velocidadTiempo / 3600f) % 24f;

            // PERF FIX: recalcular sol/niebla/ambiente solo cada INTERVALO_ATM segundos.
            // Antes: ActualizarAtmosfera() se llamaba 60 veces/seg → 360 trig/seg.
            // Ahora: 2 veces/seg → 12 trig/seg. Diferencia visual: imperceptible
            // (la posición solar varía <0.5° entre ticks).
            _timerAtmosfera -= Time.deltaTime;
            if (_timerAtmosfera <= 0f)
            {
                _timerAtmosfera = INTERVALO_ATM;
                ActualizarAtmosfera();
            }
        }
    }

    // BUG 25 FIX: responder a cambios en el Inspector sin necesitar tiempoAnimado=true.
    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        ActualizarAtmosfera();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  LÓGICA PRINCIPAL
    // ═══════════════════════════════════════════════════════════════════════

    private void ActualizarAtmosfera()
    {
        // FIX TEST T20: en tests edit-mode los Gradient [SerializeField] pueden no
        // deserializarse automáticamente → null → NullReferenceException en Evaluate().
        // Awake ya los inicializa pero la doble comprobación aquí hace el método
        // robusto ante cualquier llamada externa (reflexión, hotreload, etc.).
        if (colorDelSol   == null || colorDelSol.colorKeys.Length   < 3) colorDelSol   = GradienteSolar();
        if (colorAmbiente == null || colorAmbiente.colorKeys.Length < 3) colorAmbiente = GradienteAmbiente();

        CalcularPosicionSolar();
        AplicarLuzSolar();
        AplicarAmbiente();
        AplicarNiebla();

        // ── Detectar transición día/noche y disparar evento ────────────────
        bool esDeDiaAhora = EsDeDia;
        if (esDeDiaAhora != _eraDeDia)
        {
            _eraDeDia = esDeDiaAhora;
            string transicion = esDeDiaAhora ? "Noche → Día" : "Día → Noche";
            AlsasuaLogger.Info("Atmósfera", $"{transicion} — hora: {horaDelDia:F1}h  elevación: {elevacionSolar:F1}°");
            OnCambioDia?.Invoke(esDeDiaAhora);
        }
    }

    /// <summary>
    /// Cálculo astronómico simplificado de la posición solar para lat 42.9016° N (Alsasua).
    /// Referencias: NOAA Solar Position Algorithms (simplificado).
    /// </summary>
    private void CalcularPosicionSolar()
    {
        // Declinación solar: varía de -23.45° (invierno) a +23.45° (verano)
        float declRad = -23.45f * Mathf.Cos(2f * Mathf.PI * (diaDelAnio + 10) / 365f) * Mathf.Deg2Rad;

        // Ángulo horario: 0° al mediodía solar, ±15° por hora
        float anguloHRad = (horaDelDia - 12f) * 15f * Mathf.Deg2Rad;

        // Elevación solar (altitud sobre el horizonte)
        float sinElev = Mathf.Sin(LAT_RAD) * Mathf.Sin(declRad)
                      + Mathf.Cos(LAT_RAD) * Mathf.Cos(declRad) * Mathf.Cos(anguloHRad);
        elevacionSolar = Mathf.Asin(Mathf.Clamp(sinElev, -1f, 1f)) * Mathf.Rad2Deg;

        // Azimut solar (dirección horizontal)
        float cosElev = Mathf.Cos(elevacionSolar * Mathf.Deg2Rad);
        if (cosElev < 0.001f)
        {
            azimutSolar = 180f;  // Zénit — apuntar al Sur
        }
        else
        {
            float cosAz = (Mathf.Sin(declRad) - Mathf.Sin(LAT_RAD) * sinElev) /
                          (Mathf.Cos(LAT_RAD) * cosElev);
            azimutSolar = Mathf.Acos(Mathf.Clamp(cosAz, -1f, 1f)) * Mathf.Rad2Deg;
            if (horaDelDia > 12f) azimutSolar = 360f - azimutSolar;
        }
    }

    private void AplicarLuzSolar()
    {
        if (luzSolar == null) return;

        // Rotación: Unity Directional Light "brilla hacia abajo" por defecto
        // Elevation = -X (hacia arriba), Azimuth = Y (girar en horizontal)
        luzSolar.transform.rotation = Quaternion.Euler(-elevacionSolar, azimutSolar, 0f);

        // Intensidad: sube progresivamente cuando el sol sale
        float factorElevacion = Mathf.Clamp01((elevacionSolar + 8f) / 20f);
        luzSolar.intensity = Mathf.Lerp(intensidadNoche, intensidadMaximaSol,
                                        factorElevacion * factorElevacion);

        // Color solar según hora del día
        float t = horaDelDia / 24f;
        luzSolar.color = colorDelSol.Evaluate(t);

        // MEJORA SOMBRAS: activadas desde -3° (incluye amanecer/atardecer).
        // Hard shadows en ángulos bajos (más dramáticas y baratas); Soft en ángulos altos.
        if (elevacionSolar > 18f)
            luzSolar.shadows = LightShadows.Soft;
        else if (elevacionSolar > -3f)
            luzSolar.shadows = LightShadows.Hard; // rayos rasantes del amanecer/atardecer
        else
            luzSolar.shadows = LightShadows.None;

        // MEJORA SOMBRAS: intensidad gradual según ángulo solar (penumbra natural).
        // Bajo el horizonte: 0.0; horizon: 0.3; cénit: 0.95.
        // SistemaClima.AplicarLuz() puede multiplicar este valor para efecto de nubes.
        float shadowFade = Mathf.Clamp01((elevacionSolar + 3f) / 21f);
        luzSolar.shadowStrength = Mathf.Lerp(0.25f, 0.95f, shadowFade * shadowFade);
    }

    private void AplicarAmbiente()
    {
        if (colorAmbiente == null) colorAmbiente = GradienteAmbiente();
        float t         = horaDelDia / 24f;
        Color colorBase = colorAmbiente.Evaluate(t) * intensidadAmbiente;

        // Cielo: el más brillante, ligeramente más frío (refleja el color del cielo real)
        // (AmbientMode.Trilinear se establece una sola vez en ConfigurarNiebla() — no cada frame)
        RenderSettings.ambientSkyColor     = colorBase * 1.15f;

        // Ecuador / horizonte: color base del gradiente
        RenderSettings.ambientEquatorColor = colorBase;

        // Suelo: siempre más oscuro y con tono cálido-terroso (asfalto, hierba, tierra)
        // De noche se neutraliza un poco para no tener un suelo demasiado naranja oscuro.
        float factorNoche = Mathf.Clamp01(-elevacionSolar / 12f);
        Color tintSuelo   = Color.Lerp(new Color(0.52f, 0.42f, 0.32f), Color.grey, factorNoche * 0.5f);
        RenderSettings.ambientGroundColor  = colorBase * tintSuelo * 0.38f;
    }

    private void AplicarNiebla()
    {
        if (!nieblaActiva) return;

        float t = horaDelDia / 24f;

        // Tinte cálido al amanecer y atardecer
        float calidez = Mathf.Max(
            Mathf.InverseLerp(0.22f, 0.27f, t),   // amanecer
            Mathf.InverseLerp(0.78f, 0.73f, t)    // atardecer
        );
        Color colorFinal = Color.Lerp(colorNieblaBase,
                                      new Color(0.95f, 0.65f, 0.40f),
                                      calidez * 0.45f);

        // Niebla más densa de noche
        float factorNoche = 1f - Mathf.Clamp01((elevacionSolar + 5f) / 15f);
        float densidadFinal = densidadNiebla * (1f + factorNoche * 1.5f);

        RenderSettings.fogColor   = colorFinal;
        RenderSettings.fogDensity = densidadFinal;
    }

    private void ConfigurarNiebla()
    {
        RenderSettings.fog        = nieblaActiva;
        RenderSettings.fogMode    = FogMode.ExponentialSquared;
        RenderSettings.fogColor   = colorNieblaBase;
        RenderSettings.fogDensity = densidadNiebla;

        // MEJORA ILUMINACIÓN INDIRECTA: Trilinear en vez de Flat.
        // Flat ilumina uniformemente desde todos los ángulos → plástico.
        // Trilinear separa sky (frío/azul), equator (neutro) y ground (cálido/oscuro).
        // Se establece UNA SOLA VEZ aquí (Start) en vez de cada frame en AplicarAmbiente().
        RenderSettings.ambientMode = AmbientMode.Trilight;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  UTILIDADES
    // ═══════════════════════════════════════════════════════════════════════

    private Light BuscarLuzDirectional()
    {
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) return l;
        AlsasuaLogger.Warn("Atmósfera", "No se encontró Directional Light. El sol no funcionará.");
        return null;
    }

    // ─── Degradados predeterminados ──────────────────────────────────────

    private static Gradient GradienteSolar()
    {
        // MÁXIMO 8 color keys en Unity — la clave t=1.00 (duplicado de t=0.00) fue eliminada.
        var g = new Gradient();
        g.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.06f, 0.06f, 0.18f), 0.00f),  // Medianoche
                new GradientColorKey(new Color(0.14f, 0.09f, 0.22f), 0.23f),  // Pre-amanecer
                new GradientColorKey(new Color(1.00f, 0.45f, 0.15f), 0.27f),  // Amanecer
                new GradientColorKey(new Color(1.00f, 0.82f, 0.60f), 0.33f),  // Post-amanecer
                new GradientColorKey(new Color(1.00f, 0.96f, 0.88f), 0.50f),  // Mediodía
                new GradientColorKey(new Color(1.00f, 0.85f, 0.62f), 0.67f),  // Pre-atardecer
                new GradientColorKey(new Color(1.00f, 0.42f, 0.12f), 0.73f),  // Atardecer
                new GradientColorKey(new Color(0.06f, 0.06f, 0.18f), 0.78f),  // Post-atardecer → noche
            },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
        );
        return g;
    }

    private static Gradient GradienteAmbiente()
    {
        // MÁXIMO 8 color keys en Unity — la clave t=1.00 (duplicado de t=0.00) fue eliminada.
        var g = new Gradient();
        g.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.03f, 0.03f, 0.09f), 0.00f),  // Medianoche
                new GradientColorKey(new Color(0.10f, 0.07f, 0.18f), 0.23f),  // Pre-amanecer
                new GradientColorKey(new Color(0.55f, 0.33f, 0.22f), 0.27f),  // Amanecer
                new GradientColorKey(new Color(0.55f, 0.60f, 0.72f), 0.35f),  // Mañana
                new GradientColorKey(new Color(0.52f, 0.60f, 0.74f), 0.50f),  // Mediodía
                new GradientColorKey(new Color(0.58f, 0.53f, 0.68f), 0.67f),  // Tarde
                new GradientColorKey(new Color(0.48f, 0.26f, 0.20f), 0.73f),  // Atardecer
                new GradientColorKey(new Color(0.03f, 0.03f, 0.09f), 0.78f),  // Post-atardecer → noche
            },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
        );
        return g;
    }

    // ─── Inspector helpers ───────────────────────────────────────────────

    // BUG 25 FIX: los presets llaman ActualizarAtmosfera() directamente para que
    // el cambio de hora se aplique inmediatamente aunque tiempoAnimado=false.
    [ContextMenu("Preset: Mediodía soleado")]
    private void PresetMediodiaSoleado() { horaDelDia = 12f;  diaDelAnio = 172; ActualizarAtmosfera(); }

    [ContextMenu("Preset: Amanecer de verano")]
    private void PresetAmanecer()        { horaDelDia = 6.5f; diaDelAnio = 172; ActualizarAtmosfera(); }

    [ContextMenu("Preset: Atardecer de otoño")]
    private void PresetAtardecer()       { horaDelDia = 19f;  diaDelAnio = 280; ActualizarAtmosfera(); }

    [ContextMenu("Preset: Noche de invierno")]
    private void PresetNoche()           { horaDelDia = 1f;   diaDelAnio = 355; ActualizarAtmosfera(); }
}
