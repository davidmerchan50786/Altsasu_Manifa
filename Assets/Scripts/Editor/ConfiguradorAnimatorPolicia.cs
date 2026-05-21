// Assets/Scripts/Editor/ConfiguradorAnimatorPolicia.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONFIGURADOR ANIMATOR POLICÍA FORAL — usa las animaciones reales de MeshyAI
//  Tools → Alsasua → 🎭 Configurar Animator Policía
//
//  Crea PoliciaForalAnimator.controller conectando los 17 clips FBX extraídos:
//    GC_Idle, GC_Walk, GC_Run, GC_Aim, GC_Hit, GC_Die, GC_Fall,
//    GC_WalkGun, GC_WalkInjured, GC_Arise, GC_Kick, etc.
//
//  También actualiza JugadorAnimator.controller para usar Civil_Walk
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public static class ConfiguradorAnimatorPolicia
{
    const string CLIPS_DIR     = "Assets/Animators/Clips";
    const string POLICIA_CTRL  = "Assets/Animators/PoliciaForalAnimator.controller";
    const string JUGADOR_CTRL  = "Assets/Animators/JugadorAnimator.controller";

    [MenuItem("Tools/Alsasua/Assets/🎭 Animator Policia (MeshyAI)", priority = 16)]
    public static void Configurar()
    {
        // ── 1. Cargar clips reales ──────────────────────────────────────────
        var clips = CargarClipsReales();
        Debug.Log($"[AnimPolicia] Clips encontrados: {clips.Count}");

        // ── 2. Crear/actualizar controller de policía ──────────────────────
        CrearControllerPolicia(clips);

        // ── 3. Actualizar controller del jugador con Civil_Walk ─────────────
        ActualizarControllerJugador(clips);

        // ── 4. Asignar a prefab del GC en escena ──────────────────────────
        AsignarAGuardiaCivil();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("✅ Animator configurado",
            $"PoliciaForalAnimator.controller creado con {clips.Count} clips reales\n\n" +
            string.Join("\n", clips.Keys.Select(k => $"• {k}")),
            "OK");
    }

    static Dictionary<string, AnimationClip> CargarClipsReales()
    {
        var result = new Dictionary<string, AnimationClip>();

        // Mapeo nombre FBX → nombre lógico
        var mapa = new Dictionary<string, string>
        {
            ["GC_Walk"]         = "Walk",
            ["GC_Run"]          = "Run",
            ["GC_Idle"]         = "Idle",
            ["GC_Die"]          = "Die",
            ["GC_Hit"]          = "Hit",
            ["GC_Fall"]         = "Fall",
            ["GC_Aim"]          = "Aim",
            ["GC_WalkGun"]      = "WalkGun",
            ["GC_WalkInjured"]  = "WalkInjured",
            ["GC_Arise"]        = "Arise",
            ["GC_Kick"]         = "Kick",
            ["GC_IdleTurnL"]    = "IdleTurnL",
            ["GC_IdleTurnR"]    = "IdleTurnR",
            ["GC_RunTurn"]      = "RunTurn",
            ["GC_WalkUnsteady"] = "WalkUnsteady",
            ["Civil_Walk"]      = "CivilWalk",
        };

        foreach (var kv in mapa)
        {
            string fbxPath = $"{CLIPS_DIR}/{kv.Key}.fbx";
            var allAssets  = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            var clip       = allAssets.OfType<AnimationClip>()
                                      .FirstOrDefault(c => !c.name.Contains("__preview__"));
            if (clip != null)
            {
                result[kv.Value] = clip;
                Debug.Log($"[AnimPolicia] ✅ {kv.Value}: {clip.name} ({clip.length:F2}s)");
            }
            else
                Debug.LogWarning($"[AnimPolicia] ⚠ Sin clip en: {fbxPath}");
        }

        return result;
    }

    static void CrearControllerPolicia(Dictionary<string, AnimationClip> clips)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(POLICIA_CTRL)
            ?? AnimatorController.CreateAnimatorControllerAtPath(POLICIA_CTRL);

        // Parámetros
        EnsureParam(controller, "Speed",      AnimatorControllerParameterType.Float);
        EnsureParam(controller, "IsChasing",  AnimatorControllerParameterType.Bool);
        EnsureParam(controller, "IsAiming",   AnimatorControllerParameterType.Bool);
        EnsureParam(controller, "IsDead",     AnimatorControllerParameterType.Bool);
        EnsureParam(controller, "GotHit",     AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;
        sm.anyStatePosition  = new Vector3(0, 0, 0);
        sm.entryPosition     = new Vector3(0, 100, 0);
        sm.exitPosition      = new Vector3(0, 200, 0);

        // Estados
        var idle   = AddState(sm, "Idle",       clips.GetValueOrDefault("Idle"),       new Vector3(250,  0, 0));
        var walk   = AddState(sm, "Walk",        clips.GetValueOrDefault("Walk"),       new Vector3(250, 100, 0));
        var run    = AddState(sm, "Run",         clips.GetValueOrDefault("Run"),        new Vector3(250, 200, 0));
        var aim    = AddState(sm, "Aim",         clips.GetValueOrDefault("Aim"),        new Vector3(500, 100, 0));
        var die    = AddState(sm, "Die",         clips.GetValueOrDefault("Die"),        new Vector3(500, 300, 0));
        var hit    = AddState(sm, "Hit",         clips.GetValueOrDefault("Hit"),        new Vector3(500, 200, 0));
        var fall   = AddState(sm, "Fall",        clips.GetValueOrDefault("Fall"),       new Vector3(750, 300, 0));

        sm.defaultState = idle;

        // Transiciones desde Idle
        Transition(idle, walk,  "Speed",     AnimatorConditionMode.Greater, 0.5f, 0.15f);
        Transition(idle, aim,   "IsAiming",  AnimatorConditionMode.If,      0,    0.10f);

        // Transiciones desde Walk
        Transition(walk, run,   "IsChasing", AnimatorConditionMode.If,      0,    0.10f);
        Transition(walk, idle,  "Speed",     AnimatorConditionMode.Less,    0.5f, 0.15f);
        Transition(walk, aim,   "IsAiming",  AnimatorConditionMode.If,      0,    0.10f);

        // Transiciones desde Run
        Transition(run,  walk,  "IsChasing", AnimatorConditionMode.IfNot,   0,    0.15f);
        Transition(run,  aim,   "IsAiming",  AnimatorConditionMode.If,      0,    0.10f);

        // Aim → Walk
        Transition(aim,  walk,  "IsAiming",  AnimatorConditionMode.IfNot,   0,    0.10f);

        // AnyState → Die (máxima prioridad)
        var anyDie = sm.AddAnyStateTransition(die);
        anyDie.AddCondition(AnimatorConditionMode.If, 0, "IsDead");
        anyDie.duration = 0.10f;

        // Die → Fall (tras acabar)
        var dieToFall = die.AddTransition(fall);
        dieToFall.hasExitTime = true; dieToFall.exitTime = 0.9f; dieToFall.duration = 0.1f;

        // AnyState → Hit (trigger)
        var anyHit = sm.AddAnyStateTransition(hit);
        anyHit.AddCondition(AnimatorConditionMode.If, 0, "GotHit");
        anyHit.duration = 0.05f;

        // Hit → Idle
        var hitToIdle = hit.AddTransition(idle);
        hitToIdle.hasExitTime = true; hitToIdle.exitTime = 0.8f; hitToIdle.duration = 0.15f;

        EditorUtility.SetDirty(controller);
        Debug.Log($"[AnimPolicia] ✅ PoliciaForalAnimator.controller creado");
    }

    static void ActualizarControllerJugador(Dictionary<string, AnimationClip> clips)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(JUGADOR_CTRL);
        if (controller == null) { Debug.LogWarning("[AnimPolicia] JugadorAnimator no encontrado"); return; }

        var sm = controller.layers[0].stateMachine;

        // Actualizar clips de Locomotion BlendTree si existe
        foreach (var state in sm.states)
        {
            if (state.state.name == "Idle" && clips.ContainsKey("CivilWalk"))
            {
                // El civil usa el mismo Walk que el GC por ahora
                if (state.state.motion == null || state.state.motion.name.Contains("placeholder"))
                    state.state.motion = clips["CivilWalk"];
            }
        }

        EditorUtility.SetDirty(controller);
        Debug.Log("[AnimPolicia] JugadorAnimator actualizado con Civil_Walk");
    }

    static void AsignarAGuardiaCivil()
    {
        // Buscar prefab del GC en escena o en Prefabs
        var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(POLICIA_CTRL);
        if (ctrl == null) return;

        // Prefabs de GC
        var gcGuids = AssetDatabase.FindAssets("t:Prefab GC", new[] { "Assets/Prefabs" });
        gcGuids = gcGuids.Concat(
            AssetDatabase.FindAssets("t:Prefab Guardia", new[] { "Assets/Prefabs" })).ToArray();

        foreach (var g in gcGuids)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
            if (prefab == null) continue;
            var anim = prefab.GetComponentInChildren<Animator>();
            if (anim != null && anim.runtimeAnimatorController == null)
            {
                anim.runtimeAnimatorController = ctrl;
                PrefabUtility.SavePrefabAsset(prefab);
                Debug.Log($"[AnimPolicia] Controller asignado a prefab: {prefab.name}");
            }
        }

        // También asignar en escena activa
        foreach (var gc in Object.FindObjectsByType<PoliciaForalIA>(FindObjectsSortMode.None))
        {
            var anim = gc.GetComponent<Animator>();
            if (anim != null && anim.runtimeAnimatorController == null)
                anim.runtimeAnimatorController = ctrl;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    static AnimatorState AddState(AnimatorStateMachine sm, string nombre,
                                   AnimationClip clip, Vector3 pos)
    {
        // Buscar estado existente
        foreach (var cs in sm.states)
            if (cs.state.name == nombre)
            {
                if (clip != null) cs.state.motion = clip;
                return cs.state;
            }

        var state = sm.AddState(nombre, pos);
        state.motion = clip;
        state.iKOnFeet = nombre != "Die" && nombre != "Fall";
        state.writeDefaultValues = true;
        return state;
    }

    static void Transition(AnimatorState from, AnimatorState to,
                            string param, AnimatorConditionMode mode,
                            float threshold, float duration)
    {
        var t = from.AddTransition(to);
        t.AddCondition(mode, threshold, param);
        t.duration = duration;
        t.hasExitTime = false;
        t.canTransitionToSelf = false;
    }

    static void EnsureParam(AnimatorController ctrl, string nombre,
                              AnimatorControllerParameterType tipo)
    {
        if (ctrl.parameters.Any(p => p.name == nombre)) return;
        ctrl.AddParameter(nombre, tipo);
    }
}
#endif
