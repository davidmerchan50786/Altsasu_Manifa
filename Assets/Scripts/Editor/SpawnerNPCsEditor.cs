// Assets/Scripts/Editor/SpawnerNPCsEditor.cs
// Tools → Alsasua → 👥 Poblar Zone_01 con Civiles

using UnityEngine;
using UnityEditor;
using UnityEngine.AI;

public static class SpawnerNPCsEditor
{
    private const int    NUM_NPCS  = 25;
    private const float  CENTRO_X  = GeoDataAlsasua.OX, CENTRO_Z = GeoDataAlsasua.OZ;
    private const float  RADIO     = 200f;

    [MenuItem("Tools/Alsasua/Mundo/👥 Poblar Zone_01", priority = 53)]
    public static void PoblarCiviles()
    {
        var zona = GameObject.Find("Zone_01_HerrikoPlaza_Centro");
        if (zona == null) { Debug.LogWarning("[Spawner] Zone_01 no encontrada."); return; }

        bool activa = zona.activeSelf; zona.SetActive(true);

        // Borrar civiles anteriores
        var existentes = zona.transform.Find("Civiles");
        if (existentes != null) Undo.DestroyObjectImmediate(existentes.gameObject);

        var contenedor = new GameObject("Civiles");
        Undo.RegisterCreatedObjectUndo(contenedor, "Poblar civiles");
        contenedor.transform.SetParent(zona.transform, false);

        int colocados = 0;
        int intentos  = 0;

        while (colocados < NUM_NPCS && intentos < NUM_NPCS * 5)
        {
            intentos++;
            Vector2 offset = Random.insideUnitCircle * RADIO;
            Vector3 pos = new Vector3(CENTRO_X + offset.x, 0, CENTRO_Z + offset.y);

            // Calcular Y del terreno
            float y = Terrain.activeTerrain != null
                ? Terrain.activeTerrain.SampleHeight(pos) + 0.05f
                : 241f;
            pos.y = y;

            // Verificar que no está dentro de un edificio
            if (Physics.OverlapSphere(pos, 0.4f, ~0, QueryTriggerInteraction.Ignore)
                .Length > 0) continue;

            var go = new GameObject($"Civil_{colocados:D2}");
            Undo.RegisterCreatedObjectUndo(go, "Crear civil");
            go.transform.SetParent(contenedor.transform, false);
            go.transform.position = pos;
            go.tag   = "NPC";
            go.layer = 10; // NPC layer
            go.AddComponent<CapsuleCollider>().height = 1.8f;
            go.AddComponent<NPCCivil>();
            colocados++;
        }

        zona.SetActive(activa);
        EditorUtility.SetDirty(zona);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        Debug.Log($"[Spawner] ✅ {colocados} civiles colocados en Zone_01.");
    }
}
