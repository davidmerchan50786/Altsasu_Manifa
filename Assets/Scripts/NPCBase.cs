// Assets/Scripts/NPCBase.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CLASE BASE NPC — boilerplate compartido entre NPCCivil y PoliciaForalIA
//
//  Gestiona:
//    · NavMeshAgent: setup + activación diferida a NavMesh listo
//    · Jugador: búsqueda por tag + caché de ControladorJugador
//    · Animator: hash AnimVel + actualización automática
//    · Rotación suave hacia la dirección de movimiento
//    · HuirDe(Vector3) + PuntoAleatorioNavMesh()
//    · prefabModelo: instanciación o cuerpo procedural vía CrearCuerpoFallback()
//
//  Las subclases implementan:
//    · ActualizarComportamiento() — máquina de estados específica
//    · AlActivarAgente()          — qué hacer cuando el NavMesh está listo
//    · CrearCuerpoFallback()      — cuerpo procedural si prefabModelo es null
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public abstract class NPCBase : MonoBehaviour, IAgente
{
    // IAgente
    public Vector3 Posicion   => transform.position;
    public bool    EstaActivo => _agente != null && _agente.enabled && gameObject.activeInHierarchy;
    public virtual void Alertar(Vector3 origen) { }   // subclases sobreescriben si reaccionan

    [Header("Modelo")]
    [Tooltip("Prefab visual del NPC. Si null se llama a CrearCuerpoFallback().")]
    public GameObject prefabModelo;

    [Header("Movimiento base")]
    public float velocidadBase    = 1.4f;
    public float velocidadMaxima  = 5.5f;

    // ── Componentes cacheados ─────────────────────────────────────────────
    protected NavMeshAgent        _agente;
    protected Animator            _animator;
    protected Transform           _jugador;
    protected ControladorJugador  _controlJugador;

    protected static readonly int AnimVel = Animator.StringToHash("VelocidadMovimiento");

    // ════════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ════════════════════════════════════════════════════════════════════════

    protected virtual void Awake()
    {
        _agente = GetComponent<NavMeshAgent>();
        _agente.enabled         = false; // espera NavMesh
        _agente.angularSpeed    = 200f;
        _agente.stoppingDistance = 0.5f;
        _agente.radius          = 0.3f;
        _agente.height          = 1.8f;
        _agente.speed           = velocidadBase;
        InicializarModelo();
    }

    protected virtual void Start()
    {
        SistemaIA.Registrar(this);

        // Prioridad: AltsasuCore.Jugador (O(1)) antes que FindGameObjectWithTag
        var jugadorT = AltsasuCore.Jugador;
        if (jugadorT != null)
        {
            _jugador        = jugadorT;
            _controlJugador = jugadorT.GetComponent<ControladorJugador>();
        }
        else
        {
            // Fallback para el caso en que el NPC se spawne antes del boot completo
            AltsasuCore.OnJugadorSpawned += CacharJugadorDesdeEvento;
            var jGO = GameObject.FindGameObjectWithTag("Player");
            if (jGO != null)
            {
                _jugador        = jGO.transform;
                _controlJugador = jGO.GetComponent<ControladorJugador>();
                AltsasuCore.OnJugadorSpawned -= CacharJugadorDesdeEvento;
            }
        }

        if (SistemaNavMesh.EstaListo) ActivarAgente();
        else SistemaNavMesh.OnNavMeshListo += ActivarAgente;

        OnStart();
    }

    private void CacharJugadorDesdeEvento(Transform t)
    {
        AltsasuCore.OnJugadorSpawned -= CacharJugadorDesdeEvento;
        _jugador        = t;
        _controlJugador = t.GetComponent<ControladorJugador>();
    }

    protected virtual void OnDestroy()
    {
        SistemaNavMesh.OnNavMeshListo      -= ActivarAgente;
        AltsasuCore.OnJugadorSpawned       -= CacharJugadorDesdeEvento;
        SistemaIA.Desregistrar(this);
    }

    protected virtual void Update()
    {
        if (!_agente.enabled) return;
        ActualizarRotacion();
        ActualizarAnimador();
        ActualizarComportamiento();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MÉTODOS ABSTRACTOS
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Lógica de IA específica — llamada cada frame si el agente está activo.</summary>
    protected abstract void ActualizarComportamiento();

    /// <summary>Callback cuando el NavMesh está listo y el agente se ha habilitado.</summary>
    protected abstract void AlActivarAgente();

    // ════════════════════════════════════════════════════════════════════════
    //  HOOKS OPCIONALES
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Lógica adicional de Start() en subclases (sin reemplazar el boilerplate).</summary>
    protected virtual void OnStart() { }

    /// <summary>Crea un cuerpo visual procedural cuando prefabModelo es null.</summary>
    protected virtual void CrearCuerpoFallback() { }

    // ════════════════════════════════════════════════════════════════════════
    //  ACTIVACIÓN NAVMESH
    // ════════════════════════════════════════════════════════════════════════

    protected void ActivarAgente()
    {
        if (_agente == null || _agente.enabled) return;

        if (!SistemaNavMesh.PuntoEnNavMesh(transform.position, 3f))
        {
            // BUG FIX 6: desuscribir ANTES de volver a suscribir para evitar que
            // múltiples rehorneados acumulen suscripciones duplicadas y llamen
            // ActivarAgente() y AlActivarAgente() N veces.
            SistemaNavMesh.OnNavMeshListo -= ActivarAgente;
            SistemaNavMesh.OnNavMeshListo += ActivarAgente;
            return;
        }

        // Desuscribir definitivamente antes de activar — ya no necesitamos el evento
        SistemaNavMesh.OnNavMeshListo -= ActivarAgente;

        _agente.enabled = true;
        AlActivarAgente();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MOVIMIENTO COMPARTIDO
    // ════════════════════════════════════════════════════════════════════════

    protected void HuirDe(Vector3 origen)
    {
        Vector3 huida   = (transform.position - origen).normalized;
        Vector3 destino = PuntoAleatorioNavMesh(transform.position + huida * 20f, 10f);
        if (destino == Vector3.zero) return;
        _agente.SetDestination(destino);
    }

    protected static Vector3 PuntoAleatorioNavMesh(Vector3 centro, float radio)
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

    // ════════════════════════════════════════════════════════════════════════
    //  ACTUALIZACIÓN VISUAL
    // ════════════════════════════════════════════════════════════════════════

    private void ActualizarRotacion()
    {
        if (_agente.velocity.sqrMagnitude > 0.1f)
        {
            Quaternion rot = Quaternion.LookRotation(_agente.velocity.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, 10f * Time.deltaTime);
        }
    }

    private void ActualizarAnimador()
    {
        if (_animator == null) return;
        float vel = _agente.velocity.magnitude / Mathf.Max(0.01f, velocidadMaxima);
        _animator.SetFloat(AnimVel, vel, 0.1f, Time.deltaTime);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MODELO VISUAL
    // ════════════════════════════════════════════════════════════════════════

    private void InicializarModelo()
    {
        // Si no hay prefab asignado en el Inspector, tirar de SistemaAssets (pull).
        // Así CUALQUIER NPC instanciado en cualquier momento (incluida la policía
        // que spawnea minutos después del arranque) recibe su modelo real, en vez
        // de depender de que SistemaAssets lo empuje (push) en su Awake único.
        var prefab = prefabModelo != null ? prefabModelo : ObtenerModeloPorDefecto();

        if (prefab != null)
        {
            var go = Instantiate(prefab, transform);
            go.transform.localPosition = Vector3.zero;
            _animator = go.GetComponentInChildren<Animator>();
        }
        else
        {
            CrearCuerpoFallback();
        }

        GarantizarCollider();
    }

    /// <summary>
    /// Si tras crear el modelo no hay NINGÚN Collider en la jerarquía (caso de
    /// modelos importados que solo traen mallas, p.ej. civilian_girl), añade una
    /// CapsuleCollider humana al root para que el NPC sea sólido y DISPARABLE.
    /// Los modelos que ya traen su propio collider se respetan (no se duplica).
    /// El raycast de disparo usa capasImpacto = todo salvo IgnoreRaycast, así que
    /// con estar en una capa normal (Default) basta para recibir impactos.
    /// </summary>
    private void GarantizarCollider()
    {
        if (GetComponentInChildren<Collider>(true) != null) return;
        var cap = gameObject.AddComponent<CapsuleCollider>();
        cap.radius    = 0.3f;
        cap.height    = 1.8f;
        cap.center    = new Vector3(0f, 0.9f, 0f);
        cap.direction = 1; // eje Y (capsula vertical)
    }

    /// <summary>
    /// Modelo visual usado cuando prefabModelo no está asignado. Por defecto un
    /// civil aleatorio de SistemaAssets; las subclases lo sobreescriben (p.ej. la
    /// policía devuelve un modelo de Guardia Civil). Devuelve null si no hay
    /// catálogo cargado, en cuyo caso se usa el cuerpo procedural.
    /// </summary>
    protected virtual GameObject ObtenerModeloPorDefecto()
        => SistemaAssets.Instance != null ? SistemaAssets.Instance.CivilAleatorio() : null;
}
