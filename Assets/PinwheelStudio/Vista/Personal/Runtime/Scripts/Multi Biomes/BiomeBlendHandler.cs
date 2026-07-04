#if VISTA
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Diagnostics;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Pinwheel.Vista.BigWorld
{
    /// <summary>
    /// Entry point for the multi-biome blending pipeline. Registered as a callback on
    /// <see cref="VistaManager.blendBiomeDataCallback"/> and called by the runtime whenever
    /// multiple biome results need to be composited into a single <see cref="BiomeData"/>.
    /// </summary>
    public static class BiomeBlendHandler
    {
#if UNITY_EDITOR
        [InitializeOnLoadMethod]
#else
        [UnityEngine.RuntimeInitializeOnLoadMethod]
#endif
        private static void OnInitialize()
        {
            VistaManager.blendBiomeDataCallback += Blend;
        }

        /// <summary>
        /// Blends all channels from the given source biomes into a single composite <see cref="BiomeData"/>.
        /// </summary>
        /// <param name="srcBiomes">
        /// Ordered list of source biome instances. This list is parallel to <paramref name="srcDatas"/> and provides
        /// per-biome blend settings and scene transform context for channels that need it.
        /// </param>
        /// <param name="srcDatas">
        /// Ordered list of biome data outputs. Biomes are composited in list order, so later entries
        /// are layered on top of earlier ones according to their blend options.
        /// </param>
        /// <returns>
        /// A new <see cref="BiomeData"/> containing all composited channels. The caller owns the
        /// returned object and is responsible for releasing its GPU resources.
        /// </returns>
        public static BiomeData Blend(List<IBiome> srcBiomes, List<BiomeData> srcDatas)
        {
            BiomeData data = new BiomeData();

            VistaDebugger.OpenScope("Biome Blend", DebugScopeType.BlendPass);

            // Texture channels. One shader load covers all texture blend dispatches.
            BiomeTextureBlend.BlendContext textureBlendContext = BiomeTextureBlend.Begin();
            BiomeTextureBlend.BlendHeightMap(data, srcBiomes, srcDatas, textureBlendContext);
            BiomeTextureBlend.BlendHoleMap(data, srcBiomes, srcDatas, textureBlendContext);
            BiomeTextureBlend.BlendMeshDensityMap(data, srcBiomes, srcDatas, textureBlendContext);
            BiomeTextureBlend.BlendAlbedoMap(data, srcBiomes, srcDatas, textureBlendContext);
            BiomeTextureBlend.BlendMetallicMap(data, srcBiomes, srcDatas, textureBlendContext);
            BiomeTextureBlend.BlendGenericTextures(data, srcBiomes, srcDatas, textureBlendContext);
            BiomeTextureBlend.BlendTextureWeights(data, srcBiomes, srcDatas, textureBlendContext);
            BiomeTextureBlend.BlendDensityMaps(data, srcBiomes, srcDatas, textureBlendContext);

            // Buffer channels. Uses a separate shader and context from the texture pass.
            BiomeBufferBlend.BufferBlendContext bufferBlendContext = BiomeBufferBlend.Begin();
            BiomeBufferBlend.BlendTreeBuffer(data, srcBiomes, srcDatas, textureBlendContext, bufferBlendContext);
            BiomeBufferBlend.BlendDetailInstanceBuffer(data, srcBiomes, srcDatas, textureBlendContext, bufferBlendContext);
            BiomeBufferBlend.BlendObjectBuffer(data, srcBiomes, srcDatas, textureBlendContext, bufferBlendContext);
            BiomeBufferBlend.BlendGenericBuffer(data, srcBiomes, srcDatas, textureBlendContext, bufferBlendContext);
            BiomeBufferBlend.End(bufferBlendContext);
            BiomeTextureBlend.End(textureBlendContext);

            VistaDebugger.CloseScope();
            return data;
        }
    }
}
#endif
