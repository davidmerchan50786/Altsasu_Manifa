#if VISTA
using System.Collections.Generic;
using UnityEngine;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Defines a tile contract for applying generated detail instance buffers.
    /// </summary>
    public interface IDetailInstancePopulator : ITile
    {
        /// <summary>
        /// Applies generated detail instance buffers to the tile.
        /// </summary>
        /// <param name="templates">Detail templates paired with <paramref name="samples"/> by index.</param>
        /// <param name="samples">Generated detail instance buffers for the supplied templates.</param>
        void PopulateDetailInstance(List<DetailTemplate> templates, List<ComputeBuffer> samples);
        /// <summary>
        /// Clears all generated detail-instance prototypes and instances from the tile backend.
        /// </summary>
        void ClearDetailInstance();
    }
}
#endif


