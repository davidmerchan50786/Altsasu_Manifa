#if VISTA
using UnityEngine;
#if UNITY_EDITOR
using Unity.EditorCoroutines.Editor;
#endif

namespace Pinwheel.Vista
{
    /// <summary>
    /// Wraps a running Vista coroutine so the same handle shape can be used in editor and play mode.
    /// </summary>
    /// <remarks>
    /// <see cref="CoroutineUtility"/> returns this wrapper instead of exposing Unity's coroutine types
    /// directly. That lets higher-level systems such as <see cref="Graph.ExecutionHandle"/> track and
    /// stop coroutines without caring whether they are backed by <see cref="EditorCoroutine"/> or a
    /// runtime <see cref="Coroutine"/>.
    /// </remarks>
    public class CoroutineHandle
    {
#if UNITY_EDITOR
        /// <summary>
        /// Editor coroutine currently wrapped by this handle when running in the editor coroutine path.
        /// </summary>
        public EditorCoroutine coroutine { get; set; }
#else
        /// <summary>
        /// Runtime Unity coroutine currently wrapped by this handle when running in play mode.
        /// </summary>
        public Coroutine coroutine { get; set; }
#endif
        internal CoroutineManager manager { get; set; }
    }
}
#endif


