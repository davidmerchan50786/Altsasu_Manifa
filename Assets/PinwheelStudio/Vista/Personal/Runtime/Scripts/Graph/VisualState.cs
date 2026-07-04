#if VISTA
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [System.Serializable]
    /// <summary>
    /// Stores editor-facing display state for a graph node.
    /// </summary>
    /// <remarks>
    /// This data is serialized on each <see cref="INode"/> implementation so the graph editor can
    /// restore node layout and foldout state between sessions. It does not affect runtime execution.
    /// </remarks>
    public struct VisualState
    {
        [SerializeField]
        private Vector2 m_position;
        /// <summary>
        /// Node position on the graph canvas.
        /// </summary>
        public Vector2 position
        {
            get
            {
                return m_position;
            }
            set
            {
                m_position = value;
            }
        }

        [SerializeField]
        private bool m_collapsed;
        /// <summary>
        /// Whether the node is displayed in its collapsed form in the graph editor.
        /// </summary>
        public bool collapsed
        {
            get
            {
                return m_collapsed;
            }
            set
            {
                m_collapsed = value;
            }
        }
    }
}
#endif


