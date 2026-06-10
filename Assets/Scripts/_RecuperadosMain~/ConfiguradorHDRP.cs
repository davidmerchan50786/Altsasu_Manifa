// Assets/Scripts/Editor/ConfiguradorHDRP.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONFIGURADOR HDRP AAA — ALSASUA / ALTSASUA
//
//  Post-process físicamente correcto para:
//    Latitud 42.9°N (Navarra, País Vasco)
//    Clima oceánico (Cfb) — nublado, húmedo, niebla frecuente
//    Luz de tarde de otoño (escena típica del pueblo)
//
//  Menú: Altsasu GTA → MAESTRO → Solo gráficos HDRP
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using System.IO;

#if UNITY_HDRP
using UnityEngine.Rendering.HighDefinition;
#endif

public static class ConfiguradorHDRP
{
    // =========================================================================
    //  API PÚBLICA — llamado desde AltsasuMaestro
    // =========================================================================

    public static void AplicarConfiguracionAAA()
    {
        ConfigurarSolDirecional();
        ConfigurarSkyFisico();
        ConfigurarPostProcess();
        ConfigurarSombras();
        ConfigurarNieblaNorteEspaña();
        ConfigurarLuz();
        AssetDatabase.SaveAssets();
        Debug.Log("[HDRP] ✅ Configuración AAA aplicada — atmósfera vasca/navarra de tarde otoñal.");
    }

    // MENÚ-LEGACY: [MenuItem("Altsasu GTA/MAESTRO/Configurar Post-process HDRP", false, 26)]
    static void MenuHDRP() => AplicarConfiguracionAAA();

    // =========================================================================
    //  SOL DIRECCIONAL — ángulo real para latitud 42.9°N, tarde otoño
    // =========================================================================

    static void ConfigurarSolDirecional()
    {
        Light sol = null;
        var solGO = GameObject.Find("Sun") ?? GameObject.Find("Directional Light");
        if (solGO != null) sol = solGO.GetComponent<Light>();

        if (sol == null)
        {
            solGO = new GameObject("Sun_Alsasua");
            sol   = solGO.AddComponent<Light>();
        }

        Undo.RecordObject(sol, "Sol HDRP");
        Undo.RecordObject(solGO.transform, "Sol transform");

        sol.type      = LightType.Directional;
        sol.shadows   = LightShadows.Soft;
        sol.shadowStrength    = 0.85f;
        sol.shadowNearPlane   = 0.1f;
        sol.renderMode        = LightRenderMode.ForcePixel;

        // Tarde otoñal en Navarra (16:00h, sol bajo del oeste-suroeste)
        // Azimuth: ~240° (OSO), Elevation: ~20° sobre horizonte
        solGO.transform.rotation = Quaternion.Euler(22f, -145f, 0f);

        // Temperatura de color: 4800K (tarde otoñal, algo cálido)
        sol.color     = new Color(1.00f, 0.92f, 0.78f);
        sol.intensity = 1.15f;

#if UNITY_HDRP
        var hdLight = solGO.GetComponent<HDAdditionalLightData>();
        if (hdLight == null) hdLight = solGO.AddComponent<HDAdditionalLightData>();
        hdLight.intensity            = 85000f; // lux de sol de tarde
        hdLight.angularDiameter      = 0.53f;  // diámetro solar real
        hdLight.useColorTemperature  = true;
        hdLight.colorTemperature     = 4800f;
        hdLight.volumetricDimmer     = 1f;
        hdLight.shadowNormalBias     = 0.4f;
        hdLight.shadowSlopeBias      = 0.5f;
#endif

        EditorUtility.SetDirty(solGO);
        Debug.Log("[HDRP] ✓ Sol configurado: tarde otoñal, 42.9°N, 4800K.");
    }

    // =========================================================================
    //  CIELO FÍSICO — nubes voluminosas del norte de España
    // =========================================================================

    static void ConfigurarSkyFisico()
    {
        EnsureVolumeProfile(out var profile);

        // Configurar niebla ambiental base (se puede ver el terreno pero con atmósfera)
        RenderSettings.ambientMode        = AmbientMode.Skybox;
        RenderSettings.ambientIntensity   = 1.1f;
        RenderSettings.ambientSkyColor    = new Color(0.52f, 0.62f, 0.78f); // azul cielo nublado
        RenderSettings.ambientEquatorColor= new Color(0.60f, 0.58f, 0.52f); // horizonte beige-gris
        RenderSettings.ambientGroundColor = new Color(0.18f, 0.16f, 0.13f); // tierra oscura

#if UNITY_HDRP
        if (profile.TryGet<HDRISky>(out var hdriSky))
        {
            // Si tiene HDRI sky, mantenerlo
        }
        else if (profile.TryGet<PhysicallyBasedSky>(out var pbSky))
        {
            pbSky.active = true;
            // Parámetros ajustados para clima oceánico del norte de España
        }
        else
        {
            // Crear Gradient Sky como fallback limpio
            var gradSky = profile.Add<GradientSky>();
            gradSky.top.value    = new Color(0.28f, 0.42f, 0.65f); // azul profundo
            gradSky.middle.value = new Color(0.55f, 0.68f, 0.82f); // azul medio
            gradSky.bottom.value = new Color(0.72f, 0.74f, 0.76f); // horizonte gris-azul
            gradSky.gradientDiffusion.value = 0.7f;
            gradSky.active = true;
        }

        // Volumen de niebla local sobre el valle
        if (!profile.TryGet<VolumetricFog>(out var fog))
            fog = profile.Add<VolumetricFog>();
        fog.active                    = true;
        fog.meanFreePath.value        = 600f;    // niebla baja del valle
        fog.meanFreePath.overrideState= true;
        fog.albedo.value              = new Color(0.90f, 0.92f, 0.95f);
        fog.albedo.overrideState      = true;
        fog.globalLightProbeDimmer.value = 0.6f;
        fog.globalLightProbeDimmer.overrideState = true;
        fog.maxFogDistance.value      = 3000f;
        fog.maxFogDistance.overrideState = true;
#endif

        // Skybox procedural de fallback (Built-in / URP)
        var proceduralShader = Shader.Find("Skybox/Procedural");
        if (proceduralShader != null && RenderSettings.skybox == null)
        {
            var skyMat = new Material(proceduralShader);
            skyMat.SetFloat("_SunSize", 0.035f);
            skyMat.SetColor("_SkyTint", new Color(0.50f, 0.65f, 0.85f));
            skyMat.SetColor("_GroundColor", new Color(0.42f, 0.40f, 0.36f));
            skyMat.SetFloat("_Exposure", 1.05f);
            skyMat.SetFloat("_AtmosphereThickness", 1.1f);
            RenderSettings.skybox = skyMat;
        }
    }

    // =========================================================================
    //  POST-PROCESS HDRP — calidad AAA
    // =========================================================================

    static void ConfigurarPostProcess()
    {
        EnsureVolumeProfile(out var profile);

#if UNITY_HDRP
        // ── Bloom (brillo suave realista, no exagerado) ───────────────────
        if (!profile.TryGet<Bloom>(out var bloom)) bloom = profile.Add<Bloom>();
        bloom.active           = true;
        bloom.intensity.value  = 0.18f;      // sutil
        bloom.intensity.overrideState = true;
        bloom.threshold.value  = 0.85f;
        bloom.threshold.overrideState = true;
        bloom.scatter.value    = 0.6f;
        bloom.scatter.overrideState = true;

        // ── SSAO (oclusión ambiental en esquinas y juntas) ────────────────
        if (!profile.TryGet<ScreenSpaceAmbientOcclusion>(out var ssao)) ssao = profile.Add<ScreenSpaceAmbientOcclusion>();
        ssao.active = true;
        ssao.intensity.value    = 1.2f;
        ssao.intensity.overrideState = true;
        ssao.radius.value       = 0.5f;
        ssao.radius.overrideState = true;

        // ── Color Grading — paleta otoñal del País Vasco ──────────────────
        if (!profile.TryGet<ColorAdjustments>(out var colorAdj)) colorAdj = profile.Add<ColorAdjustments>();
        colorAdj.active = true;
        colorAdj.postExposure.value        = 0.1f;    // ligeramente más brillante
        colorAdj.postExposure.overrideState= true;
        colorAdj.contrast.value            = 8f;      // contraste suave
        colorAdj.contrast.overrideState    = true;
        colorAdj.colorFilter.value         = new Color(1.00f, 0.97f, 0.93f); // cálido otoñal
        colorAdj.colorFilter.overrideState = true;
        colorAdj.saturation.value          = -8f;     // desaturar ligeramente (clima nublado)
        colorAdj.saturation.overrideState  = true;
        colorAdj.hueShift.value            = 2f;      // ligeramente verde-dorado
        colorAdj.hueShift.overrideState    = true;

        // ── Lift/Gamma/Gain (look cinematográfico) ────────────────────────
        if (!profile.TryGet<LiftGammaGain>(out var lgg)) lgg = profile.Add<LiftGammaGain>();
        lgg.active = true;
        lgg.lift.value  = new Vector4(0.98f, 0.98f, 1.02f, 0f);  // sombras ligeramente azules
        lgg.lift.overrideState = true;
        lgg.gamma.value = new Vector4(1.00f, 0.99f, 0.98f, 0.02f); // medios neutros
        lgg.gamma.overrideState = true;
        lgg.gain.value  = new Vector4(1.02f, 1.00f, 0.97f, 0f);  // altas luces cálidas
        lgg.gain.overrideState = true;

        // ── Tonemapping ACES (estándar cinematográfico) ───────────────────
        if (!profile.TryGet<Tonemapping>(out var tone)) tone = profile.Add<Tonemapping>();
        tone.active = true;
        tone.mode.value = TonemappingMode.ACES;
        tone.mode.overrideState = true;

        // ── Vignette (sutil, enfoca el centro) ───────────────────────────
        if (!profile.TryGet<Vignette>(out var vig)) vig = profile.Add<Vignette>();
        vig.active = true;
        vig.intensity.value   = 0.22f;
        vig.intensity.overrideState = true;
        vig.smoothness.value  = 0.5f;
        vig.smoothness.overrideState = true;

        // ── Depth of Field (enfoque en jugador, fondo desenfocado) ────────
        if (!profile.TryGet<DepthOfField>(out var dof)) dof = profile.Add<DepthOfField>();
        dof.active = true;
        dof.focusMode.value = DepthOfFieldMode.UsePhysicalCamera;
        dof.focusMode.overrideState = true;

        // ── Chromatic Aberration mínima (lente real) ──────────────────────
        if (!profile.TryGet<ChromaticAberration>(out var ca)) ca = profile.Add<ChromaticAberration>();
        ca.active = true;
        ca.intensity.value = 0.05f;
        ca.intensity.overrideState = true;

        // ── Film Grain (sutilísimo, look fotográfico) ─────────────────────
        if (!profile.TryGet<FilmGrain>(out var grain)) grain = profile.Add<FilmGrain>();
        grain.active = true;
        grain.intensity.value = 0.12f;
        grain.intensity.overrideState = true;
        grain.response.value  = 0.7f;
        grain.response.overrideState = true;

        // ── Panini Projection (reduce distorsión gran angular) ────────────
        if (!profile.TryGet<PaniniProjection>(out var panini)) panini = profile.Add<PaniniProjection>();
        panini.active = true;
        panini.distance.value = 0.1f;
        panini.distance.overrideState = true;
#endif

        // Marcar dirty el perfil
        EditorUtility.SetDirty(profile);
        Debug.Log("[HDRP] ✓ Post-process AAA: Bloom, SSAO, Color Grading otoñal, ACES Tonemap, DoF.");
    }

    // =========================================================================
    //  SOMBRAS — calidad AAA
    // =========================================================================

    static void ConfigurarSombras()
    {
#if UNITY_HDRP
        var hdrpSettings = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                         as HDRenderPipelineAsset;
        if (hdrpSettings != null)
        {
            // No podemos modificar el asset directamente sin SerializedObject en Editor,
            // pero sí los Quality Settings
        }
#endif
        QualitySettings.shadowDistance   = 300f;
        QualitySettings.shadowCascades   = 4;
        QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
        QualitySettings.shadowProjection = ShadowProjection.CloseFit;
        QualitySettings.lodBias          = 2.5f;
        QualitySettings.maximumLODLevel  = 0;
        QualitySettings.antiAliasing     = 0; // TAA se maneja en HDRP camera
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
        QualitySettings.globalTextureMipmapLimit   = 0; // texturas a máxima resolución

        Debug.Log("[HDRP] ✓ Sombras: 300m, 4 cascades, Very High.");
    }

    // =========================================================================
    //  NIEBLA — valle de Sakana / clima oceánico
    // =========================================================================

    static void ConfigurarNieblaNorteEspaña()
    {
        // La niebla HDRP volumétrica se configura en el VolumeProfile.
        // Aquí configuramos la niebla de Unity como fallback y para builds.
        RenderSettings.fog        = true;
        RenderSettings.fogMode    = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.0012f; // niebla muy sutil — visible en montañas lejanas
        RenderSettings.fogColor   = new Color(0.72f, 0.74f, 0.78f); // gris azulado típico de Navarra
        // Empieza a notarse a ~400m, montañas funden en bruma a 1500m+
        Debug.Log("[HDRP] ✓ Niebla: clima oceánico Cfb, 0.0012 density, gris-azulado.");
    }

    // =========================================================================
    //  LUZ AMBIENTAL — GI realista
    // =========================================================================

    static void ConfigurarLuz()
    {
        // Luz ambiental trilineal (cielo/horizonte/suelo)
        RenderSettings.ambientMode        = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor    = new Color(0.52f, 0.62f, 0.78f); // cielo azul-gris
        RenderSettings.ambientEquatorColor= new Color(0.65f, 0.62f, 0.55f); // horizonte beige
        RenderSettings.ambientGroundColor = new Color(0.20f, 0.18f, 0.15f); // tierra marrón
        RenderSettings.ambientIntensity   = 1.1f;

        // Reflexión ambiental
        RenderSettings.reflectionIntensity = 0.6f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.defaultReflectionResolution = 256;

        Debug.Log("[HDRP] ✓ Iluminación ambiental: GI trilineal, reflexión 0.6, cielo azul-gris.");
    }

    // =========================================================================
    //  UTILIDADES
    // =========================================================================

    static void EnsureVolumeProfile(out VolumeProfile profile)
    {
        var vol = Object.FindFirstObjectByType<Volume>();
        if (vol == null)
        {
            var go = new GameObject("PostProcess_Global_Alsasua");
            Undo.RegisterCreatedObjectUndo(go, "PP Volume");
            vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
        }

        if (vol.profile == null)
        {
            const string PROFILE_PATH = "Assets/Settings/PP_Alsasua_AAA.asset";
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PROFILE_PATH);
            if (existing != null)
            {
                vol.sharedProfile = existing;
            }
            else
            {
                var newProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                newProfile.name  = "PP_Alsasua_AAA";
                if (!AssetDatabase.IsValidFolder("Assets/Settings"))
                    AssetDatabase.CreateFolder("Assets", "Settings");
                AssetDatabase.CreateAsset(newProfile, PROFILE_PATH);
                vol.sharedProfile = newProfile;
            }
        }

        profile = vol.profile ?? vol.sharedProfile;
        Undo.RecordObject(vol, "Config volume");
        EditorUtility.SetDirty(vol.gameObject);
    }
}

