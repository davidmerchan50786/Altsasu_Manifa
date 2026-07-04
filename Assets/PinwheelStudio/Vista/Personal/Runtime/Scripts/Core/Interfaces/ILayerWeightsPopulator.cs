#if VISTA
using System.Collections.Generic;
using UnityEngine;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Defines a tile contract for applying terrain layer weights.
    /// </summary>
    public interface ILayerWeightsPopulator : ITile
    {
        /// <summary>
        /// Gets the terrain layers currently assigned to the tile backend.
        /// </summary>
        TerrainLayer[] terrainLayers { get; }
        /// <summary>
        /// Applies generated layer-weight textures to the tile.
        /// </summary>
        /// <param name="layers">Terrain layers paired with <paramref name="weights"/> by index.</param>
        /// <param name="weights">Generated weight textures for the supplied layers.</param>
        void PopulateLayerWeights(List<TerrainLayer> layers, List<RenderTexture> weights);
        /// <summary>
        /// Clears all terrain layer weights and assigned terrain layers from the tile backend.
        /// </summary>
        void ClearLayerWeights();
    }
}
#endif


