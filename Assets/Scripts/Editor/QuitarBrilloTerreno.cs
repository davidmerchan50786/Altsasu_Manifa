#if UNITY_EDITOR
// Assets/Scripts/Editor/QuitarBrilloTerreno.cs
// Pone el terrain en aspecto mate — sin reflejos tipo agua o hielo.

using UnityEngine;
using UnityEditor;

public static class QuitarBrilloTerreno
{
    [MenuItem("Altsasu GTA/Utilidades/★ Quitar brillo del terreno (mate)", false, 330)]
    public static void Quitar()
    {
        int arreglados = 0;

        // 1. Arreglar todas las terrain layers del proyecto
        var guids = AssetDatabase.FindAssets("t:TerrainLayer");
        foreach (var g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null) continue;
            layer.smoothness = 0f;        // 0 = totalmente mate
            layer.metallic   = 0f;        // 0 = no metálico
            layer.specular   = new Color(0.02f, 0.02f, 0.02f);  // sin reflejos especulares
            EditorUtility.SetDirty(layer);
            arreglados++;
        }

        // 2. Arreglar el material del terrain activo si tiene materialTemplate custom
        var terrain = Object.FindFirstObjectByType<Terrain>();
        if (terrain != null && terrain.materialTemplate != null)
        {
            var mat = terrain.materialTemplate;
            mat.SetFloat("_Smoothness", 0f);
            mat.SetFloat("_Metallic",   0f);
            mat.SetFloat("_SpecularHighlights", 0f);
            mat.SetFloat("_GlossyReflections", 0f);
            EditorUtility.SetDirty(mat);
            arreglados++;
        }

        // 3. Forzar refresco del terrain
        if (terrain != null)
        {
            terrain.Flush();
            EditorUtility.SetDirty(terrain.terrainData);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[QuitarBrillo] ✓ {arreglados} materiales/layers ajustados a mate.");
        EditorUtility.DisplayDialog("✅ Terreno mate",
            $"Arreglados {arreglados} materiales:\n• Smoothness → 0\n• Metallic → 0\n\n" +
            "El terreno ya no brillará como agua.", "OK");
    }

    [MenuItem("Altsasu GTA/Utilidades/Quitar brillo TODOS los materiales (escena)", false, 331)]
    public static void QuitarTodos()
    {
        var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        int n = 0;
        foreach (var r in renderers)
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
                if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", 0f);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.05f);
                EditorUtility.SetDirty(m);
                n++;
            }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[QuitarBrillo] ✓ {n} materiales ajustados.");
        EditorUtility.DisplayDialog("✅ OK", $"{n} materiales puestos en mate.", "OK");
    }
}
#endif
