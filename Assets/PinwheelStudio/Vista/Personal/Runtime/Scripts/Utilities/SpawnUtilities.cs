#if VISTA
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Pinwheel.Vista
{
    /// <summary>
    /// Wraps GameObject instantiation so Vista can use editor-aware spawning when available.
    /// </summary>
    public static class SpawnUtilities
    {
        /// <summary>
        /// Reserved name used by Vista for the hidden spawn root when one is needed.
        /// </summary>
        public static readonly string ROOT_NAME = "~VistaSpawnerRoot";

        /// <summary>
        /// Instantiates a GameObject, preserving prefab linkage in the editor when the source object is a prefab asset.
        /// </summary>
        /// <param name="original">Prefab asset or scene object to instantiate.</param>
        /// <returns>The spawned GameObject.</returns>
        /// <remarks>
        /// In the editor this method also registers Undo for the created object. Outside the editor it falls back to a normal runtime instantiate.
        /// </remarks>
        public static GameObject Spawn(GameObject original)
        {
            GameObject g = null;
#if UNITY_EDITOR
            bool isPrefab = PrefabUtility.IsPartOfPrefabAsset(original);

            if (isPrefab)
            {
                g = PrefabUtility.InstantiatePrefab(original) as GameObject;
            }
            else
            {
                g = GameObject.Instantiate<GameObject>(original);
            }

            string undoName = string.Format("Spawn {0}", original.name);
            Undo.RegisterCreatedObjectUndo(g, undoName);
#else
            g = GameObject.Instantiate<GameObject>(original);
#endif
            return g;
        }
    }
}
#endif


