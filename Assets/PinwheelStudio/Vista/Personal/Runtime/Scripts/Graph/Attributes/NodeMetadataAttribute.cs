#if VISTA
using System;

namespace Pinwheel.Vista.Graph
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    /// <summary>
    /// Stores editor and documentation metadata for a graph node type.
    /// </summary>
    public class NodeMetadataAttribute : Attribute
    {
        /// <summary>
        /// Transforms a node description string before it is shown or exported.
        /// </summary>
        /// <param name="description">Original description text from the attribute.</param>
        /// <returns>The transformed description text.</returns>
        public delegate string ParseNodeDescriptionHandler(string description);
        /// <summary>
        /// Optional global hook used to post-process node descriptions.
        /// </summary>
        public static ParseNodeDescriptionHandler parseDescriptionCallback;

        /// <summary>
        /// Display name used for the node in search results and graph UI.
        /// </summary>
        public string title { get; set; }
        /// <summary>
        /// Slash-delimited menu path used to place the node in the node-creation hierarchy.
        /// </summary>
        public string path { get; set; }
        /// <summary>
        /// Optional icon identifier used by the graph editor.
        /// </summary>
        public string icon { get; set; }
        /// <summary>
        /// Optional external documentation link for the node.
        /// </summary>
        public string documentation { get; set; }
        /// <summary>
        /// Search keywords used to make the node easier to discover in the graph editor.
        /// </summary>
        public string keywords { get; set; }
        /// <summary>
        /// Short descriptive text shown in node search and documentation surfaces.
        /// </summary>
        public string description { get; set; }
        /// <summary>
        /// Indicates whether this node should be omitted from generated public documentation.
        /// </summary>
        public bool hideFromDoc { get; set; }

        /// <summary>
        /// Returns the top-level category extracted from <see cref="path"/>.
        /// </summary>
        /// <returns>
        /// The substring before the first slash in <see cref="path"/>, or an empty string when no category can be derived.
        /// </returns>
        public string GetCategory()
        {
            string category = string.Empty;
            if (!string.IsNullOrEmpty(path))
            {
                int separatorIndex = path.IndexOf('/');
                if (separatorIndex > 0)
                {
                    category = path.Substring(0, separatorIndex);
                }
            }
            return category;
        }
    }
}
#endif


