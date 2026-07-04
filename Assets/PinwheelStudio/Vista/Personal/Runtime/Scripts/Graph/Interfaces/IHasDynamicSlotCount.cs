#if VISTA

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Marks a node whose slot layout can change after creation.
    /// </summary>
    public interface IHasDynamicSlotCount
    {
        /// <summary>
        /// Notifies listeners that the node's slot layout has changed and cached edge or UI state may need to refresh.
        /// </summary>
        /// <param name="sender">Node whose slot layout changed.</param>
        delegate void SlotsChangedHandler(INode sender);
        /// <summary>
        /// Raised when the node adds, removes, or restructures its slots.
        /// </summary>
        event SlotsChangedHandler slotsChanged;
    }
}
#endif


