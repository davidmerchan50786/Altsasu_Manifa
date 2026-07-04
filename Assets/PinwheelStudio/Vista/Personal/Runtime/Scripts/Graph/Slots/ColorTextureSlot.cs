#if VISTA
using System;

namespace Pinwheel.Vista.Graph
{
    [System.Serializable]
    /// <summary>
    /// Slot type used for full color textures.
    /// </summary>
    public class ColorTextureSlot : SlotBase
    {
        /// <summary>
        /// Adapter describing color-texture slot compatibility rules.
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
                    return typeof(ColorTextureSlot);
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
            /// Allows opposite-direction connections from other color-texture slots, generic slots, or mask outputs.
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
                else if (other.slotType.Equals(typeof(MaskSlot)) && other.direction == SlotDirection.Output)
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
            /// Creates a color-texture adapter for the specified slot direction.
            /// </summary>
            /// <param name="direction">Direction of the slot instance that owns this adapter.</param>
            public Adapter(SlotDirection direction)
            {
                m_direction = direction;
            }
        }

        /// <summary>
        /// Creates an empty color-texture slot for serialization.
        /// </summary>
        public ColorTextureSlot() : base()
        {

        }

        /// <summary>
        /// Creates a named color-texture slot with a fixed direction and id.
        /// </summary>
        /// <param name="name">Display name of the slot.</param>
        /// <param name="type">Input or output direction.</param>
        /// <param name="id">Per-node slot identifier.</param>
        public ColorTextureSlot(string name, SlotDirection type, int id) : base(name, type, id)
        {

        }

        /// <summary>
        /// Returns an adapter describing how this slot can connect to other slot types.
        /// </summary>
        public override ISlotAdapter GetAdapter()
        {
            return new Adapter(direction);
        }
    }
}
#endif


