#if VISTA
using System.Collections.Generic;
using UnityEngine;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Defines a tile contract for converting generated detail density maps into backend-specific detail data.
    /// </summary>
    public interface IDetailDensityPopulator : ITile
    {
        /// <summary>
        /// Applies generated detail density maps to the tile.
        /// </summary>
        /// <param name="templates">Detail templates paired with <paramref name="densityMaps"/> by index.</param>
        /// <param name="densityMaps">Generated density maps for the supplied templates.</param>
        /// <returns>A progressive task that completes when density population finishes.</returns>
        ProgressiveTask PopulateDetailDensity(List<DetailTemplate> templates, List<RenderTexture> densityMaps);
        /// <summary>
        /// Clears all detail-density prototypes and instances from the tile backend.
        /// </summary>
        /// <returns>A progressive task that completes when density clearing finishes.</returns>
        ProgressiveTask ClearDetailDensity();
    }
}
#endif


