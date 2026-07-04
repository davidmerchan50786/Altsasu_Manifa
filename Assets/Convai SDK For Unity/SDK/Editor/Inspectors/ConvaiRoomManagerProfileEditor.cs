using System;
using Convai.Modules.LipSync.Editor.UI;
using Convai.Runtime;
using Convai.Shared.Types;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor.Inspectors
{
    [CustomEditor(typeof(ConvaiRoomManagerProfile))]
    public class ConvaiRoomManagerProfileEditor : UnityEditor.Editor
    {
        private const string EditorStateHostId = "ConvaiRoomManagerProfileEditor";
        private const string SectionRoomDefaultsId = "RoomDefaults";
        private const string SectionReconnectId = "Reconnect";
        private SerializedProperty _autoMicStartDelaySecondsProp;

        private SerializedProperty _connectionTypeProp;
        private SerializedProperty _connectOnStartProp;
        private GUIStyle _headerStyle;
        private Texture2D _icon;
        private SerializedProperty _maxReconnectAttemptsProp;
        private SerializedProperty _resumePolicyProp;
        private SerializedProperty _roomRejoinTtlSecondsProp;
        private SerializedProperty _serverEndpointProp;
        private bool _showReconnect = true;
        private bool _showRoomDefaults = true;
        private SerializedProperty _spawnAgentOnRejoinProp;
        private SerializedProperty _startWaitTimeoutMsProp;
        private GUIStyle _statusStyle;
        private bool _stylesInitialized;
        private SerializedProperty _turnTakingOptionsProp;
        private SerializedProperty _videoTrackNameProp;

        private void OnEnable()
        {
            _icon = ConvaiEditorSettings.Instance.ConvaiIconTexture;
            _connectionTypeProp = serializedObject.FindProperty("_connectionType");
            _videoTrackNameProp = serializedObject.FindProperty("_videoTrackName");
            _serverEndpointProp = serializedObject.FindProperty("_serverEndpoint");
            _connectOnStartProp = serializedObject.FindProperty("_connectOnStart");
            _turnTakingOptionsProp = serializedObject.FindProperty("_turnTakingOptions");
            _roomRejoinTtlSecondsProp = serializedObject.FindProperty("_roomRejoinTtlSeconds");
            _resumePolicyProp = serializedObject.FindProperty("_resumePolicy");
            _maxReconnectAttemptsProp = serializedObject.FindProperty("_maxReconnectAttempts");
            _spawnAgentOnRejoinProp = serializedObject.FindProperty("_spawnAgentOnRejoin");
            _startWaitTimeoutMsProp = serializedObject.FindProperty("_startWaitTimeoutMs");
            _autoMicStartDelaySecondsProp = serializedObject.FindProperty("_autoMicStartDelaySeconds");

            _showRoomDefaults = ConvaiLipSyncSectionStateStore.Get(EditorStateHostId, SectionRoomDefaultsId, true);
            _showReconnect = ConvaiLipSyncSectionStateStore.Get(EditorStateHostId, SectionReconnectId, true);
        }

        private void OnDisable()
        {
            ConvaiLipSyncSectionStateStore.Set(EditorStateHostId, SectionRoomDefaultsId, _showRoomDefaults);
            ConvaiLipSyncSectionStateStore.Set(EditorStateHostId, SectionReconnectId, _showReconnect);
        }

        public override void OnInspectorGUI()
        {
            try
            {
                InitializeStyles();
                serializedObject.Update();

                DrawInspectorHeader();
                EditorGUILayout.Space(6f);

                DrawRoomDefaultsSection();
                DrawReconnectSection();

                serializedObject.ApplyModifiedProperties();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ConvaiRoomManagerProfileEditor] Falling back to default inspector: {ex.Message}");
                serializedObject.UpdateIfRequiredOrScript();
                DrawDefaultInspector();
            }
        }

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
            _stylesInitialized = true;
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
                        Rect iconRect = new(rowRect.x, rowRect.y + ((headerHeight - iconSize) * 0.5f), iconSize,
                            iconSize);
                        GUI.DrawTexture(iconRect, _icon, ScaleMode.ScaleToFit, true);
                    }

                    float textX = rowRect.x + iconSize + 6f;
                    Rect textRect = new(textX, rowRect.y + 11f, rowRect.width - statusRegionWidth - textX, 22f);
                    GUI.Label(textRect, "Convai Room Manager Profile", _headerStyle);

                    Rect statusRect = new(rowRect.xMax - statusRegionWidth, rowRect.y, statusRegionWidth,
                        rowRect.height);
                    GUI.Label(statusRect, "Reusable Asset", _statusStyle);
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

        private void DrawRoomDefaultsSection()
        {
            _showRoomDefaults = DrawSectionHeader(SectionRoomDefaultsId, "ROOM DEFAULTS", _showRoomDefaults, "\u2699");
            if (!_showRoomDefaults) return;

            DrawSectionBackground(() =>
            {
                DrawSubsectionLabel("Connection");
                EditorGUILayout.PropertyField(_connectionTypeProp, ConvaiInspectorContent.ConnectionType);

                if (_connectionTypeProp != null &&
                    (ConvaiConnectionType)_connectionTypeProp.enumValueIndex == ConvaiConnectionType.Video)
                    EditorGUILayout.PropertyField(_videoTrackNameProp, ConvaiInspectorContent.VideoTrackName);

                ConvaiInspectorFieldUtility.DrawServerEndpointField(_serverEndpointProp, ConvaiInspectorContent.Server);
                EditorGUILayout.PropertyField(_connectOnStartProp, ConvaiInspectorContent.ConnectOnStart);

                EditorGUILayout.Space(4f);
                DrawSubsectionLabel("Turn Taking");
                EditorGUILayout.PropertyField(_turnTakingOptionsProp, ConvaiInspectorContent.TurnTaking, true);
            });
        }

        private void DrawReconnectSection()
        {
            _showReconnect = DrawSectionHeader(SectionReconnectId, "RECONNECT", _showReconnect, "\u21C4");
            if (!_showReconnect) return;

            DrawSectionBackground(() =>
            {
                EditorGUILayout.PropertyField(_roomRejoinTtlSecondsProp, ConvaiInspectorContent.RoomRejoinTtlSeconds);
                EditorGUILayout.PropertyField(_resumePolicyProp, ConvaiInspectorContent.ResumePolicy);
                EditorGUILayout.PropertyField(_maxReconnectAttemptsProp, ConvaiInspectorContent.MaxReconnectAttempts);
                EditorGUILayout.PropertyField(_spawnAgentOnRejoinProp, ConvaiInspectorContent.SpawnAgentOnRejoin);
                EditorGUILayout.PropertyField(_startWaitTimeoutMsProp, ConvaiInspectorContent.StartWaitTimeoutMs);
                EditorGUILayout.PropertyField(_autoMicStartDelaySecondsProp,
                    ConvaiInspectorContent.AutoMicStartDelaySeconds);
            });
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

        private bool DrawSectionHeader(string sectionId, string title, bool expanded, string icon) =>
            ConvaiLipSyncSectionChrome.DrawHeader(
                new ConvaiSectionHeaderSpec(
                    EditorStateHostId,
                    sectionId,
                    title,
                    icon,
                    ConvaiLipSyncEditorThemeTokens.Accent),
                expanded);

        private static void DrawSubsectionLabel(string title) =>
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }
}
