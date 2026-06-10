#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public static class LimpiarArbolesTerreno
{
    [MenuItem("Altsasu GTA/Utilidades/Limpiar prototipos árbol vacíos", false, 310)]
    static void Limpiar()
    {
        var terrain = Object.FindFirstObjectByType<Terrain>();
        if (terrain == null) { Debug.LogWarning("No hay Terrain en escena."); return; }
        terrain.terrainData.treePrototypes = new TreePrototype[0];
        terrain.terrainData.treeInstances  = new TreeInstance[0];
        EditorUtility.SetDirty(terrain.terrainData);
        Debug.Log("✓ Prototipos de árbol vacíos eliminados del Terrain.");
    }
}
#endif
