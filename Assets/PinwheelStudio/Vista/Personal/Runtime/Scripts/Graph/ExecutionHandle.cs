#if VISTA
using System;
using System.Collections.Generic;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Represents one asynchronous graph execution run.
    /// </summary>
    /// <remarks>
    /// This handle is returned by <see cref="TerrainGraph.Execute(string[], TerrainGenerationConfigs, GraphInputContainer, FillArgumentsHandler)"/>.
    /// Callers can yield on it as a <see cref="ProgressiveTask"/>, inspect execution progress while it
    /// runs, extract outputs from <see cref="data"/> after completion, and then dispose it to stop any
    /// remaining coroutines and release still-owned transient resources.
    /// </remarks>
    public class ExecutionHandle : ProgressiveTask, IDisposable
    {
        private List<CoroutineHandle> m_coroutines;
        internal List<CoroutineHandle> coroutines
        {
            get
            {
                return m_coroutines;
            }
        }

        private DataPool m_data;
        /// <summary>
        /// Transient resource pool owned by this execution.
        /// </summary>
        /// <remarks>
        /// Completed graph outputs remain in this pool until the caller removes them, typically with
        /// <see cref="DataPool.RemoveRTFromPool(string)"/> or
        /// <see cref="DataPool.RemoveBufferFromPool(string)"/>. Anything left in the pool when
        /// <see cref="Dispose"/> is called is released automatically.
        /// </remarks>
        public DataPool data
        {
            get
            {
                return m_data;
            }
        }

        private ExecutionProgress m_progress;
        /// <summary>
        /// Progress state shared by the currently running graph execution.
        /// </summary>
        /// <remarks>
        /// This object is updated by the progressive execution loop so callers can inspect completed
        /// nodes, total work, or similar runtime progress information while the handle is active.
        /// </remarks>
        public ExecutionProgress progress
        {
            get
            {
                return m_progress;
            }
        }

        /// <summary>
        /// Creates a new execution handle with its own progress tracker, coroutine list, and data pool.
        /// </summary>
        /// <returns>
        /// A ready-to-use handle for one graph execution run.
        /// </returns>
        public static ExecutionHandle Create()
        {
            ExecutionHandle handle = new ExecutionHandle();
            handle.m_data = new DataPool();
            handle.m_progress = new ExecutionProgress();
            handle.m_coroutines = new List<CoroutineHandle>();
            return handle;
        }

        /// <summary>
        /// Stops any tracked execution coroutines and releases resources still owned by this handle.
        /// </summary>
        /// <remarks>
        /// Call this after you have removed any outputs you want to keep from <see cref="data"/>.
        /// Disposing the handle without extracting outputs first will also dispose those pooled graph
        /// results.
        /// </remarks>
        public void Dispose()
        {
            if (m_coroutines != null)
            {
                foreach (CoroutineHandle c in m_coroutines)
                {
                    CoroutineUtility.StopCoroutine(c);
                }
            }
            if (m_data != null)
            {
                m_data.Dispose();
            }
        }
    }
}
#endif


