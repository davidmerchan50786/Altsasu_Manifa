#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class FixTags
{
    static readonly string[] TAGS_REQUERIDAS = {
        "Player", "Enemy", "Vehicle", "NPC", "Bullet", "Explosion", "Barricada"
    };

    static FixTags()
    {
        AñadirTags();
    }

    [MenuItem("Altsasu GTA/Utilidades/Asegurar Tags necesarias", false, 340)]
    public static void AñadirTagsMenu() => AñadirTags();

    static void AñadirTags()
    {
        var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (asset == null || asset.Length == 0) return;

        var so = new SerializedObject(asset[0]);
        var tagsProp = so.FindProperty("tags");
        if (tagsProp == null) return;

        var existentes = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < tagsProp.arraySize; i++)
            existentes.Add(tagsProp.GetArrayElementAtIndex(i).stringValue);

        int añadidas = 0;
        foreach (var t in TAGS_REQUERIDAS)
        {
            if (!existentes.Contains(t))
            {
                tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
                tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = t;
                añadidas++;
            }
        }
        if (añadidas > 0)
        {
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Tags] ✓ {añadidas} tags añadidas: {string.Join(", ", TAGS_REQUERIDAS)}");
        }
    }
}
#endif
