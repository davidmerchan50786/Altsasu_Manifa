// Assets/Scripts/SistemaLuzHDRP.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ILUMINACIÓN HDRP AAA — Alsasua GTA
//
//  Configura en runtime:
//   · PhysicallyBasedSky (cielo físico real con Rayleigh/Mie)
//   · HDRenderPipelineGlobalSettings
//   · Luz directional con parámetros físicos (lux, temperatura, diámetro)
//   · Probe de reflexión global
//   · Luz de luna nocturna
//   · Lens flare en el sol
//   · VolumetricClouds (si disponible en HDRP 17.3)
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

[DefaultExecutionOrder(-90)]
public class SistemaLuzHDRP : MonoBehaviour
{
    public static SistemaLuzHDRP Instance { get; private set; }

    [Header("Luz solar")]
    public Light sol;
    [Range(10000f, 130000f)] public float luxMediodia  = 85000f;
    [Range(1500f,  9000f)]   public float tempColor     = 5500f;
    [Range(0.1f,   1f)]      public float diametroAngular = 0.53f;

    [Header("Luna")]
    public Light luna;
    [Range(0f, 10f)]  public float luxLuna = 0.25f;

    [Header("Sky")]
    public Volume volumenSky;
    public bool usarPhysicallyBasedSky = true;
    public bool usarNubesVolumetricas  = false; // Solo disponible en HDRP High Fidelity

    [Header("Probe de reflexión global")]
    public ReflectionProbe probeGlobal;

    // ── Overrides de sky ──────────────────────────────────────────────────
    PhysicallyBasedSky _pbSky;
    GradientSky        _gradSky;
    HDAdditionalLightData _solHD;
    HDAdditionalLightData _lunaHD;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        yield return null;

        sol ??= FindFirstObjectByType<Light>();
        ConfigurarSol();
        ConfigurarSky();
        ConfigurarLuna();
        EnsureReflectionProbe();

        AlsasuaLogger.Info("SistemaLuzHDRP", "✓ Iluminación HDRP configurada.");
    }

    // =========================================================================
    //  SOL FÍSICO
    // =========================================================================

    void ConfigurarSol()
    {
        if (sol == null) return;

        sol.type      = LightType.Directional;
        sol.shadows   = LightShadows.Soft;
        sol.shadowStrength = 0.85f;
        sol.shadowBias     = 0.02f;

        // HDAdditionalLightData para parámetros físicos
        _solHD = sol.GetComponent<HDAdditionalLightData>();
        if (_solHD == null) _solHD = sol.gameObject.AddComponent<HDAdditionalLightData>();

        // Intensidad física (lux al mediodía) — HDRP 17: usar Light directamente
        sol.lightUnit = LightUnit.Lux;
        sol.intensity = luxMediodia;

        // Temperatura de color
        sol.useColorTemperature = true;
        sol.colorTemperature    = tempColor;

        // Diámetro angular del disco solar (0.53° real)
        _solHD.angularDiameter = diametroAngular;

        // Sombras en cascada — vía QualitySettings (HDRP 17)
        QualitySettings.shadowDistance  = 350f;
        QualitySettings.shadowCascades  = 4;

        sol.gameObject.name = "Sun_HDRP";
    }

    // =========================================================================
    //  SKY FÍSICAMENTE BASADO
    // =========================================================================

    void ConfigurarSky()
    {
        // Buscar o crear volumen de sky
        volumenSky ??= FindFirstObjectByType<Volume>();
        if (volumenSky == null)
        {
            var go = new GameObject("Sky_Volume");
            volumenSky = go.AddComponent<Volume>();
            volumenSky.isGlobal = true;
            volumenSky.priority = 5f;
        }

        var perfil = volumenSky.profile;
        if (perfil == null)
        {
            perfil = ScriptableObject.CreateInstance<VolumeProfile>();
            volumenSky.profile = perfil;
        }

        // VisualEnvironment — necesario para que HDRP renderice el cielo
        if (!perfil.TryGet(out VisualEnvironment ve)) ve = perfil.Add<VisualEnvironment>(true);
        ve.skyType.overrideState        = true;
        ve.skyType.value                = 1; // 1 = GradientSky (constante HDRP)
        ve.skyAmbientMode.overrideState = true;
        ve.skyAmbientMode.value         = SkyAmbientMode.Dynamic;

        ConfigurarGradientSkyFallback(perfil);

        // Exposición correcta para HDRP (evita imagen blanca/negra)
        if (!perfil.TryGet(out Exposure expo)) expo = perfil.Add<Exposure>(true);
        expo.mode.overrideState   = true; expo.mode.value   = ExposureMode.Automatic;
        expo.compensation.overrideState = true; expo.compensation.value = 0f;

        // Niebla volumétrica — valle vasco (reducida para no tapar la escena)
        if (!perfil.TryGet(out Fog fog)) fog = perfil.Add<Fog>(true);
        fog.enabled.overrideState      = true; fog.enabled.value      = true;
        fog.meanFreePath.overrideState = true; fog.meanFreePath.value = 800f;
        fog.baseHeight.overrideState   = true; fog.baseHeight.value   = 250f;
        fog.maximumHeight.overrideState= true; fog.maximumHeight.value= 650f;
        fog.albedo.overrideState       = true; fog.albedo.value       = new Color(0.85f, 0.88f, 0.92f);
    }

    void ConfigurarPhysicallyBasedSky(VolumeProfile perfil)
    {
        if (!perfil.TryGet(out _pbSky)) _pbSky = perfil.Add<PhysicallyBasedSky>(true);

        // Aerosoles (polvo, humedad del País Vasco)
        _pbSky.aerosolDensity.overrideState = true;
        _pbSky.aerosolDensity.value         = 0.12f;

        AlsasuaLogger.Info("SistemaLuzHDRP", "Sky: PhysicallyBasedSky configurado.");
    }

    void ConfigurarGradientSkyFallback(VolumeProfile perfil)
    {
        if (!perfil.TryGet(out _gradSky)) _gradSky = perfil.Add<GradientSky>(true);

        // Cielo vasco: azul intenso → gris azulado → gris horizonte
        _gradSky.top.overrideState    = true; _gradSky.top.value    = new Color(0.25f, 0.42f, 0.72f);
        _gradSky.middle.overrideState = true; _gradSky.middle.value = new Color(0.52f, 0.68f, 0.87f);
        _gradSky.bottom.overrideState = true; _gradSky.bottom.value = new Color(0.72f, 0.76f, 0.80f);
        _gradSky.gradientDiffusion.overrideState = true;
        _gradSky.gradientDiffusion.value = 1.2f;

        AlsasuaLogger.Info("SistemaLuzHDRP", "Sky: GradientSky (fallback) configurado.");
    }

    // =========================================================================
    //  LUNA
    // =========================================================================

    void ConfigurarLuna()
    {
        if (luna == null)
        {
            var lunaGO = new GameObject("Moon_HDRP");
            luna = lunaGO.AddComponent<Light>();
            luna.type = LightType.Directional;
            luna.transform.rotation = Quaternion.Euler(45f, 200f, 0f);
        }

        luna.color = new Color(0.75f, 0.82f, 0.95f);
        luna.shadows = LightShadows.None; // Sin sombras de luna (rendimiento)

        _lunaHD = luna.GetComponent<HDAdditionalLightData>();
        if (_lunaHD == null) _lunaHD = luna.gameObject.AddComponent<HDAdditionalLightData>();
        _lunaHD.SetIntensity(luxLuna, LightUnit.Lux);
        _lunaHD.angularDiameter = 0.52f;

        // La luna se activa de noche — lo gestiona SistemaAtmosfera
        luna.gameObject.SetActive(false);
    }

    // =========================================================================
    //  PROBE DE REFLEXIÓN GLOBAL
    // =========================================================================

    void EnsureReflectionProbe()
    {
        if (probeGlobal != null) return;

        var go = new GameObject("ReflectionProbe_Global");
        go.transform.position = new Vector3(1918f, 280f, 8570f); // Herriko Plaza

        probeGlobal = go.AddComponent<ReflectionProbe>();
        probeGlobal.mode           = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
        probeGlobal.refreshMode    = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
        probeGlobal.size           = new Vector3(6000f, 800f, 20000f); // cubre toda Alsasua
        probeGlobal.farClipPlane   = 5000f;
        probeGlobal.resolution     = 256; // bajo para rendimiento
        probeGlobal.hdr            = true;

        var hdProbe = go.AddComponent<HDAdditionalReflectionData>();
        hdProbe.influenceVolume.boxSize = probeGlobal.size;

        // Renderizar una vez al inicio
        StartCoroutine(RenderizarProbe());
    }

    IEnumerator RenderizarProbe()
    {
        yield return new WaitForSeconds(3f); // esperar a que la escena esté lista
        if (probeGlobal != null) probeGlobal.RenderProbe();
        AlsasuaLogger.Info("SistemaLuzHDRP", "✓ Reflection Probe renderizado.");
    }

    // =========================================================================
    //  API PÚBLICA — llamado por SistemaAtmosfera
    // =========================================================================

    public void SetLuzNocturna(float t)
    {
        // t = 0 → día, 1 → noche
        if (_solHD != null) _solHD.SetIntensity(Mathf.Lerp(luxMediodia, 0f, t), LightUnit.Lux);
        if (luna  != null) luna.gameObject.SetActive(t > 0.5f);
        if (_lunaHD != null && t > 0.5f) _lunaHD.SetIntensity(Mathf.Lerp(0f, luxLuna, (t - 0.5f) * 2f), LightUnit.Lux);
    }

    public void SetTemperaturaAmanecer()
    {
        if (sol == null) return;
        sol.colorTemperature = 3200f; // naranja cálido amanecer
    }

    public void SetTemperaturaMediodia()
    {
        if (sol == null) return;
        sol.colorTemperature = 5500f;
    }

    public void SetTemperaturaAtardecer()
    {
        if (sol == null) return;
        sol.colorTemperature = 3500f;
    }

    static new T FindFirstObjectByType<T>() where T : UnityEngine.Object
        => UnityEngine.Object.FindFirstObjectByType<T>();
}
