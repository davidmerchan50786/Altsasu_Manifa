#if VISTA
using UnityEngine;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Defines a tile contract for applying generated metallic textures.
    /// </summary>
    public interface IMetallicMapPopulator : ITile
    {
        /// <summary>
        /// Applies a generated metallic map to the tile.
        /// </summary>
        /// <param name="metallicMap">The generated metallic texture for the tile.</param>
        void PopulateMetallicMap(RenderTexture metallicMap);
        /// <summary>
        /// Clears the tile metallic map by writing a zero-valued texture.
        /// </summary>
        void ClearMetallicMap();
    }
}
#endif


