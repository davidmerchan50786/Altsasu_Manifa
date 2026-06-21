// Assets/Scripts/Editor/BakeadorNavMeshV3.cs
// ═══════════════════════════════════════════════════════════════════════════
//  BAKEADOR NAVMESH V3 — bake de NavMesh sobre el terreno Mosaico V3
//
//  El NavMesh por defecto se bake sobre Terrain objects. Cuando MosaicoV3Sistema
//  oculta los renders del Terrain pero conserva sus TerrainColliders, el NavMesh
//  sigue siendo válido (los colliders son la fuente, no el render).
//
//  SIN EMBARGO: si en el futuro se desactivan los TerrainColliders
//  (MosaicoV3SO.preservarTerrainColliders = false), el NavMesh queda sin
//  geometría y los NPCs caen al vacío. Este tool rebakea el NavMesh sobre
//  las MALLAS V3 (que sí tienen geometría) para ese caso futuro.
//
//  WORKFLOW:
//    1. Hornear el Mosaico V3 (🏔️ Hornear Mosaico V3) — las mallas deben existir
//    2. Ejecutar este tool (🧭 Bake NavMesh sobre Mosaico V3)
//    3. El NavMesh se bake sobre las mallas del anillo 0 (zona jugable) y las
//       mallas del anillo 1 (valle); el anillo 2 no es navegable (sierras)
// ═══════════════════════════════════════════════════════════════════════════

using System.IO;
using UnityEditor;
using UnityEditor.AI;
using UnityEngine;
using UnityEngine.AI;

public static class BakeadorNavMeshV3
{
    [MenuItem("Tools/Alsasua/Mundo/🧭 Bake NavMesh sobre Mosaico V3", priority = 36)]
    static void BakeNavMesh()
    {
        // ── 1. Verificar que existen las mallas V3 ───────────────────────
        if (!AssetDatabase.IsValidFolder("Assets/MosaicoV3"))
        {
            EditorUtility.DisplayDialog("Bake NavMesh V3",
                "No existe Assets/MosaicoV3/. Ejecuta primero:\n" +
                "🏔️ Hornear Mosaico V3", "Vale");
            return;
        }

        // ── 2. Temporalmente instanciar las mallas como GameObjects ─────
        // El NavMesh bake de Unity usa la geometría de objetos en escena.
        var tempRoot = new GameObject("__NavMeshBakeTemp");
        var mallasPrefab = new[] { "terreno_anillo_0.asset", "terreno_anillo_1.asset" };
        bool alguna = false;

        foreach (var nombre in mallasPrefab)
        {
            string path = $"Assets/MosaicoV3/{nombre}";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) continue;

            var go = new GameObject(nombre.Replace(".asset", ""));
            go.transform.SetParent(tempRoot.transform);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            // MeshCollider necesario para que NavMeshBuilder lo incluya
            go.AddComponent<MeshCollider>().sharedMesh = mesh;

            // Marcar como NavigationStatic (capa Walkable)
            GameObjectUtility.SetNavMeshArea(go, 0);  // 0 = Walkable
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.NavigationStatic);
            alguna = true;
        }

        if (!alguna)
        {
            Object.DestroyImmediate(tempRoot);
            EditorUtility.DisplayDialog("Bake NavMesh V3",
                "No se encontraron mallas en Assets/MosaicoV3/.\n" +
                "Ejecuta 🏔️ Hornear Mosaico V3 primero.", "Vale");
            return;
        }

        // ── 3. Lanzar el bake ────────────────────────────────────────────
        if (!EditorUtility.DisplayDialog("Bake NavMesh V3",
            $"Bakeará el NavMesh incluyendo los anillos 0 y 1 del Mosaico V3.\n" +
            "También se incluyen los Terrain originales si están en escena.\n\n" +
            "¿Continuar? (puede tardar 1-5 min)", "Bake", "Cancelar"))
        {
            Object.DestroyImmediate(tempRoot);
            return;
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        UnityEditor.AI.NavMeshBuilder.BuildNavMesh();

        Object.DestroyImmediate(tempRoot);
        AssetDatabase.Refresh();

        Debug.Log("[NavMeshV3] ✅ NavMesh bakeado sobre mallas V3 (anillos 0 y 1). " +
            "Si luego desactivas los TerrainColliders, los NPCs seguirán siendo navegables.");
        EditorUtility.DisplayDialog("Bake NavMesh V3",
            "✅ NavMesh bakeado.\n\n" +
            "Si activas MosaicoV3SO.preservarTerrainColliders = false, los NPCs " +
            "usarán la malla V3 como suelo navegable.", "Perfecto");
    }

    [MenuItem("Tools/Alsasua/Mundo/🔍 Ver NavMesh actual", priority = 37)]
    static void MostrarNavMesh()
        => NavMeshVisualizationSettings.showNavigation = NavMeshVisualizationSettings.showNavigation == 0 ? 1 : 0;
}
