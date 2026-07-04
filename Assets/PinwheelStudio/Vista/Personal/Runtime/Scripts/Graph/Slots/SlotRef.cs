#if VISTA
using System;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [System.Serializable]
    /// <summary>
    /// Serialized reference to one slot on one node.
    /// </summary>
    public struct SlotRef : IEquatable<SlotRef>
    {
        /// <summary>
        /// Sentinel value used when a slot reference is intentionally absent or invalid.
        /// </summary>
        public static readonly SlotRef invalid = new SlotRef(string.Empty, int.MinValue);

        [SerializeField]
        private string m_nodeId;
        /// <summary>
        /// Identifier of the node that owns the referenced slot.
        /// </summary>
        public string nodeId
        {
            get
            {
                return m_nodeId;
            }
        }

        [SerializeField]
        private int m_slotId;
        /// <summary>
        /// Identifier of the referenced slot within the owning node.
        /// </summary>
        public int slotId
        {
            get
            {
                return m_slotId;
            }
        }

        /// <summary>
        /// Creates a slot reference from a node id and slot id pair.
        /// </summary>
        /// <param name="nodeId">Identifier of the node that owns the slot.</param>
        /// <param name="slotId">Identifier of the slot within that node.</param>
        public SlotRef(string nodeId, int slotId)
        {
            this.m_nodeId = nodeId;
            this.m_slotId = slotId;
        }

        /// <summary>
        /// Compares both the node id and slot id.
        /// </summary>
        /// <param name="other">Slot reference to compare against.</param>
        public bool Equals(SlotRef other)
        {
            return m_nodeId.Equals(other.m_nodeId) && m_slotId.Equals(other.m_slotId);
        }

        /// <summary>
        /// Returns a compact debug string containing the slot id and node id.
        /// </summary>
        public override string ToString()
        {
            return $"({slotId},{nodeId})";
        }
    }
}
#endif
