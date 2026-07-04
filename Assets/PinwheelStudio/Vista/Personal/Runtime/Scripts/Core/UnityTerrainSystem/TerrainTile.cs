#if VISTA
using Pinwheel.Vista.Graphics;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Pinwheel.Vista.UnityTerrain
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(Terrain))]
    [AddComponentMenu("Vista/Terrain Tile")]
    /// <summary>
    /// Adapts a Unity <see cref="Terrain"/> so Vista can write biome outputs into it.
    /// </summary>
    /// <remarks>
    /// This component is the Unity Terrain backend for Vista's tile pipeline. It exposes tile bounds and resolution
    /// settings to <see cref="VistaManager"/>, translates generated textures and instance buffers into Unity Terrain data,
    /// and provides scene-height sampling back to Local Procedural Biomes when requested.
    /// </remarks>
    public class TerrainTile : MonoBehaviour, ITile, IGeometryPopulator, ILayerWeightsPopulator, ITreePopulator, IDetailDensityPopulator, IObjectPopulator, IGenericTexturePopulator, IGenericBufferPopulator, ISceneHeightProvider
    {
        /// <summary>
        /// Occurs when Vista forwards generic texture outputs to this tile.
        /// </summary>
        public event PopulateGenericTexturesHandler populateGenericTexturesCallback;
        /// <summary>
        /// Occurs when Vista forwards generic buffer outputs to this tile.
        /// </summary>
        public event PopulateGenericBuffersHandler populateGenericBuffersCallback;
        /// <summary>
        /// Occurs after this tile spawns one prefab instance during object population.
        /// </summary>
        public event PopulatePrefabHandler populatePrefabInstanceCallback;

        [SerializeField]
        private string m_managerId;
        /// <summary>
        /// Gets or sets the identifier of the <see cref="VistaManager"/> that owns this tile.
        /// </summary>
        /// <remarks>
        /// The tile registers itself only for managers whose <see cref="VistaManager.id"/> matches this value.
        /// </remarks>
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
        /// Gets the Unity Terrain component wrapped by this tile.
        /// </summary>
        public Terrain terrain { get; private set; }

        /// <summary>
        /// Gets the world-space bounds of the wrapped terrain.
        /// </summary>
        /// <remarks>
        /// The bounds are derived from <see cref="TerrainData.size"/> transformed by this object's transform, so translated,
        /// rotated, or scaled terrains report their effective world-space coverage.
        /// </remarks>
        public Bounds worldBounds
        {
            get
            {
                Vector3 worldCenter = transform.TransformPoint(terrain.terrainData.size * 0.5f);
                Vector3 worldSize = transform.TransformVector(terrain.terrainData.size);
                return new Bounds(worldCenter, worldSize);
            }
        }

        /// <summary>
        /// Gets or sets the maximum terrain height.
        /// </summary>
        /// <remarks>
        /// This maps directly to the Y component of <see cref="TerrainData.size"/>. Setting it changes the terrain's vertical
        /// scale without modifying normalized height samples.
        /// </remarks>
        public float maxHeight
        {
            get
            {
                if (terrain != null && terrain.terrainData != null)
                {
                    return terrain.terrainData.size.y;
                }
                else
                {
                    return 0;
                }
            }
            set
            {
                if (terrain != null && terrain.terrainData != null)
                {
                    Vector3 size = terrain.terrainData.size;
                    size.y = value;
                    terrain.terrainData.size = size;
                }
            }
        }

        /// <summary>
        /// Gets or sets the Unity height-map resolution of the wrapped terrain.
        /// </summary>
        /// <remarks>
        /// When the value changes, the current height map is resampled into a temporary render texture and copied back into
        /// the resized terrain data so existing geometry is preserved instead of reset.
        /// </remarks>
        public int heightMapResolution
        {
            get
            {
                if (terrain != null && terrain.terrainData != null)
                {
                    return terrain.terrainData.heightmapResolution;
                }
                else
                {
                    return 0;
                }
            }
            set
            {
                if (terrain != null && terrain.terrainData != null && terrain.terrainData.heightmapResolution != value)
                {
                    SetHeightMapResolution(value);
                }
            }
        }

        /// <summary>
        /// Gets or sets the alphamap resolution used for layer-weight textures.
        /// </summary>
        /// <remarks>
        /// Changing this value resamples every existing alphamap texture into the new resolution and updates both
        /// <see cref="TerrainData.alphamapResolution"/> and <see cref="TerrainData.baseMapResolution"/>.
        /// </remarks>
        public int textureResolution
        {
            get
            {
                if (terrain != null && terrain.terrainData != null)
                {
                    return terrain.terrainData.alphamapResolution;
                }
                else
                {
                    return 0;
                }
            }
            set
            {
                if (terrain != null && terrain.terrainData != null && terrain.terrainData.alphamapResolution != value)
                {
                    SetTextureResolution(value);
                }
            }
        }

        /// <summary>
        /// Gets or sets the detail density resolution of the wrapped terrain.
        /// </summary>
        /// <remarks>
        /// The setter updates the detail resolution while preserving the current resolution-per-patch setting.
        /// </remarks>
        public int detailDensityMapResolution
        {
            get
            {
                if (terrain != null && terrain.terrainData != null)
                {
                    return terrain.terrainData.detailResolution;
                }
                else
                {
                    return 0;
                }
            }
            set
            {
                if (terrain != null && terrain.terrainData != null && terrain.terrainData.detailResolution != value)
                {
                    terrain.terrainData.SetDetailResolution(value, terrain.terrainData.detailResolutionPerPatch);
                }
            }
        }

        [SerializeField]
        private TerrainLayer[] m_terrainLayers;
        /// <summary>
        /// Gets the terrain layers most recently assigned by Vista.
        /// </summary>
        /// <remarks>
        /// The array is updated when <see cref="PopulateLayerWeights"/> rebuilds the terrain's alphamap layout.
        /// </remarks>
        public TerrainLayer[] terrainLayers
        {
            get
            {
                return m_terrainLayers;
            }
        }

        private void OnEnable()
        {
            terrain = GetComponent<Terrain>();
            VistaManager.collectTiles += OnCollectTiles;
            MatchSeams();
        }

        private void OnDisable()
        {
            VistaManager.collectTiles -= OnCollectTiles;
        }

        private void OnCollectTiles(VistaManager manager, Collector<ITile> tiles)
        {
            if (string.Equals(manager.id, m_managerId) && terrain != null && terrain.terrainData != null)
            {
                if (terrain != null && terrain.terrainData != null)
                {
                    tiles.Add(this);
                }
            }
        }

        private void SetHeightMapResolution(int res)
        {
            RenderTexture scaledHm = new RenderTexture(res, res, 0, Terrain.heightmapFormat);
            Drawing.Blit(terrain.terrainData.heightmapTexture, scaledHm);

            Vector3 size = terrain.terrainData.size;
            terrain.terrainData.heightmapResolution = res;
            terrain.terrainData.size = size;

            RenderTexture.active = scaledHm;
            RectInt srcRect = new RectInt(0, 0, scaledHm.width, scaledHm.height);
            Vector2Int dst = new Vector2Int(0, 0);
            terrain.terrainData.CopyActiveRenderTextureToHeightmap(srcRect, dst, TerrainHeightmapSyncControl.None);
            RenderTexture.active = null;

            scaledHm.Release();
            Object.DestroyImmediate(scaledHm);
        }

        private void SetTextureResolution(int res)
        {
            int textureCount = terrain.terrainData.alphamapTextureCount;
            RenderTexture[] scaledAlphaMaps = new RenderTexture[textureCount];
            for (int i = 0; i < textureCount; ++i)
            {
                scaledAlphaMaps[i] = new RenderTexture(res, res, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Drawing.Blit(terrain.terrainData.alphamapTextures[i], scaledAlphaMaps[i]);
            }

            terrain.terrainData.alphamapResolution = res;
            terrain.terrainData.baseMapResolution = res;

            for (int i = 0; i < textureCount; ++i)
            {
                GraphicsUtils.ReadRenderTexture(scaledAlphaMaps[i], terrain.terrainData.alphamapTextures[i]);

                scaledAlphaMaps[i].Release();
                Object.DestroyImmediate(scaledAlphaMaps[i]);
            }
        }

        /// <summary>
        /// Writes a generated Vista height map into the Unity Terrain heightmap.
        /// </summary>
        /// <param name="heightMap">
        /// The generated height texture in Vista's normalized format. It is converted into Unity Terrain's packed heightmap
        /// format before being copied into terrain data.
        /// </param>
        public void PopulateHeightMap(RenderTexture heightMap)
        {
            int resolution = terrain.terrainData.heightmapResolution;
            //generated height map is in range [0,1]
            //Unity uses some packing for its height value
            RenderTexture remappedHeightMap = new RenderTexture(resolution, resolution, 0, Terrain.heightmapRenderTextureFormat);
            TerrainTileUtilities.ConvertHeightMapToUnity(heightMap, remappedHeightMap);

            RenderTexture.active = remappedHeightMap;
            RectInt srcRect = new RectInt(0, 0, remappedHeightMap.width, remappedHeightMap.height);
            Vector2Int dst = new Vector2Int(0, 0);
            terrain.terrainData.CopyActiveRenderTextureToHeightmap(srcRect, dst, TerrainHeightmapSyncControl.None);
            RenderTexture.active = null;

            remappedHeightMap.Release();
            Object.DestroyImmediate(remappedHeightMap);
        }

        /// <summary>
        /// Clears the Unity Terrain heightmap by writing a zero-valued Vista height map.
        /// </summary>
        public void ClearHeightMap()
        {
            int resolution = terrain.terrainData.heightmapResolution;
            RenderTexture zeroHeightMap = RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
            GraphicsUtils.ClearWithZeros(zeroHeightMap);
            PopulateHeightMap(zeroHeightMap);
            RenderTexture.ReleaseTemporary(zeroHeightMap);
        }

        /// <summary>
        /// Writes a generated Vista hole map into the Unity Terrain holes data.
        /// </summary>
        /// <param name="holeMap">
        /// The generated hole texture. It is resampled to the terrain hole resolution, read back to CPU memory, then
        /// converted into the boolean hole array expected by Unity Terrain.
        /// </param>
        public void PopulateHoleMap(RenderTexture holeMap)
        {
            int resolution = terrain.terrainData.holesResolution;
            RenderTexture scaledHoleMap = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RFloat);
            Drawing.Blit(holeMap, scaledHoleMap);

            Texture2D map = new Texture2D(resolution, resolution, TextureFormat.RFloat, false);
            GraphicsUtils.ReadRenderTexture(scaledHoleMap, map);

            byte[] data = map.GetRawTextureData();
            bool[,] holes = new bool[resolution, resolution];
            for (int y = 0; y < resolution; ++y)
            {
                for (int x = 0; x < resolution; ++x)
                {
                    float value = System.BitConverter.ToSingle(data, (y * resolution + x) * 4);
                    holes[y, x] = value == 0f;
                }
            }
            terrain.terrainData.SetHolesDelayLOD(0, 0, holes);

            scaledHoleMap.Release();
            Object.DestroyImmediate(scaledHoleMap);
            Object.DestroyImmediate(map);
        }

        /// <summary>
        /// Clears the Unity Terrain holes data by writing a zero-valued Vista hole map.
        /// </summary>
        public void ClearHoleMap()
        {
            int resolution = terrain.terrainData.holesResolution;
            RenderTexture zeroHoleMap = RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
            GraphicsUtils.ClearWithZeros(zeroHoleMap);
            PopulateHoleMap(zeroHoleMap);
            RenderTexture.ReleaseTemporary(zeroHoleMap);
        }

        /// <summary>
        /// Receives a mesh-density map generated by Vista.
        /// </summary>
        /// <param name="meshDensityMap">The generated mesh-density texture.</param>
        /// <remarks>
        /// Unity Terrain does not use Vista's mesh-density output, so this method is intentionally empty.
        /// </remarks>
        public void PopulateMeshDensityMap(RenderTexture meshDensityMap)
        {

        }

        /// <summary>
        /// Clears the mesh-density channel. Unity Terrain does not consume this output, so this is a no-op.
        /// </summary>
        public void ClearMeshDensityMap()
        {

        }

        /// <summary>
        /// Finalizes pending height changes on the wrapped terrain.
        /// </summary>
        /// <remarks>
        /// This currently syncs the terrain heightmap after height-related writes have been staged.
        /// </remarks>
        public void UpdateGeometry()
        {
            terrain.terrainData.SyncHeightmap();
        }

        /// <summary>
        /// Averages shared edges with neighboring Unity terrains to reduce visible seams.
        /// </summary>
        /// <remarks>
        /// Height samples and alphamap weights are blended against each direct neighbor when the corresponding resolutions
        /// match. The method then syncs the heightmap so geometry reflects any seam corrections.
        /// </remarks>
        public void MatchSeams()
        {
            if (terrain.terrainData == null)
                return;

            MatchSeamLeft();
            MatchSeamTop();
            MatchSeamRight();
            MatchSeamBottom();
            terrain.terrainData.SyncHeightmap();
        }

        /// <summary>
        /// Writes terrain layer weights into Unity Terrain alphamaps.
        /// </summary>
        /// <param name="layers">The terrain layers paired with <paramref name="weights"/> by index.</param>
        /// <param name="weights">The generated layer-weight textures to merge and apply.</param>
        /// <remarks>
        /// Duplicate layers are merged through <see cref="AlphaMapsCombiner"/>, the terrain layer array is replaced with the
        /// distinct result, and the merged alphamap textures are copied into Unity's internal alphamap textures.
        /// </remarks>
        public void PopulateLayerWeights(List<TerrainLayer> layers, List<RenderTexture> weights)
        {
            List<TerrainLayer> distinctLayers;
            List<RenderTexture> alphaMaps;
            int resolution = textureResolution;
            
            AlphaMapsCombiner combiner = new AlphaMapsCombiner();
            combiner.CombineAndMerge(layers, weights, resolution, out distinctLayers, out alphaMaps);
            
            m_terrainLayers = distinctLayers.ToArray();
            terrain.terrainData.terrainLayers = m_terrainLayers;

            for (int i = 0; i < alphaMaps.Count; ++i)
            {
                Texture2D alphaMap = terrain.terrainData.GetAlphamapTexture(i);
                GraphicsUtils.ReadRenderTexture(alphaMaps[i], alphaMap);
            }

            for (int i = 0; i < alphaMaps.Count; ++i)
            {
                alphaMaps[i].Release();
                Object.DestroyImmediate(alphaMaps[i]);
            }
        }

        /// <summary>
        /// Clears all Unity Terrain layer weights and terrain layers from this tile.
        /// </summary>
        public void ClearLayerWeights()
        {
            int textureCount = terrain.terrainData.alphamapTextureCount;
            int resolution = terrain.terrainData.alphamapResolution;

            if (textureCount > 0)
            {
                RenderTexture zeroAlphaMap = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                GraphicsUtils.ClearWithZeros(zeroAlphaMap);

                for (int i = 0; i < textureCount; ++i)
                {
                    Texture2D alphaMap = terrain.terrainData.GetAlphamapTexture(i);
                    if (alphaMap == null)
                        continue;

                    GraphicsUtils.ReadRenderTexture(zeroAlphaMap, alphaMap);
                }

                zeroAlphaMap.Release();
                Object.DestroyImmediate(zeroAlphaMap);
            }

            m_terrainLayers = System.Array.Empty<TerrainLayer>();
            terrain.terrainData.terrainLayers = m_terrainLayers;
        }

        private void MatchSeamLeft()
        {
            Terrain leftNeighbor = terrain.leftNeighbor;
            if (leftNeighbor == null || leftNeighbor.terrainData == null)
                return;
            if (leftNeighbor.terrainData.heightmapResolution == terrain.terrainData.heightmapResolution)
            {
                int resolution = terrain.terrainData.heightmapResolution;
                float[,] neighborHeights = leftNeighbor.terrainData.GetHeights(resolution - 1, 0, 1, resolution);
                float[,] selfHeights = terrain.terrainData.GetHeights(0, 0, 1, resolution);
                float[,] avgHeights = new float[resolution, 1];
                for (int i = 0; i < resolution; ++i)
                {
                    avgHeights[i, 0] = (selfHeights[i, 0] + neighborHeights[i, 0]) * 0.5f;
                }
                terrain.terrainData.SetHeightsDelayLOD(0, 0, avgHeights);
                leftNeighbor.terrainData.SetHeights(resolution - 1, 0, avgHeights);
            }

            if (leftNeighbor.terrainData.alphamapResolution == terrain.terrainData.alphamapResolution)
            {
                int resolution = terrain.terrainData.alphamapResolution;
                float[,,] neighborAlpha = leftNeighbor.terrainData.GetAlphamaps(resolution - 1, 0, 1, resolution);
                float[,,] selfAlpha = terrain.terrainData.GetAlphamaps(0, 0, 1, resolution);

                TerrainLayer[] layers = terrain.terrainData.terrainLayers;
                for (int layerIndex = 0; layerIndex < layers.Length; ++layerIndex)
                {
                    int neighborLayerIndex = leftNeighbor.terrainData.GetLayerIndex(layers[layerIndex]);
                    if (neighborLayerIndex < 0)
                        continue;
                    for (int i = 0; i < resolution; ++i)
                    {
                        float avg = (neighborAlpha[i, 0, neighborLayerIndex] + selfAlpha[i, 0, layerIndex]) * 0.5f;
                        neighborAlpha[i, 0, neighborLayerIndex] = avg;
                        selfAlpha[i, 0, layerIndex] = avg;
                    }
                }
                terrain.terrainData.SetAlphamaps(0, 0, selfAlpha);
                leftNeighbor.terrainData.SetAlphamaps(resolution - 1, 0, neighborAlpha);
            }
        }

        private void MatchSeamTop()
        {
            Terrain topNeighbor = terrain.topNeighbor;
            if (topNeighbor == null || topNeighbor.terrainData == null)
                return;
            if (topNeighbor.terrainData.heightmapResolution == terrain.terrainData.heightmapResolution)
            {
                int resolution = terrain.terrainData.heightmapResolution;
                float[,] neighborHeights = topNeighbor.terrainData.GetHeights(0, 0, resolution, 1);
                float[,] selfHeights = terrain.terrainData.GetHeights(0, resolution - 1, resolution, 1);
                float[,] avgHeights = new float[1, resolution];
                for (int i = 0; i < resolution; ++i)
                {
                    avgHeights[0, i] = (selfHeights[0, i] + neighborHeights[0, i]) * 0.5f;
                }
                terrain.terrainData.SetHeightsDelayLOD(0, resolution - 1, avgHeights);
                topNeighbor.terrainData.SetHeights(0, 0, avgHeights);
            }

            if (topNeighbor.terrainData.alphamapResolution == terrain.terrainData.alphamapResolution)
            {
                int resolution = terrain.terrainData.alphamapResolution;
                float[,,] neighborAlpha = topNeighbor.terrainData.GetAlphamaps(0, 0, resolution, 1);
                float[,,] selfAlpha = terrain.terrainData.GetAlphamaps(0, resolution - 1, resolution, 1);

                TerrainLayer[] layers = terrain.terrainData.terrainLayers;
                for (int layerIndex = 0; layerIndex < layers.Length; ++layerIndex)
                {
                    int neighborLayerIndex = topNeighbor.terrainData.GetLayerIndex(layers[layerIndex]);
                    if (neighborLayerIndex < 0)
                        continue;
                    for (int i = 0; i < resolution; ++i)
                    {
                        float avg = (neighborAlpha[0, i, neighborLayerIndex] + selfAlpha[0, i, layerIndex]) * 0.5f;
                        neighborAlpha[0, i, neighborLayerIndex] = avg;
                        selfAlpha[0, i, layerIndex] = avg;
                    }
                }
                terrain.terrainData.SetAlphamaps(0, resolution - 1, selfAlpha);
                topNeighbor.terrainData.SetAlphamaps(0, 0, neighborAlpha);
            }
        }

        private void MatchSeamRight()
        {
            Terrain rightNeighbor = terrain.rightNeighbor;
            if (rightNeighbor == null || rightNeighbor.terrainData == null)
                return;
            if (rightNeighbor.terrainData.heightmapResolution == terrain.terrainData.heightmapResolution)
            {
                int resolution = terrain.terrainData.heightmapResolution;
                float[,] neighborHeights = rightNeighbor.terrainData.GetHeights(0, 0, 1, resolution);
                float[,] selfHeights = terrain.terrainData.GetHeights(resolution - 1, 0, 1, resolution);
                float[,] avgHeights = new float[resolution, 1];
                for (int i = 0; i < resolution; ++i)
                {
                    avgHeights[i, 0] = (selfHeights[i, 0] + neighborHeights[i, 0]) * 0.5f;
                }
                terrain.terrainData.SetHeightsDelayLOD(resolution - 1, 0, avgHeights);
                rightNeighbor.terrainData.SetHeights(0, 0, avgHeights);
            }

            if (rightNeighbor.terrainData.alphamapResolution == terrain.terrainData.alphamapResolution)
            {
                int resolution = terrain.terrainData.alphamapResolution;
                float[,,] neighborAlpha = rightNeighbor.terrainData.GetAlphamaps(0, 0, 1, resolution);
                float[,,] selfAlpha = terrain.terrainData.GetAlphamaps(resolution - 1, 0, 1, resolution);

                TerrainLayer[] layers = terrain.terrainData.terrainLayers;
                for (int layerIndex = 0; layerIndex < layers.Length; ++layerIndex)
                {
                    int neighborLayerIndex = rightNeighbor.terrainData.GetLayerIndex(layers[layerIndex]);
                    if (neighborLayerIndex < 0)
                        continue;
                    for (int i = 0; i < resolution; ++i)
                    {
                        float avg = (neighborAlpha[i, 0, neighborLayerIndex] + selfAlpha[i, 0, layerIndex]) * 0.5f;
                        neighborAlpha[i, 0, neighborLayerIndex] = avg;
                        selfAlpha[i, 0, layerIndex] = avg;
                    }
                }
                terrain.terrainData.SetAlphamaps(resolution - 1, 0, selfAlpha);
                rightNeighbor.terrainData.SetAlphamaps(0, 0, neighborAlpha);
            }
        }

        private void MatchSeamBottom()
        {
            Terrain bottomNeighbor = terrain.bottomNeighbor;
            if (bottomNeighbor == null || bottomNeighbor.terrainData == null)
                return;
            if (bottomNeighbor.terrainData.heightmapResolution == terrain.terrainData.heightmapResolution)
            {
                int resolution = terrain.terrainData.heightmapResolution;
                float[,] neighborHeights = bottomNeighbor.terrainData.GetHeights(0, resolution - 1, resolution, 1);
                float[,] selfHeights = terrain.terrainData.GetHeights(0, 0, resolution, 1);
                float[,] avgHeights = new float[1, resolution];
                for (int i = 0; i < resolution; ++i)
                {
                    avgHeights[0, i] = (selfHeights[0, i] + neighborHeights[0, i]) * 0.5f;
                }
                terrain.terrainData.SetHeightsDelayLOD(0, 0, selfHeights);
                bottomNeighbor.terrainData.SetHeights(0, resolution - 1, avgHeights);
            }

            if (bottomNeighbor.terrainData.alphamapResolution == terrain.terrainData.alphamapResolution)
            {
                int resolution = terrain.terrainData.alphamapResolution;
                float[,,] neighborAlpha = bottomNeighbor.terrainData.GetAlphamaps(0, resolution - 1, resolution, 1);
                float[,,] selfAlpha = terrain.terrainData.GetAlphamaps(0, 0, resolution, 1);

                TerrainLayer[] layers = terrain.terrainData.terrainLayers;
                for (int layerIndex = 0; layerIndex < layers.Length; ++layerIndex)
                {
                    int neighborLayerIndex = bottomNeighbor.terrainData.GetLayerIndex(layers[layerIndex]);
                    if (neighborLayerIndex < 0)
                        continue;
                    for (int i = 0; i < resolution; ++i)
                    {
                        float avg = (neighborAlpha[0, i, neighborLayerIndex] + selfAlpha[0, i, layerIndex]) * 0.5f;
                        neighborAlpha[0, i, neighborLayerIndex] = avg;
                        selfAlpha[0, i, layerIndex] = avg;
                    }
                }
                terrain.terrainData.SetAlphamaps(0, 0, selfAlpha);
                bottomNeighbor.terrainData.SetAlphamaps(0, resolution - 1, neighborAlpha);
            }
        }

        /// <summary>
        /// Populates trees.
        /// </summary>
        /// <param name="templates">Template collection used by this operation.</param>
        /// <param name="buffers">Compute buffer collection used by this operation.</param>
        public void PopulateTrees(List<TreeTemplate> templates, List<ComputeBuffer> buffers)
        {
            // Build one Unity prototype range for each unique Vista tree template.
            List<TreeTemplate> distinctTemplates = templates.Distinct().ToList();
            int[] templateIndices = new int[templates.Count];
            for (int i = 0; i < templates.Count; ++i)
            {
                templateIndices[i] = distinctTemplates.IndexOf(templates[i]);
            }

            // Expand each unique template into one or more Unity prototypes, including prefab variants.
            List<TreePrototype> prototypes = new List<TreePrototype>();
            int[] minProtoIndices = new int[distinctTemplates.Count];
            int[] maxProtoIndices = new int[distinctTemplates.Count];
            int baseProtoIndex = 0;
            for (int i = 0; i < distinctTemplates.Count; ++i)
            {
                TreeTemplate template = distinctTemplates[i];
                List<TreePrototype> prototypesFromTemplates = CreateTreePrototypesFromTemplate(template);
                prototypes.AddRange(prototypesFromTemplates);

                int minProtoIndex = baseProtoIndex;
                int maxProtoIndex = baseProtoIndex + prototypesFromTemplates.Count - 1;
                minProtoIndices[i] = minProtoIndex;
                maxProtoIndices[i] = maxProtoIndex;

                baseProtoIndex += prototypesFromTemplates.Count;
            }

            // Convert every source buffer into Unity tree instances that point to its template's prototype range.
            List<TreeInstance> instances = new List<TreeInstance>();
            for (int i = 0; i < buffers.Count; ++i)
            {
                ComputeBuffer buffer = buffers[i];
                int tIndex = templateIndices[i];
                int minProtoIndex = minProtoIndices[tIndex];
                int maxProtoIndex = maxProtoIndices[tIndex];
                ParseTreeInstances(instances, buffer, minProtoIndex, maxProtoIndex);
            }

            // Replace Unity Terrain tree data in one batch so instance prototype indices match the new prototype array.
            terrain.terrainData.treeInstances = new TreeInstance[0];
            terrain.terrainData.treePrototypes = prototypes.ToArray();
            terrain.terrainData.SetTreeInstances(instances.ToArray(), true);
        }

        /// <summary>
        /// Clears all Unity Terrain tree prototypes and instances from this tile.
        /// </summary>
        public void ClearTrees()
        {
            terrain.terrainData.treeInstances = System.Array.Empty<TreeInstance>();
            terrain.terrainData.treePrototypes = System.Array.Empty<TreePrototype>();
        }

        private List<TreePrototype> CreateTreePrototypesFromTemplate(TreeTemplate template)
        {
            List<TreePrototype> prototypes = new List<TreePrototype>();
            if (!template.IsValid())
            {
                return prototypes;
            }

            // The main prefab is always the first prototype for this template.
            TreePrototype p0 = new TreePrototype();
            p0.prefab = template.prefab;
            p0.bendFactor = template.bendFactor;
            p0.navMeshLod = template.navMeshLod;
            prototypes.Add(p0);

            // Variants share the same prototype settings but swap the prefab.
            GameObject[] variants = template.prefabVariants;
            foreach (GameObject v in variants)
            {
                if (v == null)
                    continue;
                TreePrototype p = new TreePrototype(p0);
                p.prefab = v;
                prototypes.Add(p);
            }
            return prototypes;
        }

        private void ParseTreeInstances(List<TreeInstance> instances, ComputeBuffer buffer, int minProtoIndex, int maxProtoIndex)
        {
            if (buffer.count % InstanceSample.SIZE != 0)
            {
                Debug.LogError("Cannot parse instance sample buffer");
                return;
            }

            InstanceSample[] data = new InstanceSample[buffer.count / InstanceSample.SIZE];
            buffer.GetData(data);

            // Invalid samples are skipped; valid samples pick a random prototype variant within the template range.
            foreach (InstanceSample t in data)
            {
                if (t.isValid != 1)
                    continue;
                TreeInstance tree = new TreeInstance();
                tree.position = t.position;
                tree.rotation = t.rotationY;
                tree.heightScale = t.verticalScale;
                tree.widthScale = t.horizontalScale;
                if (minProtoIndex == maxProtoIndex)
                {
                    tree.prototypeIndex = minProtoIndex;
                }
                else
                {
                    tree.prototypeIndex = Random.Range(minProtoIndex, maxProtoIndex + 1);
                }

                instances.Add(tree);
            }
        }

        /// <summary>
        /// Converts generated detail density maps into Unity Terrain detail layers.
        /// </summary>
        /// <param name="templates">Detail templates paired with <paramref name="densityMaps"/> by index.</param>
        /// <param name="densityMaps">Generated density maps for the supplied templates.</param>
        /// <returns>A progressive task that completes after all detail layers have been written.</returns>
        /// <remarks>
        /// Duplicated templates are merged first, then each distinct template is expanded into one or more
        /// <see cref="DetailPrototype"/> entries depending on authored variants. Density is redistributed across the
        /// generated prototypes and written patch-by-patch over multiple frames.
        /// </remarks>
        public ProgressiveTask PopulateDetailDensity(List<DetailTemplate> templates, List<RenderTexture> densityMaps)
        {
            ProgressiveTask task = new ProgressiveTask();
            CoroutineUtility.StartCoroutine(PopulateDetailDensityProgressive(task, templates, densityMaps));
            return task;
        }

        /// <summary>
        /// Clears all Unity Terrain detail prototypes and density data from this tile.
        /// </summary>
        public ProgressiveTask ClearDetailDensity()
        {
            ProgressiveTask task = new ProgressiveTask();
            CoroutineUtility.StartCoroutine(ClearDetailDensityProgressive(task));
            return task;
        }

        private IEnumerator PopulateDetailDensityProgressive(ProgressiveTask task, List<DetailTemplate> templates, List<RenderTexture> densityMaps)
        {
            int resolution = terrain.terrainData.detailResolution;
            List<DetailTemplate> distinctTemplates;
            List<Texture2D> preparedDensityMaps;
            PrepareDetailDensityMapsForCpuRead(templates, densityMaps, resolution, out distinctTemplates, out preparedDensityMaps);

            //Create detail prototypes
            List<DetailPrototype> prototypes = new List<DetailPrototype>();
            List<Texture2D> densityMapByProto = new List<Texture2D>();
            List<float> baseDensityByProto = new List<float>();
            for (int i = 0; i < distinctTemplates.Count; ++i)
            {
                DetailTemplate template = distinctTemplates[i];
                List<DetailPrototype> prototypesFromTemplates = CreateDetailPrototypesFromTemplate(template);
                prototypes.AddRange(prototypesFromTemplates);

                float baseDensity = template.density * 1.0f / prototypesFromTemplates.Count;
                for (int j = 0; j < prototypesFromTemplates.Count; ++j)
                {
                    densityMapByProto.Add(preparedDensityMaps[i]);
                    baseDensityByProto.Add(baseDensity);
                }
            }

            //Populate
            terrain.terrainData.detailPrototypes = prototypes.ToArray();
            CoroutineHandle[] coroutines = new CoroutineHandle[prototypes.Count];
            int layerIndex = 0;
            for (int i = 0; i < prototypes.Count; ++i)
            {
                Texture2D dm = densityMapByProto[i];
                float baseDensity = baseDensityByProto[i];
                CoroutineHandle c = CoroutineUtility.StartCoroutine(PopulateLayerDensityProgressive(layerIndex, dm, baseDensity));
                coroutines[i] = c;
                layerIndex += 1;
            }

            foreach (CoroutineHandle c in coroutines)
            {
                yield return c.coroutine;
            }

            for (int i = 0; i < preparedDensityMaps.Count; ++i)
            {
                Object.DestroyImmediate(preparedDensityMaps[i]);
            }

            task.Complete();
            yield break;
        }

        private class DetailDensityBucket
        {
            public DetailTemplate template;
            public List<RenderTexture> densityMaps = new List<RenderTexture>();
            public bool needsMergeOrRescale;
        }

        private void PrepareDetailDensityMapsForCpuRead(
            List<DetailTemplate> templates,
            List<RenderTexture> densityMaps,
            int resolution,
            out List<DetailTemplate> distinctTemplates,
            out List<Texture2D> cpuDensityMaps)
        {
            distinctTemplates = new List<DetailTemplate>();
            cpuDensityMaps = new List<Texture2D>();

            if (templates == null || densityMaps == null || templates.Count == 0 || densityMaps.Count == 0)
            {
                return;
            }

            // Group incoming density maps by distinct template and mark buckets that need merge/rescale work.
            List<DetailDensityBucket> buckets = new List<DetailDensityBucket>();

            for (int i = 0; i < templates.Count; ++i)
            {
                DetailTemplate t = templates[i];
                int distinctIndex = distinctTemplates.IndexOf(t);
                if (distinctIndex >= 0)
                {
                    buckets[distinctIndex].needsMergeOrRescale = true;
                }
                else
                {
                    distinctIndex = distinctTemplates.Count;
                    distinctTemplates.Add(t);
                    buckets.Add(new DetailDensityBucket()
                    {
                        template = t,
                        needsMergeOrRescale = false
                    });
                }

                RenderTexture densityMap = densityMaps[i];
                buckets[distinctIndex].densityMaps.Add(densityMap);
                if (densityMap == null || densityMap.width != resolution || densityMap.height != resolution)
                {
                    buckets[distinctIndex].needsMergeOrRescale = true;
                }
            }

            // Produce one CPU-readable density map per distinct template.
            for (int i = 0; i < distinctTemplates.Count; ++i)
            {
                DetailDensityBucket bucket = buckets[i];
                if (!bucket.needsMergeOrRescale)
                {
                    // Fast path: a single density map already matches the target resolution.
                    RenderTexture densityMap = bucket.densityMaps[0];
                    Texture2D cpuDensityMap = new Texture2D(resolution, resolution, TextureFormat.RFloat, false);
                    GraphicsUtils.ReadRenderTexture(densityMap, cpuDensityMap);
                    cpuDensityMaps.Add(cpuDensityMap);
                }
                else
                {
                    // Slow path: merge duplicates and/or rescale mismatched inputs through a temporary RT.
                    RenderTexture mergedDensityMap = GraphicsUtils.GetBlankTempRT(resolution, RenderTextureFormat.RFloat);
                    for (int j = 0; j < bucket.densityMaps.Count; ++j)
                    {
                        RenderTexture densityMap = bucket.densityMaps[j];
                        if (densityMap != null)
                        {
                            Drawing.BlitAdd(densityMap, mergedDensityMap);
                        }
                    }

                    Texture2D cpuDensityMap = new Texture2D(resolution, resolution, TextureFormat.RFloat, false);
                    GraphicsUtils.ReadRenderTexture(mergedDensityMap, cpuDensityMap);
                    cpuDensityMaps.Add(cpuDensityMap);

                    GraphicsUtils.ReleaseTempRT(mergedDensityMap);
                }
            }
        }

        private IEnumerator ClearDetailDensityProgressive(ProgressiveTask task)
        {
            int prototypeCount = terrain.terrainData.detailPrototypes.Length;
            int resolution = terrain.terrainData.detailResolution;
            int resolutionPerPatch = terrain.terrainData.detailResolutionPerPatch;

            if (prototypeCount > 0 && resolution > 0 && resolutionPerPatch > 0)
            {
                int stepCount = resolution / resolutionPerPatch;
                int[,] zeroDensityArray = new int[resolutionPerPatch, resolution];
                for (int layerIndex = 0; layerIndex < prototypeCount; ++layerIndex)
                {
                    for (int i = 0; i < stepCount; ++i)
                    {
                        int baseX = 0;
                        int baseY = i * resolutionPerPatch;
                        terrain.terrainData.SetDetailLayer(baseX, baseY, layerIndex, zeroDensityArray);
                        yield return null;
                    }
                }
            }

            terrain.terrainData.detailPrototypes = System.Array.Empty<DetailPrototype>();
            task.Complete();
            yield break;
        }

        private IEnumerator PopulateLayerDensityProgressive(int layerIndex, Texture2D densityMap, float density)
        {
            if (densityMap == null)
            {
                yield break;
            }

            int resolution = terrain.terrainData.detailResolution;
            int resolutionPerPatch = terrain.terrainData.detailResolutionPerPatch;

            int stepCount = resolution / resolutionPerPatch;
            int[,] densityArray = new int[resolutionPerPatch, resolution];
            for (int i = 0; i < stepCount; ++i)
            {
                int baseX = 0;
                int baseY = i * resolutionPerPatch;
                int blockWidth = resolution;
                int blockHeight = resolutionPerPatch;

                Color[] data = densityMap.GetPixels(baseX, baseY, blockWidth, blockHeight, 0);
                FillDensityArray(densityArray, blockWidth, blockHeight, data, density, layerIndex);
                terrain.terrainData.SetDetailLayer(baseX, baseY, layerIndex, densityArray);
                yield return null;
            }
            yield break;
        }

        private List<DetailPrototype> CreateDetailPrototypesFromTemplate(DetailTemplate t)
        {
            List<DetailPrototype> prototypes = new List<DetailPrototype>();
            if (!t.IsValid())
                return prototypes;

            DetailPrototype p = new DetailPrototype();
            p.renderMode = t.renderMode;
            p.healthyColor = t.primaryColor;
            p.dryColor = t.secondaryColor;
            p.minWidth = t.minWidth;
            p.minHeight = t.minHeight;
            p.maxWidth = t.maxWidth;
            p.maxHeight = t.maxHeight;
            p.noiseSpread = t.noiseSpread;
            p.holeEdgePadding = t.holeEdgePadding;
            p.usePrototypeMesh = t.renderMode == DetailRenderMode.VertexLit;
#if UNITY_2021_2_OR_NEWER
            if (t.renderMode == DetailRenderMode.VertexLit)
            {
                p.useInstancing = t.useInstancing;
            }
            else
            {
                p.useInstancing = false;
            }
#endif

            if (t.renderMode == DetailRenderMode.VertexLit)
            {
                p.prototype = t.prefab;
            }
            else
            {
                p.prototypeTexture = t.texture;
            }
            prototypes.Add(p);

            if (t.renderMode == DetailRenderMode.VertexLit)
            {
                GameObject[] variants = t.prefabVariants;
                foreach (GameObject v in variants)
                {
                    if (v == null)
                        continue;
                    DetailPrototype p0 = new DetailPrototype(p);
                    p0.prototype = v;
                    prototypes.Add(p0);
                }
            }
            else
            {
                Texture2D[] variants = t.textureVariants;

                foreach (Texture2D v in variants)
                {
                    if (v == null)
                        continue;
                    DetailPrototype p0 = new DetailPrototype(p);
                    p0.prototypeTexture = v;
                    prototypes.Add(p0);
                }
            }

            return prototypes;
        }

        private void FillDensityArray(int[,] array, int width, int height, Color[] data, float baseDensity, int randomSeed)
        {
            Random.InitState(randomSeed * 12345);
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    float densityFloat = (data[y * width + x].r * baseDensity);
                    int densityInt = (int)densityFloat;
                    float remainder = densityFloat - densityInt;
                    if (remainder > Random.value)
                    {
                        densityInt += 1;
                    }
                    array[y, x] = densityInt;
                }
            }
        }

        /// <summary>
        /// Spawns object prefabs from generated instance buffers under the terrain.
        /// </summary>
        /// <param name="templates">Object templates paired with <paramref name="sampleBuffers"/> by index.</param>
        /// <param name="sampleBuffers">Generated object instance buffers.</param>
        /// <param name="objectPopulateArgs">Options that control progressive spawn cadence.</param>
        /// <returns>A progressive task that completes after all eligible object instances have been spawned.</returns>
        /// <remarks>
        /// Existing spawned-object hierarchy under the terrain is cleared first. Each valid sample chooses from the primary
        /// prefab plus any variants, converts normalized sample coordinates into terrain-local placement, applies sampled
        /// scale and rotation, and optionally aligns to the terrain normal.
        /// </remarks>
        public ProgressiveTask PopulateObject(List<ObjectTemplate> templates, List<ComputeBuffer> sampleBuffers, VistaManager.ObjectPopulateArgs objectPopulateArgs)
        {
            ProgressiveTask task = new ProgressiveTask();
            CoroutineUtility.StartCoroutine(PopulateObjectProgressive(task, templates, sampleBuffers, objectPopulateArgs));
            return task;
        }

        /// <summary>
        /// Clears spawned object prefabs owned by this terrain tile.
        /// </summary>
        /// <returns>A progressive task that completes after the spawned-object hierarchy is removed.</returns>
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

            Vector3 terrainSize = terrain.terrainData.size;
            for (int i = 0; i < instanceCount; ++i)
            {
                if (mainRoot == null || prefabRoot == null)
                {
                    yield break;
                }

                InstanceSample sample = samples[i];
                if (sample.isValid == 0)
                    continue;
                GameObject prefab;
                if (prefabCount == 1)
                {
                    prefab = prefabs[0];
                }
                else
                {
                    prefab = prefabs[Random.Range(0, prefabs.Count)];
                }

                Vector3 localPosition = new Vector3(
                    Mathf.Lerp(0, terrainSize.x, sample.position.x),
                    terrain.terrainData.GetInterpolatedHeight(sample.position.x, sample.position.z),
                    Mathf.Lerp(0, terrainSize.z, sample.position.z));
                Quaternion localRotation = Quaternion.Euler(0, sample.rotationY * Mathf.Rad2Deg, 0);
                Vector3 baseScale = prefab.transform.localScale;
                Vector3 localScale = new Vector3(sample.horizontalScale, sample.verticalScale, sample.horizontalScale);
                localScale.Scale(baseScale);

                if (template.alignToNormal)
                {
                    Vector3 normalVector = terrain.terrainData.GetInterpolatedNormal(sample.position.x, sample.position.z);
                    float errorFactor = Random.Range(1 - template.normalAlignmentError, 1 + template.normalAlignmentError);
                    normalVector = Vector3.LerpUnclamped(Vector3.up, normalVector, errorFactor);
                    Quaternion alignmentRotation = Quaternion.FromToRotation(Vector3.up, normalVector);
                    localRotation *= alignmentRotation;
                }

                GameObject instance = SpawnUtilities.Spawn(prefab);
                instance.transform.parent = prefabRoot;
                instance.transform.localPosition = localPosition;
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
        /// Forwards generic texture outputs to listeners attached to this tile.
        /// </summary>
        /// <param name="labels">Labels paired with <paramref name="textures"/> by index.</param>
        /// <param name="textures">Generic texture outputs produced for this tile.</param>
        public void PopulateGenericTextures(List<string> labels, List<RenderTexture> textures)
        {
            populateGenericTexturesCallback?.Invoke(labels, textures);
        }

        /// <summary>
        /// Forwards generic buffer outputs to listeners attached to this tile.
        /// </summary>
        /// <param name="labels">Labels paired with <paramref name="buffers"/> by index.</param>
        /// <param name="buffers">Generic buffer outputs produced for this tile.</param>
        public void PopulateGenericBuffers(List<string> labels, List<ComputeBuffer> buffers)
        {
            populateGenericBuffersCallback?.Invoke(labels, buffers);
        }

        /// <summary>
        /// Called by <see cref="VistaManager"/> before this tile starts receiving generated data.
        /// </summary>
        /// <remarks>
        /// The Unity Terrain backend does not currently require pre-apply setup here.
        /// </remarks>
        public void OnBeforeApplyingData()
        {
        }

        /// <summary>
        /// Called by <see cref="VistaManager"/> after all generated data has been applied to this tile.
        /// </summary>
        /// <remarks>
        /// The Unity Terrain backend does not currently require post-apply cleanup here.
        /// </remarks>
        public void OnAfterApplyingData()
        {
        }

        /// <summary>
        /// Draws this terrain's height contribution into a shared scene-height render texture.
        /// </summary>
        /// <param name="targetRt">The destination texture representing the requested scene-height region.</param>
        /// <param name="requestedWorldRect">The world-space rectangle encoded by <paramref name="targetRt"/>.</param>
        /// <remarks>
        /// The method converts this terrain's world bounds into UV corners relative to the requested rect, then uses
        /// <see cref="TerrainTileUtilities.DecodeAndDrawHeightMap"/> to decode Unity's packed heightmap into the shared
        /// destination texture.
        /// </remarks>
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

            RenderTexture terrainHeightMap = terrain.terrainData.heightmapTexture;
            TerrainTileUtilities.DecodeAndDrawHeightMap(targetRt, terrainHeightMap, uvCorner);
        }
    }
}
#endif


