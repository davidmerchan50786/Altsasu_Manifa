#if VISTA
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Pinwheel.Vista.Graphics
{
    /// <summary>
    /// Packs per-layer weight textures into terrain alphamaps and merges duplicate terrain layers.
    /// </summary>
    /// <remarks>
    /// Unity Terrain and Polaris both emit one weight texture per requested terrain layer. This helper
    /// reduces that set into the final alphamap layout expected by terrain systems by first collapsing
    /// duplicate <see cref="TerrainLayer"/> entries, then packing up to four distinct layers into each
    /// ARGB render texture.
    /// </remarks>
    public class AlphaMapsCombiner
    {
        private static readonly string SHADER_NAME = "Hidden/Vista/AlphaMapsCombine";
        private static readonly int LAYER_WEIGHT = Shader.PropertyToID("_LayerWeight");
        private static readonly int CHANNEL_MASK = Shader.PropertyToID("_ChannelMask");
   
        private static readonly int PASS_COMBINE_MERGE = 0;

        /// <summary>
        /// Creates an alphamap combiner.
        /// </summary>
        public AlphaMapsCombiner()
        {
        }

        /// <summary>
        /// Merges duplicate terrain layers and packs their weight maps into ARGB alphamaps.
        /// </summary>
        /// <param name="srcLayers">
        /// Terrain layers associated with <paramref name="srcWeights"/>, matched by index.
        /// Duplicate entries are merged into a single distinct layer in the output.
        /// </param>
        /// <param name="srcWeights">
        /// Weight textures for the source layers, matched by index with <paramref name="srcLayers"/>.
        /// Each texture is written into the RGBA channel assigned to its distinct-layer slot.
        /// </param>
        /// <param name="resolution">
        /// Resolution, in pixels, of the output alphamaps to allocate.
        /// </param>
        /// <param name="distinctLayers">
        /// Receives the distinct terrain-layer list after duplicate entries have been collapsed.
        /// The order of this list determines the alphamap and channel assignment.
        /// </param>
        /// <param name="alphaMaps">
        /// Receives the generated alphamap render textures. Each texture stores up to four packed
        /// layer weights in RGBA order.
        /// </param>
        /// <remarks>
        /// The method allocates new <see cref="RenderTexture"/> instances for the output alphamaps and
        /// leaves ownership to the caller. Layer packing is determined by the index of each distinct
        /// layer: <c>index / 4</c> selects the target alphamap and <c>index % 4</c> selects the output
        /// channel.
        /// </remarks>
        public void CombineAndMerge(List<TerrainLayer> srcLayers, List<RenderTexture> srcWeights, int resolution, out List<TerrainLayer> distinctLayers, out List<RenderTexture> alphaMaps)
        {
            distinctLayers = srcLayers.Distinct().ToList();

            int[] layerIndices = new int[srcLayers.Count];
            for (int i = 0; i < srcLayers.Count; ++i)
            {
                TerrainLayer layer = srcLayers[i];
                int index = distinctLayers.IndexOf(layer);
                layerIndices[i] = index;
            }

            int alphaMapCount = (distinctLayers.Count + 3) / 4;
            alphaMaps = new List<RenderTexture>();
            for (int i = 0; i < alphaMapCount; ++i)
            {
                RenderTexture rt = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                rt.enableRandomWrite = true;
                GraphicsUtils.ClearWithZeros(rt);

                alphaMaps.Add(rt);                
            }

            Material mat = new Material(ShaderUtilities.Find(SHADER_NAME));
            for (int i = 0; i < srcWeights.Count; ++i)
            {
                int lIndex = layerIndices[i];
                int tIndex = lIndex / 4;
                int cIndex = lIndex % 4;
                RenderTexture targetRt = alphaMaps[tIndex];
                RenderTexture layerWeight = srcWeights[i];
                                
                Vector4 channelMask = Vector4.zero;
                channelMask[cIndex] = 1;

                mat.SetTexture(LAYER_WEIGHT, layerWeight);
                mat.SetVector(CHANNEL_MASK, channelMask);
                Drawing.DrawQuad(targetRt, mat, PASS_COMBINE_MERGE);                
            }
            Object.DestroyImmediate(mat);
        }
    }
}
#endif


