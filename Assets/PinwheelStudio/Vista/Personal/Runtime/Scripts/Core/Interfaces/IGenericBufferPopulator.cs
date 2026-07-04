#if VISTA
using System.Collections.Generic;
using UnityEngine;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Defines a contract for receiving generic compute-buffer outputs generated for a tile.
    /// </summary>
    public interface IGenericBufferPopulator
    {
        /// <summary>
        /// Applies generic buffer outputs to the implementation.
        /// </summary>
        /// <param name="labels">Labels paired with <paramref name="buffers"/> by index.</param>
        /// <param name="buffers">Generic compute-buffer outputs.</param>
        void PopulateGenericBuffers(List<string> labels, List<ComputeBuffer> buffers);
    }
}
#endif


