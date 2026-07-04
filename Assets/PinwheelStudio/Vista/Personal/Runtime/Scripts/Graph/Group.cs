#if VISTA
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [System.Serializable]
    /// <summary>
    /// Represents one graph-canvas group used to organize nodes and notes visually.
    /// </summary>
    /// <remarks>
    /// Groups are editor-facing layout elements stored inside <see cref="GraphAsset"/>. They do not
    /// affect graph execution, but nodes, sticky notes, and sticky images can reference a group id so
    /// the canvas can move or select them together.
    /// </remarks>
    public class Group : IGroup
    {
        [SerializeField]
        protected string m_id;
        /// <summary>
        /// Stable identifier of this group inside the serialized graph.
        /// </summary>
        public string id
        {
            get
            {
                return m_id;
            }
        }

        [SerializeField]
        protected string m_title;
        /// <summary>
        /// User-facing title shown on the group header in the graph editor.
        /// </summary>
        public string title
        {
            get
            {
                return m_title;
            }
            set
            {
                m_title = value;
            }
        }

        [SerializeField]
        protected Rect m_position;
        /// <summary>
        /// Canvas-space rectangle that defines the group's frame and header position.
        /// </summary>
        public Rect position
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

        /// <summary>
        /// Creates a group with a new serialized identifier.
        /// </summary>
        public Group()
        {
            this.m_id = Utilities.GenerateId();
        }
    }
}
#endif


