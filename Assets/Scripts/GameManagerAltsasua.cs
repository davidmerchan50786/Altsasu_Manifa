using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GameManagerAltsasua — Núcleo del juego estilo GTA ambientado en Alsasua.
/// Gestiona: nivel de búsqueda, spawn de policía/enemigos, dinero, HUD y respawn.
/// Coloca este componente en un GameObject vacío llamado "GameManager" en la escena.
/// </summary>
public class GameManagerAltsasua : MonoBehaviour
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

    // ─── Árboles / Decoración ────────────────────────────────────────────────
    [Header("Vegetación")]
    [Tooltip("Prefabs de árboles (Tree10_1 … Tree10_5)")]
    public GameObject[] prefabsArboles;
    [Tooltip("Puntos donde colocar árboles al iniciar")]
    public Transform[] puntosArboles;
    private bool _arbolesSembrados = false;

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
    [Tooltip("Texto UI para mostrar el dinero (opcional)")]
    public Text textoDinero;
    [Tooltip("Texto UI para mostrar el nivel de búsqueda")]
    public Text textoNivelBusqueda;
    [Tooltip("Texto UI para mostrar puntuación")]
    public Text textoPuntuacion;
    [Tooltip("Panel de pausa (opcional)")]
    public GameObject panelPausa;

    private bool _enPausa = false;

    // ─── Estado ───────────────────────────────────────────────────────────────
    private bool _jugadorVivo = false;

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
    }

    void Start()
    {
        SpawnJugador();
        SembrarArboles();
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

        // Pausa con Escape
        if (Input.GetKeyDown(KeyCode.Escape))
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
            Debug.LogWarning("[GameManager] prefabJugador no asignado. Buscando 'Player' en escena.");
            jugadorActivo = GameObject.FindGameObjectWithTag("Player");
            if (jugadorActivo == null) { Debug.LogError("[GameManager] No hay jugador en la escena."); return; }
        }
        else
        {
            Vector3 pos = puntoSpawnJugador != null ? puntoSpawnJugador.position : Vector3.zero + Vector3.up * 2f;
            Quaternion rot = puntoSpawnJugador != null ? puntoSpawnJugador.rotation : Quaternion.identity;
            jugadorActivo = Instantiate(prefabJugador, pos, rot);
        }
        jugadorActivo.tag = "Player";
        _jugadorVivo = true;
        Debug.Log("[GameManager] Jugador spawneado en " + jugadorActivo.transform.position);
    }

    /// <summary>Llamar desde Health cuando el jugador muere.</summary>
    public void JugadorMuerto()
    {
        if (!_jugadorVivo) return;
        _jugadorVivo = false;
        nivelBusqueda = 0;
        Debug.Log("[GameManager] Jugador muerto. Respawneando en 3 segundos...");
        StartCoroutine(RespawnJugador(3f));
    }

    IEnumerator RespawnJugador(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (jugadorActivo != null) Destroy(jugadorActivo);
        SpawnJugador();
        // Restaurar salud
        var health = jugadorActivo != null ? jugadorActivo.GetComponent<Health>() : null;
        if (health != null) health.CurrentHealth = 100f;
    }

    // =========================================================================
    //  NIVEL DE BÚSQUEDA
    // =========================================================================

    /// <summary>Aumentar el nivel de búsqueda (llamar al atacar civiles/policía).</summary>
    public void AumentarBusqueda(int cantidad = 1)
    {
        nivelBusqueda = Mathf.Clamp(nivelBusqueda + cantidad, 0, 5);
        _timerBajarNivel = tiempoBajarNivel; // reinicia el timer
        Debug.Log($"[GameManager] Nivel búsqueda: {nivelBusqueda}★");
    }

    void GestionarNivelBusqueda()
    {
        if (nivelBusqueda <= 0) return;

        _timerBajarNivel -= Time.deltaTime;
        if (_timerBajarNivel <= 0f)
        {
            nivelBusqueda = Mathf.Max(0, nivelBusqueda - 1);
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
        if (_enemigosActivos.Count >= maxEnemigos || puntosSpawnEnemigos == null) return;

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
    //  ÁRBOLES / VEGETACIÓN
    // =========================================================================

    void SembrarArboles()
    {
        if (_arbolesSembrados || prefabsArboles == null || prefabsArboles.Length == 0) return;
        if (puntosArboles == null) return;

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

        // Sincronizar con GUISystem si existe
        var gui = FindFirstObjectByType<GUISystem>();
        if (gui != null) gui.Money = dinero;
    }

    public bool GastarDinero(int cantidad)
    {
        if (dinero < cantidad) return false;
        dinero -= cantidad;
        var gui = FindFirstObjectByType<GUISystem>();
        if (gui != null) { gui.Cost = cantidad; gui.CostShow = true; }
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
        if (textoDinero != null)
            textoDinero.text = "$ " + dinero;

        if (textoNivelBusqueda != null)
        {
            string estrellas = "";
            for (int i = 0; i < 5; i++)
                estrellas += i < nivelBusqueda ? "★" : "☆";
            textoNivelBusqueda.text = estrellas;
        }

        if (textoPuntuacion != null)
            textoPuntuacion.text = "Score: " + puntuacion;
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
