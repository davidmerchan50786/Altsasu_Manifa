// Assets/Scripts/Editor/FixEventSystem.cs
// ═══════════════════════════════════════════════════════════════════════════
//  Upgrades any StandaloneInputModule → InputSystemUIInputModule at play-time.
//  Runs automatically via [InitializeOnLoad] + playModeStateChanged.
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.EventSystems;

[InitializeOnLoad]
public static class FixEventSystem
{
    static FixEventSystem()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        // Run just before entering Play mode so EventSystems are already in scene
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        UpgradeAllEventSystems();
    }

    [MenuItem("Tools/Alsasua/🔧 Fix EventSystem (Input System)")]
    public static void UpgradeAllEventSystems()
    {
        var eventSystems = Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
        int fixed_ = 0;

        foreach (var es in eventSystems)
        {
            var standalone = es.GetComponent<StandaloneInputModule>();
            if (standalone == null) continue;

#if ENABLE_INPUT_SYSTEM
            // Add InputSystemUIInputModule if not already present
            var inputSysModule = es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            if (inputSysModule == null)
                es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

            // Remove the old module
            Object.DestroyImmediate(standalone);
            fixed_++;
            AlsasuaLogger.Info("FixEventSystem",
                $"Upgraded EventSystem '{es.name}' → InputSystemUIInputModule");
#endif
        }

        if (fixed_ == 0 && !Application.isPlaying)
            Debug.Log("[FixEventSystem] No StandaloneInputModule found — already correct.");
    }
}
#endif
