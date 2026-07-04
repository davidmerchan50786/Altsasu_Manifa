#if VISTA
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Executes biome mask graphs against the already generated mask of a Local Procedural Biome.
    /// </summary>
    /// <remarks>
    /// This utility is the post-process step used by <see cref="Core.LocalProceduralBiome"/> after
    /// the biome's combined mask has been rendered. It feeds that texture back into a
    /// <see cref="BiomeMaskGraph"/>, extracts the dedicated biome-mask output, and stores the result
    /// in the supplied <see cref="BiomeDataRequest"/>.
    /// </remarks>
    public static class BiomeMaskGraphUtilities
    {
        /// <summary>
        /// Runs a biome mask graph and writes its biome-mask output into a request payload.
        /// </summary>
        /// <param name="request">
        /// The request whose <see cref="BiomeDataRequest.data"/> receives the generated
        /// <see cref="Core.BiomeData.biomeMaskMap"/> texture.
        /// </param>
        /// <param name="graph">
        /// The biome mask graph to execute. Only the output node named
        /// <see cref="GraphConstants.BIOME_MASK_OUTPUT_NAME"/> is evaluated.
        /// </param>
        /// <param name="worldBounds">
        /// The biome cache bounds that should be exposed to the graph as execution bounds.
        /// </param>
        /// <param name="simSpace">
        /// The coordinate space of the source biome. When this is <see cref="Space.World"/>, the
        /// graph receives the actual world-space minimum coordinates; otherwise the request starts at
        /// local origin.
        /// </param>
        /// <param name="baseBiomeMask">
        /// The combined biome mask produced before post-processing. Its width is used as the graph
        /// resolution, and the texture is bound as the graph input identified by
        /// <see cref="GraphConstants.BIOME_MASK_INPUT_NAME"/>.
        /// </param>
        /// <returns>
        /// An enumerator that executes the graph asynchronously and completes
        /// <paramref name="request"/> when the post-process step finishes or short-circuits.
        /// </returns>
        /// <exception cref="System.ArgumentException">
        /// Thrown when <paramref name="baseBiomeMask"/> does not use a valid graph resolution.
        /// </exception>
        /// <remarks>
        /// If the graph does not contain the dedicated biome-mask output node, the method completes
        /// the request immediately and leaves the request data unchanged.
        /// </remarks>
        public static IEnumerator RequestData(BiomeDataRequest request, BiomeMaskGraph graph, Bounds worldBounds, Space simSpace, RenderTexture baseBiomeMask)
        {
            int baseResolution = baseBiomeMask.width;
            if (baseResolution < 0 || (baseResolution - 0 * 1) % 8 != 0)
            {
                throw new System.ArgumentException("Invalid base resolution, must be 8*x");
            }

            List<OutputNode> genericOutputNodes = graph.GetNodesOfType<OutputNode>();
            OutputNode targetOutputNode = genericOutputNodes.Find(n => GraphConstants.BIOME_MASK_OUTPUT_NAME.Equals(n.outputName));
            if (targetOutputNode == null)
            {
                request.Complete();
                yield break;
            }

            string[] nodeIds = new string[] { targetOutputNode.id };

            TerrainGenerationConfigs configs = new TerrainGenerationConfigs();
            configs.resolution = baseResolution;
            configs.seed = 0;
            configs.terrainHeight = worldBounds.size.y;
            configs.worldBounds = new Rect(simSpace == Space.World ? worldBounds.min.x : 0, simSpace == Space.World ? worldBounds.min.z : 0, worldBounds.size.x, worldBounds.size.z);

            GraphInputContainer inputContainer = new GraphInputContainer();
            inputContainer.AddTexture(GraphConstants.BIOME_MASK_INPUT_NAME, baseBiomeMask);

            ExecutionHandle handle = graph.Execute(nodeIds, configs, inputContainer);
            yield return handle;

            RenderTexture rt = handle.data.RemoveRTFromPool(new SlotRef(targetOutputNode.id, targetOutputNode.mainOutputSlot.slotId));
            if (rt != null)
            {
                request.data.biomeMaskMap = rt;
            }

            handle.Dispose();
            request.Complete();
        }
    }
}
#endif


