// Assets/Scripts/Editor/BUILD_TODO.cs
// Tools → Alsasua → ██ BUILD TODO ██
//
// UN SOLO BOTÓN — construye el simulador completo en orden correcto:
//
//  PASO 1  Crear escena Alsasua_Main.unity con jerarquía v4 completa
//  PASO 2  Configurar Cesium (define + Georeference + Google 3D Tiles)
//  PASO 3  Animator Controller del jugador (parámetros exactos de ControladorJugador)
//  PASO 4  Prefabs NPC (6 variantes: civil, manifestante, policía, etc.)
//  PASO 5  Prefabs vehículos (Trabant, Ambulancia, Tractor, Camión)
//  PASO 6  Tags y capas del proyecto
//  PASO 7  Quality settings para 60 FPS
//  PASO 8  Build Settings (MenuPrincipal + Alsasua_Main)
//  PASO 9  Guardar todo y refrescar AssetDatabase

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public static class BUILD_TODO
{
    [MenuItem("Tools/Alsasua/██ BUILD TODO ██", priority = 0)]
    public static void EjecutarTodo()
    {
        bool ok = EditorUtility.DisplayDialog(
            "██ BUILD TODO ██",
            "Construirá el simulador completo:\n\n" +
            "✔ Escena principal con 30 sistemas\n" +
            "✔ Cesium → Google Photorealistic 3D Tiles\n" +
            "✔ Animator Controller jugador\n" +
            "✔ 6 prefabs NPC (civil, manifestante, policía...)\n" +
            "✔ 4 prefabs vehículo conducible (Trabant, Ambulancia...)\n" +
            "✔ Tags, capas, quality settings\n" +
            "✔ Build Settings ordenados\n\n" +
            "⏱ ~2-5 minutos.\n" +
            "Unity puede parecer lento durante la importación.",
            "██ EJECUTAR TODO ██", "Cancelar");

        if (!ok) return;

        _pasos = new Queue<(string, System.Action)>();

        _pasos.Enqueue(("Escena principal v4",           () => CreadorEscenaPrincipal.Crear()));
        _pasos.Enqueue(("Cesium + Google 3D Tiles",      () => ConfiguradorCesiumAlsasua.Configurar()));
        _pasos.Enqueue(("Animator Controller jugador",   () => ConfiguradorAnimatorJugador.Configurar()));
        _pasos.Enqueue(("Prefabs NPC",                   () => ConstructorNPCsPrefabs.Construir()));
        _pasos.Enqueue(("Prefabs vehículos",             () => ConstructorVehiculosPrefabs.Construir()));
        _pasos.Enqueue(("Tags y capas",                  CrearTagsYCapas));
        _pasos.Enqueue(("Quality settings 60fps",        ConfigurarQuality));
        _pasos.Enqueue(("Build Settings",                ConfigurarBuildSettings));
        _pasos.Enqueue(("Guardar todo",                  GuardarTodo));

        _total   = _pasos.Count;
        _actual  = 0;
        _log     = new List<string>();

        EditorApplication.delayCall += Siguiente;
    }

    // ── Motor de pasos ────────────────────────────────────────────────────

    static Queue<(string, System.Action)> _pasos;
    static int _total, _actual;
    static List<string> _log;

    static void Siguiente()
    {
        if (_pasos == null || _pasos.Count == 0)
        {
            EditorUtility.ClearProgressBar();
            Resumen();
            return;
        }

        var (nombre, accion) = _pasos.Dequeue();
        _actual++;

        float p = (float)_actual / _total;
        EditorUtility.DisplayProgressBar($"BUILD TODO [{_actual}/{_total}]", nombre, p);

        try
        {
            accion();
            _log.Add($"✅ {nombre}");
        }
        catch (System.Exception e)
        {
            _log.Add($"⚠ {nombre}: {e.Message}");
            Debug.LogWarning($"[BUILD_TODO] {nombre}: {e.Message}");
        }

        EditorApplication.delayCall += Siguiente;
    }

    static void Resumen()
    {
        string resumen = string.Join("\n", _log);
        Debug.Log($"[BUILD_TODO] ✅ Completado:\n{resumen}");
        EditorUtility.DisplayDialog("✅ BUILD TODO completado",
            resumen + "\n\n" +
            "SIGUIENTE:\n" +
            "1. Window → Cesium → Connect to Cesium ion (si no has conectado)\n" +
            "2. Pulsa PLAY — el mundo se genera automáticamente\n" +
            "3. Espera ~20s hasta ver '[Core] ✅ Boot completo'",
            "OK");
    }

    // ── Pasos adicionales ─────────────────────────────────────────────────

    static void CrearTagsYCapas()
    {
        // Tags necesarios
        string[] tags = { "Player", "Enemy", "NPC", "Vehicle", "Projectile",
                          "Ground", "Building", "Prop", "Weapon" };
        foreach (var tag in tags)
        {
            try { if (!System.Array.Exists(UnityEditorInternal.InternalEditorUtility.tags, t => t == tag))
                    UnityEditorInternal.InternalEditorUtility.AddTag(tag); }
            catch { }
        }

        // Capas
        var layers = new (int idx, string nombre)[]
        {
            (8,  "Ground"),
            (9,  "Player"),
            (10, "NPC"),
            (11, "Vehicle"),
            (12, "Projectile"),
            (13, "Building"),
            (14, "Ignore"),
        };
        var so = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layersProp = so.FindProperty("layers");
        foreach (var (idx, nombre) in layers)
        {
            var p = layersProp.GetArrayElementAtIndex(idx);
            if (string.IsNullOrEmpty(p.stringValue)) p.stringValue = nombre;
        }
        so.ApplyModifiedProperties();

        Debug.Log("[BUILD_TODO] Tags y capas configurados.");
    }

    static void ConfigurarQuality()
    {
        QualitySettings.shadowDistance   = 300f;
        QualitySettings.shadowCascades   = 4;
        QualitySettings.lodBias          = 2.5f;
        QualitySettings.maximumLODLevel  = 0;
        QualitySettings.vSyncCount       = 0;
        Application.targetFrameRate      = 60;

        // Physics — sin sleep para los NPCs
        Physics.defaultSolverIterations       = 8;
        Physics.defaultSolverVelocityIterations = 2;

        // Layer collision matrix: projectiles no colisionan entre sí
        Physics.IgnoreLayerCollision(12, 12);   // Projectile vs Projectile
        Physics.IgnoreLayerCollision(10, 10);   // NPC vs NPC (less friction)

        Debug.Log("[BUILD_TODO] Quality settings aplicados.");
    }

    static void ConfigurarBuildSettings()
    {
        var scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/#Scenes/MenuPrincipal.unity", true),
            new EditorBuildSettingsScene("Assets/#Scenes/Alsasua_Main.unity",  true),
        };

        var existentes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (var s in scenes)
        {
            bool yaExiste = existentes.Exists(e => e.path == s.path);
            if (!yaExiste) existentes.Add(s);
            else
            {
                int idx = existentes.FindIndex(e => e.path == s.path);
                existentes[idx] = s;
            }
        }
        EditorBuildSettings.scenes = existentes.ToArray();
        Debug.Log("[BUILD_TODO] Build Settings configurados (MenuPrincipal + Alsasua_Main).");
    }

    static void GuardarTodo()
    {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        UnityEditor.SceneManagement.EditorSceneManager
            .SaveOpenScenes();
        Debug.Log("[BUILD_TODO] Todo guardado.");
    }
}
#endif
