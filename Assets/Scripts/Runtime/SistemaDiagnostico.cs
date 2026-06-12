// Assets/Scripts/SistemaDiagnostico.cs
// ═══════════════════════════════════════════════════════════════════════════
//  DIAGNÓSTICO DE ARRANQUE UNIFICADO (fusión 2026-06 con DiagnosticoArranque,
//  ahora deprecado).
//
//  Al entrar en Play Mode genera un informe en 7 secciones:
//    1) Render / HDRP        4) Entidades        7) Rendimiento
//    2) Datos (LIDAR/JSON)   5) Gameplay
//    3) Core / World         6) UI / Audio
//  Cada línea ✅/❌/⚠ y al final OPCIONES DE MEJORA según lo detectado.
//
//  F1 → panel en pantalla (se abre solo si hay errores).
//  repetirCada > 0 → re-ejecuta el informe periódicamente.
//  Lo instancian AltsasuCore (EnsureOn) y CreadorEscenaPrincipal.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

[DefaultExecutionOrder(200)] // después de que todo arranque
public class SistemaDiagnostico : SingletonMono<SistemaDiagnostico>
{
    [Tooltip("Repetir el informe cada X segundos (0 = solo una vez al arrancar)")]
    public float repetirCada = 0f;

    bool     _mostrandoPanel;
    string   _informe = "";
    float    _timer;
    GUIStyle _estiloPanel, _estiloTitulo;
    bool     _estilosInit;
    Vector2  _scroll;

    readonly List<string> _errores  = new();
    readonly List<string> _warnings = new();
    readonly List<string> _mejoras  = new();

    void Start()
    {
        StartCoroutine(DiagnosticarTrasArranque());
        // Re-diagnosticar cuando el mosaico V2 termine de cargar los 48 tiles
        EventBus.Subscribe<MosaicoCompletoEvent>(OnMosaicoCompleto);
    }

    protected override void OnDestroyed() => EventBus.Unsubscribe<MosaicoCompletoEvent>(OnMosaicoCompleto);

    void OnMosaicoCompleto(MosaicoCompletoEvent _) => Ejecutar();

    IEnumerator DiagnosticarTrasArranque()
    {
        yield return new WaitForSeconds(4f);   // esperar a que todos los sistemas arranquen
        Ejecutar();
    }

    void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.f1Key.wasPressedThisFrame)
        {
            _mostrandoPanel = !_mostrandoPanel;
            // FIX (jun 2026): re-ejecutar al abrir — antes mostraba el informe
            // de los 4s del arranque (NavMesh/streamers aún horneando = falsos ❌).
            if (_mostrandoPanel) Ejecutar();
        }

        if (repetirCada > 0f)
        {
            _timer += Time.deltaTime;
            if (_timer >= repetirCada) { _timer = 0f; Ejecutar(); }
        }
    }

    // ════════════════════════════════════════════════════════════════════════

    void Ejecutar()
    {
        _errores.Clear(); _warnings.Clear(); _mejoras.Clear();
        var sb = new StringBuilder();
        sb.AppendLine("═══ DIAGNÓSTICO COMPLETO — Altsasu Manifa ═══");

        SecRender(sb);
        SecDatos(sb);
        SecMosaico(sb);
        SecCoreWorld(sb);
        SecEntidades(sb);
        SecGameplay(sb);
        SecUIAudio(sb);
        SecRendimiento(sb);

        // ── Opciones de mejora ────────────────────────────────────────────
        sb.AppendLine("\n┌─ OPCIONES DE MEJORA ─────────────────────────────┐");
        if (_mejoras.Count == 0)
            sb.AppendLine("  🎉 Nada crítico detectado. El juego está listo.");
        else
            for (int i = 0; i < _mejoras.Count; i++)
                sb.AppendLine($"  {i + 1}. {_mejoras[i]}");
        sb.AppendLine("└──────────────────────────────────────────────────┘");

        // ── Resumen ───────────────────────────────────────────────────────
        sb.AppendLine($"\n═══ RESUMEN ═══  Errores: {_errores.Count} · Warnings: {_warnings.Count}");
        sb.AppendLine(_errores.Count == 0 ? "✅ Sin errores críticos" : "❌ Hay errores críticos");

        _informe = sb.ToString();
        _mostrandoPanel = _errores.Count > 0 || _warnings.Count > 3;

        Debug.Log($"[Diagnóstico]\n{_informe}");
        if (_errores.Count > 0)
            Debug.LogError($"[Diagnóstico] {_errores.Count} errores críticos:\n" + string.Join("\n", _errores));

        AlsasuaLogger.Info("Diagnostico",
            $"Completado — {_errores.Count} errores, {_warnings.Count} warnings, {_mejoras.Count} mejoras");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  1) RENDER / HDRP
    // ════════════════════════════════════════════════════════════════════════
    void SecRender(StringBuilder sb)
    {
        sb.AppendLine("\n── 1) RENDER / HDRP ──────────────────────────────");

        bool hdrp = GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset;
        Error(sb, "Pipeline HDRP activo", hdrp,
              "Sin HDRP todo se verá rosa. Project Settings→Graphics→asignar HDRP asset.");

        var cam = Camera.main;
        Error(sb, "Main Camera (tag MainCamera)", cam != null,
              "Asigna el tag 'MainCamera' a la cámara del jugador o no verás nada.");
        if (cam != null)
        {
            Warn(sb, "HDRP camera data", cam.GetComponent<HDAdditionalCameraData>() != null);
            Warn(sb, $"FOV configurado ({cam.fieldOfView:F0}°)",
                 cam.fieldOfView >= 60f && cam.fieldOfView <= 90f);
        }

        var vols = FindObjectsByType<Volume>(FindObjectsSortMode.None);
        Warn(sb, $"HDRP Volume ({vols.Length})", vols.Length > 0,
             "Añade un Volume global con el perfil 'HDRP Balanced' para SSAO/Bloom/Fog.");

        Light sol = null;
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) { sol = l; break; }
        Error(sb, "Sol direccional", sol != null,
              "Sin sol direccional la escena está negra. SceneBootstrapper debería crearlo.");
        if (sol != null)
        {
            var hd = sol.GetComponent<HDAdditionalLightData>();
            Warn(sb, "Sol con HDAdditionalLightData", hd != null,
                 "En HDRP la luz no se ve sin HDAdditionalLightData + intensidad en Lux.");
            if (hd != null) sb.AppendLine($"     Intensidad sol: {hd.intensity:F0} lux");
        }

        sb.AppendLine($"     Quality level: {QualitySettings.GetQualityLevel()} " +
                      $"({QualitySettings.names[QualitySettings.GetQualityLevel()]})");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  2) DATOS (solo Editor — en build se empaquetan distinto)
    // ════════════════════════════════════════════════════════════════════════
    void SecDatos(StringBuilder sb)
    {
        sb.AppendLine("\n── 2) DATOS (LIDAR / JSON) ───────────────────────");
#if UNITY_EDITOR
        DatoArchivo(sb, "lidar_dtm_05m.raw",    8_000_000);
        DatoArchivo(sb, "dem_unity_1025.raw",   2_000_000);
        DatoArchivo(sb, "lidar_buildings.json",   100_000);
        DatoArchivo(sb, "buildings_unity.json",   100_000);
        DatoArchivo(sb, "trees_unity.json",        50_000);
        DatoArchivo(sb, "roads_unity.json",        10_000);
        DatoArchivo(sb, "ortofoto_unity.png",     100_000);
#else
        sb.AppendLine("  (Verificación de archivos solo disponible en el Editor)");
#endif
    }

#if UNITY_EDITOR
    void DatoArchivo(StringBuilder sb, string nombre, long minBytes)
    {
        string ruta = Path.Combine(Application.dataPath, "AlsasuaData", nombre);
        if (!File.Exists(ruta))
        {
            Error(sb, nombre, false, $"Falta {nombre}. Revisa Assets/AlsasuaData/.");
            return;
        }
        long size = new FileInfo(ruta).Length;
        if (size < 200) // puntero Git LFS no descargado
        {
            Error(sb, $"{nombre} (LFS sin descargar)", false,
                  $"{nombre} es un puntero LFS de {size}B. Ejecuta: git lfs pull");
            return;
        }
        bool ok = size >= minBytes * 0.5f;
        Warn(sb, $"{nombre} ({size / 1024}KB)", ok,
             ok ? null : $"{nombre} parece truncado ({size}B, esperado >{minBytes / 1024}KB).");
    }
#endif

    // ════════════════════════════════════════════════════════════════════════
    //  2b) MOSAICO TERRENO V2 — checks del plan (se re-ejecuta al recibir
    //  MosaicoCompletoEvent; ver suscripción en Start)
    // ════════════════════════════════════════════════════════════════════════
    void SecMosaico(StringBuilder sb)
    {
        sb.AppendLine("\n── 2b) MOSAICO TERRENO V2 ────────────────────────");
        var svc = ServiceLocator.Get<ITerrainService>();
        if (svc == null)
        { sb.AppendLine("  (sin ITerrainService — escena legacy)"); return; }

        sb.AppendLine($"  Estado: {svc.Estado} · Fuente: {svc.Fuente}");
        if (!svc.EsMosaico)
        {
            if (svc.Fuente == FuenteTerreno.DEM)
                _mejoras.Add("Terreno legacy DEM 6 km activo. Hornea el mosaico V2: " +
                             "Tools/Alsasua/Mundo/🧩 Construir Mosaico V2 (bake).");
            return;
        }

        // nº de tiles
        Warn(sb, $"Tiles del mosaico: {svc.Tiles.Count}", svc.Tiles.Count >= 48,
             svc.Tiles.Count < 48 ? $"Mosaico incompleto ({svc.Tiles.Count}/48)." : null);

        // cota de la plaza (datum: 531.94 − 511.33 = 20.61)
        float esperada = GeoDataAlsasua.COTA_PLAZA - GeoDataAlsasua.Z_MIN;
        float yPlaza = svc.AlturaMundo(GeoDataAlsasua.HerrikoPlaza);
        Error(sb, $"Cota plaza: {yPlaza:F2} (esperada {esperada:F2}±0.5)",
              Mathf.Abs(yPlaza - esperada) <= 0.5f,
              "La plaza no está a su cota — datum/y64 de tiles mal aplicado.");

        // terrenos AJENOS activos (sin marcador) — deberían estar desactivados
        int ajenos = 0;
        foreach (var t in Terrain.activeTerrains)
            if (t.GetComponent<MarcadorTerrenoAltsasua>() == null) ajenos++;
        Warn(sb, $"Terrains ajenos activos: {ajenos}", ajenos == 0,
             "Hay Terrains de asset packs activos junto al mosaico (DesactivarTerrenosAjenos).");

        // colliders: anillo 0 siempre ON
        int sinCollider = 0;
        foreach (var t in svc.Tiles)
        {
            var m = t.GetComponent<MarcadorTerrenoAltsasua>();
            var c = t.GetComponent<TerrainCollider>();
            if (m != null && m.anillo == 0 && (c == null || !c.enabled)) sinCollider++;
        }
        Error(sb, "Colliders anillo 0", sinCollider == 0,
              $"{sinCollider} tiles del anillo 0 sin TerrainCollider activo — el jugador caería.");

        // solapes de bounds (mismo anillo)
        bool solape = false;
        var lista = svc.Tiles;
        for (int i = 0; i < lista.Count && !solape; i++)
        {
            var mi = lista[i].GetComponent<MarcadorTerrenoAltsasua>();
            if (mi == null) continue;
            var pi = lista[i].transform.position; var si = lista[i].terrainData.size;
            for (int j = i + 1; j < lista.Count; j++)
            {
                var mj = lista[j].GetComponent<MarcadorTerrenoAltsasua>();
                if (mj == null || mj.anillo != mi.anillo) continue;
                var pj = lista[j].transform.position; var sj = lista[j].terrainData.size;
                if (pi.x < pj.x + sj.x - 0.01f && pj.x < pi.x + si.x - 0.01f &&
                    pi.z < pj.z + sj.z - 0.01f && pj.z < pi.z + si.z - 0.01f)
                { solape = true; break; }
            }
        }
        Error(sb, "Bounds sin solape", !solape,
              "Dos tiles del mismo anillo se solapan — índices del manifest corruptos.");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  3) CORE / WORLD
    // ════════════════════════════════════════════════════════════════════════
    void SecCoreWorld(StringBuilder sb)
    {
        sb.AppendLine("\n── 3) CORE / WORLD ───────────────────────────────");

        Error(sb, "AltsasuCore", AltsasuCore.I != null,
              "Singleton central ausente. SceneBootstrapper debería crearlo.");
        Error(sb, "Jugador (tag=Player)", AltsasuCore.Jugador != null, null);

        var t = Terrain.activeTerrain;
        Error(sb, "Terreno activo", t != null,
              "No hay terreno. Menú Territorio Real → GENERAR TODO, o revisa GeneradorTerrenoUltraPreciso.");
        if (t != null)
        {
            var td = t.terrainData;
            sb.AppendLine($"     {td.heightmapResolution}x{td.heightmapResolution} | " +
                          $"{td.size.x:F0}x{td.size.z:F0}m | altura máx {td.size.y:F1}m");
            if (td.heightmapResolution < 1025)
                _mejoras.Add("Terreno a baja resolución. Regenera desde lidar_dtm_05m.raw (2049) para máximo detalle.");
            Warn(sb, "Terrain layers (texturas)",
                 td.terrainLayers != null && td.terrainLayers.Length > 0,
                 "Terreno sin texturas. Ejecuta SistemaTerreno (splatmap 8 biomas).");
            sb.AppendLine($"     Árboles en terrain: {td.treeInstanceCount}");
            // AlsasuaTreeStreamer instancia PREFABS (no terrain trees) — solo es
            // mejora real si tampoco existe el streamer.
            if (td.treeInstanceCount == 0 && FindAnyObjectByType<AlsasuaTreeStreamer>() == null)
                _mejoras.Add("No hay árboles. Activa AlsasuaTreeStreamer (2956 árboles LIDAR reales).");
        }

        // NavMesh
        Error(sb, "SistemaNavMesh", SistemaNavMesh.Instance != null, null);
        Warn(sb, "NavMesh horneado", SistemaNavMesh.EstaListo,
             SistemaNavMesh.EstaListo ? null : "Espera ~2s después de Play, o Window→AI→Navigation→Bake.");
        int triCount = UnityEngine.AI.NavMesh.CalculateTriangulation().vertices.Length;
        Warn(sb, $"NavMesh vértices ({triCount})", triCount > 100,
             triCount > 100 ? null : "NavMesh vacío — ¿terrain sin collider?");

        // Zone streaming
        Error(sb, "SistemaZonas activo", SistemaZonas.Instance != null, null);
        Warn(sb, "GeneradorMundoOSM indexado", GeneradorMundoOSM.MundoListo);
        Warn(sb, "SistemaEdificiosAAA listo",  SistemaEdificiosAAA.Listo);
        Warn(sb, "SistemaTerreno pintado",     SistemaTerreno.Listo);
        // SistemaChunks vive en Alsasua.Systems (capa superior) — detección por nombre
        Warn(sb, "SistemaChunks", ExisteComponente("SistemaChunks"));
        Warn(sb, "SistemaClima",  FindAnyObjectByType<SistemaClima>() != null,
             "Sin clima no hay lluvia/niebla dinámica (opcional).");
        Warn(sb, "AlsasuaTreeStreamer", FindAnyObjectByType<AlsasuaTreeStreamer>() != null);

        // Edificios activos en zonas (zone streaming — no hay parent único)
        int nEdifActivos = 0;
        foreach (var go in GameObject.FindGameObjectsWithTag("Untagged"))
            if (go.name.StartsWith("Zona_")) nEdifActivos += go.transform.childCount;
        Warn(sb, $"Edificios en zonas activas ({nEdifActivos})",
             nEdifActivos > 0 || !GeneradorMundoOSM.MundoListo,
             nEdifActivos > 0 ? null : "Jugador fuera del área o indexado incompleto");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  4) ENTIDADES
    // ════════════════════════════════════════════════════════════════════════
    void SecEntidades(StringBuilder sb)
    {
        sb.AppendLine("\n── 4) ENTIDADES ──────────────────────────────────");

        var jugador = FindAnyObjectByType<ControladorJugador>();
        Error(sb, "ControladorJugador", jugador != null,
              "Sin jugador no podrás moverte. Revisa el prefab del jugador en la escena.");
        if (jugador != null)
        {
            sb.AppendLine($"     Posición jugador: {jugador.transform.position}");
            float suelo = GeoDataAlsasua.AlturaTerreno(jugador.transform.position);
            float dy = jugador.transform.position.y - suelo;
            if (dy > 50f || dy < -5f)
                _mejoras.Add($"Jugador a {dy:F0}m del suelo. Ajusta spawn a y={suelo + 2f:F1} para no caer al vacío.");

            bool tieneMesh = jugador.GetComponentInChildren<SkinnedMeshRenderer>() != null
                          || jugador.GetComponentInChildren<MeshRenderer>() != null;
            Warn(sb, "Jugador con malla visible", tieneMesh,
                 "Jugador sin modelo 3D. Asigna prefabPersonaje o usa ConfiguradorPersonajeAAA.");

            // Animator
            var anim = jugador.GetComponent<Animator>() ?? jugador.GetComponentInChildren<Animator>();
            Warn(sb, "Jugador tiene Animator", anim != null);
            if (anim != null)
            {
                Warn(sb, "Controller asignado", anim.runtimeAnimatorController != null,
                     anim.runtimeAnimatorController == null ? "Animator sin controller — T-pose." : null);
                Warn(sb, "Parámetro VelocidadMovimiento",
                     anim.runtimeAnimatorController != null && TieneParametro(anim, "VelocidadMovimiento"));
            }
        }

        int npcs = FindObjectsByType<NPCBase>(FindObjectsSortMode.None).Length;
        sb.AppendLine($"     NPCs activos: {npcs}");
        if (npcs == 0)
            _mejoras.Add("No hay NPCs. Activa el spawner de NPCs para dar vida a la ciudad.");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  5) GAMEPLAY
    // ════════════════════════════════════════════════════════════════════════
    void SecGameplay(StringBuilder sb)
    {
        sb.AppendLine("\n── 5) GAMEPLAY ───────────────────────────────────");
        Error(sb, "GameManagerAltsasua", GameManagerAltsasua.Instance != null,
              "Núcleo de gameplay ausente (Wanted/Economy/Spawn).");
        Error(sb, "SistemaManifestacion", FindAnyObjectByType<SistemaManifestacion>() != null,
              "Sin esto no hay manifestación (mecánica central del juego).");
        Error(sb, "SistemaMisiones",     SistemaMisiones.Instance != null, null);
        Error(sb, "SistemaApoyoPopular", SistemaApoyoPopular.Instance != null, null);
        Error(sb, "SistemaGuardado",     SistemaGuardado.Instance != null, null);
        Error(sb, "SistemaLogros",       SistemaLogros.Instance != null, null);

        // GeneradorInterioresAAA (Systems) e InterioresExplorables (Modules) viven en
        // asambleas superiores que Runtime no puede referenciar — detección por nombre.
        bool intAAA = ExisteComponente("GeneradorInterioresAAA");
        Warn(sb, "GeneradorInterioresAAA (interior mapping)", intAAA,
             "Añade GeneradorInterioresAAA para ventanas con profundidad 3D.");
        if (intAAA && Shader.Find("Altsasu/InteriorMapping") == null)
            _mejoras.Add("El shader Altsasu/InteriorMapping no compiló. Revisa Assets/Shaders/InteriorMapping.shader.");
        Warn(sb, "InterioresExplorables (caminables)", ExisteComponente("InterioresExplorables"),
             "Añade InterioresExplorables para entrar a bar/comisaría/tienda de misión.");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  6) UI / AUDIO
    // ════════════════════════════════════════════════════════════════════════
    void SecUIAudio(StringBuilder sb)
    {
        sb.AppendLine("\n── 6) UI / AUDIO ─────────────────────────────────");
        Error(sb, "HUDCanvas",    FindAnyObjectByType<HUDCanvas>() != null,
              "Sin HUD no verás vida/dinero/wanted en pantalla.");
        Error(sb, "AudioManager", AudioManager.I != null, "Sin AudioManager no hay sonido.");

        var listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
        Warn(sb, $"AudioListener ({listeners.Length})", listeners.Length == 1,
             listeners.Length == 0 ? "Falta AudioListener (normalmente en la cámara)."
                                   : listeners.Length > 1 ? "Hay MÁS de un AudioListener — deja solo 1." : null);

        var clips = Resources.LoadAll<AudioClip>("Audio");
        Warn(sb, $"Clips en Resources/Audio ({clips.Length})", clips.Length >= 10,
             clips.Length < 10 ? $"{clips.Length} clips — mínimo 10 para experiencia completa" : null);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  7) RENDIMIENTO
    // ════════════════════════════════════════════════════════════════════════
    void SecRendimiento(StringBuilder sb)
    {
        sb.AppendLine("\n── 7) RENDIMIENTO ────────────────────────────────");

        float fps = SistemaOptimizacion.FPSActual > 0f
            ? SistemaOptimizacion.FPSActual
            : 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        string estado = fps >= 50 ? "✅" : fps >= 25 ? "⚠" : "❌";
        sb.AppendLine($"  {estado} FPS: {fps:F0}");
        if (fps < 25)
            _mejoras.Add("FPS bajo: baja Quality level, reduce distancia de sombras, o ejecuta OptimizadorMallaOBJ (chunking/LOD).");

        long memMB = System.GC.GetTotalMemory(false) / (1024 * 1024);
        sb.AppendLine($"     Memoria gestionada: {memMB} MB");

        int luces = FindObjectsByType<Light>(FindObjectsSortMode.None).Length;
        sb.AppendLine($"     Luces en escena: {luces}");
        if (luces > 60)
            _mejoras.Add($"Hay {luces} luces. Limita las realtime con sombra; usa Baked donde puedas.");

        int renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length;
        sb.AppendLine($"     Renderers en escena: {renderers}");
        if (renderers > 3000)
            _mejoras.Add($"{renderers} renderers. Activa GPU Instancing + LOD (OptimizadorVisualHDRP) y combina mallas estáticas.");

        if (QualitySettings.GetQualityLevel() == 0)
            _mejoras.Add("Quality level mínimo. Sube a Medium/High si el FPS lo permite.");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Utilidades
    // ════════════════════════════════════════════════════════════════════════

    void Error(StringBuilder sb, string nombre, bool ok, string mejoraSiFalla = null)
        => Linea(sb, nombre, ok, "❌", _errores, mejoraSiFalla);

    void Warn(StringBuilder sb, string nombre, bool ok, string mejoraSiFalla = null)
        => Linea(sb, nombre, ok, "⚠ ", _warnings, mejoraSiFalla);

    void Linea(StringBuilder sb, string nombre, bool ok, string iconoFallo,
               List<string> bucket, string mejora)
    {
        sb.AppendLine($"  {(ok ? "✅" : iconoFallo)} {nombre}");
        if (ok) return;
        bucket.Add(nombre);
        if (!string.IsNullOrEmpty(mejora)) _mejoras.Add(mejora);
    }

    static bool TieneParametro(Animator anim, string param)
    {
        foreach (var p in anim.parameters)
            if (p.name == param) return true;
        return false;
    }

    /// <summary>
    /// Detecta un componente por nombre de tipo aunque viva en una asamblea
    /// superior (Alsasua.Systems/Modules) que Runtime no puede referenciar.
    /// </summary>
    static bool ExisteComponente(string nombreTipo)
    {
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var tipo = asm.GetType(nombreTipo);
            if (tipo == null || !typeof(Component).IsAssignableFrom(tipo)) continue;
            return FindAnyObjectByType(tipo) != null;
        }
        return false;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  UI — panel de diagnóstico (F1 para abrir/cerrar)
    // ════════════════════════════════════════════════════════════════════════

    void OnGUI()
    {
        if (!_mostrandoPanel) return;
        InicializarEstilos();

        float w = 540f, h = Mathf.Min(Screen.height * 0.8f, 600f);
        float x = (Screen.width - w) / 2f;
        float y = (Screen.height - h) / 2f;

        GUI.color = new Color(0, 0, 0, 0.92f);
        GUI.DrawTexture(new Rect(x - 8, y - 8, w + 16, h + 16), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(x, y, w, h));
        GUI.Label(new Rect(0, 0, w, 28), "F1 = cerrar | DIAGNÓSTICO DE ARRANQUE", _estiloTitulo);
        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(h - 36));
        GUI.Label(new Rect(0, 0, w - 20, 3000), _informe, _estiloPanel);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void InicializarEstilos()
    {
        if (_estilosInit) return; _estilosInit = true;
        _estiloPanel  = new GUIStyle(GUI.skin.label)
            { fontSize = 12, wordWrap = true, richText = true,
              normal = { textColor = new Color(0.88f, 0.88f, 0.92f) } };
        _estiloTitulo = new GUIStyle(GUI.skin.label)
            { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
              normal   = { textColor = new Color(1f, 0.92f, 0.4f) } };
    }
}
