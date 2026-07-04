#if VISTA
using UnityEngine;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Defines a tile contract for applying geometry-related biome outputs.
    /// </summary>
    public interface IGeometryPopulator : ITile
    {
        /// <summary>
        /// Applies a generated height map to the tile geometry.
        /// </summary>
        /// <param name="heightMap">The generated height map for the tile.</param>
        void PopulateHeightMap(RenderTexture heightMap);
        /// <summary>
        /// Clears the tile height map by writing a zero-valued height field.
        /// </summary>
        void ClearHeightMap();
        /// <summary>
        /// Applies a generated hole map to the tile geometry.
        /// </summary>
        /// <param name="holeMap">The generated hole map for the tile.</param>
        void PopulateHoleMap(RenderTexture holeMap);
        /// <summary>
        /// Clears the tile hole map by writing a zero-valued hole field.
        /// </summary>
        void ClearHoleMap();
        /// <summary>
        /// Applies a generated mesh-density map to the tile geometry, when supported by the backend.
        /// </summary>
        /// <param name="meshDensityMap">The generated mesh-density map for the tile.</param>
        void PopulateMeshDensityMap(RenderTexture meshDensityMap);
        /// <summary>
        /// Clears the tile mesh-density map by writing a zero-valued density field.
        /// </summary>
        void ClearMeshDensityMap();
        /// <summary>
        /// Finalizes staged geometry changes after geometry-related outputs have been applied.
        /// </summary>
        void UpdateGeometry();
        /// <summary>
        /// Resolves border seams against neighboring tiles after geometry and layer data have been applied.
        /// </summary>
        void MatchSeams();
    }
}
#endif


