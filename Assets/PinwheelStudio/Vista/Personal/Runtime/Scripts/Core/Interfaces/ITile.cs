#if VISTA
using UnityEngine;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Defines the minimum runtime contract for a terrain tile that can participate in Vista generation.
    /// </summary>
    /// <remarks>
    /// A tile exposes its world bounds and manager binding, receives manager-assigned runtime settings before generation,
    /// and participates in the apply lifecycle before and after the manager pushes data into backend-specific populators.
    /// </remarks>
    public interface ITile
    {
        /// <summary>
        /// Gets or sets the owning manager identifier used for tile-manager association.
        /// </summary>
        string managerId { get; set; }
        /// <summary>
        /// Gets the GameObject that owns this tile component.
        /// </summary>
        GameObject gameObject { get; }
        /// <summary>
        /// Gets the world-space bounds of this tile.
        /// </summary>
        Bounds worldBounds { get; }
        /// <summary>
        /// Gets or sets the max terrain height assigned by the manager before applying data.
        /// </summary>
        float maxHeight { get; set; }
        /// <summary>
        /// Gets or sets the heightmap resolution assigned by the manager before applying data.
        /// </summary>
        int heightMapResolution { get; set; }
        /// <summary>
        /// Gets or sets the texture resolution assigned by the manager before applying data.
        /// </summary>
        int textureResolution { get; set; }
        /// <summary>
        /// Gets or sets the detail density map resolution assigned by the manager before applying data.
        /// </summary>
        int detailDensityMapResolution { get; set; }
        /// <summary>
        /// Called before any generated data is applied to this tile.
        /// </summary>
        void OnBeforeApplyingData();
        /// <summary>
        /// Called after generated data has been fully applied to this tile.
        /// </summary>
        void OnAfterApplyingData();
    }
}
#endif


