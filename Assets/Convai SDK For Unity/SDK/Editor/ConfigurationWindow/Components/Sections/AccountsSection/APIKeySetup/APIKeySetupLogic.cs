using System;
using System.Threading.Tasks;
using Convai.Domain.Logging;
using Convai.Editor.Utilities;
using Convai.RestAPI;
using Convai.Runtime;
using Convai.Runtime.Logging;
using UnityEditor;
using UnityEngine.UIElements;

namespace Convai.Editor.ConfigurationWindow.Components.Sections.AccountsSection.APIKeySetup
{
    /// <summary>
    ///     Handles API key setup and validation logic for the Configuration Window.
    ///     Uses ConvaiSettings for API key storage.
    /// </summary>
    public abstract class APIKeySetupLogic
    {
        private static void RunOnEditorMainThread(Action action)
        {
            if (action == null) return;

            // Ensure UnityEditor/UIElements interactions happen on the editor main thread.
            EditorApplication.delayCall += () =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    // Do not throw from delayCall, but log so failures are diagnosable.
                    ConvaiLogger.Error($"[APIKeySetupLogic] delayCall callback threw: {ex}", LogCategory.Editor);
                }
            };
        }

        /// <summary>
        ///     Validates the provided API key and persists it to <see cref="ConvaiSettings" /> if valid.
        /// </summary>
        /// <param name="apiKey">API key string.</param>
        /// <param name="callback">Callback invoked with validation result.</param>
        public static void BeginButtonTask(string apiKey, Action<bool> callback) =>
            _ = BeginButtonTaskAsync(apiKey, callback);

        private static async Task BeginButtonTaskAsync(string apiKey, Action<bool> callback)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                RunOnEditorMainThread(() =>
                {
                    EditorUtility.DisplayDialog("Error", "Please enter a valid API Key.", "OK");
                    callback?.Invoke(false);
                });
                return;
            }

            try
            {
                var options = new ConvaiRestClientOptions(apiKey);
                using var client = new ConvaiRestClient(options);
                // ValidateApiKeyAsync throws ConvaiRestException for non-2xx responses (e.g., invalid API key).
                await client.Users.ValidateApiKeyAsync();

                RunOnEditorMainThread(() =>
                {
                    var settings = ConvaiSettings.Instance;
                    if (settings != null)
                    {
                        settings.SetApiKey(apiKey);
                        EditorUtility.SetDirty(settings);
                        AssetDatabase.SaveAssets();
                    }

                    callback?.Invoke(true);
                });
            }
            catch (ConvaiRestException ex)
            {
                ConvaiLogger.Warning(
                    $"[APIKeySetupLogic] API key validation failed ({ex.Category}, HTTP {ex.StatusCodeInt}): {ex.Message}",
                    LogCategory.Editor);

                RunOnEditorMainThread(() =>
                {
                    EditorUtility.DisplayDialog(
                        "Error",
                        $"{ex.GetUserFriendlyMessage()}\n\nContact {ConvaiEditorLinks.SupportEmail} for more help.",
                        "OK");

                    callback?.Invoke(false);
                });
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"[APIKeySetupLogic] API key validation encountered an unexpected error: {ex}",
                    LogCategory.Editor);

                RunOnEditorMainThread(() =>
                {
                    EditorUtility.DisplayDialog(
                        "Error",
                        $"Something went wrong. Please check your API Key. Contact {ConvaiEditorLinks.SupportEmail} for more help.",
                        "OK");
                    callback?.Invoke(false);
                });
            }
        }

        /// <summary>
        ///     Loads an existing API key from <see cref="ConvaiSettings" /> into the UI.
        /// </summary>
        /// <param name="apiKeyField">Text field to populate.</param>
        /// <param name="saveApiKeyButton">Button to update label/state.</param>
        /// <returns>True when an existing API key was found and loaded.</returns>
        public static bool LoadExistingApiKey(TextField apiKeyField, Button saveApiKeyButton)
        {
            var settings = ConvaiSettings.Instance;
            if (settings == null || !settings.HasApiKey)
            {
                saveApiKeyButton.text = "Save API Key";
                return false;
            }

            apiKeyField.value = settings.ApiKey;
            saveApiKeyButton.text = "Update API Key";
            return true;
        }
    }
}
