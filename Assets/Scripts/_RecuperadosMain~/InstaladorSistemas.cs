#if UNITY_EDITOR
// Assets/Scripts/Editor/InstaladorSistemas.cs
// ═══════════════════════════════════════════════════════════════════════════
//  INSTALADOR DE SISTEMAS RUNTIME — añade los managers a la escena de forma
//  segura. Llama una vez antes de Play.
//
//   · SistemaLluviaVisual (lluvia + rayos)
//   · SistemaSuperficiesMojadas (cambio shader cuando llueve)
//   · SistemaDiaNocheReal (rotación sol)
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class InstaladorSistemas
{
    public static void Instalar()
    {
        EnsureComponent<SistemaLluviaVisual>("_SistemaLluviaVisual");
        EnsureComponent<SistemaSuperficiesMojadas>("_SistemaSuperficiesMojadas");
        EnsureComponent<SistemaDiaNocheReal>("_SistemaDiaNoche");

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        EditorUtility.DisplayDialog("✅ Sistemas instalados",
            "Sistemas runtime añadidos a la escena:\n\n" +
            "• SistemaLluviaVisual (rayos + lluvia)\n" +
            "• SistemaSuperficiesMojadas\n" +
            "• SistemaDiaNocheReal\n\n" +
            "Activa en Play. Puedes ajustar intensidad de lluvia\n" +
            "y hora del día en el Inspector durante Play.", "OK");
    }

    static void EnsureComponent<T>(string nombreGO) where T : Component
    {
        var existente = Object.FindFirstObjectByType<T>();
        if (existente != null) return;
        var go = GameObject.Find(nombreGO) ?? new GameObject(nombreGO);
        go.AddComponent<T>();
        Undo.RegisterCreatedObjectUndo(go, "Instalar " + typeof(T).Name);
    }
}
#endif
