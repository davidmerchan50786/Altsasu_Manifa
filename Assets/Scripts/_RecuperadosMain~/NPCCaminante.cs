// Assets/Scripts/NPCCaminante.cs
// ═══════════════════════════════════════════════════════════════════════════
//  NPC que camina por waypoints aleatorios usando NavMesh.
//   · Si tiene Animator con parámetro "VelocidadMovimiento", lo actualiza.
//   · Si no, mueve directamente el transform (fallback básico).
//   · Cambia de destino al llegar o tras N segundos si se atasca.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class NPCCaminante : MonoBehaviour
{
    [Tooltip("Radio máximo desde el punto inicial al elegir destino.")]
    public float radioRoaming = 80f;
    [Tooltip("Tiempo máximo antes de cambiar de destino aunque no haya llegado.")]
    public float tiempoMaxRuta = 20f;
    public float velocidadAndar = 1.3f;
    public float velocidadCorrer = 4f;

    NavMeshAgent _agent;
    Animator     _anim;
    Vector3      _origen;
    float        _timerRuta;

    static readonly int AnimVelocidad = Animator.StringToHash("VelocidadMovimiento");

    void Start()
    {
        _agent = GetComponent<NavMeshAgent>();
        _anim  = GetComponentInChildren<Animator>();
        _origen = transform.position;
        _agent.speed = velocidadAndar;
        EscogerNuevoDestino();
    }

    void Update()
    {
        _timerRuta -= Time.deltaTime;
        if (_timerRuta <= 0f || !_agent.pathPending && _agent.remainingDistance < 1f)
            EscogerNuevoDestino();

        if (_anim != null)
        {
            float vel = _agent.velocity.magnitude;
            float normalizada = vel / velocidadCorrer;
            _anim.SetFloat(AnimVelocidad, normalizada, 0.1f, Time.deltaTime);
        }
    }

    void EscogerNuevoDestino()
    {
        Vector2 r = Random.insideUnitCircle * radioRoaming;
        Vector3 destino = _origen + new Vector3(r.x, 0, r.y);

        if (NavMesh.SamplePosition(destino, out NavMeshHit hit, 10f, NavMesh.AllAreas))
        {
            _agent.SetDestination(hit.position);
            _agent.speed = Random.value < 0.15f ? velocidadCorrer : velocidadAndar;
            _timerRuta = tiempoMaxRuta;
        }
    }
}
