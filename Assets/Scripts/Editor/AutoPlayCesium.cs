#if UNITY_EDITOR
// Assets/Scripts/Editor/AutoPlayCesium.cs
// ═══════════════════════════════════════════════════════════════════════════
//  AUTO-PLAY BOOTSTRAPPER — Se ejecuta antes de entrar en Play Mode.
//  Garantiza que la escena tiene TODO lo necesario para jugar sin tocar
//  ningún menú: Cesium, SceneBootstrapper, AltsasuCore, GameManager,
//  luz, volumen HDRP, jugador y cámara.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class AutoPlayCesium
{
    static AutoPlayCesium()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChange;
    }

    static void OnPlayModeChange(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode) return;

        bool cambios = false;

        // ── 1. SceneBootstrapper ────────────────────────────────────────────
        // Debe existir en escena — él se ocupa de terrain, jugador, cámara, etc.
        if (Object.FindFirstObjectByType<SceneBootstrapper>() == null)
        {
            var go = new GameObject("SceneBootstrapper");
            go.AddComponent<SceneBootstrapper>();
            Debug.Log("[AutoPlay] ✓ SceneBootstrapper añadido a la escena.");
            cambios = true;
        }

        // ── 2. GameManager ──────────────────────────────────────────────────
        if (Object.FindFirstObjectByType<GameManagerAltsasua>() == null)
        {
            var go = new GameObject("GameManager");
            go.AddComponent<GameManagerAltsasua>();
            Debug.Log("[AutoPlay] ✓ GameManager añadido.");
            cambios = true;
        }

        // ── 3. AltsasuCore ──────────────────────────────────────────────────
        if (Object.FindFirstObjectByType<AltsasuCore>() == null)
        {
            var gm = Object.FindFirstObjectByType<GameManagerAltsasua>();
            var go = gm != null ? gm.gameObject : new GameObject("AltsasuCore");
            go.AddComponent<AltsasuCore>();
            Debug.Log("[AutoPlay] ✓ AltsasuCore añadido.");
            cambios = true;
        }

        // ── 4. Cesium ───────────────────────────────────────────────────────
        bool cesiumDisponible =
            System.Type.GetType("CesiumForUnity.CesiumGeoreference, CesiumForUnity") != null;

        if (cesiumDisponible)
        {
            if (GameObject.Find("CesiumGeoreference") == null)
            {
                Debug.Log("[AutoPlay] Configurando Cesium automáticamente...");
                ConfiguradorCesiumAlsasua.Configurar();
                cambios = true;
            }
            else
            {
                QuitarCameraController();
            }
        }
        else
        {
            Debug.LogWarning("[AutoPlay] Cesium no compilado — jugando sin tiles 3D.");
        }

        // ── 5. Luz solar mínima ─────────────────────────────────────────────
        bool hayLuz = false;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) { hayLuz = true; break; }

        if (!hayLuz)
        {
            var go  = new GameObject("Sun_AutoPlay");
            var luz = go.AddComponent<Light>();
            luz.type      = LightType.Directional;
            luz.intensity = 80000f; // HDRP usa lux — 80k = sol exterior
            luz.color     = new Color(1f, 0.97f, 0.90f);
            luz.shadows   = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(52f, -25f, 0f);
            Debug.Log("[AutoPlay] ✓ Luz solar creada.");
            cambios = true;
        }

        // ── 6. Volumen HDRP mínimo (exposición fija, sin pantalla negra) ────
        bool hayVolumen = false;
        foreach (var v in Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None))
            if (v.isGlobal) { hayVolumen = true; break; }

        if (!hayVolumen)
        {
            CrearVolumenMinimo();
            cambios = true;
        }

        // ── 7. Cámara con HDAdditionalCameraData ────────────────────────────
        var cam = Camera.main;
        if (cam == null)
        {
            var camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<AudioListener>();
            cam.fieldOfView   = 65f;
            cam.nearClipPlane = 0.15f;
            cam.farClipPlane  = 3000f;
            Debug.Log("[AutoPlay] ✓ Cámara creada.");
            cambios = true;
        }
        AsegurarHDRPCamera(cam.gameObject);

        // ── Guardar escena si hubo cambios ───────────────────────────────────
        if (cambios)
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log("[AutoPlay] ✅ Escena lista para Play.");
    }

    // ── Cesium CameraController: QUITARLO de las cámaras ──────────────────
    // Es un controlador de cámara libre (vuelo WASD) que pelea con la cámara
    // en tercera persona de ControladorJugador (causaba que la cámara acabase
    // dentro de la malla de los tiles).
    static void QuitarCameraController()
    {
        var tipo = System.Type.GetType("CesiumForUnity.CesiumCameraController, CesiumForUnity");
        if (tipo == null) return;
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            var ctrl = cam.GetComponent(tipo);
            if (ctrl != null)
            {
                Object.DestroyImmediate(ctrl);
                Debug.Log("[AutoPlay] CesiumCameraController eliminado de la cámara (conflicto con cámara TP).");
            }
        }
    }

    // ── HDRP: HDAdditionalCameraData en la cámara ─────────────────────────
    static void AsegurarHDRPCamera(GameObject camGO)
    {
        var tipo = System.Type.GetType(
            "UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData, " +
            "Unity.RenderPipelines.HighDefinition.Runtime");
        if (tipo == null) return;
        if (camGO.GetComponent(tipo) == null)
            camGO.AddComponent(tipo);
    }

    // ── Volumen HDRP mínimo: exposición fija, cielo degradado, sin niebla ─
    static void CrearVolumenMinimo()
    {
        var go  = new GameObject("Volume_AutoPlay");
        var vol = go.AddComponent<UnityEngine.Rendering.Volume>();
        vol.isGlobal = true;
        vol.priority = 50f;
        var perfil = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
        vol.profile = perfil;

        try
        {
            // Exposición automática con límites (EV 11-15). Fija a EV12 con un
            // sol de 80.000 lux quedaba ~3 pasos sobreexpuesta (imagen lavada).
            var expo = perfil.Add<UnityEngine.Rendering.HighDefinition.Exposure>(true);
            expo.mode.Override(UnityEngine.Rendering.HighDefinition.ExposureMode.Automatic);
            expo.limitMin.Override(11f);
            expo.limitMax.Override(15f);

            // Tonemapping neutro
            var tm = perfil.Add<UnityEngine.Rendering.HighDefinition.Tonemapping>(true);
            tm.mode.Override(UnityEngine.Rendering.HighDefinition.TonemappingMode.Neutral);

            // Cielo físico HDRP (PhysicallyBased = 1 en Unity 2022+)
            // GradientSky como fallback visual simple si PhysicallyBased no está disponible
            var sky = perfil.Add<UnityEngine.Rendering.HighDefinition.GradientSky>(true);
            sky.top.Override(new Color(0.30f, 0.55f, 1.00f));
            sky.middle.Override(new Color(0.60f, 0.80f, 1.00f));
            sky.bottom.Override(new Color(0.45f, 0.62f, 0.28f));

            var ve = perfil.Add<UnityEngine.Rendering.HighDefinition.VisualEnvironment>(true);
            ve.skyType.Override(189733825); // GradientSky unique ID
            ve.skyAmbientMode.Override(UnityEngine.Rendering.HighDefinition.SkyAmbientMode.Dynamic);

            // Niebla muy suave
            var fog = perfil.Add<UnityEngine.Rendering.HighDefinition.Fog>(true);
            fog.enabled.Override(true);
            fog.meanFreePath.Override(3000f);
            fog.baseHeight.Override(0f);
            fog.maximumHeight.Override(600f);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[AutoPlay] Volumen HDRP parcialmente configurado: {e.Message}");
        }

        Debug.Log("[AutoPlay] ✓ Volumen HDRP mínimo creado.");
    }
}
#endif
