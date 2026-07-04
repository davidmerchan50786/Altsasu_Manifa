using System;
using Convai.Editor.ConfigurationWindow.Services;
using Convai.Modules.LipSync.Editor.UI;
using Convai.Runtime;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Room;
using Convai.Runtime.Vision.Sources;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor.Inspectors
{
    [CustomEditor(typeof(ConvaiRoomManager))]
    public class ConvaiRoomManagerEditor : UnityEditor.Editor
    {
        private const string EditorStateHostId = "ConvaiRoomManagerEditor";
        private const string SectionConfigurationId = "Configuration";
        private const string SectionRoomDefaultsId = "RoomDefaults";
        private const string SectionRuntimeId = "Runtime";
        private const string SectionConversationId = "Conversation";
        private const string SectionSceneId = "Scene";
        private const string SectionValidationId = "Validation";
        private const string SectionAdvancedId = "Advanced";
        private const double ValidationRefreshIntervalSeconds = 0.5d;
        private SerializedProperty _autoMicStartDelaySecondsProp;
        private SetupHealthReport _cachedSetupHealthReport;
        private SerializedProperty _configurationSourceInitializedProp;
        private SerializedProperty _configurationSourceProp;
        private SerializedProperty _connectionTypeProp;
        private SerializedProperty _connectOnStartProp;
        private SerializedProperty _coreServerBaseUrlProp;
        private SerializedProperty _debugProp;
        private GUIStyle _headerStyle;
        private Texture2D _icon;
        private SerializedProperty _maxReconnectAttemptsProp;
        private GUIStyle _miniButtonStyle;
        private double _nextValidationRefreshTime;
        private SerializedProperty _resumePolicyProp;
        private SerializedProperty _roomConfigAssetProp;
        private SerializedObject _roomConfigSerializedObject;

        private ConvaiRoomManager _roomManager;
        private SerializedProperty _roomPushToTalkKeyProp;
        private SerializedProperty _roomRejoinTtlSecondsProp;
        private SerializedProperty _serverEndpointProp;
        private bool _showAdvanced;
        private bool _showConfiguration;
        private bool _showConversation = true;
        private bool _showRoomDefaults;
        private bool _showRuntime;
        private bool _showScene = true;
        private bool _showValidation = true;
        private SerializedProperty _spawnAgentOnRejoinProp;
        private SerializedProperty _startWaitTimeoutMsProp;
        private GUIStyle _statusStyle;
        private bool _stylesInitialized;
        private SerializedProperty _turnTakingOptionsProp;

        private bool UsesRoomConfigAsset =>
            (ConvaiConfigSourceMode)_configurationSourceProp.enumValueIndex == ConvaiConfigSourceMode.Asset &&
            _roomConfigAssetProp.objectReferenceValue != null;

        private bool IsAssetModeSelected =>
            (ConvaiConfigSourceMode)_configurationSourceProp.enumValueIndex == ConvaiConfigSourceMode.Asset;

        private bool HasLegacyCoreServerOverride =>
            _coreServerBaseUrlProp != null && !string.IsNullOrWhiteSpace(_coreServerBaseUrlProp.stringValue);

        private void OnEnable()
        {
            _roomManager = (ConvaiRoomManager)target;
            _icon = ConvaiEditorSettings.Instance.ConvaiIconTexture;

            _configurationSourceProp = serializedObject.FindProperty("_configurationSource");
            _configurationSourceInitializedProp = serializedObject.FindProperty("_configurationSourceInitialized");
            _roomConfigAssetProp = serializedObject.FindProperty("_roomConfigAsset");
            _connectionTypeProp = serializedObject.FindProperty("_connectionType");
            _coreServerBaseUrlProp = serializedObject.FindProperty("<CoreServerBaseURL>k__BackingField");
            _serverEndpointProp = serializedObject.FindProperty("<ServerEndpoint>k__BackingField");
            _connectOnStartProp = serializedObject.FindProperty("<ConnectOnStart>k__BackingField");
            _turnTakingOptionsProp = serializedObject.FindProperty("_turnTakingOptions");
            _roomPushToTalkKeyProp = serializedObject.FindProperty("_pushToTalkKey");
            _debugProp = serializedObject.FindProperty("<Debug>k__BackingField");
            _roomRejoinTtlSecondsProp = serializedObject.FindProperty("_roomRejoinTtlSeconds");
            _resumePolicyProp = serializedObject.FindProperty("_resumePolicy");
            _maxReconnectAttemptsProp = serializedObject.FindProperty("_maxReconnectAttempts");
            _spawnAgentOnRejoinProp = serializedObject.FindProperty("_spawnAgentOnRejoin");
            _startWaitTimeoutMsProp = serializedObject.FindProperty("_startWaitTimeoutMs");
            _autoMicStartDelaySecondsProp = serializedObject.FindProperty("_autoMicStartDelaySeconds");

            _showConversation = ConvaiLipSyncSectionStateStore.Get(EditorStateHostId, SectionConversationId, true);
            _showScene = ConvaiLipSyncSectionStateStore.Get(EditorStateHostId, SectionSceneId, true);
            _showValidation = ConvaiLipSyncSectionStateStore.Get(EditorStateHostId, SectionValidationId, true);
            _showAdvanced = ConvaiLipSyncSectionStateStore.Get(EditorStateHostId, SectionAdvancedId, false);
            _showConfiguration = ConvaiLipSyncSectionStateStore.Get(EditorStateHostId, SectionConfigurationId, true);
            _showRoomDefaults = ConvaiLipSyncSectionStateStore.Get(EditorStateHostId, SectionRoomDefaultsId, true);
            _showRuntime = ConvaiLipSyncSectionStateStore.Get(EditorStateHostId, SectionRuntimeId, false);

            EnsureConfigurationSourceMigration();
        }

        private void OnDisable()
        {
            ConvaiLipSyncSectionStateStore.Set(EditorStateHostId, SectionConversationId, _showConversation);
            ConvaiLipSyncSectionStateStore.Set(EditorStateHostId, SectionSceneId, _showScene);
            ConvaiLipSyncSectionStateStore.Set(EditorStateHostId, SectionValidationId, _showValidation);
            ConvaiLipSyncSectionStateStore.Set(EditorStateHostId, SectionAdvancedId, _showAdvanced);
            ConvaiLipSyncSectionStateStore.Set(EditorStateHostId, SectionConfigurationId, _showConfiguration);
            ConvaiLipSyncSectionStateStore.Set(EditorStateHostId, SectionRoomDefaultsId, _showRoomDefaults);
            ConvaiLipSyncSectionStateStore.Set(EditorStateHostId, SectionRuntimeId, _showRuntime);
        }

        public override void OnInspectorGUI()
        {
            try
            {
                if (!HasRequiredProperties())
                {
                    DrawDefaultInspector();
                    return;
                }

                InitializeStyles();
                serializedObject.Update();
                EnsureConfigurationSourceMigration();

                DrawInspectorHeader();
                EditorGUILayout.Space(6f);

                DrawConversationSection();
                DrawSceneSection();
                DrawValidationSection();
                DrawAdvancedSection();

                serializedObject.ApplyModifiedProperties();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ConvaiRoomManagerEditor] Falling back to default inspector: {ex.Message}");
                serializedObject.UpdateIfRequiredOrScript();
                DrawDefaultInspector();
            }
        }

        private bool HasRequiredProperties() =>
            _configurationSourceProp != null &&
            _roomConfigAssetProp != null &&
            _connectionTypeProp != null &&
            _connectOnStartProp != null;

        private void InitializeStyles()
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

        private void EnsureConfigurationSourceMigration()
        {
            if (_configurationSourceInitializedProp == null || _roomConfigAssetProp == null ||
                _configurationSourceProp == null)
                return;

            if (_configurationSourceInitializedProp.boolValue) return;

            _configurationSourceProp.enumValueIndex =
                _roomConfigAssetProp.objectReferenceValue != null
                    ? (int)ConvaiConfigSourceMode.Asset
                    : (int)ConvaiConfigSourceMode.Inline;
            _configurationSourceInitializedProp.boolValue = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();
        }

        private void DrawInspectorHeader()
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
                    GUI.Label(textRect, "Convai Room Manager", _headerStyle);

                    string modeText = _roomManager != null &&
                                      _roomManager.EffectiveTurnTakingOptions.Mode == ConversationInputMode.PushToTalk
                        ? "Push To Talk"
                        : "Hands Free";
                    var statusRect = new Rect(rowRect.xMax - statusRegionWidth, rowRect.y, statusRegionWidth,
                        rowRect.height);
                    GUI.Label(statusRect, modeText, _statusStyle);
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

        private void DrawConversationSection()
        {
            _showConversation = DrawSectionHeader(SectionConversationId, "CONVERSATION", _showConversation, "\u266B");
            if (!_showConversation) return;

            DrawSectionBackground(() =>
            {
                SerializedProperty turnTakingOptionsProp =
                    GetConversationTurnTakingOptionsProperty(out bool readOnlyAssetValues);
                if (turnTakingOptionsProp == null)
                {
                    EditorGUILayout.HelpBox(
                        IsAssetModeSelected
                            ? "Assign a Room Manager Profile asset or switch Room Setup Source back to scene defaults."
                            : "Turn-taking settings are not available.",
                        MessageType.Warning);
                    return;
                }

                if (readOnlyAssetValues && _roomConfigAssetProp.objectReferenceValue != null)
                {
                    EditorGUILayout.LabelField(
                        $"Using Room Manager Profile: {_roomConfigAssetProp.objectReferenceValue.name}",
                        EditorStyles.wordWrappedMiniLabel);
                }

                SerializedProperty modeProp = turnTakingOptionsProp.FindPropertyRelative("<Mode>k__BackingField");
                SerializedProperty pushToTalkPolicyProp =
                    turnTakingOptionsProp.FindPropertyRelative("<PushToTalkPolicy>k__BackingField");
                if (modeProp == null || pushToTalkPolicyProp == null)
                    return;

                using (new EditorGUI.DisabledScope(readOnlyAssetValues))
                    EditorGUILayout.PropertyField(modeProp, ConvaiInspectorContent.HowThePlayerTalks);

                if ((ConversationInputMode)modeProp.enumValueIndex != ConversationInputMode.PushToTalk)
                    return;

                if (_roomPushToTalkKeyProp != null)
                    EditorGUILayout.PropertyField(_roomPushToTalkKeyProp, ConvaiInspectorContent.PushToTalkKey);

                using (new EditorGUI.DisabledScope(readOnlyAssetValues))
                {
                    EditorGUILayout.PropertyField(
                        pushToTalkPolicyProp.FindPropertyRelative("<InterruptBotOnPress>k__BackingField"),
                        ConvaiInspectorContent.InterruptCharacterWhenPressed);
                    EditorGUILayout.PropertyField(
                        pushToTalkPolicyProp.FindPropertyRelative(
                            "<RequireTurnCompletionBeforeNextPress>k__BackingField"),
                        ConvaiInspectorContent.WaitForCharacterToFinishBeforeTalkingAgain);
                    EditorGUILayout.PropertyField(
                        pushToTalkPolicyProp.FindPropertyRelative("<TurnCompletionTimeoutMs>k__BackingField"),
                        ConvaiInspectorContent.FallbackWaitTimeMs);
                }
            });
        }

        private void DrawSceneSection()
        {
            _showScene = DrawSectionHeader(SectionSceneId, "SCENE", _showScene, "\u2699");
            if (!_showScene) return;

            DrawSectionBackground(() =>
            {
                SerializedProperty connectOnStartProp = GetConnectOnStartProperty(out bool readOnlyAssetValue);
                if (connectOnStartProp == null)
                {
                    EditorGUILayout.HelpBox(
                        IsAssetModeSelected
                            ? "Assign a Room Manager Profile asset or switch Room Setup Source back to scene defaults."
                            : "Scene defaults are not available.",
                        MessageType.Warning);
                    return;
                }

                if (readOnlyAssetValue && _roomConfigAssetProp.objectReferenceValue != null)
                {
                    EditorGUILayout.LabelField(
                        $"Starts Connected comes from Room Manager Profile: {_roomConfigAssetProp.objectReferenceValue.name}",
                        EditorStyles.wordWrappedMiniLabel);
                }

                using (new EditorGUI.DisabledScope(readOnlyAssetValue))
                    EditorGUILayout.PropertyField(connectOnStartProp, ConvaiInspectorContent.StartsConnected);
            });
        }

        private void DrawValidationSection()
        {
            _showValidation = DrawSectionHeader(SectionValidationId, "VALIDATION", _showValidation, "\u2713");
            if (!_showValidation) return;

            DrawSectionBackground(() =>
            {
                SetupHealthReport report = GetSetupHealthReport();
                if (report.HasBlockingIssues)
                {
                    EditorGUILayout.HelpBox(
                        "Some required setup is still missing. Use the checks below to fix the scene.",
                        MessageType.Error);
                }
                else if (report.HasWarnings || HasRoomSpecificIssues())
                {
                    EditorGUILayout.HelpBox(
                        "Setup is mostly ready, but a few non-blocking issues should be reviewed.",
                        MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Scene setup looks healthy.",
                        MessageType.Info);
                }

                foreach (SetupHealthCheckResult result in report.Results)
                {
                    EditorGUILayout.LabelField($"{GetStatusIcon(result.Status)} {result.Title}",
                        EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(result.Message, EditorStyles.wordWrappedMiniLabel);
                    GUILayout.Space(3f);
                }

                DrawRoomSpecificValidationMessages();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Run Full Validation", _miniButtonStyle, GUILayout.Width(160f)))
                {
                    InvalidateValidationCache(true);
                    ConvaiSetupWizard.ValidateSceneSetup();
                }

                if (GUILayout.Button("Open Project Settings", _miniButtonStyle, GUILayout.Width(160f)))
                    SettingsService.OpenProjectSettings("Project/Convai SDK");
                EditorGUILayout.EndHorizontal();
            });
        }

        private void DrawAdvancedSection()
        {
            _showAdvanced = DrawSectionHeader(SectionAdvancedId, "ADVANCED SETTINGS", _showAdvanced, "\u2692");
            if (!_showAdvanced) return;

            DrawConfigurationSection();
            DrawRoomDefaultsSection();
            DrawRuntimeSection();
        }

        private void DrawConfigurationSection()
        {
            _showConfiguration = DrawSectionHeader(SectionConfigurationId, "ROOM SOURCE", _showConfiguration, "\u2699");
            if (!_showConfiguration) return;

            DrawSectionBackground(() =>
            {
                EditorGUILayout.PropertyField(_configurationSourceProp, ConvaiInspectorContent.RoomSetupSource);
                if (IsAssetModeSelected)
                    EditorGUILayout.PropertyField(_roomConfigAssetProp, ConvaiInspectorContent.RoomConfigAsset);

                EditorGUILayout.LabelField(GetRoomSourceStatusText(), EditorStyles.wordWrappedMiniLabel);
                if (IsAssetModeSelected && _roomConfigAssetProp.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox(
                        "Assign a Room Manager Profile asset or switch Room Setup Source back to scene defaults.",
                        MessageType.Warning);
                }
            });
        }

        private void DrawRoomDefaultsSection()
        {
            _showRoomDefaults = DrawSectionHeader(SectionRoomDefaultsId, "ADVANCED ROOM DEFAULTS", _showRoomDefaults,
                "\u21C4");
            if (!_showRoomDefaults) return;

            DrawSectionBackground(() =>
            {
                if (IsAssetModeSelected && _roomConfigAssetProp.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox(
                        "Assign a Room Manager Profile asset to review advanced defaults while Room Setup Source is set to Room Manager Profile Asset.",
                        MessageType.Warning);
                    return;
                }

                if (IsAssetModeSelected)
                    DrawReadOnlyRoomConfigDefaults();
                else
                    DrawInlineRoomDefaults();
            });
        }

        private void DrawRuntimeSection()
        {
            _showRuntime = DrawSectionHeader(SectionRuntimeId, "RUNTIME", _showRuntime, "\u25B6");
            if (!_showRuntime) return;

            DrawSectionBackground(() =>
            {
                string projectServerUrl =
                    ConvaiSettings.Instance != null ? ConvaiSettings.Instance.ServerUrl : string.Empty;
                string legacyOverride = _coreServerBaseUrlProp?.stringValue ?? string.Empty;
                bool hasLegacyOverride = !string.IsNullOrWhiteSpace(legacyOverride);
                string effectiveBaseUrl = hasLegacyOverride ? legacyOverride.Trim() : projectServerUrl;

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("Session State", _roomManager.CurrentState.ToString());
                    EditorGUILayout.Toggle("Connected", _roomManager.IsConnected);
                    EditorGUILayout.TextField("Current Room",
                        string.IsNullOrWhiteSpace(_roomManager.CurrentRoomName)
                            ? "Not connected"
                            : _roomManager.CurrentRoomName);
                    EditorGUILayout.TextField("Session ID",
                        string.IsNullOrWhiteSpace(_roomManager.CurrentSessionId)
                            ? "Not connected"
                            : _roomManager.CurrentSessionId);
                    EditorGUILayout.TextField(
                        "Core Server URL Source",
                        hasLegacyOverride ? "Legacy Override (this component)" : "Project Settings");
                    EditorGUILayout.TextField("Effective Core Server Base URL", effectiveBaseUrl);
                    if (!string.IsNullOrWhiteSpace(_roomManager.RoomControllerTypeName))
                        EditorGUILayout.TextField("Room Controller", _roomManager.RoomControllerTypeName);
                    if (!string.IsNullOrWhiteSpace(_roomManager.TransportAccessorTypeName))
                        EditorGUILayout.TextField("Transport", _roomManager.TransportAccessorTypeName);
                }

                if (hasLegacyOverride)
                {
                    EditorGUILayout.HelpBox(
                        "This component still has a legacy Core Server Base URL override. Move it to Project Settings, then clear the per-scene value.",
                        MessageType.Warning);

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Copy To Project Settings", _miniButtonStyle, GUILayout.Width(170f)))
                        CopyLegacyCoreServerOverrideToProjectSettings();
                    if (GUILayout.Button("Clear Legacy Override", _miniButtonStyle, GUILayout.Width(150f)))
                        ClearLegacyCoreServerOverride();
                    EditorGUILayout.EndHorizontal();
                }

                if (GUILayout.Button("Open Convai SDK Project Settings", _miniButtonStyle, GUILayout.Width(220f)))
                    SettingsService.OpenProjectSettings("Project/Convai SDK");

                EditorGUILayout.PropertyField(_debugProp, ConvaiInspectorContent.Debug);
            });
        }

        private string GetRoomSourceStatusText()
        {
            if (!IsAssetModeSelected)
                return "Using scene defaults.";

            return _roomConfigAssetProp.objectReferenceValue != null
                ? $"Using Room Manager Profile: {_roomConfigAssetProp.objectReferenceValue.name}"
                : "Room Manager Profile not assigned.";
        }

        private void DrawReadOnlyRoomConfigDefaults()
        {
            var roomConfig = _roomConfigAssetProp.objectReferenceValue as ConvaiRoomManagerProfile;
            if (roomConfig == null)
                return;

            var roomConfigSerializedObject = new SerializedObject(roomConfig);
            roomConfigSerializedObject.UpdateIfRequiredOrScript();

            using (new EditorGUI.DisabledScope(true))
            {
                DrawConnectionDefaultsGroup(
                    roomConfigSerializedObject.FindProperty("_connectionType"),
                    roomConfigSerializedObject.FindProperty("_serverEndpoint"));
            }

            DrawTurnTakingDefaultsGroup(
                roomConfigSerializedObject.FindProperty("_turnTakingOptions"),
                _roomPushToTalkKeyProp,
                true);

            using (new EditorGUI.DisabledScope(true))
            {
                DrawReconnectDefaultsGroup(
                    roomConfigSerializedObject.FindProperty("_roomRejoinTtlSeconds"),
                    roomConfigSerializedObject.FindProperty("_resumePolicy"),
                    roomConfigSerializedObject.FindProperty("_maxReconnectAttempts"),
                    roomConfigSerializedObject.FindProperty("_spawnAgentOnRejoin"),
                    roomConfigSerializedObject.FindProperty("_startWaitTimeoutMs"),
                    roomConfigSerializedObject.FindProperty("_autoMicStartDelaySeconds"));
            }
        }

        private void DrawInlineRoomDefaults()
        {
            DrawConnectionDefaultsGroup(
                _connectionTypeProp,
                _serverEndpointProp);
            DrawTurnTakingDefaultsGroup(
                _turnTakingOptionsProp,
                _roomPushToTalkKeyProp,
                false);
            DrawReconnectDefaultsGroup(
                _roomRejoinTtlSecondsProp,
                _resumePolicyProp,
                _maxReconnectAttemptsProp,
                _spawnAgentOnRejoinProp,
                _startWaitTimeoutMsProp,
                _autoMicStartDelaySecondsProp);
        }

        /// <summary>
        ///     Draws the connection defaults section (Connection Type and Server Endpoint).
        ///     Video track name is intentionally not rendered here as it should not be user-editable
        ///     (the backend expects a specific track name format).
        /// </summary>
        private void DrawConnectionDefaultsGroup(
            SerializedProperty connectionTypeProp,
            SerializedProperty serverEndpointProp)
        {
            DrawSubsectionLabel("Connection");
            if (connectionTypeProp != null)
                EditorGUILayout.PropertyField(connectionTypeProp, ConvaiInspectorContent.ConnectionType);

            if (serverEndpointProp != null)
                ConvaiInspectorFieldUtility.DrawServerEndpointField(serverEndpointProp, ConvaiInspectorContent.Server);
            EditorGUILayout.Space(4f);
        }

        private void DrawTurnTakingDefaultsGroup(
            SerializedProperty turnTakingOptionsProp,
            SerializedProperty pushToTalkKeyProp,
            bool usesRoomConfigAsset)
        {
            DrawSubsectionLabel("Turn Taking");
            using (new EditorGUI.DisabledScope(usesRoomConfigAsset))
            {
                if (turnTakingOptionsProp != null)
                    EditorGUILayout.PropertyField(turnTakingOptionsProp, ConvaiInspectorContent.TurnTaking, true);
            }

            if (ShouldShowRoomManagerPushToTalkKey(turnTakingOptionsProp, usesRoomConfigAsset) &&
                pushToTalkKeyProp != null)
                EditorGUILayout.PropertyField(pushToTalkKeyProp, ConvaiInspectorContent.PushToTalkKey);

            EditorGUILayout.Space(4f);
        }

        private bool ShouldShowRoomManagerPushToTalkKey(
            SerializedProperty turnTakingOptionsProp,
            bool usesRoomConfigAsset)
        {
            if (usesRoomConfigAsset)
                return _roomManager != null &&
                       _roomManager.EffectiveTurnTakingOptions.Mode == ConversationInputMode.PushToTalk;

            if (turnTakingOptionsProp == null)
                return false;

            SerializedProperty modeProp = turnTakingOptionsProp.FindPropertyRelative("<Mode>k__BackingField");
            return modeProp != null &&
                   (ConversationInputMode)modeProp.enumValueIndex == ConversationInputMode.PushToTalk;
        }

        private void DrawReconnectDefaultsGroup(
            SerializedProperty roomRejoinTtlSecondsProp,
            SerializedProperty resumePolicyProp,
            SerializedProperty maxReconnectAttemptsProp,
            SerializedProperty spawnAgentOnRejoinProp,
            SerializedProperty startWaitTimeoutMsProp,
            SerializedProperty autoMicStartDelaySecondsProp)
        {
            DrawSubsectionLabel("Reconnect");
            if (roomRejoinTtlSecondsProp != null)
                EditorGUILayout.PropertyField(roomRejoinTtlSecondsProp, ConvaiInspectorContent.RoomRejoinTtlSeconds);
            if (resumePolicyProp != null)
                EditorGUILayout.PropertyField(resumePolicyProp, ConvaiInspectorContent.ResumePolicy);
            if (maxReconnectAttemptsProp != null)
                EditorGUILayout.PropertyField(maxReconnectAttemptsProp, ConvaiInspectorContent.MaxReconnectAttempts);
            if (spawnAgentOnRejoinProp != null)
                EditorGUILayout.PropertyField(spawnAgentOnRejoinProp, ConvaiInspectorContent.SpawnAgentOnRejoin);
            if (startWaitTimeoutMsProp != null)
                EditorGUILayout.PropertyField(startWaitTimeoutMsProp, ConvaiInspectorContent.StartWaitTimeoutMs);
            if (autoMicStartDelaySecondsProp != null)
                EditorGUILayout.PropertyField(autoMicStartDelaySecondsProp,
                    ConvaiInspectorContent.AutoMicStartDelaySeconds);
        }

        private static void DrawSubsectionLabel(string title) =>
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

        private SerializedProperty GetConversationTurnTakingOptionsProperty(out bool readOnlyAssetValues)
        {
            readOnlyAssetValues = false;
            if (!IsAssetModeSelected)
                return _turnTakingOptionsProp;

            if (_roomConfigAssetProp.objectReferenceValue is not ConvaiRoomManagerProfile roomConfig)
                return null;

            SerializedObject roomConfigSerializedObject = GetRoomConfigSerializedObject(roomConfig);
            if (roomConfigSerializedObject == null)
                return null;

            readOnlyAssetValues = true;
            return roomConfigSerializedObject.FindProperty("_turnTakingOptions");
        }

        private SerializedProperty GetConnectOnStartProperty(out bool readOnlyAssetValue)
        {
            readOnlyAssetValue = false;
            if (!IsAssetModeSelected)
                return _connectOnStartProp;

            if (_roomConfigAssetProp.objectReferenceValue is not ConvaiRoomManagerProfile roomConfig)
                return null;

            SerializedObject roomConfigSerializedObject = GetRoomConfigSerializedObject(roomConfig);
            if (roomConfigSerializedObject == null)
                return null;

            readOnlyAssetValue = true;
            return roomConfigSerializedObject.FindProperty("_connectOnStart");
        }

        private SerializedObject GetRoomConfigSerializedObject(ConvaiRoomManagerProfile roomConfig)
        {
            if (roomConfig == null)
            {
                _roomConfigSerializedObject = null;
                return null;
            }

            if (_roomConfigSerializedObject == null || _roomConfigSerializedObject.targetObject != roomConfig)
                _roomConfigSerializedObject = new SerializedObject(roomConfig);

            _roomConfigSerializedObject.UpdateIfRequiredOrScript();
            return _roomConfigSerializedObject;
        }

        private bool HasRoomSpecificIssues()
        {
            if (IsAssetModeSelected && _roomConfigAssetProp.objectReferenceValue == null)
                return true;

            if (HasLegacyCoreServerOverride)
                return true;

            if (_roomManager != null && _roomManager.EffectiveConnectionType == ConvaiConnectionType.Video)
            {
                (bool hasPublisher, bool hasFrameSource) = GetVisionComponentFlags();
                if (!hasPublisher || !hasFrameSource)
                    return true;
            }

            return false;
        }

        private void DrawRoomSpecificValidationMessages()
        {
            bool anyRoomSpecificIssues = false;

            if (IsAssetModeSelected && _roomConfigAssetProp.objectReferenceValue == null)
            {
                EditorGUILayout.LabelField("\u2717 Room Manager Profile", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Room Manager Profile Asset is required while Room Setup Source is set to Room Manager Profile Asset.",
                    EditorStyles.wordWrappedMiniLabel);
                GUILayout.Space(3f);
                anyRoomSpecificIssues = true;
            }

            if (HasLegacyCoreServerOverride)
            {
                EditorGUILayout.LabelField("\u26A0 Legacy Server Override", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "This component still has a legacy Core Server Base URL override. Move it to Project Settings, then clear the per-scene value.",
                    EditorStyles.wordWrappedMiniLabel);
                GUILayout.Space(3f);
                anyRoomSpecificIssues = true;
            }

            if (_roomManager != null && _roomManager.EffectiveConnectionType == ConvaiConnectionType.Video)
            {
                (bool hasPublisher, bool hasFrameSource) = GetVisionComponentFlags();
                if (!hasPublisher || !hasFrameSource)
                {
                    EditorGUILayout.LabelField("\u26A0 Video Requirements", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(
                        $"Video mode is missing required components: {GetMissingVideoRequirementsMessage(hasPublisher, hasFrameSource)}.",
                        EditorStyles.wordWrappedMiniLabel);
                    GUILayout.Space(3f);
                    anyRoomSpecificIssues = true;
                }
            }

            if (!anyRoomSpecificIssues)
                EditorGUILayout.LabelField("No room-specific issues detected.", EditorStyles.wordWrappedMiniLabel);
        }

        private static string GetStatusIcon(SetupHealthStatus status) =>
            status switch
            {
                SetupHealthStatus.Healthy => "\u2713",
                SetupHealthStatus.Warning => "\u26A0",
                _ => "\u2717"
            };

        private SetupHealthReport GetSetupHealthReport()
        {
            Event currentEvent = Event.current;
            bool shouldRefresh =
                _cachedSetupHealthReport == null ||
                (currentEvent != null &&
                 currentEvent.type == EventType.Layout &&
                 EditorApplication.timeSinceStartup >= _nextValidationRefreshTime);

            if (!shouldRefresh)
                return _cachedSetupHealthReport;

            _cachedSetupHealthReport = SetupHealthService.BuildReport();
            _nextValidationRefreshTime = EditorApplication.timeSinceStartup + ValidationRefreshIntervalSeconds;
            return _cachedSetupHealthReport;
        }

        private void InvalidateValidationCache(bool forceImmediateRefresh = false)
        {
            _cachedSetupHealthReport = null;
            _nextValidationRefreshTime = forceImmediateRefresh ? 0d : EditorApplication.timeSinceStartup;
        }

        private (bool hasPublisher, bool hasFrameSource) GetVisionComponentFlags()
        {
            bool hasPublisher = false;
            bool hasFrameSource = false;

            foreach (MonoBehaviour component in _roomManager.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!hasPublisher && component is IVisionPublisher) hasPublisher = true;
                if (!hasFrameSource && component is IVisionFrameSource) hasFrameSource = true;
                if (hasPublisher && hasFrameSource) break;
            }

            return (hasPublisher, hasFrameSource);
        }

        private static string GetMissingVideoRequirementsMessage(bool hasPublisher, bool hasFrameSource)
        {
            if (!hasPublisher && !hasFrameSource)
                return "ConvaiVisionPublisher and a vision frame source";
            if (!hasPublisher)
                return "ConvaiVisionPublisher";
            return "a vision frame source";
        }

        private bool DrawSectionHeader(string sectionId, string title, bool expanded, string icon) =>
            ConvaiLipSyncSectionChrome.DrawHeader(
                new ConvaiSectionHeaderSpec(
                    EditorStateHostId,
                    sectionId,
                    title,
                    icon,
                    ConvaiLipSyncEditorThemeTokens.Accent),
                expanded);

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

        private void CopyLegacyCoreServerOverrideToProjectSettings()
        {
            if (!HasLegacyCoreServerOverride) return;

            var settings = ConvaiSettings.Instance;
            if (settings == null) return;

            Undo.RecordObject(settings, "Copy Convai Server URL To Project Settings");
            settings.SetServerUrl(_coreServerBaseUrlProp.stringValue.Trim());
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        private void ClearLegacyCoreServerOverride()
        {
            if (!HasLegacyCoreServerOverride) return;

            Undo.RecordObject(_roomManager, "Clear Legacy Convai Server URL Override");
            _coreServerBaseUrlProp.stringValue = string.Empty;
            EditorUtility.SetDirty(_roomManager);
            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
        }
    }
}
