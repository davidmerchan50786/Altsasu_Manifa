#if VISTA
using System.Collections.Generic;
using UnityEngine;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Defines a tile contract for applying generated tree instance buffers.
    /// </summary>
    public interface ITreePopulator : ITile
    {
        /// <summary>
        /// Applies generated tree instance buffers to the tile.
        /// </summary>
        /// <param name="templates">Tree templates paired with <paramref name="samples"/> by index.</param>
        /// <param name="samples">Generated tree instance buffers.</param>
        void PopulateTrees(List<TreeTemplate> templates, List<ComputeBuffer> samples);
        /// <summary>
        /// Clears all tree prototypes and instances from the tile backend.
        /// </summary>
        void ClearTrees();
    }
}
#endif


