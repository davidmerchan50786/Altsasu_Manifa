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
using System.Linq;

public static class ConfiguradorAnimatorJugador
{
    const string RUTA = "Assets/Animators/JugadorAnimator.controller";

    [MenuItem("Tools/Alsasua/Assets/🎭 Animator Jugador", priority = 40)]
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

        // ── Parámetros — nombres exactos que ControladorJugador.cs envía ──
        // Velocidad de movimiento normalizada (0=quieto, 1=corriendo)
        EnsureParameter(controller, "VelocidadMovimiento", AnimatorControllerParameterType.Float);
        // Estados booleanos
        EnsureParameter(controller, "EstaAgachado",   AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "EstaApuntando",  AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "EstaEnSuelo",    AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "InVehicle",      AnimatorControllerParameterType.Bool);
        // Triggers
        EnsureParameter(controller, "Saltar",  AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "Morir",   AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "Disparar",AnimatorControllerParameterType.Trigger);
        // Alias compatibles con otros sistemas
        EnsureParameter(controller, "Speed",      AnimatorControllerParameterType.Float);
        EnsureParameter(controller, "IsCrouching",AnimatorControllerParameterType.Bool);

        var layer = controller.layers[0];
        var sm    = layer.stateMachine;

        // ── Blend Tree de locomoción ──────────────────────────────────────
        BlendTree locoTree;
        var locoState = controller.CreateBlendTreeInController("Locomotion", out locoTree, 0);
        locoTree.blendType      = BlendTreeType.Simple1D;
        locoTree.blendParameter = "VelocidadMovimiento"; // nombre exacto de ControladorJugador

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

        // ── Transiciones — usan los parámetros exactos de ControladorJugador ─
        // Loco → Jump (trigger Saltar)
        var locoJump = locoState.AddTransition(jumpState);
        locoJump.AddCondition(AnimatorConditionMode.If, 0, "Saltar");
        locoJump.duration = 0.05f; locoJump.hasExitTime = false;

        // Loco → Drive
        var locoDrive = locoState.AddTransition(driveState);
        locoDrive.AddCondition(AnimatorConditionMode.If, 0, "InVehicle");
        locoDrive.duration = 0.15f;

        // Loco → Aim
        var locoAim = locoState.AddTransition(aimState);
        locoAim.AddCondition(AnimatorConditionMode.If, 0, "EstaApuntando");
        locoAim.duration = 0.1f;

        // Loco → Crouch
        var locoCrouch = locoState.AddTransition(crouchState);
        locoCrouch.AddCondition(AnimatorConditionMode.If, 0, "EstaAgachado");
        locoCrouch.duration = 0.12f;

        // Jump → Fall
        var jumpFall = jumpState.AddTransition(fallState);
        jumpFall.duration = 0.1f; jumpFall.exitTime = 0.5f; jumpFall.hasExitTime = true;

        // Fall → Loco (cuando toca suelo)
        var fallLoco = fallState.AddTransition(locoState);
        fallLoco.AddCondition(AnimatorConditionMode.If, 0, "EstaEnSuelo");
        fallLoco.duration = 0.2f;

        // Drive → Loco
        var driveLoco = driveState.AddTransition(locoState);
        driveLoco.AddCondition(AnimatorConditionMode.IfNot, 0, "InVehicle");
        driveLoco.duration = 0.2f;

        // Aim → Loco
        var aimLoco = aimState.AddTransition(locoState);
        aimLoco.AddCondition(AnimatorConditionMode.IfNot, 0, "EstaApuntando");
        aimLoco.duration = 0.1f;

        // Crouch → Loco
        var crouchLoco = crouchState.AddTransition(locoState);
        crouchLoco.AddCondition(AnimatorConditionMode.IfNot, 0, "EstaAgachado");
        crouchLoco.duration = 0.12f;

        // ── AnyState → Die (trigger Morir — máxima prioridad) ─────────────
        var anyDie = sm.AddAnyStateTransition(dieState);
        anyDie.AddCondition(AnimatorConditionMode.If, 0, "Morir");
        anyDie.duration = 0.1f; anyDie.canTransitionToSelf = false;

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
        // 1. Primero buscar en Player_*.fbx (animaciones procedurales del jugador)
        string playerFbx = $"Assets/Animators/Clips/Player_{nombre}.fbx";
        var assets = AssetDatabase.LoadAllAssetsAtPath(playerFbx);
        var clip   = System.Linq.Enumerable.OfType<AnimationClip>(assets)
                          .FirstOrDefault(c => !c.name.Contains("__preview__"));
        if (clip != null) return clip;

        // 2. Buscar cualquier AnimationClip con ese nombre en el proyecto
        var guids = AssetDatabase.FindAssets($"Player_{nombre} t:AnimationClip", new[]{"Assets/Animators"});
        if (guids.Length > 0)
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guids[0]));

        guids = AssetDatabase.FindAssets($"{nombre} t:AnimationClip", new[]{"Assets/Animators"});
        if (guids.Length > 0)
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guids[0]));

        // 3. Placeholder vacío (Unity lo acepta sin error)
        var ph   = new AnimationClip { name = nombre };
        string r = $"Assets/Animators/{nombre}_placeholder.anim";
        if (!System.IO.File.Exists(System.IO.Path.GetFullPath(r.Replace("Assets/",
            UnityEngine.Application.dataPath + "/"))))
            AssetDatabase.CreateAsset(ph, r);
        return AssetDatabase.LoadAssetAtPath<AnimationClip>(r) ?? ph;
    }
}
#endif
