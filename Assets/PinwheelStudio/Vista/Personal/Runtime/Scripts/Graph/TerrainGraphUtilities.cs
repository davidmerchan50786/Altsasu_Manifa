#if VISTA
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Executes terrain graphs for biome generation and transfers selected outputs into <see cref="BiomeData"/>.
    /// </summary>
    /// <remarks>
    /// This utility is the high-level bridge between the generic graph executor
    /// (<see cref="TerrainGraph"/>) and Vista's biome-data pipeline. It decides which output nodes are
    /// relevant for a request, runs the graph once for those targets, removes the resulting textures
    /// and buffers from the execution pool, and stores them in the caller-provided
    /// <see cref="BiomeDataRequest"/>.
    /// </remarks>
    public static class TerrainGraphUtilities
    {
        /// <summary>
        /// Executes a terrain graph for one biome request and fills the requested output channels into <see cref="BiomeData"/>.
        /// </summary>
        /// <param name="biome">
        /// The biome requesting data. This is used for editor progress reporting and for identifying
        /// the source of the generation request.
        /// </param>
        /// <param name="request">
        /// The asynchronous request whose <see cref="BiomeDataRequest.data"/> instance will receive the
        /// generated outputs.
        /// </param>
        /// <param name="graph">
        /// The terrain graph to execute.
        /// </param>
        /// <param name="biomeWorldBounds">
        /// The biome bounds that define the height scale and horizontal coverage of the graph request.
        /// </param>
        /// <param name="simSpace">
        /// The coordinate space in which the graph should evaluate its bounds. In local space the
        /// request starts at the origin; in world space it uses the biome's world minimum XZ.
        /// </param>
        /// <param name="baseResolution">
        /// Base graph resolution for this request. It must already be compatible with the graph's
        /// compute-shader constraints.
        /// </param>
        /// <param name="seed">
        /// Base random seed used to initialize the execution arguments.
        /// </param>
        /// <param name="inputContainer">
        /// Optional external graph inputs, such as Local Procedural Biome masks, scene height, custom
        /// textures, or uploaded point buffers.
        /// </param>
        /// <param name="dataMask">
        /// Bitmask that selects which output categories should be requested and extracted. Output nodes
        /// outside these categories are ignored for this run.
        /// </param>
        /// <param name="fillArgumentsCallback">
        /// Optional callback that appends extra execution arguments before the graph starts.
        /// </param>
        /// <param name="cache">
        /// Optional graph execution cache owned by the caller and reused across graph executions.
        /// </param>
        /// <returns>
        /// An enumerator that executes the graph asynchronously, transfers selected outputs into the
        /// request's <see cref="BiomeData"/>, and completes <paramref name="request"/> when finished.
        /// </returns>
        /// <exception cref="System.ArgumentException">
        /// Thrown when <paramref name="baseResolution"/> is invalid for graph execution.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The method first discovers the output nodes that match the requested
        /// <paramref name="dataMask"/>. For some categories it also filters out nodes whose referenced
        /// templates or terrain layers are not usable.
        /// </para>
        /// <para>
        /// Once execution completes, outputs are removed from <see cref="ExecutionHandle.data"/> in
        /// category order and written into the request data. Removing them from the pool transfers
        /// ownership away from the execution handle so the final <see cref="handle.Dispose"/> call
        /// does not destroy them.
        /// </para>
        /// <para>
        /// If the mask resolves to no output nodes, the request completes immediately without running
        /// the graph.
        /// </para>
        /// </remarks>
        public static IEnumerator RequestBiomeData(IBiome biome, BiomeDataRequest request, TerrainGraph graph, Bounds biomeWorldBounds, Space simSpace, int baseResolution, int seed, GraphInputContainer inputContainer = null, BiomeDataMask dataMask = (BiomeDataMask)~0, TerrainGraph.FillArgumentsHandler fillArgumentsCallback = null, GraphExecutionCache cache = null)
        {
            if (baseResolution < 0 || (baseResolution - 0 * 1) % 8 != 0)
            {
                throw new System.ArgumentException("Invalid base resolution, must be 8*x");
            }

            List<string> nodeIds = new List<string>();
            HeightOutputNode heightOutputNode = null;
            if (dataMask.HasFlag(BiomeDataMask.HeightMap))
            {
                heightOutputNode = graph.GetNode<HeightOutputNode>() as HeightOutputNode;
            }
            if (heightOutputNode != null)
            {
                nodeIds.Add(heightOutputNode.id);
            }

            HoleOutputNode holeOutputNode = null;
            if (dataMask.HasFlag(BiomeDataMask.HoleMap))
            {
                holeOutputNode = graph.GetNode<HoleOutputNode>() as HoleOutputNode;
            }
            if (holeOutputNode != null)
            {
                nodeIds.Add(holeOutputNode.id);
            }

            MeshDensityOutputNode meshDensityOutputNode = null;
            if (dataMask.HasFlag(BiomeDataMask.MeshDensityMap))
            {
                meshDensityOutputNode = graph.GetNode<MeshDensityOutputNode>() as MeshDensityOutputNode;
            }
            if (meshDensityOutputNode != null)
            {
                nodeIds.Add(meshDensityOutputNode.id);
            }

            AlbedoOutputNode albedoOutputNode = null;
            if (dataMask.HasFlag(BiomeDataMask.AlbedoMap))
            {
                albedoOutputNode = graph.GetNode<AlbedoOutputNode>() as AlbedoOutputNode;
            }
            if (albedoOutputNode != null)
            {
                nodeIds.Add(albedoOutputNode.id);
            }

            MetallicSmoothnessOutputNode metallicSmoothnessOutputNode = null;
            if (dataMask.HasFlag(BiomeDataMask.MetallicMap))
            {
                metallicSmoothnessOutputNode = graph.GetNode<MetallicSmoothnessOutputNode>() as MetallicSmoothnessOutputNode;
            }
            if (metallicSmoothnessOutputNode != null)
            {
                nodeIds.Add(metallicSmoothnessOutputNode.id);
            }

            List<TextureOutputNode> textureOutputNodes = null;
            if (dataMask.HasFlag(BiomeDataMask.LayerWeightMaps))
            {
                textureOutputNodes = graph.GetNodesOfType<TextureOutputNode>();
                textureOutputNodes.RemoveAll(n => n.terrainLayer == null);
                textureOutputNodes.Sort((n0, n1) => { return n0.order.CompareTo(n1.order); });
            }
            else
            {
                textureOutputNodes = new List<TextureOutputNode>();
            }
            foreach (TextureOutputNode n in textureOutputNodes)
            {
                nodeIds.Add(n.id);
            }

            List<TreeOutputNode> treeOutputNodes = null;
            if (dataMask.HasFlag(BiomeDataMask.TreeInstances))
            {
                treeOutputNodes = graph.GetNodesOfType<TreeOutputNode>();
                treeOutputNodes.RemoveAll(n => n.treeTemplate == null || !n.treeTemplate.IsValid());
            }
            else
            {
                treeOutputNodes = new List<TreeOutputNode>();
            }
            foreach (TreeOutputNode n in treeOutputNodes)
            {
                nodeIds.Add(n.id);
            }

            List<DetailDensityOutputNode> detailDensityOutputNodes = null;
            if (dataMask.HasFlag(BiomeDataMask.DetailDensityMaps))
            {
                detailDensityOutputNodes = graph.GetNodesOfType<DetailDensityOutputNode>();
                detailDensityOutputNodes.RemoveAll(n => n.detailTemplate == null || !n.detailTemplate.IsValid());
            }
            else
            {
                detailDensityOutputNodes = new List<DetailDensityOutputNode>();
            }
            foreach (DetailDensityOutputNode n in detailDensityOutputNodes)
            {
                nodeIds.Add(n.id);
            }

            List<DetailInstanceOutputNode> detailInstanceOutputNodes = null;
            if (dataMask.HasFlag(BiomeDataMask.DetailInstances))
            {
                detailInstanceOutputNodes = graph.GetNodesOfType<DetailInstanceOutputNode>();
                detailInstanceOutputNodes.RemoveAll(n => n.detailTemplate == null || !n.detailTemplate.IsValid());
            }
            else
            {
                detailInstanceOutputNodes = new List<DetailInstanceOutputNode>();
            }
            foreach (DetailInstanceOutputNode n in detailInstanceOutputNodes)
            {
                nodeIds.Add(n.id);
            }

            List<ObjectOutputNode> objectOutputNodes = null;
            if (dataMask.HasFlag(BiomeDataMask.ObjectInstances))
            {
                objectOutputNodes = graph.GetNodesOfType<ObjectOutputNode>();
                objectOutputNodes.RemoveAll(n => n.objectTemplate == null || !n.objectTemplate.IsValid());
            }
            else
            {
                objectOutputNodes = new List<ObjectOutputNode>();
            }
            foreach (ObjectOutputNode n in objectOutputNodes)
            {
                nodeIds.Add(n.id);
            }

            List<OutputNode> genericOutputNodes = null;
            if (dataMask.HasFlag(BiomeDataMask.GenericTextures) || dataMask.HasFlag(BiomeDataMask.GenericBuffers))
            {
                genericOutputNodes = graph.GetNodesOfType<OutputNode>();
                genericOutputNodes.RemoveAll(n => string.IsNullOrEmpty(n.outputName));
            }
            else
            {
                genericOutputNodes = new List<OutputNode>();
            }
            foreach (OutputNode n in genericOutputNodes)
            {
                nodeIds.Add(n.id);
            }

            if (nodeIds.Count == 0)
            {
                request.Complete();
                yield break;
            }

            Vector2 mpp = new Vector2(biomeWorldBounds.size.x / (baseResolution - 0), biomeWorldBounds.size.z / (baseResolution - 0));
            float rx = simSpace == Space.World ? biomeWorldBounds.min.x : 0;
            float ry = simSpace == Space.World ? biomeWorldBounds.min.z : 0;
            float rw = baseResolution * mpp.x;
            float rh = baseResolution * mpp.y;

            TerrainGenerationConfigs configs = new TerrainGenerationConfigs();
            configs.resolution = baseResolution;
            configs.seed = seed;
            configs.terrainHeight = biomeWorldBounds.size.y;
            configs.worldBounds = new Rect(rx, ry, rw, rh);

#if UNITY_EDITOR
            int editorProgressId = Progress.Start($"Processing biome {biome.gameObject.name}");
#endif
            ExecutionHandle handle = graph.Execute(nodeIds.ToArray(), configs, inputContainer, fillArgumentsCallback, cache);
            while (!handle.isCompleted)
            {
#if UNITY_EDITOR
                Progress.Report(editorProgressId, handle.progress.totalProgress);
#endif
                yield return null;
            }

            if (heightOutputNode != null)
            {
                RenderTexture generatedHeightMap = handle.data.RemoveRTFromPool(heightOutputNode.mainOutputSlot);
                request.data.heightMap = generatedHeightMap;
            }
            yield return null;

            if (holeOutputNode != null)
            {
                RenderTexture generatedHoleMap = handle.data.RemoveRTFromPool(holeOutputNode.mainOutputSlot);
                request.data.holeMap = generatedHoleMap;
            }
            yield return null;

            if (meshDensityOutputNode != null)
            {
                RenderTexture generatedmeshDensityMap = handle.data.RemoveRTFromPool(meshDensityOutputNode.mainOutputSlot);
                request.data.meshDensityMap = generatedmeshDensityMap;
            }
            yield return null;

            if (albedoOutputNode != null)
            {
                RenderTexture generatedAlbedoMap = handle.data.RemoveRTFromPool(albedoOutputNode.mainOutputSlot);
                request.data.albedoMap = generatedAlbedoMap;
            }
            yield return null;

            if (metallicSmoothnessOutputNode != null)
            {
                RenderTexture generatedMetallicMap = handle.data.RemoveRTFromPool(metallicSmoothnessOutputNode.mainOutputSlot);
                request.data.metallicMap = generatedMetallicMap;
            }
            yield return null;

            foreach (TextureOutputNode n in textureOutputNodes)
            {
                RenderTexture generatedWeight = handle.data.RemoveRTFromPool(n.mainOutputSlot);
                request.data.AddTextureLayer(n.terrainLayer, generatedWeight);
            }
            yield return null;

            foreach (TreeOutputNode n in treeOutputNodes)
            {
                GraphBuffer treeBuffer = handle.data.RemoveBufferFromPool(n.mainOutputSlot);
                if (treeBuffer != null && treeBuffer.buffer != null)
                {
                    request.data.AddTree(n.treeTemplate, treeBuffer.buffer);
                }
            }
            yield return null;

            foreach (DetailDensityOutputNode n in detailDensityOutputNodes)
            {
                RenderTexture generatedDensityMap = handle.data.RemoveRTFromPool(n.mainOutputSlot);
                request.data.AddDetailDensity(n.detailTemplate, generatedDensityMap);
            }
            yield return null;

            foreach (DetailInstanceOutputNode n in detailInstanceOutputNodes)
            {
                GraphBuffer instanceBuffer = handle.data.RemoveBufferFromPool(n.mainOutputSlot);
                if (instanceBuffer != null && instanceBuffer.buffer != null)
                {
                    request.data.AddDetailInstance(n.detailTemplate, instanceBuffer.buffer);
                }
            }
            yield return null;

            foreach (ObjectOutputNode n in objectOutputNodes)
            {
                GraphBuffer objectBuffer = handle.data.RemoveBufferFromPool(n.mainOutputSlot);
                if (objectBuffer != null && objectBuffer.buffer != null)
                {
                    request.data.AddObject(n.objectTemplate, objectBuffer.buffer);
                }
            }
            yield return null;

            foreach (OutputNode n in genericOutputNodes)
            {
                if (n.slotType.Equals(typeof(BufferSlot)))
                {
                    if (dataMask.HasFlag(BiomeDataMask.GenericBuffers))
                    {
                        GraphBuffer buffer = handle.data.RemoveBufferFromPool(n.mainOutputSlot);
                        if (buffer != null && buffer.buffer != null)
                        {
                            request.data.AddGenericBuffer(n.outputName, buffer.buffer);
                        }
                    }
                }
                else if (n.slotType.Equals(typeof(MaskSlot)) || n.slotType.Equals(typeof(ColorTextureSlot)))
                {
                    if (dataMask.HasFlag(BiomeDataMask.GenericTextures))
                    {
                        RenderTexture generatedTexture = handle.data.RemoveRTFromPool(n.mainOutputSlot);
                        if (generatedTexture != null)
                        {
                            request.data.AddGenericTexture(n.outputName, generatedTexture);
                        }
                    }
                }
            }

            handle.Dispose();
            request.Complete();

#if UNITY_EDITOR
            Progress.Finish(editorProgressId);
#endif
        }

        public static bool HasSceneHeightInput(this TerrainGraph graph)
        {
            List<InputNode> inputNodes = graph.GetNodesOfType<InputNode>();
            return inputNodes.Exists(n => string.Equals(n.inputName, GraphConstants.SCENE_HEIGHT_INPUT_NAME));
        }
    }
}
#endif


