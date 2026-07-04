#if VISTA

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Marks a node that exposes one primary output slot for graph-target discovery.
    /// </summary>
    public interface IOutputNode
    {
        /// <summary>
        /// Reference to the slot treated as the node's main graph output.
        /// </summary>
        SlotRef mainOutputSlot { get; }
    }
}
#endif


