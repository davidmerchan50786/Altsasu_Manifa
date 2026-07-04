#if VISTA
using System;

namespace Pinwheel.Vista.Graph
{
    [System.Serializable]
    /// <summary>
    /// Slot type used for compute-buffer data flowing through the graph.
    /// </summary>
    public class BufferSlot : SlotBase
    {
        /// <summary>
        /// Adapter describing buffer-slot compatibility rules.
        /// </summary>
        public struct Adapter : ISlotAdapter
        {
            /// <summary>
            /// Reports the concrete slot type represented by this adapter.
            /// </summary>
            public Type slotType
            {
                get
                {
                    return typeof(BufferSlot);
                }
            }

            private SlotDirection m_direction;
            /// <summary>
            /// Direction of the slot instance that created this adapter.
            /// </summary>
            public SlotDirection direction
            {
                get
                {
                    return m_direction;
                }
            }

            /// <summary>
            /// Allows connections to other buffer slots or generic slots as long as they point in the opposite direction.
            /// </summary>
            /// <param name="other">Other slot adapter to test.</param>
            public bool CanConnectTo(ISlotAdapter other)
            {
                if (other.direction.Equals(this.direction))
                    return false;
                if (other.slotType.Equals(this.slotType))
                {
                    return true;
                }
                else if (other.slotType.Equals(typeof(GenericSlot)))
                {
                    return true;
                }
                return false;
            }

            /// <summary>
            /// Creates a buffer-slot adapter for the specified slot direction.
            /// </summary>
            /// <param name="dir">Direction of the slot instance that owns this adapter.</param>
            public Adapter(SlotDirection dir)
            {
                m_direction = dir;
            }
        }

        /// <summary>
        /// Returns an adapter describing how this slot can connect to other slot types.
        /// </summary>
        public override ISlotAdapter GetAdapter()
        {
            return new Adapter(direction);
        }

        /// <summary>
        /// Creates an empty buffer slot for serialization.
        /// </summary>
        public BufferSlot() : base()
        { 
        }

        /// <summary>
        /// Creates a named buffer slot with a fixed direction and id.
        /// </summary>
        /// <param name="name">Display name of the slot.</param>
        /// <param name="direction">Input or output direction.</param>
        /// <param name="id">Per-node slot identifier.</param>
        public BufferSlot(string name, SlotDirection direction, int id) : base(name, direction, id)
        {

        }
    }
}
#endif


