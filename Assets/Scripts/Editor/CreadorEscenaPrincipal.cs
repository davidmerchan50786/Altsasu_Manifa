// Assets/Scripts/Editor/CreadorEscenaPrincipal.cs
// Tools → Alsasua → 🎬 Crear Escena Principal Alsasua
//
// Crea Assets/#Scenes/Alsasua_Main.unity con todos los GameObjects
// necesarios para que el juego arranque y funcione.

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.IO;

public static class CreadorEscenaPrincipal
{
    private const string RUTA_ESCENA = "Assets/#Scenes/Alsasua_Main.unity";

    [MenuItem("Tools/Alsasua/🎬 Crear Escena Principal", priority = 2)]
    public static void Crear()
    {
        // Guardar escena actual
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // Crear escena nueva
        var escena = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── 1. SceneBootstrapper ─────────────────────────────────────────
        var bootstrap = new GameObject("SceneBootstrapper");
        var sb = bootstrap.AddComponent<SceneBootstrapper>();
        // centroX/centroZ = Herriko Plaza en coordenadas de terreno
        SetField(sb, "centroX", 1918f);
        SetField(sb, "centroZ", 8570f);
        SetField(sb, "generarTerrenoDesdeDEM", true);
        SetField(sb, "usarPlanoCuadradoFallback", true);
        SetField(sb, "crearJugadorSiNoHay", true);

        // ── 2. GameManager ───────────────────────────────────────────────
        var gmGO = new GameObject("GameManager");
        gmGO.AddComponent<GameManagerAltsasua>();
        gmGO.AddComponent<AudioManager>();

        // ── 3. AltsasuCore ───────────────────────────────────────────────
        var coreGO = new GameObject("AltsasuCore");
        coreGO.AddComponent<AltsasuCore>();

        // ── 4. NavMeshManager ────────────────────────────────────────────
        var nmGO = new GameObject("NavMeshManager");
        nmGO.AddComponent<SistemaNavMesh>();

        // ── 5. WorldManager (zonas + streaming) ──────────────────────────
        var worldGO = new GameObject("WorldManager");
        worldGO.AddComponent<GestorZonasAlsasua>();
        worldGO.AddComponent<SistemaChunks>();

        // ── 6. Audio ambiente ────────────────────────────────────────────
        var audioGO = new GameObject("SonidosAmbiente");
        audioGO.AddComponent<SonidosAmbienteAltsasu>();

        // ── 7. Sistemas de juego ─────────────────────────────────────────
        var sistGO = new GameObject("Sistemas");
        sistGO.AddComponent<SistemaBarricadas>();
        sistGO.AddComponent<SistemaFarolas>();
        sistGO.AddComponent<SistemaManifestacion>();

        // ── 8. Misiones ──────────────────────────────────────────────────
        var misionesGO = new GameObject("SistemaMisiones");
        misionesGO.AddComponent<SistemaMisiones>();

        // ── 9. Árbol streamer (20.000 árboles OSM) ───────────────────────
        var treesGO = new GameObject("AlsasuaTreeStreamer");
        treesGO.AddComponent<AlsasuaTreeStreamer>();

        // ── 10. Luz solar mínima (bootstrap la reemplazará si no hay) ────
        var solGO = new GameObject("Sol_Directional");
        var luz   = solGO.AddComponent<Light>();
        luz.type      = LightType.Directional;
        luz.intensity = 4f;
        luz.color     = new Color(1f, 0.95f, 0.85f);
        luz.shadows   = LightShadows.Soft;
        solGO.transform.rotation = Quaternion.Euler(52f, -30f, 0f);

        // ── Guardar escena ───────────────────────────────────────────────
        if (!Directory.Exists(Path.GetDirectoryName(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", RUTA_ESCENA)))))
            Directory.CreateDirectory(Path.GetDirectoryName(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", RUTA_ESCENA))));

        EditorSceneManager.SaveScene(escena, RUTA_ESCENA);
        AssetDatabase.Refresh();

        // Añadir a Build Settings
        AnadirABuildSettings(RUTA_ESCENA);

        Debug.Log($"[Escena] ✅ Alsasua_Main.unity creada en {RUTA_ESCENA}");
        EditorUtility.DisplayDialog("Escena creada",
            $"✅ Alsasua_Main.unity creada con todos los sistemas.\n\n" +
            "SIGUIENTE:\n" +
            "Tools → Alsasua → ⚡ AUTOMATIZAR TODO\n\n" +
            "Luego dale Play — el SceneBootstrapper generará el terreno,\n" +
            "el jugador y todos los sistemas automáticamente.", "OK");
    }

    static void AnadirABuildSettings(string ruta)
    {
        var scenes = EditorBuildSettings.scenes;
        foreach (var s in scenes)
            if (s.path == ruta) return; // ya está

        var lista = new System.Collections.Generic.List<EditorBuildSettingsScene>(scenes)
        {
            new EditorBuildSettingsScene(ruta, true)
        };
        EditorBuildSettings.scenes = lista.ToArray();
    }

    static void SetField(object target, string field, object value)
    {
        var f = target.GetType().GetField(field,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);
        f?.SetValue(target, value);
    }
}
