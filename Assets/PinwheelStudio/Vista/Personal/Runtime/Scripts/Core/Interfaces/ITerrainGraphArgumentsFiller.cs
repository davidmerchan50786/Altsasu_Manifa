#if VISTA
using Pinwheel.Vista.Graph;
using System.Collections.Generic;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Defines a contract for supplying additional execution arguments to a terrain graph.
    /// </summary>
    public interface ITerrainGraphArgumentsFiller
    {
        /// <summary>
        /// Adds or updates graph argument entries before terrain-graph execution.
        /// </summary>
        /// <param name="graph">The graph that will be executed.</param>
        /// <param name="args">The mutable argument table that should receive the implementation's entries.</param>
        void FillTerrainGraphArguments(TerrainGraph graph, IDictionary<int, Args> args);
    }
}
#endif


