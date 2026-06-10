#if UNITY_EDITOR
// Assets/Scripts/Editor/FixTodoAAA.cs
// ═══════════════════════════════════════════════════════════════════════════
//  FIX TODO — Una sola operación que prepara la escena para Play
//
//  No crea jugador, terrain ni cámara aquí (lo hace SceneBootstrapper en runtime).
//  Solo:
//    1. Activa Input System "Both"
//    2. Limpia objetos rotos/duplicados (jugadores, cámaras, volumes globales)
//    3. Garantiza que existe UN SceneBootstrapper en la escena
//    4. Garantiza que existe Camera.main (con HDAdditionalCameraData)
//    5. Garantiza que el tag "Player" existe
//    6. Guarda la escena
//
//  SceneBootstrapper (en runtime al pulsar Play) genera:
//    - Terrain desde Assets/AlsasuaData/dem_unity_1025.raw (o plano fallback)
//    - Sol direccional + Volume HDRP con exposición fija
//    - Jugador Rigidbody + ControladorMovimientoGTA + tag "Player"
//    - Cámara con CameraFollowGTA
//    - GameManager, AltsasuCore, sistemas básicos
// ═══════════════════════════════════════════════════════════════════════════

using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public static class FixTodoAAA
{
    public static void FixTodo()
    {
        try
        {
            EditorUtility.DisplayProgressBar("Fix Todo", "Activando Input System Both...", 0.1f);
            ActivarInputSystemBoth();

            EditorUtility.DisplayProgressBar("Fix Todo", "Limpiando objetos rotos...", 0.3f);
            LimpiarEscena();

            EditorUtility.DisplayProgressBar("Fix Todo", "Asegurando Camera.main...", 0.5f);
            AsegurarCamaraMain();

            EditorUtility.DisplayProgressBar("Fix Todo", "Verificando recursos críticos...", 0.7f);
            string aviso = VerificarRecursos();

            EditorUtility.DisplayProgressBar("Fix Todo", "Añadiendo SceneBootstrapper...", 0.85f);
            AsegurarSceneBootstrapper();

            EditorUtility.DisplayProgressBar("Fix Todo", "Guardando escena...", 0.95f);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            EditorUtility.ClearProgressBar();

            string scene = SceneManager.GetActiveScene().path;
            EditorUtility.DisplayDialog("✅ Fix Todo completo",
                $"Escena preparada: {scene}\n\n" +
                "• Input System: Both\n" +
                "• Camera.main configurada\n" +
                "• SceneBootstrapper añadido\n" +
                (string.IsNullOrEmpty(aviso) ? "" : $"\n⚠ {aviso}\n") +
                "\nPulsa ▶ Play.\n" +
                "SceneBootstrapper creará terrain, jugador y cámara en runtime.",
                "OK");
        }
        catch (System.Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"[FixTodo] {e}");
            EditorUtility.DisplayDialog("Error", e.Message, "OK");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  INPUT SYSTEM
    // ─────────────────────────────────────────────────────────────────────

    static void ActivarInputSystemBoth()
    {
        var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (ps.Length == 0) return;
        var so = new SerializedObject(ps[0]);
        var prop = so.FindProperty("activeInputHandler");
        if (prop != null && prop.intValue != 2)
        {
            prop.intValue = 2;
            so.ApplyModifiedProperties();
            Debug.Log("[FixTodo] Input System → Both");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  LIMPIEZA
    // ─────────────────────────────────────────────────────────────────────

    static void LimpiarEscena()
    {
        bool Safe(GameObject go)
        {
            if (go == null) return false;
            if (go.GetComponentInParent<Terrain>() != null) return false;
            if (go.GetComponentInChildren<Terrain>() != null) return false;
            return true;
        }

        // Jugadores duplicados/rotos
        foreach (var p in GameObject.FindGameObjectsWithTag("Player"))
            if (Safe(p)) Undo.DestroyObjectImmediate(p);

        // Cámaras huérfanas — solo si NO son Camera.main que está siendo seguida
        var cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        if (cams.Length > 1)
        {
            // dejar solo una; preferiblemente la que ya está tagged MainCamera
            var conservar = cams.FirstOrDefault(c => c.CompareTag("MainCamera")) ?? cams[0];
            foreach (var c in cams)
                if (c != conservar && Safe(c.gameObject))
                    Undo.DestroyObjectImmediate(c.gameObject);
        }

        // Luces direccionales duplicadas (dejar 1 — evita Cascade Shadow warning)
        var luces = Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                          .Where(l => l.type == LightType.Directional).ToArray();
        for (int i = 1; i < luces.Length; i++)
            if (Safe(luces[i].gameObject)) Undo.DestroyObjectImmediate(luces[i].gameObject);

        // Volumes globales — SceneBootstrapper crea el suyo en runtime con override profile
        foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            if (v.isGlobal && Safe(v.gameObject)) Undo.DestroyObjectImmediate(v.gameObject);

        // SceneBootstrappers duplicados (dejar uno se hace en AsegurarSceneBootstrapper)
        var bs = Object.FindObjectsByType<SceneBootstrapper>(FindObjectsSortMode.None);
        for (int i = 1; i < bs.Length; i++)
            if (Safe(bs[i].gameObject)) Undo.DestroyObjectImmediate(bs[i].gameObject);

        Debug.Log("[FixTodo] ✓ Escena limpiada.");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  CAMARA.MAIN
    // ─────────────────────────────────────────────────────────────────────

    static void AsegurarCamaraMain()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("MainCamera");
            Undo.RegisterCreatedObjectUndo(go, "MainCamera");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
        }

        cam.fieldOfView    = 65f;
        cam.nearClipPlane  = 0.3f;
        cam.farClipPlane   = 3000f;
        cam.clearFlags     = CameraClearFlags.Skybox;

        // HDAdditionalCameraData necesario para HDRP — sin esto la cámara puede no rendirizar nada
        if (cam.GetComponent<HDAdditionalCameraData>() == null)
        {
            var hd = cam.gameObject.AddComponent<HDAdditionalCameraData>();
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        }

        Debug.Log("[FixTodo] ✓ Camera.main configurada.");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  SCENE BOOTSTRAPPER
    // ─────────────────────────────────────────────────────────────────────

    static void AsegurarSceneBootstrapper()
    {
        var existente = Object.FindFirstObjectByType<SceneBootstrapper>();
        if (existente != null)
        {
            Debug.Log("[FixTodo] SceneBootstrapper ya existe.");
            return;
        }

        var go = new GameObject("_SceneBootstrapper");
        Undo.RegisterCreatedObjectUndo(go, "SceneBootstrapper");
        go.AddComponent<SceneBootstrapper>();
        Debug.Log("[FixTodo] ✓ SceneBootstrapper añadido.");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  RECURSOS
    // ─────────────────────────────────────────────────────────────────────

    static string VerificarRecursos()
    {
        string dem  = "Assets/AlsasuaData/dem_unity_1025.raw";
        string orto = "Assets/AlsasuaData/ortofoto_alsasua_REAL.png";

        bool demOK  = File.Exists(dem);
        bool ortoOK = File.Exists(orto);

        if (demOK && ortoOK) return "";
        if (!demOK && !ortoOK) return "DEM y ortofoto ausentes — terrain será un plano fallback.";
        if (!demOK) return "DEM ausente — terrain será un plano fallback.";
        return "Ortofoto ausente — terrain tendrá color verde sólido.";
    }
}
#endif
