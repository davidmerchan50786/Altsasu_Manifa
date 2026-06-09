// Assets/Scripts/SistemaAnimacionesRuntime.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ANIMACIONES RUNTIME — wiring de FBX clips a Animator Controllers
//
//  Los Animator Controllers del proyecto (NPC_Civil_Animator, NPC_GuardiaCivil,
//  JugadorAnimator) tienen estados definidos pero los clips referenciados por
//  GUID pueden estar vacíos o ser placeholders. Este sistema carga los FBX de
//  Assets/Animations/ (en Resources) y construye AnimatorOverrideControllers
//  en runtime para asignar los clips correctos a cada estado.
//
//  Mapping estado → clip FBX:
//
//  NPC_Civil_Animator:
//    "Locomotion" → Civil_Walk (blendtree por VelocidadMovimiento)
//    "Die"        → Civil_Merged (clip de muerte incluido)
//
//  NPC_GuardiaCivil_Animator:
//    "Locomotion" → GC_Walk / GC_Run (blendtree)
//    "Rifle"      → GC_WalkGun
//    "Die"        → GC_Die
//    "Hit"        → GC_Hit
//
//  JugadorAnimator:
//    "Locomotion 0" → Player_Walk / Player_Run
//    "Aim 4"        → Player_Aim
//    "Jump 4"       → Player_Jump
//    "Fall 5"       → Player_Die
//
//  Uso:
//    Este sistema se añade a cualquier GameObject de la escena. Al Start()
//    busca todos los Animator con esos controllers y aplica el override.
//    Los NPCs creados después pueden llamar
//    SistemaAnimacionesRuntime.AplicarOverride(animator, tipo) manualmente.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[DefaultExecutionOrder(150)]
public class SistemaAnimacionesRuntime : MonoBehaviour
{
    public static SistemaAnimacionesRuntime Instance { get; private set; }

    public enum TipoPersonaje { Civil, GuardiaCivil, Jugador }

    // ── Clips cargados ────────────────────────────────────────────────────
    // (nombre → clip; se cargan desde Resources/Animations/)
    readonly Dictionary<string, AnimationClip> _clips = new();

    // Override controllers cacheados por tipo (se crean una vez y se reusan)
    AnimatorOverrideController _overrideCivil;
    AnimatorOverrideController _overrideGC;
    AnimatorOverrideController _overrideJugador;

    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        CargarClips();
    }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(2f);   // esperar a que los NPCs existan
        AplicarATodosLosAnimators();
    }

    // ── Carga ─────────────────────────────────────────────────────────────

    void CargarClips()
    {
        // Resources.LoadAll carga TODOS los assets de tipo AnimationClip dentro
        // del FBX. Unity los expone como sub-assets.
        var todos = Resources.LoadAll<AnimationClip>("Animations");
        foreach (var c in todos)
            _clips[c.name.ToLower()] = c;

        AlsasuaLogger.Info("Animaciones", $"{_clips.Count} clips cargados desde Resources/Animations");
    }

    AnimationClip Clip(string nombre)
    {
        _clips.TryGetValue(nombre.ToLower(), out var c);
        return c;
    }

    // ── Construir override controllers ────────────────────────────────────

    AnimatorOverrideController BuildOverride(RuntimeAnimatorController base_,
        params (string estado, string fbxNombre)[] mapeo)
    {
        if (base_ == null) return null;
        var oc = new AnimatorOverrideController(base_);
        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        oc.GetOverrides(overrides);

        for (int i = 0; i < overrides.Count; i++)
        {
            var original = overrides[i].Key;
            if (original == null) continue;

            // Buscar si algún mapeo coincide con el nombre del estado/clip
            foreach (var (estado, fbxNombre) in mapeo)
            {
                if (original.name.ToLower().Contains(estado.ToLower()))
                {
                    var nuevoClip = Clip(fbxNombre);
                    if (nuevoClip != null)
                        overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(original, nuevoClip);
                    break;
                }
            }
        }
        oc.ApplyOverrides(overrides);
        return oc;
    }

    // ── Aplicar a todos los animators en escena ───────────────────────────

    void AplicarATodosLosAnimators()
    {
        int aplicados = 0;
        var animators = FindObjectsByType<Animator>(FindObjectsSortMode.None);

        foreach (var anim in animators)
        {
            if (anim.runtimeAnimatorController == null) continue;
            string nombre = anim.runtimeAnimatorController.name;

            TipoPersonaje? tipo = nombre switch
            {
                var n when n.Contains("Civil") && !n.Contains("Guardia") => TipoPersonaje.Civil,
                var n when n.Contains("GuardiaCivil") || n.Contains("Policia") => TipoPersonaje.GuardiaCivil,
                var n when n.Contains("Jugador") || n.Contains("Player") => TipoPersonaje.Jugador,
                _ => (TipoPersonaje?)null
            };

            if (tipo == null) continue;
            AplicarOverride(anim, tipo.Value);
            aplicados++;
        }

        AlsasuaLogger.Info("Animaciones", $"Override aplicado a {aplicados} animators");
    }

    // ── API pública ───────────────────────────────────────────────────────

    /// <summary>
    /// Aplica el AnimatorOverrideController correcto a un Animator.
    /// Llamar desde SistemaSpawnCiviles al activar cada NPC.
    /// </summary>
    public static void AplicarOverride(Animator anim, TipoPersonaje tipo)
    {
        if (Instance == null || anim == null) return;

        var oc = tipo switch
        {
            TipoPersonaje.Civil        => Instance.GetOverrideCivil(anim),
            TipoPersonaje.GuardiaCivil => Instance.GetOverrideGC(anim),
            TipoPersonaje.Jugador      => Instance.GetOverrideJugador(anim),
            _ => null
        };

        if (oc != null) anim.runtimeAnimatorController = oc;
    }

    AnimatorOverrideController GetOverrideCivil(Animator anim)
    {
        if (_overrideCivil != null) return _overrideCivil;
        _overrideCivil = BuildOverride(anim.runtimeAnimatorController,
            ("locomotion", "Civil_Walk__Civil_Walk"),
            ("die",        "Civil_Merged__Civil_Merged"),
            ("idle",       "Civil_Walk__Civil_Idle"));
        return _overrideCivil;
    }

    AnimatorOverrideController GetOverrideGC(Animator anim)
    {
        if (_overrideGC != null) return _overrideGC;
        _overrideGC = BuildOverride(anim.runtimeAnimatorController,
            ("locomotion", "GC_Walk__GC_Walk"),
            ("rifle",      "GC_WalkGun__GC_WalkGun"),
            ("die",        "GC_Die__GC_Die"),
            ("hit",        "GC_Hit__GC_Hit"),
            ("idle",       "GC_Idle__GC_Idle"),
            ("run",        "GC_Run__GC_Run"),
            ("aim",        "GC_Aim__GC_Aim"),
            ("arise",      "GC_Arise__GC_Arise"),
            ("fall",       "GC_Fall__GC_Fall"));
        return _overrideGC;
    }

    AnimatorOverrideController GetOverrideJugador(Animator anim)
    {
        if (_overrideJugador != null) return _overrideJugador;
        _overrideJugador = BuildOverride(anim.runtimeAnimatorController,
            ("locomotion", "Player_Walk__Player_Walk"),
            ("run",        "Player_Run__Player_Run"),
            ("aim",        "Player_Aim__Player_Aim"),
            ("jump",       "Player_Jump__Player_Jump"),
            ("die",        "Player_Die__Player_Die"),
            ("idle",       "Player_Idle__Player_Idle"),
            ("crouch",     "Player_Crouch__Player_Crouch"));
        return _overrideJugador;
    }
}
