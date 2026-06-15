// Assets/Scripts/Editor/AutoHornearEnPlay.cs
// ═══════════════════════════════════════════════════════════════════════════
//  AUTO-HORNEAR EN PLAY — dispara el HorneadorCiudad desde CÓDIGO (sin UI)
//
//  Por qué: el mundo se genera en runtime, y el editor en Play va a ~1 FPS y queda
//  "(No responde)" → no se puede pulsar el menú de horneado de forma remota. Este
//  hook ejecuta el bake automáticamente unos segundos después de entrar a Play,
//  cuando la ciudad ya está generada, SIN necesidad de clicar nada.
//
//  Self-disarming: en cuanto el bake crea Assets/CiudadHorneada/manifest_ciudad.json,
//  no vuelve a ejecutarse. Si salta antes de tiempo (pocas mallas), reintenta.
//
//  AYUDA DE DESARROLLO TEMPORAL: poner HABILITADO=false (o menú) para desactivarlo.
//  Bórralo cuando el flujo de horneado sea manual/definitivo.
// ═══════════════════════════════════════════════════════════════════════════

using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class AutoHornearEnPlay
{
    const bool   HABILITADO = true;                       // ← dev aid; false para apagarlo
    const double ESPERA_S   = 30.0;                       // s tras entrar a Play antes del 1er intento
    const double REINTENTO_S = 20.0;                      // s entre reintentos si el mundo no estaba listo
    const string MANIFEST   = "Assets/CiudadHorneada/manifest_ciudad.json";
    const string PREF_OFF   = "Alsasua_AutoHornear_Off";  // override por usuario

    static double _proximoIntento = double.MaxValue;

    static AutoHornearEnPlay()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.update += OnUpdate;
    }

    static void OnPlayMode(PlayModeStateChange s)
    {
        if (s == PlayModeStateChange.EnteredPlayMode)
            _proximoIntento = EditorApplication.timeSinceStartup + ESPERA_S;
        else if (s == PlayModeStateChange.ExitingPlayMode)
            _proximoIntento = double.MaxValue;
    }

    static void OnUpdate()
    {
        if (!HABILITADO || EditorPrefs.GetBool(PREF_OFF, false)) return;
        if (!EditorApplication.isPlaying) return;
        if (EditorApplication.timeSinceStartup < _proximoIntento) return;
        if (File.Exists(MANIFEST)) { _proximoIntento = double.MaxValue; return; }  // ya horneado

        _proximoIntento = EditorApplication.timeSinceStartup + REINTENTO_S;        // arma reintento
        Debug.Log("[AutoHornear] Lanzando horneado de ciudad desde código (sin UI)…");
        try { HorneadorCiudad.HornearAuto(); }
        catch (System.Exception e) { Debug.LogError($"[AutoHornear] Falló: {e}"); }
    }

    [MenuItem("Tools/Alsasua/Mundo/⏹️ Auto-hornear: desactivar")]
    static void Desactivar() { EditorPrefs.SetBool(PREF_OFF, true);  Debug.Log("[AutoHornear] Desactivado."); }
    [MenuItem("Tools/Alsasua/Mundo/▶️ Auto-hornear: activar")]
    static void Activar()    { EditorPrefs.SetBool(PREF_OFF, false); Debug.Log("[AutoHornear] Activado."); }
}
