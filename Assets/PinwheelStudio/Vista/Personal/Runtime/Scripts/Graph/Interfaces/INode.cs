#if VISTA

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Core contract implemented by every serialized graph node.
    /// </summary>
    public interface INode : IHasID, IGroupable
    {
        /// <summary>
        /// Editor-only visual state stored with the node.
        /// </summary>
        VisualState visualState { get; set; }
        /// <summary>
        /// Returns the input slots currently exposed by the node.
        /// </summary>
        ISlot[] GetInputSlots();
        /// <summary>
        /// Returns the output slots currently exposed by the node.
        /// </summary>
        ISlot[] GetOutputSlots();
        /// <summary>
        /// Finds a slot by its per-node slot id.
        /// </summary>
        /// <param name="id">Slot identifier to look up.</param>
        ISlot GetSlot(int id);
        /// <summary>
        /// Creates a shallow copy suitable for duplication workflows such as copy/paste.
        /// </summary>
        INode ShallowCopy();
    }
}
#endif


