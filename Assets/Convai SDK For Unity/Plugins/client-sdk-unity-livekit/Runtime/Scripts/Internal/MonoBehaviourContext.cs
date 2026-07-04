using System;
using System.Collections;
using UnityEngine;

namespace LiveKit
{
    /// <summary>
    /// Global singleton used for accessing specific MonoBehaviour methods from
    /// non-MonoBehaviour contexts.
    /// </summary>
    internal class MonoBehaviourContext : MonoBehaviour
    {
        private static MonoBehaviourContext _instance;
        private const string OBJECT_NAME = "LiveKitSDK";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            // If an instance already exists and is still valid, do not create another.
            if (_instance != null)
            {
                return;
            }

            // Try to find an existing MonoBehaviourContext in the scene (e.g., from a bootstrap scene).
            var existing = FindFirstObjectByType<MonoBehaviourContext>();
            if (existing != null)
            {
                _instance = existing;
                DontDestroyOnLoad(existing.gameObject);
                return;
            }

            // No existing instance was found, create a new one.
            var obj = new GameObject(OBJECT_NAME);
            _instance = obj.AddComponent<MonoBehaviourContext>();
            DontDestroyOnLoad(obj);
        }

        /// <summary>
        /// Invoked when the application is paused or resumed.
        /// </summary>
        internal static Action<bool> OnApplicationPauseEvent;

        /// <summary>
        /// Runs a coroutine from a non-MonoBehaviour context, invoking the callback when the
        /// coroutine completes.
        /// </summary>
        internal static void RunCoroutine(IEnumerator coroutine, Action onComplete = null)
        {
            _instance.StartCoroutine(WrapCoroutine(coroutine, onComplete));
        }

        private static IEnumerator WrapCoroutine(IEnumerator coroutine, Action onComplete = null)
        {
            yield return coroutine;
            onComplete?.Invoke();
        }

        private void OnApplicationPause(bool pause)
        {
            OnApplicationPauseEvent?.Invoke(pause);
        }
    }
}