#if VISTA
using System;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Describes slot typing and compatibility rules independent of a concrete slot instance.
    /// </summary>
    public interface ISlotAdapter
    {
        /// <summary>
        /// Concrete slot type represented by this adapter.
        /// </summary>
        Type slotType { get; }
        /// <summary>
        /// Direction supported by the adapted slot type.
        /// </summary>
        SlotDirection direction { get; }
        /// <summary>
        /// Determines whether this slot type can connect to another adapted slot type.
        /// </summary>
        /// <param name="other">Other slot adapter to test against.</param>
        bool CanConnectTo(ISlotAdapter other);
    }
}
#endif


