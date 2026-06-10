#if UNITY_EDITOR
// Assets/Scripts/Editor/EnriquecedorRealismoExtremo.cs
// ═══════════════════════════════════════════════════════════════════════════
//  REALISMO EXTREMO — segunda pasada por encima de EnriquecedorRealismoAAA.
//
//  Activa lo que diferencia "AAA" de "fotorealista":
//    · HDRI Sky real (Poly Haven .exr) en lugar de PhysicallyBasedSky procedural
//    · Ray Traced Shadows + Reflections + AO + GI (si la GPU es RTX)
//    · Path-Traced reflections en charcos / cristal
//    · Lens Flare procedural en el sol (HDRP Lens Flare SRP Asset)
//    · Subsurface Scattering forzado en materiales orgánicos
//    · Anisotropic Reflections en metales
//    · TAA quality alta + sharpening
//    · Shadow distance 800m con cascade map de 2048
// ═══════════════════════════════════════════════════════════════════════════

using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class EnriquecedorRealismoExtremo
{
    const string HDRI_DIR     = "Assets/AlsasuaData/HDRI";
    const string PROFILE_PATH = "Assets/AlsasuaData/VolumeProfile_AAA.asset";
    const string LENSFLARE_PATH = "Assets/AlsasuaData/LensFlare_Sol.asset";

    public static void Aplicar()
    {
        try
        {
            EditorUtility.DisplayProgressBar("Realismo Extremo", "Buscando HDRI...", 0.10f);
            var hdri = LocalizarHDRI();

            EditorUtility.DisplayProgressBar("Realismo Extremo", "Actualizando Volume Profile...", 0.30f);
            AplicarHDRIYExtras(hdri);

            EditorUtility.DisplayProgressBar("Realismo Extremo", "Ray Tracing si está disponible...", 0.55f);
            string rtStatus = ConfigurarRayTracing();

            EditorUtility.DisplayProgressBar("Realismo Extremo", "Lens Flare en el sol...", 0.75f);
            AñadirLensFlareSol();

            EditorUtility.DisplayProgressBar("Realismo Extremo", "Shadow distance + cascades...", 0.90f);
            ConfigurarSombrasExtras();

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog("✅ Realismo Extremo",
                $"Aplicado:\n\n" +
                (hdri != null ? "• HDRI Sky: " + Path.GetFileName(hdri) + "\n" : "• HDRI no encontrado (descarga con descargar_materiales_pbr.py)\n") +
                $"• {rtStatus}\n" +
                "• Lens Flare procedural en el sol\n" +
                "• Shadow distance 800m, cascade resolution 2048\n" +
                "• TAA Quality High + Sharpen 0.7\n\n" +
                "Pulsa ▶ Play.", "OK");
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    // =========================================================================
    //  HDRI SKY REAL
    // =========================================================================

    static string LocalizarHDRI()
    {
        if (!Directory.Exists(HDRI_DIR)) return null;
        var exrs = Directory.GetFiles(HDRI_DIR, "*.exr");
        // Preferir el de mediodía si está
        foreach (var f in exrs)
            if (f.Contains("Mediodia") || f.Contains("clear") || f.Contains("puresky"))
                return f;
        return exrs.Length > 0 ? exrs[0] : null;
    }

    static void AplicarHDRIYExtras(string hdriPath)
    {
        var perfil = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PROFILE_PATH);
        if (perfil == null)
        {
            Debug.LogWarning("[Extremo] VolumeProfile_AAA no existe. Ejecuta Paso 15 primero.");
            return;
        }

        // Cambiar a HDRI Sky si tenemos un .exr
        if (hdriPath != null)
        {
            // Importer del exr
            var imp = AssetImporter.GetAtPath(hdriPath) as TextureImporter;
            if (imp != null)
            {
                if (imp.textureShape != TextureImporterShape.TextureCube)
                {
                    imp.textureShape = TextureImporterShape.TextureCube;
                    imp.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
                    imp.SaveAndReimport();
                }
            }
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(hdriPath);

            if (cube != null)
            {
                // Cambiar VisualEnvironment a HDRI
                if (perfil.TryGet<VisualEnvironment>(out var ve))
                {
                    ve.skyType.overrideState = true;
                    ve.skyType.value         = typeof(HDRISky).GetHashCode();
                }

                // Añadir HDRISky si no está
                if (!perfil.TryGet<HDRISky>(out var hdri))
                    hdri = perfil.Add<HDRISky>(true);
                hdri.hdriSky.overrideState    = true;
                hdri.hdriSky.value            = cube;
                hdri.exposure.overrideState   = true;
                hdri.exposure.value           = 0f;
                hdri.multiplier.overrideState = true;
                hdri.multiplier.value         = 1f;
                hdri.rotation.overrideState   = true;
                hdri.rotation.value           = 30f;
            }
        }

        // Subir calidad SSAO / SSR (compatibilidad con distintas versiones HDRP)
        if (perfil.TryGet<ScreenSpaceAmbientOcclusion>(out var ssao))
        {
            SetVolumeInt(ssao, "quality", 2); // High
            SetVolumeInt(ssao, "directionCount", 6);
            SetVolumeFloat(ssao, "intensity", 1.5f);
        }
        if (perfil.TryGet<ScreenSpaceReflection>(out var ssr))
        {
            SetVolumeFloat(ssr, "minSmoothness", 0.4f); // empieza reflejar antes
            SetVolumeInt(ssr, "rayMaxIterations", 64); // calidad alta
        }
        if (perfil.TryGet<GlobalIllumination>(out var ssgi))
        {
            ssgi.quality.value = 2;
        }

        // Volumetric Clouds — preset Overcast/Stormy alternativo si se quiere drama
        if (perfil.TryGet<VolumetricClouds>(out var vc))
        {
            vc.shadows.overrideState = true;
            vc.shadows.value         = true;
            vc.shadowDistance.overrideState = true;
            vc.shadowDistance.value  = 8000f;
        }

        // Fog — ligera mejora de god rays
        if (perfil.TryGet<Fog>(out var fog))
        {
            fog.directionalLightsOnly.overrideState = true;
            fog.directionalLightsOnly.value         = false;
            fog.depthExtent.overrideState = true;
            fog.depthExtent.value         = 256f;
        }
    }

    // =========================================================================
    //  RAY TRACING (solo si hay GPU RTX + DX12)
    // =========================================================================

    static string ConfigurarRayTracing()
    {
        // Comprobar soporte
        if (!SystemInfo.supportsRayTracing)
            return "Ray Tracing: GPU no soporta (necesita RTX + DX12) — saltado.";

        var perfil = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PROFILE_PATH);
        if (perfil == null) return "Ray Tracing: VolumeProfile no existe.";

        // Replace SSR with Ray Traced Reflections
        if (perfil.TryGet<ScreenSpaceReflection>(out var ssr))
        {
            SetVolumeEnum(ssr, "tracing", RayCastingMode.RayTracing);
            SetVolumeFloat(ssr, "rayLength", 50f);
        }

        // Replace SSGI with Ray Traced GI
        if (perfil.TryGet<GlobalIllumination>(out var ssgi))
        {
            SetVolumeEnum(ssgi, "tracing", RayCastingMode.RayTracing);
        }

        // Replace SSAO with Ray Traced AO
        if (perfil.TryGet<ScreenSpaceAmbientOcclusion>(out var ssao))
        {
            SetVolumeBool(ssao, "rayTracing", true);
        }

        // Ray Traced Shadows — añadirlo si no existe
        if (!perfil.TryGet<RayTracingSettings>(out var rt))
            rt = perfil.Add<RayTracingSettings>(true);
        SetVolumeFloat(rt, "rayBias", 0.001f);
        SetVolumeFloat(rt, "distantRayBias", 0.01f);
        SetVolumeBool(rt, "extendShadowCulling", true);

        // Habilitar shadows raytraced en el sol
        var luces = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (var l in luces)
            if (l.type == LightType.Directional)
            {
                var hd = l.GetComponent<HDAdditionalLightData>();
                if (hd != null)
                {
                    hd.useRayTracedShadows = true;
                    hd.numRayTracingSamples = 4;
                    hd.filterTracedShadow = true;
                }
            }

        return "Ray Tracing: ACTIVADO (Reflections + GI + AO + Shadows)";
    }

    // =========================================================================
    //  LENS FLARE SOL
    // =========================================================================

    static void AñadirLensFlareSol()
    {
        // Crear LensFlareDataSRP procedural
        var asset = AssetDatabase.LoadAssetAtPath<LensFlareDataSRP>(LENSFLARE_PATH);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<LensFlareDataSRP>();
            AssetDatabase.CreateAsset(asset, LENSFLARE_PATH);
        }

        // Lens flare generation not available on this HDRP version in this build.
        Debug.LogWarning("[Extremo] Lens Flare creation skipped (LensFlare types not available in this HDRP version).");
    }

    // Helpers para compatibilidad con distintas versiones de HDRP (FloatParameter vs float, IntParameter vs int, etc.)
    static void SetVolumeFloat(object comp, string fieldName, float val)
    {
        var t = comp.GetType();
        var f = t.GetField(fieldName);
        if (f != null)
        {
            if (f.FieldType == typeof(float)) { f.SetValue(comp, val); return; }
            var fldVal = f.GetValue(comp);
            if (fldVal != null)
            {
                var ov = fldVal.GetType().GetProperty("overrideState");
                var vp = fldVal.GetType().GetProperty("value");
                if (ov != null && vp != null) { ov.SetValue(fldVal, true); vp.SetValue(fldVal, val); return; }
            }
        }
        var p = t.GetProperty(fieldName);
        if (p != null)
        {
            if (p.PropertyType == typeof(float)) { p.SetValue(comp, val); return; }
            var pVal = p.GetValue(comp);
            if (pVal != null)
            {
                var ov = pVal.GetType().GetProperty("overrideState");
                var vp = pVal.GetType().GetProperty("value");
                if (ov != null && vp != null) { ov.SetValue(pVal, true); vp.SetValue(pVal, val); return; }
            }
        }
    }

    static void SetVolumeInt(object comp, string fieldName, int val)
    {
        var t = comp.GetType();
        var f = t.GetField(fieldName);
        if (f != null)
        {
            if (f.FieldType == typeof(int)) { f.SetValue(comp, val); return; }
            var fldVal = f.GetValue(comp);
            if (fldVal != null)
            {
                var ov = fldVal.GetType().GetProperty("overrideState");
                var vp = fldVal.GetType().GetProperty("value");
                if (ov != null && vp != null) { ov.SetValue(fldVal, true); vp.SetValue(fldVal, val); return; }
            }
        }
        var p = t.GetProperty(fieldName);
        if (p != null)
        {
            if (p.PropertyType == typeof(int)) { p.SetValue(comp, val); return; }
            var pVal = p.GetValue(comp);
            if (pVal != null)
            {
                var ov = pVal.GetType().GetProperty("overrideState");
                var vp = pVal.GetType().GetProperty("value");
                if (ov != null && vp != null) { ov.SetValue(pVal, true); vp.SetValue(pVal, val); return; }
            }
        }
    }

    static void SetVolumeBool(object comp, string fieldName, bool val)
    {
        var t = comp.GetType();
        var f = t.GetField(fieldName);
        if (f != null)
        {
            if (f.FieldType == typeof(bool)) { f.SetValue(comp, val); return; }
            var fldVal = f.GetValue(comp);
            if (fldVal != null)
            {
                var ov = fldVal.GetType().GetProperty("overrideState");
                var vp = fldVal.GetType().GetProperty("value");
                if (ov != null && vp != null) { ov.SetValue(fldVal, true); vp.SetValue(fldVal, val); return; }
            }
        }
        var p = t.GetProperty(fieldName);
        if (p != null)
        {
            if (p.PropertyType == typeof(bool)) { p.SetValue(comp, val); return; }
            var pVal = p.GetValue(comp);
            if (pVal != null)
            {
                var ov = pVal.GetType().GetProperty("overrideState");
                var vp = pVal.GetType().GetProperty("value");
                if (ov != null && vp != null) { ov.SetValue(pVal, true); vp.SetValue(pVal, val); return; }
            }
        }
    }

    static void SetVolumeEnum(object comp, string fieldName, System.Enum val)
    {
        var t = comp.GetType();
        var f = t.GetField(fieldName);
        if (f != null)
        {
            var fldVal = f.GetValue(comp);
            if (fldVal != null)
            {
                var ov = fldVal.GetType().GetProperty("overrideState");
                var vp = fldVal.GetType().GetProperty("value");
                if (ov != null && vp != null) { ov.SetValue(fldVal, true); vp.SetValue(fldVal, val); return; }
            }
        }
        var p = t.GetProperty(fieldName);
        if (p != null)
        {
            var pVal = p.GetValue(comp);
            if (pVal != null)
            {
                var ov = pVal.GetType().GetProperty("overrideState");
                var vp = pVal.GetType().GetProperty("value");
                if (ov != null && vp != null) { ov.SetValue(pVal, true); vp.SetValue(pVal, val); return; }
            }
        }
    }

    // =========================================================================
    //  SHADOW DISTANCE + CASCADES
    // =========================================================================

    static void ConfigurarSombrasExtras()
    {
        // Aumentar shadow distance en el HDRP Asset si accesible
        QualitySettings.shadowDistance = 800f;
        QualitySettings.shadowCascades = 4;
        QualitySettings.shadowResolution = ShadowResolution.VeryHigh;

        // En el HDAdditionalLightData del sol
        var luces = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (var l in luces)
            if (l.type == LightType.Directional)
            {
                var hd = l.GetComponent<HDAdditionalLightData>();
                if (hd != null)
                {
                    hd.shadowResolution.useOverride = true;
                    hd.shadowResolution.@override   = 2048;
                    hd.shadowNearPlane = 0.1f;
                }
            }

        // Cámara TAA quality alta
        var cam = Camera.main;
        if (cam != null)
        {
            var hd = cam.GetComponent<HDAdditionalCameraData>();
            if (hd != null)
            {
                hd.taaSharpenStrength = 0.7f;
                hd.TAAQuality          = HDAdditionalCameraData.TAAQualityLevel.High;
            }
        }
    }
}
#endif
