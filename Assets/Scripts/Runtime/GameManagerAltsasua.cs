using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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
    private GameObject _helicopteroActivo;

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

    // ── IWantedSystem ─────────────────────────────────────────────────────────
    int IWantedSystem.NivelBusqueda => nivelBusqueda;
    void IWantedSystem.AumentarBusqueda(int cantidad) => AumentarBusqueda(cantidad);
    void IWantedSystem.FijarBusqueda(int nivel)
    {
        int prev = nivelBusqueda;
        nivelBusqueda = Mathf.Clamp(nivel, 0, 5);
        if (nivelBusqueda != prev) OnEstrellasCambia?.Invoke(nivelBusqueda);
    }

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

        _timerBajarNivel -= Time.deltaTime;
        if (_timerBajarNivel <= 0f)
        {
            int prev = nivelBusqueda;
            nivelBusqueda = Mathf.Max(0, nivelBusqueda - 1);
            if (nivelBusqueda != prev) OnEstrellasCambia?.Invoke(nivelBusqueda);
            _timerBajarNivel = tiempoBajarNivel;

            // Desactivar helicóptero si baja del umbral
            if (nivelBusqueda < nivelHelicoptero && _helicopteroActivo != null)
            {
                Destroy(_helicopteroActivo);
                _helicopteroActivo = null;
            }
        }
    }

    // =========================================================================
    //  SPAWN DE POLICÍA
    // =========================================================================

    void GestionarSpawnPolicia()
    {
        if (nivelBusqueda <= 0 || jugadorActivo == null) return;

        _timerSpawnPolicia -= Time.deltaTime;
        if (_timerSpawnPolicia > 0f) return;
        _timerSpawnPolicia = intervalSpawnCochePolicia();

        // Spawn coche policia
        LimpiarPoliciasDestruidosDeListaActivos();
        if (_policiasActivos.Count < maxCochesPolicia && prefabCochePolicia != null)
        {
            Transform puntoSpawn = ElegirPuntoSpawnPolicia();
            if (puntoSpawn != null)
            {
                var coche = Instantiate(prefabCochePolicia, puntoSpawn.position, puntoSpawn.rotation);
                _policiasActivos.Add(coche);
            }
        }

        // Helicóptero en nivel alto
        if (nivelBusqueda >= nivelHelicoptero && _helicopteroActivo == null && prefabHelicoptero != null)
        {
            Vector3 posHeli = jugadorActivo.transform.position + new Vector3(20f, 50f, 20f);
            _helicopteroActivo = Instantiate(prefabHelicoptero, posHeli, Quaternion.identity);
        }
    }

    float intervalSpawnCochePolicia()
    {
        // Más nivel de búsqueda → spawn más frecuente
        return Mathf.Max(5f, intervalSpawnPoliciaCohe - nivelBusqueda * 2f);
    }

    Transform ElegirPuntoSpawnPolicia()
    {
        if (puntosSpawnPolicia == null || puntosSpawnPolicia.Length == 0)
        {
            // Spawn detrás del jugador si no hay puntos definidos
            if (jugadorActivo == null) return null;
            var go = new GameObject("SpawnPoliciaTemporal");
            go.transform.position = jugadorActivo.transform.position - jugadorActivo.transform.forward * 40f;
            Destroy(go, 0.1f);
            return go.transform;
        }
        return puntosSpawnPolicia[Random.Range(0, puntosSpawnPolicia.Length)];
    }

    void LimpiarPoliciasDestruidosDeListaActivos()
    {
        _policiasActivos.RemoveAll(p => p == null);
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
        if (_helicopteroActivo == null) _helicopteroActivo = null; // referencia colgante
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
