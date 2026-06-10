// Assets/Scripts/Editor/NavMeshBaker.cs
// Bake automático de NavMesh para que la IA pueda navegar por Alsasua.
// Menú: Altsasu GTA → MAESTRO → Bake NavMesh

using UnityEngine;
using UnityEditor;
using Unity.AI.Navigation;
using UnityEngine.AI;

public static class NavMeshBaker
{
    // MENÚ-LEGACY: [MenuItem("Altsasu GTA/MAESTRO/Bake NavMesh (IA peatones y policía)", false, 27)]
    public static void BakeNavMesh()
    {
        // 1. Asegurar que el Terrain está marcado como NavigationStatic
        var terrain = Object.FindFirstObjectByType<Terrain>();
        if (terrain != null)
        {
            GameObjectUtility.SetStaticEditorFlags(terrain.gameObject,
                StaticEditorFlags.NavigationStatic |
                StaticEditorFlags.ContributeGI |
                StaticEditorFlags.OccludeeStatic);
            Debug.Log("[NavMesh] Terrain marcado como NavigationStatic.");
        }
        else Debug.LogWarning("[NavMesh] No hay Terrain — bake sobre geometría de carreteras.");

        // 2. Marcar carreteras como NavigationStatic (zona caminable)
        var roads = GameObject.Find("=== Carreteras ===");
        if (roads != null) MarcarHijosNavStatic(roads.transform);

        var plazas = GameObject.Find("=== Plazas ===");
        if (plazas != null) MarcarHijosNavStatic(plazas.transform);

        var caminos = GameObject.Find("=== Caminos ===");
        if (caminos != null) MarcarHijosNavStatic(caminos.transform);

        // 3. Marcar edificios como NavigationStatic OBSTACLE (no entrar)
        var edifs = GameObject.Find("=== Edificios ===");
        if (edifs != null)
        {
            foreach (Transform h in edifs.transform)
            {
                GameObjectUtility.SetStaticEditorFlags(h.gameObject,
                    StaticEditorFlags.NavigationStatic);
                // Añadir NavMeshObstacle a edificios para que la IA los evite
                foreach (var col in h.GetComponentsInChildren<MeshCollider>())
                {
                    if (col.GetComponent<NavMeshObstacle>() == null)
                    {
                        var obs = col.gameObject.AddComponent<NavMeshObstacle>();
                        obs.carving = true;
                        obs.shape   = NavMeshObstacleShape.Box;
                    }
                }
            }
        }

        // 4. Crear NavMeshSurface si no existe (requiere com.unity.ai.navigation)
        CrearNavMeshSurface();

        // 5. Bake
        var surface = Object.FindFirstObjectByType<NavMeshSurface>();
        if (surface != null)
        {
            surface.BuildNavMesh();
            EditorUtility.SetDirty(surface.gameObject);
            Debug.Log("[NavMesh] ✅ NavMesh bakeado. Los NPCs pueden navegar por Alsasua.");
        }
        else
        {
            Debug.LogWarning("[NavMesh] NavMeshSurface no encontrada. Instala el paquete 'AI Navigation' (ya en manifest.json).");
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    static void MarcarHijosNavStatic(Transform padre)
    {
        foreach (Transform h in padre)
            GameObjectUtility.SetStaticEditorFlags(h.gameObject,
                StaticEditorFlags.NavigationStatic);
    }

    static void CrearNavMeshSurface()
    {
        if (Object.FindFirstObjectByType<NavMeshSurface>() != null) return;

        // Añadir al GameManager para centralización
        var gmGO = Object.FindFirstObjectByType<GameManagerAltsasua>()?.gameObject
                ?? GameObject.Find("AltsasuCore")
                ?? new GameObject("NavMeshSurface");

        var surface = gmGO.AddComponent<NavMeshSurface>();
        surface.collectObjects  = CollectObjects.All;
        surface.useGeometry     = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask       = ~0; // todas las capas

        // Configuración óptima para área urbana de ~500m de radio
        surface.overrideTileSize   = true;
        surface.tileSize           = 256;
        surface.overrideVoxelSize  = true;
        surface.voxelSize          = 0.15f; // 15cm — suficiente para personas

        // AgentType: Humanoid (altura 1.8m, radio 0.35m, escalón 0.4m, pendiente 45°)
        surface.agentTypeID = 0; // default Humanoid

        EditorUtility.SetDirty(gmGO);
        Debug.Log("[NavMesh] NavMeshSurface creado en " + gmGO.name);
    }
}
