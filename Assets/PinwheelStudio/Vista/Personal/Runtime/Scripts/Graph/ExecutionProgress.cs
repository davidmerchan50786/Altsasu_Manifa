#if VISTA

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Stores progress information for a running graph execution.
    /// </summary>
    /// <remarks>
    /// <see cref="ExecutionHandle"/> exposes one of these objects while a graph is running.
    /// <see cref="TerrainGraph"/> updates <see cref="totalProgress"/> after each node finishes, and
    /// long-running nodes can report finer-grained work through
    /// <see cref="GraphContext.SetCurrentProgress(float)"/>.
    /// </remarks>
    public class ExecutionProgress
    {
        /// <summary>
        /// Coarse graph-level completion ratio for the current execution.
        /// </summary>
        /// <remarks>
        /// This value advances when nodes are dequeued from the execution sequence. It represents
        /// overall graph completion rather than the internal progress of the currently running node.
        /// </remarks>
        public float totalProgress { get; internal set; }
        /// <summary>
        /// Fine-grained progress reported by the node that is currently executing.
        /// </summary>
        /// <remarks>
        /// Nodes that split work across multiple steps can update this value through
        /// <see cref="GraphContext.SetCurrentProgress(float)"/>. Callers should treat it as the
        /// active node's local progress, not as a second graph-wide percentage.
        /// </remarks>
        public float currentProgress { get; set; }

        /// <summary>
        /// Creates a progress object with both progress channels initialized to zero.
        /// </summary>
        public ExecutionProgress()
        {
            totalProgress = 0f;
            currentProgress = 0f;
        }
    }
}
#endif


