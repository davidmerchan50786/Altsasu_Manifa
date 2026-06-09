// Assets/Scripts/SistemaNeblina.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA NEBLINA — Local Volumetric Fog en el cauce del río Arakil
//
//  Crea en runtime un volumen de niebla local pegado al cauce del Arakil
//  (cota ~520 m, eje E-O del valle de Alsasua) y reacciona a:
//    • SistemaClima.climaActual  — Niebla/Lluvia/Tormenta = densidad alta
//    • _GlobalNightLevel (0=día, 1=noche)
//    • Hora del día (amanecer/atardecer = máxima densidad)
//
//  Dimensiones del volumen:
//    X = 1400 m (eje E-O del Arakil a su paso por el valle)
//    Y = 12 m   (columna de niebla sobre el cauce)
//    Z = 80 m   (anchura del cauce + vegetación de ribera)
//
//  HDRP API: LocalVolumetricFog (UnityEngine.Rendering.HighDefinition)
//    — Siempre disponible en HDRP ≥ 14; no requiere activación en HDRP Asset.
//    — Si el proyecto no tiene HDRP instalado el script no compila: intencional.
//
//  Autocontenido: no modifica SistemaVolumenHDRP. Se añade a cualquier
//  GameObject de la escena; se auto-destruye si HDRP no está disponible.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

public class SistemaNeblina : MonoBehaviour
{
    public static SistemaNeblina Instance { get; private set; }

    // ── Config ────────────────────────────────────────────────────────────
    [Header("Posición del cauce (Unity coords)")]
    [Tooltip("Centro del volumen en X (eje E-O del Arakil)")]
    [SerializeField] float centroX = 1918f;   // ~Herriko Plaza
    [Tooltip("Centro del volumen en Z (sur del casco urbano)")]
    [SerializeField] float centroZ = 8420f;   // ~150 m al sur de la plaza
    [Tooltip("Altura base del cauce (Y Unity = altitud - Z_min)")]
    [SerializeField] float alturaBase = 8f;   // 520 m - 511.33 ≈ 8.7 m Unity

    [Header("Densidades (meanFreePath en metros — menor = más denso)")]
    [SerializeField] float densidadSol      = 80f;    // día despejado: niebla residual
    [SerializeField] float densidadAmanecer = 12f;    // amanecer/atardecer: ribera brumosa
    [SerializeField] float densidadNublado  = 35f;
    [SerializeField] float densidadLluvia   = 18f;
    [SerializeField] float densidadNiebla   = 6f;     // estado Niebla: valle cubierto
    [SerializeField] float densidadNoche    = 25f;    // noche: niebla baja azulada

    // ── Estado interno ────────────────────────────────────────────────────
    LocalVolumetricFog _fog;
    float              _densidadObj  = 80f;
    Color              _colorObj     = new Color(0.88f, 0.92f, 0.95f);
    float              _timerUpdate;
    static readonly int ID_NightLevel = Shader.PropertyToID("_GlobalNightLevel");

    // ── Colores por estado ────────────────────────────────────────────────
    static readonly Color COL_DIA      = new Color(0.88f, 0.92f, 0.95f);   // blanco azulado
    static readonly Color COL_AMANECER = new Color(0.98f, 0.85f, 0.72f);   // cálido
    static readonly Color COL_LLUVIA   = new Color(0.70f, 0.75f, 0.80f);   // gris plomo
    static readonly Color COL_NIEBLA   = new Color(0.80f, 0.85f, 0.88f);   // gris suave
    static readonly Color COL_NOCHE    = new Color(0.45f, 0.52f, 0.68f);   // azul profundo

    // ════════════════════════════════════════════════════════════════════════
    //  BOOT
    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        CrearVolumen();
    }

    void CrearVolumen()
    {
        var go = new GameObject("NeblinaArakil_Volume");
        go.transform.SetParent(transform);
        go.transform.position = new Vector3(centroX, alturaBase, centroZ);

        _fog = go.AddComponent<LocalVolumetricFog>();

        var p = _fog.parameters;
        p.albedo      = COL_DIA;
        p.meanFreePath = densidadSol;
        p.size        = new Vector3(1400f, 12f, 80f);

        // Fade suave en los bordes del volumen (evita corte brusco)
        p.distanceFadeStart = 0.05f;
        p.distanceFadeEnd   = 0.3f;

        _fog.parameters = p;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  UPDATE — cada 2 s para no gastar CPU
    // ════════════════════════════════════════════════════════════════════════

    void Update()
    {
        _timerUpdate += Time.deltaTime;
        if (_timerUpdate < 2f) return;
        _timerUpdate = 0f;

        RecalcularObjetivo();
        AplicarSuave();
    }

    void RecalcularObjetivo()
    {
        float nightLevel = Shader.GetGlobalFloat(ID_NightLevel);  // 0=día 1=noche

        // ── Hora del día → detectar amanecer/atardecer ────────────────────
        // SistemaVolumenHDRP publica _GlobalNightLevel como rampa suave.
        // Amanecer/atardecer = zona de transición (0.1–0.4 o 0.6–0.9).
        bool esAmanecer = nightLevel > 0.05f && nightLevel < 0.45f;
        bool esNoche    = nightLevel >= 0.5f;

        // ── Estado del clima ──────────────────────────────────────────────
        var clima = SistemaClima.EstadoActual;   // static property (ver abajo)

        if (esNoche)
        {
            _densidadObj = densidadNoche;
            _colorObj    = COL_NOCHE;
        }
        else if (esAmanecer)
        {
            _densidadObj = densidadAmanecer;
            _colorObj    = COL_AMANECER;
        }
        else
        {
            switch (clima)
            {
                case SistemaClima.EstadoClima.Niebla:
                    _densidadObj = densidadNiebla;   _colorObj = COL_NIEBLA;   break;
                case SistemaClima.EstadoClima.Tormenta:
                case SistemaClima.EstadoClima.LluviaLigera:
                    _densidadObj = densidadLluvia;   _colorObj = COL_LLUVIA;   break;
                case SistemaClima.EstadoClima.Nublado:
                    _densidadObj = densidadNublado;  _colorObj = COL_DIA;      break;
                default:
                    _densidadObj = densidadSol;      _colorObj = COL_DIA;      break;
            }
        }
    }

    void AplicarSuave()
    {
        if (_fog == null) return;
        var p = _fog.parameters;
        // Lerp gradual (la niebla cambia despacio, igual que en la realidad)
        p.meanFreePath = Mathf.Lerp(p.meanFreePath, _densidadObj, 0.15f);
        p.albedo       = Color.Lerp(p.albedo, _colorObj, 0.15f);
        _fog.parameters = p;
    }
}

// ── Extensión estática para SistemaClima ──────────────────────────────────
// Añade una propiedad estática sin tocar el archivo original.
// SistemaClima.climaActual es un campo público de instancia;
// SistemaClima.EstadoActual es el accessor estático conveniente.
public static class SistemaClimaExtension
{
    public static SistemaClima.EstadoClima EstadoActual
    {
        get
        {
            // Busca la primera instancia en escena (SistemaClima es MonoBehaviour)
            var inst = Object.FindFirstObjectByType<SistemaClima>();
            return inst != null ? inst.climaActual : SistemaClima.EstadoClima.Sol;
        }
    }
}
