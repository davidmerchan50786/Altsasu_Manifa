#if VISTA

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Describes one input or output connection point on a graph node.
    /// </summary>
    public interface ISlot
    {
        /// <summary>
        /// Slot identifier unique within the owning node.
        /// </summary>
        int id { get; }
        /// <summary>
        /// Whether the slot accepts incoming data or produces outgoing data.
        /// </summary>
        SlotDirection direction { get; }
        /// <summary>
        /// Display name shown in the graph editor.
        /// </summary>
        string name { get; }
        /// <summary>
        /// Adapter describing the slot's runtime type and connection rules.
        /// </summary>
        ISlotAdapter GetAdapter();
    }
}
#endif


