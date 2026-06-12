// Assets/Scripts/DirectorMundo.cs
// ═══════════════════════════════════════════════════════════════════════════
//  DIRECTOR DE MUNDO — AI Director de eventos dinámicos (estilo L4D)
//  (Blueprint AAA+++, Pilar Mundo y Simulación §3.3 — Fase 6)
//
//  Orquesta el ritmo del mundo según la "intensidad", derivada del nivel de
//  búsqueda (IWantedSystem) y del estado del movimiento (SistemaApoyoPopular:
//  apoyo bajo / paranoia alta = más tensión). Sigue un ciclo de pacing
//  (calma → acumulación → pico → relajación) y DIFUNDE eventos vía un evento
//  estático; no spawnea nada por sí mismo → cero acoplamiento.
//
//  CONSUMIRLO desde cualquier sistema:
//      void OnEnable()  => DirectorMundo.OnEvento += Reaccionar;
//      void OnDisable() => DirectorMundo.OnEvento -= Reaccionar;
//      void Reaccionar(DirectorMundo.EventoMundo e) { ... }
//
//  Ejemplos de reacción: PoliciaForalIA spawnea un control en ControlPolicial,
//  SistemaManifestacion lanza un disturbio en Disturbio, AudioManager mete un
//  stinger, SistemaMusicaAdaptativa ya sube solo por el nivel de búsqueda.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(35)]
public class DirectorMundo : MonoBehaviour
{
    public static DirectorMundo Instance { get; private set; }

    public enum EventoMundo { Calma, MercadoDia, PatrullaRefuerzo, ControlPolicial, Disturbio, Redada }

    /// <summary>Se dispara al entrar en un nuevo evento de mundo. Suscríbete para reaccionar.</summary>
    public static event System.Action<EventoMundo> OnEvento;

    [Header("Ajustes de pacing")]
    [Range(1, 8)] public int nivelBusquedaMax = 5;
    [Tooltip("Segundos entre evaluaciones del director.")]
    public float intervaloEval = 5f;
    [Tooltip("Cooldown (s) tras un evento grande — evita avalanchas.")]
    public float cooldownPico = 45f;

    float _intensidad;            // 0..1 suavizada
    float _cooldown;
    EventoMundo _estado = EventoMundo.Calma;

    /// <summary>Intensidad del mundo 0..1 (lectura pública para otros sistemas/grading).</summary>
    public static float IntensidadActual => Instance != null ? Instance._intensidad : 0f;
    /// <summary>Último evento difundido.</summary>
    public static EventoMundo EstadoActual => Instance != null ? Instance._estado : EventoMundo.Calma;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start() => StartCoroutine(BucleDirector());

    IEnumerator BucleDirector()
    {
        yield return new WaitForSeconds(8f); // dejar que el mundo arranque
        var espera = new WaitForSeconds(intervaloEval);
        AlsasuaLogger.Info("DirectorMundo", "AI Director activo.");
        while (true)
        {
            _cooldown = Mathf.Max(0f, _cooldown - intervaloEval);
            ActualizarIntensidad();
            DecidirEvento();
            yield return espera;
        }
    }

    void ActualizarIntensidad()
    {
        int wanted   = ServiceLocator.Get<IWantedSystem>()?.NivelBusqueda ?? 0;
        float w      = Mathf.Clamp01(wanted / (float)Mathf.Max(1, nivelBusquedaMax));

        float apoyo    = SistemaApoyoPopular.Instance != null ? SistemaApoyoPopular.Instance.apoyo    : 50f;
        float paranoia = SistemaApoyoPopular.Instance != null ? SistemaApoyoPopular.Instance.paranoia : 0f;

        // Apoyo bajo y paranoia alta elevan la tensión.
        float objetivo = Mathf.Clamp01(0.55f * w
                                     + 0.25f * (1f - apoyo / 100f)
                                     + 0.20f * (paranoia / 100f));
        _intensidad = Mathf.MoveTowards(_intensidad, objetivo, 0.15f);
    }

    void DecidirEvento()
    {
        if (_cooldown > 0f) return; // respetar el descanso tras un evento grande

        // ── Pico de tensión ────────────────────────────────────────────────
        if (_intensidad > 0.66f)
        {
            var ev = _intensidad > 0.85f ? EventoMundo.Redada : EventoMundo.ControlPolicial;
            Disparar(ev);
            _cooldown = cooldownPico;
            if (ev == EventoMundo.ControlPolicial)
                ServiceLocator.Get<IWantedSystem>()?.AumentarBusqueda(1); // montar un control sube la presión
            return;
        }

        // ── Tensión media ──────────────────────────────────────────────────
        if (_intensidad > 0.35f)
        {
            float apoyo = SistemaApoyoPopular.Instance != null ? SistemaApoyoPopular.Instance.apoyo : 50f;
            Disparar(apoyo < 35f ? EventoMundo.Disturbio : EventoMundo.PatrullaRefuerzo);
            _cooldown = cooldownPico * 0.5f;
            return;
        }

        // ── Calma: de día, día de mercado ocasional ────────────────────────
        float night = Shader.GetGlobalFloat("_GlobalNightLevel");
        if (night < 0.30f && Random.value < 0.25f)
        {
            Disparar(EventoMundo.MercadoDia);
            _cooldown = cooldownPico;
        }
        else if (_estado != EventoMundo.Calma)
        {
            Disparar(EventoMundo.Calma); // volver a calma una sola vez
        }
    }

    void Disparar(EventoMundo ev)
    {
        _estado = ev;
        OnEvento?.Invoke(ev);
        AlsasuaLogger.Info("DirectorMundo", $"Evento: {ev}  (intensidad={_intensidad:F2})");
    }

    void OnDestroy() { if (Instance == this) Instance = null; }
}
