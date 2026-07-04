#if VISTA
using Pinwheel.Vista.Graphics;
using UnityEngine;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Provides helper functions used by Local Procedural Biome mask generation.
    /// </summary>
    /// <remarks>
    /// LPB stands for <see cref="LocalProceduralBiome"/>. The current utility surface focuses on combining a generated base
    /// biome mask with user-authored per-pixel adjustment data through a hidden shader pass.
    /// </remarks>
    public static class LPBUtilities
    {
        /// <summary>
        /// The hidden shader used to merge a base biome mask with an adjustment texture.
        /// </summary>
        public static readonly string BIOME_MASK_COMBINE_SHADER = "Hidden/Vista/BiomeMaskCombine";
        /// <summary>
        /// Shader property ID for the source biome mask texture.
        /// </summary>
        public static readonly int BASE_BIOME_MASK = Shader.PropertyToID("_BaseBiomeMask");
        /// <summary>
        /// Shader property ID for the user-authored biome mask adjustment texture.
        /// </summary>
        public static readonly int BIOME_MASK_ADJUSTMENTS = Shader.PropertyToID("_BiomeMaskAdjustments");

        /// <summary>
        /// Shader property ID for source blend state.
        /// </summary>
        public static readonly int SRC_BLEND = Shader.PropertyToID("_SrcBlend");
        /// <summary>
        /// Shader property ID for destination blend state.
        /// </summary>
        public static readonly int DST_BLEND = Shader.PropertyToID("_DstBlend");
        /// <summary>
        /// Shader property ID for blend operation selection.
        /// </summary>
        public static readonly int BLEND_OP = Shader.PropertyToID("_BlendOp");
        /// <summary>
        /// Shader keyword used by the underlying material when blitting directly into the destination render target.
        /// </summary>
        public static readonly string KW_BLIT_TO_DEST = "BLIT_TO_DEST";
        /// <summary>
        /// The shader pass index used for biome mask combination.
        /// </summary>
        public static readonly int PASS = 0;

        /// <summary>
        /// Applies a biome-mask adjustment texture onto an existing base biome mask in place.
        /// </summary>
        /// <param name="baseMask">
        /// The destination biome mask to modify. The texture must already exist and be writable because the final result is
        /// drawn back into this render target.
        /// </param>
        /// <param name="adjustmentTex">
        /// The adjustment texture to blend with the base mask. This is typically created from
        /// <see cref="LocalProceduralBiome.biomeMaskAdjustments"/>.
        /// </param>
        /// <remarks>
        /// The method first copies <paramref name="baseMask"/> into a temporary render texture, then runs the hidden
        /// combine shader with both the original mask copy and the adjustment texture bound as inputs. This avoids reading
        /// from and writing to the same render target in one pass. Temporary resources created by the method are released
        /// before it returns.
        /// </remarks>
        public static void CombineBiomeMask(RenderTexture baseMask, Texture adjustmentTex)
        {
            RenderTexture baseMaskCopy = new RenderTexture(baseMask);
            Drawing.Blit(baseMask, baseMaskCopy);

            Material mat = new Material(ShaderUtilities.Find(BIOME_MASK_COMBINE_SHADER));
            mat.SetTexture(BASE_BIOME_MASK, baseMaskCopy);
            mat.SetTexture(BIOME_MASK_ADJUSTMENTS, adjustmentTex);
            Drawing.DrawQuad(baseMask, mat, PASS);

            baseMaskCopy.Release();
            Object.DestroyImmediate(baseMaskCopy);
            Object.DestroyImmediate(mat);
        }
    }
}
#endif


