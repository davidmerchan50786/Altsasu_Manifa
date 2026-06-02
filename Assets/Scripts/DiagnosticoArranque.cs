// Assets/Scripts/DiagnosticoArranque.cs
// ═══════════════════════════════════════════════════════════════════════════
//  DIAGNÓSTICO COMPLETO DE ARRANQUE — Altsasu Manifa
//
//  Adjunta este componente a cualquier GameObject de la escena (ej. GameManager)
//  y pulsa Play. Imprime en Console un informe en 7 secciones:
//    1) Render / HDRP        4) Entidades        7) Rendimiento
//    2) Datos (LIDAR/JSON)   5) Gameplay
//    3) Core / World         6) UI / Audio
//  Cada línea ✅/❌/⚠️  y al final OPCIONES DE MEJORA realistas según lo detectado.
//
//  Quítalo (o desactívalo) cuando ya no lo necesites — no afecta al juego.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using System.Collections.Generic;
using System.IO;
using System.Text;

[DefaultExecutionOrder(9999)] // ejecutar DESPUÉS de que todo inicialice
public class DiagnosticoArranque : MonoBehaviour
{
    [Tooltip("Repetir el informe cada X segundos (0 = solo una vez al arrancar)")]
    public float repetirCada = 0f;

    readonly List<string> _mejoras = new();
    float _timer;

    void Start() => Ejecutar();

    void Update()
    {
        if (repetirCada <= 0f) return;
        _timer += Time.deltaTime;
        if (_timer < repetirCada) return;
        _timer = 0f;
        Ejecutar();
    }

    void Ejecutar()
    {
        _mejoras.Clear();
        var sb = new StringBuilder();
        sb.AppendLine("\n╔══════════════════════════════════════════════════════════╗");
        sb.AppendLine("║   DIAGNÓSTICO COMPLETO — Altsasu Manifa                  ║");
        sb.AppendLine("╚══════════════════════════════════════════════════════════╝");

        Render(sb);
        Datos(sb);
        CoreWorld(sb);
        Entidades(sb);
        Gameplay(sb);
        UIAudio(sb);
        Rendimiento(sb);

        // ── Opciones de mejora ──────────────────────────────────────────────
        sb.AppendLine("\n┌─ OPCIONES DE MEJORA (realistas) ─────────────────────────┐");
        if (_mejoras.Count == 0)
            sb.AppendLine("  🎉 Nada crítico detectado. El juego está listo.");
        else
            for (int i = 0; i < _mejoras.Count; i++)
                sb.AppendLine($"  {i + 1}. {_mejoras[i]}");
        sb.AppendLine("└──────────────────────────────────────────────────────────┘");
        sb.AppendLine("  Copia este informe completo al chat si algo falla.");

        Debug.Log(sb.ToString());
    }

    // ════════════════════════════════════════════════════════════════════════
    //  1) RENDER / HDRP
    // ════════════════════════════════════════════════════════════════════════
    void Render(StringBuilder sb)
    {
        sb.AppendLine("\n── 1) RENDER / HDRP ──────────────────────────────");

        bool hdrp = GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset;
        Check(sb, "Pipeline HDRP activo", hdrp,
              "Sin HDRP todo se verá rosa. Edit→Project Settings→Graphics→asignar HDRP asset.");

        bool cam = Camera.main != null;
        Check(sb, "Camera.main (tag MainCamera)", cam,
              "Asigna el tag 'MainCamera' a la cámara del jugador o no verás nada.");

        var vols = FindObjectsByType<Volume>(FindObjectsSortMode.None);
        Check(sb, $"HDRP Volume ({vols.Length})", vols.Length > 0,
              "Añade un Volume global con el perfil 'HDRP Balanced' para SSAO/Bloom/Fog.");

        // Sol direccional
        Light sol = null;
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) { sol = l; break; }
        Check(sb, "Sol direccional", sol != null,
              "Sin sol direccional la escena está negra. SceneBootstrapper debería crearlo.");

        if (sol != null)
        {
            var hd = sol.GetComponent<HDAdditionalLightData>();
            Check(sb, "  Sol con HDAdditionalLightData", hd != null,
                  "En HDRP la luz no se ve sin HDAdditionalLightData + SetIntensity(Lux).");
            if (hd != null)
                sb.AppendLine($"     Intensidad sol: {hd.intensity:F0} lux");
        }

        sb.AppendLine($"     Quality level: {QualitySettings.GetQualityLevel()} " +
                      $"({QualitySettings.names[QualitySettings.GetQualityLevel()]})");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  2) DATOS (solo en Editor — en build se empaquetan distinto)
    // ════════════════════════════════════════════════════════════════════════
    void Datos(StringBuilder sb)
    {
        sb.AppendLine("\n── 2) DATOS (LIDAR / JSON) ───────────────────────");
#if UNITY_EDITOR
        DatoArchivo(sb, "lidar_dtm_05m.raw",   8_000_000); // ~8.4 MB real
        DatoArchivo(sb, "dem_unity_1025.raw",  2_000_000); // ~2.1 MB real
        DatoArchivo(sb, "lidar_buildings.json", 100_000);
        DatoArchivo(sb, "buildings_unity.json", 100_000);
        DatoArchivo(sb, "trees_unity.json",     50_000);
        DatoArchivo(sb, "roads_unity.json",     10_000);
        DatoArchivo(sb, "ortofoto_unity.png",   100_000);
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
            Check(sb, nombre, false, $"Falta {nombre}. Revisa Assets/AlsasuaData/.");
            return;
        }
        long size = new FileInfo(ruta).Length;
        if (size < 200) // puntero Git LFS no descargado
        {
            Check(sb, $"{nombre} (LFS sin descargar)", false,
                  $"{nombre} es un puntero LFS de {size}B. Ejecuta: git lfs pull");
            return;
        }
        bool ok = size >= minBytes * 0.5f; // tolerancia
        Check(sb, $"{nombre} ({size / 1024}KB)", ok,
              ok ? null : $"{nombre} parece truncado ({size}B, esperado >{minBytes / 1024}KB).");
    }
#endif

    // ════════════════════════════════════════════════════════════════════════
    //  3) CORE / WORLD
    // ════════════════════════════════════════════════════════════════════════
    void CoreWorld(StringBuilder sb)
    {
        sb.AppendLine("\n── 3) CORE / WORLD ───────────────────────────────");

        Check(sb, "AltsasuCore", AltsasuCore.I != null,
              "Singleton central ausente. SceneBootstrapper debería crearlo.");

        var t = Terrain.activeTerrain;
        Check(sb, "Terreno activo", t != null,
              "No hay terreno. Menú Territorio Real → GENERAR TODO, o revisa GeneradorTerrenoUltraPreciso.");
        if (t != null)
        {
            var td = t.terrainData;
            sb.AppendLine($"     {td.heightmapResolution}x{td.heightmapResolution} | " +
                          $"{td.size.x:F0}x{td.size.z:F0}m | altura máx {td.size.y:F1}m");
            if (td.heightmapResolution < 1025)
                _mejoras.Add("Terreno a baja resolución. Regenera desde lidar_dtm_05m.raw (2049) para máximo detalle.");
            // ¿splatmap aplicado?
            bool tieneCapas = td.terrainLayers != null && td.terrainLayers.Length > 0;
            Check(sb, "  Terrain layers (texturas)", tieneCapas,
                  "Terreno sin texturas. Ejecuta SistemaTerreno (splatmap 8 biomas).");
            // ¿ortofoto?
            bool tieneArboles = td.treeInstanceCount > 0;
            sb.AppendLine($"     Árboles en terrain: {td.treeInstanceCount}");
            if (!tieneArboles)
                _mejoras.Add("No hay árboles. Activa AlsasuaTreeStreamer (2956 árboles LIDAR reales).");
        }

        Check(sb, "SistemaChunks",  FindAnyObjectByType<SistemaChunks>() != null, null);
        Check(sb, "SistemaZonas",   FindAnyObjectByType<SistemaZonas>() != null, null);
        Check(sb, "SistemaClima",   FindAnyObjectByType<SistemaClima>() != null,
              "Sin clima no hay lluvia/niebla dinámica (opcional).");
        Check(sb, "AlsasuaTreeStreamer", FindAnyObjectByType<AlsasuaTreeStreamer>() != null, null);

        var edif = FindAnyObjectByType<SistemaEdificiosAAA>();
        Check(sb, "SistemaEdificiosAAA", edif != null,
              "Sin generador de edificios la ciudad estará vacía.");
        int nEdificios = ContarPorNombre("edificio", "building");
        sb.AppendLine($"     Edificios en escena: {nEdificios}");
        if (edif != null && nEdificios < 50)
            _mejoras.Add("Pocos edificios generados (<50). Revisa buildings_unity.json y los logs de SistemaEdificiosAAA.");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  4) ENTIDADES
    // ════════════════════════════════════════════════════════════════════════
    void Entidades(StringBuilder sb)
    {
        sb.AppendLine("\n── 4) ENTIDADES ──────────────────────────────────");

        var jugador = FindAnyObjectByType<ControladorJugador>();
        Check(sb, "ControladorJugador", jugador != null,
              "Sin jugador no podrás moverte. Revisa el prefab del jugador en la escena.");
        if (jugador != null)
        {
            sb.AppendLine($"     Posición jugador: {jugador.transform.position}");
            // ¿está sobre el terreno o cayendo al vacío?
            float suelo = GeoDataAlsasua.AlturaTerreno(jugador.transform.position);
            float dy = jugador.transform.position.y - suelo;
            if (dy > 50f || dy < -5f)
                _mejoras.Add($"Jugador a {dy:F0}m del suelo. Ajusta spawn a y={suelo + 2f:F1} para no caer al vacío.");
            // ¿tiene personaje visible (mesh)?
            bool tieneMesh = jugador.GetComponentInChildren<SkinnedMeshRenderer>() != null
                          || jugador.GetComponentInChildren<MeshRenderer>() != null;
            Check(sb, "  Jugador con malla visible", tieneMesh,
                  "Jugador sin modelo 3D. Asigna prefabPersonaje (Ch20/MeshyAI) o usa ConfiguradorPersonajeAAA.");
        }

        int npcs = FindObjectsByType<NPCBase>(FindObjectsSortMode.None).Length;
        sb.AppendLine($"     NPCs activos: {npcs}");
        if (npcs == 0)
            _mejoras.Add("No hay NPCs. Activa el spawner de NPCs para dar vida a la ciudad.");

        Check(sb, "NavMesh horneado", NavMeshDisponible(),
              "Sin NavMesh los NPCs no caminan. Window→AI→Navigation→Bake o SistemaNavMesh.");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  5) GAMEPLAY
    // ════════════════════════════════════════════════════════════════════════
    void Gameplay(StringBuilder sb)
    {
        sb.AppendLine("\n── 5) GAMEPLAY ───────────────────────────────────");
        Check(sb, "GameManagerAltsasua",   FindAnyObjectByType<GameManagerAltsasua>() != null,
              "Núcleo de gameplay ausente (Wanted/Economy/Spawn).");
        Check(sb, "SistemaManifestacion",  FindAnyObjectByType<SistemaManifestacion>() != null,
              "Sin esto no hay manifestación (mecánica central del juego).");
        Check(sb, "SistemaApoyoPopular",   FindAnyObjectByType<SistemaApoyoPopular>() != null, null);
        Check(sb, "SistemaMisiones",       FindAnyObjectByType<SistemaMisiones>() != null, null);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  6) UI / AUDIO
    // ════════════════════════════════════════════════════════════════════════
    void UIAudio(StringBuilder sb)
    {
        sb.AppendLine("\n── 6) UI / AUDIO ─────────────────────────────────");
        Check(sb, "HUDCanvas",    FindAnyObjectByType<HUDCanvas>() != null,
              "Sin HUD no verás vida/dinero/wanted en pantalla.");
        Check(sb, "AudioManager", FindAnyObjectByType<AudioManager>() != null,
              "Sin AudioManager no hay sonido.");

        var listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
        Check(sb, $"AudioListener ({listeners.Length})", listeners.Length == 1,
              listeners.Length == 0 ? "Falta AudioListener (normalmente en la cámara)."
                                    : "Hay MÁS de un AudioListener — Unity avisará. Deja solo 1.");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  7) RENDIMIENTO
    // ════════════════════════════════════════════════════════════════════════
    void Rendimiento(StringBuilder sb)
    {
        sb.AppendLine("\n── 7) RENDIMIENTO ────────────────────────────────");

        float fps = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        string estado = fps >= 50 ? "✅" : fps >= 25 ? "⚠️" : "❌";
        sb.AppendLine($"  {estado} FPS instantáneo: {fps:F0}");

        long memMB = System.GC.GetTotalMemory(false) / (1024 * 1024);
        sb.AppendLine($"     Memoria gestionada: {memMB} MB");

        int luces = FindObjectsByType<Light>(FindObjectsSortMode.None).Length;
        sb.AppendLine($"     Luces en escena: {luces}");
        if (luces > 60)
            _mejoras.Add($"Hay {luces} luces. En HDRP limita las luces realtime con sombra; usa modo Baked donde puedas.");

        int renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length;
        sb.AppendLine($"     Renderers en escena: {renderers}");
        if (renderers > 3000)
            _mejoras.Add($"{renderers} renderers. Activa GPU Instancing + LOD (OptimizadorVisualHDRP) y combina mallas estáticas.");

        if (fps < 25)
            _mejoras.Add("FPS bajo: baja el Quality level, reduce distancia de sombras del sol, o ejecuta OptimizadorTerreno (chunking/LOD).");

        // Sugerencias de calidad GRATIS siempre útiles
        if (QualitySettings.GetQualityLevel() == 0)
            _mejoras.Add("Estás en el Quality level más bajo. Sube a Medium/High si el FPS lo permite para mejor imagen.");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Utilidades
    // ════════════════════════════════════════════════════════════════════════
    void Check(StringBuilder sb, string nombre, bool ok, string mejoraSiFalla)
    {
        sb.AppendLine($"  {(ok ? "✅" : "❌")} {nombre}");
        if (!ok && !string.IsNullOrEmpty(mejoraSiFalla))
            _mejoras.Add(mejoraSiFalla);
    }

    static int ContarPorNombre(params string[] claves)
    {
        int n = 0;
        foreach (var r in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            string s = r.name.ToLower();
            foreach (var k in claves)
                if (s.Contains(k)) { n++; break; }
        }
        return n;
    }

    static bool NavMeshDisponible()
    {
        var tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
        return tri.vertices != null && tri.vertices.Length > 0;
    }
}
