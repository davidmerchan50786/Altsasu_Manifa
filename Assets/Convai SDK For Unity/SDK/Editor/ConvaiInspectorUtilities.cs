using Convai.Shared.Types;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor
{
    internal static class ConvaiInspectorDeveloperSettings
    {
        internal const string InspectorDeveloperVisibilityPrefKey = "Convai.Editor.ShowDeveloperInspectorSettings";

        internal static bool AreInspectorDeveloperSettingsVisible =>
            EditorPrefs.GetBool(InspectorDeveloperVisibilityPrefKey, false);

        [MenuItem("Convai/Developer/Toggle Inspector Developer Settings", false, 210)]
        private static void ToggleInspectorDeveloperSettings()
        {
            bool next = !AreInspectorDeveloperSettingsVisible;
            EditorPrefs.SetBool(InspectorDeveloperVisibilityPrefKey, next);
            Debug.Log($"[Convai] Inspector developer settings {(next ? "enabled" : "disabled")}.");
        }
    }

    internal static class ConvaiInspectorFieldUtility
    {
        private static readonly string[] PublicServerEndpointOptions = { "Connect" };

        internal static void DrawServerEndpointField(SerializedProperty property, GUIContent label)
        {
            if (property == null)
                return;

            if (ConvaiInspectorDeveloperSettings.AreInspectorDeveloperSettingsVisible)
            {
                EditorGUILayout.PropertyField(property, label);
                return;
            }

            var endpoint = (ConvaiServerEndpoint)property.enumValueIndex;
            if (endpoint == ConvaiServerEndpoint.Connect)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.Popup(label, 0, PublicServerEndpointOptions);

                return;
            }

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField(label, endpoint.ToString());

            EditorGUILayout.HelpBox(
                "This room is using an internal server route. Normal projects should use Connect.",
                MessageType.Warning);
        }
    }
}
