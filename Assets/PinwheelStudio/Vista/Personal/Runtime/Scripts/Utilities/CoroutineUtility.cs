#if VISTA
using System.Collections;
#if UNITY_EDITOR
using Unity.EditorCoroutines.Editor;
#endif

namespace Pinwheel.Vista
{
    /// <summary>
    /// Starts and stops Vista coroutines through a single API that works in both the Unity editor and play mode.
    /// </summary>
    public class CoroutineUtility
    {
        private static CoroutineManager manager { get; set; }

        /// <summary>
        /// Starts a coroutine and returns a handle that can later be passed back to <see cref="StopCoroutine(CoroutineHandle)"/>.
        /// </summary>
        /// <param name="coroutine">The enumerator to run.</param>
        /// <returns>
        /// A Vista coroutine handle that wraps either an editor coroutine or a runtime coroutine, depending on the current execution environment.
        /// </returns>
        /// <remarks>
        /// The backing <see cref="CoroutineManager"/> instance is created lazily the first time this method is called.
        /// </remarks>
        public static CoroutineHandle StartCoroutine(IEnumerator coroutine)
        {
            if (manager == null)
            {
                manager = CoroutineManager.CreateInstance();
            }

            CoroutineHandle handler = new CoroutineHandle();
            handler.manager = manager;
#if UNITY_EDITOR
            handler.coroutine = EditorCoroutineUtility.StartCoroutine(coroutine, manager);
#else
            handler.coroutine = manager.StartCoroutine(coroutine);
#endif
            return handler;
        }

        /// <summary>
        /// Stops a coroutine previously started through <see cref="StartCoroutine(IEnumerator)"/>.
        /// </summary>
        /// <param name="coroutine">The handle returned when the coroutine was started.</param>
        public static void StopCoroutine(CoroutineHandle coroutine)
        {
            if (coroutine.coroutine != null)
            {
#if UNITY_EDITOR
                EditorCoroutineUtility.StopCoroutine(coroutine.coroutine);
#else
                coroutine.manager.StopCoroutine(coroutine.coroutine);
#endif
            }
        }
    }
}
#endif


