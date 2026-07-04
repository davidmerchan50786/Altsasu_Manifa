#if VISTA

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Identifies whether a slot receives data or produces it.
    /// </summary>
    public enum SlotDirection
    {
        /// <summary>
        /// Slot accepts an incoming connection.
        /// </summary>
        Input,
        /// <summary>
        /// Slot provides data to downstream connections.
        /// </summary>
        Output
    }
}
#endif


