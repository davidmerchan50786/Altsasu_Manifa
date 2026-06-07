// Assets/Scripts/Editor/ReimportarAudio.cs
// Menú: Tools → Alsasua → 🔊 Reimportar Audio
// Soluciona "FMOD error: File not found" tras limpiar Library/artifacts.

using UnityEngine;
using UnityEditor;

public static class ReimportarAudio
{
    [MenuItem("Tools/Alsasua/Debug/🔊 Reimportar Audio", priority = 50)]
    public static void Reimportar()
    {
        var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" });
        int n = 0;
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            n++;
        }
        AssetDatabase.Refresh();
        Debug.Log($"[ReimportarAudio] ✅ {n} clips de audio reimportados. " +
                  "Reinicia Play Mode para aplicar los cambios.");
    }
}
