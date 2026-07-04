#if VISTA

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Describes a directed connection between one output slot and one input slot in a graph.
    /// </summary>
    public interface IEdge : IHasID
    {
        /// <summary>
        /// Reference to the upstream output slot that produces data for this connection.
        /// </summary>
        SlotRef outputSlot { get; set; }
        /// <summary>
        /// Reference to the downstream input slot that consumes data from this connection.
        /// </summary>
        SlotRef inputSlot { get; set; }
    }
}
#endif
