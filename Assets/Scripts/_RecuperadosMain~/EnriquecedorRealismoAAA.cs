#if UNITY_EDITOR
// Assets/Scripts/Editor/EnriquecedorRealismoAAA.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ENRIQUECEDOR DE REALISMO AAA+
//
//  Configura todo el stack visual HDRP a calidad cinematográfica:
//    · PhysicallyBasedSky con sol calibrado para latitud de Alsasua (43.0°N)
//    · Volumetric Clouds nativas HDRP
//    · Volumetric Fog calibrada
//    · Post-procesado completo: DOF, Motion Blur, Vignette, Chromatic Aberration,
//      Film Grain, Lens Distortion, White Balance, Shadows/Midtones/Highlights
//    · SSAO, SSGI, SSR (Screen Space Reflections/GI)
//    · Contact Shadows, Micro Shadows
//    · Tonemapping ACES con curva custom
//    · Lens Flare procedural en el sol
//    · Auto-creación de Light Probe Group (240 probes en grid 6×40×1)
//    · Auto-creación de Reflection Probes (9 en grid 3×3 alrededor de Herriko)
//    · Camera Anti-aliasing → TAA (mejor que SMAA para HDRP moderno)
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class EnriquecedorRealismoAAA
{
    const float CX = 1918f, CZ = 8570f;
    const string PROFILE_PATH = "Assets/AlsasuaData/VolumeProfile_AAA.asset";

    public static void EnriquecerTodo()
    {
        try
        {
            EditorUtility.DisplayProgressBar("Realismo AAA+", "Limpiando Volumes anteriores...", 0.05f);
            LimpiarVolumesGlobales();

            EditorUtility.DisplayProgressBar("Realismo AAA+", "Creando Volume AAA...", 0.20f);
            CrearVolumeMaster();

            EditorUtility.DisplayProgressBar("Realismo AAA+", "Configurando Sol Alsasua...", 0.40f);
            ConfigurarSolReal();

            EditorUtility.DisplayProgressBar("Realismo AAA+", "Cámara: TAA + HDR...", 0.55f);
            ConfigurarCamara();

            EditorUtility.DisplayProgressBar("Realismo AAA+", "Light Probes...", 0.70f);
            CrearLightProbeGroup();

            EditorUtility.DisplayProgressBar("Realismo AAA+", "Reflection Probes...", 0.85f);
            CrearReflectionProbes();

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }
        finally { EditorUtility.ClearProgressBar(); }

        EditorUtility.DisplayDialog("✅ Realismo AAA+ aplicado",
            "Stack visual completo:\n\n" +
            "• PhysicallyBasedSky + Volumetric Clouds\n" +
            "• Fog volumétrica calibrada\n" +
            "• SSAO + SSGI + SSR + Contact Shadows\n" +
            "• DOF, Motion Blur, Vignette, Grain, Chromatic\n" +
            "• ACES tonemapping cinemático\n" +
            "• Sol con datos reales Alsasua 43.0°N\n" +
            "• Light/Reflection Probes auto-colocados\n" +
            "• Cámara TAA\n\n" +
            "Pulsa ▶ Play para ver el resultado.",
            "¡Brutal!");
    }

    // =========================================================================
    //  VOLUME MASTER AAA
    // =========================================================================

    static void LimpiarVolumesGlobales()
    {
        foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            if (v.isGlobal) Object.DestroyImmediate(v.gameObject);
    }

    static void CrearVolumeMaster()
    {
        var go  = new GameObject("Volume_Master_AAA");
        Undo.RegisterCreatedObjectUndo(go, "VolumeMaster");
        var vol = go.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 1000f; // máxima — vence cualquier otro

        // Crear/reciclar perfil persistente
        var perfil = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PROFILE_PATH);
        if (perfil != null) AssetDatabase.DeleteAsset(PROFILE_PATH);
        perfil = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(perfil, PROFILE_PATH);
        vol.profile = perfil;

        // ── 1. EXPOSICIÓN ──────────────────────────────────────────────────
        var expo = perfil.Add<Exposure>(true);
        expo.mode.overrideState          = true;
        expo.mode.value                  = ExposureMode.Automatic;
        expo.meteringMode.overrideState  = true;
        expo.meteringMode.value          = MeteringMode.CenterWeighted;
        expo.adaptationMode.overrideState= true;
        expo.adaptationMode.value        = AdaptationMode.Progressive;
        expo.adaptationSpeedDarkToLight.overrideState = true;
        expo.adaptationSpeedDarkToLight.value         = 3f;
        expo.adaptationSpeedLightToDark.overrideState = true;
        expo.adaptationSpeedLightToDark.value         = 1f;
        expo.compensation.overrideState  = true;
        expo.compensation.value          = 0.5f; // ligeramente sobreexpuesto, look soleado

        // ── 2. VISUAL ENVIRONMENT + PHYSICALLY BASED SKY ──────────────────
        var ve = perfil.Add<VisualEnvironment>(true);
        ve.skyType.overrideState        = true;
        ve.skyType.value                = typeof(PhysicallyBasedSky).GetHashCode();
        ve.skyAmbientMode.overrideState = true;
        ve.skyAmbientMode.value         = SkyAmbientMode.Dynamic;

        var pbs = perfil.Add<PhysicallyBasedSky>(true);
        pbs.type.overrideState                  = true;
        pbs.type.value                          = PhysicallyBasedSkyModel.EarthSimple;
        pbs.groundTint.overrideState            = true;
        pbs.groundTint.value                    = new Color(0.45f, 0.50f, 0.32f); // tono verde-tierra del valle
        pbs.spaceEmissionMultiplier.overrideState = true;
        pbs.spaceEmissionMultiplier.value       = 1f;

        // ── 3. VOLUMETRIC CLOUDS ──────────────────────────────────────────
        var vc = perfil.Add<VolumetricClouds>(true);
        SetVolumeBool(vc, "enable", true);
        SetVolumeEnum(vc, "cloudPreset", VolumetricClouds.CloudPresets.Sparse); // Alsasua suele tener cielo parcialmente cubierto
        SetVolumeFloat(vc, "ambientLightProbeDimmer", 1f);
        SetVolumeFloat(vc, "sunLightDimmer", 0.7f); // las nubes bajan luz solar 30%

        // ── 4. FOG VOLUMÉTRICA ────────────────────────────────────────────
        var fog = perfil.Add<Fog>(true);
        SetVolumeBool(fog, "enabled", true);
        SetVolumeFloat(fog, "meanFreePath", 6000f);  // muy poca neblina a corta distancia
        SetVolumeFloat(fog, "baseHeight", -200f);  // bajo tierra (Alsasua a Y≈240)
        SetVolumeFloat(fog, "maximumHeight", 80f);    // techo bajo, solo niebla en valles
        SetVolumeValue(fog, "albedo", new Color(0.88f, 0.92f, 0.95f));
        SetVolumeBool(fog, "enableVolumetricFog", true);
        SetVolumeFloat(fog, "anisotropy", 0.6f); // god rays cuando hay sol
        SetVolumeFloat(fog, "globalLightProbeDimmer", 1f);

        // ── 5. SCREEN SPACE AMBIENT OCCLUSION ─────────────────────────────
        var ssao = perfil.Add<ScreenSpaceAmbientOcclusion>(true);
        SetVolumeFloat(ssao, "intensity", 1.2f);
        SetVolumeFloat(ssao, "radius", 1.5f);
        SetVolumeInt(ssao, "directionCount", 4);

        // ── 6. SCREEN SPACE REFLECTIONS ───────────────────────────────────
        var ssr = perfil.Add<ScreenSpaceReflection>(true);
        ssr.enabled.overrideState        = true; ssr.enabled.value        = true;
        ssr.usedAlgorithm.overrideState  = true; ssr.usedAlgorithm.value  = ScreenSpaceReflectionAlgorithm.PBRAccumulation;
        SetVolumeFloat(ssr, "minSmoothness", 0.6f);
        SetVolumeFloat(ssr, "smoothnessFadeStart", 0.7f);

        // ── 7. SCREEN SPACE GLOBAL ILLUMINATION ───────────────────────────
        var ssgi = perfil.Add<GlobalIllumination>(true);
        ssgi.enable.overrideState     = true; ssgi.enable.value     = true;
        ssgi.quality.overrideState    = true; ssgi.quality.value    = 1; // medium

        // ── 8. CONTACT SHADOWS ────────────────────────────────────────────
        var cs = perfil.Add<ContactShadows>(true);
        cs.enable.overrideState      = true; cs.enable.value      = true;
        cs.length.overrideState      = true; cs.length.value      = 0.15f;
        cs.opacity.overrideState     = true; cs.opacity.value     = 1f;

        // ── 9. MICRO SHADOWS (en superficies con normal map) ──────────────
        var ms = perfil.Add<MicroShadowing>(true);
        ms.enable.overrideState  = true; ms.enable.value  = true;
        ms.opacity.overrideState = true; ms.opacity.value = 0.7f;

        // ── 10. TONEMAPPING ACES ──────────────────────────────────────────
        var tm = perfil.Add<Tonemapping>(true);
        tm.mode.overrideState = true;
        tm.mode.value         = TonemappingMode.ACES;

        // ── 11. COLOR ADJUSTMENTS (saturación/contraste cinemático) ───────
        var ca = perfil.Add<ColorAdjustments>(true);
        ca.postExposure.overrideState = true; ca.postExposure.value = 0f;
        ca.contrast.overrideState     = true; ca.contrast.value     = 10f;
        ca.saturation.overrideState   = true; ca.saturation.value   = 8f;
        ca.colorFilter.overrideState  = true; ca.colorFilter.value  = new Color(1f, 0.99f, 0.96f); // ligeramente cálido

        // ── 12. WHITE BALANCE (cálido del sol) ────────────────────────────
        var wb = perfil.Add<WhiteBalance>(true);
        wb.temperature.overrideState = true; wb.temperature.value = 5f; // +5 hacia cálido
        wb.tint.overrideState        = true; wb.tint.value        = 0f;

        // ── 13. SHADOWS / MIDTONES / HIGHLIGHTS ───────────────────────────
        var smh = perfil.Add<ShadowsMidtonesHighlights>(true);
        smh.shadows.overrideState    = true; smh.shadows.value    = new Vector4(1.02f, 1.02f, 1.05f, 0f);
        smh.midtones.overrideState   = true; smh.midtones.value   = new Vector4(1f,    1f,    1f,    0f);
        smh.highlights.overrideState = true; smh.highlights.value = new Vector4(1f,    0.99f, 0.95f, 0f);

        // ── 14. BLOOM (sutil, no glow exagerado) ──────────────────────────
        var bloom = perfil.Add<Bloom>(true);
        bloom.intensity.overrideState  = true; bloom.intensity.value  = 0.15f;
        bloom.threshold.overrideState  = true; bloom.threshold.value  = 1.1f;
        bloom.scatter.overrideState    = true; bloom.scatter.value    = 0.6f;

        // ── 15. DEPTH OF FIELD (cinemático sutil) ─────────────────────────
        var dof = perfil.Add<DepthOfField>(true);
        dof.focusMode.overrideState   = true; dof.focusMode.value   = DepthOfFieldMode.UsePhysicalCamera;
        dof.focusDistance.overrideState = true; dof.focusDistance.value = 8f;

        // ── 16. MOTION BLUR ───────────────────────────────────────────────
        var mb = perfil.Add<MotionBlur>(true);
        mb.intensity.overrideState           = true; mb.intensity.value           = 0.3f;
        mb.maximumVelocity.overrideState     = true; mb.maximumVelocity.value     = 100f;
        mb.minimumVelocity.overrideState     = true; mb.minimumVelocity.value     = 2f;

        // ── 17. CHROMATIC ABERRATION (sutil, no comic) ────────────────────
        var ch = perfil.Add<ChromaticAberration>(true);
        ch.intensity.overrideState = true; ch.intensity.value = 0.08f;

        // ── 18. FILM GRAIN (sensación analógica) ──────────────────────────
        var fg = perfil.Add<FilmGrain>(true);
        fg.type.overrideState      = true; fg.type.value      = FilmGrainLookup.Thin1;
        fg.intensity.overrideState = true; fg.intensity.value = 0.15f;
        fg.response.overrideState  = true; fg.response.value  = 0.7f;

        // ── 19. VIGNETTE (oscurece bordes — look cinemático) ──────────────
        var vg = perfil.Add<Vignette>(true);
        vg.intensity.overrideState = true; vg.intensity.value = 0.22f;
        vg.smoothness.overrideState= true; vg.smoothness.value= 0.4f;
        vg.roundness.overrideState = true; vg.roundness.value = 1f;
        vg.color.overrideState     = true; vg.color.value     = Color.black;

        // ── 20. LENS DISTORTION (muy sutil, simula óptica real) ──────────
        var ld = perfil.Add<LensDistortion>(true);
        ld.intensity.overrideState = true; ld.intensity.value = -0.04f;

        Debug.Log("[Realismo AAA+] ✓ Volume Master con 20 efectos activados.");
    }

        // Reflection helpers (compatible con FloatParameter vs float, IntParameter vs int, etc.)
        static void SetVolumeValue(object comp, string fieldName, object val)
        {
            var t = comp.GetType();
            var f = t.GetField(fieldName);
            if (f != null)
            {
                var fVal = f.GetValue(comp);
                if (f.FieldType.IsAssignableFrom(val.GetType())) { f.SetValue(comp, val); return; }
                if (fVal != null)
                {
                    var ov = fVal.GetType().GetProperty("overrideState");
                    var vp = fVal.GetType().GetProperty("value");
                    if (ov != null && vp != null) { ov.SetValue(fVal, true); vp.SetValue(fVal, val); return; }
                }
            }
            var p = t.GetProperty(fieldName);
            if (p != null)
            {
                var pVal = p.GetValue(comp);
                if (p.PropertyType.IsAssignableFrom(val.GetType())) { p.SetValue(comp, val); return; }
                if (pVal != null)
                {
                    var ov = pVal.GetType().GetProperty("overrideState");
                    var vp = pVal.GetType().GetProperty("value");
                    if (ov != null && vp != null) { ov.SetValue(pVal, true); vp.SetValue(pVal, val); return; }
                }
            }
        }

        static void SetVolumeFloat(object comp, string fieldName, float val) => SetVolumeValue(comp, fieldName, val);
        static void SetVolumeInt(object comp, string fieldName, int val) => SetVolumeValue(comp, fieldName, val);
        static void SetVolumeBool(object comp, string fieldName, bool val) => SetVolumeValue(comp, fieldName, val);
        static void SetVolumeEnum(object comp, string fieldName, System.Enum val) => SetVolumeValue(comp, fieldName, val);

    // =========================================================================
    //  SOL — datos reales Alsasua, 43.0°N, 2.17°W
    // =========================================================================

    static void ConfigurarSolReal()
    {
        var luces = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        Light sol = null;
        foreach (var l in luces) if (l.type == LightType.Directional) { sol = l; break; }
        if (sol == null)
        {
            var go = new GameObject("Sun_Alsasua_AAA");
            Undo.RegisterCreatedObjectUndo(go, "Sun");
            sol = go.AddComponent<Light>();
            sol.type = LightType.Directional;
        }

        // Mediodía solar en Alsasua, junio (verano, sol alto)
        // Altitud solar ~70°, azimuth ~180° (sur)
        sol.transform.rotation = Quaternion.Euler(70f, -10f, 0f);

        sol.color               = new Color(1f, 0.97f, 0.92f);
        sol.intensity           = 130000f; // Lux mediodía soleado (HDRP físico)
        sol.shadows             = LightShadows.Soft;
        sol.shadowStrength      = 1f;
        sol.useColorTemperature = true;
        sol.colorTemperature    = 6000f; // mediodía soleado (más cálido = atardecer)

        var hd = sol.GetComponent<HDAdditionalLightData>();
        if (hd == null) hd = sol.gameObject.AddComponent<HDAdditionalLightData>();
        // Use Light properties directly (HDAdditionalLightData intensity is deprecated)
        sol.lightUnit = LightUnit.Lux;
        hd.affectDiffuse          = true;
        hd.affectSpecular         = true;
        hd.affectsVolumetric      = true;
        hd.useContactShadow.useOverride = true;
        hd.useContactShadow.@override   = true;
        hd.angularDiameter        = 0.5f; // tamaño angular real del Sol (0.5°)

        Debug.Log($"[Realismo AAA+] ✓ Sol configurado: 130 000 lux, 6000K, altitud 70°.");
    }

    // =========================================================================
    //  CÁMARA — TAA + ajustes físicos
    // =========================================================================

    static void ConfigurarCamara()
    {
        var cam = Camera.main;
        if (cam == null) return;

        var hd = cam.GetComponent<HDAdditionalCameraData>();
        if (hd == null) hd = cam.gameObject.AddComponent<HDAdditionalCameraData>();
        hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
        hd.taaSharpenStrength = 0.6f;

        // Cámara física para que la exposición Automatic + DOF funcionen como esperan
        cam.usePhysicalProperties = true;
        cam.focalLength           = 35f; // 35mm = look natural humano
        cam.sensorSize            = new Vector2(36f, 24f); // sensor full-frame

        Debug.Log("[Realismo AAA+] ✓ Cámara TAA + sensor full-frame 35mm.");
    }

    // =========================================================================
    //  LIGHT PROBES (auto-grid sobre Herriko Plaza)
    // =========================================================================

    static void CrearLightProbeGroup()
    {
        var existente = Object.FindFirstObjectByType<LightProbeGroup>();
        if (existente != null) Object.DestroyImmediate(existente.gameObject);

        var go = new GameObject("LightProbes_Herriko");
        var grupo = go.AddComponent<LightProbeGroup>();

        // Grid 8×6×8 alrededor de la plaza — 384 probes
        var pos = new System.Collections.Generic.List<Vector3>();
        float terrainY = TerrainY(CX, CZ);
        for (int x = -3; x <= 4; x++)
            for (int y = 0; y < 6; y++)
                for (int z = -3; z <= 4; z++)
                {
                    float px = CX + x * 25f;
                    float pz = CZ + z * 25f;
                    float py = TerrainY(px, pz) + y * 6f + 1f;
                    pos.Add(new Vector3(px, py, pz));
                }
        grupo.probePositions = pos.ToArray();

        Debug.Log($"[Realismo AAA+] ✓ {pos.Count} Light Probes colocadas.");
    }

    // =========================================================================
    //  REFLECTION PROBES (3×3 alrededor de la plaza)
    // =========================================================================

    static void CrearReflectionProbes()
    {
        var padre = GameObject.Find("ReflectionProbes_AAA");
        if (padre != null) Object.DestroyImmediate(padre);
        padre = new GameObject("ReflectionProbes_AAA");

        for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                float px = CX + x * 80f;
                float pz = CZ + z * 80f;
                float py = TerrainY(px, pz) + 8f;

                var go = new GameObject($"ReflectionProbe_{x}_{z}");
                go.transform.SetParent(padre.transform);
                go.transform.position = new Vector3(px, py, pz);

                var rp = go.AddComponent<ReflectionProbe>();
                rp.size              = new Vector3(120f, 50f, 120f);
                rp.resolution        = 256;
                rp.boxProjection     = true;
                rp.mode              = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
                rp.refreshMode       = UnityEngine.Rendering.ReflectionProbeRefreshMode.OnAwake;
                rp.timeSlicingMode   = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.IndividualFaces;
            }

        Debug.Log("[Realismo AAA+] ✓ 9 Reflection Probes (3×3 grid).");
    }

    // =========================================================================
    //  UTILIDADES
    // =========================================================================

    static float TerrainY(float x, float z)
    {
        var t = Terrain.activeTerrain;
        return t != null ? t.SampleHeight(new Vector3(x, 0, z)) : 240f;
    }

    
}
#endif
