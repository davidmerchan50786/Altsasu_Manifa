// Assets/Scripts/PoliciaForalIA.cs
// ═══════════════════════════════════════════════════════════════════════════
//  POLICÍA FORAL — IA de detección por trazado de rayos real
//
//  Gorka Pillar 3 traducido a Unity:
//    · Gorka (UE5): Overlap Event con esfera invisible → jugador detectado
//    · Unity AAA:   Cono de visión + Raycast multi-punto → LOS físico real
//      "La Policía Foral no te encontrará porque tocaste una esfera invisible,
//       sino porque su línea de visión real te iluminó en el callejón."
//
//  Estados: Patrullando → Sospechoso → Persiguiendo → Atacando → Muerto
//
//  Setup en el Editor:
//    · RequireComponent: NavMeshAgent + CapsuleCollider
//    · Asigna waypoints de patrulla en el array
//    · Asigna una Light hijo como linterna (opcional, modo nocturno)
//    · La IA llama a GameManagerAltsasua.AumentarBusqueda() al confirmar avistamiento
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using Alsasua.GOAP;
using Alsasua.IA;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(CapsuleCollider))]
public class PoliciaForalIA : NPCBase, IDamageable
{
    // Tipo espacial para el Omni-Grid cuando es Ghost (la base NPCBase es Manifestante).
    protected override TipoEspacial TipoEspacialSim => TipoEspacial.Policia;

    // ── Estado ────────────────────────────────────────────────────────────────
    public enum EstadoPolicia
    {
        Patrullando,
        Sospechoso,      // vio algo, se acerca a investigar
        Persiguiendo,    // confirmado: hay delito
        Atacando,
        Muerto
    }

    // ── Visión ────────────────────────────────────────────────────────────────
    [Header("═══ VISIÓN ═══")]
    [Tooltip("Radio máximo de visión diurna (m).")]
    [SerializeField] private float radioVision      = 22f;
    [Tooltip("Ángulo total del cono de visión frontal (°). 90 = ±45° del frente.")]
    [SerializeField] private float anguloVision     = 90f;
    [Tooltip("Radio de escucha (m). Detecta al jugador sin LOS si está muy cerca.")]
    [SerializeField] private float radioEscucha     = 5f;
    [Tooltip("Puntos de LOS: 0=pies, 1=pecho, 2=cabeza. Más puntos = más robusto.")]
    [SerializeField] private float[] alturasLOS     = { 0.1f, 0.85f, 1.6f };
    [Tooltip("Capas que bloquean la visión (paredes, coches). No incluir 'Player'.")]
    [SerializeField] private LayerMask capasObstaculo = ~0;

    [Header("═══ LINTERNA (nocturna) ═══")]
    [Tooltip("Light hijo del policía. Si está asignada, se activa de noche.")]
    [SerializeField] private Light linterna;
    [Tooltip("Radio del cono de linterna de noche (m). Menor que el diurno.")]
    [SerializeField] private float radioLinterna    = 12f;
    [Tooltip("Ángulo del haz de linterna (°). Más estrecho pero prioritario de noche.")]
    [SerializeField] private float anguloLinterna   = 30f;

    // ── Patrulla ──────────────────────────────────────────────────────────────
    [Header("═══ PATRULLA ═══")]
    [Tooltip("Waypoints de la ruta de patrulla (en bucle).")]
    [SerializeField] private Transform[] waypoints;
    [Tooltip("Tiempo de espera en cada waypoint (s).")]
    [SerializeField] private float tiempoEsperaWP   = 3f;

    // ── Movimiento ────────────────────────────────────────────────────────────
    [Header("═══ MOVIMIENTO ═══")]
    [Tooltip("Velocidad en modo patrulla (m/s).")]
    [SerializeField] private float velPatrulla       = 1.4f;
    [Tooltip("Velocidad en persecución (m/s).")]
    [SerializeField] private float velPerseguir      = 5.2f;

    // ── Combate ───────────────────────────────────────────────────────────────
    [Header("═══ COMBATE ═══")]
    [Tooltip("Puntos de vida del policía.")]
    [SerializeField] private int   vida              = 120;
    [Tooltip("Daño por impacto al jugador.")]
    [SerializeField] private int   danoPorDisparo    = 20;
    [Tooltip("Segundos entre disparos.")]
    [SerializeField] private float cadencia          = 1.4f;
    [Tooltip("Radio máximo de ataque (m).")]
    [SerializeField] private float radioAtaque       = 16f;
    [Tooltip("Dispersión del disparo (0 = puntería perfecta, 0.08 = normal).")]
    [SerializeField] private float dispersion        = 0.05f;

    // ── GOAP (decisión táctica tras confirmar al jugador) ──────────────────────
    [Header("═══ GOAP ═══")]
    [Tooltip("Si está activo, una vez confirmado el jugador la POSTURA (arrestar / " +
             "llamar refuerzos / replegarse) la decide el planificador GOAP según el " +
             "apoyo popular y la distancia, en vez de la persecución fija. La detección " +
             "por LOS y el combate siguen siendo del FSM. Desactívalo para volver a la FSM clásica.")]
    [SerializeField] private bool      usarGOAP        = true;
    [Tooltip("Punto de cobertura alcanzable. Si es null, la policía nunca se repliega (captura siempre).")]
    [SerializeField] private Transform coberturaCercana;
    [Tooltip("Segundos entre replanificaciones GOAP durante la persecución.")]
    [SerializeField] private float     replanGOAP      = 0.5f;
    [Tooltip("Radio (m) al que se considera al jugador 'en rango de arresto'.")]
    [SerializeField] private float     radioArresto    = 2.2f;

    // ── Estado interno ────────────────────────────────────────────────────────
    private EstadoPolicia  estado      = EstadoPolicia.Patrullando;
    // ARCH: usa IWantedSystem en lugar de GameManagerAltsasua — elimina dependencia
    //       directa de Gameplay→GameManager. Se resuelve desde ServiceLocator.
    private IWantedSystem  _wantedSystem;

    // Alias para compatibilidad con código existente — NPCBase ya declara _jugador/_controlJugador/_agente
    private Transform          jugador          => _jugador;
    private ControladorJugador controlJugador   => _controlJugador;
    private NavMeshAgent       agente           => _agente;

    private int   wpActual      = 0;
    private float timerEspera   = 0f;
    private float timerSospecha = 0f;
    private float timerAtaque   = 0f;
    private Vector3 ultimaPosJugador;

    // PERF: cada SetDestination encola un recálculo de path asíncrono. En persecución se
    // llamaba cada frame (60Hz × N policías → satura la cola del NavMesh). Solo re-pathear
    // cuando el destino se mueve > 0.5 m respecto al último. Se resetea en cada transición.
    private static readonly Vector3 SIN_DESTINO = new Vector3(float.PositiveInfinity, 0f, 0f);
    private Vector3 _ultimoDestinoNav = SIN_DESTINO;

    private void RepathSiNecesario(Vector3 destino)
    {
        if ((destino - _ultimoDestinoNav).sqrMagnitude < 0.25f) return;  // umbral 0.5 m
        _ultimoDestinoNav = destino;
        agente.SetDestination(destino);
    }

    private int              maskObstaculo;
    private SistemaAtmosfera _atmosfera;

    // ── GOAP: planificador + contexto + sets (todo alocado UNA vez en OnStart) ──
    private enum PosturaGOAP { Arrestar, LlamarRefuerzos, Replegarse }
    private PlanificadorGOAP _goap;
    private ContextoPolicia  _ctxGoap;
    private ISpawnService    _spawnService;       // para spawnear refuerzos reales
    private IAction[]        _accionesGoap;
    private IGoal[]          _metasGoap;
    private IAction          _accRefuerzos;       // referencia para identificar la acción en el plan
    private readonly IAction[] _planGoap = new IAction[16];
    private int              _planLenGoap;
    private float            _timerGoap;
    private PosturaGOAP      _postura = PosturaGOAP.Arrestar;
    private IGoal            _metaActualGoap;
    private bool             _refuerzosPedidos;

    // ── Detección batch (SistemaDeteccionIA) ──────────────────────────────────
    private int   _slotDeteccion  = -1;    // slot en SistemaDeteccionIA (-1 = no registrado)
    private int   _visionFrame    = -999;  // último frame en que se enviaron comandos
    private const int VISION_TICK = 3;     // enviar raycasts cada 3 frames (~20 Hz)

    // ── Propiedades públicas ───────────────────────────────────────────────────
    public EstadoPolicia Estado         => estado;
    public bool          JugadorVisto   => estado == EstadoPolicia.Persiguiendo
                                        || estado == EstadoPolicia.Atacando;

    // IDamageable
    public int  Vida        => vida;
    public int  VidaMax     => 120; // valor inicial del SerializeField
    public bool EstaMuerto  => estado == EstadoPolicia.Muerto;

    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        velocidadBase   = velPatrulla;
        velocidadMaxima = velPerseguir;
        base.Awake();
        maskObstaculo = capasObstaculo & ~LayerMask.GetMask("Player");
    }

    // Modelo por defecto = Guardia Civil real (NPC_GuardiaCivil / GC_*) en vez de
    // un civil. Funciona para policías spawneadas en cualquier momento, no solo al
    // arranque, porque NPCBase tira de esto cuando prefabModelo no está asignado.
    protected override GameObject ObtenerModeloPorDefecto()
        => SistemaAssets.Instance != null
            ? SistemaAssets.Instance.GuardiaAleatorio()
            : null;

    private IEnumerator BuscarAtmosfera()
    {
        float t = 0f;
        while (_atmosfera == null && t < 10f)
        {
            _atmosfera = AltsasuCore.I?.atmosferaSystem
                      ?? FindFirstObjectByType<SistemaAtmosfera>();
            if (_atmosfera != null) yield break;
            t += 0.5f;
            yield return new WaitForSeconds(0.5f);
        }
        if (_atmosfera == null)
            AlsasuaLogger.Warn("PoliciaForal", "SistemaAtmosfera no encontrado — noche desactivada");
    }

    protected override void OnStart()
    {
        StartCoroutine(BuscarAtmosfera());
        _wantedSystem  = ServiceLocator.Get<IWantedSystem>();
        _slotDeteccion = SistemaDeteccionIA.Registrar();
        if (linterna != null) linterna.enabled = false;

        if (usarGOAP) InicializarGOAP();
    }

    private void InicializarGOAP()
    {
        _goap         = new PlanificadorGOAP(64);
        _spawnService = ServiceLocator.Get<ISpawnService>();
        _ctxGoap = new ContextoPolicia { nav = _agente, wanted = _wantedSystem, radioArresto = radioArresto };

        _accRefuerzos = new LlamarRefuerzosAction();
        _accionesGoap = new IAction[]
        {
            new PerseguirAction(),
            new ArrestarJugadorAction(),
            _accRefuerzos,
            new MoverACoberturaAction(),
        };
        _metasGoap = new IGoal[]
        {
            new MetaCapturarJugador(),
            new MetaReplegarse(),
        };
    }

    // BUG FIX (auditoría): liberar el slot de detección al destruirse para que se
    // reutilice. Antes los slots sólo crecían → tras 32 policías, los nuevos ciegos.
    protected override void OnDestroy()
    {
        if (_slotDeteccion >= 0) { SistemaDeteccionIA.Liberar(_slotDeteccion); _slotDeteccion = -1; }
        base.OnDestroy();
    }

    protected override void AlActivarAgente()
    {
        _agente.speed            = velPatrulla;
        _agente.stoppingDistance = 1.5f;
        AlsasuaLogger.Info("PoliciaForal", $"{name}: NavMesh detectado — iniciando patrulla.");
        IrAlSiguienteWP();
    }

    protected override void ActualizarComportamiento()
    {
        if (estado == EstadoPolicia.Muerto) return;

        ActualizarLinterna();

        // INFLUENCIA SOCIAL: mientras es agresiva, la policía se reporta como
        // antagonista → los manifestantes radicalizados (opinión alta) se interponen.
        if (estado == EstadoPolicia.Persiguiendo || estado == EstadoPolicia.Atacando)
            InfluenciaSocial.ReportarAntagonista(GetInstanceID(), transform.position);

        switch (estado)
        {
            case EstadoPolicia.Patrullando:  TickPatrulla();    break;
            case EstadoPolicia.Sospechoso:   TickSospecha();    break;
            case EstadoPolicia.Persiguiendo:
                if (usarGOAP && _goap != null) TickPersecucionGOAP();
                else                           TickPersecucion();
                break;
            case EstadoPolicia.Atacando:     TickAtaque();      break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GORKA PILLAR 3 — DETECCIÓN POR RAYCAST REAL
    //  En lugar del Overlap Event de Gorka, usamos múltiples rayos a distintas
    //  alturas del jugador para simular visión real con posibilidad de cobertura.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Devuelve true si el jugador está en el cono de visión Y con línea de visión despejada.
    /// De noche se usa el cono de linterna (más estrecho, menor radio).
    /// </summary>
    private bool JugadorEnVision()
    {
        if (jugador == null) return false;

        // ── 1. Comprobación de radio y ángulo de cono (sin raycast — O(1)) ────
        bool  esDeNoche = EsDeNoche();
        float radioAct  = esDeNoche ? radioLinterna  : radioVision;
        float anguloAct = esDeNoche ? anguloLinterna : anguloVision;

        Vector3 oriEye = transform.position + Vector3.up * 1.65f;
        Vector3 dirJug = jugador.position - oriEye;
        float   dist   = dirJug.magnitude;

        if (dist > radioAct) return false;
        if (Vector3.Angle(transform.forward, dirJug) > anguloAct * 0.5f) return false;

        // ── 2. Enviar raycasts al batch (cada VISION_TICK frames) ─────────────
        int frame = Time.frameCount;
        if (frame - _visionFrame >= VISION_TICK)
        {
            _visionFrame = frame;
            SistemaDeteccionIA.EscribirComandos(
                _slotDeteccion, oriEye, jugador, alturasLOS, maskObstaculo);
        }

        // ── 3. Leer resultado del job ejecutado el frame anterior ─────────────
        return SistemaDeteccionIA.TieneVision(_slotDeteccion);
    }

    /// <summary>Detección por proximidad sonora (pasos, disparo cercano).</summary>
    private bool JugadorEnEscucha()
        => jugador != null
        && Vector3.Distance(transform.position, jugador.position) <= radioEscucha;

    // ─────────────────────────────────────────────────────────────────────────
    //  MÁQUINA DE ESTADOS
    // ─────────────────────────────────────────────────────────────────────────

    private void TickPatrulla()
    {
        agente.speed = velPatrulla;

        if (!agente.pathPending && agente.remainingDistance <= agente.stoppingDistance)
        {
            timerEspera -= Time.deltaTime;
            if (timerEspera <= 0f) IrAlSiguienteWP();
        }

        if (JugadorEnVision() || JugadorEnEscucha())
            CambiarEstado(EstadoPolicia.Sospechoso);
    }

    private void TickSospecha()
    {
        // Girar lentamente hacia donde vio/escuchó
        if (jugador != null)
        {
            Vector3 dirS = jugador.position - transform.position;
            dirS.y = 0f; dirS = dirS.normalized;
            if (dirS != Vector3.zero)
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, Quaternion.LookRotation(dirS), 4f * Time.deltaTime);
        }

        timerSospecha -= Time.deltaTime;

        if (JugadorEnVision())
        {
            // Confirmado con LOS → perseguir y avisar al GameManager
            if (jugador != null) ultimaPosJugador = jugador.position;
            _wantedSystem?.AumentarBusqueda(1);
            CambiarEstado(EstadoPolicia.Persiguiendo);
        }
        else if (timerSospecha <= 0f)
        {
            // Perdido de vista → vuelve a patrulla
            CambiarEstado(EstadoPolicia.Patrullando);
        }
    }

    private void TickPersecucion()
    {
        agente.speed = velPerseguir;

        if (jugador != null)
        {
            ultimaPosJugador = jugador.position;
            RepathSiNecesario(ultimaPosJugador);
            float dist = Vector3.Distance(transform.position, jugador.position);
            if (dist <= radioAtaque) { CambiarEstado(EstadoPolicia.Atacando); return; }
        }
        else
        {
            // Sin referencia al jugador → ir a última posición conocida
            RepathSiNecesario(ultimaPosJugador);
            if (!agente.pathPending && agente.remainingDistance <= agente.stoppingDistance)
                CambiarEstado(EstadoPolicia.Patrullando);
        }

        // Si pierde visión y escucha durante 6 s → vuelve a patrulla
        if (!JugadorEnVision() && !JugadorEnEscucha())
        {
            timerSospecha -= Time.deltaTime;
            if (timerSospecha < -6f) CambiarEstado(EstadoPolicia.Patrullando);
        }
        else timerSospecha = 0f;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PERSECUCIÓN GOAP — el FSM ya confirmó al jugador (LOS real). Aquí el
    //  planificador decide la POSTURA según apoyo popular/distancia y el FSM la
    //  ejecuta con sus primitivas probadas (NavMesh + combate). GOAP planifica;
    //  PoliciaForalIA actúa → un único dueño del NavMeshAgent, sin conflictos.
    // ─────────────────────────────────────────────────────────────────────────
    private void TickPersecucionGOAP()
    {
        agente.speed = velPerseguir;

        SensarGOAP();
        _timerGoap += Time.deltaTime;
        if (_timerGoap >= replanGOAP) { _timerGoap = 0f; PlanificarGOAP(); }

        if (jugador != null) ultimaPosJugador = jugador.position;

        switch (_postura)
        {
            case PosturaGOAP.Replegarse:
                agente.isStopped = false;
                RepathSiNecesario(_ctxGoap.posCobertura);
                break;

            case PosturaGOAP.LlamarRefuerzos:
                if (!_refuerzosPedidos) LlamarRefuerzos();
                PerseguirYAtacarGOAP();
                break;

            default: // Arrestar → perseguir y, en rango de tiro, combatir (FSM)
                PerseguirYAtacarGOAP();
                break;
        }

        // Pérdida de visión y escucha → mismo timeout de 6 s que el FSM clásico.
        if (!JugadorEnVision() && !JugadorEnEscucha())
        {
            timerSospecha -= Time.deltaTime;
            if (timerSospecha < -6f) { ReiniciarGOAP(); CambiarEstado(EstadoPolicia.Patrullando); }
        }
        else timerSospecha = 0f;
    }

    private void PerseguirYAtacarGOAP()
    {
        if (jugador == null)
        {
            RepathSiNecesario(ultimaPosJugador);
            if (!agente.pathPending && agente.remainingDistance <= agente.stoppingDistance)
            { ReiniciarGOAP(); CambiarEstado(EstadoPolicia.Patrullando); }
            return;
        }
        RepathSiNecesario(ultimaPosJugador);
        if (Vector3.Distance(transform.position, jugador.position) <= radioAtaque)
            CambiarEstado(EstadoPolicia.Atacando);   // el combate sigue siendo del FSM
    }

    // Refresca el contexto con sensores REALES; tieneLOS = el raycast multipunto.
    private void SensarGOAP()
    {
        _ctxGoap.posAgente     = transform.position;
        if (jugador != null) _ctxGoap.posJugador = jugador.position;
        _ctxGoap.hayCobertura  = coberturaCercana != null;
        _ctxGoap.posCobertura  = _ctxGoap.hayCobertura ? coberturaCercana.position : _ctxGoap.posAgente;
        _ctxGoap.nivelBusqueda = _wantedSystem != null ? _wantedSystem.NivelBusqueda : 0;
        var sap = SistemaApoyoPopular.Instance;
        _ctxGoap.apoyo01       = sap != null ? Mathf.Clamp01(sap.apoyo / 100f) : 0.5f;
        _ctxGoap.tieneLOS      = JugadorEnVision();
    }

    private void PlanificarGOAP()
    {
        // 1. Meta relevante de mayor prioridad.
        IGoal mejor = null;
        float mejorP = float.NegativeInfinity;
        for (int i = 0; i < _metasGoap.Length; i++)
        {
            var m = _metasGoap[i];
            if (!m.EsRelevante(_ctxGoap)) continue;
            float p = m.Prioridad(_ctxGoap);
            if (p > mejorP) { mejorP = p; mejor = m; }
        }
        _metaActualGoap = mejor;
        if (mejor == null) { _postura = PosturaGOAP.Arrestar; _planLenGoap = 0; return; }

        // 2. Planificar (zero-alloc) hacia esa meta.
        EstadoMundo inicial = LeerEstadoGOAP();
        _planLenGoap = _goap.Planificar(inicial, mejor.Objetivo, _accionesGoap, _ctxGoap, _planGoap);

        // 3. Traducir meta + plan a una POSTURA que el FSM sabe ejecutar.
        if (mejor is MetaReplegarse)       _postura = PosturaGOAP.Replegarse;
        else if (PlanContiene(_accRefuerzos)) _postura = PosturaGOAP.LlamarRefuerzos;
        else                               _postura = PosturaGOAP.Arrestar;
    }

    private bool PlanContiene(IAction accion)
    {
        for (int i = 0; i < _planLenGoap; i++)
            if (ReferenceEquals(_planGoap[i], accion)) return true;
        return false;
    }

    private EstadoMundo LeerEstadoGOAP()
    {
        bool enRango = jugador != null &&
                       Vector3.Distance(transform.position, jugador.position) <= radioArresto;
        EstadoMundo e = default;
        e.Set((int)HechoPol.VeAlJugador,         _ctxGoap.tieneLOS);
        e.Set((int)HechoPol.JugadorEnRango,      enRango);
        e.Set((int)HechoPol.EnCobertura,         false);
        e.Set((int)HechoPol.RefuerzosPedidos,    _refuerzosPedidos);
        e.Set((int)HechoPol.JugadorNeutralizado, false);
        return e;
    }

    private void LlamarRefuerzos()
    {
        _refuerzosPedidos = true;   // un intento por enfrentamiento; el cooldown global hace el anti-spam

        // Oleada de refuerzos escalada por nivel de búsqueda (GameManagerAltsasua).
        _spawnService ??= ServiceLocator.Get<ISpawnService>();
        int llegan = _spawnService?.SolicitarRefuerzosPolicia(transform.position, 1) ?? 0;
        if (llegan > 0)
        {
            _wantedSystem?.AumentarBusqueda(1);   // solo sube el calor si la oleada sale de verdad
            AlsasuaLogger.Info("PoliciaForal", $"{name}: oleada de refuerzos → {llegan} en camino.");
        }
    }

    private void ReiniciarGOAP()
    {
        _refuerzosPedidos = false;
        _postura = PosturaGOAP.Arrestar;
        _planLenGoap = 0;
    }

    private void TickAtaque()
    {
        // agente.ResetPath() ya se llama en CambiarEstado(Atacando) — no repetir cada frame
        if (jugador == null) { CambiarEstado(EstadoPolicia.Patrullando); return; }

        float dist = Vector3.Distance(transform.position, jugador.position);

        // Rotar hacia el jugador
        Vector3 dirA = jugador.position - transform.position;
        dirA.y = 0f; dirA = dirA.normalized;
        if (dirA != Vector3.zero)
            transform.rotation = Quaternion.Slerp(
                transform.rotation, Quaternion.LookRotation(dirA), 8f * Time.deltaTime);

        if (dist > radioAtaque * 1.35f) { CambiarEstado(EstadoPolicia.Persiguiendo); return; }

        timerAtaque -= Time.deltaTime;
        if (timerAtaque <= 0f) { Disparar(); timerAtaque = cadencia; }
    }

    private void CambiarEstado(EstadoPolicia nuevo)
    {
        var anterior = estado;
        estado = nuevo;
        agente.isStopped = (nuevo == EstadoPolicia.Atacando);

        // INFLUENCIA SOCIAL: la carga policial (entrar en estado agresivo desde uno
        // que no lo era) radicaliza a la multitud → pozo de gravedad social positivo.
        bool eraAgresivo = anterior == EstadoPolicia.Persiguiendo || anterior == EstadoPolicia.Atacando;
        bool esAgresivo  = nuevo    == EstadoPolicia.Persiguiendo || nuevo    == EstadoPolicia.Atacando;
        if (esAgresivo && !eraAgresivo)
            InfluenciaSocial.Emitir(transform.position, 0.6f, 22f);
        // PERF: forzar re-path en la próxima llamada — la transición invalida el destino previo.
        _ultimoDestinoNav = SIN_DESTINO;

        // Resetear timer según estado — evita semántica dual no determinista
        if (nuevo == EstadoPolicia.Sospechoso)   timerSospecha = 2.8f;
        if (nuevo == EstadoPolicia.Persiguiendo) timerSospecha = 0f;   // timeout de 6 s limpio
        if (nuevo == EstadoPolicia.Atacando)     agente.ResetPath();   // solo en la transición
        if (nuevo == EstadoPolicia.Patrullando)  IrAlSiguienteWP();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  WAYPOINTS
    // ─────────────────────────────────────────────────────────────────────────

    private void IrAlSiguienteWP()
    {
        if (waypoints == null || waypoints.Length == 0)
        {
            AlsasuaLogger.Warn("PoliciaForal", $"{name}: sin waypoints — el policía no patrullará.");
            return;
        }
        wpActual = (wpActual + 1) % waypoints.Length;
        if (waypoints[wpActual] != null)
            agente.SetDestination(waypoints[wpActual].position);
        else
            AlsasuaLogger.Warn("PoliciaForal", $"{name}: waypoint[{wpActual}] es null.");
        timerEspera = tiempoEsperaWP;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  COMBATE
    // ─────────────────────────────────────────────────────────────────────────

    private void Disparar()
    {
        if (jugador == null || controlJugador == null || controlJugador.EstaMuerto) return;

        Vector3 ori = transform.position + Vector3.up * 1.4f;
        Vector3 dir = (jugador.position + Vector3.up * 0.9f - ori).normalized;

        // Dispersión aleatoria (renormalizar para que la magnitud no afecte al alcance)
        dir += new Vector3(
            Random.Range(-dispersion, dispersion),
            Random.Range(-dispersion * 0.5f, dispersion * 0.5f),
            Random.Range(-dispersion, dispersion));
        dir.Normalize();

        if (Physics.Raycast(ori, dir, out RaycastHit hit, radioAtaque * 1.5f,
                             Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            var jug = hit.collider.GetComponentInParent<ControladorJugador>();
            if (jug != null) jug.RecibirDano(danoPorDisparo);
        }
    }

    public void RecibirDano(int cantidad, Vector3 origen = default, TipoDano tipo = TipoDano.Bala)
    {
        if (estado == EstadoPolicia.Muerto) return;
        vida -= cantidad;
        if (vida <= 0) { Morir(); return; }
        // Si le disparan estando en patrulla → pasa directamente a perseguir
        if (estado == EstadoPolicia.Patrullando || estado == EstadoPolicia.Sospechoso)
            CambiarEstado(EstadoPolicia.Persiguiendo);
    }

    public void Curar(int cantidad) { } // la policía no se cura en gameplay

    private void Morir()
    {
        estado = EstadoPolicia.Muerto;
        if (agente != null) agente.isStopped = true;
        if (linterna != null) linterna.enabled = false;
        AudioManager.Play(AudioManager.Clip.ImpactoSangre, transform.position);
        AlsasuaLogger.Info("PoliciaForal", $"{name} abatido.");
        SistemaRagdoll.Activar(transform, 4f, -transform.forward);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    private bool EsDeNoche()
    {
        if (_atmosfera == null) return false;
        float h = _atmosfera.HoraDelDia;
        return h >= 20f || h < 6.5f; // noche = 20:00-06:30
    }

    private void ActualizarLinterna()
    {
        if (linterna == null) return;

        // FIX 2: throttling — solo recalcular estado y rotación de linterna cada 3 frames.
        // La linterna es decorativa; 20 actualizaciones/seg (a 60fps) son más que suficientes.
        // Reduce el coste de ActualizarLinterna() un ~66%: de 60 a 20 llamadas/seg.
        if (Time.frameCount % 3 != 0) return;

        bool debeEncenderse = EsDeNoche()
                           || estado == EstadoPolicia.Persiguiendo
                           || estado == EstadoPolicia.Atacando;
        linterna.enabled = debeEncenderse;

        if (debeEncenderse && jugador != null)
            linterna.transform.LookAt(jugador.position + Vector3.up * 0.9f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GIZMOS (visión en el Editor — Scene View)
    // ─────────────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Vector3 ori = transform.position + Vector3.up * 1.65f;

        // Cono de visión diurna
        Gizmos.color = estado == EstadoPolicia.Persiguiendo ? Color.red : Color.yellow;
        DibujarCono(ori, transform.forward, radioVision, anguloVision);

        // Radio de escucha
        Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, radioEscucha);

        // Cono de linterna (noche)
        if (linterna != null)
        {
            Gizmos.color = Color.white;
            DibujarCono(ori, transform.forward, radioLinterna, anguloLinterna);
        }

        // Radio de ataque
        Gizmos.color = new Color(1f, 0f, 0f, 0.15f);
        Gizmos.DrawWireSphere(transform.position, radioAtaque);
    }

    private void DibujarCono(Vector3 origen, Vector3 dir, float radio, float angulo)
    {
        float semi = angulo * 0.5f;
        Gizmos.DrawRay(origen, Quaternion.Euler(0,  semi, 0) * dir * radio);
        Gizmos.DrawRay(origen, Quaternion.Euler(0, -semi, 0) * dir * radio);
        Gizmos.DrawWireSphere(origen + dir * radio, 0.25f);
    }
}

