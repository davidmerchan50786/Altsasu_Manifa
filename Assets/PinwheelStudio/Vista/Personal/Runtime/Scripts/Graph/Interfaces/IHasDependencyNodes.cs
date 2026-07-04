#if VISTA
using System.Collections.Generic;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Marks a node that depends on other graphs or nodes outside its normal slot connections.
    /// </summary>
    public interface IHasDependencyNodes : INode
    {
        /// <summary>
        /// Returns the extra dependency nodes that must be considered when building graph execution or recursion checks.
        /// </summary>
        /// <param name="nodes">Candidate nodes available in the owning graph.</param>
        IEnumerable<INode> GetDependencies(IEnumerable<INode> nodes);
    }
}
#endif


