#if VISTA

namespace Pinwheel.Vista
{
    [System.Flags]
    /// <summary>
    /// Bitmask that selects which output channels a biome graph should generate for a request.
    /// </summary>
    /// <remarks>
    /// This mask is passed into <c>TerrainGraphUtilities.RequestBiomeData</c> and used to skip whole categories of graph output collection.
    /// Disabling a flag does not remove nodes from the graph; it only prevents the runtime from requesting and storing that output category.
    /// </remarks>
    public enum BiomeDataMask : int
    {
        /// <summary>
        /// Request height map output.
        /// </summary>
        HeightMap = 1,
        /// <summary>
        /// Request terrain hole map output.
        /// </summary>
        HoleMap = 2,
        /// <summary>
        /// Request mesh density output.
        /// </summary>
        /// <remarks>
        /// This is typically consumed by terrain systems that support density-driven mesh placement or tessellation-related workflows.
        /// </remarks>
        MeshDensityMap = 4,
        /// <summary>
        /// Request albedo or diffuse texture output.
        /// </summary>
        AlbedoMap = 8,
        /// <summary>
        /// Request metallic or smoothness-related texture output.
        /// </summary>
        MetallicMap = 16,
        /// <summary>
        /// Request terrain layer weight maps.
        /// </summary>
        /// <remarks>
        /// These maps drive terrain texturing systems such as Unity Terrain alphamaps or Polaris splat controls.
        /// </remarks>
        LayerWeightMaps = 32,
        /// <summary>
        /// Request tree instance buffers.
        /// </summary>
        TreeInstances = 64,
        /// <summary>
        /// Request detail density maps.
        /// </summary>
        DetailDensityMaps = 128,
        /// <summary>
        /// Request detail instance buffers.
        /// </summary>
        DetailInstances = 256,
        /// <summary>
        /// Request object instance buffers.
        /// </summary>
        ObjectInstances = 512,
        /// <summary>
        /// Request generic texture outputs exposed by graph nodes.
        /// </summary>
        GenericTextures = 1024,
        /// <summary>
        /// Request generic buffer outputs exposed by graph nodes.
        /// </summary>
        GenericBuffers = 2048
    }
}
#endif


