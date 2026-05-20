// Assets/Scripts/Editor/AsignadorAnimacionesJugador.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ASIGNADOR ANIMACIONES JUGADOR — conecta los FBX procedurales al controller
//  Tools → Alsasua → 🎭 Asignar Animaciones Jugador (Player_*.fbx)
//
//  Lee los 7 FBX de Player_Walk/Run/Idle/Jump/Crouch/Aim/Die
//  y los asigna al JugadorAnimator.controller existente,
//  también actualiza el prefab Jugador_Altsasua.prefab
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.IO;
using System.Linq;
using System.Collections.Generic;

public static class AsignadorAnimacionesJugador
{
    const string CLIPS_DIR  = "Assets/Animators/Clips";
    const string CTRL_PATH  = "Assets/Animators/JugadorAnimator.controller";
    const string PREFAB_JUG = "Assets/Prefabs/Personajes/Jugador_Altsasua.prefab";

    [MenuItem("Tools/Alsasua/🎭 Asignar Animaciones Jugador (Player_*.fbx)", priority = 17)]
    public static void Asignar()
    {
        // 1. Cargar clips Player_* de los FBX procedurales
        var clips = CargarClips();
        Debug.Log($"[AnimJugador] Clips cargados: {clips.Count}");

        if (clips.Count == 0)
        {
            EditorUtility.DisplayDialog("Sin clips",
                "No se encontraron Player_*.fbx en Assets/Animators/Clips/\n" +
                "Ejecuta primero: Blender → GenerarAnimaciones.py", "OK");
            return;
        }

        // 2. Actualizar el AnimatorController
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(CTRL_PATH);
        if (ctrl == null)
        {
            // Crear uno desde cero si no existe
            ConfiguradorAnimatorJugador.Configurar();
            ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(CTRL_PATH);
        }

        if (ctrl != null)
        {
            ActualizarController(ctrl, clips);
            EditorUtility.SetDirty(ctrl);
        }

        // 3. Asignar al prefab del jugador
        AsignarAlPrefab(ctrl);

        // 4. Asignar a jugador en escena activa
        AsignarEnEscena(ctrl);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string resumen = $"✅ Animaciones del jugador asignadas:\n\n" +
            string.Join("\n", clips.Select(kv => $"  • {kv.Key}: {kv.Value.name} ({kv.Value.length:F2}s)"));
        Debug.Log("[AnimJugador] " + resumen);
        EditorUtility.DisplayDialog("✅ Animaciones del jugador", resumen, "OK");
    }

    static Dictionary<string, AnimationClip> CargarClips()
    {
        var result = new Dictionary<string, AnimationClip>();
        var mapa = new Dictionary<string, string>
        {
            ["Player_Idle"]   = "Idle",
            ["Player_Walk"]   = "Walk",
            ["Player_Run"]    = "Run",
            ["Player_Jump"]   = "Jump",
            ["Player_Crouch"] = "Crouch",
            ["Player_Aim"]    = "Aim",
            ["Player_Die"]    = "Die",
        };

        foreach (var kv in mapa)
        {
            string fbxPath = $"{CLIPS_DIR}/{kv.Key}.fbx";
            var assets    = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            var clip      = assets.OfType<AnimationClip>()
                                  .FirstOrDefault(c => !c.name.Contains("__preview__"));
            if (clip != null)
            {
                result[kv.Value] = clip;
                Debug.Log($"[AnimJugador] ✅ {kv.Value}: {clip.length:F2}s ({(int)(clip.length*clip.frameRate)} frames)");
            }
            else
                Debug.LogWarning($"[AnimJugador] Sin clip en: {fbxPath}");
        }
        return result;
    }

    static void ActualizarController(AnimatorController ctrl,
                                      Dictionary<string, AnimationClip> clips)
    {
        var sm = ctrl.layers[0].stateMachine;

        // Recorrer todos los estados y asignar el clip correcto
        foreach (var cs in sm.states)
        {
            string stateName = cs.state.name;
            if (clips.TryGetValue(stateName, out var clip))
            {
                cs.state.motion = clip;
                Debug.Log($"[AnimJugador] Estado '{stateName}' → {clip.name}");
            }
        }

        // Si hay un BlendTree de Locomotion, actualizar sus motions
        foreach (var cs in sm.states)
        {
            if (cs.state.motion is BlendTree bt)
            {
                var children = bt.children;
                for (int i = 0; i < children.Length; i++)
                {
                    var c = children[i];
                    if (c.threshold < 0.5f && clips.TryGetValue("Idle", out var idle))
                        c.motion = idle;
                    else if (c.threshold < 2.5f && clips.TryGetValue("Walk", out var walk))
                        c.motion = walk;
                    else if (clips.TryGetValue("Run", out var run))
                        c.motion = run;
                    children[i] = c;
                }
                bt.children = children;
            }
        }
    }

    static void AsignarAlPrefab(RuntimeAnimatorController ctrl)
    {
        if (ctrl == null) return;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_JUG);
        if (prefab == null) return;

        var anim = prefab.GetComponentInChildren<Animator>();
        if (anim == null) anim = prefab.AddComponent<Animator>();

        if (anim.runtimeAnimatorController != ctrl)
        {
            anim.runtimeAnimatorController = ctrl;
            PrefabUtility.SavePrefabAsset(prefab);
            Debug.Log($"[AnimJugador] Controller asignado al prefab: {prefab.name}");
        }
    }

    static void AsignarEnEscena(RuntimeAnimatorController ctrl)
    {
        if (ctrl == null) return;
        var jugGO = GameObject.FindGameObjectWithTag("Player");
        if (jugGO == null) return;

        var anim = jugGO.GetComponent<Animator>();
        if (anim == null) anim = jugGO.AddComponent<Animator>();
        anim.runtimeAnimatorController = ctrl;
        Debug.Log("[AnimJugador] Controller asignado al jugador en escena");
    }
}
#endif
