// Assets/Scripts/Editor/ConstructorNPCsFacciones.cs
// ═══════════════════════════════════════════════════════════════════════════
//  Crea prefabs de NPC para las facciones del juego usando los modelos de
//  Personajes/ + sus skins de facción.
//
//  Facciones generadas:
//    · Manifestante  → PersonajeBase + skin_manifestante.png
//    · Jarrai        → PersonajeBase + skin_jarrai.png
//    · Civil mujer   → PersonajeBase + skin_mujer.png
//    · Civil hombre  → PersonajeBase + skin_civil.png
//    · Kenney crowd  → 12 variantes mini-character (male/female a-f)
//
//  Salida: Assets/Resources/Prefabs/NPCs/
//  Menú: Tools/Alsasua/Assets/🧍 Construir NPCs Facciones
// ═══════════════════════════════════════════════════════════════════════════

using System.IO;
using UnityEditor;
using UnityEngine;

public static class ConstructorNPCsFacciones
{
    const string RUTA_MODELOS   = "Assets/Personajes/Modelos";
    const string RUTA_TEXTURAS  = "Assets/Personajes/Texturas";
    const string RUTA_KENNEY    = "Assets/Personajes/Kenney/mini-characters/Models/FBX format";
    const string RUTA_ANIMS     = "Assets/Animators/Personajes";
    const string RUTA_PREFABS   = "Assets/Resources/Prefabs/NPCs";

    // ── Facciones principales (PersonajeBase + skin) ───────────────────────
    static readonly (string prefabName, string skin, string animator, string tag)[] FACCIONES =
    {
        ("NPC_Manifestante",    "skin_manifestante",  "NPC_Civil_Animator",       "Manifestante"),
        ("NPC_Jarrai",          "skin_jarrai",        "NPC_Civil_Animator",       "Manifestante"),
        ("NPC_Civil_Mujer",     "skin_mujer",         "NPC_Civil_Animator",       "Civilian"),
        ("NPC_Civil_Kenney",    "skin_civil",         "NPC_Civil_Animator",       "Civilian"),
        ("NPC_GuardiaCivil_K",  "skin_guardia",       "NPC_GuardiaCivil_Animator","GuardiaCivil"),
    };

    // ── Variantes Kenney crowd ─────────────────────────────────────────────
    static readonly string[] KENNEY_MODELS = {
        "character-male-a", "character-male-b", "character-male-c",
        "character-male-d", "character-male-e", "character-male-f",
        "character-female-a", "character-female-b", "character-female-c",
        "character-female-d", "character-female-e", "character-female-f",
    };

    [MenuItem("Tools/Alsasua/Assets/🧍 Construir NPCs Facciones", priority = 6)]
    public static void Construir()
    {
        Directory.CreateDirectory(RUTA_PREFABS);
        AssetDatabase.Refresh();

        int ok = 0, err = 0;

        // ── Facciones con PersonajeBase ────────────────────────────────────
        var modeloBase = AssetDatabase.LoadAssetAtPath<GameObject>($"{RUTA_MODELOS}/PersonajeBase.fbx");
        if (modeloBase == null)
        {
            Debug.LogError("[NPCsFacciones] No se encontró PersonajeBase.fbx en " + RUTA_MODELOS);
            err++;
        }
        else
        {
            foreach (var (nombre, skin, animNombre, tag) in FACCIONES)
            {
                if (CrearPrefabFaccion(modeloBase, nombre, skin, animNombre, tag))
                    ok++;
                else
                    err++;
            }
        }

        // ── Kenney crowd ───────────────────────────────────────────────────
        var animCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
            $"{RUTA_ANIMS}/NPC_Civil_Animator.controller");

        foreach (var modelName in KENNEY_MODELS)
        {
            string ruta = $"{RUTA_KENNEY}/{modelName}.fbx";
            var modelo = AssetDatabase.LoadAssetAtPath<GameObject>(ruta);
            if (modelo == null) { Debug.LogWarning($"[NPCsFacciones] No encontrado: {ruta}"); err++; continue; }

            string prefabPath = $"{RUTA_PREFABS}/NPC_Crowd_{modelName}.prefab";
            var go = (GameObject)PrefabUtility.InstantiatePrefab(modelo);
            go.name = $"NPC_Crowd_{modelName}";
            go.tag = "Civilian";
            go.layer = LayerMask.NameToLayer("NPC") >= 0 ? LayerMask.NameToLayer("NPC") : 0;

            AgregarComponentesNPC(go, animCtrl);

            PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            ok++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string resumen = $"✅ {ok} prefabs NPC creados en {RUTA_PREFABS}/\n" +
                         (err > 0 ? $"⚠ {err} errores (ver Console)" : "");
        Debug.Log("[NPCsFacciones] " + resumen);
        EditorUtility.DisplayDialog("NPCs Facciones", resumen +
            "\n\nEjecuta ahora: Tools/Alsasua/Assets/⭐ Asignar Todos los Assets AAA+\npara conectarlos a ConfiguradorAssetsAAA.", "OK");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    static bool CrearPrefabFaccion(GameObject modeloBase, string nombre, string skinName,
                                   string animNombre, string tag)
    {
        string prefabPath = $"{RUTA_PREFABS}/{nombre}.prefab";
        string texPath    = $"{RUTA_TEXTURAS}/{skinName}.png";
        string animPath   = $"{RUTA_ANIMS}/{animNombre}.controller";

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        var animCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(animPath);

        if (tex == null)
            Debug.LogWarning($"[NPCsFacciones] Textura no encontrada: {texPath}");

        var go = (GameObject)PrefabUtility.InstantiatePrefab(modeloBase);
        go.name = nombre;
        go.tag = tag;
        go.layer = LayerMask.NameToLayer("NPC") >= 0 ? LayerMask.NameToLayer("NPC") : 0;

        // Aplicar skin a todos los SkinnedMeshRenderers
        if (tex != null)
        {
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // Crear material HDRP Lit con la skin
                var mat = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
                mat.mainTexture = tex;
                mat.name = $"Mat_{nombre}";
                string matPath = $"Assets/Materials/{nombre}_Mat.mat";
                Directory.CreateDirectory("Assets/Materials");
                AssetDatabase.CreateAsset(mat, matPath);
                smr.sharedMaterial = mat;
            }
        }

        AgregarComponentesNPC(go, animCtrl);

        PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);
        Debug.Log($"[NPCsFacciones] ✓ {nombre} → {prefabPath}");
        return true;
    }

    static void AgregarComponentesNPC(GameObject go, RuntimeAnimatorController animCtrl)
    {
        // Animator
        var anim = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
        if (animCtrl != null) anim.runtimeAnimatorController = animCtrl;
        anim.applyRootMotion = false;

        // Collider cápsula si no tiene
        if (go.GetComponent<Collider>() == null)
        {
            var cap = go.AddComponent<CapsuleCollider>();
            cap.height = 1.8f;
            cap.radius = 0.3f;
            cap.center = new Vector3(0f, 0.9f, 0f);
        }

        // Rigidbody si no tiene
        if (go.GetComponent<Rigidbody>() == null)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        // NavMeshAgent si no tiene
        var agentType = System.Type.GetType("UnityEngine.AI.NavMeshAgent, UnityEngine");
        if (agentType != null && go.GetComponent(agentType) == null)
            go.AddComponent(agentType);

        // NPCBase si existe en el proyecto
        var npcType = System.Type.GetType("NPCBase");
        if (npcType != null && go.GetComponent(npcType) == null)
            go.AddComponent(npcType);
    }
}
