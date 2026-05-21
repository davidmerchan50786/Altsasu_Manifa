// Assets/Scripts/Editor/ConstructorNPCsPrefabs.cs
// Tools → Alsasua → 👥 Construir Prefabs NPC
//
// Crea 6 variantes de prefab NPC en Assets/Prefabs/NPCs/:
//   Civil_Hombre, Civil_Mujer, Txikitero, Manifestante, PoliciaForal, Periodista
// Cada prefab: CapsuleCollider + NavMeshAgent + NPCCivil + color distintivo.
// Sin modelo 3D asignado → NPCCivil crea cápsula coloreada en runtime.

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

public static class ConstructorNPCsPrefabs
{
    const string DIR = "Assets/Prefabs/NPCs";

    static readonly (string nombre, Color color, float vel, float radioHuida)[] VARIANTES =
    {
        ("Civil_Hombre",   new Color(0.6f, 0.8f, 1.0f), 1.3f, 25f),
        ("Civil_Mujer",    new Color(1.0f, 0.7f, 0.8f), 1.2f, 28f),
        ("Txikitero",      new Color(0.9f, 0.6f, 0.2f), 1.0f, 15f),
        ("Manifestante",   new Color(0.8f, 0.1f, 0.1f), 1.5f, 35f),
        ("PoliciaForal",   new Color(0.1f, 0.3f, 0.7f), 1.8f, 5f),
        ("Periodista",     new Color(0.2f, 0.7f, 0.2f), 1.4f, 40f),
    };

    // [DESACTIVADO] [MenuItem("Tools/Alsasua/👥 Construir Prefabs NPC", priority = 25)]
    public static void Construir()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(DIR))
            AssetDatabase.CreateFolder("Assets/Prefabs", "NPCs");

        int creados = 0;
        foreach (var (nombre, color, vel, radioHuida) in VARIANTES)
        {
            string ruta = $"{DIR}/{nombre}.prefab";

            var root = new GameObject(nombre);

            // Collider
            var cap = root.AddComponent<CapsuleCollider>();
            cap.height = 1.8f;
            cap.radius = 0.3f;
            cap.center = new Vector3(0, 0.9f, 0);

            // NavMeshAgent
            var agent = root.AddComponent<UnityEngine.AI.NavMeshAgent>();
            agent.height          = 1.8f;
            agent.radius          = 0.3f;
            agent.speed           = vel;
            agent.angularSpeed    = 200f;
            agent.stoppingDistance = 0.5f;
            agent.enabled         = false; // NPCCivil lo activa al NavMesh listo

            // NPCCivil
            var npc = root.AddComponent<NPCCivil>();
            npc.velocidadAndar  = vel;
            npc.velocidadHuida  = vel * 3.2f;
            npc.radioEscucha    = radioHuida;

            // Material de color identificativo (asignado en CrearCuerpoSimple de NPCCivil)
            // Lo preconfiguramos via MaterialPropertyBlock en un renderer hijo
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform);
            body.transform.localPosition = new Vector3(0, 0.9f, 0);
            body.transform.localScale    = new Vector3(0.55f, 0.9f, 0.55f);
            Object.DestroyImmediate(body.GetComponent<CapsuleCollider>());

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ??
                                   Shader.Find("HDRP/Lit") ??
                                   Shader.Find("Standard"));
            mat.color = color;
            mat.name  = $"Mat_{nombre}";
            AssetDatabase.CreateAsset(mat, $"{DIR}/Mat_{nombre}.mat");
            body.GetComponent<Renderer>().sharedMaterial = mat;

            // Guardar prefab
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, ruta);
            Object.DestroyImmediate(root);
            creados++;

            Debug.Log($"[NPCs] ✅ {nombre}.prefab creado");
        }

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("👥 NPCs creados",
            $"{creados} prefabs NPC listos en {DIR}/\n\n" +
            "SpawnerNPCsEditor los distribuirá por el mapa.\n" +
            "Tools → Alsasua → Poblar Civiles",
            "OK");
    }
}
#endif
