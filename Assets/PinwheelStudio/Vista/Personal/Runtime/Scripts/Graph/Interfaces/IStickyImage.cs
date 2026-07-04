#if VISTA
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Describes an image annotation placed on the graph editor canvas.
    /// </summary>
    public interface IStickyImage: IHasID, IGroupable
    {
        /// <summary>
        /// GUID of the texture asset displayed by the sticky image.
        /// </summary>
        string textureGuid { get; set; }
        /// <summary>
        /// Sticky-image rectangle in graph-canvas coordinates.
        /// </summary>
        Rect position { get; set; }
    }
}
#endif


