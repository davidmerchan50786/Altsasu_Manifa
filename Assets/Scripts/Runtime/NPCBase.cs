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
public abstract class NPCBase : MonoBehaviour, IAgente, ISimulable, ITickable
{
    // IAgente (Posicion también satisface ISimulable)
    public Vector3 Posicion   => transform.position;
    public bool    EstaActivo => _agente != null && _agente.enabled && gameObject.activeInHierarchy;
    public virtual void Alertar(Vector3 origen) { }   // subclases sobreescriben si reaccionan

    // ── Sim-LOD / orquestador ────────────────────────────────────────────────
    protected NivelSim _nivelSim = NivelSim.Actor;
    bool _orquestado;

    public NivelSim Nivel => _nivelSim;

    /// <summary>Tipo de esta entidad para el Omni-Grid mientras es Ghost.
    /// La base es Manifestante; las subclases lo afinan (la policía → Policia).</summary>
    protected virtual TipoEspacial TipoEspacialSim => TipoEspacial.Manifestante;

    // El orquestador lee esto cada frame → la frecuencia baja sola con el nivel.
    public Frecuencia Frecuencia => _nivelSim switch
    {
        NivelSim.Actor => Frecuencia.Hz30,
        NivelSim.Proxy => Frecuencia.Hz5,
        _              => Frecuencia.Hz1
    };

    /// <summary>Lo llama el orquestador (NO un Update propio): solo la IA pesada.</summary>
    public void Tick(float dtAcumulado)
    {
        if (_agente == null || !_agente.enabled) return;
        ActualizarComportamiento();
    }

    // ── Pool por desactivación (Ghost) ──────────────────────────────────────
    // Renderers cacheados la primera vez que ghosteamos (no en Awake: muchos NPC
    // nunca llegan a Ghost y no queremos pagar el GetComponentsInChildren de balde).
    Renderer[] _renderers;
    bool       _renderersCacheados;
    bool       _aparcado;          // true mientras el GO está bajo el contenedor inactivo del pool
    int        _slotGhost = -1;    // slot en PoolGhosts (capa de datos) mientras es Ghost; -1 = sin slot

    // ── Omni-Grid ────────────────────────────────────────────────────────────
    IUnifiedSpatialGrid _grid;     // publicamos nuestra posición cada frame mientras Actor/Proxy
    int _idGrid;                   // id opaco (instanceID) para el grid

    public virtual void AplicarNivel(NivelSim n)
    {
        if (_nivelSim == n) return;

        bool entraGhost = n == NivelSim.Ghost && _nivelSim != NivelSim.Ghost;
        bool saleGhost  = n != NivelSim.Ghost && _nivelSim == NivelSim.Ghost;

        _nivelSim = n;

        // Ghost con GameObject = pool por DESACTIVACIÓN (no Destroy → sin GC/picos).
        // Apagamos los componentes pesados y aparcamos el GO bajo un contenedor
        // inactivo; al promocionar lo reactivamos y reenganchamos el NavMeshAgent.
        // (El Ghost SIN GameObject — fila en el NativeArray de la multitud BRG
        //  calculada por un Job — es trabajo futuro; ver PoolNPCSimulacion.)
        if (entraGhost)      EntrarGhost();
        else if (saleGhost)  SalirGhost();
        else
        {
            // Transición Actor↔Proxy: comportamiento visual de siempre.
            // (El animador a ½ rate en Proxy lo decide Update(); aquí solo
            //  garantizamos que esté encendido fuera de Ghost.)
            if (_animator != null) _animator.enabled = true;
        }

        OnNivelSimCambiado(n);
    }

    /// <summary>Pasa a Ghost: apaga componentes pesados y aparca el GO en el pool inactivo.</summary>
    void EntrarGhost()
    {
        // 0) Capturar el destino actual ANTES de apagar el agente → el ghost
        //    "seguirá caminando" hacia allí en el NativeArray mientras esté aparcado.
        Vector3 destinoGhost = transform.position;
        if (_agente != null && _agente.enabled && _agente.hasPath)
            destinoGhost = _agente.destination;

        // 1) Apagar IA/navegación ANTES de aparcar (Warp futuro necesita un agente
        //    que activamos al volver; aquí lo dejamos deshabilitado y limpio).
        if (_agente != null && _agente.enabled)
        {
            if (_agente.isOnNavMesh) _agente.ResetPath();
            _agente.enabled = false;
        }

        // 2) Apagar animador y renderers (invisible + sin coste de skinning).
        if (_animator != null) _animator.enabled = false;
        CachearRenderers();
        if (_renderers != null)
            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].enabled = false;

        // 3) Aparcar el GO bajo el contenedor inactivo del pool. Esto desactiva
        //    toda la jerarquía (sin tocar activeSelf) → cero Update/físicas.
        PoolNPCSimulacion.ObtenerOCrear().Aparcar(this);
        _aparcado = true;

        // 4) Registrar en la capa de datos: un Job Burst hará derivar su posición
        //    @1 Hz mientras el GO está congelado (Pilar 2 — Ghost como dato puro).
        //    Si el servicio no existe, _slotGhost queda -1 y el comportamiento es el de antes.
        var poolDatos = ServiceLocator.Get<IPoolGhosts>();
        if (poolDatos != null)
            _slotGhost = poolDatos.Registrar(transform.position, destinoGhost,
                                             Mathf.Max(0.3f, velocidadBase), TipoEspacialSim);
    }

    /// <summary>Promociona desde Ghost: reactiva el GO, re-enciende componentes y reengancha el agente.</summary>
    void SalirGhost()
    {
        // 0) Recuperar la posición que el ghost SIMULÓ mientras estuvo aparcado y
        //    teletransportar el GO ahí (la Y la re-fija el SamplePosition/Warp de
        //    abajo). Así reaparece "donde habría caminado", no donde se durmió.
        var poolDatos = ServiceLocator.Get<IPoolGhosts>();
        if (poolDatos != null && _slotGhost >= 0)
        {
            if (poolDatos.LeerPos(_slotGhost, out Vector3 posSim))
                transform.position = new Vector3(posSim.x, transform.position.y, posSim.z);
            poolDatos.Liberar(_slotGhost);
            _slotGhost = -1;
        }

        // 1) Sacar del pool → el GO vuelve a estar activo en jerarquía.
        if (_aparcado)
        {
            var pool = PoolNPCSimulacion.Instance;
            if (pool != null) pool.Reactivar(this);
            else              transform.SetParent(null, true); // pool desaparecido: degradación segura
            _aparcado = false;
        }

        // 2) Re-encender renderers y animador.
        if (_renderers != null)
            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].enabled = true;
        if (_animator != null) _animator.enabled = true;

        // 3) Reenganchar el NavMeshAgent. Warp recoloca el agente en el NavMesh sin
        //    arrastrar el path viejo (que apunta a donde estaba al ghostearse).
        //    RIESGO: si el punto actual NO está sobre NavMesh, Warp falla y el agente
        //    queda flotando; por eso muestreamos primero el NavMesh más cercano.
        if (_agente != null)
        {
            if (!SistemaNavMesh.EstaListo)
            {
                // NavMesh aún no horneado: que ActivarAgente lo retome cuando esté.
                SistemaNavMesh.OnNavMeshListo -= ActivarAgente;
                SistemaNavMesh.OnNavMeshListo += ActivarAgente;
                return;
            }

            Vector3 destino = transform.position;
            if (NavMesh.SamplePosition(destino, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                destino = hit.position;

            _agente.enabled = true;
            if (_agente.isOnNavMesh) _agente.Warp(destino);
            else                     _agente.Warp(transform.position);
        }
    }

    void CachearRenderers()
    {
        if (_renderersCacheados) return;
        _renderers = GetComponentsInChildren<Renderer>(true);
        _renderersCacheados = true;
    }

    /// <summary>Hook para subclases (p.ej. la policía apaga la linterna en Ghost).</summary>
    protected virtual void OnNivelSimCambiado(NivelSim n) { }

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

        // Omni-Grid: cacheamos el servicio y un id opaco para publicarnos cada frame.
        _grid   = ServiceLocator.Get<IUnifiedSpatialGrid>();
        _idGrid = gameObject.GetInstanceID();

        // Inversión de control: si el Director está activo, NO nos auto-actualizamos
        // en Update — nos suscribimos y él decide cuándo y a qué frecuencia (Sim-LOD).
        var orq = GlobalSimulationOrchestrator.Instancia;
        if (orq != null && orq.Config.orquestarNPCs)
        {
            orq.Registrar((ITickable)this);
            orq.Registrar((ISimulable)this);
            _orquestado = true;
        }

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

        if (_orquestado)
        {
            var orq = GlobalSimulationOrchestrator.Instancia;
            if (orq != null) { orq.Desregistrar((ITickable)this); orq.Desregistrar((ISimulable)this); }
        }

        // Liberar el slot de ghost si se destruye estando aparcado (evita fugas).
        if (_slotGhost >= 0)
        {
            ServiceLocator.Get<IPoolGhosts>()?.Liberar(_slotGhost);
            _slotGhost = -1;
        }
    }

    protected virtual void Update()
    {
        if (!_agente.enabled) return;
        if (_nivelSim == NivelSim.Ghost) return;                 // sin representación: nada visual

        // Publicar al Omni-Grid (immediate-mode): IA/jugador/otros nos "ven" en O(1).
        // Los Ghost NO pasan por aquí (van por la vía retenida de PoolGhosts).
        _grid?.Submit(new SpatialData { pos = transform.position, id = _idGrid, tipo = TipoEspacialSim, radio = 0.4f });

        ActualizarRotacion();
        // En Proxy, animador a mitad de framerate (lejos no se nota).
        if (_nivelSim != NivelSim.Proxy || (Time.frameCount & 1) == 0) ActualizarAnimador();

        // La IA pesada solo aquí si NO hay Director; con Director la conduce Tick().
        if (!_orquestado) ActualizarComportamiento();
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
