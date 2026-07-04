#if VISTA
using UnityEngine;

namespace Pinwheel.Vista
{
    [AddComponentMenu("")]
    internal class CoroutineManager : MonoBehaviour
    {
        /// <summary>
        /// Creates the hidden helper object that hosts Vista runtime coroutines.
        /// </summary>
        /// <returns>A new manager component attached to a non-saved hidden GameObject.</returns>
        public static CoroutineManager CreateInstance()
        {
            GameObject g = new GameObject("Vista Coroutine Manager");
            g.hideFlags = HideFlags.HideAndDontSave;
            CoroutineManager managerComponent = g.AddComponent<CoroutineManager>();
            return managerComponent;
        }
    }
}
#endif


