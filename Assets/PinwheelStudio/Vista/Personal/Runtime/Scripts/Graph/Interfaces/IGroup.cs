#if VISTA
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Describes a grouping rectangle used to organize graph elements on the editor canvas.
    /// </summary>
    public interface IGroup : IHasID
    {
        /// <summary>
        /// Display title shown on the group header.
        /// </summary>
        string title { get; set; }
        /// <summary>
        /// Group rectangle in graph-canvas coordinates.
        /// </summary>
        Rect position { get; set; }
    }
}
#endif


