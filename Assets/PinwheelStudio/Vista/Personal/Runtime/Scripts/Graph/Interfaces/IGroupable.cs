#if VISTA

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Marks a graph element that can be assigned to a group on the editor canvas.
    /// </summary>
    public interface IGroupable
    {
        /// <summary>
        /// Identifier of the group that owns this element, or an empty value when it is ungrouped.
        /// </summary>
        string groupId { get; set; }
    }
}
#endif


