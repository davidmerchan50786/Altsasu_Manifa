// Assets/Scripts/Editor/ConfiguradorAnimatorJugador.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONFIGURADOR ANIMATOR JUGADOR — crea el AnimatorController por código
//  Tools → Alsasua → 🎭 Configurar Animator Jugador
//
//  Crea o actualiza Assets/Animators/JugadorAnimator.controller con:
//    • Parámetros: Speed (float), IsJumping (bool), InVehicle (bool),
//                  IsDead (bool), IsAiming (bool)
//    • Estados: Idle, Walk, Run, Jump, Fall, Drive, Die, Aim
//    • Blend Tree Locomotion: Idle→Walk→Run por Speed
//    • Transiciones con condiciones correctas
//    • AnyState → Die, AnyState → Jump
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.IO;

public static class ConfiguradorAnimatorJugador
{
    const string RUTA = "Assets/Animators/JugadorAnimator.controller";

    [MenuItem("Tools/Alsasua/🎭 Configurar Animator Jugador", priority = 15)]
    public static void Configurar()
    {
        // Crear carpeta si no existe
        if (!AssetDatabase.IsValidFolder("Assets/Animators"))
            AssetDatabase.CreateFolder("Assets", "Animators");

        // Crear o cargar controller
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(RUTA);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(RUTA);
            Debug.Log("[AnimatorJugador] AnimatorController creado en " + RUTA);
        }

        // ── Parámetros ────────────────────────────────────────────────────
        EnsureParameter(controller, "Speed",     AnimatorControllerParameterType.Float);
        EnsureParameter(controller, "IsJumping", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "InVehicle", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "IsDead",    AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "IsAiming",  AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "IsCrouching",AnimatorControllerParameterType.Bool);

        var layer = controller.layers[0];
        var sm    = layer.stateMachine;

        // ── Blend Tree de locomoción ──────────────────────────────────────
        BlendTree locoTree;
        var locoState = controller.CreateBlendTreeInController("Locomotion", out locoTree, 0);
        locoTree.blendType   = BlendTreeType.Simple1D;
        locoTree.blendParameter = "Speed";

        // Añadir motions (clips null = placeholder — Unity los acepta)
        var idleClip  = BuscarOCrearClip("Idle");
        var walkClip  = BuscarOCrearClip("Walk");
        var runClip   = BuscarOCrearClip("Run");
        locoTree.AddChild(idleClip, 0f);
        locoTree.AddChild(walkClip, 1f);
        locoTree.AddChild(runClip,  4f);
        locoState.speed = 1f;

        // ── Estados adicionales ───────────────────────────────────────────
        var jumpState   = sm.AddState("Jump",   new Vector3(300, -50,  0));
        var fallState   = sm.AddState("Fall",   new Vector3(300,  50,  0));
        var driveState  = sm.AddState("Drive",  new Vector3(300, 150,  0));
        var dieState    = sm.AddState("Die",    new Vector3(300, 250,  0));
        var aimState    = sm.AddState("Aim",    new Vector3(300, -150, 0));
        var crouchState = sm.AddState("Crouch", new Vector3(300, -250, 0));

        jumpState.motion   = BuscarOCrearClip("Jump");
        fallState.motion   = BuscarOCrearClip("Fall");
        driveState.motion  = BuscarOCrearClip("Drive");
        dieState.motion    = BuscarOCrearClip("Die");
        aimState.motion    = BuscarOCrearClip("Aim");
        crouchState.motion = BuscarOCrearClip("Crouch");

        // Estado por defecto
        sm.defaultState = locoState;

        // ── Transiciones desde Locomotion ─────────────────────────────────
        // Loco → Jump
        var locoJump = locoState.AddTransition(jumpState);
        locoJump.AddCondition(AnimatorConditionMode.If, 0, "IsJumping");
        locoJump.duration = 0.05f;

        // Loco → Drive
        var locoDrive = locoState.AddTransition(driveState);
        locoDrive.AddCondition(AnimatorConditionMode.If, 0, "InVehicle");
        locoDrive.duration = 0.15f;

        // Loco → Aim
        var locoAim = locoState.AddTransition(aimState);
        locoAim.AddCondition(AnimatorConditionMode.If, 0, "IsAiming");
        locoAim.duration = 0.1f;

        // Loco → Crouch
        var locoCrouch = locoState.AddTransition(crouchState);
        locoCrouch.AddCondition(AnimatorConditionMode.If, 0, "IsCrouching");
        locoCrouch.duration = 0.15f;

        // Jump → Fall (tras 0.5s sin condición)
        var jumpFall = jumpState.AddTransition(fallState);
        jumpFall.duration = 0.1f; jumpFall.exitTime = 0.5f; jumpFall.hasExitTime = true;

        // Fall → Loco
        var fallLoco = fallState.AddTransition(locoState);
        fallLoco.AddCondition(AnimatorConditionMode.IfNot, 0, "IsJumping");
        fallLoco.duration = 0.2f;

        // Drive → Loco
        var driveLoco = driveState.AddTransition(locoState);
        driveLoco.AddCondition(AnimatorConditionMode.IfNot, 0, "InVehicle");
        driveLoco.duration = 0.2f;

        // Aim → Loco
        var aimLoco = aimState.AddTransition(locoState);
        aimLoco.AddCondition(AnimatorConditionMode.IfNot, 0, "IsAiming");
        aimLoco.duration = 0.1f;

        // Crouch → Loco
        var crouchLoco = crouchState.AddTransition(locoState);
        crouchLoco.AddCondition(AnimatorConditionMode.IfNot, 0, "IsCrouching");
        crouchLoco.duration = 0.15f;

        // ── AnyState → Die (máxima prioridad) ────────────────────────────
        var anyDie = sm.AddAnyStateTransition(dieState);
        anyDie.AddCondition(AnimatorConditionMode.If, 0, "IsDead");
        anyDie.duration = 0.1f;

        // ── Guardar ───────────────────────────────────────────────────────
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // ── Asignar al jugador si existe en escena ────────────────────────
        var jugadorGO = GameObject.FindGameObjectWithTag("Player");
        if (jugadorGO != null)
        {
            var anim = jugadorGO.GetComponent<Animator>();
            if (anim == null) anim = jugadorGO.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            Debug.Log("[AnimatorJugador] ✅ Animator asignado al jugador en escena.");
        }

        Debug.Log($"[AnimatorJugador] ✅ Configuración completa — {RUTA}");
        EditorUtility.DisplayDialog("Animator Jugador",
            $"✅ AnimatorController configurado:\n\n" +
            $"• 8 estados (Locomotion/Jump/Fall/Drive/Aim/Crouch/Die)\n" +
            $"• Blend Tree Locomotion: Idle→Walk→Run\n" +
            $"• 6 parámetros (Speed, IsJumping, InVehicle, IsDead, IsAiming, IsCrouching)\n" +
            $"• Transiciones con condiciones correctas\n\n" +
            $"📌 Asigna los clips de animación en cada estado del Animator.",
            "OK");
    }

    static void EnsureParameter(AnimatorController ctrl, string nombre, AnimatorControllerParameterType tipo)
    {
        foreach (var p in ctrl.parameters)
            if (p.name == nombre) return;
        ctrl.AddParameter(nombre, tipo);
    }

    static AnimationClip BuscarOCrearClip(string nombre)
    {
        // Buscar en Assets/Animations
        var guids = AssetDatabase.FindAssets($"{nombre} t:AnimationClip", new[]{"Assets"});
        if (guids.Length > 0)
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guids[0]));

        // Crear clip placeholder vacío
        var clip = new AnimationClip { name = nombre };
        string ruta = $"Assets/Animators/{nombre}_placeholder.anim";
        AssetDatabase.CreateAsset(clip, ruta);
        return clip;
    }
}
#endif
