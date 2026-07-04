#if VISTA
using System;
using System.Reflection;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Retrieves <see cref="NodeMetadataAttribute"/> data from node types.
    /// </summary>
    /// <remarks>
    /// This helper centralizes the reflection lookup used by graph editors and graph-type filters,
    /// such as the node restrictions enforced by <see cref="BiomeMaskGraph"/>.
    /// </remarks>
    public class NodeMetadata
    {
        /// <summary>
        /// Returns the node metadata attribute declared on a node type.
        /// </summary>
        /// <param name="nodeType">
        /// The node type to inspect.
        /// </param>
        /// <returns>
        /// The attached <see cref="NodeMetadataAttribute"/>, or <see langword="null"/> when the type
        /// does not declare one.
        /// </returns>
        public static NodeMetadataAttribute Get(Type nodeType)
        {
            NodeMetadataAttribute att = nodeType.GetCustomAttribute<NodeMetadataAttribute>(false);
            return att;
        }

        /// <summary>
        /// Returns the node metadata attribute declared on a node type specified by its CLR type argument.
        /// </summary>
        public static NodeMetadataAttribute Get<T>()
        {
            return Get(typeof(T));
        }
    }
}
#endif


