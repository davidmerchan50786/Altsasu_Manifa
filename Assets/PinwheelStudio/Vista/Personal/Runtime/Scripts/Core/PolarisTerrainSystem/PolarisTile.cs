#if VISTA
#if GRIFFIN
using Pinwheel.Griffin;
using Pinwheel.Vista.Graphics;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Pinwheel.Vista.PolarisTerrain
{
    [RequireComponent(typeof(GStylizedTerrain))]
    [ExecuteInEditMode]
    [AddComponentMenu("Vista/Polaris Tile")]
    /// <summary>
    /// Represents polaris tile.
    /// </summary>
    public class PolarisTile : MonoBehaviour, ITile, IGeometryPopulator, IAlbedoMapPopulator, IMetallicMapPopulator, ILayerWeightsPopulator, ITreePopulator, IDetailInstancePopulator, IObjectPopulator, IGenericTexturePopulator, IGenericBufferPopulator, ISceneHeightProvider, ISerializationCallbackReceiver
    {
        private const string GENERATED_SPLAT_GROUP_NAME = "~GeneratedSplatGroup";
        private const string GENERATED_TREE_GROUP_NAME = "~GeneratedTreeGroup";
        private const string GENERATED_GRASS_GROUP_NAME = "~GeneratedGrassGroup";

        /// <summary>
        /// Occurs when populate generic textures callback.
        /// </summary>
        public event PopulateGenericTexturesHandler populateGenericTexturesCallback;
        /// <summary>
        /// Occurs when populate generic buffers callback.
        /// </summary>
        public event PopulateGenericBuffersHandler populateGenericBuffersCallback;
        /// <summary>
        /// Occurs when populate prefab instance callback.
        /// </summary>
        public event PopulatePrefabHandler populatePrefabInstanceCallback;

        [SerializeField]
        private string m_managerId;
        /// <summary>
        /// Gets or sets member.
        /// </summary>
        public string managerId
        {
            get
            {
                return m_managerId;
            }
            set
            {
                m_managerId = value;
            }
        }

        /// <summary>
        /// Gets or sets terrain.
        /// </summary>
        public GStylizedTerrain terrain { get; private set; }

        /// <summary>
        /// Gets or sets member.
        /// </summary>
        public Bounds worldBounds
        {
            get
            {
                return terrain.Bounds;
            }
        }

        /// <summary>
        /// Gets or sets member.
        /// </summary>
        public float maxHeight
        {
            get
            {
                if (terrain != null && terrain.TerrainData != null)
                {
                    return terrain.TerrainData.Geometry.Height;
                }
                else
                {
                    return 0;
                }
            }
            set
            {
                if (terrain != null && terrain.TerrainData != null)
                {
                    terrain.TerrainData.Geometry.Height = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets member.
        /// </summary>
        public int heightMapResolution
        {
            get
            {
                if (terrain != null && terrain.TerrainData != null)
                {
                    return terrain.TerrainData.Geometry.HeightMapResolution;
                }
                else
                {
                    return 0;
                }
            }
            set
            {
                if (terrain != null && terrain.TerrainData != null)
                {
                    terrain.TerrainData.Geometry.HeightMapResolution = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets member.
        /// </summary>
        public int textureResolution
        {
            get
            {
                if (terrain != null && terrain.TerrainData != null)
                {
                    return terrain.TerrainData.Shading.SplatControlResolution;
                }
                else
                {
                    return 0;
                }
            }
            set
            {
                if (terrain != null && terrain.TerrainData != null)
                {
                    terrain.TerrainData.Shading.SplatControlResolution = value;
                    terrain.TerrainData.Shading.AlbedoMapResolution = value;
                    terrain.TerrainData.Shading.MetallicMapResolution = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets member.
        /// </summary>
        public int detailDensityMapResolution
        {
            get
            {
                return 0;
            }
            set
            {

            }
        }

        [SerializeField]
        private List<GSplatPrototype> m_splatPrototypesSerialized;
        [SerializeField]
        private List<GTreePrototype> m_treePrototypesSerialized;
        [SerializeField]
        private List<GGrassPrototype> m_grassPrototypesSerialized;

        [SerializeField]
        private TerrainLayer[] m_terrainLayers;
        /// <summary>
        /// Gets or sets member.
        /// </summary>
        public TerrainLayer[] terrainLayers
        {
            get
            {
                return m_terrainLayers;
            }
        }

        private void OnEnable()
        {
            terrain = GetComponent<GStylizedTerrain>();
            VistaManager.collectTiles += OnCollectTiles;
            DeserializePrototypes();
        }

        private void OnDisable()
        {
            VistaManager.collectTiles -= OnCollectTiles;
        }

        private void OnCollectTiles(VistaManager manager, Collector<ITile> tiles)
        {
            if (string.Equals(manager.id, m_managerId) && terrain != null && terrain.TerrainData != null)
            {
                tiles.Add(this);
            }
        }

        /// <summary>
        /// Handles the before applying data callback.
        /// </summary>
        public void OnBeforeApplyingData()
        {
        }

        /// <summary>
        /// Handles the after applying data callback.
        /// </summary>
        public void OnAfterApplyingData()
        {
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(terrain.TerrainData);
#endif
        }

        /// <summary>
        /// Populates height map.
        /// </summary>
        /// <param name="heightMap">Height map texture input.</param>
        public void PopulateHeightMap(RenderTexture heightMap)
        {
            int resolution = terrain.TerrainData.Geometry.HeightMapResolution;
            RenderTexture destHeightMap = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            PolarisTileUtilities.SetHeightMap(terrain.TerrainData.Geometry.HeightMap, heightMap, destHeightMap);

            GraphicsUtils.ReadRenderTexture(destHeightMap, terrain.TerrainData.Geometry.HeightMap);
            destHeightMap.Release();
            Object.DestroyImmediate(destHeightMap);
        }

        /// <summary>
        /// Clears the Polaris height channel by writing a zero-valued Vista height map.
        /// </summary>
        public void ClearHeightMap()
        {
            int resolution = terrain.TerrainData.Geometry.HeightMapResolution;
            RenderTexture zeroHeightMap = RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
            GraphicsUtils.ClearWithZeros(zeroHeightMap);
            PopulateHeightMap(zeroHeightMap);
            RenderTexture.ReleaseTemporary(zeroHeightMap);
        }

        /// <summary>
        /// Populates hole map.
        /// </summary>
        /// <param name="holeMap">Hole map texture input.</param>
        public void PopulateHoleMap(RenderTexture holeMap)
        {
            int resolution = terrain.TerrainData.Geometry.HeightMapResolution;
            RenderTexture destHeightMap = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            PolarisTileUtilities.SetHoleMap(terrain.TerrainData.Geometry.HeightMap, holeMap, destHeightMap);

            GraphicsUtils.ReadRenderTexture(destHeightMap, terrain.TerrainData.Geometry.HeightMap);
            destHeightMap.Release();
            Object.DestroyImmediate(destHeightMap);
        }

        /// <summary>
        /// Clears the Polaris hole channel by writing a zero-valued Vista hole map.
        /// </summary>
        public void ClearHoleMap()
        {
            int resolution = terrain.TerrainData.Geometry.HeightMapResolution;
            RenderTexture zeroHoleMap = RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
            GraphicsUtils.ClearWithZeros(zeroHoleMap);
            PopulateHoleMap(zeroHoleMap);
            RenderTexture.ReleaseTemporary(zeroHoleMap);
        }

        /// <summary>
        /// Populates mesh density map.
        /// </summary>
        /// <param name="meshDensityMap">Mesh density map texture input.</param>
        public void PopulateMeshDensityMap(RenderTexture meshDensityMap)
        {
            int resolution = terrain.TerrainData.Geometry.HeightMapResolution;
            RenderTexture destHeightMap = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            PolarisTileUtilities.SetMeshDensityMap(terrain.TerrainData.Geometry.HeightMap, meshDensityMap, destHeightMap);

            GraphicsUtils.ReadRenderTexture(destHeightMap, terrain.TerrainData.Geometry.HeightMap);
            destHeightMap.Release();
            Object.DestroyImmediate(destHeightMap);
        }

        /// <summary>
        /// Clears the Polaris mesh-density channel by writing a zero-valued Vista density map.
        /// </summary>
        public void ClearMeshDensityMap()
        {
            int resolution = terrain.TerrainData.Geometry.HeightMapResolution;
            RenderTexture zeroDensityMap = RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
            GraphicsUtils.ClearWithZeros(zeroDensityMap);
            PopulateMeshDensityMap(zeroDensityMap);
            RenderTexture.ReleaseTemporary(zeroDensityMap);
        }

        /// <summary>
        /// Updates geometry.
        /// </summary>
        public void UpdateGeometry()
        {
            terrain.TerrainData.Geometry.SetRegionDirty(GCommon.UnitRect);
            terrain.TerrainData.SetDirty(GTerrainData.DirtyFlags.Geometry);
        }

        /// <summary>
        /// Matches seams.
        /// </summary>
        public void MatchSeams()
        {
            terrain.MatchEdges();
        }

        /// <summary>
        /// Populates albedo map.
        /// </summary>
        /// <param name="albedoMap">Albedo map texture input.</param>
        public void PopulateAlbedoMap(RenderTexture albedoMap)
        {
            int resolution = terrain.TerrainData.Shading.AlbedoMapResolution;
            if (resolution == albedoMap.width && resolution == albedoMap.height)
            {
                GraphicsUtils.ReadRenderTexture(albedoMap, terrain.TerrainData.Shading.AlbedoMap);
            }
            else
            {
                RenderTexture scaledAlbedo = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Drawing.Blit(albedoMap, scaledAlbedo);

                GraphicsUtils.ReadRenderTexture(scaledAlbedo, terrain.TerrainData.Shading.AlbedoMap);

                scaledAlbedo.Release();
                Object.DestroyImmediate(scaledAlbedo);
            }

            terrain.TerrainData.SetDirty(GTerrainData.DirtyFlags.Shading);
        }

        /// <summary>
        /// Clears the Polaris albedo map by writing a zero-valued texture.
        /// </summary>
        public void ClearAlbedoMap()
        {
            int resolution = terrain.TerrainData.Shading.AlbedoMapResolution;
            RenderTexture zeroAlbedoMap = RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            GraphicsUtils.ClearWithZeros(zeroAlbedoMap);
            PopulateAlbedoMap(zeroAlbedoMap);
            RenderTexture.ReleaseTemporary(zeroAlbedoMap);
        }

        /// <summary>
        /// Populates metallic map.
        /// </summary>
        /// <param name="metallicMap">Metallic map texture input.</param>
        public void PopulateMetallicMap(RenderTexture metallicMap)
        {
            int resolution = terrain.TerrainData.Shading.MetallicMapResolution;
            if (resolution == metallicMap.width && resolution == metallicMap.height)
            {
                GraphicsUtils.ReadRenderTexture(metallicMap, terrain.TerrainData.Shading.MetallicMap);
            }
            else
            {
                RenderTexture scaledMetallicMap = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Drawing.Blit(metallicMap, scaledMetallicMap);

                GraphicsUtils.ReadRenderTexture(scaledMetallicMap, terrain.TerrainData.Shading.MetallicMap);

                scaledMetallicMap.Release();
                Object.DestroyImmediate(scaledMetallicMap);
            }
            terrain.TerrainData.SetDirty(GTerrainData.DirtyFlags.Shading);
        }

        /// <summary>
        /// Clears the Polaris metallic map by writing a zero-valued texture.
        /// </summary>
        public void ClearMetallicMap()
        {
            int resolution = terrain.TerrainData.Shading.MetallicMapResolution;
            RenderTexture zeroMetallicMap = RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            GraphicsUtils.ClearWithZeros(zeroMetallicMap);
            PopulateMetallicMap(zeroMetallicMap);
            RenderTexture.ReleaseTemporary(zeroMetallicMap);
        }

        /// <summary>
        /// Populates layer weights.
        /// </summary>
        /// <param name="layers">Terrain layers corresponding to weight textures.</param>
        /// <param name="weights">Layer-weight textures.</param>
        public void PopulateLayerWeights(List<TerrainLayer> layers, List<RenderTexture> weights)
        {
            List<TerrainLayer> distinctLayers;
            List<RenderTexture> alphaMaps;
            int resolution = textureResolution;

            AlphaMapsCombiner combiner = new AlphaMapsCombiner();
            combiner.CombineAndMerge(layers, weights, resolution, out distinctLayers, out alphaMaps);

            m_terrainLayers = distinctLayers.ToArray();

            List<GSplatPrototype> prototypes = new List<GSplatPrototype>();
            foreach (TerrainLayer l in m_terrainLayers)
            {
                prototypes.Add((GSplatPrototype)l);
            }
            GSplatPrototypeGroup splatGroup = CreateSplatGroup(prototypes);
            SetSplatGroup(splatGroup);

#if __MICROSPLAT_POLARIS__ 
            JBooth.MicroSplat.TextureArrayConfig cfg = terrain.TerrainData.Shading.MicroSplatTextureArrayConfig;
            while (cfg!=null && cfg.sourceTextures.Count < m_terrainLayers.Length)
            {
                cfg.sourceTextures.Add(new JBooth.MicroSplat.TextureArrayConfig.TextureEntry());
            }
#endif

            for (int i = 0; i < alphaMaps.Count; ++i)
            {
                Texture2D alphaMap = terrain.TerrainData.Shading.GetSplatControl(i);
                GraphicsUtils.ReadRenderTexture(alphaMaps[i], alphaMap);
            }

            for (int i = 0; i < alphaMaps.Count; ++i)
            {
                alphaMaps[i].Release();
                Object.DestroyImmediate(alphaMaps[i]);
            }
        }

        /// <summary>
        /// Clears all Polaris splat weights and assigned terrain layers from this tile.
        /// </summary>
        public void ClearLayerWeights()
        {
            m_terrainLayers = System.Array.Empty<TerrainLayer>();
            GSplatPrototypeGroup splatGroup = CreateSplatGroup(new List<GSplatPrototype>());
            SetSplatGroup(splatGroup);
            terrain.TerrainData.Shading.RemoveSplatControlMaps();
        }

        private void SetSplatGroup(GSplatPrototypeGroup splatGroup)
        {
            GSplatPrototypeGroup currentGroup = terrain.TerrainData.Shading.Splats;
            if (currentGroup != null && string.Equals(currentGroup.name, GENERATED_SPLAT_GROUP_NAME))
            {
                Object.DestroyImmediate(currentGroup);
            }
            terrain.TerrainData.Shading.Splats = splatGroup;
        }

        private GSplatPrototypeGroup CreateSplatGroup(List<GSplatPrototype> prototypes)
        {
            GSplatPrototypeGroup splatGroup = ScriptableObject.CreateInstance<GSplatPrototypeGroup>();
            splatGroup.name = GENERATED_SPLAT_GROUP_NAME;
            splatGroup.Prototypes = prototypes;
            return splatGroup;
        }

        /// <summary>
        /// Populates trees.
        /// </summary>
        /// <param name="templates">Template collection used by this operation.</param>
        /// <param name="buffers">Compute buffer collection used by this operation.</param>
        public void PopulateTrees(List<TreeTemplate> templates, List<ComputeBuffer> buffers)
        {
            List<TreeTemplate> distinctTemplates = templates.Distinct().ToList();
            int[] templateIndices = new int[templates.Count];
            for (int i = 0; i < templates.Count; ++i)
            {
                templateIndices[i] = distinctTemplates.IndexOf(templates[i]);
            }

            List<GTreePrototype> prototypes = new List<GTreePrototype>();
            int[] minProtoIndices = new int[distinctTemplates.Count];
            int[] maxProtoIndices = new int[distinctTemplates.Count];
            int baseProtoIndex = 0;
            for (int i = 0; i < distinctTemplates.Count; ++i)
            {
                TreeTemplate template = distinctTemplates[i];
                List<GTreePrototype> prototypesFromTemplates = CreateTreePrototypesFromTemplate(template);
                prototypes.AddRange(prototypesFromTemplates);

                int minProtoIndex = baseProtoIndex;
                int maxProtoIndex = baseProtoIndex + prototypesFromTemplates.Count - 1;
                minProtoIndices[i] = minProtoIndex;
                maxProtoIndices[i] = maxProtoIndex;

                baseProtoIndex += prototypesFromTemplates.Count;
            }

            List<GTreeInstance> instances = new List<GTreeInstance>();
            for (int i = 0; i < buffers.Count; ++i)
            {
                ComputeBuffer buffer = buffers[i];
                int tIndex = templateIndices[i];
                int minProtoIndex = minProtoIndices[tIndex];
                int maxProtoIndex = maxProtoIndices[tIndex];
                ParseTreeInstances(instances, buffer, minProtoIndex, maxProtoIndex);
            }

            GTreePrototypeGroup treeGroup = CreateTreeGroup(prototypes);
            SetTreeGroup(treeGroup);
            terrain.TerrainData.Foliage.TreeInstances = instances;
            terrain.TerrainData.Foliage.SetTreeRegionDirty(new Rect(0, 0, 1, 1));
            terrain.UpdateTreesPosition();
            terrain.TerrainData.Foliage.ClearTreeDirtyRegions();
        }

        /// <summary>
        /// Clears all Polaris tree prototypes and instances from this tile.
        /// </summary>
        public void ClearTrees()
        {
            GTreePrototypeGroup treeGroup = CreateTreeGroup(new List<GTreePrototype>());
            SetTreeGroup(treeGroup);
            terrain.TerrainData.Foliage.ClearTreeInstances();
            terrain.TerrainData.Foliage.SetTreeRegionDirty(new Rect(0, 0, 1, 1));
            terrain.TerrainData.Foliage.ClearTreeDirtyRegions();
        }

        private List<GTreePrototype> CreateTreePrototypesFromTemplate(TreeTemplate template)
        {
            List<GTreePrototype> prototypes = new List<GTreePrototype>();
            if (template.prefab == null)
            {
                return prototypes;
            }

            List<GameObject> prefabs = new List<GameObject>();
            prefabs.Add(template.prefab);
            GameObject[] variants = template.prefabVariants;
            foreach (GameObject v in variants)
            {
                if (v != null)
                {
                    prefabs.Add(v);
                }
            }

            foreach (GameObject p in prefabs)
            {
                GTreePrototype proto = new GTreePrototype();
                proto.Prefab = p;
                proto.BaseScale = template.baseScale;
                proto.BaseRotation = template.baseRotation;

                proto.ShadowCastingMode = template.shadowCastingMode;
                proto.ReceiveShadow = template.receiveShadow;

                proto.Billboard = template.billboard;
                proto.BillboardShadowCastingMode = template.billboardShadowCastingMode;
                proto.BillboardReceiveShadow = template.billboardReceiveShadow;

                proto.KeepPrefabLayer = template.keepPrefabLayer;
                proto.Layer = template.layer;
                proto.PivotOffset = template.pivotOffset;

                prototypes.Add(proto);
            }

            return prototypes;
        }

        private void ParseTreeInstances(List<GTreeInstance> instances, ComputeBuffer buffer, int minProtoIndex, int maxProtoIndex)
        {
            if (buffer.count % InstanceSample.SIZE != 0)
            {
                Debug.LogError("Cannot parse instance sample buffer");
                return;
            }

            InstanceSample[] data = new InstanceSample[buffer.count / InstanceSample.SIZE];
            buffer.GetData(data);

            foreach (InstanceSample t in data)
            {
                if (t.isValid <= 0)
                    continue;
                GTreeInstance tree = new GTreeInstance();
                tree.Position = t.position;
                tree.Rotation = Quaternion.Euler(0, t.rotationY, 0);
                tree.Scale = new Vector3(t.horizontalScale, t.verticalScale, t.horizontalScale);
                if (minProtoIndex == maxProtoIndex)
                {
                    tree.PrototypeIndex = minProtoIndex;
                }
                else
                {
                    tree.PrototypeIndex = Random.Range(minProtoIndex, maxProtoIndex + 1);
                }

                instances.Add(tree);
            }
        }

        private GTreePrototypeGroup CreateTreeGroup(List<GTreePrototype> prototypes)
        {
            GTreePrototypeGroup treeGroup = ScriptableObject.CreateInstance<GTreePrototypeGroup>();
            treeGroup.name = GENERATED_TREE_GROUP_NAME;
            treeGroup.Prototypes = prototypes;
            return treeGroup;
        }

        private void SetTreeGroup(GTreePrototypeGroup treeGroup)
        {
            GTreePrototypeGroup currentGroup = terrain.TerrainData.Foliage.Trees;
            if (currentGroup != null && string.Equals(currentGroup.name, GENERATED_TREE_GROUP_NAME, System.StringComparison.Ordinal))
            {
                Object.DestroyImmediate(currentGroup);
            }
            terrain.TerrainData.Foliage.Trees = treeGroup;
        }

        /// <summary>
        /// Populates detail instance.
        /// </summary>
        /// <param name="templates">Template collection used by this operation.</param>
        /// <param name="buffers">Compute buffer collection used by this operation.</param>
        public void PopulateDetailInstance(List<DetailTemplate> templates, List<ComputeBuffer> buffers)
        {
            List<DetailTemplate> distinctTemplates = templates.Distinct().ToList();
            int[] templateIndices = new int[templates.Count];
            for (int i = 0; i < templates.Count; ++i)
            {
                templateIndices[i] = distinctTemplates.IndexOf(templates[i]);
            }

            List<GGrassPrototype> prototypes = new List<GGrassPrototype>();
            int[] minProtoIndices = new int[distinctTemplates.Count];
            int[] maxProtoIndices = new int[distinctTemplates.Count];
            int baseProtoIndex = 0;
            for (int i = 0; i < distinctTemplates.Count; ++i)
            {
                DetailTemplate template = distinctTemplates[i];
                List<GGrassPrototype> prototypesFromTemplates = CreateGrassPrototypesFromTemplate(template);
                prototypes.AddRange(prototypesFromTemplates);

                int minProtoIndex = baseProtoIndex;
                int maxProtoIndex = baseProtoIndex + prototypesFromTemplates.Count - 1;
                minProtoIndices[i] = minProtoIndex;
                maxProtoIndices[i] = maxProtoIndex;

                baseProtoIndex += prototypesFromTemplates.Count;
            }

            List<GGrassInstance> instances = new List<GGrassInstance>();
            for (int i = 0; i < buffers.Count; ++i)
            {
                ComputeBuffer buffer = buffers[i];
                int tIndex = templateIndices[i];
                int minProtoIndex = minProtoIndices[tIndex];
                int maxProtoIndex = maxProtoIndices[tIndex];
                ParseGrassInstances(instances, buffer, minProtoIndex, maxProtoIndex);
            }

            GGrassPrototypeGroup grassGroup = CreateGrassGroup(prototypes);
            terrain.TerrainData.Foliage.ClearGrassInstances();
            SetGrassGroup(grassGroup);
            terrain.TerrainData.Foliage.AddGrassInstances(instances);
            terrain.TerrainData.Foliage.SetGrassRegionDirty(new Rect(0, 0, 1, 1));
            terrain.UpdateGrassPatches();
            terrain.TerrainData.Foliage.ClearGrassDirtyRegions();
        }

        /// <summary>
        /// Clears all Polaris grass prototypes and grass instances from this tile.
        /// </summary>
        public void ClearDetailInstance()
        {
            GGrassPrototypeGroup grassGroup = CreateGrassGroup(new List<GGrassPrototype>());
            SetGrassGroup(grassGroup);
            terrain.TerrainData.Foliage.ClearGrassInstances();
            terrain.TerrainData.Foliage.SetGrassRegionDirty(new Rect(0, 0, 1, 1));
            terrain.TerrainData.Foliage.ClearGrassDirtyRegions();
        }

        private List<GGrassPrototype> CreateGrassPrototypesFromTemplate(DetailTemplate template)
        {
            List<GGrassPrototype> prototypes = new List<GGrassPrototype>();
            if (!template.IsValid())
                return prototypes;

            if (template.renderMode == DetailRenderMode.VertexLit)
            {
                List<GameObject> prefabs = new List<GameObject>();
                prefabs.Add(template.prefab);
                GameObject[] variants = template.prefabVariants;
                foreach (GameObject g in variants)
                {
                    if (g == null)
                        continue;
                    prefabs.Add(g);
                }

                foreach (GameObject g in prefabs)
                {
                    GGrassPrototype proto = new GGrassPrototype();
                    proto.Shape = GGrassShape.DetailObject;
                    proto.Prefab = g;
                    prototypes.Add(proto);
                }
            }
            else
            {
                List<Texture2D> textures = new List<Texture2D>();
                textures.Add(template.texture);
                Texture2D[] variants = template.textureVariants;
                foreach (Texture2D t in variants)
                {
                    if (t == null)
                        continue;
                    textures.Add(t);
                }

                foreach (Texture2D t in textures)
                {
                    GGrassPrototype proto = new GGrassPrototype();
                    proto.Shape = DetailTemplate.ToPolarisGrassShape(template.textureBasedGrassShape);
                    proto.Texture = t;
                    prototypes.Add(proto);
                }
            }

            foreach (GGrassPrototype proto in prototypes)
            {
                proto.Color = template.primaryColor;
                proto.Size = new Vector3(template.minWidth, template.minHeight, template.minWidth);
                proto.PivotOffset = template.pivotOffset;
                proto.BendFactor = template.bendFactor;
                proto.Layer = template.layer;
                proto.AlignToSurface = template.alignToSurface;
                proto.ShadowCastingMode = template.castShadow;
                proto.ReceiveShadow = template.receiveShadow;
            }

            return prototypes;
        }

        private GGrassPrototypeGroup CreateGrassGroup(List<GGrassPrototype> prototypes)
        {
            GGrassPrototypeGroup grassGroup = ScriptableObject.CreateInstance<GGrassPrototypeGroup>();
            grassGroup.name = GENERATED_GRASS_GROUP_NAME;
            grassGroup.Prototypes = prototypes;
            return grassGroup;
        }

        private void SetGrassGroup(GGrassPrototypeGroup grassGroup)
        {
            GGrassPrototypeGroup currentGroup = terrain.TerrainData.Foliage.Grasses;
            if (currentGroup != null && string.Equals(currentGroup.name, GENERATED_GRASS_GROUP_NAME, System.StringComparison.Ordinal))
            {
                Object.DestroyImmediate(currentGroup);
            }
            terrain.TerrainData.Foliage.Grasses = grassGroup;
        }

        private void ParseGrassInstances(List<GGrassInstance> instances, ComputeBuffer buffer, int minProtoIndex, int maxProtoIndex)
        {
            if (buffer.count % InstanceSample.SIZE != 0)
            {
                Debug.LogError("Cannot parse instance sample buffer");
                return;
            }

            InstanceSample[] data = new InstanceSample[buffer.count / InstanceSample.SIZE];
            buffer.GetData(data);

            foreach (InstanceSample t in data)
            {
                if (t.isValid <= 0)
                    continue;
                GGrassInstance tree = new GGrassInstance();
                tree.Position = t.position;
                tree.Rotation = Quaternion.Euler(0, t.rotationY, 0);
                tree.Scale = new Vector3(t.horizontalScale, t.verticalScale, t.horizontalScale);
                if (minProtoIndex == maxProtoIndex)
                {
                    tree.PrototypeIndex = minProtoIndex;
                }
                else
                {
                    tree.PrototypeIndex = Random.Range(minProtoIndex, maxProtoIndex + 1);
                }

                instances.Add(tree);
            }
        }

        /// <summary>
        /// Populates object.
        /// </summary>
        /// <param name="templates">Template collection used by this operation.</param>
        /// <param name="sampleBuffers">Collection of sample buffer values.</param>
        /// <param name="objectPopulateArgs">Options that control object spawning cadence.</param>
        /// <returns>Task handle that can be used to track asynchronous completion.</returns>
        public ProgressiveTask PopulateObject(List<ObjectTemplate> templates, List<ComputeBuffer> sampleBuffers, VistaManager.ObjectPopulateArgs objectPopulateArgs)
        {
            ProgressiveTask task = new ProgressiveTask();
            CoroutineUtility.StartCoroutine(PopulateObjectProgressive(task, templates, sampleBuffers, objectPopulateArgs));
            return task;
        }

        /// <summary>
        /// Clears spawned object prefabs owned by this terrain tile.
        /// </summary>
        /// <returns>Task handle that can be used to track clearing completion.</returns>
        public ProgressiveTask ClearObject()
        {
            string rootName = SpawnUtilities.ROOT_NAME;
            Transform existingRoot = terrain.transform.Find(rootName);
            if (existingRoot != null)
            {
                DestroyImmediate(existingRoot.gameObject);
            }

            ProgressiveTask task = new ProgressiveTask();
            task.Complete();
            return task;
        }

        private IEnumerator PopulateObjectProgressive(ProgressiveTask task, List<ObjectTemplate> templates, List<ComputeBuffer> sampleBuffers, VistaManager.ObjectPopulateArgs objectPopulateArgs)
        {
            string rootName = SpawnUtilities.ROOT_NAME;
            Transform existingRoot = terrain.transform.Find(rootName);
            if (existingRoot != null)
            {
                DestroyImmediate(existingRoot.gameObject);
            }

            Transform mainRoot = new GameObject(rootName).transform;
            mainRoot.parent = terrain.transform;
            mainRoot.localPosition = Vector3.zero;
            mainRoot.localRotation = Quaternion.identity;
            mainRoot.localScale = Vector3.one;

            List<ObjectTemplate> distinctTemplates = templates.Distinct().ToList();
            int[] templateIndices = new int[templates.Count];
            for (int i = 0; i < templates.Count; ++i)
            {
                templateIndices[i] = distinctTemplates.IndexOf(templates[i]);
            }

            CoroutineHandle[] coroutines = new CoroutineHandle[templates.Count];
            for (int i = 0; i < sampleBuffers.Count; ++i)
            {
                int tIndex = templateIndices[i];
                ObjectTemplate template = distinctTemplates[tIndex];
                CoroutineHandle c = CoroutineUtility.StartCoroutine(PopulateObjectProgressive(template, sampleBuffers[i], mainRoot, objectPopulateArgs));
                coroutines[i] = c;
            }

            foreach (CoroutineHandle c in coroutines)
            {
                yield return c.coroutine;
            }

            task.Complete();
            yield break;
        }

        private IEnumerator PopulateObjectProgressive(ObjectTemplate template, ComputeBuffer buffer, Transform mainRoot, VistaManager.ObjectPopulateArgs objectPopulateArgs)
        {
            if (buffer.count % InstanceSample.SIZE != 0)
            {
                Debug.LogError("Cannot parse tree sample buffer");
                yield break;
            }
            if (mainRoot == null)
            {
                yield break;
            }

            int instanceCount = buffer.count / InstanceSample.SIZE;
            InstanceSample[] samples = new InstanceSample[buffer.count / InstanceSample.SIZE];
            buffer.GetData(samples);

            string prefabRootName = $"~{template.name}";
            Transform prefabRoot = mainRoot.Find(prefabRootName);
            if (prefabRoot == null)
            {
                prefabRoot = new GameObject(prefabRootName).transform;
                prefabRoot.parent = mainRoot;
                prefabRoot.localPosition = Vector3.zero;
                prefabRoot.localRotation = Quaternion.identity;
                prefabRoot.localScale = Vector3.one;
            }

            List<GameObject> prefabs = new List<GameObject>();
            prefabs.Add(template.prefab);
            foreach (GameObject g in template.prefabVariants)
            {
                if (g != null)
                {
                    prefabs.Add(g);
                }
            }
            int prefabCount = prefabs.Count;

            Vector3 terrainSize = terrain.TerrainData.Geometry.Size;
            for (int i = 0; i < instanceCount; ++i)
            {
                InstanceSample sample = samples[i];
                if (sample.isValid == 0)
                    continue;
                RaycastHit hit;
                Vector3 normalizedPoint = new Vector3(sample.position.x, 0, sample.position.z);
                Vector3 worldPosition;
                if (terrain.Raycast(normalizedPoint, out hit))
                {
                    worldPosition = hit.point;
                }
                else
                {
                    worldPosition = terrain.transform.TransformPoint(new Vector3(terrainSize.x * sample.position.x, 0, terrainSize.z * sample.position.z));
                }

                GameObject prefab;
                if (prefabCount == 1)
                {
                    prefab = prefabs[0];
                }
                else
                {
                    prefab = prefabs[Random.Range(0, prefabs.Count)];
                }

                Quaternion localRotation = Quaternion.Euler(0, sample.rotationY * Mathf.Rad2Deg, 0);
                Vector3 baseScale = prefab.transform.localScale;
                Vector3 localScale = new Vector3(sample.horizontalScale, sample.verticalScale, sample.horizontalScale);
                localScale.Scale(baseScale);

                if (template.alignToNormal)
                {
                    Vector3 normalVector = hit.normal;
                    float errorFactor = Random.Range(1 - template.normalAlignmentError, 1 + template.normalAlignmentError);
                    normalVector = Vector3.LerpUnclamped(Vector3.up, normalVector, errorFactor);
                    Quaternion alignmentRotation = Quaternion.FromToRotation(Vector3.up, normalVector);
                    localRotation *= alignmentRotation;
                }

                GameObject instance = SpawnUtilities.Spawn(prefab);
                instance.transform.parent = prefabRoot;
                instance.transform.position = worldPosition;
                instance.transform.localRotation = localRotation;
                instance.transform.localScale = localScale;
                populatePrefabInstanceCallback?.Invoke(this, instance);

                if (i % objectPopulateArgs.objectsPerFrame == 0)
                {
                    yield return null;
                }
            }

            yield break;
        }

        /// <summary>
        /// Populates generic textures.
        /// </summary>
        /// <param name="labels">Labels paired with corresponding outputs.</param>
        /// <param name="textures">Texture collection used by this operation.</param>
        public void PopulateGenericTextures(List<string> labels, List<RenderTexture> textures)
        {
            populateGenericTexturesCallback?.Invoke(labels, textures);
        }

        /// <summary>
        /// Populates generic buffers.
        /// </summary>
        /// <param name="labels">Labels paired with corresponding outputs.</param>
        /// <param name="buffers">Compute buffer collection used by this operation.</param>
        public void PopulateGenericBuffers(List<string> labels, List<ComputeBuffer> buffers)
        {
            populateGenericBuffersCallback?.Invoke(labels, buffers);
        }

        private void SerializePrototypes()
        {
            if (terrain != null && terrain.TerrainData != null)
            {
                if (terrain.TerrainData.Shading.Splats != null)
                {
                    m_splatPrototypesSerialized = terrain.TerrainData.Shading.Splats.Prototypes;
                }
                if (terrain.TerrainData.Foliage.Trees != null)
                {
                    m_treePrototypesSerialized = terrain.TerrainData.Foliage.Trees.Prototypes;
                }
                if (terrain.TerrainData.Foliage.Grasses != null)
                {
                    m_grassPrototypesSerialized = terrain.TerrainData.Foliage.Grasses.Prototypes;
                }
            }
        }

        private void DeserializePrototypes()
        {
            if (terrain != null && terrain.TerrainData != null)
            {
                if (m_splatPrototypesSerialized != null)
                {
                    GSplatPrototypeGroup splatGroup = CreateSplatGroup(m_splatPrototypesSerialized);
                    SetSplatGroup(splatGroup);
                }
                if (m_treePrototypesSerialized != null)
                {
                    GTreePrototypeGroup treeGroup = CreateTreeGroup(m_treePrototypesSerialized);
                    SetTreeGroup(treeGroup);
                }
                if (m_grassPrototypesSerialized != null)
                {
                    GGrassPrototypeGroup grassGroup = CreateGrassGroup(m_grassPrototypesSerialized);
                    SetGrassGroup(grassGroup);
                }
            }
        }

        /// <summary>
        /// Handles the before serialize callback.
        /// </summary>
        public void OnBeforeSerialize()
        {
            SerializePrototypes();
        }

        /// <summary>
        /// Handles the after deserialize callback.
        /// </summary>
        public void OnAfterDeserialize()
        {

        }

        /// <summary>
        /// Handles the collect scene height callback.
        /// </summary>
        /// <param name="targetRt">Target rt value.</param>
        /// <param name="requestedWorldRect">World-space rectangle represented by the destination texture.</param>
        public void OnCollectSceneHeight(RenderTexture targetRt, Rect requestedWorldRect)
        {
            Bounds selfWorldBounds = worldBounds;
            Rect selfRect = new Rect(selfWorldBounds.min.x, selfWorldBounds.min.z, selfWorldBounds.size.x, selfWorldBounds.size.z);
            float minX = Utilities.InverseLerpUnclamped(requestedWorldRect.min.x, requestedWorldRect.max.x, selfRect.min.x);
            float maxX = Utilities.InverseLerpUnclamped(requestedWorldRect.min.x, requestedWorldRect.max.x, selfRect.max.x) + targetRt.texelSize.x;
            float minY = Utilities.InverseLerpUnclamped(requestedWorldRect.min.y, requestedWorldRect.max.y, selfRect.min.y);
            float maxY = Utilities.InverseLerpUnclamped(requestedWorldRect.min.y, requestedWorldRect.max.y, selfRect.max.y) + targetRt.texelSize.y;

            Vector2[] uvCorner = new Vector2[]
            {
                new Vector2(minX, minY),
                new Vector2(minX, maxY),
                new Vector2(maxX, maxY),
                new Vector2(maxX, minY)
            };

            Texture terrainHeightMap = terrain.TerrainData.Geometry.HeightMap;
            PolarisTileUtilities.DecodeAndDrawHeightMap(targetRt, terrainHeightMap, uvCorner);
        }
    }
}
#endif
#endif


