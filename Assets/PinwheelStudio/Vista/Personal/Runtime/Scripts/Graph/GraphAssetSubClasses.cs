#if VISTA
using System.Collections.Generic;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Adds helper value types used by graph-asset mutation APIs.
    /// </summary>
    public partial class GraphAsset
    {
        /// <summary>
        /// Describes the graph elements removed by a mutation operation.
        /// </summary>
        /// <remarks>
        /// <see cref="GraphAsset.RemoveNode(string)"/> uses this to return both the removed node and every connected edge
        /// that had to be removed with it. <see cref="GraphAsset.RemoveEdge(string)"/> uses the same container with only the
        /// <see cref="edges"/> field populated.
        /// </remarks>
        public struct RemovedElements
        {
            /// <summary>
            /// The removed node, when the mutation deleted a node.
            /// </summary>
            public INode node;
            /// <summary>
            /// The removed edges associated with the mutation.
            /// </summary>
            public List<IEdge> edges;
        }
    }
}
#endif


