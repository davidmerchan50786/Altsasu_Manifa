#if VISTA
using System;
using System.ComponentModel;

namespace Pinwheel.Vista.Graph
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    /// <summary>
    /// Fallback slot type that can connect to any opposite-direction slot regardless of its concrete payload type.
    /// </summary>
    public class GenericSlot : SlotBase
    {
        /// <summary>
        /// Adapter describing generic-slot compatibility rules.
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
                    return typeof(GenericSlot);
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
            /// Allows any connection as long as the other slot points in the opposite direction.
            /// </summary>
            /// <param name="other">Other slot adapter to test.</param>
            public bool CanConnectTo(ISlotAdapter other)
            {
                if (other.direction.Equals(this.direction))
                    return false;
                return true;
            }

            /// <summary>
            /// Creates a generic-slot adapter for the specified slot direction.
            /// </summary>
            /// <param name="direction">Direction of the slot instance that owns this adapter.</param>
            public Adapter(SlotDirection direction)
            {
                m_direction = direction;
            }
        }

        /// <summary>
        /// Creates an empty generic slot for serialization.
        /// </summary>
        public GenericSlot() : base()
        {

        }

        /// <summary>
        /// Creates a named generic slot with a fixed direction and id.
        /// </summary>
        /// <param name="name">Display name of the slot.</param>
        /// <param name="type">Input or output direction.</param>
        /// <param name="id">Per-node slot identifier.</param>
        public GenericSlot(string name, SlotDirection type, int id) : base(name, type, id)
        {
        }

        /// <summary>
        /// Returns an adapter describing how this slot can connect to other slot types.
        /// </summary>
        public override ISlotAdapter GetAdapter()
        {
            return new Adapter(this.direction);
        }
    }
}
#endif


