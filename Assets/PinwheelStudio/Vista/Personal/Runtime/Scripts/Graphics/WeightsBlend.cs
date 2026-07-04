#if VISTA
using UnityEngine;

namespace Pinwheel.Vista.Graphics
{
    /// <summary>
    /// Separates overlapping weight inputs into one output texture per layer slot.
    /// </summary>
    /// <remarks>
    /// This helper is used by <c>WeightBlendNode</c> to turn a list of input weight textures into a
    /// parallel list of output textures where each target represents the contribution of one layer
    /// after accounting for later layers in the blend order.
    /// </remarks>
    public static class WeightsBlend
    {
        private static readonly string SHADER_NAME = "Hidden/Vista/Graph/WeightBlend";
        private static readonly int BACKGROUND = Shader.PropertyToID("_Background");
        private static readonly int FOREGROUND = Shader.PropertyToID("_Foreground");
        private static readonly int FOREGROUND_MASK = Shader.PropertyToID("_ForegroundMask");
        private static readonly int TARGET_VALUE = Shader.PropertyToID("_TargetValue");
        private static readonly int PASS = 0;

        /// <summary>
        /// Blends a stack of input textures into per-layer output targets.
        /// </summary>
        /// <param name="targetRts">
        /// Output render textures to receive the separated layer weights. Null entries are skipped.
        /// </param>
        /// <param name="inputTextures">
        /// Input weight textures ordered from first layer to last layer.
        /// </param>
        /// <param name="inputMasks">
        /// Optional masks applied per input texture. When omitted, each input uses a white mask.
        /// </param>
        /// <remarks>
        /// The implementation iterates from each target layer through the remaining inputs, using a
        /// temporary accumulation texture and the weight-blend shader's target-value test to isolate
        /// each layer's final contribution.
        /// </remarks>
        public static void Blend<T>(RenderTexture[] targetRts, T[] inputTextures, T[] inputMasks = null) where T : Texture
        {
            RenderTexture firstNotNullTexture = null;
            for (int i = 0; i < targetRts.Length; ++i)
            {
                if (targetRts[i] != null)
                {
                    firstNotNullTexture = targetRts[i];
                    break;
                }
            }

            RenderTexture tempRt = new RenderTexture(firstNotNullTexture);
            Material m_material = new Material(ShaderUtilities.Find(SHADER_NAME));
            for (int i = 0; i < targetRts.Length; ++i)
            {
                RenderTexture targetRt = targetRts[i];
                if (targetRt == null)
                    continue;
                GraphicsUtils.ClearWithZeros(tempRt);
                for (int j = i; j < inputTextures.Length; ++j)
                {
                    m_material.SetTexture(BACKGROUND, tempRt);
                    m_material.SetTexture(FOREGROUND, inputTextures[j]);
                    if (inputMasks != null)
                    {
                        m_material.SetTexture(FOREGROUND_MASK, inputMasks[j]);
                    }
                    else
                    {
                        m_material.SetTexture(FOREGROUND_MASK, Texture2D.whiteTexture);
                    }
                    m_material.SetFloat(TARGET_VALUE, (i == j) ? 1f : 0f);
                    Drawing.DrawQuad(targetRt, m_material, PASS);
                    Drawing.Blit(targetRt, tempRt);
                }
            }

            tempRt.Release();
            Object.DestroyImmediate(tempRt);
            Object.DestroyImmediate(m_material);
        }
    }
}
#endif


