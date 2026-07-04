using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Editor.Utilities;
using Convai.Runtime;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor
{
    /// <summary>
    ///     Editor wizard for setting up required Convai SDK components in a scene.
    ///     Provides menu items to add missing components and validate scene setup.
    /// </summary>
    public static class ConvaiSetupWizard
    {
        private const string MenuPath = "GameObject/Convai/";

        /// <summary>
        ///     Adds all required Convai SDK components to the scene if they are missing.
        /// </summary>
        [MenuItem(MenuPath + "Setup Required Components", false, 10)]
        public static void SetupRequiredComponents()
        {
            Undo.SetCurrentGroupName("Setup Convai Components");
            int undoGroup = Undo.GetCurrentGroup();

            bool addedAny = false;

            GameObject managerGo = null;
            if (Object.FindFirstObjectByType<ConvaiManager>() == null)
            {
                managerGo = new GameObject("[Convai Manager]");
                Undo.RegisterCreatedObjectUndo(managerGo, "Create ConvaiManager");
                Undo.AddComponent<ConvaiManager>(managerGo);
                ConvaiLogger.Debug("[Convai Setup] Added ConvaiManager to scene.", LogCategory.Editor);
                addedAny = true;
            }
            else
                managerGo = Object.FindFirstObjectByType<ConvaiManager>().gameObject;

            if (managerGo != null && managerGo.GetComponent<ConvaiRoomManager>() == null)
            {
                Undo.AddComponent<ConvaiRoomManager>(managerGo);
                ConvaiLogger.Debug("[Convai Setup] Added ConvaiRoomManager to [Convai Manager].", LogCategory.Editor);
                addedAny = true;
            }

            Undo.CollapseUndoOperations(undoGroup);

            if (addedAny)
            {
                EditorUtility.DisplayDialog(
                    "Convai Setup Complete",
                    "Required Convai SDK components have been added to the scene.\n\n" +
                    "Next steps:\n" +
                    "1. Configure your API key:\n" +
                    "   Edit > Project Settings > Convai SDK\n\n" +
                    "2. Add ConvaiCharacter to your Characters:\n" +
                    "   Select Character > Add Component > Convai Character\n\n" +
                    "3. Add ConvaiPlayer to your player:\n" +
                    "   Select Player > Add Component > Convai Player",
                    "OK");
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "Convai Setup",
                    "All required components are already in the scene!\n\n" +
                    "Your scene has:\n" +
                    "✓ ConvaiManager\n" +
                    "✓ ConvaiRoomManager",
                    "OK");
            }
        }

        /// <summary>
        ///     Validates the current scene setup and reports any issues.
        /// </summary>
        [MenuItem(MenuPath + "Validate Scene Setup", false, 11)]
        public static void ValidateSceneSetup()
        {
            var issues = new List<string>();
            var warnings = new List<string>();

            if (Object.FindFirstObjectByType<ConvaiManager>() == null) issues.Add("Missing ConvaiManager");

            var settings = ConvaiSettings.Instance;
            if (settings == null || !settings.HasApiKey)
                warnings.Add("API key not configured (Edit > Project Settings > Convai SDK)");

            ConvaiCharacter[] characters = Object.FindObjectsByType<ConvaiCharacter>(FindObjectsSortMode.None);
            if (characters.Length == 0)
                issues.Add("No ConvaiCharacter components found in scene");
            else
            {
                foreach (ConvaiCharacter character in characters)
                {
                    if (string.IsNullOrWhiteSpace(character.CharacterId))
                        issues.Add($"ConvaiCharacter on '{character.gameObject.name}' has no Character ID");
                }
            }

            ConvaiPlayer[] players = Object.FindObjectsByType<ConvaiPlayer>(FindObjectsSortMode.None);
            if (players.Length == 0) issues.Add("No ConvaiPlayer component found in scene");

            if (issues.Count == 0 && warnings.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Validation Passed ✓",
                    "Scene setup is correct!\n\n" +
                    $"Found {characters.Length} ConvaiCharacter(s) in scene.",
                    "OK");
                ConvaiLogger.Debug("[Convai Validation] Scene setup is correct.", LogCategory.Editor);
            }
            else
            {
                string message = "";

                if (issues.Count > 0) message += "❌ ERRORS (must fix):\n• " + string.Join("\n• ", issues) + "\n\n";

                if (warnings.Count > 0) message += "⚠️ WARNINGS:\n• " + string.Join("\n• ", warnings) + "\n\n";

                if (issues.Count > 0)
                {
                    bool hasManagerIssue = false;
                    bool hasCharacterIssue = false;
                    bool hasPlayerIssue = false;

                    foreach (string issue in issues)
                    {
                        if (issue.Contains("ConvaiManager")) hasManagerIssue = true;

                        if (issue.Contains("ConvaiCharacter")) hasCharacterIssue = true;

                        if (issue.Contains("ConvaiPlayer")) hasPlayerIssue = true;
                    }

                    var fixes = new List<string>();
                    if (hasManagerIssue)
                        fixes.Add("Add ConvaiManager: GameObject > Convai > Setup Required Components");

                    if (hasCharacterIssue)
                        fixes.Add("Add ConvaiCharacter to your character object and set Character ID");

                    if (hasPlayerIssue) fixes.Add("Add ConvaiPlayer to your player object");

                    if (fixes.Count > 0) message += "How to fix:\n• " + string.Join("\n• ", fixes);
                }

                EditorUtility.DisplayDialog(
                    issues.Count > 0 ? "Validation Failed" : "Validation Warnings",
                    message,
                    "OK");

                if (issues.Count > 0)
                    ConvaiLogger.Error("[Convai Validation] " + message, LogCategory.Editor);
                else
                    ConvaiLogger.Warning("[Convai Validation] " + message, LogCategory.Editor);
            }
        }

        /// <summary>
        ///     Opens the Convai SDK documentation in a browser.
        /// </summary>
        [MenuItem(MenuPath + "Open Documentation", false, 100)]
        public static void OpenDocumentation() =>
            UnityEngine.Application.OpenURL(ConvaiEditorLinks.DocsUnityQuickstartUrl);

        /// <summary>
        ///     Opens the Convai SDK settings in Project Settings.
        /// </summary>
        [MenuItem(MenuPath + "Open SDK Settings", false, 101)]
        public static void OpenSDKSettings() => SettingsService.OpenProjectSettings("Project/Convai SDK");
    }
}
