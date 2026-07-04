#if VISTA
using System.Collections.Generic;
using UnityEngine;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Container for all generated outputs produced by a biome request or biome-blending pass.
    /// </summary>
    /// <remarks>
    /// A <see cref="BiomeData"/> instance owns the textures and buffers assigned to it.
    /// The generation pipeline passes this object between graph execution, biome blending, bounds remapping, and tile population.
    /// Call <see cref="Dispose"/> when the data is no longer needed to release GPU resources.
    /// </remarks>
    public class BiomeData : System.IDisposable
    {
        /// <summary>
        /// Gets or sets the generated height map.
        /// </summary>
        /// <remarks>
        /// This texture is typically consumed by geometry populators such as terrain tiles.
        /// </remarks>
        public RenderTexture heightMap { get; set; }
        /// <summary>
        /// Gets or sets the generated hole map.
        /// </summary>
        public RenderTexture holeMap { get; set; }
        /// <summary>
        /// Gets or sets the generated mesh density map.
        /// </summary>
        public RenderTexture meshDensityMap { get; set; }
        /// <summary>
        /// Gets or sets the generated albedo map.
        /// </summary>
        public RenderTexture albedoMap { get; set; }
        /// <summary>
        /// Gets or sets the generated metallic or smoothness-related map.
        /// </summary>
        public RenderTexture metallicMap { get; set; }

        internal Collector<TerrainLayer> m_terrainLayers;
        internal Collector<RenderTexture> m_layerWeights;

        internal Collector<TreeTemplate> m_treeTemplates;
        internal Collector<ComputeBuffer> m_treeBuffers;

        internal Collector<DetailTemplate> m_detailTemplates_Density;
        internal Collector<RenderTexture> m_detailDensityMaps;

        internal Collector<DetailTemplate> m_detailTemplates_Instances;
        internal Collector<ComputeBuffer> m_detailInstanceBuffers;

        internal Collector<ObjectTemplate> m_objectTemplates;
        internal Collector<ComputeBuffer> m_objectBuffers;

        internal Collector<string> m_genericTextureLabels;
        internal Collector<RenderTexture> m_genericTextures;

        internal Collector<string> m_genericBufferLabels;
        internal Collector<ComputeBuffer> m_genericBuffers;

        /// <summary>
        /// Gets or sets the biome mask map associated with this data set.
        /// </summary>
        /// <remarks>
        /// This mask is used by multi-biome blending code to determine where this biome contributes to the final result.
        /// </remarks>
        public RenderTexture biomeMaskMap { get; set; }

        /// <summary>
        /// Initializes an empty biome data container with collectors for every supported output category.
        /// </summary>
        public BiomeData()
        {
            m_terrainLayers = new Collector<TerrainLayer>();
            m_layerWeights = new Collector<RenderTexture>();

            m_treeTemplates = new Collector<TreeTemplate>();
            m_treeBuffers = new Collector<ComputeBuffer>();

            m_detailTemplates_Density = new Collector<DetailTemplate>();
            m_detailDensityMaps = new Collector<RenderTexture>();

            m_detailTemplates_Instances = new Collector<DetailTemplate>();
            m_detailInstanceBuffers = new Collector<ComputeBuffer>();

            m_objectTemplates = new Collector<ObjectTemplate>();
            m_objectBuffers = new Collector<ComputeBuffer>();

            m_genericTextureLabels = new Collector<string>();
            m_genericTextures = new Collector<RenderTexture>();

            m_genericBufferLabels = new Collector<string>();
            m_genericBuffers = new Collector<ComputeBuffer>();
        }

        /// <summary>
        /// Adds one terrain layer and its corresponding weight texture.
        /// </summary>
        /// <param name="desc">Terrain layer that the weight texture belongs to.</param>
        /// <param name="texture">Weight texture for <paramref name="desc"/>.</param>
        /// <remarks>
        /// Layer and weight ordering is significant. Consumers assume both collections stay index-aligned.
        /// </remarks>
        public void AddTextureLayer(TerrainLayer desc, RenderTexture texture)
        {
            m_terrainLayers.Add(desc);
            m_layerWeights.Add(texture);
        }

        /// <summary>
        /// Adds multiple terrain layers and their corresponding weight textures.
        /// </summary>
        public void AddTextureLayers(List<TerrainLayer> layers, List<RenderTexture> weights)
        {
            ValidatePairedCollections(layers, weights, nameof(layers), nameof(weights));
            for (int i = 0; i < layers.Count; ++i)
            {
                m_terrainLayers.Add(layers[i]);
                m_layerWeights.Add(weights[i]);
            }
        }

        /// <summary>
        /// Copies the stored terrain layers and weight textures into caller-provided lists.
        /// </summary>
        /// <param name="layers">Destination list that receives terrain layers.</param>
        /// <param name="weights">Destination list that receives weight textures.</param>
        /// <remarks>
        /// The destination lists are cleared before data is appended.
        /// Indices in <paramref name="layers"/> and <paramref name="weights"/> are guaranteed to match.
        /// </remarks>
        public void GetLayerWeights(List<TerrainLayer> layers, List<RenderTexture> weights)
        {
            layers.Clear();
            layers.AddRange(m_terrainLayers);
            weights.Clear();
            weights.AddRange(m_layerWeights);
        }

        /// <summary>
        /// Appends the stored terrain layers and weight textures into caller-provided lists.
        /// </summary>
        /// <param name="layers">Destination list that receives terrain layers.</param>
        /// <param name="weights">Destination list that receives weight textures.</param>
        /// <remarks>
        /// Existing list contents are preserved.
        /// The appended entries preserve source ordering, but callers are responsible for ensuring
        /// both destination lists had matching counts before this method is called.
        /// </remarks>
        public void GetLayerWeightsAppended(List<TerrainLayer> layers, List<RenderTexture> weights)
        {
            layers.AddRange(m_terrainLayers);
            weights.AddRange(m_layerWeights);
        }

        /// <summary>
        /// Gets the number of stored terrain-layer weight pairs.
        /// </summary>
        /// <returns>The number of weight textures currently stored in this data set.</returns>
        public int GetLayerCount()
        {
            if (m_layerWeights != null)
            {
                return m_layerWeights.Count;
            }
            else
            {
                return 0;
            }
        }

        /// <summary>
        /// Gets the number of stored tree template/buffer pairs.
        /// </summary>
        /// <returns>The number of tree instance buffers currently stored in this data set.</returns>
        public int GetTreeCount()
        {
            if (m_treeBuffers != null)
            {
                return m_treeBuffers.Count;
            }
            else
            {
                return 0;
            }
        }

        /// <summary>
        /// Adds one tree template and its corresponding instance buffer.
        /// </summary>
        /// <param name="template">Tree template referenced by the instance buffer.</param>
        /// <param name="buffer">Buffer containing packed tree instances for <paramref name="template"/>.</param>
        /// <remarks>
        /// Template and buffer ordering is significant. Consumers assume both collections stay index-aligned.
        /// </remarks>
        public void AddTree(TreeTemplate template, ComputeBuffer buffer)
        {
            m_treeTemplates.Add(template);
            m_treeBuffers.Add(buffer);
        }

        /// <summary>
        /// Adds multiple tree templates and their corresponding instance buffers.
        /// </summary>
        public void AddTrees(List<TreeTemplate> templates, List<ComputeBuffer> buffers)
        {
            ValidatePairedCollections(templates, buffers, nameof(templates), nameof(buffers));
            for (int i = 0; i < templates.Count; ++i)
            {
                m_treeTemplates.Add(templates[i]);
                m_treeBuffers.Add(buffers[i]);
            }
        }

        /// <summary>
        /// Copies the stored tree templates and tree instance buffers into caller-provided lists.
        /// </summary>
        /// <param name="templates">Destination list that receives tree templates.</param>
        /// <param name="buffers">Destination list that receives tree instance buffers.</param>
        public void GetTrees(List<TreeTemplate> templates, List<ComputeBuffer> buffers)
        {
            templates.Clear();
            templates.AddRange(m_treeTemplates);
            buffers.Clear();
            buffers.AddRange(m_treeBuffers);
        }

        /// <summary>
        /// Appends the stored tree templates and tree instance buffers into caller-provided lists.
        /// </summary>
        /// <param name="templates">Destination list that receives tree templates.</param>
        /// <param name="buffers">Destination list that receives tree instance buffers.</param>
        /// <remarks>
        /// Existing list contents are preserved.
        /// The appended entries preserve source ordering, but callers are responsible for ensuring
        /// both destination lists had matching counts before this method is called.
        /// </remarks>
        public void GetTreesAppended(List<TreeTemplate> templates, List<ComputeBuffer> buffers)
        {
            templates.AddRange(m_treeTemplates);
            buffers.AddRange(m_treeBuffers);
        }

        /// <summary>
        /// Adds one object template and its corresponding instance buffer.
        /// </summary>
        /// <param name="template">Object template referenced by the instance buffer.</param>
        /// <param name="buffer">Buffer containing packed object instances for <paramref name="template"/>.</param>
        public void AddObject(ObjectTemplate template, ComputeBuffer buffer)
        {
            m_objectTemplates.Add(template);
            m_objectBuffers.Add(buffer);
        }

        /// <summary>
        /// Adds multiple object templates and their corresponding instance buffers.
        /// </summary>
        public void AddObjects(List<ObjectTemplate> templates, List<ComputeBuffer> buffers)
        {
            ValidatePairedCollections(templates, buffers, nameof(templates), nameof(buffers));
            for (int i = 0; i < templates.Count; ++i)
            {
                m_objectTemplates.Add(templates[i]);
                m_objectBuffers.Add(buffers[i]);
            }
        }

        /// <summary>
        /// Copies the stored object templates and object instance buffers into caller-provided lists.
        /// </summary>
        /// <param name="templates">Destination list that receives object templates.</param>
        /// <param name="buffers">Destination list that receives object instance buffers.</param>
        public void GetObjects(List<ObjectTemplate> templates, List<ComputeBuffer> buffers)
        {
            templates.Clear();
            templates.AddRange(m_objectTemplates);
            buffers.Clear();
            buffers.AddRange(m_objectBuffers);
        }

        /// <summary>
        /// Gets the number of stored object template/buffer pairs.
        /// </summary>
        /// <returns>The number of object instance buffers currently stored in this data set.</returns>
        public int GetObjectCount()
        {
            if (m_objectBuffers != null)
            {
                return m_objectBuffers.Count;
            }
            else
            {
                return 0;
            }
        }

        /// <summary>
        /// Adds one detail template and its corresponding density map.
        /// </summary>
        /// <param name="template">Detail template referenced by the density map.</param>
        /// <param name="densityMap">Density map for <paramref name="template"/>.</param>
        public void AddDetailDensity(DetailTemplate template, RenderTexture densityMap)
        {
            m_detailTemplates_Density.Add(template);
            m_detailDensityMaps.Add(densityMap);
        }

        /// <summary>
        /// Adds multiple detail templates and their corresponding density maps.
        /// </summary>
        public void AddDetailDensities(List<DetailTemplate> templates, List<RenderTexture> densityMaps)
        {
            ValidatePairedCollections(templates, densityMaps, nameof(templates), nameof(densityMaps));
            for (int i = 0; i < templates.Count; ++i)
            {
                m_detailTemplates_Density.Add(templates[i]);
                m_detailDensityMaps.Add(densityMaps[i]);
            }
        }

        /// <summary>
        /// Copies the stored detail templates and density maps into caller-provided lists.
        /// </summary>
        /// <param name="templates">Destination list that receives detail templates.</param>
        /// <param name="densityMaps">Destination list that receives density maps.</param>
        public void GetDensityMaps(List<DetailTemplate> templates, List<RenderTexture> densityMaps)
        {
            templates.Clear();
            templates.AddRange(m_detailTemplates_Density);
            densityMaps.Clear();
            densityMaps.AddRange(m_detailDensityMaps);
        }

        /// <summary>
        /// Gets the number of stored detail density template/map pairs.
        /// </summary>
        /// <returns>The number of detail density maps currently stored in this data set.</returns>
        public int GetDensityMapCount()
        {
            if (m_detailDensityMaps != null)
            {
                return m_detailDensityMaps.Count;
            }
            else
            {
                return 0;
            }
        }

        /// <summary>
        /// Gets the maximum resolution among all stored detail density maps.
        /// </summary>
        /// <returns>The largest width found in the stored density maps, or 0 if none exist.</returns>
        public int GetMaxDensityMapResolution()
        {
            int maxResolution = 0;
            if (m_detailDensityMaps != null)
            {
                for (int i = 0; i < m_detailDensityMaps.Count; ++i)
                {
                    RenderTexture densityMap = m_detailDensityMaps.At(i);
                    if (densityMap != null)
                    {
                        maxResolution = Mathf.Max(maxResolution, densityMap.width);
                    }
                }
            }

            return maxResolution;
        }

        /// <summary>
        /// Appends the stored detail templates and density maps into caller-provided lists.
        /// </summary>
        /// <param name="templates">Destination list that receives detail templates.</param>
        /// <param name="densityMaps">Destination list that receives density maps.</param>
        /// <remarks>
        /// Existing list contents are preserved.
        /// The appended entries preserve source ordering, but callers are responsible for ensuring
        /// both destination lists had matching counts before this method is called.
        /// </remarks>
        public void GetDensityMapsAppended(List<DetailTemplate> templates, List<RenderTexture> densityMaps)
        {
            templates.AddRange(m_detailTemplates_Density);
            densityMaps.AddRange(m_detailDensityMaps);
        }

        /// <summary>
        /// Adds one detail template and its corresponding instance buffer.
        /// </summary>
        /// <param name="template">Detail template referenced by the instance buffer.</param>
        /// <param name="buffer">Buffer containing packed detail instances for <paramref name="template"/>.</param>
        public void AddDetailInstance(DetailTemplate template, ComputeBuffer buffer)
        {
            m_detailTemplates_Instances.Add(template);
            m_detailInstanceBuffers.Add(buffer);
        }

        /// <summary>
        /// Adds multiple detail templates and their corresponding instance buffers.
        /// </summary>
        public void AddDetailInstances(List<DetailTemplate> templates, List<ComputeBuffer> buffers)
        {
            ValidatePairedCollections(templates, buffers, nameof(templates), nameof(buffers));
            for (int i = 0; i < templates.Count; ++i)
            {
                m_detailTemplates_Instances.Add(templates[i]);
                m_detailInstanceBuffers.Add(buffers[i]);
            }
        }

        /// <summary>
        /// Copies the stored detail templates and detail instance buffers into caller-provided lists.
        /// </summary>
        /// <param name="templates">Destination list that receives detail templates.</param>
        /// <param name="buffers">Destination list that receives detail instance buffers.</param>
        public void GetDetailInstances(List<DetailTemplate> templates, List<ComputeBuffer> buffers)
        {
            templates.Clear();
            templates.AddRange(m_detailTemplates_Instances);
            buffers.Clear();
            buffers.AddRange(m_detailInstanceBuffers);
        }

        /// <summary>
        /// Gets the number of stored detail instance template/buffer pairs.
        /// </summary>
        /// <returns>The number of detail instance buffers currently stored in this data set.</returns>
        public int GetDetailInstanceCount()
        {
            if (m_detailInstanceBuffers != null)
            {
                return m_detailInstanceBuffers.Count;
            }
            else
            {
                return 0;
            }
        }

        /// <summary>
        /// Appends the stored detail templates and detail instance buffers into caller-provided lists.
        /// </summary>
        /// <param name="templates">Destination list that receives detail templates.</param>
        /// <param name="buffers">Destination list that receives detail instance buffers.</param>
        /// <remarks>
        /// Existing list contents are preserved.
        /// The appended entries preserve source ordering, but callers are responsible for ensuring
        /// both destination lists had matching counts before this method is called.
        /// </remarks>
        public void GetDetailInstancesAppended(List<DetailTemplate> templates, List<ComputeBuffer> buffers)
        {
            templates.AddRange(m_detailTemplates_Instances);
            buffers.AddRange(m_detailInstanceBuffers);
        }

        /// <summary>
        /// Adds one labeled generic texture output.
        /// </summary>
        /// <param name="label">Application-defined label that identifies the output.</param>
        /// <param name="texture">Texture associated with <paramref name="label"/>.</param>
        public void AddGenericTexture(string label, RenderTexture texture)
        {
            m_genericTextureLabels.Add(label);
            m_genericTextures.Add(texture);
        }

        /// <summary>
        /// Copies the stored generic texture labels and textures into caller-provided lists.
        /// </summary>
        /// <param name="labels">Destination list that receives output labels.</param>
        /// <param name="textures">Destination list that receives output textures.</param>
        public void GetGenericTextures(List<string> labels, List<RenderTexture> textures)
        {
            labels.Clear();
            labels.AddRange(m_genericTextureLabels);
            textures.Clear();
            textures.AddRange(m_genericTextures);
        }

        /// <summary>
        /// Gets the number of stored generic texture label/texture pairs.
        /// </summary>
        /// <returns>The number of generic textures currently stored in this data set.</returns>
        public int GetGenericTextureCount()
        {
            if (m_genericTextures != null)
            {
                return m_genericTextures.Count;
            }
            else
            {
                return 0;
            }
        }

        /// <summary>
        /// Adds one labeled generic buffer output.
        /// </summary>
        /// <param name="label">Application-defined label that identifies the output.</param>
        /// <param name="buffer">Buffer associated with <paramref name="label"/>.</param>
        public void AddGenericBuffer(string label, ComputeBuffer buffer)
        {
            m_genericBufferLabels.Add(label);
            m_genericBuffers.Add(buffer);
        }

        /// <summary>
        /// Copies the stored generic buffer labels and buffers into caller-provided lists.
        /// </summary>
        /// <param name="labels">Destination list that receives output labels.</param>
        /// <param name="buffers">Destination list that receives output buffers.</param>
        public void GetGenericBuffers(List<string> labels, List<ComputeBuffer> buffers)
        {
            labels.Clear();
            labels.AddRange(m_genericBufferLabels);
            buffers.Clear();
            buffers.AddRange(m_genericBuffers);
        }

        /// <summary>
        /// Gets the number of stored generic buffer label/buffer pairs.
        /// </summary>
        /// <returns>The number of generic buffers currently stored in this data set.</returns>
        public int GetGenericBufferCount()
        {
            if (m_genericBuffers != null)
            {
                return m_genericBuffers.Count;
            }
            else
            {
                return 0;
            }
        }

        /// <summary>
        /// Releases all textures and compute buffers owned by this data set and clears every internal collection.
        /// </summary>
        /// <remarks>
        /// Render textures are released and destroyed immediately. Compute buffers are released.
        /// After disposal, texture properties are set to <see langword="null"/> and collectors are emptied, so repeated calls are safe.
        /// This method assumes the resources stored here are owned exclusively by this <see cref="BiomeData"/> instance.
        /// </remarks>
        public void Dispose()
        {
            if (heightMap != null)
            {
                heightMap.Release();
                Object.DestroyImmediate(heightMap);
                heightMap = null;
            }
            if (holeMap != null)
            {
                holeMap.Release();
                Object.DestroyImmediate(holeMap);
                holeMap = null;
            }
            if (meshDensityMap != null)
            {
                meshDensityMap.Release();
                Object.DestroyImmediate(meshDensityMap);
                meshDensityMap = null;
            }
            if (albedoMap != null)
            {
                albedoMap.Release();
                Object.DestroyImmediate(albedoMap);
                albedoMap = null;
            }
            if (metallicMap != null)
            {
                metallicMap.Release();
                Object.DestroyImmediate(metallicMap);
                metallicMap = null;
            }
            foreach (RenderTexture t in m_layerWeights)
            {
                if (t != null)
                {
                    t.Release();
                    Object.DestroyImmediate(t);
                }
            }
            m_terrainLayers.Clear();
            m_layerWeights.Clear();

            foreach (ComputeBuffer b in m_treeBuffers)
            {
                if (b != null)
                {
                    b.Release();
                }
            }
            m_treeTemplates.Clear();
            m_treeBuffers.Clear();

            foreach (ComputeBuffer b in m_objectBuffers)
            {
                if (b != null)
                {
                    b.Release();
                }
            }
            m_objectTemplates.Clear();
            m_objectBuffers.Clear();

            foreach (RenderTexture t in m_detailDensityMaps)
            {
                if (t != null)
                {
                    t.Release();
                    Object.DestroyImmediate(t);
                }
            }
            m_detailTemplates_Density.Clear();
            m_detailDensityMaps.Clear();

            foreach (ComputeBuffer b in m_detailInstanceBuffers)
            {
                if (b != null)
                {
                    b.Release();
                }
            }
            m_detailTemplates_Instances.Clear();
            m_detailInstanceBuffers.Clear();

            foreach (RenderTexture t in m_genericTextures)
            {
                if (t != null)
                {
                    t.Release();
                    Object.DestroyImmediate(t);
                }
            }
            m_genericTextureLabels.Clear();
            m_genericTextures.Clear();

            foreach (ComputeBuffer b in m_genericBuffers)
            {
                if (b != null)
                {
                    b.Release();
                }
            }
            m_genericBufferLabels.Clear();
            m_genericBuffers.Clear();

            if (biomeMaskMap != null)
            {
                biomeMaskMap.Release();
                Object.DestroyImmediate(biomeMaskMap);
                biomeMaskMap = null;
            }
        }

        private static void ValidatePairedCollections<TFirst, TSecond>(
            List<TFirst> first,
            List<TSecond> second,
            string firstName,
            string secondName)
        {
            if (first == null)
            {
                throw new System.ArgumentNullException(firstName);
            }

            if (second == null)
            {
                throw new System.ArgumentNullException(secondName);
            }

            if (first.Count != second.Count)
            {
                throw new System.ArgumentException($"{firstName} and {secondName} must have the same element count.");
            }

            for (int i = 0; i < first.Count; ++i)
            {
                if (first[i] == null)
                {
                    throw new System.ArgumentException($"{firstName} must not contain null elements.", firstName);
                }

                if (second[i] == null)
                {
                    throw new System.ArgumentException($"{secondName} must not contain null elements.", secondName);
                }
            }
        }
    }
}
#endif


