#if VISTA
using System;
using System.Collections.Generic;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Provides convenience queries over graph assets.
    /// </summary>
    public static class GraphAssetExtensions
    {
        /// <summary>
        /// Collects the graph-local variables declared by <see cref="SetVariableNode"/> nodes.
        /// </summary>
        /// <param name="graph">The graph to inspect.</param>
        /// <param name="varNames">The destination list that receives variable names.</param>
        /// <param name="varTypes">The destination list that receives slot types paired with <paramref name="varNames"/> by index.</param>
        /// <remarks>
        /// The method clears both destination lists first, then scans every <see cref="SetVariableNode"/> in the graph. Only
        /// nodes with a non-empty variable name and a valid slot type assignable to <see cref="ISlot"/> are reported.
        /// </remarks>
        public static void GetRegisteredVars(this GraphAsset graph, List<string> varNames, List<Type> varTypes)
        {
            varNames.Clear();
            varTypes.Clear();

            List<SetVariableNode> setVarNodes = graph.GetNodesOfType<SetVariableNode>();
            foreach (SetVariableNode n in setVarNodes)
            {
                if (string.IsNullOrEmpty(n.varName))
                    continue;
                if (!typeof(ISlot).IsAssignableFrom(n.slotType))
                    continue;
                varNames.Add(n.varName);
                varTypes.Add(n.slotType);
            }
        }
    }
}
#endif


