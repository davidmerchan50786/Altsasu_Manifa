// SistemaMoralManifestacion.cs — Moral de la multitud + integración con facciones
// ═══════════════════════════════════════════════════════════════════════════
//  Capa GAMEPLAY. Componente hermano de SistemaManifestacion (mismo GO).
//
//  Responsabilidades:
//  - Moral 0–100 de la manifestación activa. Sube con: tiempo aguantado,
//    jugador presente, barricadas. Baja con: cargas policiales.
//  - Integra IFactionService: el multiplicador de reclutamiento escala el nº
//    de manifestantes ANTES de iniciar; reputación con Morea/Komuntza da
//    resistencia extra a cargas (diseño: Docs/Narrativa_Facciones_TMEO_Vol2.md).
//  - Reacción a cargas: desplaza el centro de boids para que la multitud
//    huya del origen de la carga de forma natural (sin tocar cada agente).
//  - Moral 0 → desbandada (TerminarManifestacion + evento de derrota).
//
//  Event-driven: Update solo corre mientras hay manifestación activa.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[RequireComponent(typeof(SistemaManifestacion))]
public class SistemaMoralManifestacion : MonoBehaviour
{
    [Header("Moral")]
    [Range(0, 100)] [SerializeField] float moral = 70f;
    [Tooltip("Moral ganada por minuto aguantando")]
    [SerializeField] float regeneracionPorMinuto = 4f;
    [Tooltip("Moral perdida por carga a intensidad 1.0")]
    [SerializeField] float danoPorCarga = 25f;
    [Tooltip("Bono de regeneración si el jugador está a menos de este radio del centro")]
    [SerializeField] float radioPresenciaJugador = 40f;
    [SerializeField] float bonoPresenciaJugador = 3f;

    [Header("Huida en cargas")]
    [Tooltip("Metros que retrocede el centro de la multitud por carga a intensidad 1.0")]
    [SerializeField] float retrocesoPorCarga = 25f;

    [Header("Recompensas")]
    [Tooltip("Apoyo popular por minuto de manifestación viva")]
    [SerializeField] float apoyoPorMinuto = 1.5f;

    SistemaManifestacion _manifestacion;
    IFactionService _facciones;
    Transform _jugador;
    float _timerMinuto;
    float _inicioManifestacion;
    bool _activa;
    int _numBaseManifestantes = -1;   // valor original del Inspector, capturado una vez

    public float Moral => moral;

    void Awake()
    {
        _manifestacion = GetComponent<SistemaManifestacion>();
        enabled = false;   // se activa con la manifestación
    }

    void Start()
    {
        _facciones = ServiceLocator.Get<IFactionService>();
        // Localizar jugador una sola vez en Start (no en Update — convención del proyecto)
        var goJugador = GameObject.FindGameObjectWithTag("Player");
        if (goJugador != null) _jugador = goJugador.transform;
    }

    void OnEnable()
    {
        EventBus.Subscribe<CargaPolicialEvent>(OnCarga);
        EventBus.Subscribe<AvisoCargaPolicialEvent>(OnAviso);
    }

    void OnDisable()
    {
        EventBus.Unsubscribe<CargaPolicialEvent>(OnCarga);
        EventBus.Unsubscribe<AvisoCargaPolicialEvent>(OnAviso);
    }

    // ── API pública ──────────────────────────────────────────────────────────

    /// <summary>
    /// Llamar ANTES de IniciarManifestacion(). Escala el tamaño de la
    /// convocatoria según la propaganda y reputación de facciones.
    /// </summary>
    public void PrepararConvocatoria()
    {
        if (_numBaseManifestantes < 0)
            _numBaseManifestantes = _manifestacion.numManifestantes;

        float mult = _facciones?.MultiplicadorReclutamiento ?? 1f;
        _manifestacion.numManifestantes =
            Mathf.Clamp(Mathf.RoundToInt(_numBaseManifestantes * mult), 20, 200);

        moral = 70f;
        _inicioManifestacion = Time.time;
        _timerMinuto = 0f;
        _activa = true;
        enabled = true;

        // Avisar a NPCs y sistemas de que la manifestación arranca (foco + participantes).
        EventBus.Publish(new ManifestacionIniciadaEvent
        {
            centro         = _manifestacion.centroManifestacion,
            radio          = 140f,
            participantes  = _manifestacion.numManifestantes,
        });

        AlsasuaLogger.Info("MoralManifa",
            $"Convocatoria x{mult:F2} → {_manifestacion.numManifestantes} manifestantes");
    }

    public void NotificarFin(bool porCarga)
    {
        if (!_activa) return;
        _activa = false;
        enabled = false;

        EventBus.Publish(new ManifestacionTerminadaEvent
        {
            dispersadaPorCarga   = porCarga,
            duracionSegundos     = Time.time - _inicioManifestacion,
            participantesFinales = _manifestacion.numManifestantes,
        });

        // Aguantar entera sin desbandada refuerza a quien convoca
        if (!porCarga && _facciones != null)
        {
            _facciones.ModificarReputacion(FaccionId.GazteSutegi,   3f, "Manifestación aguantada");
            _facciones.ModificarReputacion(FaccionId.MoreaBilgunea, 2f, "Logística impecable");
        }
    }

    // ── Lógica ───────────────────────────────────────────────────────────────

    void Update()
    {
        if (!_activa || !_manifestacion.EnCurso) return;

        _timerMinuto += Time.deltaTime;
        if (_timerMinuto < 60f) return;
        _timerMinuto = 0f;

        // Tick por minuto: regeneración + presencia del jugador + apoyo popular
        float regen = regeneracionPorMinuto;
        if (_jugador != null &&
            (_jugador.position - _manifestacion.centroManifestacion).sqrMagnitude
                < radioPresenciaJugador * radioPresenciaJugador)
            regen += bonoPresenciaJugador;

        CambiarMoral(regen);
        SistemaApoyoPopular.Instance?.SumarApoyo(apoyoPorMinuto, "Manifestación en curso");
    }

    void OnAviso(AvisoCargaPolicialEvent evt)
    {
        // La multitud se tensa: pequeña pérdida anticipada (el miedo es libre)
        CambiarMoral(-3f);
    }

    void OnCarga(CargaPolicialEvent evt)
    {
        if (!_activa) return;

        // Resistencia por facciones: Morea (organización) y Komuntza (disciplina de choque)
        float resistencia = 1f;
        if (_facciones != null)
        {
            float repDefensa = (_facciones.GetReputacion(FaccionId.MoreaBilgunea) +
                                _facciones.GetReputacion(FaccionId.Komuntza)) * 0.5f;
            resistencia = Mathf.Lerp(1.3f, 0.7f, repDefensa / 100f); // rep 100 → -30% daño
        }

        CambiarMoral(-danoPorCarga * evt.intensidad * resistencia);

        // La multitud huye: desplazar el objetivo de boids lejos del origen de la carga.
        // Los agentes siguen el centro vía pesoObjetivo → huida emergente, sin tocar IAs.
        Vector3 huida = evt.direccion.sqrMagnitude > 0.01f
            ? evt.direccion
            : (_manifestacion.centroManifestacion - evt.origen).normalized;
        huida.y = 0f;

        var nuevoCentro = _manifestacion.centroManifestacion
                        + huida.normalized * (retrocesoPorCarga * evt.intensidad);
        if (Terrain.activeTerrain != null)
            nuevoCentro.y = Terrain.activeTerrain.SampleHeight(nuevoCentro) + 0.1f;
        _manifestacion.centroManifestacion = nuevoCentro;

        if (moral <= 0f) Desbandada();
    }

    void CambiarMoral(float delta)
    {
        float anterior = moral;
        moral = Mathf.Clamp(moral + delta, 0f, 100f);
        if (Mathf.Approximately(anterior, moral)) return;
        EventBus.Publish(new MoralManifestacionEvent { moral = moral, delta = delta });
    }

    void Desbandada()
    {
        AlsasuaLogger.Info("MoralManifa", "Desbandada — la carga rompe la manifestación");
        SistemaApoyoPopular.Instance?.RestarApoyo(10f, "Manifestación dispersada");
        SistemaApoyoPopular.Instance?.SumarParanoia(15f);
        NotificarFin(porCarga: true);
        _manifestacion.TerminarManifestacion();
    }
}
