// Assets/Scripts/Runtime/QualitySettingsAAA.cs
// ═══════════════════════════════════════════════════════════════════════════
//  QUALITY SETTINGS AAA — calidad adaptada al tier de GPU
//
//  Detecta el tier de GPU en BeforeSceneLoad y aplica el perfil correcto.
//  No toca configuraciones que HDRP ignora (shadowDistance, shadowCascades…).
//
//  TIERS:
//    High   ≥ 8 GB VRAM  (RTX 3080+, RX 6800+)  → máxima calidad
//    Medium  4–7 GB VRAM  (RTX 2070, RX 5700…)   → balance calidad/rendimiento
//    Low    < 4 GB VRAM  o GPU integrada          → rendimiento primero
//
//  PARÁMETROS QUE SÍ FUNCIONAN EN HDRP:
//    · lodBias              — distancia de transición de LOD (afecta city cells + vegetación)
//    · maximumLODLevel      — 0 = siempre LOD0 (más detalle)
//    · anisotropicFiltering — nitidez en texturas vistas en ángulo
//    · globalTextureMipmapLimit — resolución de texturas en memoria
//    · asyncUpload*         — presupuesto de carga asíncrona de assets
//    · skinWeights          — huesos de skinning para NPCs
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

public static class QualitySettingsAAA
{
    public enum TierGPU { Low, Medium, High }

    /// <summary>Tier detectado al arrancar. Consultar después de BeforeSceneLoad.</summary>
    public static TierGPU Tier { get; private set; } = TierGPU.Medium;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Aplicar()
    {
        Tier = DetectarTier();
        switch (Tier)
        {
            case TierGPU.High:   AplicarHigh();   break;
            case TierGPU.Medium: AplicarMedium(); break;
            default:             AplicarLow();    break;
        }
        Debug.Log($"[QualityAAA] GPU tier={Tier} ({SystemInfo.graphicsDeviceName}, " +
                  $"{SystemInfo.graphicsMemorySize}MB VRAM) → perfil aplicado.");
    }

    // ── Detección ─────────────────────────────────────────────────────────
    static TierGPU DetectarTier()
    {
        int vram = SystemInfo.graphicsMemorySize;
        string gpu = SystemInfo.graphicsDeviceName.ToLowerInvariant();

        // GPU integrada (Intel, Apple GPU, ARM Mali/Adreno)
        bool esIntegrada = gpu.Contains("intel") || gpu.Contains("iris") ||
                           gpu.Contains("uhd")   || gpu.Contains("hd graphics") ||
                           gpu.Contains("mali")  || gpu.Contains("adreno") ||
                           gpu.Contains("apple") || vram <= 512;

        if (esIntegrada || vram < 4096) return TierGPU.Low;
        if (vram < 8192)                return TierGPU.Medium;
        return TierGPU.High;
    }

    // ── Perfiles ───────────────────────────────────────────────────────────
    static void AplicarHigh()
    {
        QualitySettings.lodBias                  = 2.5f;
        QualitySettings.maximumLODLevel          = 0;
        QualitySettings.anisotropicFiltering     = AnisotropicFiltering.ForceEnable;
        QualitySettings.globalTextureMipmapLimit = 0;       // máxima resolución
        QualitySettings.skinWeights              = SkinWeights.FourBones;
        QualitySettings.asyncUploadTimeSlice     = 4;
        QualitySettings.asyncUploadBufferSize    = 64;
    }

    static void AplicarMedium()
    {
        QualitySettings.lodBias                  = 2.0f;
        QualitySettings.maximumLODLevel          = 0;
        QualitySettings.anisotropicFiltering     = AnisotropicFiltering.Enable;
        QualitySettings.globalTextureMipmapLimit = 0;
        QualitySettings.skinWeights              = SkinWeights.FourBones;
        QualitySettings.asyncUploadTimeSlice     = 3;
        QualitySettings.asyncUploadBufferSize    = 32;
    }

    static void AplicarLow()
    {
        QualitySettings.lodBias                  = 1.5f;
        QualitySettings.maximumLODLevel          = 0;
        QualitySettings.anisotropicFiltering     = AnisotropicFiltering.Enable;
        QualitySettings.globalTextureMipmapLimit = 1;       // texturas a mitad de resolución
        QualitySettings.skinWeights              = SkinWeights.TwoBones;
        QualitySettings.asyncUploadTimeSlice     = 2;
        QualitySettings.asyncUploadBufferSize    = 16;
    }
}
