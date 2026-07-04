#if VISTA
using System;
using UnityEngine;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Defines the integration contract between Vista and a terrain backend.
    /// </summary>
    /// <remarks>
    /// Terrain-system implementations identify the backend's terrain and tile component types and ensure that target
    /// terrain objects are equipped with a compatible <see cref="ITile"/> adapter for Vista generation.
    /// </remarks>
    public interface ITerrainSystem
    {
        /// <summary>
        /// Gets the display label used for this terrain system in setup and selection workflows.
        /// </summary>
        string terrainLabel { get; }
        /// <summary>
        /// Gets the component type that represents a terrain object in this backend.
        /// </summary>
        /// <returns>The backend terrain component type.</returns>
        Type GetTerrainComponentType();
        /// <summary>
        /// Gets the component type that implements <see cref="ITile"/> for this backend.
        /// </summary>
        /// <returns>The backend tile component type.</returns>
        Type GetTileComponentType();
        /// <summary>
        /// Ensures a compatible tile component is configured on a target terrain object.
        /// </summary>
        /// <param name="manager">Manager that owns the tile.</param>
        /// <param name="target">Target object containing terrain components.</param>
        /// <returns>The configured tile, or <see langword="null"/> when setup is not possible.</returns>
        ITile SetupTile(VistaManager manager, GameObject target);
    }
}
#endif


