#if VISTA
using UnityEngine;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Defines a tile contract for applying generated albedo textures.
    /// </summary>
    public interface IAlbedoMapPopulator : ITile
    {
        /// <summary>
        /// Applies a generated albedo map to the tile.
        /// </summary>
        /// <param name="albedoMap">The generated albedo texture for the tile.</param>
        void PopulateAlbedoMap(RenderTexture albedoMap);
        /// <summary>
        /// Clears the tile albedo map by writing a zero-valued texture.
        /// </summary>
        void ClearAlbedoMap();
    }
}
#endif


