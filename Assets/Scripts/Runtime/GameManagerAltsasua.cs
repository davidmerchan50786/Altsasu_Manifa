using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// GameManagerAltsasua — Núcleo del juego estilo GTA ambientado en Alsasua.
/// Gestiona: nivel de búsqueda, spawn de policía/enemigos, dinero, HUD y respawn.
/// Coloca este componente en un GameObject vacío llamado "GameManager" en la escena.
///
/// Implementa IWantedSystem e IEconomyService para que los sistemas de gameplay
/// no dependan de esta clase concreta — usan ServiceLocator.Get&lt;IWantedSystem&gt;()
/// y ServiceLocator.Get&lt;IEconomyService&gt;() en su lugar.
/// </summary>
public class GameManagerAltsasua : MonoBehaviour, IWantedSystem, IEconomyService, ISpawnService
{
    // ─── Singleton ───────────────────────────────────────────────────────────
    public static GameManagerAltsasua Instance { get; private set; }

    // ─── Jugador ─────────────────────────────────────────────────────────────
    [Header("Jugador")]
    [Tooltip("Arrastra aquí el prefab del jugador (PlayerMotor + Weapons)")]
    public GameObject prefabJugador;
    [Tooltip("Punto de spawn inicial del jugador en el escenario de Alsasua")]
    public Transform puntoSpawnJugador;
    [HideInInspector] public GameObject jugadorActivo;

    // ─── Nivel de Búsqueda (Wanted Level) ────────────────────────────────────
    [Header("Nivel de Búsqueda")]
    [Range(0, 5)]
    public int nivelBusqueda = 0;
    [Tooltip("Segundos sin crimen para que baje un nivel de búsqueda")]
    public float tiempoBajarNivel = 8f;
    [Tooltip("Cada cuántos segundos spawn un coche de policía cuando hay búsqueda")]
    public float intervalSpawnPoliciaCohe = 15f;
    [Tooltip("Nivel de búsqueda mínimo para que aparezca helicóptero")]
    public int nivelHelicoptero = 3;

    private float _timerBajarNivel = 0f;
    private float _timerSpawnPolicia = 0f;

    // ─── Prefabs Policía ─────────────────────────────────────────────────────
    [Header("Policía")]
    [Tooltip("Prefab del coche de policía (Interceptor)")]
    public GameObject prefabCochePolicia;
    [Tooltip("Prefab del helicóptero")]
    public GameObject prefabHelicoptero;
    [Tooltip("Puntos donde aparece la policía (detrás del jugador)")]
    public Transform[] puntosSpawnPolicia;

    private List<GameObject> _policiasActivos = new List<GameObject>();
    [Tooltip("Máximo de coches policía activos simultáneamente")]
    public int maxCochesPolicia = 3;
    [Tooltip("Máximo de helicópteros simultáneos (escala con el nivel de búsqueda).")]
    public int maxHelicopteros = 2;
    private readonly List<GameObject> _helicopterosActivos = new List<GameObject>();
    private float _timerHeli;

    // ─── Refuerzos por oleadas (escaladas por nivel de búsqueda) ──────────────
    [Header("Refuerzos (oleadas)")]
    [Tooltip("Tamaño de oleada = nivel de búsqueda, hasta este máximo de coches.")]
    public int   maxCochesPorOleada       = 5;
    [Tooltip("Segundos entre cada coche de una misma oleada (llegada escalonada, evita el pico de Instantiate).")]
    public float intervaloLlegadaOleada   = 1.5f;
    [Tooltip("Cooldown global entre oleadas (s). Anti-spam: aunque varios policías pidan a la vez, solo sale una.")]
    public float cooldownOleada           = 12f;
    [Tooltip("Coches simultáneos extra permitidos por nivel de búsqueda, sobre maxCochesPolicia.")]
    public int   cochesExtraPorNivel      = 1;
    [Tooltip("Radio (m) del anillo de aparición alrededor del jugador cuando NO hay puntos de spawn definidos.")]
    public float radioAnilloRefuerzo      = 35f;

    private float      _timerCooldownOleada;
    private Coroutine  _oleadaEnCurso;
    // De-dup de puntos de spawn dentro de una oleada (para rodear desde sitios distintos).
    private readonly HashSet<Transform> _puntosUsadosOleada = new HashSet<Transform>();

    // ─── Enemigos (Soldados/Manifestantes) ───────────────────────────────────
    [Header("Enemigos NPC")]
    [Tooltip("Prefab de enemigo (Z Walker / LowPolySoldier)")]
    public GameObject prefabEnemigo;
    [Tooltip("Puntos de spawn de enemigos por el escenario")]
    public Transform[] puntosSpawnEnemigos;
    [Tooltip("Máximo de enemigos NPC activos")]
    public int maxEnemigos = 8;

    private List<GameObject> _enemigosActivos = new List<GameObject>();
    private float _timerSpawnEnemigo = 0f;
    [Tooltip("Cada cuántos segundos intentar repoblar enemigos")]
    public float intervalSpawnEnemigos = 10f;

    // Vegetación legacy: campos migrados a SembradoVegetacionManual.
    // Se mantienen solo para serialización de prefabs existentes — no añadir nuevos aquí.
    [Header("Vegetación (legacy — usar SembradoVegetacionManual)")]
    public GameObject[] prefabsArboles;
    public Transform[]  puntosArboles;
    bool _arbolesSembrados;

    // ─── Terreno Cloud Compare ────────────────────────────────────────────────
    [Header("Terreno CloudCompare")]
    [Tooltip("Objeto del terreno importado desde Cloud Compare (OBJ)")]
    public GameObject terrenoCloudCompare;
    [Tooltip("LOD distance — por encima de este valor se desactiva el terreno detallado")]
    public float distanciaLOD = 300f;

    // ─── Economía / Puntuación ────────────────────────────────────────────────
    [Header("Economía")]
    public int dinero = 500;
    public int puntuacion = 0;
    [Tooltip("Recompensa en dinero por eliminar un enemigo")]
    public int recompensaEnemigo = 100;

    // ─── HUD ─────────────────────────────────────────────────────────────────
    [Header("HUD")]
    [Tooltip("Panel de pausa (opcional)")]
    public GameObject panelPausa;

    private bool _enPausa = false;

    // ─── Eventos estáticos ────────────────────────────────────────────────────
    /// <summary>Nivel de búsqueda cambió (0-5 estrellas). Suscriptor: HUDCanvas.</summary>
    public static event System.Action<int> OnEstrellasCambia;
    /// <summary>Dinero o puntuación cambiaron. Suscriptor: HUDCanvas.</summary>
    public static event System.Action<int, int> OnEconomiaCambia;
    /// <summary>El jugador hizo respawn.</summary>
    public static event System.Action OnRespawn;

    // ─── Estado ───────────────────────────────────────────────────────────────
    private bool  _jugadorVivo = false;
    // BUG FIX: guardar referencia al respawn para cancelarlo en OnDestroy y evitar
    // que acceda a jugadorActivo ya destruido tras un cambio de escena.
    private Coroutine _crRespawn;

    // Cache de valores HUD — OnEconomiaCambia y OnEstrellasCambia solo se disparan al cambiar
    private int _hudDineroCache      = int.MinValue;
    private int _hudBusquedaCache    = -1;
    private int _hudPuntuacionCache  = int.MinValue;

    // =========================================================================
    //  UNITY LIFECYCLE
    // =========================================================================

    // ── ISpawnService ─────────────────────────────────────────────────────────
    bool ISpawnService.JugadorEnVehiculo             => JugadorEnVehiculo;
    void ISpawnService.EnemigoEliminado(UnityEngine.GameObject e) => EnemigoEliminado(e);
    void ISpawnService.SetJugadorEnVehiculo(bool v)  => SetJugadorEnVehiculo(v);
    int  ISpawnService.SolicitarRefuerzosPolicia(Vector3 pos, int n) => SolicitarRefuerzosPolicia(pos, n);

    // ── IWantedSystem ─────────────────────────────────────────────────────────
    int IWantedSystem.NivelBusqueda => nivelBusqueda;
    void IWantedSystem.AumentarBusqueda(int cantidad) => AumentarBusqueda(cantidad);
    void IWantedSystem.FijarBusqueda(int nivel)
    {
        int prev = nivelBusqueda;
        nivelBusqueda = Mathf.Clamp(nivel, 0, 5);
        if (nivelBusqueda != prev) OnEstrellasCambia?.Invoke(nivelBusqueda);
    }

    bool _deescaladaBloqueada;
    void IWantedSystem.BloquearDeescalada(bool bloquear) => _deescaladaBloqueada = bloquear;

    // ── IEconomyService ───────────────────────────────────────────────────────
    int IEconomyService.Dinero     => dinero;
    int IEconomyService.Puntuacion => puntuacion;
    void IEconomyService.GanarDinero(int cantidad)  => GanarDinero(cantidad);
    bool IEconomyService.GastarDinero(int cantidad) => GastarDinero(cantidad);

    // =========================================================================
    //  UNITY LIFECYCLE
    // =========================================================================

    void Awake()
    {
        // Singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Registrar servicios para consumo desacoplado por el resto del juego
        ServiceLocator.Registrar<IWantedSystem>(this);
        ServiceLocator.Registrar<IEconomyService>(this);
        ServiceLocator.Registrar<ISpawnService>(this);
    }

    void OnDestroy()
    {
        // BUG FIX: desuscribir OnJugadorListoDesdeCore para evitar delegate huérfano
        // si GameManager se destruye antes de que AltsasuCore dispare OnJugadorSpawned.
        AltsasuCore.OnJugadorSpawned -= OnJugadorListoDesdeCore;
        if (_crRespawn != null) StopCoroutine(_crRespawn);
        ServiceLocator.Desregistrar<IWantedSystem>();
        ServiceLocator.Desregistrar<IEconomyService>();
        ServiceLocator.Desregistrar<ISpawnService>();
    }

    void Start()
    {
        SpawnJugador();
        // Delegar siembra a SembradoVegetacionManual si existe en la escena;
        // si no, ejecutar el método legacy por compatibilidad hacia atrás.
        if (GetComponent<SembradoVegetacionManual>() == null) SembrarArboles();
        InicializarEnemigos();
        ActualizarHUD();
    }

    void Update()
    {
        if (_enPausa) return;

        GestionarNivelBusqueda();
        GestionarSpawnPolicia();
        GestionarSpawnEnemigos();
        LimpiarListasMuertas();

        // Pausa con Escape — nuevo Input System
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            TogglePausa();

        ActualizarHUD();
    }

    // =========================================================================
    //  JUGADOR
    // =========================================================================

    void SpawnJugador()
    {
        if (prefabJugador == null)
        {
            jugadorActivo = GameObject.FindGameObjectWithTag("Player");
            if (jugadorActivo == null)
            {
                // SceneBootstrapper crea el jugador en corrutina — esperar señal de AltsasuCore
                AltsasuCore.OnJugadorSpawned += OnJugadorListoDesdeCore;
                return;
            }
        }
        else
        {
            // FIX (playtest): el fallback ponía al jugador en (0,2,0) → ¡240m BAJO el
            // terreno! (se veía el terreno desde abajo). Spawn en Herriko Plaza sobre el suelo.
            Vector3 pos;
            if (puntoSpawnJugador != null) pos = puntoSpawnJugador.position;
            else
            {
                Vector3 c = GeoDataAlsasua.HerrikoPlaza;
                pos = new Vector3(c.x, GeoDataAlsasua.AlturaTerreno(c.x, c.z) + 1.5f, c.z);
            }
            Quaternion rot = puntoSpawnJugador != null ? puntoSpawnJugador.rotation : Quaternion.identity;
            jugadorActivo = Instantiate(prefabJugador, pos, rot);
        }
        jugadorActivo.tag = "Player";
        _jugadorVivo = true;
        Debug.Log("[GameManager] ✓ Jugador listo en " + jugadorActivo.transform.position);
    }

    void OnJugadorListoDesdeCore(UnityEngine.Transform t)
    {
        AltsasuCore.OnJugadorSpawned -= OnJugadorListoDesdeCore;
        jugadorActivo = t.gameObject;
        _jugadorVivo  = true;
        Debug.Log("[GameManager] ✓ Jugador recibido desde AltsasuCore: " + t.position);
    }

    /// <summary>Llamar desde Health cuando el jugador muere.</summary>
    public void JugadorMuerto()
    {
        if (!_jugadorVivo) return;
        _jugadorVivo = false;
        nivelBusqueda = 0;
        Debug.Log("[GameManager] Jugador muerto. Respawneando en 3 segundos...");

        // Notificar a todos los sistemas vía EventBus (sin acoplamiento directo).
        // Receptores: HUDCanvas (fade), SistemaPolish (efecto muerte), SistemaLogros, AudioManager.
        EventBus.Publish(new PlayerDeathEvent
        {
            posicion = jugadorActivo != null ? jugadorActivo.transform.position : Vector3.zero,
            causa    = "muerte"
        });

        _crRespawn = StartCoroutine(RespawnJugador(3f));
    }

    IEnumerator RespawnJugador(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (jugadorActivo != null) Destroy(jugadorActivo);
        SpawnJugador();
        // Restaurar salud usando ControladorJugador (Health no existe — usa Curar)
        var ctrl = jugadorActivo?.GetComponent<ControladorJugador>();
        ctrl?.Curar(9999); // Curar(max) = vida llena
        OnRespawn?.Invoke();
    }

    // =========================================================================
    //  NIVEL DE BÚSQUEDA
    // =========================================================================

    // ── Estado de vehículo ────────────────────────────────────────────────────
    /// <summary>True mientras el jugador conduce un vehículo.</summary>
    public bool JugadorEnVehiculo { get; private set; }

    /// <summary>Llamar desde ControladorVehiculoJugador al entrar/salir.</summary>
    public void SetJugadorEnVehiculo(bool enVehiculo)
    {
        JugadorEnVehiculo = enVehiculo;
        // La IA de persecución puede comprobar esta bandera para elegir
        // si perseguir al GO del jugador (a pie) o al vehículo activo
    }

    // =========================================================================

    /// <summary>Aumentar el nivel de búsqueda (llamar al atacar civiles/policía).</summary>
    public void AumentarBusqueda(int cantidad = 1)
    {
        int anterior = nivelBusqueda;
        nivelBusqueda = Mathf.Clamp(nivelBusqueda + cantidad, 0, 5);
        if (cantidad > 0) _timerBajarNivel = tiempoBajarNivel;
        if (nivelBusqueda != anterior) OnEstrellasCambia?.Invoke(nivelBusqueda);
        AlsasuaLogger.Info("GameManager", $"Nivel búsqueda: {nivelBusqueda}★");
    }

    void GestionarNivelBusqueda()
    {
        if (nivelBusqueda <= 0) return;
        if (_deescaladaBloqueada) return; // SistemaEscapeWanted pausa esto mientras hay policía cerca

        _timerBajarNivel -= Time.deltaTime;
        if (_timerBajarNivel <= 0f)
        {
            int prev = nivelBusqueda;
            nivelBusqueda = Mathf.Max(0, nivelBusqueda - 1);
            if (nivelBusqueda != prev) OnEstrellasCambia?.Invoke(nivelBusqueda);
            _timerBajarNivel = tiempoBajarNivel;
            // La flota de helicópteros la gestiona GestionarHelicopteros() (escala y retira solo).
        }
    }

    // =========================================================================
    //  SPAWN DE POLICÍA
    // =========================================================================

    void GestionarSpawnPolicia()
    {
        if (_timerCooldownOleada > 0f) _timerCooldownOleada -= Time.deltaTime;   // cooldown de oleadas (siempre corre)

        // Flota de helicópteros ~1 Hz, independiente del nivel (así también se RETIRAN al bajar a 0).
        _timerHeli -= Time.deltaTime;
        if (_timerHeli <= 0f) { _timerHeli = 1f; GestionarHelicopteros(); }

        if (nivelBusqueda <= 0 || jugadorActivo == null) return;

        _timerSpawnPolicia -= Time.deltaTime;
        if (_timerSpawnPolicia > 0f) return;
        _timerSpawnPolicia = intervalSpawnCochePolicia();

        // Spawn coche policia
        LimpiarPoliciasDestruidosDeListaActivos();
        if (_policiasActivos.Count < maxCochesPolicia && prefabCochePolicia != null)
        {
            if (ElegirPoseSpawnPolicia(out Vector3 pos, out Quaternion rot))
            {
                var coche = Instantiate(prefabCochePolicia, pos, rot);
                _policiasActivos.Add(coche);
            }
        }

        // (los helicópteros los gestiona GestionarHelicopteros() arriba)
    }

    // Flota de helicópteros escalada por nivel de búsqueda: 1 al alcanzar
    // 'nivelHelicoptero', +1 cada 2 niveles, hasta 'maxHelicopteros'. Spawnea los
    // que falten repartidos en ángulo sobre el jugador y RETIRA los sobrantes
    // cuando el nivel baja (incluido a 0).
    void GestionarHelicopteros()
    {
        for (int i = _helicopterosActivos.Count - 1; i >= 0; i--)
            if (_helicopterosActivos[i] == null) _helicopterosActivos.RemoveAt(i);

        if (prefabHelicoptero == null || jugadorActivo == null) return;

        int objetivo = nivelBusqueda >= nivelHelicoptero
            ? Mathf.Clamp(1 + (nivelBusqueda - nivelHelicoptero) / 2, 0, maxHelicopteros)
            : 0;

        // Spawnear los que falten (cada uno en un sector angular distinto sobre el jugador).
        while (_helicopterosActivos.Count < objetivo)
        {
            int i = _helicopterosActivos.Count;
            float ang = Mathf.PI * 2f * i / Mathf.Max(1, objetivo);
            Vector3 off = new Vector3(Mathf.Cos(ang) * 22f, 50f, Mathf.Sin(ang) * 22f);
            var heli = Instantiate(prefabHelicoptero, jugadorActivo.transform.position + off, Quaternion.identity);
            _helicopterosActivos.Add(heli);
        }
        // Retirar excedentes si el nivel ha bajado.
        while (_helicopterosActivos.Count > objetivo)
        {
            int ult = _helicopterosActivos.Count - 1;
            if (_helicopterosActivos[ult] != null) Destroy(_helicopterosActivos[ult]);
            _helicopterosActivos.RemoveAt(ult);
        }
    }

    float intervalSpawnCochePolicia()
    {
        // Más nivel de búsqueda → spawn más frecuente
        return Mathf.Max(5f, intervalSpawnPoliciaCohe - nivelBusqueda * 2f);
    }

    // Pose de aparición de un coche de policía de ambiente. Si hay puntos autorizados,
    // usa uno al azar tal cual (ya están sobre carretera → sin snap). Si no, cae detrás
    // del jugador con snap a NavMesh (la pos cruda puede caer dentro de un edificio o
    // fuera de carretera → coche sin navegar). Devuelve false si no hay dónde spawnear.
    bool ElegirPoseSpawnPolicia(out Vector3 pos, out Quaternion rot)
    {
        if (puntosSpawnPolicia != null && puntosSpawnPolicia.Length > 0)
        {
            Transform punto = puntosSpawnPolicia[Random.Range(0, puntosSpawnPolicia.Length)];
            pos = punto.position;
            rot = punto.rotation;
            return true;
        }

        // Sin puntos definidos → detrás del jugador, sobre superficie navegable.
        if (jugadorActivo == null) { pos = default; rot = default; return false; }
        Vector3 cruda = jugadorActivo.transform.position - jugadorActivo.transform.forward * 40f;
        if (NavMesh.SamplePosition(cruda, out NavMeshHit hit, 40f, NavMesh.AllAreas))
            pos = hit.position;
        else
            pos = cruda;
        rot = jugadorActivo.transform.rotation;
        return true;
    }

    void LimpiarPoliciasDestruidosDeListaActivos()
    {
        _policiasActivos.RemoveAll(p => p == null);
    }

    // Refuerzos a demanda (GOAP de PoliciaForalIA vía ISpawnService). Lanza una
    // OLEADA escalada por nivel de búsqueda, escalonada en el tiempo y con cooldown
    // global anti-spam. Devuelve el tamaño de oleada planificado (0 si en cooldown,
    // ya hay una en curso, o no hay prefab). 'cantidadBase' = mínimo garantizado.
    public int SolicitarRefuerzosPolicia(Vector3 posicion, int cantidadBase)
    {
        if (prefabCochePolicia == null)   return 0;
        if (_timerCooldownOleada > 0f)    return 0;   // dispatch ocupado (otro policía ya pidió)
        if (_oleadaEnCurso != null)       return 0;   // oleada anterior aún llegando

        int nivel = Mathf.Max(nivelBusqueda, 1);
        int tam   = Mathf.Clamp(Mathf.Max(cantidadBase, nivel), 1, maxCochesPorOleada);

        _timerCooldownOleada = cooldownOleada;
        _oleadaEnCurso = StartCoroutine(DesplegarOleada(posicion, tam));
        // (los helicópteros de apoyo los gestiona GestionarHelicopteros(), escalados aparte)

        AlsasuaLogger.Info("GameManager", $"Oleada de refuerzos: {tam} coches (nivel {nivelBusqueda}).");
        return tam;
    }

    // Despliega la oleada UN coche cada 'intervaloLlegadaOleada' s: reparte el coste
    // de Instantiate y se siente como refuerzos que van llegando, no un pop masivo.
    // El tope simultáneo escala con el nivel de búsqueda (cochesExtraPorNivel) y cada
    // coche entra por un SECTOR distinto alrededor del jugador → te rodean.
    IEnumerator DesplegarOleada(Vector3 posicion, int cantidad)
    {
        var espera = new WaitForSeconds(intervaloLlegadaOleada);
        _puntosUsadosOleada.Clear();   // de-dup de puntos para repartir la oleada
        for (int i = 0; i < cantidad; i++)
        {
            LimpiarPoliciasDestruidosDeListaActivos();
            int cap = maxCochesPolicia + nivelBusqueda * cochesExtraPorNivel;
            if (_policiasActivos.Count < cap)
            {
                // Rodear AL JUGADOR (el objetivo), no al policía que pidió ayuda.
                Vector3 centro = jugadorActivo != null ? jugadorActivo.transform.position : posicion;
                PoseSpawnOleada(centro, i, cantidad, out Vector3 pos, out Quaternion rot);

                var coche = Instantiate(prefabCochePolicia, pos, rot);
                _policiasActivos.Add(coche);
            }
            yield return espera;
        }
        _oleadaEnCurso = null;
    }

    // Pose de aparición del coche 'i' de una oleada de 'total': reparte la oleada en
    // SECTORES angulares alrededor del jugador. Si hay puntos de spawn definidos, usa
    // el más alineado con el sector (sin repetir dentro de la oleada → cercan desde
    // sitios distintos). Si no hay, cae a un anillo alrededor del jugador.
    void PoseSpawnOleada(Vector3 centro, int i, int total, out Vector3 pos, out Quaternion rot)
    {
        float   ang = Mathf.PI * 2f * i / Mathf.Max(1, total);
        Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));

        Transform punto = PuntoMasAlineadoNoUsado(centro, dir);
        if (punto != null)
        {
            _puntosUsadosOleada.Add(punto);
            pos = punto.position;
            rot = punto.rotation;
            return;
        }
        // Sin puntos (o ya todos usados) → anillo alrededor del jugador, mirando hacia él.
        // Snap a NavMesh: una posición de anillo cruda puede caer dentro de un edificio
        // o en una ladera → el coche quedaría sin poder navegar. Buscamos la superficie
        // navegable más cercana; si no hay NavMesh a tiro, se usa la cruda como último recurso.
        Vector3 cruda = centro + dir * radioAnilloRefuerzo;
        if (NavMesh.SamplePosition(cruda, out NavMeshHit hit, radioAnilloRefuerzo, NavMesh.AllAreas))
            pos = hit.position;
        else
            pos = cruda;
        rot = Quaternion.LookRotation(-dir, Vector3.up);
    }

    // Punto de spawn cuyo rumbo desde 'centro' mejor casa con 'dir', excluyendo los ya
    // usados en esta oleada. Null si no hay puntos válidos disponibles.
    Transform PuntoMasAlineadoNoUsado(Vector3 centro, Vector3 dir)
    {
        if (puntosSpawnPolicia == null) return null;

        Transform mejor = null;
        float mejorDot = -2f;
        for (int i = 0; i < puntosSpawnPolicia.Length; i++)
        {
            var p = puntosSpawnPolicia[i];
            if (p == null || _puntosUsadosOleada.Contains(p)) continue;
            Vector3 d = p.position - centro; d.y = 0f;
            if (d.sqrMagnitude < 1e-3f) continue;
            float dot = Vector3.Dot(d.normalized, dir);
            if (dot > mejorDot) { mejorDot = dot; mejor = p; }
        }
        return mejor;
    }

    // =========================================================================
    //  SPAWN DE ENEMIGOS
    // =========================================================================

    void InicializarEnemigos()
    {
        if (prefabEnemigo == null || puntosSpawnEnemigos == null) return;
        for (int i = 0; i < Mathf.Min(maxEnemigos / 2, puntosSpawnEnemigos.Length); i++)
            SpawnEnemigo(puntosSpawnEnemigos[i]);
    }

    void GestionarSpawnEnemigos()
    {
        if (prefabEnemigo == null) return;
        _timerSpawnEnemigo -= Time.deltaTime;
        if (_timerSpawnEnemigo > 0f) return;
        _timerSpawnEnemigo = intervalSpawnEnemigos;

        _enemigosActivos.RemoveAll(e => e == null);
        if (_enemigosActivos.Count >= maxEnemigos || puntosSpawnEnemigos == null || puntosSpawnEnemigos.Length == 0) return;

        Transform punto = puntosSpawnEnemigos[Random.Range(0, puntosSpawnEnemigos.Length)];
        SpawnEnemigo(punto);
    }

    void SpawnEnemigo(Transform punto)
    {
        if (punto == null) return;
        var enemigo = Instantiate(prefabEnemigo, punto.position, punto.rotation);
        enemigo.tag = "Enemy";
        _enemigosActivos.Add(enemigo);
    }

    /// <summary>Llamar cuando el jugador elimina un enemigo.</summary>
    public void EnemigoEliminado(GameObject enemigo)
    {
        _enemigosActivos.Remove(enemigo);
        GanarDinero(recompensaEnemigo);
        AumentarBusqueda(1); // Matar enemigos también sube búsqueda
    }

    // =========================================================================
    //  ÁRBOLES / VEGETACIÓN (legacy — la lógica real está en SembradoVegetacionManual)
    // =========================================================================

    void SembrarArboles()
    {
        if (_arbolesSembrados || prefabsArboles == null || puntosArboles == null) return;
        foreach (var punto in puntosArboles)
        {
            if (punto == null) continue;
            var prefab = prefabsArboles[Random.Range(0, prefabsArboles.Length)];
            if (prefab != null)
                Instantiate(prefab, punto.position, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
        }
        _arbolesSembrados = true;
    }

    // =========================================================================
    //  ECONOMÍA
    // =========================================================================

    public void GanarDinero(int cantidad)
    {
        dinero += cantidad;
        puntuacion += cantidad;
        ActualizarHUD();
    }

    public bool GastarDinero(int cantidad)
    {
        if (dinero < cantidad) return false;
        dinero -= cantidad;
        return true;
    }

    // =========================================================================
    //  LIMPIEZA
    // =========================================================================

    void LimpiarListasMuertas()
    {
        _policiasActivos.RemoveAll(p => p == null);
        _enemigosActivos.RemoveAll(e => e == null);
        _helicopterosActivos.RemoveAll(h => h == null);
    }

    // =========================================================================
    //  HUD
    // =========================================================================

    void ActualizarHUD()
    {
        bool economiaChanged = dinero != _hudDineroCache || puntuacion != _hudPuntuacionCache;
        if (economiaChanged)
        {
            _hudDineroCache     = dinero;
            _hudPuntuacionCache = puntuacion;
            OnEconomiaCambia?.Invoke(dinero, puntuacion);
        }

        if (nivelBusqueda != _hudBusquedaCache)
        {
            _hudBusquedaCache = nivelBusqueda;
            OnEstrellasCambia?.Invoke(nivelBusqueda);
        }
    }

    // =========================================================================
    //  PAUSA
    // =========================================================================

    public void TogglePausa()
    {
        _enPausa = !_enPausa;
        Time.timeScale = _enPausa ? 0f : 1f;
        if (panelPausa != null) panelPausa.SetActive(_enPausa);
        Cursor.lockState = _enPausa ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = _enPausa;
        Debug.Log(_enPausa ? "[GameManager] PAUSA" : "[GameManager] JUEGO REANUDADO");
    }

    // =========================================================================
    //  GIZMOS (ayuda visual en el editor)
    // =========================================================================

    void OnDrawGizmos()
    {
        // Punto spawn jugador
        if (puntoSpawnJugador != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(puntoSpawnJugador.position, 1.5f);
            Gizmos.DrawIcon(puntoSpawnJugador.position + Vector3.up * 2f, "sv_icon_name0");
        }

        // Puntos spawn policía
        if (puntosSpawnPolicia != null)
        {
            Gizmos.color = Color.blue;
            foreach (var p in puntosSpawnPolicia)
                if (p != null) Gizmos.DrawWireCube(p.position, Vector3.one * 2f);
        }

        // Puntos spawn enemigos
        if (puntosSpawnEnemigos != null)
        {
            Gizmos.color = Color.red;
            foreach (var p in puntosSpawnEnemigos)
                if (p != null) Gizmos.DrawWireSphere(p.position, 1f);
        }

        // Puntos árboles
        if (puntosArboles != null)
        {
            Gizmos.color = Color.green * 0.7f;
            foreach (var p in puntosArboles)
                if (p != null) Gizmos.DrawWireSphere(p.position, 0.5f);
        }
    }
}
