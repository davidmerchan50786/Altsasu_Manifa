#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Se ejecuta automáticamente al abrir el proyecto
[InitializeOnLoad]
public static class FixInputSystem
{
    static FixInputSystem()
    {
        // Activar ambos sistemas de input (Old + New) para compatibilidad total
        var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (settings.Length == 0) return;

        var so = new SerializedObject(settings[0]);
        var prop = so.FindProperty("activeInputHandler");
        if (prop != null && prop.intValue != 2)
        {
            prop.intValue = 2; // 0=Old Input, 1=New Input, 2=Both
            so.ApplyModifiedProperties();
            Debug.Log("[Fix] Active Input Handling → Both. Reiniciando Unity si es necesario...");
        }
    }

    [MenuItem("Altsasu GTA/Utilidades/Fix Input System (Both)", false, 300)]
    static void FixManual()
    {
        var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (settings.Length == 0) { Debug.LogError("No se encontró ProjectSettings.asset"); return; }
        var so = new SerializedObject(settings[0]);
        var prop = so.FindProperty("activeInputHandler");
        if (prop == null) { Debug.LogError("No se encontró activeInputHandler"); return; }
        prop.intValue = 2;
        so.ApplyModifiedProperties();
        EditorUtility.DisplayDialog("✅ Input System", "Active Input Handling → Both\nReinicia Unity para aplicar.", "OK");
    }
}
#endif
