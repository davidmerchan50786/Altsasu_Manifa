#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista.Graph;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Supplies external graph inputs and later releases any temporary resources created for those inputs.
    /// </summary>
    public interface IExternalInputProvider
    {
        /// <summary>
        /// Registers externally prepared textures and buffers into the graph input container before execution starts.
        /// </summary>
        /// <param name="inputContainer">Destination container that the provider should populate.</param>
        void SetInput(GraphInputContainer inputContainer);
        /// <summary>
        /// Returns the terrain generation configs that reflect the current state of the host object.
        /// Called by the graph editor immediately before each execution to keep preview configs in sync.
        /// </summary>
        TerrainGenerationConfigs GetDebugConfigs();
        /// <summary>
        /// Releases temporary resources created while preparing external inputs.
        /// </summary>
        void CleanUp();
    } 
}
#endif


