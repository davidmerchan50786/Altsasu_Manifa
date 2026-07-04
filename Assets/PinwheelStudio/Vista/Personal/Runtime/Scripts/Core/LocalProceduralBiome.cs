#if VISTA
using Pinwheel.Vista.Geometric;
using Pinwheel.Vista.Graph;
using Pinwheel.Vista.Graphics;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Pinwheel.Vista.ExposeProperty;
using Pinwheel.Vista.Diagnostics;

namespace Pinwheel.Vista
{
    [ExecuteInEditMode]
    [AddComponentMenu("Vista/Local Procedural Biome")]
    [HelpURL("https://docs.pinwheelstud.io/vista/docs/local-procedural-biome-component-overview.html")]
    /// <summary>
    /// Defines a biome whose graph is generated once in its own bounds, cached, then remapped into each requested tile.
    /// </summary>
    /// <remarks>
    /// A <see cref="LocalProceduralBiome"/> is both a biome definition and a graph input source. It contributes a polygonal
    /// biome mask, optional scene height, and custom texture or position inputs, then stores the generated
    /// <see cref="BiomeData"/> in <see cref="cachedData"/> for reuse. Subsequent tile requests copy that cached data from the
    /// biome's own bounds into the requested world bounds instead of re-running the terrain graph every time.
    /// </remarks>
    public class LocalProceduralBiome : MonoBehaviour, IProceduralBiome, ISerializationCallbackReceiver
    {
        protected static HashSet<LocalProceduralBiome> s_allInstances = new HashSet<LocalProceduralBiome>();
        /// <summary>
        /// Gets the currently enabled local procedural biomes tracked by the runtime.
        /// </summary>
        /// <remarks>
        /// Instances register in <c>OnEnable</c> and unregister in <c>OnDisable</c>. This collection is used by helpers
        /// such as <see cref="Graph.LPBInputProvider"/> to resolve a biome by serialized GUID after domain reload or
        /// deserialization.
        /// </remarks>
        public static IEnumerable<LocalProceduralBiome> allInstances
        {
            get
            {
                return s_allInstances;
            }
        }

        internal delegate TerrainGraph CloneAndOverrideGraphHandler(TerrainGraph src, IEnumerable<PropertyOverride> overrides);
        internal static event CloneAndOverrideGraphHandler cloneAndOverrideGraphCallback;

        [SerializeField]
        protected int m_order;
        /// <summary>
        /// Gets or sets the sort order used when multiple local biomes overlap the same tile.
        /// </summary>
        /// <remarks>
        /// The manager uses biome order to decide evaluation and blending sequence for overlapping local procedural biomes.
        /// Higher-level blending behavior is still controlled by <see cref="blendOptions"/>.
        /// </remarks>
        public int order
        {
            get
            {
                return m_order;
            }
            set
            {
                m_order = value;
            }
        }

        [SerializeField]
        protected TerrainGraph m_terrainGraph;
        /// <summary>
        /// Gets or sets the terrain graph that generates this biome's cached outputs.
        /// </summary>
        /// <remarks>
        /// The assigned graph is evaluated in the biome's own bounds when the cache is rebuilt. When exposed properties are
        /// present and a clone callback is available, the graph is cloned per cache rebuild so
        /// <see cref="propertyOverrides"/> can be applied without mutating the source asset instance.
        /// </remarks>
        public TerrainGraph terrainGraph
        {
            get
            {
                return m_terrainGraph;
            }
            set
            {
                m_terrainGraph = value;
            }
        }

        [SerializeField]
        protected Space m_space;
        /// <summary>
        /// Gets or sets the simulation space passed to the terrain graph and biome mask graph.
        /// </summary>
        /// <remarks>
        /// The value is forwarded into graph execution arguments so nodes can interpret biome-local data in either local or
        /// world space, depending on the selected mode.
        /// </remarks>
        public Space space
        {
            get
            {
                return m_space;
            }
            set
            {
                m_space = value;
            }
        }

        [SerializeField]
        protected BiomeDataMask m_dataMask;
        /// <summary>
        /// Gets or sets which output categories should be generated and cached for this biome.
        /// </summary>
        /// <remarks>
        /// The mask is passed to <c>TerrainGraphUtilities.RequestBiomeData</c>. Disabling a flag skips generating and
        /// storing that output category for this biome's cache.
        /// </remarks>
        public BiomeDataMask dataMask
        {
            get
            {
                return m_dataMask;
            }
            set
            {
                m_dataMask = value;
            }
        }

        [SerializeField]
        protected int m_baseResolution;
        /// <summary>
        /// Gets or sets the base graph resolution used when generating the biome cache in the biome's own bounds.
        /// </summary>
        /// <remarks>
        /// The value is clamped to Vista's supported range and rounded up to a multiple of 8 before storage because graph
        /// compute kernels expect resolutions aligned to that granularity.
        /// </remarks>
        public int baseResolution
        {
            get
            {
                return m_baseResolution;
            }
            set
            {
                m_baseResolution = Utilities.MultipleOf8(Mathf.Clamp(value, Constants.RES_MIN, Constants.RES_MAX));
            }
        }

        [SerializeField]
        protected int m_seed;
        /// <summary>
        /// Gets or sets the random seed forwarded into terrain graph generation for this biome.
        /// </summary>
        public int seed
        {
            get
            {
                return m_seed;
            }
            set
            {
                m_seed = value;
            }
        }

        /// <summary>
        /// Gets or sets whether the biome should capture a scene height texture and expose it to the terrain graph.
        /// </summary>
        /// <remarks>
        /// When enabled, <see cref="Graph.LPBInputProvider"/> asks the owning <see cref="VistaManager"/> to render the
        /// current scene height into an input texture named by Vista's graph constants before graph execution begins.
        /// </remarks>
        public bool shouldCollectSceneHeight
        {
            get
            {
                if (m_terrainGraph != null)
                {
                    return m_terrainGraph.HasSceneHeightInput();
                }
                return false;
            }
        }

        [SerializeField]
        protected int m_biomeMaskResolution;
        /// <summary>
        /// Gets or sets the resolution of the base biome mask and the editable biome-mask adjustment texture.
        /// </summary>
        /// <remarks>
        /// The value is clamped to Vista's supported range and rounded to a multiple of 8. When the resolution changes,
        /// existing <see cref="biomeMaskAdjustments"/> data is resampled with bilinear filtering so manual mask edits are
        /// preserved instead of discarded.
        /// </remarks>
        public int biomeMaskResolution
        {
            get
            {
                return m_biomeMaskResolution;
            }
            set
            {
                int oldRes = m_biomeMaskResolution;
                int newRes = Utilities.MultipleOf8(Mathf.Clamp(value, Constants.RES_MIN, Constants.RES_MAX));
                if (oldRes != newRes)
                {
                    m_biomeMaskResolution = newRes;
                    if (m_biomeMaskAdjustments != null && m_biomeMaskAdjustments.Length > 0)
                    {
                        m_biomeMaskAdjustments = Utilities.ResampleBilinear(m_biomeMaskAdjustments, oldRes, oldRes, newRes, newRes);
                    }
                }
            }
        }

        [SerializeField]
        protected BiomeMaskGraph m_biomeMaskGraph;
        /// <summary>
        /// Gets or sets an optional post-process graph that refines the generated biome mask.
        /// </summary>
        /// <remarks>
        /// When assigned, the base combined biome mask is fed into <c>BiomeMaskGraphUtilities.RequestData</c>, and the
        /// resulting mask replaces the original cache mask before the biome data is stored.
        /// </remarks>
        public BiomeMaskGraph biomeMaskGraph
        {
            get
            {
                return m_biomeMaskGraph;
            }
            set
            {
                m_biomeMaskGraph = value;
            }
        }

        /// <summary>
        /// Gets the biome bounds in world space.
        /// </summary>
        /// <remarks>
        /// The bounds are derived from anchor positions transformed by this object's transform. When
        /// <see cref="falloffDirection"/> is <see cref="FalloffDirection.Outer"/>, the expanded falloff polygon is used so
        /// overlap checks and cache generation cover the full fade region.
        /// </remarks>
        public Bounds worldBounds
        {
            get
            {
                return CalculateWorldBounds();
            }
        }

        protected long m_updateCounter;
        /// <summary>
        /// Gets or sets the biome change stamp used by the manager to detect invalidated state.
        /// </summary>
        /// <remarks>
        /// Extension helpers update this value with a timestamp-like counter whenever the biome changes and regeneration is
        /// required. After a successful manager pass, <see cref="VistaManager"/> overwrites it with the manager's own update
        /// counter so later comparisons can determine whether the biome is already in sync with that pass.
        /// </remarks>
        public long updateCounter
        {
            get
            {
                return m_updateCounter;
            }
            set
            {
                m_updateCounter = value;
            }
        }

        [SerializeField]
        protected Vector3[] m_anchors;
        /// <summary>
        /// Gets or sets the polygon vertices that define the core biome shape in biome-local space.
        /// </summary>
        /// <remarks>
        /// The getter returns a copy of the stored array. Assigning a new set of anchors recalculates
        /// <see cref="falloffAnchors"/> immediately so overlap checks and mask rendering stay in sync.
        /// </remarks>
        public Vector3[] anchors
        {
            get
            {
                Vector3[] clonedAnchors = new Vector3[m_anchors.Length];
                m_anchors.CopyTo(clonedAnchors, 0);
                return clonedAnchors;
            }
            set
            {
                if (value == null)
                {
                    m_anchors = new Vector3[0];
                }
                else
                {
                    m_anchors = new Vector3[value.Length];
                    value.CopyTo(m_anchors, 0);
                }
                RecalculateFalloffAnchors();
            }
        }

        [SerializeField]
        protected FalloffDirection m_falloffDirection;
        /// <summary>
        /// Gets or sets whether the falloff region expands outside the anchor polygon or shrinks inward.
        /// </summary>
        /// <remarks>
        /// Changing this value recomputes <see cref="falloffAnchors"/>. The selected direction also changes which polygon is
        /// treated as the biome's effective world bounds.
        /// </remarks>
        public FalloffDirection falloffDirection
        {
            get
            {
                return m_falloffDirection;
            }
            set
            {
                FalloffDirection oldValue = m_falloffDirection;
                FalloffDirection newValue = value;
                m_falloffDirection = newValue;
                if (oldValue != newValue)
                {
                    RecalculateFalloffAnchors();
                }
            }
        }

        [SerializeField]
        protected float m_falloffDistance;
        /// <summary>
        /// Gets or sets the distance used to build the falloff polygon from <see cref="anchors"/>.
        /// </summary>
        /// <remarks>
        /// Negative values are clamped to zero. Changing the distance recalculates <see cref="falloffAnchors"/>.
        /// </remarks>
        public float falloffDistance
        {
            get
            {
                return m_falloffDistance;
            }
            set
            {
                float oldValue = m_falloffDistance;
                float newValue = Mathf.Max(0, value);
                m_falloffDistance = newValue;
                if (oldValue != newValue)
                {
                    RecalculateFalloffAnchors();
                }
            }
        }

        [SerializeField]
        protected Vector3[] m_falloffAnchors;
        /// <summary>
        /// Gets the derived falloff polygon vertices in biome-local space.
        /// </summary>
        /// <remarks>
        /// The getter lazily rebuilds the array if it is missing or out of sync with <see cref="anchors"/>, then returns a
        /// cloned copy so callers cannot modify internal state by reference.
        /// </remarks>
        public Vector3[] falloffAnchors
        {
            get
            {
                if (m_falloffAnchors == null || m_falloffAnchors.Length != m_anchors.Length)
                {
                    RecalculateFalloffAnchors();
                }
                Vector3[] clonedFalloffAnchors = new Vector3[m_falloffAnchors.Length];
                m_falloffAnchors.CopyTo(clonedFalloffAnchors, 0);
                return clonedFalloffAnchors;
            }
        }

        /// <summary>
        /// Gets or sets the cached biome data generated in this biome's own bounds.
        /// </summary>
        /// <remarks>
        /// The cache owns GPU resources until <see cref="CleanUp"/> is called or the biome is regenerated. Tile requests do
        /// not return this object directly; they copy or remap from it into a separate request payload.
        /// </remarks>
        internal BiomeData cachedData { get; set; }

        private GraphExecutionCache m_graphExecutionCache;

        [System.Serializable]
        /// <summary>
        /// Defines clean up mode values.
        /// </summary>
        public enum CleanUpMode
        {
            /// <summary>
            /// Dispose cached biome data automatically after each manager generation pass.
            /// </summary>
            EachIteration,
            /// <summary>
            /// Keep cached biome data until <see cref="CleanUp"/> is called manually.
            /// </summary>
            Manually
        }

        [SerializeField]
        protected CleanUpMode m_cleanUpMode;
        /// <summary>
        /// Gets or sets when cached biome data should be released.
        /// </summary>
        /// <remarks>
        /// <see cref="CleanUpMode.EachIteration"/> frees the cache after each manager generation pass. Use
        /// <see cref="CleanUpMode.Manually"/> only when repeated remapping of the same biome cache is more valuable than the
        /// extra memory cost.
        /// </remarks>
        public CleanUpMode cleanUpMode
        {
            get
            {
                return m_cleanUpMode;
            }
            set
            {
                m_cleanUpMode = value;
            }
        }

        [SerializeField]
        protected float[] m_biomeMaskAdjustments;
        /// <summary>
        /// Gets or sets per-pixel adjustments applied to the generated base biome mask.
        /// </summary>
        /// <remarks>
        /// The array length must exactly match <c>biomeMaskResolution * biomeMaskResolution</c>. The getter returns a copy.
        /// If the stored array length is invalid, it is discarded and treated as empty.
        /// </remarks>
        /// <exception cref="System.ArgumentException">
        /// Thrown when the assigned array length does not match the current biome mask resolution.
        /// </exception>
        public float[] biomeMaskAdjustments
        {
            get
            {
                if (m_biomeMaskAdjustments.Length != m_biomeMaskResolution * m_biomeMaskResolution)
                {
                    m_biomeMaskAdjustments = new float[0];
                }

                float[] clonedData = new float[m_biomeMaskAdjustments.Length];
                m_biomeMaskAdjustments.CopyTo(clonedData, 0);
                return clonedData;
            }
            set
            {
                if (value == null)
                {
                    m_biomeMaskAdjustments = new float[0];
                }
                else if (value.Length != m_biomeMaskResolution * m_biomeMaskResolution)
                {
                    throw new System.ArgumentException("Wrong data dimension. Biome mask adjustment array length must be biomeMaskResolution^2");
                }
                else
                {
                    m_biomeMaskAdjustments = new float[value.Length];
                    value.CopyTo(m_biomeMaskAdjustments, 0);
                }
            }
        }

        [SerializeField]
        protected BiomeBlendOptions m_blendOptions;
        /// <summary>
        /// Gets or sets the per-output blending policy used when this biome overlaps others.
        /// </summary>
        /// <remarks>
        /// These settings are consumed by the multi-biome blend pipeline when cached results from several local biomes are
        /// merged into one tile payload.
        /// </remarks>
        public BiomeBlendOptions blendOptions
        {
            get
            {
                return m_blendOptions;
            }
            set
            {
                m_blendOptions = value;
            }
        }

        [SerializeField]
        protected TextureInput[] m_textureInputs = new TextureInput[0];
        /// <summary>
        /// Gets or sets custom texture inputs exposed to the terrain graph for this biome.
        /// </summary>
        /// <remarks>
        /// The getter returns a copy of the array. At request time, each valid input is copied into a temporary render
        /// texture and injected into the graph input container by name.
        /// </remarks>
        public TextureInput[] textureInputs
        {
            get
            {
                TextureInput[] clonedInputs = new TextureInput[m_textureInputs.Length];
                m_textureInputs.CopyTo(clonedInputs, 0);
                return clonedInputs;
            }
            set
            {
                if (value == null)
                {
                    m_textureInputs = new TextureInput[0];
                }
                else
                {
                    m_textureInputs = new TextureInput[value.Length];
                    value.CopyTo(m_textureInputs, 0);
                }
            }
        }

        [SerializeField]
        protected PositionInput[] m_positionInputs = new PositionInput[0];
        /// <summary>
        /// Gets or sets custom position-buffer inputs exposed to the terrain graph for this biome.
        /// </summary>
        /// <remarks>
        /// The getter returns a copy of the array. At request time, each valid position container is copied into a
        /// temporary graph buffer keyed by its configured input name.
        /// </remarks>
        public PositionInput[] positionInputs
        {
            get
            {
                PositionInput[] clonedInputs = new PositionInput[m_positionInputs.Length];
                m_positionInputs.CopyTo(clonedInputs, 0);
                return clonedInputs;
            }
            set
            {
                if (value == null)
                {
                    m_positionInputs = new PositionInput[0];
                }
                else
                {
                    m_positionInputs = new PositionInput[value.Length];
                    value.CopyTo(m_positionInputs, 0);
                }
            }
        }

        [SerializeField]
        internal string m_guid = Utilities.GenerateId();

        [SerializeField]
        internal PropertyOverride[] m_propertyOverrides = new PropertyOverride[0];
        /// <summary>
        /// Gets or sets exposed-property overrides applied when the terrain graph is cloned for this biome.
        /// </summary>
        /// <remarks>
        /// The getter returns a copy of the array. These overrides are used only when the graph exposes properties and the
        /// runtime has registered a clone-and-override callback; otherwise the source graph is executed directly and the
        /// overrides have no effect.
        /// </remarks>
        public PropertyOverride[] propertyOverrides
        {
            get
            {
                PropertyOverride[] clonedInputs = new PropertyOverride[m_propertyOverrides.Length];
                m_propertyOverrides.CopyTo(clonedInputs, 0);
                return clonedInputs;
            }
            set
            {
                if (value == null)
                {
                    m_propertyOverrides = new PropertyOverride[0];
                }
                else
                {
                    m_propertyOverrides = new PropertyOverride[value.Length];
                    value.CopyTo(m_propertyOverrides, 0);
                }
            }
        }

        /// <summary>
        /// Gets whether this biome is currently rebuilding its cached data.
        /// </summary>
        /// <remarks>
        /// This flag is raised only during the cache-generation portion of <see cref="RequestData"/> before the
        /// request-specific remap step runs.
        /// </remarks>
        internal bool isGeneratingCacheData { get; private set; }

        /// <summary>
        /// Restores the biome to Vista's default local-biome configuration.
        /// </summary>
        /// <remarks>
        /// The reset values define a square biome centered on the object, enable all output categories, use a 1024 base
        /// graph resolution, and clear all custom inputs and property overrides.
        /// </remarks>
        public void Reset()
        {
            m_order = 0;
            m_terrainGraph = null;
            m_space = Space.World;
            m_dataMask = (BiomeDataMask)(~0);
            m_baseResolution = 1024;
            m_seed = 0;

            m_biomeMaskResolution = 512;
            m_biomeMaskGraph = null;
            m_falloffDirection = FalloffDirection.Inner;
            m_falloffDistance = 100;
            m_anchors = new Vector3[]
            {
                new Vector3(-500, 0, -500), new Vector3(-500, 0, 500), new Vector3(500, 0, 500), new Vector3(500, 0, -500)
            };
            RecalculateFalloffAnchors();

            m_cleanUpMode = CleanUpMode.EachIteration;
            m_biomeMaskAdjustments = new float[0];

            m_blendOptions = BiomeBlendOptions.Default();
            m_textureInputs = new TextureInput[0];
            m_positionInputs = new PositionInput[0];
        }

        protected void OnEnable()
        {
            s_allInstances.Add(this);
            GraphAsset.graphChanged += OnGraphChanged;
            EnsureGraphExecutionCache();
        }

        protected void OnDisable()
        {
            s_allInstances.Remove(this);
            GraphAsset.graphChanged -= OnGraphChanged;
            CleanUp();
            DisposeGraphExecutionCache();
        }

        private GraphExecutionCache EnsureGraphExecutionCache()
        {
            if (m_graphExecutionCache == null)
            {
                m_graphExecutionCache = new GraphExecutionCache();
            }
            return m_graphExecutionCache;
        }

        private void DisposeGraphExecutionCache()
        {
            if (m_graphExecutionCache != null)
            {
                m_graphExecutionCache.Dispose();
                m_graphExecutionCache = null;
            }
        }

        protected void OnGraphChanged(GraphAsset graph)
        {
            if (graph != m_terrainGraph)
                return;
            CleanUp();
            this.MarkChanged();
            // GraphAsset.graphChanged is a static event, so when multiple biomes share the same graph,
            // every biome receives this callback on Save. GenerateBiomesInGroup calls GenerateAll which
            // already processes all tiles under the manager. Skip if a task is already running to avoid
            // the duplicate generation exception.
            if (!VistaManager.HasActiveTask())
            {
                this.GenerateBiomesInGroup();
            }
        }

        /// <summary>
        /// Creates a new biome GameObject in the current scene and optionally parents it to a manager.
        /// </summary>
        /// <param name="manager">
        /// Optional manager that should own the new biome. When supplied, the new object is parented under the manager and
        /// reset to local origin with identity rotation and unit scale.
        /// </param>
        /// <returns>The newly created biome component.</returns>
        public static LocalProceduralBiome CreateInstanceInScene(VistaManager manager)
        {
            GameObject biomeGO = new GameObject("Local Procedural Biome");
            LocalProceduralBiome biome = biomeGO.AddComponent<LocalProceduralBiome>();

            if (manager != null)
            {
                biome.transform.parent = manager.transform;
                biome.transform.localPosition = Vector3.zero;
                biome.transform.localRotation = Quaternion.identity;
                biome.transform.localScale = Vector3.one;
            }

            return biome;
        }

        /// <summary>
        /// Requests biome data for a target tile by remapping this biome's cached outputs into the requested bounds.
        /// </summary>
        /// <param name="worldBounds">
        /// Tile bounds that should receive the biome contribution. Cached data is copied from the biome's own world bounds
        /// into this destination area.
        /// </param>
        /// <param name="heightMapResolution">
        /// Target resolution for height-related outputs such as height, holes, and mesh density in the returned data.
        /// </param>
        /// <param name="textureResolution">
        /// Target resolution for texture-like outputs such as splat weights, albedo maps, density maps, and biome mask.
        /// </param>
        /// <returns>
        /// A progressive request whose <see cref="BiomeDataRequest.data"/> payload is filled asynchronously. If
        /// <see cref="terrainGraph"/> is not assigned, the returned request completes immediately with an empty data object.
        /// </returns>
        /// <remarks>
        /// The first request after cache invalidation triggers full graph execution in the biome's own bounds at
        /// <see cref="baseResolution"/>. Later requests reuse <see cref="cachedData"/> and only perform the bounds-aware
        /// copy/remap step. The cache bounds are rounded to whole-world-unit XZ extents before generation so repeated
        /// requests use a stable cache domain.
        /// </remarks>
        public BiomeDataRequest RequestData(Bounds worldBounds, int heightMapResolution, int textureResolution)
        {
            BiomeDataRequest request = new BiomeDataRequest();
            BiomeData data = new BiomeData();
            request.data = data;
            if (m_terrainGraph != null)
            {
                CoroutineUtility.StartCoroutine(RequestDataProgressive(request, worldBounds, heightMapResolution, textureResolution));
                return request;
            }
            else
            {
                request.Complete();
                return request;
            }
        }

        private IEnumerator RequestDataProgressive(BiomeDataRequest request, Bounds worldBounds, int heightMapResolution, int textureResolution)
        {
            VistaDebugger.OpenScope($"Request biome data: {name}", DebugScopeType.Custom);

            Bounds biomeWorldBoundsInt = this.worldBounds;
            Vector3 boundsCenter = biomeWorldBoundsInt.center;
            boundsCenter.x = Mathf.Round(boundsCenter.x);
            boundsCenter.z = Mathf.Round(boundsCenter.z);
            Vector3 boundsSize = biomeWorldBoundsInt.size;
            boundsSize.x = Mathf.Round(boundsSize.x);
            boundsSize.y = worldBounds.size.y;
            boundsSize.z = Mathf.Round(boundsSize.z);

            biomeWorldBoundsInt.center = boundsCenter;
            biomeWorldBoundsInt.size = boundsSize;

            //If it is a fresh generation(no cache), then generate cache data in the biome self bounds
            if (cachedData == null)
            {
                isGeneratingCacheData = true;

                BiomeDataRequest cacheDataRequest = new BiomeDataRequest();
                BiomeData cache = new BiomeData();
                cacheDataRequest.data = cache;

                GraphInputContainer inputContainer = new GraphInputContainer();
                LPBInputProvider inputProvider = new LPBInputProvider(this);
                // SetInput allocates the biome mask internally via RenderPostProcessedBiomeMask.
                // Ownership stays with the provider until RemoveTexture is called below.
                inputProvider.SetInput(inputContainer);

                TerrainGraph graphToExecute;
                if (m_terrainGraph.HasExposedProperties && cloneAndOverrideGraphCallback != null)
                {
                    graphToExecute = cloneAndOverrideGraphCallback.Invoke(terrainGraph, m_propertyOverrides);
                }
                else
                {
                    graphToExecute = m_terrainGraph;
                }

                CoroutineUtility.StartCoroutine(TerrainGraphUtilities.RequestBiomeData(this, cacheDataRequest, graphToExecute, biomeWorldBoundsInt, space, m_baseResolution, m_seed, inputContainer, m_dataMask, inputProvider.FillTerrainGraphArguments, EnsureGraphExecutionCache()));
                yield return cacheDataRequest;

                // Transfer biome mask ownership from the input provider to BiomeData for blending.
                // RemoveTexture detaches it from m_textures so CleanUp below will not dispose it.
                cacheDataRequest.data.biomeMaskMap = inputProvider.RemoveTexture(GraphConstants.BIOME_MASK_INPUT_NAME);
                cachedData = cacheDataRequest.data;

                if (graphToExecute != m_terrainGraph)
                {
                    Object.DestroyImmediate(graphToExecute);
                }
                inputProvider.CleanUp();
                isGeneratingCacheData = false;
            }

            //Copy cache data from the biome self bounds to the target world bounds
            BiomeDataUtilities.Copy(cachedData, biomeWorldBoundsInt, request.data, worldBounds, heightMapResolution, textureResolution);
            request.Complete();

            VistaDebugger.CloseScope();
            yield break;
        }

        /// <summary>
        /// Tests whether the biome's effective polygon overlaps a world-space rectangular area.
        /// </summary>
        /// <param name="area">The world-space bounds of the area to test.</param>
        /// <returns>
        /// <see langword="true"/> when the biome polygon overlaps the rectangle formed by <paramref name="area"/> in the XZ
        /// plane; otherwise, <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// The check is purely planar. It ignores Y extent and uses <see cref="falloffAnchors"/> rather than the raw
        /// anchors, so the fade region participates in overlap tests.
        /// </remarks>
        public bool IsOverlap(Bounds area)
        {
            Vector2[] biomeVertices = new Vector2[falloffAnchors.Length];
            for (int i = 0; i < biomeVertices.Length; ++i)
            {
                biomeVertices[i] = transform.TransformPoint(falloffAnchors[i]).XZ();
            }
            Polygon2D biomePolygon = new Polygon2D(biomeVertices);

            Vector2[] areaVertices = new Vector2[4];
            areaVertices[0] = new Vector2(area.min.x, area.min.z);
            areaVertices[1] = new Vector2(area.min.x, area.max.z);
            areaVertices[2] = new Vector2(area.max.x, area.max.z);
            areaVertices[3] = new Vector2(area.max.x, area.min.z);
            Polygon2D areaPolygon = new Polygon2D(areaVertices);

            return Polygon2D.IsOverlap(biomePolygon, areaPolygon);
        }

        /// <summary>
        /// Rebuilds the falloff polygon from the current anchor polygon, falloff distance, and falloff direction.
        /// </summary>
        /// <remarks>
        /// This method should be called after low-level edits to the serialized anchor data. Property setters already call
        /// it when required.
        /// </remarks>
        public void RecalculateFalloffAnchors()
        {
            if (m_anchors == null)
            {
                m_falloffAnchors = null;
            }
            else
            {
                m_falloffAnchors = AnchorUtilities.GetFalloff(m_anchors, m_falloffDistance, m_falloffDirection);
            }
        }

        /// <summary>
        /// Calculates the biome's world-space bounding box from its authored polygon.
        /// </summary>
        /// <returns>The axis-aligned world-space bounds enclosing the effective biome polygon.</returns>
        /// <remarks>
        /// When <see cref="falloffDirection"/> is <see cref="FalloffDirection.Outer"/>, the expanded falloff polygon is used;
        /// otherwise the raw anchor polygon defines the bounds. The method includes transformed Y values from the authored
        /// vertices, even though overlap tests are performed in XZ space only.
        /// </remarks>
        protected Bounds CalculateWorldBounds()
        {
            Bounds worldBounds;
            Vector3[] outerAnchors = m_falloffDirection == FalloffDirection.Outer ? m_falloffAnchors : m_anchors;
            if (outerAnchors == null || outerAnchors.Length == 0)
            {
                worldBounds = new Bounds(Vector3.zero, Vector3.zero);
            }
            else
            {
                float minX = float.MaxValue;
                float minY = float.MaxValue;
                float minZ = float.MaxValue;
                float maxX = float.MinValue;
                float maxY = float.MinValue;
                float maxZ = float.MinValue;
                foreach (Vector3 a in outerAnchors)
                {
                    Vector3 worldPos = transform.TransformPoint(a);
                    minX = Mathf.Min(minX, worldPos.x);
                    minY = Mathf.Min(minY, worldPos.y);
                    minZ = Mathf.Min(minZ, worldPos.z);

                    maxX = Mathf.Max(maxX, worldPos.x);
                    maxY = Mathf.Max(maxY, worldPos.y);
                    maxZ = Mathf.Max(maxZ, worldPos.z);
                }
                Vector3 center = new Vector3(minX + maxX, minY + maxY, minZ + maxZ) * 0.5f;
                Vector3 size = new Vector3(maxX - minX, maxY - minY, maxZ - minZ);

                worldBounds = new Bounds(center, size);
            }

            return worldBounds;
        }

        /// <summary>
        /// Releases the cached biome data owned by this biome.
        /// </summary>
        /// <remarks>
        /// This disposes GPU resources held by <see cref="cachedData"/> and clears the cache reference. It is invoked on
        /// disable, on graph changes, and optionally after each manager generation pass depending on
        /// <see cref="cleanUpMode"/>. The method does not modify biome inputs, masks, or authored settings.
        /// </remarks>
        public void CleanUp()
        {
            if (cachedData != null)
            {
                cachedData.Dispose();
                cachedData = null;
            }
        }

        /// <summary>
        /// Called by the manager before tile generation begins.
        /// </summary>
        /// <remarks>
        /// The current implementation performs no work here, but the method is part of the biome lifecycle contract and is
        /// available for future extension.
        /// </remarks>
        public void OnBeforeVMGenerate()
        {

        }

        /// <summary>
        /// Called by the manager after tile generation finishes.
        /// </summary>
        /// <remarks>
        /// When <see cref="cleanUpMode"/> is <see cref="CleanUpMode.EachIteration"/>, this method disposes the cached biome
        /// data so the next generation pass starts from a fresh cache.
        /// </remarks>
        public void OnAfterVMGenerate()
        {
            if (m_cleanUpMode == CleanUpMode.EachIteration)
            {
                CleanUp();
            }
        }

        /// <summary>
        /// Renders the authored biome polygon and falloff into a new mask texture.
        /// </summary>
        /// <returns>
        /// A newly created RFloat render texture at <see cref="biomeMaskResolution"/> containing the procedural base biome
        /// mask before manual adjustments are applied.
        /// </returns>
        /// <remarks>
        /// The raw anchor polygon defines the solid biome area, while <see cref="falloffAnchors"/> and
        /// <see cref="falloffDirection"/> define how the mask fades at the boundary. The returned texture is owned by the
        /// caller.
        /// </remarks>
        internal RenderTexture RenderBaseBiomeMask()
        {
            Bounds b = worldBounds;
            Vector2[] vertices = new Vector2[m_anchors.Length];
            for (int i = 0; i < vertices.Length; ++i)
            {
                Vector3 worldPoint = transform.TransformPoint(m_anchors[i]);
                Vector2 v = new Vector2
                (
                    (worldPoint.x - b.min.x) / (b.max.x - b.min.x),
                    (worldPoint.z - b.min.z) / (b.max.z - b.min.z)
                );
                vertices[i] = v;
            }

            Vector2[] falloffVertices = new Vector2[m_falloffAnchors.Length];
            for (int i = 0; i < falloffVertices.Length; ++i)
            {
                Vector3 worldPoint = transform.TransformPoint(m_falloffAnchors[i]);
                Vector2 v = new Vector2
                (
                    (worldPoint.x - b.min.x) / (b.max.x - b.min.x),
                    (worldPoint.z - b.min.z) / (b.max.z - b.min.z)
                );
                falloffVertices[i] = v;
            }

            PolygonMaskRenderer.Configs maskRendererConfigs = new PolygonMaskRenderer.Configs();
            maskRendererConfigs.vertices = vertices;
            maskRendererConfigs.falloffVertices = falloffVertices;
            maskRendererConfigs.falloffTexture = null;
            maskRendererConfigs.falloffDirection = m_falloffDirection;

            RenderTexture biomeMask = new RenderTexture(m_biomeMaskResolution, m_biomeMaskResolution, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
            biomeMask.name = $"{gameObject.name} - Biome Mask";
            biomeMask.wrapMode = TextureWrapMode.Clamp;
            biomeMask.filterMode = FilterMode.Bilinear;
            biomeMask.enableRandomWrite = true;
            biomeMask.antiAliasing = 1;
            biomeMask.Create();
            PolygonMaskRenderer.Render(biomeMask, maskRendererConfigs);

            return biomeMask;
        }

        /// <summary>
        /// Renders the base biome mask and applies any serialized mask adjustments.
        /// </summary>
        /// <returns>
        /// A newly created biome mask texture suitable for injection into terrain-graph inputs or biome-mask post-process
        /// graphs.
        /// </returns>
        /// <remarks>
        /// When <see cref="biomeMaskAdjustments"/> contains data, the method builds a temporary texture from that float
        /// array, combines it with the procedural base mask, and destroys the temporary CPU-generated texture before
        /// returning.
        /// </remarks>
        internal RenderTexture RenderCombinedBiomeMask()
        {
            RenderTexture baseMask = RenderBaseBiomeMask();
            if (m_biomeMaskAdjustments != null && m_biomeMaskAdjustments.Length > 0)
            {
                Texture2D adjustmentTex = Utilities.TextureFromFloats(m_biomeMaskAdjustments, m_biomeMaskResolution, m_biomeMaskResolution);
                LPBUtilities.CombineBiomeMask(baseMask, adjustmentTex);
                Object.DestroyImmediate(adjustmentTex);
            }
            return baseMask;
        }

        /// <summary>
        /// Renders the final biome mask for use by both the Terrain Graph input and the biome blending pipeline.
        /// </summary>
        /// <remarks>
        /// Starts from the combined polygon mask (anchors plus any manual paint adjustments). If a Biome Mask Graph is
        /// assigned, runs it immediately via <see cref="TerrainGraph.ExecuteImmediate"/> and returns the processed result,
        /// disposing the intermediate combined mask. The caller owns the returned texture and is responsible for releasing it.
        /// </remarks>
        internal RenderTexture RenderPostProcessedBiomeMask()
        {
            // Allocation point for the biome mask. A fresh RenderTexture is created every call.
            // Ownership passes to the caller (LPBInputProvider), which tracks it until
            // RequestDataProgressive transfers it to BiomeData.biomeMaskMap via RemoveTexture.
            RenderTexture combinedMask = RenderCombinedBiomeMask();
            if (m_biomeMaskGraph == null)
            {
                return combinedMask;
            }

            List<OutputNode> outputNodes = m_biomeMaskGraph.GetNodesOfType<OutputNode>();
            OutputNode targetOutputNode = outputNodes.Find(n => GraphConstants.BIOME_MASK_OUTPUT_NAME.Equals(n.outputName));
            if (targetOutputNode == null)
            {
                return combinedMask;
            }

            Bounds selfWorldBounds = worldBounds;
            TerrainGenerationConfigs configs = new TerrainGenerationConfigs();
            configs.resolution = combinedMask.width;
            configs.seed = 0;
            configs.terrainHeight = selfWorldBounds.size.y;
            configs.worldBounds = new Rect(
                space == Space.World ? selfWorldBounds.min.x : 0,
                space == Space.World ? selfWorldBounds.min.z : 0,
                selfWorldBounds.size.x,
                selfWorldBounds.size.z);

            GraphInputContainer inputContainer = new GraphInputContainer();
            inputContainer.AddTexture(GraphConstants.BIOME_MASK_INPUT_NAME, combinedMask);

            DataPool data = m_biomeMaskGraph.ExecuteImmediate(new string[] { targetOutputNode.id }, configs, inputContainer);
            RenderTexture processedMask = data.RemoveRTFromPool(new SlotRef(targetOutputNode.id, targetOutputNode.mainOutputSlot.slotId));
            data.Dispose();

            combinedMask.Release();
            Object.DestroyImmediate(combinedMask);

            return processedMask != null ? processedMask : RenderCombinedBiomeMask();
        }

        /// <summary>
        /// Renders the current scene height inside this biome's world bounds into a temporary texture.
        /// </summary>
        /// <returns>
        /// A newly created RFloat render texture at <see cref="baseResolution"/> containing scene height data. If no
        /// manager can be resolved, the texture is returned unchanged after allocation.
        /// </returns>
        /// <remarks>
        /// The texture is typically consumed only as a temporary graph input during biome cache generation and should be
        /// disposed by the caller or the input provider that created it.
        /// </remarks>
        public virtual RenderTexture RenderSceneHeightMap()
        {
            RenderTexture sceneHeightMap = new RenderTexture(m_baseResolution, m_baseResolution, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
            sceneHeightMap.name = $"{gameObject.name} - Scene Height Map";
            sceneHeightMap.wrapMode = TextureWrapMode.Clamp;
            sceneHeightMap.filterMode = FilterMode.Bilinear;
            sceneHeightMap.enableRandomWrite = true;
            sceneHeightMap.antiAliasing = 1;
            sceneHeightMap.Create();
            GraphicsUtils.ClearWithZeros(sceneHeightMap);

            VistaManager vm = this.GetVistaManagerInstance();
            if (vm != null)
            {
                vm.CollectSceneHeight(sceneHeightMap, worldBounds);
            }

            return sceneHeightMap;
        }

        /// <summary>
        /// Ensures the biome instance has a persistent GUID before serialization.
        /// </summary>
        /// <remarks>
        /// The GUID is used by <see cref="Graph.LPBInputProvider"/> to reconnect serialized helper objects back to the live
        /// biome instance after reload.
        /// </remarks>
        public void OnBeforeSerialize()
        {
            if (string.IsNullOrEmpty(m_guid))
            {
                m_guid = Utilities.GenerateId();
            }
        }

        /// <summary>
        /// Receives Unity's deserialization callback.
        /// </summary>
        /// <remarks>
        /// No post-deserialization repair is currently required here. Runtime reconnection is handled lazily through the
        /// biome GUID when helper objects query <see cref="allInstances"/>.
        /// </remarks>
        public void OnAfterDeserialize()
        {
        }
    }
}
#endif


