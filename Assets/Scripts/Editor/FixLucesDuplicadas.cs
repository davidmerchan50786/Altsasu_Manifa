#if UNITY_EDITOR
// Assets/Scripts/Editor/FixLucesDuplicadas.cs
// Deja UNA sola directional light en la escena (la primera con shadows).

using System.Linq;
using UnityEngine;
using UnityEditor;

public static class FixLucesDuplicadas
{
    [MenuItem("Altsasu GTA/Utilidades/★ Fix luces duplicadas (1 sola sombra)", false, 345)]
    public static void Fix()
    {
        var luces = Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                          .Where(l => l.type == LightType.Directional).ToList();

        if (luces.Count == 0)
        {
            EditorUtility.DisplayDialog("Sin luces", "No hay directional lights en escena.", "OK");
            return;
        }

        // Mantener la primera con sombras (o la primera si ninguna las tiene)
        Light principal = luces.FirstOrDefault(l => l.shadows != LightShadows.None) ?? luces[0];
        principal.shadows = LightShadows.Soft;
        principal.gameObject.name = "Sun_Principal";

        int eliminadas = 0;
        foreach (var l in luces)
        {
            if (l == principal) continue;
            Undo.DestroyObjectImmediate(l.gameObject);
            eliminadas++;
        }

        EditorUtility.DisplayDialog("✅ Luces arregladas",
            $"Mantenida: {principal.gameObject.name}\n" +
            $"Eliminadas: {eliminadas} duplicadas\n\n" +
            "Warning de Cascade Shadow atlasing resuelto.", "OK");
    }
}
#endif
