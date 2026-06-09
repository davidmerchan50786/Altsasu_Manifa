// Assets/Scripts/SistemaAPVScenarios.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GI DINÁMICA — Adaptive Probe Volumes (APV) por hora del día
//  (Blueprint AAA+++, Pilar Render §2.1 — Fase 2)
//
//  Mezcla en runtime dos "lighting scenarios" de APV (Día ↔ Noche) usando el
//  global _GlobalNightLevel que ya publica SistemaVolumenHDRP. Resultado:
//  la iluminación INDIRECTA (rebotes, bounce en interiores) cambia con la hora,
//  no solo la directa. Es el mayor salto de calidad visual del roadmap.
//
//  ───────────────────────────────────────────────────────────────────────────
//  SETUP EN EL EDITOR (necesario una sola vez — no se puede automatizar por código):
//
//   1. HDRP Asset → Lighting → Light Probe Lighting → Probe Volumes = ON.
//   2. Project Settings → Quality → marca "Adaptive Probe Volumes".
//   3. En la escena: GameObject → Light → Adaptive Probe Volume (cubre la ciudad).
//   4. Window → Rendering → Lighting → pestaña "Probe Volumes":
//        · Crea un Baking Set y añade la escena.
//        · Lighting Scenarios → añade tres: "Day", "Dusk", "Night".
//        · Para cada uno: ajusta la luz direccional/sky a esa hora y pulsa
//          "Generate Lighting" (bakea ese scenario).
//   5. Player Settings → Scripting Define Symbols → añade:  ALSASUA_APV
//      (esto ACTIVA el código de abajo; sin el símbolo, este componente es un
//       no-op seguro y el proyecto compila igual).
//   6. Añade este componente a un GameObject de la escena (p.ej. "WorldManager").
//
//  Sin el símbolo ALSASUA_APV el componente se autodesactiva y avisa por log.
//  Así esta feature NUNCA puede romper la compilación antes de estar bakeada.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
#if ALSASUA_APV
using UnityEngine.Rendering;
#endif

[DefaultExecutionOrder(-60)] // tras SistemaVolumenHDRP(-80), que ya fija _GlobalNightLevel
public class SistemaAPVScenarios : MonoBehaviour
{
    public static SistemaAPVScenarios Instance { get; private set; }

    [Header("Nombres de los Lighting Scenarios bakeados")]
    [Tooltip("Scenario base (mediodía). Debe coincidir EXACTO con el nombre en el Baking Set.")]
    public string escenarioDia   = "Day";
    [Tooltip("Scenario nocturno. Se mezcla sobre el de día según _GlobalNightLevel.")]
    public string escenarioNoche = "Night";

    [Tooltip("Suavizado del factor de mezcla (mayor = sigue antes a la hora).")]
    public float velocidadBlend = 1.5f;

    float _blend; // 0 = día, 1 = noche (suavizado)

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
#if ALSASUA_APV
        var apv = ProbeReferenceVolume.instance;
        if (apv == null)
        {
            AlsasuaLogger.Warn("APV", "ProbeReferenceVolume no disponible — ¿APV activado en el HDRP Asset?");
            enabled = false;
            return;
        }
        apv.lightingScenario = escenarioDia;   // scenario base
        AlsasuaLogger.Info("APV", $"GI dinámica activa: '{escenarioDia}' ↔ '{escenarioNoche}'.");
#else
        AlsasuaLogger.Info("APV",
            "APV inactivo (define ALSASUA_APV no presente). Sigue la guía de setup en la cabecera del script.");
        enabled = false;
#endif
    }

    void Update()
    {
#if ALSASUA_APV
        float objetivo = Mathf.Clamp01(Shader.GetGlobalFloat("_GlobalNightLevel"));
        _blend = Mathf.MoveTowards(_blend, objetivo, velocidadBlend * Time.deltaTime);

        var apv = ProbeReferenceVolume.instance;
        if (apv != null)
            apv.BlendLightingScenario(escenarioNoche, _blend); // mezcla día→noche
#endif
    }

    void OnDestroy() { if (Instance == this) Instance = null; }
}
