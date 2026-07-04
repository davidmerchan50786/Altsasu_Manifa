#if VISTA
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using System.Linq;
using Pinwheel.Vista.Diagnostics;
#if UNITY_EDITOR
using UnityEditor;
#endif


namespace Pinwheel.Vista
{
    [AddComponentMenu("Vista/Vista Manager")]
    [HelpURL("https://docs.pinwheelstud.io/vista/docs/vista-manager-component-overview.html")]
    [ExecuteInEditMode]
    /// <summary>
    /// Coordinates biome evaluation, data blending, and tile population for a Vista scene.
    /// </summary>
    /// <remarks>
    /// The manager is the runtime entry point for terrain generation. It discovers tiles and biomes, requests
    /// <see cref="BiomeData"/> for each relevant tile, optionally blends overlapping biome results, then forwards the
    /// resulting outputs to the tile interfaces implemented by the active terrain system. Generation runs progressively
    /// through a coroutine-backed <see cref="ProgressiveTask"/> and only one manager task is allowed at a time.
    /// </remarks>
    public partial class VistaManager : MonoBehaviour
    {
        protected static HashSet<VistaManager> s_allInstances = new HashSet<VistaManager>();
        /// <summary>
        /// Gets the currently enabled manager instances tracked by the runtime.
        /// </summary>
        /// <remarks>
        /// Managers register in <c>OnEnable</c> and unregister in <c>OnDisable</c>.
        /// </remarks>
        public static IEnumerable<VistaManager> allInstances
        {
            get
            {
                return s_allInstances;
            }
        }

        /// <summary>
        /// Represents a callback that contributes tiles owned by a manager.
        /// </summary>
        /// <param name="sender">The manager requesting tile discovery.</param>
        /// <param name="tiles">The collector that should receive discovered tiles.</param>
        public delegate void CollectTilesHandler(VistaManager sender, Collector<ITile> tiles);
        /// <summary>
        /// Occurs when a manager needs tile providers to register their tiles for generation.
        /// </summary>
        public static event CollectTilesHandler collectTiles;

        internal delegate void CollectBiomesHandler(VistaManager sender, Collector<IBiome> biomes);
        internal static CollectBiomesHandler collectFreeBiomes;

        /// <summary>
        /// Represents a callback raised at the start or end of the manager generation pipeline.
        /// </summary>
        /// <param name="sender">The manager whose pipeline is transitioning state.</param>
        public delegate void GeneratePipelineHandler(VistaManager sender);
        /// <summary>
        /// Occurs immediately before biome requests begin for a generation pass.
        /// </summary>
        public static event GeneratePipelineHandler beforeGenerating;
        /// <summary>
        /// Occurs after all tiles have been populated, seams matched, and biome counters updated.
        /// </summary>
        public static event GeneratePipelineHandler afterGenerating;

        [SerializeField]
        private UnityEvent m_beforeGeneratingUnityCallback;
        /// <summary>
        /// Gets the UnityEvent invoked immediately before a generation pass begins.
        /// </summary>
        public UnityEvent beforeGeneratingUnityCallback
        {
            get
            {
                return m_beforeGeneratingUnityCallback;
            }
        }

        [SerializeField]
        private UnityEvent m_afterGeneratingUnityCallback;
        /// <summary>
        /// Gets the UnityEvent invoked after a generation pass finishes successfully.
        /// </summary>
        public UnityEvent afterGeneratingUnityCallback
        {
            get
            {
                return m_afterGeneratingUnityCallback;
            }
        }

        /// <summary>
        /// Represents a callback raised after a single texture output has been pushed into a tile.
        /// </summary>
        /// <param name="sender">The manager performing the population step.</param>
        /// <param name="tile">The tile that received the texture.</param>
        /// <param name="texture">The generated texture that was just applied.</param>
        public delegate void TexturePopulatedHandler(VistaManager sender, ITile tile, RenderTexture texture);
        /// <summary>
        /// Occurs after a tile height map has been populated.
        /// </summary>
        public static event TexturePopulatedHandler heightMapPopulated;
        /// <summary>
        /// Occurs after a tile hole map has been populated.
        /// </summary>
        public static event TexturePopulatedHandler holeMapPopulated;
        /// <summary>
        /// Occurs after a tile mesh-density map has been populated.
        /// </summary>
        public static event TexturePopulatedHandler meshDensityMapPopulated;
        /// <summary>
        /// Occurs after a tile albedo map has been populated.
        /// </summary>
        public static event TexturePopulatedHandler albedoMapPopulated;
        /// <summary>
        /// Occurs after a tile metallic map has been populated.
        /// </summary>
        public static event TexturePopulatedHandler metallicMapPopulated;

        /// <summary>
        /// Represents a callback raised after terrain layer weights have been applied to a tile.
        /// </summary>
        /// <param name="sender">The manager performing the population step.</param>
        /// <param name="tile">The tile that received the layer weights.</param>
        /// <param name="layers">The terrain layers paired with <paramref name="weights"/> by index.</param>
        /// <param name="weights">The weight textures applied for each layer.</param>
        public delegate void LayerWeightPopulatedHandler(VistaManager sender, ITile tile, List<TerrainLayer> layers, List<RenderTexture> weights);
        /// <summary>
        /// Occurs after terrain layer weights have been populated for a tile.
        /// </summary>
        public static event LayerWeightPopulatedHandler layerWeightPopulated;

        /// <summary>
        /// Represents a callback raised after tree samples have been populated into a tile.
        /// </summary>
        /// <param name="sender">The manager performing the population step.</param>
        /// <param name="tile">The tile that received the tree data.</param>
        /// <param name="treeTemplates">Tree templates paired with <paramref name="treeBuffers"/> by index.</param>
        /// <param name="treeBuffers">Generated tree sample buffers for the tile.</param>
        public delegate void TreePopulatedHandler(VistaManager sender, ITile tile, List<TreeTemplate> treeTemplates, List<ComputeBuffer> treeBuffers);
        /// <summary>
        /// Occurs after tree data has been populated for a tile.
        /// </summary>
        public static event TreePopulatedHandler treePopulated;

        /// <summary>
        /// Represents a callback raised after detail density maps have been applied to a tile.
        /// </summary>
        /// <param name="sender">The manager performing the population step.</param>
        /// <param name="tile">The tile that received the detail density data.</param>
        /// <param name="detailTemplates">Detail templates paired with <paramref name="densityMaps"/> by index.</param>
        /// <param name="densityMaps">Generated detail density maps for the tile.</param>
        public delegate void DetailDensityPopulatedHandler(VistaManager sender, ITile tile, List<DetailTemplate> detailTemplates, List<RenderTexture> densityMaps);
        /// <summary>
        /// Occurs after detail density data has been populated for a tile.
        /// </summary>
        public static event DetailDensityPopulatedHandler detailDensityPopulated;

        /// <summary>
        /// Represents a callback raised after detail instance buffers have been applied to a tile.
        /// </summary>
        /// <param name="sender">The manager performing the population step.</param>
        /// <param name="tile">The tile that received the detail instance data.</param>
        /// <param name="detailTemplates">Detail templates paired with <paramref name="detailBuffers"/> by index.</param>
        /// <param name="detailBuffers">Generated detail instance buffers for the tile.</param>
        public delegate void DetailInstancePopulatedHandler(VistaManager sender, ITile tile, List<DetailTemplate> detailTemplates, List<ComputeBuffer> detailBuffers);
        /// <summary>
        /// Occurs after detail instance data has been populated for a tile.
        /// </summary>
        public static event DetailInstancePopulatedHandler detailInstancePopulated;

        /// <summary>
        /// Represents a callback raised after object instance data has been populated for a tile.
        /// </summary>
        /// <param name="sender">The manager performing the population step.</param>
        /// <param name="tile">The tile that received the object data.</param>
        /// <param name="objectTemplates">Object templates paired with <paramref name="objectBuffers"/> by index.</param>
        /// <param name="objectBuffers">Generated object instance buffers for the tile.</param>
        public delegate void ObjectPopulatedHandler(VistaManager sender, ITile tile, List<ObjectTemplate> objectTemplates, List<ComputeBuffer> objectBuffers);
        /// <summary>
        /// Occurs after object data has been populated for a tile.
        /// </summary>
        public static event ObjectPopulatedHandler objectPopulated;

        /// <summary>
        /// Represents a callback raised after generic texture outputs have been applied to a tile.
        /// </summary>
        /// <param name="sender">The manager performing the population step.</param>
        /// <param name="tile">The tile that received the generic textures.</param>
        /// <param name="labels">Labels paired with <paramref name="textures"/> by index.</param>
        /// <param name="textures">Generic texture outputs for the tile.</param>
        public delegate void GenericTexturePopulatedHandler(VistaManager sender, ITile tile, List<string> labels, List<RenderTexture> textures);
        /// <summary>
        /// Occurs after generic texture outputs have been populated for a tile.
        /// </summary>
        public static event GenericTexturePopulatedHandler genericTexturesPopulated;

        /// <summary>
        /// Represents a callback raised after generic buffer outputs have been applied to a tile.
        /// </summary>
        /// <param name="sender">The manager performing the population step.</param>
        /// <param name="tile">The tile that received the generic buffers.</param>
        /// <param name="labels">Labels paired with <paramref name="buffers"/> by index.</param>
        /// <param name="buffers">Generic buffer outputs for the tile.</param>
        public delegate void GenericBufferPopulatedHandler(VistaManager sender, ITile tile, List<string> labels, List<ComputeBuffer> buffers);
        /// <summary>
        /// Occurs after generic buffer outputs have been populated for a tile.
        /// </summary>
        public static event GenericBufferPopulatedHandler genericBuffersPopulated;

        internal static event Func<VistaManager, IBiome[]> getBiomesCallback;

        internal delegate BiomeData BiomeDataBlendHandler(List<IBiome> srcBiomes, List<BiomeData> srcDatas);
        internal static event BiomeDataBlendHandler blendBiomeDataCallback;

        internal event Action drawGizmosSelectedCallback;

        protected static List<ITerrainSystem> s_terrainSystems;
        /// <summary>
        /// Gets the tile currently being processed by the active generation pass.
        /// </summary>
        /// <remarks>
        /// The value changes as the manager iterates through overlapped tiles and is cleared when generation finishes.
        /// </remarks>
        public ITile currentlyProcessingTile { get; protected set; }

        protected static ProgressiveTask s_activeGenerateTask;

        /// <summary>
        /// Carries manager-level options used by object populators during one generation pass.
        /// </summary>
        public struct ObjectPopulateArgs
        {
            /// <summary>
            /// Gets or sets the maximum number of objects a progressive populator should spawn per frame.
            /// </summary>
            public int objectsPerFrame { get; set; }
        }

        [SerializeField]
        protected string m_id;
        /// <summary>
        /// Gets the persistent identifier of this manager instance.
        /// </summary>
        public string id
        {
            get
            {
                return m_id;
            }
        }

        [SerializeField]
        protected float m_terrainMaxHeight;
        /// <summary>
        /// Gets or sets the maximum terrain height assigned to tiles before population begins.
        /// </summary>
        /// <remarks>
        /// Values below zero are clamped to zero.
        /// </remarks>
        public float terrainMaxHeight
        {
            get
            {
                return m_terrainMaxHeight;
            }
            set
            {
                m_terrainMaxHeight = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        protected int m_heightMapResolution;
        /// <summary>
        /// Gets or sets the height-map resolution pushed into tiles and biome requests.
        /// </summary>
        /// <remarks>
        /// The value is normalized to a valid Unity-style height-map resolution by taking the closest power of two, adding
        /// one, and clamping to Vista's supported height-map range.
        /// </remarks>
        public int heightMapResolution
        {
            get
            {
                return m_heightMapResolution;
            }
            set
            {
                m_heightMapResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(value) + 1, Constants.HM_RES_MIN, Constants.HM_RES_MAX);
            }
        }

        [SerializeField]
        protected int m_textureResolution;
        /// <summary>
        /// Gets or sets the texture resolution used for non-height texture outputs.
        /// </summary>
        /// <remarks>
        /// The value is normalized to a power of two and clamped to Vista's supported range.
        /// </remarks>
        public int textureResolution
        {
            get
            {
                return m_textureResolution;
            }
            set
            {
                m_textureResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(value), Constants.HM_RES_MIN, Constants.HM_RES_MAX);
            }
        }

        [SerializeField]
        protected int m_detailDensityMapResolution;
        /// <summary>
        /// Gets or sets the resolution assigned to detail density maps before tile population.
        /// </summary>
        /// <remarks>
        /// The value is normalized to a power of two and clamped to Vista's supported generic-resolution range.
        /// </remarks>
        public int detailDensityMapResolution
        {
            get
            {
                return m_detailDensityMapResolution;
            }
            set
            {
                m_detailDensityMapResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(value), Constants.RES_MIN, Constants.RES_MAX);
            }
        }

        [SerializeField]
        protected bool m_shouldCullBiomes;
        /// <summary>
        /// Gets or sets whether the manager should skip biome requests for tile-biome pairs that do not overlap.
        /// </summary>
        /// <remarks>
        /// When enabled, the precomputed overlap test is used to avoid requesting data from biomes that cannot contribute to
        /// the current tile.
        /// </remarks>
        public bool shouldCullBiomes
        {
            get
            {
                return m_shouldCullBiomes;
            }
            set
            {
                m_shouldCullBiomes = value;
            }
        }

        [SerializeField]
        protected MissingOutputAction m_missingGeometryAction;
        /// <summary>
        /// Gets or sets how geometry channels should behave when the current generation pass does not produce them.
        /// </summary>
        public MissingOutputAction missingGeometryAction
        {
            get
            {
                return m_missingGeometryAction;
            }
            set
            {
                m_missingGeometryAction = value;
            }
        }

        [SerializeField]
        protected MissingOutputAction m_missingTextureAction;
        /// <summary>
        /// Gets or sets how texture channels should behave when the current generation pass does not produce them.
        /// </summary>
        public MissingOutputAction missingTextureAction
        {
            get
            {
                return m_missingTextureAction;
            }
            set
            {
                m_missingTextureAction = value;
            }
        }

        [SerializeField]
        protected MissingOutputAction m_missingPopulationAction;
        /// <summary>
        /// Gets or sets how population channels should behave when the current generation pass does not produce them.
        /// </summary>
        public MissingOutputAction missingPopulationAction
        {
            get
            {
                return m_missingPopulationAction;
            }
            set
            {
                m_missingPopulationAction = value;
            }
        }

        [SerializeField]
        protected int m_objectToSpawnPerFrame;
        /// <summary>
        /// Gets or sets the object spawn budget forwarded to progressive object populators.
        /// </summary>
        /// <remarks>
        /// Values below one are clamped to one.
        /// </remarks>
        public int objectToSpawnPerFrame
        {
            get
            {
                return m_objectToSpawnPerFrame;
            }
            set
            {
                m_objectToSpawnPerFrame = Mathf.Max(1, value);
            }
        }

        protected long m_updateCounter = 0;

        /// <summary>
        /// Registers a terrain-system implementation with the global Vista runtime.
        /// </summary>
        /// <returns>No value is returned.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when a terrain system of type <typeparamref name="T"/> has already been registered.
        /// </exception>
        public static void RegisterTerrainSystem<T>() where T : class, ITerrainSystem, new()
        {
            if (s_terrainSystems == null)
            {
                s_terrainSystems = new List<ITerrainSystem>();
            }
            if (s_terrainSystems.Exists(s => s.GetType().Equals(typeof(T))))
            {
                throw new ArgumentException($"Terrain System {typeof(T).Name} is already registered.");
            }
            s_terrainSystems.Add(new T());
        }

        /// <summary>
        /// Unregisters a terrain-system implementation from the global Vista runtime.
        /// </summary>
        public static void UnregisterTerrainSystem<T>() where T : class, ITerrainSystem, new()
        {
            if (s_terrainSystems == null)
            {
                s_terrainSystems = new List<ITerrainSystem>();
            }
            s_terrainSystems.RemoveAll(s => s.GetType().Equals(typeof(T)));
        }

        /// <summary>
        /// Gets all terrain-system implementations currently registered with Vista.
        /// </summary>
        /// <returns>The registered terrain-system instances.</returns>
        public static IEnumerable<ITerrainSystem> GetTerrainSystems()
        {
            if (s_terrainSystems == null)
                s_terrainSystems = new List<ITerrainSystem>();
            return s_terrainSystems;
        }

        /// <summary>
        /// Gets the registered terrain-system instance of a specific type, if one exists.
        /// </summary>
        /// <returns>The registered terrain system of type <typeparamref name="T"/>, or <see langword="null"/> if none is registered.</returns>
        public static ITerrainSystem GetTerrainSystem<T>() where T : ITerrainSystem
        {
            if (s_terrainSystems == null)
            {
                s_terrainSystems = new List<ITerrainSystem>();
            }
            ITerrainSystem system = s_terrainSystems.Find(s => s.GetType().Equals(typeof(T)));
            return system;
        }

        /// <summary>
        /// Creates a new manager GameObject in the current scene.
        /// </summary>
        /// <returns>The newly created manager component.</returns>
        public static VistaManager CreateInstanceInScene()
        {
            GameObject managerGO = new GameObject("VistaManager");
            VistaManager manager = managerGO.AddComponent<VistaManager>();
            return manager;
        }

        /// <summary>
        /// Restores the manager to Vista's default runtime settings.
        /// </summary>
        public void Reset()
        {
            m_id = Utilities.GenerateId();
            m_terrainMaxHeight = 500;
            m_heightMapResolution = 513;
            m_textureResolution = 512;
            m_detailDensityMapResolution = 1024;
            m_shouldCullBiomes = true;
            m_missingGeometryAction = MissingOutputAction.Clear;
            m_missingTextureAction = MissingOutputAction.Clear;
            m_missingPopulationAction = MissingOutputAction.Clear;
            m_objectToSpawnPerFrame = 20;
        }

        protected void OnEnable()
        {
            s_allInstances.Add(this);
        }

        protected void OnDisable()
        {
            s_allInstances.Remove(this);
        }

        /// <summary>
        /// Gets the biomes currently owned by or associated with this manager.
        /// </summary>
        /// <returns>
        /// The biomes returned by the registered biome-provider callback, or a fallback single child biome when no callback
        /// is registered.
        /// </returns>
        public IBiome[] GetBiomes()
        {
            if (getBiomesCallback != null)
            {
                return getBiomesCallback.Invoke(this);
            }
            else
            {
                return new IBiome[] { GetComponentInChildren<IBiome>() };
            }
        }

        /// <summary>
        /// Gets all tiles contributed to this manager through the tile-collection pipeline.
        /// </summary>
        /// <returns>A list containing the collected tiles.</returns>
        public List<ITile> GetTiles()
        {
            Collector<ITile> collector = new Collector<ITile>();
            if (collectTiles != null)
            {
                collectTiles.Invoke(this, collector);
            }
            return collector.ToList();
        }

        /// <summary>
        /// Gets all tiles contributed to this manager through the tile-collection pipeline as an array.
        /// </summary>
        /// <returns>An array containing the collected tiles.</returns>
        public ITile[] GetTileArray()
        {
            Collector<ITile> collector = new Collector<ITile>();
            if (collectTiles != null)
            {
                collectTiles.Invoke(this, collector);
            }
            return collector.ToArray();
        }

        protected static void SetActiveTask(ProgressiveTask task)
        {
            if (s_activeGenerateTask != null && task != null && s_activeGenerateTask != task)
            {
                //Debug.LogWarning("VISTA: There is other task running. Having multiple tasks at the same time may lead to performance drop.");
            }
            s_activeGenerateTask = task;
        }

        /// <summary>
        /// Returns whether a Vista generation task is currently active.
        /// </summary>
        /// <returns><see langword="true"/> when a manager generation task is still tracked as active; otherwise, <see langword="false"/>.</returns>
        public static bool HasActiveTask()
        {
            return s_activeGenerateTask != null;
        }

        /// <summary>
        /// Forces the currently tracked generation task, if any, to complete immediately.
        /// </summary>
        /// <remarks>
        /// This marks the active <see cref="ProgressiveTask"/> as completed and clears the global active-task reference. It
        /// does not roll back any tile state that has already been applied.
        /// </remarks>
        public static void CancelActiveGenerateTask()
        {
            if (s_activeGenerateTask != null)
            {
                s_activeGenerateTask.Complete();
                SetActiveTask(null);
            }
        }

        /// <summary>
        /// Starts generation for every collected tile and discards the returned task handle.
        /// </summary>
        public void GenerateAllAndForget()
        {
            ITile[] tiles = GetTileArray();
            Generate(tiles);
        }

        /// <summary>
        /// Starts generation for every collected tile.
        /// </summary>
        /// <returns>A progressive task that completes when the full manager pipeline finishes.</returns>
        public ProgressiveTask GenerateAll()
        {
            ITile[] tiles = GetTileArray();
            return Generate(tiles);
        }

        /// <summary>
        /// Starts generation for a single tile.
        /// </summary>
        /// <param name="tile">The tile to generate.</param>
        /// <returns>A progressive task that completes when the tile and its shared pipeline work finish.</returns>
        public ProgressiveTask Generate(ITile tile)
        {
            return Generate(new ITile[] { tile });
        }

        /// <summary>
        /// Starts generation for a set of tiles.
        /// </summary>
        /// <param name="tiles">The tiles to process in this generation pass.</param>
        /// <returns>A progressive task that completes when biome evaluation, tile population, seam matching, and cleanup finish.</returns>
        /// <exception cref="Exception">
        /// Thrown when another Vista generation task is already active.
        /// </exception>
        /// <remarks>
        /// The method first computes tile-biome overlap, filters out tiles with no contributing biome, and then runs the
        /// progressive generation coroutine for the remaining tiles.
        /// </remarks>
        public ProgressiveTask Generate(IEnumerable<ITile> tiles)
        {
            if (HasActiveTask())
                throw new Exception("Vista is processing another task, please wait for a few seconds and try again.");

            ProgressiveTask taskHandle = new ProgressiveTask();
            SetActiveTask(taskHandle);

            IBiome[] biomes = GetBiomes();
            if (biomes.Length == 0)
            {
                m_updateCounter = DateTime.Now.Ticks;
                taskHandle.Complete();
                return taskHandle;
            }
            else
            {
                List<ITile> overlappedTiles = new List<ITile>();
                HashSet<KeyValuePair<ITile, IBiome>> overlapTests = new HashSet<KeyValuePair<ITile, IBiome>>();
                foreach (ITile t in tiles)
                {
                    if (!t.OverlapTest(biomes, overlapTests))
                        continue;
                    overlappedTiles.Add(t);
                }

                CoroutineUtility.StartCoroutine(ProcessBiomesProgressive(taskHandle, biomes, overlappedTiles, overlapTests));
                return taskHandle;
            }
        }

        private IEnumerator ProcessBiomesProgressive(ProgressiveTask taskHandle, IEnumerable<IBiome> biomes, IEnumerable<ITile> overlappedTiles, ICollection<KeyValuePair<ITile, IBiome>> overlapTests)
        {
            VistaDebugger.OpenScope($"VistaManager: {gameObject.name}", DebugScopeType.Custom);
#if UNITY_EDITOR
            int editorProgressId = Progress.Start("VistaManager.Generate()");
            Progress.Report(editorProgressId, 0);

            int currentTileIndex = 0;
            int tileCount = overlappedTiles.Count();
#endif

            beforeGeneratingUnityCallback.Invoke();
            beforeGenerating?.Invoke(this);

            foreach (IBiome b in biomes)
            {
                b.OnBeforeVMGenerate();
            }

            foreach (ITile t in overlappedTiles)
            {
                t.maxHeight = terrainMaxHeight;
                t.heightMapResolution = heightMapResolution;
                t.textureResolution = textureResolution;
                t.detailDensityMapResolution = detailDensityMapResolution;
            }

            ObjectPopulateArgs objectPopulateArgs = new ObjectPopulateArgs();
            objectPopulateArgs.objectsPerFrame = m_objectToSpawnPerFrame;

            foreach (ITile t in overlappedTiles)
            {                
                VistaDebugger.OpenScope($"Process Tile: {t.gameObject.name}", DebugScopeType.Custom);
#if UNITY_EDITOR
                Progress.Report(editorProgressId, currentTileIndex, tileCount, "Processing tiles");
                currentTileIndex += 1;
#endif
                currentlyProcessingTile = t;

                List<BiomeDataRequest> requests = new List<BiomeDataRequest>();
                List<IBiome> sourceBiomes = new List<IBiome>();
                foreach (IBiome b in biomes)
                {
                    if (m_shouldCullBiomes && !overlapTests.Contains(new KeyValuePair<ITile, IBiome>(t, b)))
                    {
                        continue;
                    }

                    BiomeDataRequest r = b.RequestData(t.worldBounds, heightMapResolution, textureResolution);
                    requests.Add(r);
                    sourceBiomes.Add(b);
                    yield return r;
                }

                List<BiomeData> biomeDatas = new List<BiomeData>();
                foreach (BiomeDataRequest r in requests)
                {
                    biomeDatas.Add(r.data);
                }

                BiomeData data = blendBiomeDataCallback.Invoke(sourceBiomes, biomeDatas);
                foreach (BiomeData d in biomeDatas)
                {
                    d.Dispose();
                }

                yield return null;
                if (taskHandle.isCompleted) yield break;

                VistaDebugger.OpenScope("Populate data", DebugScopeType.Custom);
                t.OnBeforeApplyingData();
                HandlePopulateGeometry(t, data);
                HandlePopulateTextures(t, data);
                yield return null;
                if (taskHandle.isCompleted) yield break;

                HandlePopulateTrees(t, data);
                yield return null;
                if (taskHandle.isCompleted) yield break;

                yield return HandlePopulateDetailDensity(t, data);
                yield return null;
                if (taskHandle.isCompleted) yield break;

                HandlePopulateDetailInstances(t, data);
                yield return null;
                if (taskHandle.isCompleted) yield break;

                yield return HandlePopulateObjects(t, data, objectPopulateArgs);
                yield return null;
                if (taskHandle.isCompleted) yield break;

                HandlePopulateGenericTextures(t, data);
                yield return null;
                if (taskHandle.isCompleted) yield break;

                HandlePopulateGenericBuffers(t, data);

                data.Dispose();
                VistaDebugger.CloseScope(); // Populate
                VistaDebugger.CloseScope(); // Process Tile
                yield return null;
                if (taskHandle.isCompleted) yield break;
            }
            yield return null;
            if (taskHandle.isCompleted) yield break;

#if UNITY_EDITOR
            Progress.Report(editorProgressId, 1, "Finishing up");
#endif

            foreach (ITile t in overlappedTiles)
            {
                currentlyProcessingTile = t;
                if (t is IGeometryPopulator gp)
                {
                    gp.MatchSeams();
                }
            }
            yield return null;
            if (taskHandle.isCompleted) yield break;

            foreach (ITile t in overlappedTiles)
            {
                currentlyProcessingTile = t;
                t.OnAfterApplyingData();
            }

            foreach (IBiome b in biomes)
            {
                b.OnAfterVMGenerate();
            }

            currentlyProcessingTile = null;
            UpdateBiomeCounter(biomes);

            afterGeneratingUnityCallback.Invoke();
            afterGenerating?.Invoke(this);

            VistaDebugger.CloseScope(); // VistaManager generation scope
            
            taskHandle.Complete();
            SetActiveTask(null);
#if UNITY_EDITOR
            Progress.Finish(editorProgressId);
#endif
        }

        protected void UpdateBiomeCounter(IEnumerable<IBiome> biomes)
        {
            foreach (IBiome b in biomes)
            {
                b.updateCounter = m_updateCounter;
            }
        }

        /// <summary>
        /// Marks all biomes as stale from the manager's perspective and starts a full generation pass.
        /// </summary>
        /// <returns>A progressive task that completes when the forced generation pass finishes.</returns>
        /// <remarks>
        /// This updates the manager-side biome counter timestamp before calling <see cref="GenerateAll"/>.
        /// </remarks>
        public ProgressiveTask ForceGenerate()
        {
            m_updateCounter = DateTime.Now.Ticks;
            return GenerateAll();
        }

        protected static void GetPipelineDelegates(List<string> names, List<Delegate> delegates)
        {
            names.Add(nameof(collectTiles)); delegates.Add(collectTiles);

            names.Add(nameof(beforeGenerating)); delegates.Add(beforeGenerating);

            names.Add(nameof(heightMapPopulated)); delegates.Add(heightMapPopulated);
            names.Add(nameof(holeMapPopulated)); delegates.Add(holeMapPopulated);
            names.Add(nameof(meshDensityMapPopulated)); delegates.Add(meshDensityMapPopulated);

            names.Add(nameof(albedoMapPopulated)); delegates.Add(albedoMapPopulated);
            names.Add(nameof(metallicMapPopulated)); delegates.Add(metallicMapPopulated);

            names.Add(nameof(layerWeightPopulated)); delegates.Add(layerWeightPopulated);

            names.Add(nameof(treePopulated)); delegates.Add(treePopulated);

            names.Add(nameof(detailDensityPopulated)); delegates.Add(detailDensityPopulated);
            names.Add(nameof(detailInstancePopulated)); delegates.Add(detailInstancePopulated);

            names.Add(nameof(objectPopulated)); delegates.Add(objectPopulated);

            names.Add(nameof(genericTexturesPopulated)); delegates.Add(genericTexturesPopulated);
            names.Add(nameof(genericBuffersPopulated)); delegates.Add(genericBuffersPopulated);

            names.Add(nameof(afterGenerating)); delegates.Add(afterGenerating);
        }

        /// <summary>
        /// Collects scene height from the manager's tiles into a destination render texture.
        /// </summary>
        /// <param name="targetRt">The destination texture that receives the collected height data.</param>
        /// <param name="worldBounds">The world-space bounds represented by <paramref name="targetRt"/>.</param>
        /// <remarks>
        /// This is primarily used by <see cref="LocalProceduralBiome"/> when it needs a scene-height input for graph
        /// execution.
        /// </remarks>
        public void CollectSceneHeight(RenderTexture targetRt, Bounds worldBounds)
        {
            ITile[] tiles = GetTileArray();
            SceneDataUtils.CollectWorldHeight(tiles, targetRt, worldBounds);
        }

        private void OnDrawGizmosSelected()
        {
            drawGizmosSelectedCallback?.Invoke();
        }
    }
}
#endif


