#if VISTA
using System;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [System.Serializable]
    /// <summary>
    /// Represents one directed connection between two slots in a graph asset.
    /// </summary>
    /// <remarks>
    /// An edge always links an output slot on an upstream node to an input slot on a downstream
    /// node. <see cref="GraphAsset"/> stores these objects as the serialized wiring layer used for
    /// graph validation, execution-link construction, and recursion checks.
    /// </remarks>
    public class Edge : IEdge
    {
        [SerializeField]
        protected string m_id;
        /// <summary>
        /// Stable identifier of this edge inside the serialized graph.
        /// </summary>
        /// <remarks>
        /// The value is generated once on construction and is used by graph mutation APIs such as
        /// <see cref="GraphAsset.RemoveEdge(string)"/>.
        /// </remarks>
        public string id
        {
            get
            {
                return m_id;
            }
        }

        [SerializeField]
        protected SlotRef m_outputSlot;
        /// <summary>
        /// Reference to the upstream output slot that produces data for this connection.
        /// </summary>
        /// <remarks>
        /// During validation, the graph checks that this slot exists, belongs to an output, and is
        /// type-compatible with <see cref="inputSlot"/>.
        /// </remarks>
        public SlotRef outputSlot
        {
            get
            {
                return m_outputSlot;
            }
            set
            {
                m_outputSlot = value;
            }
        }

        [SerializeField]
        protected SlotRef m_inputSlot;
        /// <summary>
        /// Reference to the downstream input slot that consumes data from this connection.
        /// </summary>
        /// <remarks>
        /// <see cref="GraphContext"/> builds its input-link lookup table from this target slot to the
        /// connected <see cref="outputSlot"/>.
        /// </remarks>
        public SlotRef inputSlot
        {
            get
            {
                return m_inputSlot;
            }
            set
            {
                m_inputSlot = value;
            }
        }

        /// <summary>
        /// Creates an edge with a new identifier and no connected slots.
        /// </summary>
        /// <remarks>
        /// This constructor mainly exists for serialization and delayed initialization scenarios.
        /// </remarks>
        public Edge()
        {
            this.m_id = Utilities.GenerateId();
        }

        /// <summary>
        /// Creates an edge that connects one output slot to one input slot.
        /// </summary>
        /// <param name="outputSlot">
        /// The source slot that provides the data.
        /// </param>
        /// <param name="inputSlot">
        /// The destination slot that receives the data.
        /// </param>
        /// <remarks>
        /// The constructor records the connection as provided. Compatibility, node existence, and
        /// recursion safety are enforced later by <see cref="GraphAsset"/> when the edge is added to
        /// a graph.
        /// </remarks>
        public Edge(SlotRef outputSlot, SlotRef inputSlot)
        {
            this.m_id = Utilities.GenerateId();
            this.m_outputSlot = outputSlot;
            this.m_inputSlot = inputSlot;
        }
    }
}
#endif


