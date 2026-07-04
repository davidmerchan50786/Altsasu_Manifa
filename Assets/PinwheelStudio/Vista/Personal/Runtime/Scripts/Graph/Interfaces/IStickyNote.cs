#if VISTA
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Describes a text annotation placed on the graph editor canvas.
    /// </summary>
    public interface IStickyNote: IHasID, IGroupable
    {
        /// <summary>
        /// Note title shown in the sticky-note header.
        /// </summary>
        string title { get; set; }
        /// <summary>
        /// Main note body text.
        /// </summary>
        string contents { get; set; }
        /// <summary>
        /// Editor font size used to render the note.
        /// </summary>
        int fontSize { get; set; }
        /// <summary>
        /// Theme index used by the graph editor to choose note styling.
        /// </summary>
        int theme { get; set; }
        /// <summary>
        /// Sticky-note rectangle in graph-canvas coordinates.
        /// </summary>
        Rect position { get; set; }
    }
}
#endif


