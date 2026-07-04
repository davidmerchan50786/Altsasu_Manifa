#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graph;
using Pinwheel.Vista.Graphics;

namespace Pinwheel.Vista.Graph
{
    [System.Serializable]
    /// <summary>
    /// Builds graph inputs and extra execution arguments for a Local Procedural Biome.
    /// </summary>
    /// <remarks>
    /// This helper is created during <see cref="LocalProceduralBiome.RequestData"/> to materialize
    /// biome-authored inputs into GPU resources that a terrain graph can consume. It also injects
    /// biome-specific execution arguments such as local scale, space mode, and biome cache bounds.
    /// </remarks>
    public class LPBInputProvider : IExternalInputProvider, ITerrainGraphArgumentsFiller//, ISerializationCallbackReceiver
    {
        private LocalProceduralBiome m_biome;
        private LocalProceduralBiome biome
        {
            get
            {
                if (m_biome == null)
                {
                    foreach (LocalProceduralBiome b in LocalProceduralBiome.allInstances)
                    {
                        if (b.m_guid == m_biomeInstanceGuid)
                        {
                            m_biome = b;
                            break;
                        }
                    }
                }
                return m_biome;
            }
        }

        [SerializeField]
        private string m_biomeInstanceGuid;

        private List<GraphRenderTexture> m_textures;
        private List<GraphBuffer> m_buffers;

        /// <summary>
        /// Creates an input provider bound to one Local Procedural Biome instance.
        /// </summary>
        /// <param name="b">
        /// The biome whose authored inputs and runtime-generated textures should be exposed to graph execution.
        /// </param>
        public LPBInputProvider(LocalProceduralBiome b)
        {
            m_biome = b;
            m_biomeInstanceGuid = b.m_guid;
        }

        /// <summary>
        /// Populates a graph input container with all external inputs required by the biome.
        /// </summary>
        /// <param name="inputContainer">
        /// The container that will be passed into terrain-graph execution.
        /// </param>
        /// <remarks>
        /// This method creates and tracks temporary GPU resources for:
        /// the combined biome mask, optional scene-height map, authored texture inputs converted to
        /// render textures, and authored position inputs uploaded to compute buffers.
        /// </remarks>
        public void SetInput(GraphInputContainer inputContainer)
        {
            m_textures = new List<GraphRenderTexture>();
            m_buffers = new List<GraphBuffer>();

            // Biome mask lifecycle: allocated here, tracked in m_textures for graph input,
            // then transferred to BiomeData.biomeMaskMap via RemoveTexture after graph execution.
            // CleanUp will not dispose it because RemoveTexture removes it from tracking first.
            GraphRenderTexture biomeMask = biome.RenderPostProcessedBiomeMask();
            biomeMask.identifier = GraphConstants.BIOME_MASK_INPUT_NAME;
            m_textures.Add(biomeMask);

            GraphRenderTexture sceneHeightMap = null;
            if (biome.shouldCollectSceneHeight)
            {
                sceneHeightMap = biome.RenderSceneHeightMap();
                sceneHeightMap.identifier = GraphConstants.SCENE_HEIGHT_INPUT_NAME;
                m_textures.Add(sceneHeightMap);
            }

            GraphRenderTexture[] customInputRT = CopyTextureInputsToRT();
            foreach (GraphRenderTexture rt in customInputRT)
            {
                if (rt != null)
                {
                    m_textures.Add(rt);
                }
            }

            GraphBuffer[] positionInputsBuffers = CopyPositionInputsToBuffers();
            foreach (GraphBuffer b in positionInputsBuffers)
            {
                if (b != null)
                {
                    m_buffers.Add(b);
                }
            }

            foreach (GraphRenderTexture rt in m_textures)
            {
                inputContainer.AddTexture(rt.identifier, rt.renderTexture);
            }
            foreach (GraphBuffer b in m_buffers)
            {
                inputContainer.AddBuffer(b.identifier, b.buffer);
            }
        }

        /// <summary>
        /// Disposes every temporary texture and buffer created by this provider that is still owned by it.
        /// </summary>
        /// <remarks>
        /// Callers may remove specific resources, such as the combined biome mask, before cleanup when
        /// ownership needs to be transferred elsewhere.
        /// </remarks>
        public TerrainGenerationConfigs GetDebugConfigs()
        {
            float maxHeight = 600;
            VistaManager vistaManager = biome.GetVistaManagerInstance();
            if (vistaManager != null)
            {
                maxHeight = vistaManager.terrainMaxHeight;
            }

            Bounds worldBounds = biome.worldBounds;
            TerrainGenerationConfigs configs = TerrainGenerationConfigs.Create();
            configs.resolution = biome.baseResolution;
            configs.seed = biome.seed;
            configs.terrainHeight = maxHeight;
            configs.worldBounds = new Rect(
                biome.space == Space.World ? worldBounds.min.x : 0,
                biome.space == Space.World ? worldBounds.min.z : 0,
                worldBounds.size.x,
                worldBounds.size.z);
            return configs;
        }

        public void CleanUp()
        {
            foreach (GraphRenderTexture rt in m_textures)
            {
                if (rt != null)
                {
                    rt.Dispose();
                }
            }
            m_textures = null;

            foreach (GraphBuffer b in m_buffers)
            {
                if (b != null)
                {
                    b.Dispose();
                }
            }
            m_buffers = null;
        }

        internal GraphRenderTexture[] CopyTextureInputsToRT()
        {
            TextureInput[] textureInputs = biome.textureInputs;
            GraphRenderTexture[] renderTextures = new GraphRenderTexture[textureInputs.Length];
            for (int i = 0; i < textureInputs.Length; ++i)
            {
                TextureInput input = textureInputs[i];
                if (input == null ||
                    input.texture == null ||
                    string.IsNullOrEmpty(input.name))
                    continue;

                Texture2D t2d = input.texture;
                bool isRawTexture =
                    t2d.format == TextureFormat.R8 ||
                    t2d.format == TextureFormat.R16 ||
                    t2d.format == TextureFormat.RFloat;
                RenderTextureFormat rtFormat = isRawTexture ? RenderTextureFormat.RFloat : RenderTextureFormat.ARGB32;
                GraphRenderTexture rt = new GraphRenderTexture(t2d.width, t2d.height, rtFormat);
                rt.identifier = input.name;
                Drawing.Blit(t2d, rt);
                renderTextures[i] = rt;
            }
            return renderTextures;
        }

        /// <summary>
        /// Detaches a generated texture from this provider so the caller can keep and manage it separately.
        /// </summary>
        /// <param name="identifier">
        /// The identifier assigned to the tracked texture.
        /// </param>
        /// <returns>
        /// The matching tracked texture, or <see langword="null"/> when no texture with that
        /// identifier is currently owned by the provider.
        /// </returns>
        public GraphRenderTexture RemoveTexture(string identifier)
        {
            GraphRenderTexture rt = m_textures.Find(t => t.identifier.Equals(identifier));
            if (rt != null)
            {
                m_textures.Remove(rt);
                return rt;
            }
            else
            {
                return null;
            }
        }

        internal GraphBuffer[] CopyPositionInputsToBuffers()
        {
            PositionInput[] positionInputs = biome.positionInputs;
            GraphBuffer[] buffers = new GraphBuffer[positionInputs.Length];
            for (int i = 0; i < positionInputs.Length; ++i)
            {
                PositionInput input = positionInputs[i];
                if (input == null ||
                    input.positionContainer == null ||
                    input.positionContainer == null ||
                    input.positionContainer.positions.Length == 0)
                    continue;

                GraphBuffer b = new GraphBuffer(input.positionContainer.positions.Length * PositionSample.SIZE, sizeof(float));
                b.identifier = input.name;
                b.buffer.SetData(input.positionContainer.positions);
                buffers[i] = b;
            }
            return buffers;
        }

        /// <summary>
        /// Adds biome-specific execution arguments to the terrain graph argument table.
        /// </summary>
        /// <param name="graph">
        /// The terrain graph about to execute.
        /// </param>
        /// <param name="args">
        /// The live argument dictionary that will be attached to the resulting <see cref="GraphContext"/>.
        /// </param>
        /// <remarks>
        /// The current implementation adds <see cref="Args.BIOME_SCALE"/>,
        /// <see cref="Args.BIOME_SPACE"/>, and <see cref="Args.BIOME_WORLD_BOUNDS"/> so biome-aware
        /// nodes can evaluate against the Local Procedural Biome cache space.
        /// </remarks>
        public void FillTerrainGraphArguments(TerrainGraph graph, IDictionary<int, Args> args)
        {
            args.Add(Args.BIOME_SCALE, Args.Create(biome.transform.localScale));
            args.Add(Args.BIOME_SPACE, Args.Create((int)biome.space));

            Bounds biomeWorldBounds = biome.worldBounds;
            Vector4 bwbArg = new Vector4(biomeWorldBounds.min.x, biomeWorldBounds.min.z, biomeWorldBounds.size.x, biomeWorldBounds.size.z);
            args.Add(Args.BIOME_WORLD_BOUNDS, Args.Create(bwbArg));
        }

        /// <summary>
        /// Updates the serialized biome GUID so the provider can reconnect to the live biome later.
        /// </summary>
        public void OnBeforeSerialize()
        {
            m_biomeInstanceGuid = m_biome.m_guid;
        }

        /// <summary>
        /// Called after deserialization.
        /// </summary>
        /// <remarks>
        /// The provider resolves the live biome lazily through the serialized GUID when
        /// <see cref="biome"/> is next accessed.
        /// </remarks>
        public void OnAfterDeserialize()
        {
        }
    }
}
#endif


