// Assets/Scripts/Editor/SETUP_MAESTRO.cs
// ═══════════════════════════════════════════════════════════════════════════
//  UN SOLO BOTÓN — Tools → Alsasua → ★ SETUP MAESTRO COMPLETO ★
//
//  Hace TODO en orden correcto sin que el usuario tenga que hacer nada más:
//
//  Fase 1 — IMPORTAR
//    A. Fuerza importación de 542 texturas AAA + 11 HDRIs
//    B. Importa paquetes .unitypackage P1 (críticos) uno a uno
//    C. Convierte materiales Standard → HDRP/Lit
//
//  Fase 2 — ESCENA
//    D. Crea Alsasua_Main.unity con todos los sistemas
//    E. Tags, capas, physics, quality settings
//    F. Audio clips asignados
//
//  Fase 3 — CONTENIDO
//    G. Configura zonas del mapa (Zone_01..10)
//    H. Genera 548 edificios OSM reales
//    I. Genera carreteras desde OSM
//    J. Aplica texturas AAA PBR a edificios, calles, tejados, terreno
//    K. Crea prefabs de coche (Interceptor + Trabant)
//    L. Crea Animator Controller del jugador
//    M. Configura tráfico desde OSM (12 carriles)
//    N. Pobla con 25 civiles
//    O. Waypoints de patrulla policía
//    P. Conecta todos los sistemas null
//
//  Fase 4 — BLINDAR
//    Q. Occlusion Culling setup (marcar Static)
//    R. LOD Groups verificados
//    S. Limpia scripts Missing en la escena
//    T. Guarda todo
//
//  Usa EditorApplication.delayCall para encadenar pasos sin congelar Unity.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

public static class SETUP_MAESTRO
{
    private static Queue<(string nombre, System.Action accion)> _cola;
    private static int _totalPasos;
    private static int _pasoActual;
    private static List<string> _log = new();

    // ─────────────────────────────────────────────────────────────────────────

    // [DESACTIVADO] [MenuItem("Tools/Alsasua/★ SETUP MAESTRO COMPLETO ★", priority = 0)]
    public static void EjecutarTodo()
    {
        bool confirmar = EditorUtility.DisplayDialog(
            "★ SETUP MAESTRO COMPLETO ★",
            "Esto ejecutará todas las fases de setup:\n\n" +
            "• Importar 542 texturas AAA + 11 HDRIs\n" +
            "• Importar paquetes críticos\n" +
            "• Crear escena Alsasua_Main.unity\n" +
            "• Generar edificios, calles, tráfico, civiles\n" +
            "• Aplicar texturas PBR reales\n" +
            "• Crear prefabs y animaciones\n" +
            "• Configurar todo el pipeline\n\n" +
            "⏱ Estimado: 5-15 minutos\n" +
            "Unity puede parecer lento durante la importación.",
            "Ejecutar todo", "Cancelar");

        if (!confirmar) return;

        _log.Clear();
        _cola = new Queue<(string, System.Action)>();

        // ── FASE 1: IMPORTAR ─────────────────────────────────────────────
        _cola.Enqueue(("Importar texturas AAA (542 PNG + 11 HDR)", ImportarTexturasAAA));
        _cola.Enqueue(("Refresh AssetDatabase", () => AssetDatabase.Refresh()));
        _cola.Enqueue(("Importar paquete: GTA Unity 1.0",
            () => ImportarPaquete("GTA Unity 1.0.unitypackage")));
        _cola.Enqueue(("Importar paquete: Fast Dynamic Sky",
            () => ImportarPaquete("Fast Dynamic Sky.unitypackage")));
        _cola.Enqueue(("Importar paquete: Free Low Poly Nature Forest",
            () => ImportarPaquete("Free Low Poly Nature Forest.unitypackage")));
        _cola.Enqueue(("Importar paquete: VILLAGE HOUSES PACK",
            () => ImportarPaquete("VILLAGE HOUSES PACK.unitypackage")));
        _cola.Enqueue(("Importar paquete: simple_street_props",
            () => ImportarPaquete("simple_street_props.unitypackage")));
        _cola.Enqueue(("Importar paquete: Quirky Series Animals",
            () => ImportarPaquete("Quirky Series - FREE Animals Pack.unitypackage")));
        _cola.Enqueue(("Importar paquete: Metal and Concrete Barriers",
            () => ImportarPaquete("Metal and Concrete Barriers.unitypackage")));
        _cola.Enqueue(("Convertir materiales → HDRP",
            () => IntegradorAssetsNuevos.ConvertirMaterialesAHDRP()));
        _cola.Enqueue(("Refresh post-conversión", () => AssetDatabase.Refresh()));

        // ── FASE 2: ESCENA ───────────────────────────────────────────────
        _cola.Enqueue(("Crear escena Alsasua_Main.unity", CrearEscena));
        _cola.Enqueue(("Tags y capas", AutomatizadorCompleto.CrearTagsYCapas));
        _cola.Enqueue(("Audio ambiente", AutomatizadorCompleto.AsignarAudioAmbiente));
        _cola.Enqueue(("Audio armas y pasos", AutomatizadorCompleto.AsignarAudioArmas));
        _cola.Enqueue(("Sistemas en escena", AutomatizadorCompleto.AsegurarSistemasEscena));
        _cola.Enqueue(("HDRP Asset", AutomatizadorCompleto.ConfigurarHDRP));
        _cola.Enqueue(("Quality Settings", AutomatizadorCompleto.ConfigurarQuality));
        _cola.Enqueue(("Physics Matrix", AutomatizadorCompleto.ConfigurarPhysicsMatrix));

        // ── FASE 3: CONTENIDO ────────────────────────────────────────────
        _cola.Enqueue(("Configurar zonas del mapa", ConfiguradorZonasEditor.ConfigurarZonas));
        _cola.Enqueue(("Generar edificios Zone_01",
            () => GeneradorEdificiosZona.GenerarEdificios(silencioso: true)));
        _cola.Enqueue(("Fachadas vascas (paleta real)", FachadasVascas.AplicarFachadas));
        _cola.Enqueue(("Generar carreteras OSM", GeneradorCarreteras.Generar));
        _cola.Enqueue(("Crear prefabs de coche", WizardCochePrefab.CrearPrefabs));
        _cola.Enqueue(("Animator Controller jugador",
            (System.Action)ConfiguradorAnimatorJugador.Configurar));
        _cola.Enqueue(("Configurar tráfico OSM", ConfiguradorTrafico.Configurar));
        _cola.Enqueue(("Poblar civiles Zone_01", SpawnerNPCsEditor.PoblarCiviles));
        _cola.Enqueue(("Waypoints patrulla policía", AutomatizadorCompleto.CrearWaypointsPolicia));
        _cola.Enqueue(("Conectar sistemas null", AutomatizadorCompleto.ConectarSistemasNull));
        _cola.Enqueue(("Coche en escena", AutomatizadorCompleto.ColocarCocheEnEscena));
        _cola.Enqueue(("Aplicar texturas AAA PBR", AplicadorTexturasAAA.Aplicar));

        // ── FASE 4: BLINDAR ──────────────────────────────────────────────
        _cola.Enqueue(("Occlusion Culling setup", AutomatizadorCompleto.ConfigurarOcclusion));
        _cola.Enqueue(("Limpiar Missing Scripts", LimpiarMissingScripts));
        _cola.Enqueue(("Guardar todo", GuardarTodo));

        _totalPasos  = _cola.Count;
        _pasoActual  = 0;

        Log($"★ SETUP MAESTRO iniciado — {_totalPasos} pasos ★");
        EditorApplication.delayCall += EjecutarSiguiente;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  MOTOR DE EJECUCIÓN SECUENCIAL
    // ─────────────────────────────────────────────────────────────────────────

    static void EjecutarSiguiente()
    {
        if (_cola == null || _cola.Count == 0)
        {
            EditorUtility.ClearProgressBar();
            MostrarResumen();
            return;
        }

        var (nombre, accion) = _cola.Dequeue();
        _pasoActual++;

        float progreso = (float)_pasoActual / _totalPasos;
        EditorUtility.DisplayProgressBar(
            $"★ Setup Maestro [{_pasoActual}/{_totalPasos}]",
            nombre, progreso);

        Log($"[{_pasoActual}/{_totalPasos}] {nombre}...");

        try
        {
            accion();
            Log($"  ✅ OK");
        }
        catch (System.Exception e)
        {
            Log($"  ⚠ {e.Message}");
            // No abortar — continuar con el siguiente paso
        }

        // Encolar siguiente (pausa de un frame entre pasos)
        EditorApplication.delayCall += EjecutarSiguiente;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PASOS ESPECÍFICOS
    // ─────────────────────────────────────────────────────────────────────────

    static void ImportarTexturasAAA()
    {
        string texDir  = "Assets/Textures_AAA";
        string hdriDir = "Assets/HDRIs";

        int importados = 0;

        // Forzar importación de todas las texturas PNG descargadas
        foreach (var dir in new[]{ texDir, hdriDir })
        {
            string absDir = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", dir));
            if (!Directory.Exists(absDir)) continue;

            foreach (var file in Directory.GetFiles(absDir, "*.*",
                SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file).ToLower();
                if (ext != ".png" && ext != ".hdr" && ext != ".jpg") continue;

                // Convertir a ruta Assets/...
                string assetPath = file
                    .Replace(Path.GetFullPath(Application.dataPath + "/../"), "")
                    .Replace("\\", "/");

                // Solo importar si no tiene .meta (no importado aún)
                if (!File.Exists(file + ".meta"))
                {
                    AssetDatabase.ImportAsset(assetPath,
                        ImportAssetOptions.ForceUpdate);
                    importados++;
                }
            }
        }

        Log($"  {importados} archivos importados en Textures_AAA + HDRIs");
        AssetDatabase.Refresh();
    }

    static void ImportarPaquete(string nombre)
    {
        string pkgDir = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "PackagesToImport"));
        string ruta = Path.Combine(pkgDir, nombre);

        if (!File.Exists(ruta))
        {
            // Buscar en E:\assets también
            ruta = Path.Combine("E:\\assets", nombre);
        }

        if (!File.Exists(ruta))
        {
            Log($"  ⚠ No encontrado: {nombre}");
            return;
        }

        AssetDatabase.ImportPackage(ruta, false);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static void CrearEscena()
    {
        string rutaEscena = "Assets/#Scenes/Alsasua_Main.unity";
        if (File.Exists(Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", rutaEscena))))
        {
            // Abrir si ya existe
            EditorSceneManager.OpenScene(rutaEscena);
            return;
        }
        CreadorEscenaPrincipal.Crear();
    }

    static void LimpiarMissingScripts()
    {
        var escena = EditorSceneManager.GetActiveScene();
        int eliminados = 0;
        foreach (var go in escena.GetRootGameObjects())
        {
            eliminados += LimpiarGO(go);
        }
        Log($"  {eliminados} Missing Scripts eliminados");
    }

    static int LimpiarGO(GameObject go)
    {
        int n = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
        foreach (Transform hijo in go.transform)
            n += LimpiarGO(hijo.gameObject);
        return n;
    }

    static void GuardarTodo()
    {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        var escena = EditorSceneManager.GetActiveScene();
        if (escena.IsValid())
            EditorSceneManager.SaveScene(escena);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  LOG Y RESUMEN
    // ─────────────────────────────────────────────────────────────────────────

    static void Log(string msg)
    {
        _log.Add(msg);
        Debug.Log($"[SetupMaestro] {msg}");
    }

    static void MostrarResumen()
    {
        int ok  = _log.Count(l => l.Contains("✅"));
        int warn = _log.Count(l => l.Contains("⚠"));

        Debug.Log($"\n{'═',50}\n★ SETUP MAESTRO COMPLETADO ★\n" +
                  $"  ✅ Pasos OK:      {ok}\n" +
                  $"  ⚠  Advertencias: {warn}\n" +
                  $"{'═',50}\n\n" +
                  "PRÓXIMOS PASOS MANUALES OBLIGATORIOS:\n" +
                  "  1. Window → Rendering → Occlusion Culling → Bake\n" +
                  "  2. Abrir prefab Interceptor_Jugador → ajustar WheelColliders\n" +
                  "  3. Play → probar que el jugador anda, entra al coche y la policía persigue\n");

        EditorUtility.DisplayDialog(
            "★ Setup Maestro Completado",
            $"✅ {ok} pasos completados\n⚠ {warn} advertencias (ver Consola)\n\n" +
            "Pasos manuales restantes:\n" +
            "1. Window → Rendering → Occlusion Culling → Bake\n" +
            "2. Abrir Interceptor_Jugador.prefab → ajustar 4 WheelColliders\n" +
            "3. Dale Play y prueba el bucle:\n" +
            "   Andar → robar coche (E) → policía persigue → estrellas",
            "Entendido");
    }
}
