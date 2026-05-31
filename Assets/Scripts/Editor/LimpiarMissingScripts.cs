#if UNITY_EDITOR
// Assets/Scripts/Editor/LimpiarMissingScripts.cs
// Elimina componentes "Missing Script" de todos los GameObjects de la escena.
// Útil tras borrar scripts referenciados por componentes serializados.

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class LimpiarMissingScripts
{
    [MenuItem("Altsasu GTA/Utilidades/★ Limpiar Missing Scripts en escena", false, 350)]
    public static void Limpiar()
    {
        var escena = SceneManager.GetActiveScene();
        if (!escena.IsValid())
        {
            EditorUtility.DisplayDialog("Sin escena", "No hay escena activa.", "OK");
            return;
        }

        int totalEliminados = 0;
        int gameObjectsAfectados = 0;

        foreach (var root in escena.GetRootGameObjects())
        {
            foreach (var go in root.GetComponentsInChildren<Transform>(true))
            {
                int eliminados = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go.gameObject);
                if (eliminados > 0)
                {
                    totalEliminados += eliminados;
                    gameObjectsAfectados++;
                    EditorUtility.SetDirty(go.gameObject);
                }
            }
        }

        if (totalEliminados > 0)
        {
            EditorSceneManager.MarkSceneDirty(escena);
            EditorSceneManager.SaveOpenScenes();
        }

        EditorUtility.DisplayDialog("✅ Limpieza completa",
            $"Componentes Missing eliminados: {totalEliminados}\n" +
            $"GameObjects afectados: {gameObjectsAfectados}",
            "OK");
    }
}
#endif
