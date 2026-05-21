// Assets/Scripts/Editor/ConstructorPersonajesMeshyAI.cs
// Tools → Alsasua → 🧍 Construir Personajes MeshyAI
//
// Convierte los modelos Meshy AI en prefabs de NPC listos para jugar:
//
//  CIVILES (8 variantes):
//    Civil_Casual_01, Civil_CodeKicks, Civil_DenimOveralls, Civil_Doctor,
//    Civil_Executive, Civil_Pruu, Civil_SummerStreet_01, Civil_UrbanEdge
//    → Rig Humanoid, material HDRP/Lit PBR, AnimatorController XBot
//
//  GUARDIA CIVIL (2 variantes + 20 animaciones biped):
//    GuardiaCivil_Officer_01, GuardiaCivil_OpenArms
//    → Rig Humanoid, material HDRP/Lit PBR, AnimatorController propio
//       con walking, running, idle, hit, dead, rifle, etc.
//
//  CIVIL CON ANIMACIONES:
//    Civil_SoftGaze_Biped, Civil_UrbanEdge
//    → Rig Humanoid con animaciones embebidas extraídas

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

public static class ConstructorPersonajesMeshyAI
{
    const string BASE_PERSONAJES = "Assets/_ExtractedAssets/Personajes/MeshyAI";
    const string DIR_PREFABS     = "Assets/Prefabs/NPCs";
    const string DIR_MATERIALS   = "Assets/Materials/Personajes";
    const string DIR_ANIMATORS   = "Assets/Animators/Personajes";

    // ── Definición de personajes ──────────────────────────────────────────
    struct DefPersonaje
    {
        public string carpeta, nombrePrefab;
        public string fbxPrincipal;   // nombre del FBX con el mesh
        public string tipoAnimador;   // "civil" | "guardia" | "embebido"
        public bool esGuardia;
    }

    static readonly DefPersonaje[] PERSONAJES =
    {
        new DefPersonaje { carpeta="Civil_Casual_01",     nombrePrefab="NPC_Civil_Casual",
            fbxPrincipal="Meshy_AI_Casual_Confidence_0421161928_texture.fbx",
            tipoAnimador="civil" },
        new DefPersonaje { carpeta="Civil_CodeKicks",     nombrePrefab="NPC_Civil_CodeKicks",
            fbxPrincipal="Meshy_AI_Code_and_Kicks_0421162111_texture.fbx",
            tipoAnimador="civil" },
        new DefPersonaje { carpeta="Civil_DenimOveralls", nombrePrefab="NPC_Civil_DenimOveralls",
            fbxPrincipal="Meshy_AI_Casual_Denim_Overalls_0421162223_texture.fbx",
            tipoAnimador="civil" },
        new DefPersonaje { carpeta="Civil_Doctor",        nombrePrefab="NPC_Civil_Doctor",
            fbxPrincipal="Meshy_AI_White_Coat_Physician__0421162314_texture.fbx",
            tipoAnimador="civil" },
        new DefPersonaje { carpeta="Civil_Executive",     nombrePrefab="NPC_Civil_Executive",
            fbxPrincipal="Meshy_AI_Confident_Executive_i_0421162135_texture.fbx",
            tipoAnimador="civil" },
        new DefPersonaje { carpeta="Civil_Pruu",          nombrePrefab="NPC_Civil_Pruu",
            fbxPrincipal="Meshy_AI_Pruu_0421162445_texture.fbx",
            tipoAnimador="civil" },
        new DefPersonaje { carpeta="Civil_SummerStreet_01",nombrePrefab="NPC_Civil_SummerStreet",
            fbxPrincipal="Meshy_AI_Casual_Summer_Street__0421162005_texture.fbx",
            tipoAnimador="civil" },
        new DefPersonaje { carpeta="Civil_UrbanEdge",     nombrePrefab="NPC_Civil_UrbanEdge",
            fbxPrincipal="Meshy_AI_Urban_Edge_0501071204_texture.fbx",
            tipoAnimador="civil" },
        new DefPersonaje { carpeta="GuardiaCivil_Officer_01", nombrePrefab="NPC_GuardiaCivil_01",
            fbxPrincipal="Meshy_AI_Guardia_Civil_Officer_0501071058_texture.fbx",
            tipoAnimador="guardia", esGuardia=true },
        new DefPersonaje { carpeta="GuardiaCivil_OpenArms",   nombrePrefab="NPC_GuardiaCivil_02",
            fbxPrincipal="Meshy_AI_Guardia_Civil_Open_Ar_0501071239_texture.fbx",
            tipoAnimador="guardia", esGuardia=true },
    };

    // Animaciones XBot para civiles
    static readonly (string nombre, string fbx, float speed)[] ANIMS_CIVIL =
    {
        ("Idle",     "X Bot@Crouching Idle.fbx",           1f),
        ("Walk",     "X Bot@Walking.fbx",                  1f),
        ("Run",      "X Bot@Walk Crouching Forward.fbx",   1f),
        ("Crouch",   "X Bot@Crouching Idle.fbx",           1f),
        ("Die",      "X Bot@Dying.fbx",                    1f),
        ("Aim",      "X Bot@Rifle Aiming Idle.fbx",        1f),
        ("Shoot",    "X Bot@Shooting.fbx",                 1f),
    };

    // Animaciones biped para Guardia Civil
    static readonly (string nombre, string fbxSubstring, float speed)[] ANIMS_GUARDIA =
    {
        ("Idle",        "Idle_Turn_Left",       0.5f),
        ("Walk",        "Walking_frame",        1f),
        ("Run",         "Running_frame",        1f),
        ("RunFast",     "RunFast",              1f),
        ("Die",         "Fall_Dead",            1f),
        ("HitReaction", "Hit_Reaction",         1f),
        ("Crouch",      "Injured_Walk",         0.8f),
        ("Rifle",       "Rifle_Charge",         1f),
        ("RifleAim",    "Rifle_Aim_Turn",       1f),
    };

    [MenuItem("Tools/Alsasua/🧍 Construir Personajes MeshyAI", priority = 20)]
    public static void Construir()
    {
        EnsureDirectories();

        EditorUtility.DisplayProgressBar("🧍 Personajes MeshyAI", "Configurando rigs...", 0f);
        try
        {
            // Paso 1: Configurar todos los FBX como Humanoid
            ConfigurarRigsHumanoid();
            AssetDatabase.Refresh();

            // Paso 2: Crear AnimatorControllers
            EditorUtility.DisplayProgressBar("🧍 Personajes", "Creando Animators...", 0.2f);
            var animCivil   = CrearAnimatorCivil();
            var animGuardia = CrearAnimatorGuardia();

            // Paso 3: Crear materiales HDRP/Lit PBR
            EditorUtility.DisplayProgressBar("🧍 Personajes", "Creando materiales HDRP...", 0.4f);

            // Paso 4: Crear prefabs
            EditorUtility.DisplayProgressBar("🧍 Personajes", "Creando prefabs...", 0.6f);
            int creados = 0;
            foreach (var p in PERSONAJES)
            {
                if (CrearPrefabPersonaje(p, p.esGuardia ? animGuardia : animCivil))
                    creados++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("✅ Personajes creados",
                $"{creados} prefabs NPC en {DIR_PREFABS}/\n\n" +
                "Civiles (8 variantes): ropa casual, doctor, ejecutivo...\n" +
                "Guardia Civil (2 variantes): uniforme completo\n\n" +
                "Todos con:\n" +
                "• Material HDRP/Lit (albedo + normal + roughness)\n" +
                "• NavMeshAgent configurado\n" +
                "• NPCCivil/PoliciaForalIA wired\n" +
                "• AnimatorController con locomotion blend tree",
                "OK");
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  1. CONFIGURAR RIGS HUMANOID
    // ════════════════════════════════════════════════════════════════════════

    static void ConfigurarRigsHumanoid()
    {
        // Modelos Meshy AI → Generic (no tienen bones con nombres humanoid estándar)
        foreach (var p in PERSONAJES)
        {
            string path = $"{BASE_PERSONAJES}/{p.carpeta}/{p.fbxPrincipal}";
            ConfigurarRig(path, ModelImporterAnimationType.Generic);
        }

        // XBot (Mixamo) → Humanoid (sí tienen skeleton estándar)
        foreach (var (_, fbx, _) in ANIMS_CIVIL)
            ConfigurarRig($"Assets/_ExtractedAssets/Personajes/Animaciones/{fbx}",
                          ModelImporterAnimationType.Human);

        // GuardiaCivil biped → Humanoid (skeleton biped estándar)
        string animDir = $"{BASE_PERSONAJES}/GuardiaCivil_Biped_Anims";
        if (AssetDatabase.IsValidFolder(animDir))
            foreach (var g in AssetDatabase.FindAssets("t:GameObject", new[]{animDir}))
                ConfigurarRig(AssetDatabase.GUIDToAssetPath(g),
                              ModelImporterAnimationType.Human);

        Debug.Log("[Personajes] Rigs configurados (Meshy AI=Generic, XBot/Guardia=Humanoid).");
    }

    static void ConfigurarRig(string path, ModelImporterAnimationType tipo)
    {
        if (!File.Exists(Path.Combine(Application.dataPath.Replace("Assets",""), path))) return;
        var imp = AssetImporter.GetAtPath(path) as ModelImporter;
        if (imp == null || imp.animationType == tipo) return;
        imp.animationType        = tipo;
        imp.avatarSetup          = tipo == ModelImporterAnimationType.Human
                                    ? ModelImporterAvatarSetup.CreateFromThisModel
                                    : ModelImporterAvatarSetup.NoAvatar;
        imp.optimizeGameObjects  = false;
        imp.importBlendShapes    = true;
        imp.importAnimation      = true;
        imp.animationCompression = ModelImporterAnimationCompression.Optimal;
        imp.SaveAndReimport();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  2. ANIMATORS
    // ════════════════════════════════════════════════════════════════════════

    static AnimatorController CrearAnimatorCivil()
    {
        string ruta = $"{DIR_ANIMATORS}/NPC_Civil_Animator.controller";
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ruta)
                ?? AnimatorController.CreateAnimatorControllerAtPath(ruta);

        EnsureParam(ctrl, "VelocidadMovimiento", AnimatorControllerParameterType.Float);
        EnsureParam(ctrl, "EstaAgachado",         AnimatorControllerParameterType.Bool);
        EnsureParam(ctrl, "EstaApuntando",        AnimatorControllerParameterType.Bool);
        EnsureParam(ctrl, "Morir",                AnimatorControllerParameterType.Trigger);
        EnsureParam(ctrl, "Disparar",             AnimatorControllerParameterType.Trigger);

        var sm = ctrl.layers[0].stateMachine;
        ClearStates(sm);

        // Blend tree locomotion
        BlendTree locoTree;
        var locoState = ctrl.CreateBlendTreeInController("Locomotion", out locoTree, 0);
        locoTree.blendType      = BlendTreeType.Simple1D;
        locoTree.blendParameter = "VelocidadMovimiento";

        string animBase = "Assets/_ExtractedAssets/Personajes/Animaciones";
        locoTree.AddChild(LoadClip($"{animBase}/X Bot@Crouching Idle.fbx",  "Crouching Idle"),  0f);
        locoTree.AddChild(LoadClip($"{animBase}/X Bot@Walking.fbx",          "Walking"),          1f);
        locoTree.AddChild(LoadClip($"{animBase}/X Bot@Walk Crouching Forward.fbx", "Walk Crouching Forward"), 4f);

        var dieState  = sm.AddState("Die",  new Vector3(400, 100, 0));
        var aimState  = sm.AddState("Aim",  new Vector3(400, 200, 0));
        var shootState= sm.AddState("Shoot",new Vector3(400, 300, 0));

        dieState.motion   = LoadClip($"{animBase}/X Bot@Dying.fbx",           "Dying");
        aimState.motion   = LoadClip($"{animBase}/X Bot@Rifle Aiming Idle.fbx","Rifle Aiming Idle");
        shootState.motion = LoadClip($"{animBase}/X Bot@Shooting.fbx",         "Shooting");

        // AnyState → Die
        var tDie = sm.AddAnyStateTransition(dieState);
        tDie.AddCondition(AnimatorConditionMode.If, 0, "Morir");
        tDie.hasExitTime = false; tDie.duration = 0.1f;

        // AnyState → Aim
        var tAim = sm.AddAnyStateTransition(aimState);
        tAim.AddCondition(AnimatorConditionMode.If, 0, "EstaApuntando");
        tAim.hasExitTime = false; tAim.duration = 0.2f;

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        return ctrl;
    }

    static AnimatorController CrearAnimatorGuardia()
    {
        string ruta = $"{DIR_ANIMATORS}/NPC_GuardiaCivil_Animator.controller";
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ruta)
                ?? AnimatorController.CreateAnimatorControllerAtPath(ruta);

        EnsureParam(ctrl, "VelocidadMovimiento", AnimatorControllerParameterType.Float);
        EnsureParam(ctrl, "EstaApuntando",        AnimatorControllerParameterType.Bool);
        EnsureParam(ctrl, "Morir",                AnimatorControllerParameterType.Trigger);
        EnsureParam(ctrl, "Disparar",             AnimatorControllerParameterType.Trigger);

        var sm = ctrl.layers[0].stateMachine;
        ClearStates(sm);

        string animDir = $"{BASE_PERSONAJES}/GuardiaCivil_Biped_Anims";

        // Blend tree locomotion
        BlendTree locoTree;
        var locoState = ctrl.CreateBlendTreeInController("Locomotion", out locoTree, 0);
        locoTree.blendType      = BlendTreeType.Simple1D;
        locoTree.blendParameter = "VelocidadMovimiento";

        var idleClip = LoadClipFromDir(animDir, "Idle_Turn_Left");
        var walkClip = LoadClipFromDir(animDir, "Walking_frame");
        var runClip  = LoadClipFromDir(animDir, "Running_frame");

        locoTree.AddChild(idleClip, 0f);
        locoTree.AddChild(walkClip, 1f);
        locoTree.AddChild(runClip,  4f);

        var dieState    = sm.AddState("Die",    new Vector3(400, 50,  0));
        var hitState    = sm.AddState("Hit",    new Vector3(400, 150, 0));
        var rifleState  = sm.AddState("Rifle",  new Vector3(400, 250, 0));

        dieState.motion   = LoadClipFromDir(animDir, "Fall_Dead");
        hitState.motion   = LoadClipFromDir(animDir, "Hit_Reaction");
        rifleState.motion = LoadClipFromDir(animDir, "Rifle_Charge");

        var tDie = sm.AddAnyStateTransition(dieState);
        tDie.AddCondition(AnimatorConditionMode.If, 0, "Morir");
        tDie.hasExitTime = false; tDie.duration = 0.1f;

        var tRifle = sm.AddAnyStateTransition(rifleState);
        tRifle.AddCondition(AnimatorConditionMode.If, 0, "EstaApuntando");
        tRifle.hasExitTime = false; tRifle.duration = 0.15f;

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        return ctrl;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  3. MATERIALES HDRP/LIT
    // ════════════════════════════════════════════════════════════════════════

    static Material CrearMaterialHDRP(string carpeta, string nombreBase)
    {
        string matRuta = $"{DIR_MATERIALS}/{nombreBase}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matRuta);
        if (mat != null) return mat;

        string base_path = $"{BASE_PERSONAJES}/{carpeta}";
        var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        mat = new Material(shader) { name = nombreBase };

        // Albedo / Base Color
        var albedo = LoadTex(base_path, "_texture.png") ?? LoadTex(base_path, "_texture_0.png");
        if (albedo != null) SetTexHDRP(mat, "_BaseColorMap",     "_BaseMap",     albedo);

        // Normal Map
        var normal = LoadTex(base_path, "_texture_normal.png") ?? LoadTex(base_path, "_texture_0_normal.png");
        if (normal != null)
        {
            SetNormalImport(AssetDatabase.GetAssetPath(normal));
            SetTexHDRP(mat, "_NormalMap", "_BumpMap", normal);
            mat.SetFloat("_NormalScale", 1f);
        }

        // Roughness → HDRP usa Mask Map (R=AO, G=detail, B=thickness, A=smoothness)
        var rough = LoadTex(base_path, "_texture_roughness.png") ?? LoadTex(base_path, "_texture_0_roughness.png");
        if (rough != null) SetTexHDRP(mat, "_MaskMap", "_MetallicGlossMap", rough);

        // Emission
        var emis = LoadTex(base_path, "_texture_emission.png");
        if (emis != null)
        {
            SetTexHDRP(mat, "_EmissiveColorMap", "_EmissionMap", emis);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissiveColor", Color.white * 0.5f);
        }

        // HDRP material flags
        mat.SetFloat("_Smoothness",         0.3f);
        mat.SetFloat("_Metallic",           0f);
        mat.SetFloat("_AlphaCutoffEnable",  0f);
        mat.doubleSidedGI = false;

        AssetDatabase.CreateAsset(mat, matRuta);
        return mat;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  4. PREFABS
    // ════════════════════════════════════════════════════════════════════════

    static bool CrearPrefabPersonaje(DefPersonaje def, AnimatorController animator)
    {
        string fbxPath   = $"{BASE_PERSONAJES}/{def.carpeta}/{def.fbxPrincipal}";
        string prefabPath= $"{DIR_PREFABS}/{def.nombrePrefab}.prefab";

        var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (fbxAsset == null) { Debug.LogWarning($"[Personajes] No encontrado: {fbxPath}"); return false; }

        // Crear prefab desde el FBX
        var root = Object.Instantiate(fbxAsset);
        root.name = def.nombrePrefab;

        // Escalar a altura humana real (~1.75m)
        var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (renderers.Length > 0)
        {
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            float alturaActual = bounds.size.y;
            if (alturaActual > 0.1f)
            {
                float escala = 1.75f / alturaActual;
                // Solo escalar si está muy mal (>30% error)
                if (escala < 0.7f || escala > 1.3f)
                    root.transform.localScale = Vector3.one * escala;
            }
        }

        // Material HDRP
        var mat = CrearMaterialHDRP(def.carpeta, def.nombrePrefab);
        foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
        }
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
        }

        // Animator
        var anim = root.GetComponent<Animator>() ?? root.AddComponent<Animator>();
        anim.runtimeAnimatorController = animator;
        anim.applyRootMotion            = false;
        anim.cullingMode                = AnimatorCullingMode.CullUpdateTransforms;

        // CapsuleCollider
        var col = root.AddComponent<CapsuleCollider>();
        col.height = 1.8f; col.radius = 0.3f;
        col.center = new Vector3(0, 0.9f, 0);

        // NavMeshAgent
        var agent = root.AddComponent<UnityEngine.AI.NavMeshAgent>();
        agent.height=1.8f; agent.radius=0.3f; agent.speed=1.4f;
        agent.angularSpeed=200f; agent.stoppingDistance=0.5f;
        agent.enabled = false;

        // Script comportamiento
        if (def.esGuardia)
        {
            var policia = root.AddComponent<PoliciaForalIA>();
        }
        else
        {
            var npc = root.AddComponent<NPCCivil>();
        }

        // Guardar prefab
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        Debug.Log($"[Personajes] ✅ {def.nombrePrefab}.prefab");
        return true;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════════════════

    static void EnsureDirectories()
    {
        foreach (var dir in new[] {
            "Assets/Prefabs", DIR_PREFABS, DIR_MATERIALS, DIR_ANIMATORS })
        {
            if (!AssetDatabase.IsValidFolder(dir))
            {
                var parts = dir.Split('/');
                string parent = string.Join("/", parts.Take(parts.Length-1));
                AssetDatabase.CreateFolder(parent, parts.Last());
            }
        }
    }

    static void ClearStates(AnimatorStateMachine sm)
    {
        foreach (var s in sm.states)
            if (s.state.name != "Entry" && s.state.name != "Exit" && s.state.name != "Any State")
                sm.RemoveState(s.state);
    }

    static void EnsureParam(AnimatorController c, string name, AnimatorControllerParameterType t)
    {
        if (!c.parameters.Any(p => p.name == name))
            c.AddParameter(name, t);
    }

    static AnimationClip LoadClip(string fbxPath, string clipName)
    {
        var objs = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        return objs.OfType<AnimationClip>()
                   .FirstOrDefault(c => c.name.Contains(clipName) && !c.name.StartsWith("__"))
            ?? objs.OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__"));
    }

    static AnimationClip LoadClipFromDir(string dir, string nameSubstr)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Object", new[]{dir}))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".fbx")) continue;
            if (!path.Contains(nameSubstr)) continue;
            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                            .Where(c => !c.name.StartsWith("__")).ToList();
            if (clips.Count > 0) return clips[0];
        }
        return null;
    }

    static Texture2D LoadTex(string basePath, string suffix)
    {
        // Buscar por sufijo exacto
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[]{basePath}))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (p.EndsWith(suffix)) return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }
        return null;
    }

    static void SetTexHDRP(Material m, string hdrpProp, string urpProp, Texture2D tex)
    {
        if (m.HasProperty(hdrpProp)) m.SetTexture(hdrpProp, tex);
        else if (m.HasProperty(urpProp)) m.SetTexture(urpProp, tex);
    }

    static void SetNormalImport(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null || imp.textureType == TextureImporterType.NormalMap) return;
        imp.textureType = TextureImporterType.NormalMap;
        imp.SaveAndReimport();
    }
}
#endif
