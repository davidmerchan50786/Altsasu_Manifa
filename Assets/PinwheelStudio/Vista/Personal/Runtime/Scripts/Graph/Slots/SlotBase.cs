#if VISTA
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [System.Serializable]
    /// <summary>
    /// Shared serialized state for all concrete graph slot types.
    /// </summary>
    public abstract class SlotBase : ISlot
    {
        [SerializeField]
        protected int m_id;
        /// <summary>
        /// Slot identifier unique within the owning node.
        /// </summary>
        public int id
        {
            get
            {
                return m_id;
            }
        }

        [SerializeField]
        protected SlotDirection m_type;
        /// <summary>
        /// Whether the slot is an input or an output.
        /// </summary>
        public SlotDirection direction
        {
            get
            {
                return m_type;
            }
        }

        [SerializeField]
        protected string m_name;
        /// <summary>
        /// Display name shown in the graph editor.
        /// </summary>
        public string name
        {
            get
            {
                return m_name;
            }
        }

        /// <summary>
        /// Creates an empty slot instance for serialization.
        /// </summary>
        public SlotBase()
        {

        }

        /// <summary>
        /// Creates a named slot with a fixed direction and id.
        /// </summary>
        /// <param name="name">Display name of the slot.</param>
        /// <param name="type">Input or output direction.</param>
        /// <param name="id">Per-node slot identifier.</param>
        public SlotBase(string name, SlotDirection type, int id)
        {
            this.m_name = name;
            this.m_type = type;
            this.m_id = id;
        }

        /// <summary>
        /// Returns an adapter describing how this slot connects to other slot types.
        /// </summary>
        public abstract ISlotAdapter GetAdapter();
    }
}
#endif


