using System;
using System.Collections.Generic;
using Convai.Modules.LipSync.Editor.UI;
using Convai.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Convai.Editor
{
    /// <summary>
    ///     Provides Convai SDK settings in the Project Settings window.
    ///     Accessible via Edit > Project Settings > Convai SDK.
    /// </summary>
    public static class ConvaiSettingsProvider
    {
        private const string SettingsPath = "Project/Convai SDK";
        private const string EditorStateHostId = "ConvaiProjectSettingsProvider";
        private const string SectionCredentialsId = "Credentials";
        private const string SectionRuntimeDefaultsId = "RuntimeDefaults";
        private const string SectionDiagnosticsId = "Diagnostics";
        private const string SectionAdvancedId = "Advanced";
        private const string DefaultServerUrl = "https://live.convai.com";

        private static SerializedObject _serializedSettings;
        private static Texture2D _icon;
        private static bool _showApiKey;
        private static bool _showCredentials = true;
        private static bool _showRuntimeDefaults = true;
        private static bool _showDiagnostics = true;
        private static bool _showAdvanced;
        private static bool _showCategoryOverrides;
        private static bool _stylesInitialized;
        private static GUIStyle _headerStyle;
        private static GUIStyle _statusStyle;
        private static GUIStyle _miniButtonStyle;

        /// <summary>Creates the Project Settings provider for Convai SDK settings.</summary>
        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            var provider = new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "Convai SDK",
                guiHandler = DrawSettingsGUI,
                keywords = new HashSet<string>(new[]
                {
                    "Convai", "API", "Key", "Server", "Player", "Microphone", "Logging", "Camera", "Transcript",
                    "Notification", "Settings", "Diagnostics", "Project"
                }),
                activateHandler = OnActivate
            };

            return provider;
        }

        private static void OnActivate(string searchContext, VisualElement rootElement)
        {
            _serializedSettings = new SerializedObject(GetOrCreateSettings());
            _icon = ConvaiEditorSettings.Instance.ConvaiIconTexture;
            _showCredentials = ConvaiLipSyncSectionStateStore.Get(EditorStateHostId, SectionCredentialsId, true);
            _showRuntimeDefaults =
                ConvaiLipSyncSectionStateStore.Get(EditorStateHostId, SectionRuntimeDefaultsId, true);
            _showDiagnostics = ConvaiLipSyncSectionStateStore.Get(EditorStateHostId, SectionDiagnosticsId, true);
            _showAdvanced = ConvaiLipSyncSectionStateStore.Get(EditorStateHostId, SectionAdvancedId, false);
            InitializeStyles();
            NormalizeLegacyTranscriptModeSetting();
        }

        private static void DrawSettingsGUI(string searchContext)
        {
            if (_serializedSettings == null || _serializedSettings.targetObject == null)
            {
                _serializedSettings = new SerializedObject(GetOrCreateSettings());
                _icon = ConvaiEditorSettings.Instance.ConvaiIconTexture;
            }

            InitializeStyles();
            NormalizeLegacyTranscriptModeSetting();
            _serializedSettings.Update();

            SerializedProperty apiKeyProp = _serializedSettings.FindProperty("_apiKey");
            SerializedProperty serverUrlProp = _serializedSettings.FindProperty("_serverUrl");
            SerializedProperty playerNameProp = _serializedSettings.FindProperty("_playerName");
            SerializedProperty transcriptEnabledProp = _serializedSettings.FindProperty("_transcriptSystemEnabled");
            SerializedProperty notificationEnabledProp = _serializedSettings.FindProperty("_notificationSystemEnabled");
            SerializedProperty defaultMicIndexProp = _serializedSettings.FindProperty("_defaultMicrophoneIndex");
            SerializedProperty globalLogLevelProp = _serializedSettings.FindProperty("_globalLogLevel");
            SerializedProperty includeStackTracesProp = _serializedSettings.FindProperty("_includeStackTraces");
            SerializedProperty coloredOutputProp = _serializedSettings.FindProperty("_coloredOutput");
            SerializedProperty categoryOverridesProp = _serializedSettings.FindProperty("_categoryOverrides");
            SerializedProperty connectionTimeoutProp = _serializedSettings.FindProperty("_connectionTimeout");

            if (apiKeyProp == null || serverUrlProp == null || playerNameProp == null ||
                transcriptEnabledProp == null ||
                notificationEnabledProp == null || defaultMicIndexProp == null || globalLogLevelProp == null ||
                includeStackTracesProp == null || coloredOutputProp == null || connectionTimeoutProp == null)
            {
                EditorGUILayout.HelpBox("Convai settings asset schema is incomplete. Reimport the SDK package.",
                    MessageType.Error);
                return;
            }

            DrawInspectorHeader();
            EditorGUILayout.Space(6f);
            DrawValidationSummary(apiKeyProp, serverUrlProp);

            DrawCredentialsSection(apiKeyProp, serverUrlProp, playerNameProp);
            DrawRuntimeDefaultsSection(transcriptEnabledProp, notificationEnabledProp, defaultMicIndexProp);
            DrawDiagnosticsSection(globalLogLevelProp, includeStackTracesProp, coloredOutputProp,
                categoryOverridesProp);
            DrawAdvancedSection(connectionTimeoutProp);

            if (_serializedSettings.ApplyModifiedProperties()) AssetDatabase.SaveAssets();
        }

        private static void DrawInspectorHeader()
        {
            const float headerHeight = 44f;
            const float iconSize = 22f;
            const float statusRegionWidth = 150f;

            Rect headerRect = EditorGUILayout.BeginVertical();
            try
            {
                EditorGUI.DrawRect(
                    new Rect(headerRect.x - 18f, headerRect.y - 4f, headerRect.width + 36f, headerHeight + 4f),
                    ConvaiLipSyncEditorThemeTokens.HeaderBackground);

                EditorGUILayout.BeginVertical(GUILayout.Height(headerHeight));
                try
                {
                    Rect rowRect = GUILayoutUtility.GetRect(0f, headerHeight, GUILayout.ExpandWidth(true),
                        GUILayout.Height(headerHeight));

                    if (_icon != null && Event.current.type == EventType.Repaint)
                    {
                        var iconRect = new Rect(rowRect.x, rowRect.y + ((headerHeight - iconSize) * 0.5f), iconSize,
                            iconSize);
                        GUI.DrawTexture(iconRect, _icon, ScaleMode.ScaleToFit, true);
                    }

                    float textX = rowRect.x + iconSize + 6f;
                    var textRect = new Rect(textX, rowRect.y + 11f, rowRect.width - statusRegionWidth - textX, 22f);
                    GUI.Label(textRect, "Convai SDK", _headerStyle);

                    var statusRect = new Rect(rowRect.xMax - statusRegionWidth, rowRect.y, statusRegionWidth,
                        rowRect.height);
                    GUI.Label(statusRect, "Project Defaults", _statusStyle);
                }
                finally
                {
                    EditorGUILayout.EndVertical();
                }
            }
            finally
            {
                EditorGUILayout.EndVertical();
            }

            GUILayout.Space(2f);
        }

        private static void DrawValidationSummary(SerializedProperty apiKeyProp, SerializedProperty serverUrlProp)
        {
            bool hasApiKey = !string.IsNullOrWhiteSpace(apiKeyProp.stringValue);
            string normalizedServerUrl = NormalizeUrl(serverUrlProp.stringValue);
            bool isDefaultServer = string.Equals(normalizedServerUrl, NormalizeUrl(DefaultServerUrl),
                StringComparison.OrdinalIgnoreCase);

            if (!hasApiKey)
            {
                EditorGUILayout.HelpBox(
                    "API key is missing. Project-wide defaults are configured here, but character sessions and backend-backed tools require a valid key.",
                    MessageType.Warning);
            }

            if (!string.IsNullOrWhiteSpace(normalizedServerUrl) && !isDefaultServer)
            {
                EditorGUILayout.HelpBox(
                    "A custom core server base URL is active for this project. Keep this only for staging, enterprise, or support-directed setups.",
                    MessageType.Info);
            }
        }

        private static void DrawCredentialsSection(
            SerializedProperty apiKeyProp,
            SerializedProperty serverUrlProp,
            SerializedProperty playerNameProp)
        {
            _showCredentials = DrawSectionHeader(SectionCredentialsId, "CREDENTIALS", _showCredentials, "\u26BF");
            if (!_showCredentials) return;

            DrawSectionBackground(() =>
            {
                EditorGUILayout.HelpBox(
                    "These are package-wide defaults shared across scenes. Runtime credential providers can still override them in code.",
                    MessageType.Info);

                EditorGUILayout.BeginHorizontal();
                string currentApiKey = apiKeyProp.stringValue ?? string.Empty;
                string nextApiKey = _showApiKey
                    ? EditorGUILayout.TextField(
                        new GUIContent("API Key", "Your Convai dashboard API key."),
                        currentApiKey)
                    : EditorGUILayout.PasswordField(
                        new GUIContent("API Key", "Your Convai dashboard API key."),
                        currentApiKey);
                if (!string.Equals(nextApiKey, currentApiKey, StringComparison.Ordinal))
                    apiKeyProp.stringValue = nextApiKey?.Trim() ?? string.Empty;

                if (GUILayout.Button(_showApiKey ? "Hide" : "Show", _miniButtonStyle, GUILayout.Width(60f)))
                    _showApiKey = !_showApiKey;
                EditorGUILayout.EndHorizontal();

                string currentServerUrl = serverUrlProp.stringValue ?? string.Empty;
                string nextServerUrl = EditorGUILayout.TextField(
                    new GUIContent("Core Server Base URL",
                        "Package-wide base URL used for room/session requests unless a runtime provider overrides it."),
                    currentServerUrl);
                if (!string.Equals(nextServerUrl, currentServerUrl, StringComparison.Ordinal))
                    serverUrlProp.stringValue = nextServerUrl?.Trim() ?? string.Empty;

                string currentPlayerName = playerNameProp.stringValue ?? string.Empty;
                string nextPlayerName = EditorGUILayout.TextField(
                    new GUIContent("Default Player Name",
                        "Project-wide fallback player display name when runtime settings do not override it."),
                    currentPlayerName);
                if (!string.Equals(nextPlayerName, currentPlayerName, StringComparison.Ordinal))
                    playerNameProp.stringValue = nextPlayerName;
            });
        }

        private static void DrawRuntimeDefaultsSection(
            SerializedProperty transcriptEnabledProp,
            SerializedProperty notificationEnabledProp,
            SerializedProperty defaultMicIndexProp)
        {
            _showRuntimeDefaults = DrawSectionHeader(
                SectionRuntimeDefaultsId,
                "RUNTIME DEFAULTS",
                _showRuntimeDefaults,
                "\u25CE");
            if (!_showRuntimeDefaults) return;

            DrawSectionBackground(() =>
            {
                EditorGUILayout.HelpBox(
                    "These values seed the runtime settings service. They apply project-wide until a runtime settings UI or script overrides them.",
                    MessageType.Info);

                transcriptEnabledProp.boolValue = EditorGUILayout.Toggle(
                    new GUIContent("Transcript System", "Enable transcript UI/event flow by default."),
                    transcriptEnabledProp.boolValue);
                notificationEnabledProp.boolValue = EditorGUILayout.Toggle(
                    new GUIContent("Notification System",
                        "Enable session-state and error notifications by default."),
                    notificationEnabledProp.boolValue);

                int micIndex = defaultMicIndexProp.intValue;
                int clampedMicIndex = Mathf.Clamp(
                    EditorGUILayout.IntField(
                        new GUIContent("Default Microphone Index",
                            "Project-wide default microphone device index. 0 uses the system default device."),
                        micIndex),
                    0,
                    10);
                if (clampedMicIndex != micIndex) defaultMicIndexProp.intValue = clampedMicIndex;

                EditorGUILayout.HelpBox(
                    "Use microphone index 0 unless you need a fixed device order for kiosk or lab setups.",
                    MessageType.None);
            });
        }

        private static void DrawDiagnosticsSection(
            SerializedProperty globalLogLevelProp,
            SerializedProperty includeStackTracesProp,
            SerializedProperty coloredOutputProp,
            SerializedProperty categoryOverridesProp)
        {
            _showDiagnostics = DrawSectionHeader(SectionDiagnosticsId, "DIAGNOSTICS", _showDiagnostics, "\u26A0");
            if (!_showDiagnostics) return;

            DrawSectionBackground(() =>
            {
                globalLogLevelProp.enumValueIndex = EditorGUILayout.Popup(
                    new GUIContent("Global Log Level", "Minimum log level. Lower-severity logs are filtered."),
                    globalLogLevelProp.enumValueIndex,
                    globalLogLevelProp.enumDisplayNames);
                includeStackTracesProp.boolValue = EditorGUILayout.Toggle(
                    new GUIContent("Include Stack Traces", "Include stack traces for warning and error logs."),
                    includeStackTracesProp.boolValue);
                coloredOutputProp.boolValue = EditorGUILayout.Toggle(
                    new GUIContent("Colored Console Output", "Use colorized console messages when supported."),
                    coloredOutputProp.boolValue);

                if (categoryOverridesProp == null) return;

                _showCategoryOverrides = EditorGUILayout.Foldout(
                    _showCategoryOverrides,
                    $"Category Overrides ({categoryOverridesProp.arraySize})",
                    true);

                if (!_showCategoryOverrides) return;

                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(categoryOverridesProp, GUIContent.none, true);
                EditorGUI.indentLevel--;
            });
        }

        private static void DrawAdvancedSection(SerializedProperty connectionTimeoutProp)
        {
            _showAdvanced = DrawSectionHeader(SectionAdvancedId, "ADVANCED", _showAdvanced, "\u2699");
            if (!_showAdvanced) return;

            DrawSectionBackground(() =>
            {
                connectionTimeoutProp.floatValue = EditorGUILayout.Slider(
                    new GUIContent("Connection Timeout (s)",
                        "Project-wide timeout used by the native/editor realtime connection path."),
                    Mathf.Clamp(connectionTimeoutProp.floatValue, 5f, 120f),
                    5f,
                    120f);
                EditorGUILayout.HelpBox(
                    "Adjust this only for support-directed or custom transport scenarios.",
                    MessageType.Info);
            });
        }

        private static void NormalizeLegacyTranscriptModeSetting()
        {
            if (_serializedSettings == null || _serializedSettings.targetObject == null) return;

            SerializedProperty transcriptStyleProp = _serializedSettings.FindProperty("_activeTranscriptStyleIndex");
            if (transcriptStyleProp == null || transcriptStyleProp.intValue == 0) return;

            _serializedSettings.Update();
            transcriptStyleProp.intValue = 0;
            _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }

        private static bool DrawSectionHeader(string sectionId, string title, bool expanded, string icon)
        {
            bool next = ConvaiLipSyncSectionChrome.DrawHeader(
                new ConvaiSectionHeaderSpec(
                    EditorStateHostId,
                    sectionId,
                    title,
                    icon,
                    ConvaiLipSyncEditorThemeTokens.Accent),
                expanded);
            ConvaiLipSyncSectionStateStore.Set(EditorStateHostId, sectionId, next);
            return next;
        }

        private static void DrawSectionBackground(Action drawContent)
        {
            ConvaiLipSyncSectionChrome.BeginBody();
            try
            {
                drawContent?.Invoke();
            }
            finally
            {
                ConvaiLipSyncSectionChrome.EndBody();
            }
        }

        private static string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;

            return url.Trim().TrimEnd('/');
        }

        private static void InitializeStyles()
        {
            if (_stylesInitialized) return;

            ConvaiLipSyncEditorStyleCache.EnsureInitialized();
            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = ConvaiLipSyncEditorThemeTokens.AccentEmphasis }
            };
            _statusStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(0.76f, 0.76f, 0.79f, 0.95f) }
            };
            _miniButtonStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 10, padding = new RectOffset(8, 8, 3, 3)
            };
            _stylesInitialized = true;
        }

        private static ConvaiSettings GetOrCreateSettings()
        {
            // Delegate to the canonical singleton so that Project Settings, Configuration Window,
            // and runtime code all operate on the exact same asset instance.
            return ConvaiSettings.Instance;
        }
    }
}
