#if VISTA
using System.Collections.Generic;
using UnityEngine;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Represents a callback that receives generic texture outputs produced for a tile.
    /// </summary>
    /// <param name="labels">
    /// Labels that identify each generic texture output. The order matches <paramref name="textures"/> element-for-element.
    /// </param>
    /// <param name="textures">
    /// The generated render textures for the current tile. Consumers should treat these as paired with
    /// <paramref name="labels"/> by index.
    /// </param>
    public delegate void PopulateGenericTexturesHandler(List<string> labels, List<RenderTexture> textures);
    /// <summary>
    /// Represents a callback that receives generic buffer outputs produced for a tile.
    /// </summary>
    /// <param name="labels">
    /// Labels that identify each generic buffer output. The order matches <paramref name="buffers"/> element-for-element.
    /// </param>
    /// <param name="buffers">
    /// The generated compute buffers for the current tile. Consumers should treat these as paired with
    /// <paramref name="labels"/> by index.
    /// </param>
    public delegate void PopulateGenericBuffersHandler(List<string> labels, List<ComputeBuffer> buffers);
    /// <summary>
    /// Represents a callback invoked after a prefab instance has been spawned for a tile.
    /// </summary>
    /// <param name="tile">The tile that owns the spawned instance.</param>
    /// <param name="spawnedGO">The newly spawned GameObject.</param>
    public delegate void PopulatePrefabHandler(ITile tile, GameObject spawnedGO);
}
#endif


