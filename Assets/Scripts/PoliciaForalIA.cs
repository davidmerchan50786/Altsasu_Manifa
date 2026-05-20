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

using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(CapsuleCollider))]
public class PoliciaForalIA : MonoBehaviour
{
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

    // ── Estado interno ────────────────────────────────────────────────────────
    private EstadoPolicia      estado = EstadoPolicia.Patrullando;
    private NavMeshAgent       agente;
    private Transform          jugador;
    private ControladorJugador controlJugador;
    private GameManagerAltsasua gameManager;

    private int   wpActual      = 0;
    private float timerEspera   = 0f;
    private float timerSospecha = 0f;
    private float timerAtaque   = 0f;
    private Vector3 ultimaPosJugador;

    // Cachés de capas (se calculan en Awake, no cada frame)
    private int maskObstaculo; // capas que bloquean LOS (sin capa Player)

    // Caché de SistemaAtmosfera — FindFirstObjectByType es O(n); se cachea en Awake
    private SistemaAtmosfera _atmosfera;

    // ── Propiedades públicas ───────────────────────────────────────────────────
    public EstadoPolicia Estado         => estado;
    public bool          JugadorVisto   => estado == EstadoPolicia.Persiguiendo
                                        || estado == EstadoPolicia.Atacando;

    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        agente = GetComponent<NavMeshAgent>();
        agente.enabled = false; // esperar NavMesh
        maskObstaculo = capasObstaculo & ~LayerMask.GetMask("Player");
        // _atmosfera se busca en Start y con retry — AltsasuCore puede crearla después
    }

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

    private void Start()
    {
        StartCoroutine(BuscarAtmosfera());
        var jGO = GameObject.FindGameObjectWithTag("Player");
        if (jGO != null)
        {
            jugador        = jGO.transform;
            controlJugador = jGO.GetComponent<ControladorJugador>();
        }
        gameManager = FindFirstObjectByType<GameManagerAltsasua>();
        if (linterna != null) linterna.enabled = false;

        // Si el NavMesh ya está listo (p.ej. spawn tardío), activar directamente.
        // Si no, suscribirse al evento — el agente se activará cuando esté horneado.
        if (SistemaNavMesh.EstaListo)
            ActivarAgente();
        else
            SistemaNavMesh.OnNavMeshListo += ActivarAgente;
    }

    private void OnDestroy()
    {
        // Limpiar suscripción para evitar memory leaks
        SistemaNavMesh.OnNavMeshListo -= ActivarAgente;
    }

    private void ActivarAgente()
    {
        if (agente == null || agente.enabled) return;

        // Verificar que hay NavMesh debajo de este NPC antes de activar
        if (!SistemaNavMesh.PuntoEnNavMesh(transform.position, 3f))
        {
            // Fuera del área horneada — intentarlo cuando se rehornee
            AlsasuaLogger.Warn("PoliciaForal",
                $"{name}: no hay NavMesh en {transform.position}. " +
                "Esperando próximo rehorneado.");
            SistemaNavMesh.OnNavMeshListo += ActivarAgente;
            return;
        }

        agente.enabled         = true;
        agente.speed           = velPatrulla;
        agente.stoppingDistance = 1.5f;
        agente.angularSpeed    = 200f;

        AlsasuaLogger.Info("PoliciaForal", $"{name}: NavMesh detectado — iniciando patrulla.");
        IrAlSiguienteWP();
    }

    private void Update()
    {
        if (!agente.enabled) return;      // esperar a NavMesh
        if (estado == EstadoPolicia.Muerto) return;

        ActualizarLinterna();

        switch (estado)
        {
            case EstadoPolicia.Patrullando:  TickPatrulla();    break;
            case EstadoPolicia.Sospechoso:   TickSospecha();    break;
            case EstadoPolicia.Persiguiendo: TickPersecucion(); break;
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

        Vector3 oriEye = transform.position + Vector3.up * 1.65f; // altura de los ojos
        Vector3 dirJug = jugador.position - oriEye;
        float   dist   = dirJug.magnitude;

        // ── 1. Comprobación de radio y ángulo de cono ─────────────────────────
        bool esDeNoche = EsDeNoche();
        float radioAct  = esDeNoche ? radioLinterna  : radioVision;
        float anguloAct = esDeNoche ? anguloLinterna : anguloVision;

        if (dist > radioAct) return false;
        if (Vector3.Angle(transform.forward, dirJug) > anguloAct * 0.5f) return false;

        // ── 2. Raycast multi-punto (pies / pecho / cabeza) ────────────────────
        //   Si ALGUNO de los rayos llega al jugador sin obstáculo → detectado.
        // FIX 3: bucle for con índice en lugar de foreach sobre array de float[].
        // foreach sobre un array de valor (float[]) genera un enumerador en el heap cada
        // llamada → presión GC acumulada a 60fps. El for con índice es cero-alloc.
        for (int k = 0; k < alturasLOS.Length; k++)
        {
            Vector3 destino = jugador.position + Vector3.up * alturasLOS[k];
            Vector3 dir     = destino - oriEye;

            // El rayo ignora la capa Player para poder llegar hasta él
            if (!Physics.Raycast(oriEye, dir.normalized, dir.magnitude,
                                  maskObstaculo, QueryTriggerInteraction.Ignore))
            {
                return true; // Línea de visión despejada
            }
        }
        return false;
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
            ultimaPosJugador = jugador.position;
            gameManager?.AumentarBusqueda(1);
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
            agente.SetDestination(ultimaPosJugador);
            float dist = Vector3.Distance(transform.position, jugador.position);
            if (dist <= radioAtaque) { CambiarEstado(EstadoPolicia.Atacando); return; }
        }
        else
        {
            // Sin referencia al jugador → ir a última posición conocida
            agente.SetDestination(ultimaPosJugador);
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
        estado = nuevo;
        agente.isStopped = (nuevo == EstadoPolicia.Atacando);

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
        if (controlJugador == null || controlJugador.EstaMuerto) return;

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

    public void RecibirDano(int dano)
    {
        if (estado == EstadoPolicia.Muerto) return;
        vida -= dano;
        if (vida <= 0) { Morir(); return; }
        // Si le disparan estando en patrulla → pasa directamente a perseguir
        if (estado == EstadoPolicia.Patrullando || estado == EstadoPolicia.Sospechoso)
            CambiarEstado(EstadoPolicia.Persiguiendo);
    }

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

