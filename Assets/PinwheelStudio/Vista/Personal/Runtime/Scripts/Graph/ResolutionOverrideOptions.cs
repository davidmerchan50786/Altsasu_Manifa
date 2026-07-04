#if VISTA

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Specifies how an image node chooses the resolution of its generated output.
    /// </summary>
    public enum ResolutionOverrideOptions
    {
        /// <summary>
        /// Derives the output resolution from the graph request resolution.
        /// </summary>
        RelativeToGraph,
        /// <summary>
        /// Derives the output resolution from the main input connected to the node.
        /// </summary>
        RelativeToMainInput,
        /// <summary>
        /// Uses the node's explicitly configured absolute resolution.
        /// </summary>
        Absolute
    }
}
#endif


