// Assets/Scripts/AltsasuCore.cs  v4  — Senior refactor
// ═══════════════════════════════════════════════════════════════════════════
//  COORDINADOR CENTRAL — boot en 4 fases ordenadas por dependencia.
//
//  Fase 1 (f0): Core de audio, estado de juego y config del jugador.
//  Fase 2 (f1): Mundo — terreno, NavMesh, indexado OSM, zonas de streaming.
//  Fase 3 (f2): Gameplay — misiones, grafitis, manifestación, HUD, polish.
//  Fase 4:      Señal OnWorldReady → todos los sistemas inician sus lógicas.
//
//  Sistemas activos (v4):
//    AudioManager · GameManagerAltsasua · SistemaOpciones
//    SistemaAtmosfera · SistemaNavMesh · GeneradorMundoOSM · SistemaZonas
//    SistemaTrafico · SistemaVegetacion · SistemaFauna · SistemaMultitud
//    SistemaDisparo · SistemaGrafitis · SistemaManifestacion · SistemaArmasExtendido
//    SistemaBombas · SistemaBarricadas · SistemaDestruccion
//    SistemaPolish · SistemaImpactos · SistemaGuardado · SistemaLogros
//    SistemaMisiones · GestorMisionesSecundarias
//    HUDCanvas · MenuPausa · SistemaPolish
//    SistemaOptimizacion · SistemaDiagnostico
//    AlsasuaTreeStreamer · SistemaClima · SistemaParanoia
//    SistemaApoyoPopular · SistemaReverbZonas · SistemaTutorial
//
//  ELIMINADOS (eran stubs vacíos o duplicados):
//    SistemaFarolas, SistemaFerroviario, TrenEnMovimiento, SistemaEdificios,
//    SistemaTerreno, SistemaLuzHDRP, SistemaPostProcesoAAA (→ SistemaPolish),
//    HUDJugador (→ HUDCanvas), HUDAAA (→ HUDCanvas), UIManagerAltsasua (→ HUDCanvas),
//    GeneradorFachadasAAA (→ SistemaEdificiosAAA), IntegradorAssets (editor),
//    SistemaVidaNocturna (→ SistemaAtmosfera), SistemaAgendaNPC (vacío),
//    SistemaChunks (→ SistemaZonas automatizado)
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(-100)]
public sealed class AltsasuCore : MonoBehaviour
{
    // ── Singleton ──────────────────────────────────────────────────────────
    public static AltsasuCore I { get; private set; }

    // ── Referencias a sistemas (asignables desde Inspector) ───────────────
    [Header("── Fase 1: Core ──")]
    public AudioManager          audioManagerSystem;
    public GameManagerAltsasua   wantedSystem;
    public SistemaApoyoPopular   apoyoSystem;

    [Header("── Fase 2: Mundo ──")]
    public SistemaAtmosfera      atmosferaSystem;
    public SistemaNavMesh        navMeshSystem;
    public GeneradorMundoOSM     generadorMundoSystem;
    public SistemaZonas          zonasSystem;
    public SistemaTrafico        traficoSystem;
    public SistemaVegetacion     vegetacionSystem;
    public SistemaFauna          faunaSystem;
    public SistemaMultitud       multitudSystem;
    public SistemaClima          climaSystem;
    public SistemaParanoia       paranoiaSystem;

    [Header("── Fase 3: Gameplay ──")]
    public SistemaDisparo        disparoSystem;
    public SistemaArmasExtendido armasSystem;
    public SistemaBombas         bombasSystem;
    public SistemaBarricadas     barricadasSystem;
    public SistemaDestruccion    destruccionSystem;
    public SistemaGrafitis       grafitisSystem;
    public SistemaManifestacion  manifestacionSystem;
    public SistemaPolish         polishSystem;
    public SistemaImpactos       impactosSystem;
    public SistemaGuardado       guardadoSystem;
    public SistemaLogros         logrosSystem;
    public SistemaReverbZonas    reverbSystem;
    public SistemaTutorial       tutorialSystem;
    public SistemaMisiones           misionesSystem;
    public GestorMisionesSecundarias misionesSecSystem;

    [Header("── UI ──")]
    public HUDCanvas             hudCanvasSystem;
    public MenuPausa             menuPausaSystem;

    [Header("── Optimización ──")]
    public SistemaOptimizacion   optimizacionSystem;
    public SistemaDiagnostico    diagnosticoSystem;
    public SistemaEdificiosAAA   edificiosAAASystem;
    public SistemaTerreno        terrenoSystem;

    // ── Config ────────────────────────────────────────────────────────────
    [Header("Mundo")]
    public float centroX  = 1918f;
    public float centroZ  = 8570f;
    [Range(30, 120)] public int fpsMeta = 60;

    // ── Estado ────────────────────────────────────────────────────────────
    Transform _jugador;
    float     _fps;
    bool      _listo;

    public static event System.Action<Transform> OnJugadorSpawned;
    public static event System.Action            OnWorldReady;

    public static Transform Jugador  => I?._jugador;
    public static float     FPS      => I?._fps ?? 60f;
    public static bool      Listo    => I != null && I._listo;
    public static Vector3   Centro   => I != null
        ? new Vector3(I.centroX, AlturaEn(I.centroX, I.centroZ), I.centroZ)
        : new Vector3(1918f, 240f, 8570f);

    /// <summary>Delegado a GeoDataAlsasua.AlturaTerreno — punto único de verdad para altura de terreno.</summary>
    public static float AlturaEn(float x, float z) => GeoDataAlsasua.AlturaTerreno(x, z);

    // ══════════════════════════════════════════════════════════════════════
    //  BOOT
    // ══════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (I != null && I != this) { Destroy(this); return; }
        I = this;
    }

    IEnumerator Start()
    {
        yield return null; // Awake completado en todos los scripts

        AlsasuaLogger.Info("Core", "▶ Boot v4 iniciado");

        // ── FASE 1: Core (sin dependencias) ──────────────────────────────
        BootFase(1, () =>
        {
            EnsureOn(ref audioManagerSystem, "AudioManager");
            EnsureOn(ref wantedSystem,       "GameManager");
            EnsureOn(ref apoyoSystem,        "SistemaApoyoPopular");
            SistemaOpciones.AplicarTodo();
            Application.targetFrameRate = fpsMeta;
            QualitySettings.vSyncCount  = 0;
            AplicarGraficos();
        });

        yield return null;

        // ── FASE 2: Mundo (requiere terreno) ─────────────────────────────
        float tw = 0f;
        while (Terrain.activeTerrain == null && tw < 12f)
        {
            tw += 0.25f;
            yield return new WaitForSeconds(0.25f);
        }
        if (Terrain.activeTerrain == null)
            AlsasuaLogger.Warn("Core", "Terreno no disponible tras 12s — continuando sin él");

        BootFase(2, () =>
        {
            EnsureOn(ref atmosferaSystem,     "SistemaAtmosfera");
            EnsureOn(ref terrenoSystem,       "SistemaTerreno");
            EnsureOn(ref navMeshSystem,       "SistemaNavMesh");
            EnsureOn(ref edificiosAAASystem,  "SistemaEdificiosAAA");
            EnsureOn(ref zonasSystem,         "SistemaZonas");
            EnsureOn(ref generadorMundoSystem,"GeneradorMundoOSM");
            EnsureOn(ref traficoSystem,       "SistemaTrafico");
            EnsureOn(ref vegetacionSystem,    "SistemaVegetacion");
            EnsureOn(ref faunaSystem,         "SistemaFauna");
            EnsureOn(ref multitudSystem,      "SistemaMultitud");
            EnsureOn(ref climaSystem,         "SistemaClima");
            EnsureOn(ref paranoiaSystem,      "SistemaParanoia");

            if (climaSystem != null && climaSystem.solDireccional == null)
                climaSystem.solDireccional = FindFirstObjectByType<Light>();
        });

        yield return null;

        // ── FASE 3: Gameplay ─────────────────────────────────────────────
        BootFase(3, () =>
        {
            EnsureOn(ref destruccionSystem,   "SistemaDestruccion");
            EnsureOn(ref grafitisSystem,      "SistemaGrafitis");
            EnsureOn(ref manifestacionSystem, "SistemaManifestacion");
            EnsureOn(ref polishSystem,        "SistemaPolish");
            EnsureOn(ref impactosSystem,      "SistemaImpactos");
            EnsureOn(ref guardadoSystem,      "SistemaGuardado");
            EnsureOn(ref logrosSystem,        "SistemaLogros");
            EnsureOn(ref reverbSystem,        "SistemaReverbZonas");
            EnsureOn(ref tutorialSystem,      "SistemaTutorial");
            EnsureOn(ref misionesSystem,      "SistemaMisiones");
            EnsureOn(ref misionesSecSystem,   "GestorMisionesSecundarias");
            EnsureOn(ref hudCanvasSystem,     "HUDCanvas");
            EnsureOn(ref menuPausaSystem,     "MenuPausa");
            EnsureOn(ref optimizacionSystem,  "SistemaOptimizacion");
            EnsureOn(ref diagnosticoSystem,   "SistemaDiagnostico");
        });

        yield return null;

        // ── FASE 4: Jugador y señal final ─────────────────────────────────
        GameManagerAltsasua.OnRespawn += () => StartCoroutine(EsperarYConectarJugador());
        yield return StartCoroutine(EsperarYConectarJugador());

        _listo = true;
        OnWorldReady?.Invoke();
        AlsasuaLogger.Info("Core", "✅ Boot completo — mundo listo");
    }

    /// <summary>Ejecuta una fase de boot con try/catch — un fallo no detiene el boot.</summary>
    static void BootFase(int num, System.Action accion)
    {
        try
        {
            accion();
            AlsasuaLogger.Info("Core", $"  Fase {num} OK");
        }
        catch (System.Exception e)
        {
            AlsasuaLogger.Error("Core", $"  Fase {num} ERROR: {e.Message} — continuando");
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  HELPERS DE BOOT
    // ══════════════════════════════════════════════════════════════════════

    void EnsureOn<T>(ref T campo, string log) where T : MonoBehaviour
    {
        if (campo != null && campo.gameObject.activeInHierarchy) return;
        try
        {
            campo = FindFirstObjectByType<T>();
            if (campo == null)
            {
                campo = gameObject.AddComponent<T>();
                AlsasuaLogger.Info("Core", $"  [+] {typeof(T).Name} (creado en CoreGO)");
            }
        }
        catch (System.Exception e)
        {
            AlsasuaLogger.Error("Core", $"  [!] EnsureOn<{typeof(T).Name}> falló: {e.Message}");
            // No relanzar — un sistema fallido no debe detener el boot
        }
    }

    void AplicarGraficos()
    {
        RenderSettings.fog        = true;
        RenderSettings.fogDensity = 0.0012f;
        RenderSettings.fogColor   = new Color(0.72f, 0.75f, 0.80f);
        RenderSettings.fogMode    = FogMode.ExponentialSquared;
        QualitySettings.shadowDistance  = 300f;
        QualitySettings.shadowCascades  = 4;
        QualitySettings.lodBias         = 2.5f;
        QualitySettings.maximumLODLevel = 0;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  JUGADOR
    // ══════════════════════════════════════════════════════════════════════

    IEnumerator EsperarYConectarJugador()
    {
        float t = 0f;
        while (_jugador == null && t < 15f)
        {
            t += 0.25f;
            yield return new WaitForSeconds(0.25f);
            _jugador = GameObject.FindGameObjectWithTag("Player")?.transform;
        }

        if (_jugador == null) { AlsasuaLogger.Warn("Core", "Jugador no encontrado en 15s"); yield break; }

        // Añadir componentes de gameplay al jugador si no los tiene
        AddIfMissing<SistemaArmasExtendido>(_jugador.gameObject, ref armasSystem);
        AddIfMissing<SistemaBombas>        (_jugador.gameObject, ref bombasSystem);
        AddIfMissing<SistemaDisparo>       (_jugador.gameObject, ref disparoSystem);
        AddIfMissing<SistemaBarricadas>    (_jugador.gameObject, ref barricadasSystem);

        // Fijar altitud inicial
        var terrain = Terrain.activeTerrain;
        if (terrain != null)
        {
            float y = terrain.SampleHeight(_jugador.position) + 1.2f;
            if (Mathf.Abs(_jugador.position.y - y) > 3f)
                _jugador.position = new Vector3(_jugador.position.x, y, _jugador.position.z);
        }

        OnJugadorSpawned?.Invoke(_jugador);
        AlsasuaLogger.Info("Core", $"✓ Jugador: {_jugador.name} @ {_jugador.position:F0}");
    }

    void AddIfMissing<T>(GameObject go, ref T campo) where T : MonoBehaviour
    {
        if (campo != null) return;
        campo = go.GetComponent<T>();
        if (campo == null) campo = go.AddComponent<T>();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  UPDATE
    // ══════════════════════════════════════════════════════════════════════

    void Update()
    {
        _fps = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.001f);

        // Re-buscar jugador si murió y respawneó
        if (!_listo) return;
        if (_jugador == null)
            _jugador = GameObject.FindGameObjectWithTag("Player")?.transform;
    }
}
