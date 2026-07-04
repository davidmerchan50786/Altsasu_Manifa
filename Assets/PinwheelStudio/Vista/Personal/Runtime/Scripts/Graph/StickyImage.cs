#if VISTA
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [System.Serializable]
    /// <summary>
    /// Represents an image annotation placed on the graph canvas.
    /// </summary>
    /// <remarks>
    /// Sticky images are editor-only organizational elements stored in <see cref="GraphAsset"/>. They
    /// do not participate in graph execution.
    /// </remarks>
    public class StickyImage : IStickyImage
    {
        [SerializeField]
        protected string m_id;
        /// <summary>
        /// Stable identifier of this sticky image inside the serialized graph.
        /// </summary>
        public string id
        {
            get
            {
                return m_id;
            }
        }

        [SerializeField]
        protected string m_groupId;
        /// <summary>
        /// Optional group identifier that associates this image with a graph group.
        /// </summary>
        public string groupId
        {
            get
            {
                return m_groupId;
            }
            set
            {
                m_groupId = value;
            }
        }

        [SerializeField]
        protected string m_textureGuid;
        /// <summary>
        /// Serialized identifier of the image asset displayed by this annotation.
        /// </summary>
        public string textureGuid
        {
            get
            {
                return m_textureGuid;
            }
            set
            {
                m_textureGuid = value;
            }
        }

        [SerializeField]
        protected Rect m_position;
        /// <summary>
        /// Canvas-space rectangle used to place and size the image annotation.
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
        /// Creates a sticky image with a new identifier and no group assignment.
        /// </summary>
        public StickyImage()
        {
            m_id = Utilities.GenerateId();
            m_groupId = string.Empty;
        }
    }
}
#endif


