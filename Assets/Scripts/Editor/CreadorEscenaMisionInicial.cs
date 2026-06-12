// Assets/Scripts/Editor/CreadorEscenaMisionInicial.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CREADOR ESCENA MISIÓN INICIAL — Tools/Alsasua/Escena
//
//  Construye Mision_Inicial.unity: una escena ligera que arranca el juego
//  directamente en la misión M00 (Esnatu, Altsasu — tutorial).
//
//  La escena solo contiene la jerarquía mínima en editor; SceneBootstrapper
//  (-200) construye en Play todo lo que falte (terreno DEM, jugador, cámara,
//  sol, y los ~40 sistemas via Add<T>), y AltsasuCore completa con EnsureOn.
//
//  Jerarquía creada:
//    SceneBootstrapper   — constructor runtime del mundo
//    GameManager         — GameManagerAltsasua + AudioManager + ApoyoPopular
//                          + ConfiguradorAssetsAAA (assets asignados)
//    AltsasuCore         — coordinador v4 (EnsureOn del resto)
//    Misiones            — SistemaMisiones (saltarIntro=false) + SistemaTutorial
//    MarcadorPortal      — gizmo del punto de inicio de M00 (solo referencia)
// ═══════════════════════════════════════════════════════════════════════════

using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CreadorEscenaMisionInicial
{
    const string RUTA_ESCENA = "Assets/#Scenes/Mision_Inicial.unity";

    [MenuItem("Tools/Alsasua/Escena/🎬 Crear Escena Misión Inicial", priority = 11)]
    public static void Crear()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var escena = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── 1. SceneBootstrapper (ejecución -200, lo primero) ─────────────
        var bootstrapGO = new GameObject("SceneBootstrapper");
        var sb = bootstrapGO.AddComponent<SceneBootstrapper>();
        Set(sb, "centroX",                   GeoDataAlsasua.OX);
        Set(sb, "centroZ",                   GeoDataAlsasua.OZ);
        Set(sb, "generarTerrenoDesdeDEM",    true);
        Set(sb, "usarPlanoCuadradoFallback", true);
        Set(sb, "crearJugadorSiNoHay",       true);

        // ── 2. GameManager (core sin dependencias) ────────────────────────
        var gmGO = new GameObject("GameManager");
        gmGO.AddComponent<GameManagerAltsasua>();
        gmGO.AddComponent<AudioManager>();
        gmGO.AddComponent<SistemaApoyoPopular>();
        var cfgAAA = gmGO.AddComponent<ConfiguradorAssetsAAA>();
        AsignarAssetsAAAEditor.AsignarDesdeCreador(cfgAAA);

        // ── 3. AltsasuCore (coordinador -100, EnsureOn del resto) ─────────
        var coreGO = new GameObject("AltsasuCore");
        var core   = coreGO.AddComponent<AltsasuCore>();
        Set(core, "centroX", GeoDataAlsasua.OX);
        Set(core, "centroZ", GeoDataAlsasua.OZ);

        // ── 4. Misiones — arranca en M00 (tutorial) ───────────────────────
        var misGO = new GameObject("Misiones");
        var mis   = misGO.AddComponent<SistemaMisiones>();
        Set(mis, "saltarIntro", false);
        misGO.AddComponent<SistemaTutorial>();

        // ── 5. Marcador visual del portal de inicio (referencia en editor) ─
        var marcador = new GameObject("MarcadorPortal_M00");
        var portal = GeoDataAlsasua.HerrikoPlaza + new Vector3(140f, 0f, -55f);
        marcador.transform.position = portal;
        // (la Y real la resuelve la misión en runtime con AlturaTerreno)

        // ── Guardar ───────────────────────────────────────────────────────
        Directory.CreateDirectory(Path.GetDirectoryName(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", RUTA_ESCENA))));
        EditorSceneManager.SaveScene(escena, RUTA_ESCENA);
        AnadirBuildSettings(RUTA_ESCENA);
        AssetDatabase.Refresh();

        Debug.Log("[Escena] ✅ Mision_Inicial.unity creada.");
        EditorUtility.DisplayDialog("✅ Escena Misión Inicial",
            "Mision_Inicial.unity lista:\n\n" +
            "• SceneBootstrapper construye el mundo al dar Play\n" +
            "• M00 'Esnatu, Altsasu' arranca a los 3s:\n" +
            "   1. Aprende los controles (WASD/ratón)\n" +
            "   2. Camina hasta Herriko Plaza\n" +
            "   3. Reúnete con el grupo\n" +
            "• Al completarla encadena con M01 RobarCoche → M12\n\n" +
            "Dale Play para probarla.", "OK");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    static void Set(object target, string campo, object valor)
    {
        if (target == null || valor == null) return;
        var f = target.GetType().GetField(campo,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        f?.SetValue(target, valor);
    }

    static void AnadirBuildSettings(string ruta)
    {
        var scenes = EditorBuildSettings.scenes;
        foreach (var s in scenes)
            if (s.path == ruta) return;
        var lista = new System.Collections.Generic.List<EditorBuildSettingsScene>(scenes);
        lista.Add(new EditorBuildSettingsScene(ruta, true));
        EditorBuildSettings.scenes = lista.ToArray();
    }
}
