#if VISTA
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [System.Serializable]
    /// <summary>
    /// Represents a text annotation placed on the graph canvas.
    /// </summary>
    /// <remarks>
    /// Sticky notes are editor-only documentation elements stored in <see cref="GraphAsset"/>. They
    /// do not affect execution or graph validation.
    /// </remarks>
    public class StickyNote : IStickyNote
    {
        [SerializeField]
        protected string m_groupId;
        /// <summary>
        /// Optional group identifier that associates this note with a graph group.
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
        protected string m_id;
        /// <summary>
        /// Stable identifier of this sticky note inside the serialized graph.
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
        /// Short heading shown at the top of the note.
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
        protected string m_contents;
        /// <summary>
        /// Body text stored in the note.
        /// </summary>
        public string contents
        {
            get
            {
                return m_contents;
            }
            set
            {
                m_contents = value;
            }
        }

        [SerializeField]
        protected int m_fontSize;
        /// <summary>
        /// Font size used when rendering the note contents in the graph editor.
        /// </summary>
        public int fontSize
        {
            get
            {
                return m_fontSize;
            }
            set
            {
                m_fontSize = value;
            }
        }

        [SerializeField]
        protected int m_theme;
        /// <summary>
        /// Serialized theme index that controls the note's editor styling.
        /// </summary>
        public int theme
        {
            get
            {
                return m_theme;
            }
            set
            {
                m_theme = value;
            }
        }

        [SerializeField]
        protected Rect m_position;
        /// <summary>
        /// Canvas-space rectangle used to place and size the note.
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
        /// Creates a sticky note with a new identifier and no group assignment.
        /// </summary>
        public StickyNote()
        {
            m_id = Utilities.GenerateId();
            m_groupId = string.Empty;
        }
    }
}
#endif


