// Assets/Scripts/NPCCivil.cs
// ═══════════════════════════════════════════════════════════════════════════
//  NPC civil — camina por la ciudad, huye cuando hay disparos.
//
//  No requiere prefab externo: crea su propio cuerpo procedimental si no hay
//  modelo asignado. Con modelo Meshy AI asignado usa ése.
//
//  Comportamientos:
//    · Idle: espera 2-5s en el sitio
//    · Caminar: se mueve a un punto aleatorio en radio 50m
//    · Huir: si escucha un disparo (<30m), corre en dirección contraria
//    · Parado: si el jugador está muy cerca (<2m), se aparta
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class NPCCivil : MonoBehaviour
{
    [Header("Modelo")]
    [Tooltip("Prefab visual del NPC. Si null se crea una cápsula de color.")]
    public GameObject prefabModelo;

    [Header("Movimiento")]
    public float radioDeambulacion = 50f;
    public float velocidadAndar    = 1.4f;
    public float velocidadHuida   = 4.2f;

    [Header("Reacción")]
    public float radioEscucha = 30f;   // distancia a la que oye disparos

    // ── Estado interno ──────────────────────────────────────────────────
    private enum Estado { Idle, Caminando, Huyendo }
    private Estado _estado = Estado.Idle;
    private NavMeshAgent _agente;
    private Animator     _animator;
    private float        _timerEstado;
    private Transform    _jugador;

    private static readonly int AnimVel = Animator.StringToHash("VelocidadMovimiento");

    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _agente = GetComponent<NavMeshAgent>();
        _agente.speed           = velocidadAndar;
        _agente.angularSpeed    = 200f;
        _agente.stoppingDistance = 0.5f;
        _agente.radius          = 0.3f;
        _agente.height          = 1.8f;
        _agente.enabled         = false; // espera NavMesh

        // Crear cuerpo visual si no hay modelo asignado
        if (prefabModelo != null)
        {
            var go = Instantiate(prefabModelo, transform);
            go.transform.localPosition = Vector3.zero;
            _animator = go.GetComponentInChildren<Animator>();
        }
        else
        {
            CrearCuerpoSimple();
        }
    }

    private void Start()
    {
        var jGO = GameObject.FindGameObjectWithTag("Player");
        if (jGO != null) _jugador = jGO.transform;

        if (SistemaNavMesh.EstaListo) ActivarAgente();
        else SistemaNavMesh.OnNavMeshListo += ActivarAgente;

        _timerEstado = Random.Range(0f, 3f); // offset para no sincronizarse todos
    }

    private void OnDestroy() => SistemaNavMesh.OnNavMeshListo -= ActivarAgente;

    private void ActivarAgente()
    {
        if (!SistemaNavMesh.PuntoEnNavMesh(transform.position, 3f)) return;
        _agente.enabled = true;
        CambiarEstado(Estado.Idle);
    }

    // ─────────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!_agente.enabled) return;

        _timerEstado -= Time.deltaTime;

        switch (_estado)
        {
            case Estado.Idle:
                if (_timerEstado <= 0f) CambiarEstado(Estado.Caminando);
                break;

            case Estado.Caminando:
                // Llegó al destino
                if (!_agente.pathPending && _agente.remainingDistance < 0.8f)
                    CambiarEstado(Estado.Idle);
                // Destino inválido → nuevo destino
                if (_timerEstado <= 0f)
                    CambiarEstado(Estado.Caminando);
                break;

            case Estado.Huyendo:
                if (_timerEstado <= 0f) CambiarEstado(Estado.Caminando);
                if (!_agente.pathPending && _agente.remainingDistance < 0.5f)
                    CambiarEstado(Estado.Idle);
                break;
        }

        // Separación del jugador (evita que el NPC bloquee la cámara)
        if (_jugador != null && Vector3.Distance(transform.position, _jugador.position) < 2f)
            HuirDe(_jugador.position);

        // Actualizar animator
        if (_animator != null)
        {
            float vel = _agente.velocity.magnitude / velocidadHuida;
            _animator.SetFloat(AnimVel, vel, 0.1f, Time.deltaTime);
        }

        // Rotar hacia la dirección de movimiento
        if (_agente.velocity.sqrMagnitude > 0.1f)
        {
            Quaternion rot = Quaternion.LookRotation(_agente.velocity.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, 10f * Time.deltaTime);
        }
    }

    // ─────────────────────────────────────────────────────────────────────

    private void CambiarEstado(Estado nuevo)
    {
        _estado = nuevo;
        switch (nuevo)
        {
            case Estado.Idle:
                _agente.isStopped = true;
                _timerEstado = Random.Range(2f, 5f);
                break;

            case Estado.Caminando:
                _agente.isStopped = false;
                _agente.speed = velocidadAndar;
                _timerEstado  = 15f;
                Vector3 destino = PuntoAleatorioNavMesh(transform.position, radioDeambulacion);
                if (destino != Vector3.zero) _agente.SetDestination(destino);
                break;

            case Estado.Huyendo:
                _agente.isStopped = false;
                _agente.speed     = velocidadHuida;
                _timerEstado      = 8f;
                break;
        }
    }

    /// <summary>El GameManager llama esto cuando hay un disparo cerca.</summary>
    public void AlertarDisparo(Vector3 origenDisparo)
    {
        if (Vector3.Distance(transform.position, origenDisparo) > radioEscucha) return;
        HuirDe(origenDisparo);
    }

    private void HuirDe(Vector3 origen)
    {
        Vector3 huida = (transform.position - origen).normalized;
        Vector3 destino = PuntoAleatorioNavMesh(transform.position + huida * 20f, 10f);
        if (destino == Vector3.zero) return;
        _agente.SetDestination(destino);
        CambiarEstado(Estado.Huyendo);
    }

    private static Vector3 PuntoAleatorioNavMesh(Vector3 centro, float radio)
    {
        for (int i = 0; i < 8; i++)
        {
            Vector3 candidato = centro + Random.insideUnitSphere * radio;
            candidato.y = centro.y;
            if (NavMesh.SamplePosition(candidato, out NavMeshHit hit, radio * 0.5f, NavMesh.AllAreas))
                return hit.position;
        }
        return Vector3.zero;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  CUERPO PROCEDURAL
    // ─────────────────────────────────────────────────────────────────────

    private void CrearCuerpoSimple()
    {
        // Colores de ropa variados para que no parezcan clones
        Color[] colores = {
            new Color(0.2f,0.3f,0.6f), new Color(0.6f,0.2f,0.2f),
            new Color(0.2f,0.6f,0.3f), new Color(0.5f,0.5f,0.15f),
            new Color(0.15f,0.15f,0.15f), new Color(0.8f,0.7f,0.6f)
        };
        Color ropa = colores[Random.Range(0, colores.Length)];
        Color piel = new Color(0.85f, 0.72f, 0.58f);

        var raiz = new GameObject("_Cuerpo"); raiz.transform.SetParent(transform, false);

        Parte(raiz, "Tronco",  new Vector3(0f,   1.0f, 0f), new Vector3(0.35f, 0.55f, 0.2f), ropa);
        Parte(raiz, "Cabeza",  new Vector3(0f,   1.7f, 0f), new Vector3(0.22f, 0.22f, 0.22f), piel);
        Parte(raiz, "PiernaI", new Vector3(-0.1f,0.38f,0f), new Vector3(0.14f, 0.75f, 0.14f), ropa);
        Parte(raiz, "PiernaD", new Vector3( 0.1f,0.38f,0f), new Vector3(0.14f, 0.75f, 0.14f), ropa);
        Parte(raiz, "BrazoI",  new Vector3(-0.25f,1.05f,0f),new Vector3(0.12f, 0.5f, 0.12f), ropa);
        Parte(raiz, "BrazoD",  new Vector3( 0.25f,1.05f,0f),new Vector3(0.12f, 0.5f, 0.12f), ropa);
    }

    private void Parte(GameObject raiz, string nombre, Vector3 localPos, Vector3 escala, Color color)
    {
        var go   = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name  = nombre;
        go.transform.SetParent(raiz.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale    = escala;
        var r = go.GetComponent<Renderer>();
        var mat = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        else mat.color = color;
        r.sharedMaterial = mat;
        Object.Destroy(go.GetComponent<Collider>());
    }
}
