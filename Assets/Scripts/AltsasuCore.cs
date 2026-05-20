// Assets/Scripts/AltsasuCore.cs  v3
// ═══════════════════════════════════════════════════════════════════════════
//  COORDINADOR MONOLÍTICO — inicializa y conecta TODOS los subsistemas
//  en el orden correcto y sin conflictos.
//
//  Arquitectura: monolito (AltsasuCore) + microservicios (cada sistema)
//  Los sistemas se autocrean si no existen en escena.
//
//  Orden de boot:
//    -200  SceneBootstrapper  (terreno, jugador, cámara básicos)
//    -100  AltsasuCore        (este script — conecta todo lo demás)
//     -50  SistemaAtmosfera   (sol astronómico real)
//       0  Todos los demás    (ya con referencias válidas)
//
//  Sistemas activos (v3 — versiones data-oriented del simulator):
//    SistemaTrafico     → GPU DrawMeshInstanced, zero GC, pool preasignado
//    SistemaVegetacion  → GPU DrawMeshInstanced, Perlin noise, 4 zonas bosque
//    SistemaAtmosfera   → sol astronómico real (lat 42.9°N), HDRP
//    SistemaFauna       → pool-based, LOD culling, 6 especies
//    SistemaMultitud    → SpatialHashGrid, 500-1000 agentes, 2 draw calls
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(-100)]
public sealed class AltsasuCore : MonoBehaviour
{
    // ── Singleton ──────────────────────────────────────────────────────────
    public static AltsasuCore I { get; private set; }

    // ── Sistemas principales (simulator — data-oriented, GPU instancing) ──
    [Header("Sistemas principales (data-oriented)")]
    public SistemaTrafico      traficoSystem;
    public SistemaVegetacion   vegetacionSystem;
    public SistemaAtmosfera    atmosferaSystem;
    public SistemaFauna        faunaSystem;
    public SistemaMultitud     multitudSystem;

    // ── Misiones ───────────────────────────────────────────────────────────
    [Header("Misiones")]
    public SistemaMisiones      misionesSystem;

    // ── Sistemas de gameplay (mecánicas del juego) ─────────────────────────
    [Header("Sistemas de gameplay")]
    public GameManagerAltsasua  wantedSystem;
    public SistemaApoyoPopular  apoyoSystem;
    public SistemaDestruccion   destruccionSystem;
    public SistemaClima         climaSystem;
    public SistemaGrafitis      grafitisSystem;
    public SistemaParanoia      paranoiaSystem;
    public SistemaManifestacion manifestacionSystem;
    public SistemaArmasExtendido armasSystem;
    public SistemaBombas        bombasSystem;
    public SistemaDisparo       disparoSystem;
    public SistemaBarricadas    barricadasSystem;

    // ── Infraestructura ────────────────────────────────────────────────────
    [Header("Infraestructura")]
    public SistemaFarolas       farolasSystem;
    public SistemaFerroviario   ferroviarioSystem;
    public TrenEnMovimiento     trenSystem;
    public SistemaEdificios     edificiosSystem;
    public SonidosAmbienteAltsasu audioSystem;
    public HUDAAA               hudSystem;
    public HUDJugador           hudJugadorSystem;
    public HUDCanvas            hudCanvasSystem;
    public MenuPausa            menuPausaSystem;

    // ── Sistemas AAA+ ─────────────────────────────────────────────────────
    [Header("Sistemas AAA+")]
    public SistemaPostProcesoAAA postProcesoSystem;
    public SistemaTerreno        terrenoSystem;
    public SistemaMusica         musicaSystem;
    public SistemaLuzHDRP        luzSystem;
    public SistemaPolish         polishSystem;
    public SistemaImpactos       impactosSystem;
    public SistemaGuardado       guardadoSystem;
    public SistemaVidaNocturna   vidaNocturnaSystem;
    public TuningFisica          tuningFisicaSystem;
    public SistemaReverbZonas    reverbSystem;
    public AudioManager          audioManagerSystem;
    public SistemaLogros         logrosSystem;
    public SistemaAgendaNPC      agendaNPCSystem;
    public SistemaTutorial       tutorialSystem;
    public GeneradorMundoOSM     generadorMundoSystem;
    public IntegradorAssets      integradorAssetsSystem;
    public GeneradorFachadasAAA  fachadasSystem;
    public SistemaEdificiosAAA   edificiosAAASystem;
    public SistemaSueloAAA           sueloAAASystem;
    public SistemaOptimizacion       optimizacionSystem;
    public SistemaDiagnostico        diagnosticoSystem;
    public GestorMisionesSecundarias misionesSecSystem;
    public UIManagerAltsasua         uiManagerSystem;

    // ── Configuración ──────────────────────────────────────────────────────
    [Header("Mundo")]
    public float centroX = 1918f;
    public float centroZ = 8570f;
    [Range(30, 120)] public int fpsMeta = 60;
    public bool limitarNPCsPorFPS = true;

    // ── Estado ────────────────────────────────────────────────────────────
    Transform _jugador;
    float     _fps;
    bool      _listo;

    // ── Eventos ───────────────────────────────────────────────────────────
    public static event System.Action<Transform> OnJugadorSpawned;
    public static event System.Action            OnWorldReady;

    // ── Propiedades públicas ───────────────────────────────────────────────
    public static Transform Jugador => I?._jugador;
    public static float     FPS     => I?._fps ?? 60f;
    public static Vector3   Centro  => I != null
        ? new Vector3(I.centroX, AlturaEn(I.centroX, I.centroZ), I.centroZ)
        : new Vector3(1918, 240, 8570);

    public static float AlturaEn(float x, float z)
    {
        var t = Terrain.activeTerrain;
        if (t != null) return t.SampleHeight(new Vector3(x, 0, z));
        if (Physics.Raycast(new Vector3(x, 1000, z), Vector3.down, out var h, 2000)) return h.point.y;
        return 240f;
    }

    // =========================================================================
    //  BOOT
    // =========================================================================

    void Awake()
    {
        if (I != null && I != this) { Destroy(this); return; }
        I = this;
    }

    IEnumerator Start()
    {
        yield return null; // dejar que Awake de todos los scripts termine

        AlsasuaLogger.Info("AltsasuCore", "Iniciando boot de sistemas v3…");

        // ── PASO 1: Sistemas sin dependencias (gameplay base) ──────────────
        EnsureOn(ref wantedSystem,      "GameManager");
        EnsureOn(ref apoyoSystem,       "SistemaApoyoPopular");
        EnsureOn(ref destruccionSystem, "SistemaDestruccion");

        yield return null;

        // ── PASO 2: Clima + audio + juego social ──────────────────────────
        EnsureOn(ref climaSystem,        "SistemaClima");
        EnsureOn(ref grafitisSystem,     "SistemaGrafitis");
        EnsureOn(ref paranoiaSystem,     "SistemaParanoia");
        EnsureOn(ref audioSystem,        "SonidosAmbiente");

        yield return null;

        // ── PASO 3: Sistemas que requieren el terreno ─────────────────────
        EnsureOn(ref traficoSystem,      "SistemaTrafico");
        EnsureOn(ref vegetacionSystem,   "SistemaVegetacion");
        EnsureOn(ref faunaSystem,        "SistemaFauna");
        EnsureOn(ref atmosferaSystem,    "SistemaAtmosfera");
        EnsureOn(ref luzSystem,          "SistemaLuzHDRP");
        EnsureOn(ref terrenoSystem,      "SistemaTerreno");
        EnsureOn(ref postProcesoSystem,  "SistemaPostProcesoAAA");
        EnsureOn(ref musicaSystem,       "SistemaMusica");
        EnsureOn(ref polishSystem,        "SistemaPolish");
        EnsureOn(ref impactosSystem,     "SistemaImpactos");
        EnsureOn(ref guardadoSystem,     "SistemaGuardado");
        EnsureOn(ref vidaNocturnaSystem, "SistemaVidaNocturna");
        EnsureOn(ref tuningFisicaSystem, "TuningFisica");
        EnsureOn(ref reverbSystem,       "SistemaReverbZonas");
        EnsureOn(ref audioManagerSystem, "AudioManager");
        EnsureOn(ref logrosSystem,       "SistemaLogros");
        EnsureOn(ref agendaNPCSystem,    "SistemaAgendaNPC");
        EnsureOn(ref tutorialSystem,          "SistemaTutorial");
        EnsureOn(ref generadorMundoSystem,   "GeneradorMundoOSM");
        EnsureOn(ref integradorAssetsSystem, "IntegradorAssets");
        EnsureOn(ref fachadasSystem,         "GeneradorFachadasAAA");
        EnsureOn(ref edificiosAAASystem,     "SistemaEdificiosAAA");
        EnsureOn(ref sueloAAASystem,         "SistemaSueloAAA");
        EnsureOn(ref optimizacionSystem,     "SistemaOptimizacion");
        EnsureOn(ref diagnosticoSystem,      "SistemaDiagnostico");
        EnsureOn(ref misionesSecSystem,      "GestorMisionesSecundarias");
        EnsureOn(ref uiManagerSystem,        "UIManagerAltsasua");

        yield return null;

        // ── PASO 4: Sistemas de acción ────────────────────────────────────
        EnsureOn(ref armasSystem,        "SistemaArmasExtendido");
        EnsureOn(ref bombasSystem,       "SistemaBombas");
        EnsureOn(ref disparoSystem,      "SistemaDisparo");
        EnsureOn(ref barricadasSystem,   "SistemaBarricadas");

        yield return null;

        // ── PASO 5: Mundo — infraestructura ──────────────────────────────
        EnsureOn(ref farolasSystem,     "SistemaFarolas");
        EnsureOn(ref edificiosSystem,   "SistemaEdificios");
        EnsureTren();

        yield return null;

        // ── PASO 6: Manifestación y multitud ─────────────────────────────
        // SistemaManifestacion = mecánica de evento; SistemaMultitud = motor crowd
        EnsureOn(ref multitudSystem,     "SistemaMultitud");
        EnsureOn(ref manifestacionSystem,"SistemaManifestacion");
        EnsureOn(ref ferroviarioSystem,  "SistemaFerroviario");
        EnsureOn(ref misionesSystem,     "SistemaMisiones");  // ← añadido

        yield return null;

        // ── PASO 7: Conectar referencias cruzadas ─────────────────────────
        ConectarSistemas();

        // ── PASO 8: HUD doble (HUDAAA + HUDJugador) + Canvas AAA ─────────
        EnsureHUDs();
        EnsureOn(ref hudCanvasSystem, "HUDCanvas");

        // ── PASO 8b: Menú de pausa + opciones ────────────────────────────
        EnsureOn(ref menuPausaSystem, "MenuPausa");
        SistemaOpciones.AplicarTodo();

        // ── PASO 9: Jugador ────────────────────────────────────────────────
        yield return StartCoroutine(EsperarYConectarJugador());

        // ── PASO 10: Gráficos ─────────────────────────────────────────────
        AplicarGraficos();

        Application.targetFrameRate = fpsMeta;
        _listo = true;
        OnWorldReady?.Invoke();
        AlsasuaLogger.Info("AltsasuCore", "✅ Todos los sistemas activos y conectados.");
    }

    // =========================================================================
    //  ENSURE HELPERS
    // =========================================================================

    /// <summary>
    /// Busca T en escena; si no existe lo crea en este mismo GameObject.
    /// </summary>
    void EnsureOn<T>(ref T campo, string nombreLog) where T : MonoBehaviour
    {
        if (campo != null) return;
        campo = FindFirstObjectByType<T>();
        if (campo == null)
        {
            campo = gameObject.AddComponent<T>();
            AlsasuaLogger.Info("AltsasuCore", $"Creado: {typeof(T).Name}");
        }
    }

    void EnsureTren()
    {
        if (trenSystem != null) return;
        trenSystem = FindFirstObjectByType<TrenEnMovimiento>();
        if (trenSystem == null)
        {
            var go = new GameObject("TrenEnMovimiento");
            trenSystem = go.AddComponent<TrenEnMovimiento>();
        }
    }

    void EnsureHUDs()
    {
        // HUDAAA — barras políticas + hora + clima + dinero + wanted
        if (hudSystem == null)
        {
            hudSystem = FindFirstObjectByType<HUDAAA>();
            if (hudSystem == null)
                hudSystem = gameObject.AddComponent<HUDAAA>();
        }

        // HUDJugador — munición + mira + flash daño (COMPLEMENTARIO a HUDAAA)
        if (hudJugadorSystem == null)
        {
            hudJugadorSystem = FindFirstObjectByType<HUDJugador>();
            if (hudJugadorSystem == null)
                hudJugadorSystem = gameObject.AddComponent<HUDJugador>();
        }
    }

    // =========================================================================
    //  CONEXIONES ENTRE SISTEMAS
    // =========================================================================

    void ConectarSistemas()
    {
        // SistemaClima ← luz solar (campo público declarado en SistemaClima)
        if (climaSystem != null && climaSystem.solDireccional == null)
            climaSystem.solDireccional = FindFirstObjectByType<Light>();

        // SistemaAtmosfera auto-encuentra la luz en su propio Start()
        // SistemaTrafico auto-configura carriles desde roads_unity.json
        // SistemaVegetacion auto-detecta el Terrain activo
        // SistemaFauna auto-detecta el Terrain activo
        // SistemaMultitud auto-detecta jugador y terreno

        // GameManager ← audio
        var gmGO = wantedSystem?.gameObject;
        if (gmGO != null && gmGO.GetComponent<SonidosAmbienteAltsasu>() == null)
            gmGO.AddComponent<SonidosAmbienteAltsasu>();

        // Wanted → audio de sirenas
        GameManagerAltsasua.OnEstrellasCambia += nivel => audioSystem?.ActualizarSegunNivel(nivel);
        GameManagerAltsasua.OnRespawn         += () => StartCoroutine(EsperarYConectarJugador());

        AlsasuaLogger.Info("AltsasuCore", "✓ Sistemas conectados entre sí.");
    }

    // =========================================================================
    //  JUGADOR
    // =========================================================================

    IEnumerator EsperarYConectarJugador()
    {
        float t = 0f;
        while (_jugador == null && t < 15f)
        {
            t += 0.5f;
            yield return new WaitForSeconds(0.5f);
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) _jugador = go.transform;
        }
        if (_jugador != null) ConectarJugador();
        else AlsasuaLogger.Warn("AltsasuCore", "Jugador no encontrado en 15s.");
    }

    void ConectarJugador()
    {
        // Armas extendidas al jugador
        if (armasSystem != null && _jugador.GetComponent<SistemaArmasExtendido>() == null)
        {
            var armas = _jugador.gameObject.AddComponent<SistemaArmasExtendido>();
            armasSystem = armas;
        }

        // Bombas al jugador
        if (bombasSystem != null && _jugador.GetComponent<SistemaBombas>() == null)
            _jugador.gameObject.AddComponent<SistemaBombas>();

        // Disparo al jugador
        if (disparoSystem != null && _jugador.GetComponent<SistemaDisparo>() == null)
            _jugador.gameObject.AddComponent<SistemaDisparo>();

        // HUD al jugador (si no está ya en el GameManager)
        if (hudJugadorSystem != null && _jugador.GetComponent<HUDJugador>() == null)
        {
            // HUDJugador puede ir en el player o en el GameManager; dejarlo donde está
        }

        // SistemaTrafico y SistemaMultitud se auto-conectan al jugador via tag "Player"

        // Fijar jugador sobre terreno
        FixJugadorAltitud();

        OnJugadorSpawned?.Invoke(_jugador);
        AlsasuaLogger.Info("AltsasuCore", $"✓ Jugador conectado: {_jugador.name} en {_jugador.position}");
    }

    void FixJugadorAltitud()
    {
        if (_jugador == null) return;
        var t = Terrain.activeTerrain;
        if (t == null) return;
        float y = t.SampleHeight(_jugador.position) + 1.2f;
        if (Mathf.Abs(_jugador.position.y - y) > 3f)
            _jugador.position = new Vector3(_jugador.position.x, y, _jugador.position.z);
    }

    // =========================================================================
    //  GRÁFICOS
    // =========================================================================

    void AplicarGraficos()
    {
        RenderSettings.fog         = true;
        RenderSettings.fogDensity  = 0.0012f;
        RenderSettings.fogColor    = new Color(0.72f, 0.75f, 0.80f);
        RenderSettings.fogMode     = FogMode.ExponentialSquared;
        QualitySettings.shadowDistance   = 300f;
        QualitySettings.shadowCascades   = 4;
        QualitySettings.lodBias          = 2.5f;
        QualitySettings.maximumLODLevel  = 0;
    }

    // =========================================================================
    //  UPDATE
    // =========================================================================

    void Update()
    {
        if (!_listo) return;

        _fps = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.001f);

        if (limitarNPCsPorFPS) AjustarNPCsPorFPS();

        if (_jugador == null)
            _jugador = GameObject.FindGameObjectWithTag("Player")?.transform;
    }

    void AjustarNPCsPorFPS()
    {
        float ratio = Mathf.Clamp01(_fps / fpsMeta);

        // Los sistemas data-oriented ajustan su propio LOD internamente;
        // aquí solo limitamos si el FPS cae por debajo del umbral.
        if (_fps < fpsMeta * 0.5f)
        {
            // Señal global de bajo rendimiento — los sistemas la consultan via AltsasuCore.FPS
            Time.timeScale = Mathf.Lerp(Time.timeScale, 0.95f, Time.unscaledDeltaTime * 2f);
        }
        else if (Time.timeScale < 1f)
        {
            Time.timeScale = Mathf.Lerp(Time.timeScale, 1f, Time.unscaledDeltaTime * 4f);
        }
    }

    // =========================================================================
    //  FIND HELPER
    // =========================================================================

    static new T FindFirstObjectByType<T>() where T : UnityEngine.Object
        => UnityEngine.Object.FindFirstObjectByType<T>();
}
