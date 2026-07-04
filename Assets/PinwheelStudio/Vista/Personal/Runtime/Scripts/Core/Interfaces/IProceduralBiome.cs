#if VISTA
using Pinwheel.Vista.Graph;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Extends <see cref="IBiome"/> with a terrain-graph-driven biome source.
    /// </summary>
    public interface IProceduralBiome : IBiome
    {
        /// <summary>
        /// Gets or sets the terrain graph used to generate biome data for this biome.
        /// </summary>
        TerrainGraph terrainGraph { get; set; }
    }
}
#endif


