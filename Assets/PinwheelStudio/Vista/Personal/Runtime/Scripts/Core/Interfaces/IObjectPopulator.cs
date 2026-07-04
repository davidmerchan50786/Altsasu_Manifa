#if VISTA
using System.Collections.Generic;
using UnityEngine;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Defines a contract for spawning or applying generated object instances.
    /// </summary>
    public interface IObjectPopulator
    {
        /// <summary>
        /// Applies generated object instance buffers.
        /// </summary>
        /// <param name="templates">Object templates paired with <paramref name="sampleBuffers"/> by index.</param>
        /// <param name="sampleBuffers">Generated object instance buffers.</param>
        /// <param name="objectPopulateArgs">Manager-supplied options that control progressive spawn cadence.</param>
        /// <returns>A progressive task that completes when object population finishes.</returns>
        ProgressiveTask PopulateObject(List<ObjectTemplate> templates, List<ComputeBuffer> sampleBuffers, VistaManager.ObjectPopulateArgs objectPopulateArgs);

        /// <summary>
        /// Clears all spawned object instances owned by this tile.
        /// </summary>
        /// <returns>A progressive task that completes when object clearing finishes.</returns>
        ProgressiveTask ClearObject();
    }
}
#endif


