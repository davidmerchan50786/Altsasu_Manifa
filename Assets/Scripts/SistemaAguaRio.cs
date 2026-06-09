// Assets/Scripts/SistemaAguaRio.cs
// ═══════════════════════════════════════════════════════════════════════════
//  WATER SYSTEM — río Burunda con HDRP Water Surface reactivo al clima
//  (Blueprint AAA+++, Pilar Render §2.4 — Fase 7)
//
//  GeneradorRiosYPuentes ya construye la GEOMETRÍA del cauce desde GeoJSON.
//  Este sistema conduce una HDRP WaterSurface (caustics, foam, deformación
//  real) colocada sobre el río, y la hace reaccionar al clima: en tormenta el
//  agua se encrespa y genera más espuma; en calma queda tersa.
//
//  ───────────────────────────────────────────────────────────────────────────
//  SETUP EN EL EDITOR (una sola vez — no automatizable por código):
//
//   1. HDRP Asset → Rendering → "Water" = ON.
//   2. GameObject → Water Surface → River (o Pool para la regata/charcas grandes).
//   3. Coloca y escala la WaterSurface sobre el cauce del Burunda
//      (usa los ejes que genera GeneradorRiosYPuentes como referencia).
//   4. Añade ESTE componente a un GameObject y arrastra la WaterSurface al
//      campo "aguaRio" del Inspector.
//   5. Player Settings → Scripting Define Symbols → añade:  ALSASUA_WATER
//      (activa el código de abajo; sin el símbolo, este componente es un no-op
//       seguro y el proyecto compila igual — la API de WaterSurface ni se toca).
//
//  Nota: los nombres de propiedad de WaterSurface (ripplesWindSpeed,
//  simulationFoamAmount) pueden variar ligeramente entre versiones de HDRP;
//  si tu versión difiere, ajústalos dentro del bloque #if ALSASUA_WATER.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
#if ALSASUA_WATER
using UnityEngine.Rendering.HighDefinition;
#endif

[DefaultExecutionOrder(-34)]
public class SistemaAguaRio : MonoBehaviour
{
    public static SistemaAguaRio Instance { get; private set; }

#if ALSASUA_WATER
    [Tooltip("WaterSurface del río Burunda. Asignar en el Inspector.")]
    public WaterSurface aguaRio;
#endif

    [Header("Reacción al clima (viento/espuma)")]
    public float windCalma     = 3f;
    public float windTormenta  = 13f;
    [Range(0f, 1f)] public float foamCalma    = 0.10f;
    [Range(0f, 1f)] public float foamTormenta = 0.65f;
    [Tooltip("Velocidad de transición calma↔tormenta (por segundo).")]
    public float velReaccion = 0.4f;

    SistemaClima _clima;
    float _t; // 0 = calma … 1 = tormenta (suavizado)

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
#if ALSASUA_WATER
        if (aguaRio == null)
        {
            AlsasuaLogger.Warn("AguaRio", "Sin WaterSurface asignada — arrastra el río al campo 'aguaRio'.");
            enabled = false;
            return;
        }
        _clima = FindFirstObjectByType<SistemaClima>();
        AlsasuaLogger.Info("AguaRio", "Water Surface reactiva al clima activa.");
#else
        AlsasuaLogger.Info("AguaRio",
            "Water system inactivo (define ALSASUA_WATER no presente). Ver guía de setup en la cabecera.");
        enabled = false;
#endif
    }

    void Update()
    {
#if ALSASUA_WATER
        if (aguaRio == null) return;
        if (_clima == null) _clima = FindFirstObjectByType<SistemaClima>();

        bool tormenta = _clima != null && _clima.climaActual == SistemaClima.EstadoClima.Tormenta;
        bool lluvia   = _clima != null && (tormenta
                        || _clima.climaActual == SistemaClima.EstadoClima.LluviaLigera);

        float objetivo = tormenta ? 1f : (lluvia ? 0.5f : 0f);
        _t = Mathf.MoveTowards(_t, objetivo, velReaccion * Time.deltaTime);

        // Propiedades HDRP Water (ajustar a tu versión si difieren los nombres).
        aguaRio.ripplesWindSpeed     = Mathf.Lerp(windCalma, windTormenta, _t);
        aguaRio.simulationFoamAmount = Mathf.Lerp(foamCalma, foamTormenta, _t);
#endif
    }

    void OnDestroy() { if (Instance == this) Instance = null; }
}
