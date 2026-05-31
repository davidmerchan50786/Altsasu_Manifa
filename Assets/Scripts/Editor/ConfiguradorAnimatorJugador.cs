// Assets/Scripts/Editor/ConfiguradorAnimatorJugador.cs
// Crea y conecta un AnimatorController completo para el jugador GTA
// con todas las animaciones disponibles en el proyecto.
// Menú: Altsasu GTA → MAESTRO → Crear Animator Jugador

using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.IO;

public static class ConfiguradorAnimatorJugador
{
    const string CTRL_PATH  = "Assets/Animations/Player/GTA_Player.controller";
    const string ANIM_DIR   = "Assets/Animations";

    // Rutas de clips existentes
    const string CLIP_IDLE       = "Assets/#Xtra/Locomotion Setup/Locomotion/Animations/DefaultAvatar@Idle_Neutral.fbx";
    const string CLIP_WALK       = "Assets/#Xtra/Locomotion Setup/Locomotion/Animations/DefaultAvatar@WalkForward_NtrlFaceFwd.fbx";
    const string CLIP_RUN        = "Assets/#Xtra/Locomotion Setup/Locomotion/Animations/DefaultAvatar@RunForward_NtrlFaceFwd.fbx";
    const string CLIP_JUMP       = "Assets/#Xtra/Locomotion Setup/Locomotion/Animations/Animations/Jump.fbx";
    const string CLIP_CROUCH     = "Assets/#Xtra/Standard Assets/Characters/ThirdPersonCharacter/Animation/HumanoidCrouch.fbx";
    const string CLIP_JUMP_FALL  = "Assets/#Xtra/Standard Assets/Characters/ThirdPersonCharacter/Animation/HumanoidJumpAndFall.fbx";
    const string CLIP_IDLE_ARMED = "Assets/#Xtra/Locomotion Setup/Locomotion/Animations/Animations/Gta Style/Idle1armed.fbx";
    const string CLIP_RELOAD     = "Assets/#Xtra/Locomotion Setup/Locomotion/Animations/Animations/Gta Style/Rifle Reload.fbx";
    const string CLIP_SHOOT      = "Assets/#Xtra/Locomotion Setup/Locomotion/Animations/Animations/Gta Style/Shootgun.fbx";
    const string CLIP_WALK_XBOT  = "Assets/Animations/XBot/X Bot@Walking.fbx";
    const string CLIP_RUN_CROUCH = "Assets/Animations/XBot/X Bot@Walk Crouching Forward.fbx";
    const string CLIP_AIM_IDLE   = "Assets/Animations/XBot/X Bot@Rifle Aiming Idle.fbx";
    const string CLIP_SHOOTING   = "Assets/Animations/XBot/X Bot@Shooting.fbx";
    const string CLIP_DYING      = "Assets/Animations/XBot/X Bot@Dying.fbx";
    const string CLIP_NPC_SCARED = "Assets/Animations/NPC/ScaredNPC_Anim.FBX";
    const string CLIP_PISTOL_AIM = "Assets/Animations/Pistol/PistolAim.FBX";
    const string CLIP_PISTOL_FIRE= "Assets/Animations/Pistol/PistolFire.FBX";
    const string CLIP_PISTOL_IDLE= "Assets/Animations/Pistol/PistolIdle.FBX";

    // MENÚ-LEGACY: [MenuItem("Altsasu GTA/MAESTRO/Crear Animator Jugador", false, 26)]
    public static AnimatorController CrearAnimatorController()
    {
        // Crear carpeta
        if (!AssetDatabase.IsValidFolder("Assets/Animations/Player"))
        {
            AssetDatabase.CreateFolder("Assets/Animations", "Player");
            AssetDatabase.Refresh();
        }

        // Si ya existe, devolverlo
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(CTRL_PATH);
        if (existing != null)
        {
            Debug.Log("[Animator] AnimatorController ya existe: " + CTRL_PATH);
            AsignarAJugador(existing);
            return existing;
        }

        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(CTRL_PATH);

        // ── Parámetros ────────────────────────────────────────────────────
        ctrl.AddParameter("Speed",     AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Direction", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Forward",   AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Turn",      AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Jump",      AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("Crouch",    AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("OnGround",  AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("Armed",     AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("Shoot",     AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Reload",    AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Die",       AnimatorControllerParameterType.Trigger);

        var root = ctrl.layers[0].stateMachine;

        // ── Estados ───────────────────────────────────────────────────────
        var stIdle    = CrearEstado(root, "Idle",        CLIP_IDLE,       new Vector3(0,   0));
        var stWalk    = CrearEstado(root, "Walk",        CLIP_WALK_XBOT ?? CLIP_WALK, new Vector3(200, 0));
        var stRun     = CrearEstado(root, "Run",         CLIP_RUN,        new Vector3(400, 0));
        var stJump    = CrearEstado(root, "Jump",        CLIP_JUMP,       new Vector3(200,-120));
        var stFall    = CrearEstado(root, "Fall",        CLIP_JUMP_FALL,  new Vector3(200,-240));
        var stCrouch  = CrearEstado(root, "Crouch",      CLIP_CROUCH,     new Vector3(0,  -120));
        var stAimIdle = CrearEstado(root, "AimIdle",     CLIP_AIM_IDLE ?? CLIP_IDLE_ARMED, new Vector3(0, 120));
        var stShoot   = CrearEstado(root, "Shoot",       CLIP_SHOOTING ?? CLIP_SHOOT, new Vector3(200,120));
        var stReload  = CrearEstado(root, "Reload",      CLIP_RELOAD,     new Vector3(400, 120));
        var stDie     = CrearEstado(root, "Die",         CLIP_DYING,      new Vector3(600, 0));

        // Estado por defecto
        root.defaultState = stIdle;

        // ── Transiciones ──────────────────────────────────────────────────
        // Idle ↔ Walk
        AddTrans(stIdle, stWalk,   "Speed",    0.1f, AnimatorConditionMode.Greater, 0.1f);
        AddTrans(stWalk, stIdle,   "Speed",    0.1f, AnimatorConditionMode.Less,    0.05f);
        // Walk → Run
        AddTrans(stWalk, stRun,    "Speed",    0.1f, AnimatorConditionMode.Greater, 0.8f);
        AddTrans(stRun,  stWalk,   "Speed",    0.1f, AnimatorConditionMode.Less,    0.7f);
        // Jump
        AddTransBool(stIdle,   stJump, "Jump", true,  0.05f);
        AddTransBool(stWalk,   stJump, "Jump", true,  0.05f);
        AddTransBool(stRun,    stJump, "Jump", true,  0.05f);
        AddTransBool(stJump,   stFall, "OnGround", false, 0.1f);
        AddTransBool(stFall,   stIdle, "OnGround", true,  0.15f);
        // Crouch
        AddTransBool(stIdle,   stCrouch, "Crouch", true,  0.1f);
        AddTransBool(stCrouch, stIdle,   "Crouch", false, 0.1f);
        // Armed / Shoot
        AddTransBool(stIdle,    stAimIdle, "Armed", true,  0.1f);
        AddTransBool(stAimIdle, stIdle,    "Armed", false, 0.1f);
        AddTransTrigger(stAimIdle, stShoot,  "Shoot");
        AddTransTrigger(stShoot,   stAimIdle,"Shoot");  // loop
        AddTransTrigger(stAimIdle, stReload, "Reload");
        AddTransTrigger(stReload,  stAimIdle,"Reload");
        // Die (desde cualquier estado)
        foreach (var st in new[]{ stIdle, stWalk, stRun, stAimIdle, stShoot })
            AddTransTrigger(st, stDie, "Die");

        AssetDatabase.SaveAssets();
        Debug.Log("[Animator] ✓ AnimatorController creado: " + CTRL_PATH);

        AsignarAJugador(ctrl);
        return ctrl;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    static AnimatorState CrearEstado(AnimatorStateMachine sm, string nombre, string clipPath, Vector3 pos)
    {
        var state = sm.AddState(nombre, pos);
        var clip  = CargarPrimerClip(clipPath);
        if (clip != null) state.motion = clip;
        else Debug.LogWarning($"[Animator] Clip no encontrado: {clipPath}");
        return state;
    }

    static AnimationClip CargarPrimerClip(string path)
    {
        if (path == null) return null;
        // Intentar cargar directamente
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip != null) return clip;
        // Los FBX pueden tener múltiples clips — cargar el primero
        var assets = AssetDatabase.LoadAllAssetsAtPath(path);
        foreach (var a in assets)
            if (a is AnimationClip ac && !ac.name.StartsWith("__")) return ac;
        return null;
    }

    static void AddTrans(AnimatorState from, AnimatorState to,
        string param, float duration, AnimatorConditionMode mode, float threshold)
    {
        var t = from.AddTransition(to);
        t.duration = duration;
        t.hasExitTime = false;
        t.AddCondition(mode, threshold, param);
    }

    static void AddTransBool(AnimatorState from, AnimatorState to,
        string param, bool value, float duration)
    {
        var t = from.AddTransition(to);
        t.duration = duration;
        t.hasExitTime = false;
        t.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, param);
    }

    static void AddTransTrigger(AnimatorState from, AnimatorState to, string param)
    {
        var t = from.AddTransition(to);
        t.duration = 0.1f;
        t.hasExitTime = false;
        t.AddCondition(AnimatorConditionMode.If, 0, param);
    }

    // ── Asignar al jugador en escena y al prefab ────────────────────────────

    public static void AsignarAJugador(AnimatorController ctrl)
    {
        // Asignar a jugador en escena
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            var anim = player.GetComponent<Animator>();
            if (anim != null) { anim.runtimeAnimatorController = ctrl; EditorUtility.SetDirty(player); }
        }

        // Asignar al prefab si existe
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GTA/Jugador_GTA.prefab");
        if (prefab != null)
        {
            var anim = prefab.GetComponent<Animator>();
            if (anim != null)
            {
                anim.runtimeAnimatorController = ctrl;
                EditorUtility.SetDirty(prefab);
                AssetDatabase.SaveAssets();
            }
        }
        Debug.Log("[Animator] ✓ Controller asignado al jugador.");
    }
}
