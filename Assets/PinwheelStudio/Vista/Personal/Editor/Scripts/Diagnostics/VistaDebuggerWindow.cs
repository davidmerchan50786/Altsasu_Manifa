#if VISTA
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Pinwheel.VistaEditor;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Pinwheel.Vista.Diagnostics
{
    public class VistaDebuggerWindow : EditorWindow
    {
        private static string SessionRoot => Path.Combine(Application.persistentDataPath, "VistaDebug");
        private const string EXTERNAL_SESSION_PATHS_PREF_KEY = "Pinwheel.Vista.Diagnostics.ExternalSessionPaths";

        private const float SPLITTER_WIDTH = 1f;
        private const float SESSION_LIST_WIDTH = 220f;
        private static readonly Color32 SPLITTER_COLOR = new Color32(35, 35, 35, 255);
        private static readonly Color32 SELECTED_COLOR = new Color32(44, 93, 135, 255);

        private const float SPLIT_RATIO_MIN = 0.1f;
        private const float SPLIT_RATIO_MAX = 0.9f;

        private static readonly int POSITION_SAMPLE_BYTES = PositionSample.SIZE * sizeof(float);
        private static readonly int INSTANCE_SAMPLE_BYTES = InstanceSample.SIZE * sizeof(float);

        private static readonly int DOTS_POSITIONS = Shader.PropertyToID("_Positions");
        private static readonly int DOTS_TARGET_RT = Shader.PropertyToID("_TargetRT");
        private static readonly int DOTS_RESOLUTION = Shader.PropertyToID("_Resolution");
        private static readonly int DOTS_BASE_INDEX = Shader.PropertyToID("_BaseIndex");
        private const int DOTS_THREAD_PER_GROUP = 8;
        private const int DOTS_MAX_THREAD_GROUP = 64000 / DOTS_THREAD_PER_GROUP;
        private const string KW_DATA_TYPE_POSITION_SAMPLE = "DATA_TYPE_POSITION_SAMPLE";
        private const string KW_DATA_TYPE_INSTANCE_SAMPLE = "DATA_TYPE_INSTANCE_SAMPLE";

        private static class Styles
        {
            private static readonly Color32 DotColorMask = new Color32(132, 228, 231, 255);
            private static readonly Color32 DotColorTexture = new Color32(251, 203, 244, 255);
            private static readonly Color32 DotColorBuffer = new Color32(246, 255, 154, 255);
            private static readonly Color32 DotColorLog = new Color32(180, 180, 180, 255);

            private static readonly Color32 LogColorInfo = new Color32(210, 210, 210, 255);
            private static readonly Color32 LogColorWarning = new Color32(255, 185, 55, 255);
            private static readonly Color32 LogColorError = new Color32(225, 75, 65, 255);

            private static GUIStyle s_dotMask;
            private static GUIStyle s_dotTexture;
            private static GUIStyle s_dotBuffer;
            private static GUIStyle s_dotLog;
            private static GUIStyle s_foldoutInfo;
            private static GUIStyle s_foldoutWarning;
            private static GUIStyle s_foldoutError;
            private static GUIStyle s_remainderInfo;
            private static GUIStyle s_remainderWarning;
            private static GUIStyle s_remainderError;

            private static GUIStyle DotMask => s_dotMask ??= CreateDotStyle(DotColorMask);
            private static GUIStyle DotTexture => s_dotTexture ??= CreateDotStyle(DotColorTexture);
            private static GUIStyle DotBuffer => s_dotBuffer ??= CreateDotStyle(DotColorBuffer);
            private static GUIStyle DotLog => s_dotLog ??= CreateDotStyle(DotColorLog);

            private static GUIStyle FoldoutInfo => s_foldoutInfo ??= CreateFoldoutStyle(LogColorInfo);
            private static GUIStyle FoldoutWarning => s_foldoutWarning ??= CreateFoldoutStyle(LogColorWarning);
            private static GUIStyle FoldoutError => s_foldoutError ??= CreateFoldoutStyle(LogColorError);

            private static GUIStyle RemainderInfo => s_remainderInfo ??= CreateRemainderStyle(LogColorInfo);
            private static GUIStyle RemainderWarning => s_remainderWarning ??= CreateRemainderStyle(LogColorWarning);
            private static GUIStyle RemainderError => s_remainderError ??= CreateRemainderStyle(LogColorError);

            public static GUIStyle GetDotStyle(DebugEvent debugEvent)
            {
                if (debugEvent.type == DebugEventType.BufferCapture) return DotBuffer;
                if (debugEvent.type == DebugEventType.Log || debugEvent.type == DebugEventType.StringCapture) return DotLog;
                string primaryTextureFormat = GetPrimaryTextureFormat(debugEvent);
                if (!string.IsNullOrEmpty(primaryTextureFormat) && (primaryTextureFormat.Contains("Float") || primaryTextureFormat.Contains("Half")))
                    return DotMask;
                return DotTexture;
            }

            public static GUIStyle GetFoldoutStyle(LogType logType)
            {
                switch (logType)
                {
                    case LogType.Warning: return FoldoutWarning;
                    case LogType.Error:
                    case LogType.Exception:
                    case LogType.Assert: return FoldoutError;
                    default: return FoldoutInfo;
                }
            }

            public static GUIStyle GetRemainderStyle(LogType logType)
            {
                switch (logType)
                {
                    case LogType.Warning: return RemainderWarning;
                    case LogType.Error:
                    case LogType.Exception:
                    case LogType.Assert: return RemainderError;
                    default: return RemainderInfo;
                }
            }

            private static GUIStyle CreateDotStyle(Color32 color)
            {
                GUIStyle style = new GUIStyle(EditorStyles.label);
                style.normal.textColor = color;
                return style;
            }

            private static GUIStyle CreateFoldoutStyle(Color32 color)
            {
                GUIStyle style = new GUIStyle(EditorStyles.foldout);
                style.normal.textColor = color;
                style.onNormal.textColor = color;
                style.focused.textColor = color;
                style.onFocused.textColor = color;
                style.active.textColor = color;
                style.onActive.textColor = color;
                return style;
            }

            private static GUIStyle CreateRemainderStyle(Color32 color)
            {
                GUIStyle style = new GUIStyle(EditorStyles.label);
                style.wordWrap = true;
                style.normal.textColor = color;
                return style;
            }
        }

        private float m_splitRatio = 0.4f;
        private bool m_isDraggingSplitter;
        private System.Action m_pendingAction;

        [System.Serializable]
        private class ExternalSessionPathStore
        {
            public List<string> paths = new List<string>();
        }

        private class SessionListEntry
        {
            public string directory;
            public bool isExternal;
        }

        private List<SessionListEntry> m_sessionEntries = new List<SessionListEntry>();
        private Dictionary<string, DebugSession> m_sessionCache = new Dictionary<string, DebugSession>();
        private Dictionary<string, string> m_sessionLoadErrors = new Dictionary<string, string>();
        private string m_selectedSessionDirectory;
        private DebugSession m_loadedSession;
        private string m_loadedSessionError;
        private Vector2 m_sessionListScroll;

        private class EventTreeItem : TreeViewItem
        {
            public DebugEvent debugEvent;
            public List<DebugEvent> captures = new List<DebugEvent>();
        }

        private class EventTreeView : TreeView
        {
            private List<DebugEvent> m_events;
            public System.Action<EventTreeItem> onSelectionChanged;

            public EventTreeView(TreeViewState state) : base(state) { }

            public void SetEvents(List<DebugEvent> events)
            {
                m_events = events;
                Reload();
            }

            protected override TreeViewItem BuildRoot()
            {
                EventTreeItem root = new EventTreeItem { id = -1, depth = -1 };
                List<TreeViewItem> rows = new List<TreeViewItem>();

                if (m_events != null)
                {
                    int id = 0;
                    Stack<EventTreeItem> scopeStack = new Stack<EventTreeItem>();

                    foreach (DebugEvent debugEvent in m_events)
                    {
                        if (debugEvent.type == DebugEventType.ScopeEnd)
                        {
                            if (scopeStack.Count > 0)
                                scopeStack.Pop();
                            continue;
                        }

                        bool isCapture = debugEvent.type == DebugEventType.TextureCapture
                            || debugEvent.type == DebugEventType.BufferCapture
                            || debugEvent.type == DebugEventType.Log
                            || debugEvent.type == DebugEventType.StringCapture;

                        if (isCapture)
                        {
                            if (scopeStack.Count > 0)
                                scopeStack.Peek().captures.Add(debugEvent);
                            continue;
                        }

                        bool isScope = debugEvent.type == DebugEventType.ScopeBegin;
                        EventTreeItem item = new EventTreeItem
                        {
                            id = id++,
                            depth = scopeStack.Count,
                            displayName = GetItemLabel(debugEvent),
                            debugEvent = debugEvent
                        };
                        rows.Add(item);

                        if (isScope)
                            scopeStack.Push(item);
                    }
                }

                SetupParentsAndChildrenFromDepths(root, rows);
                return root;
            }

            private static string GetItemLabel(DebugEvent debugEvent)
            {
                switch (debugEvent.type)
                {
                    case DebugEventType.ScopeBegin: return debugEvent.label;
                    case DebugEventType.Log:
                    case DebugEventType.StringCapture:
                        string msg = debugEvent.message;
                        return msg?.Length > 48 ? msg.Substring(0, 48) + "..." : msg;
                    default: return debugEvent.type.ToString();
                }
            }

            protected override void SelectionChanged(IList<int> selectedIds)
            {
                if (selectedIds.Count == 0)
                {
                    onSelectionChanged?.Invoke(null);
                    return;
                }
                EventTreeItem item = FindItem(selectedIds[0], rootItem) as EventTreeItem;
                onSelectionChanged?.Invoke(item);
            }
        }

        private TreeViewState m_treeViewState;
        private EventTreeView m_eventTreeView;
        private EventTreeItem m_selectedItem;
        private Vector2 m_detailScroll;

        private Dictionary<int, Texture2D> m_textureCache = new Dictionary<int, Texture2D>();
        private Dictionary<int, RenderTexture> m_bufferRTCache = new Dictionary<int, RenderTexture>();
        private Dictionary<int, string> m_textureLoadErrors = new Dictionary<int, string>();
        private Dictionary<int, string> m_bufferLoadErrors = new Dictionary<int, string>();
        private Dictionary<DebugEvent, bool> m_logFoldouts = new Dictionary<DebugEvent, bool>();
        private Dictionary<DebugEvent, Vector2> m_textureArrayScrolls = new Dictionary<DebugEvent, Vector2>();
        private int m_detailItemHash = -1;

        [MenuItem("Window/Vista/Diagnostics/Session Viewer")]
        private static void Open()
        {
            VistaDebuggerWindow window = GetWindow<VistaDebuggerWindow>();
            window.titleContent = new GUIContent("Vista Diagnostics");
            window.Show();
        }

        private void OnEnable()
        {
            RefreshSessionList();
        }

        private void OnDisable()
        {
            ClearTextureCache();
        }

        private void OnGUI()
        {
            DrawToolbar();
            float toolbarHeight = EditorStyles.toolbar.fixedHeight;
            Rect bodyRect = new Rect(0, toolbarHeight, position.width, position.height - toolbarHeight);
            DrawBody(bodyRect);

            if (Event.current.type == EventType.Repaint && m_pendingAction != null)
            {
                System.Action action = m_pendingAction;
                m_pendingAction = null;
                action();
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (VistaDebugger.isRecording)
            {
                Color previousColor = GUI.color;
                GUI.color = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("End Session", EditorStyles.toolbarButton, GUILayout.Width(90)))
                {
                    string directory = VistaDebugger.EndSession();
                    if (!string.IsNullOrEmpty(directory))
                    {
                        RefreshSessionList();
                        m_selectedSessionDirectory = directory;
                        LoadSession(directory);
                    }
                }
                GUI.color = previousColor;

                GUILayout.Label("Recording", EditorStyles.toolbarButton);
            }
            else
            {
                if (GUILayout.Button("Begin Session", EditorStyles.toolbarButton, GUILayout.Width(100)))
                {
                    VistaManager vistaManager = Object.FindFirstObjectByType<VistaManager>();
                    if (vistaManager != null)
                    {
                        EditorCoroutineUtility.StartCoroutine(RecordAndGenerate(vistaManager), this);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Vista Diagnostics", "No VistaManager found in the scene.", "OK");
                    }
                }
            }

            GUILayout.Space(8f);
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                RefreshSessionList();
            }
            if (GUILayout.Button("Import Session", EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                ImportExternalSession();
            }
            if (GUILayout.Button("Open Folder", EditorStyles.toolbarButton, GUILayout.Width(90)))
            {
                if (Directory.Exists(SessionRoot))
                {
                    EditorUtility.RevealInFinder(SessionRoot);
                }
                else
                {
                    EditorUtility.DisplayDialog("Vista Diagnostics", "No sessions recorded yet.", "OK");
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawBody(Rect bodyRect)
        {
            float movableSpace = bodyRect.width - SESSION_LIST_WIDTH - 2f * SPLITTER_WIDTH;
            movableSpace = Mathf.Max(movableSpace, 0f);

            float col2Width = movableSpace * m_splitRatio;
            float col3Width = movableSpace * (1f - m_splitRatio);

            Rect col1Rect = new Rect(bodyRect.x, bodyRect.y, SESSION_LIST_WIDTH, bodyRect.height);
            Rect splitter1Rect = new Rect(col1Rect.xMax, bodyRect.y, SPLITTER_WIDTH, bodyRect.height);
            Rect col2Rect = new Rect(splitter1Rect.xMax, bodyRect.y, col2Width, bodyRect.height);
            Rect splitter2Rect = new Rect(col2Rect.xMax, bodyRect.y, SPLITTER_WIDTH, bodyRect.height);
            Rect col3Rect = new Rect(splitter2Rect.xMax, bodyRect.y, col3Width, bodyRect.height);

            HandleSplitter2(splitter2Rect, movableSpace, col2Rect.x);

            EditorGUI.DrawRect(splitter1Rect, SPLITTER_COLOR);
            EditorGUI.DrawRect(splitter2Rect, SPLITTER_COLOR);

            GUILayout.BeginArea(col1Rect);
            DrawSessionList();
            GUILayout.EndArea();

            if (m_eventTreeView != null)
            {
                m_eventTreeView.OnGUI(col2Rect);
            }
            else
            {
                GUILayout.BeginArea(col2Rect);
                GUILayout.Label("Select a session.", EditorStyles.centeredGreyMiniLabel);
                GUILayout.EndArea();
            }

            GUILayout.BeginArea(col3Rect);
            DrawDetail();
            GUILayout.EndArea();
        }

        private IEnumerator RecordAndGenerate(VistaManager vistaManager)
        {
            VistaDebugger.BeginSession();
            ProgressiveTask task = vistaManager.ForceGenerate();
            yield return task;
            string directory = VistaDebugger.EndSession();
            if (!string.IsNullOrEmpty(directory))
            {
                RefreshSessionList();
                m_selectedSessionDirectory = directory;
                LoadSession(directory);
                Repaint();
            }
        }

        private void RefreshSessionList()
        {
            m_sessionCache.Clear();
            m_sessionLoadErrors.Clear();
            m_sessionEntries.Clear();

            HashSet<string> seenDirectories = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(SessionRoot))
            {
                List<string> localDirectories = new List<string>();
                foreach (string directory in Directory.GetDirectories(SessionRoot))
                {
                    if (File.Exists(Path.Combine(directory, "session.json")))
                    {
                        localDirectories.Add(directory);
                    }
                }

                localDirectories.Sort((first, second) => string.Compare(second, first, System.StringComparison.Ordinal));
                foreach (string directory in localDirectories)
                {
                    if (seenDirectories.Add(directory))
                    {
                        m_sessionEntries.Add(new SessionListEntry
                        {
                            directory = directory,
                            isExternal = false
                        });
                    }
                }
            }

            List<string> externalDirectories = LoadExternalSessionDirectories();
            foreach (string directory in externalDirectories)
            {
                if (string.IsNullOrEmpty(directory) || !seenDirectories.Add(directory))
                {
                    continue;
                }

                m_sessionEntries.Add(new SessionListEntry
                {
                    directory = directory,
                    isExternal = true
                });
            }
        }

        private void DrawSessionList()
        {
            m_sessionListScroll = GUILayout.BeginScrollView(m_sessionListScroll);
            EditorGUILayout.BeginVertical(EditorStyles.inspectorFullWidthMargins);
            foreach (SessionListEntry entry in m_sessionEntries)
            {
                string directory = entry.directory;
                string folderName = Path.GetFileName(directory);
                DebugSession session = GetOrLoadSession(directory);
                string sessionSummary = GetSessionSummary(directory, session);
                bool isSelected = directory == m_selectedSessionDirectory;

                Rect entryRect = EditorGUILayout.BeginVertical();
                if (isSelected)
                {
                    EditorGUI.DrawRect(entryRect, SELECTED_COLOR);
                }
                GUILayout.Label(folderName, EditorCommon.Styles.p1, GUILayout.ExpandWidth(true));
                GUILayout.Label(sessionSummary, EditorCommon.Styles.p2, GUILayout.ExpandWidth(true));
                EditorGUILayout.EndVertical();
                GUILayout.Space(4f);

                if (entryRect.width > 0 && Event.current.type == EventType.MouseDown && entryRect.Contains(Event.current.mousePosition))
                {
                    if (Event.current.button == 0)
                    {
                        m_selectedSessionDirectory = directory;
                        LoadSession(directory);
                        Event.current.Use();
                    }
                    else if (Event.current.button == 1)
                    {
                        m_selectedSessionDirectory = directory;
                        string capturedDirectory = directory;
                        Event.current.Use();
                        m_pendingAction = () =>
                        {
                            GenericMenu menu = new GenericMenu();
                            if (entry.isExternal)
                            {
                                menu.AddItem(new GUIContent("Remove"), false, () => RemoveExternalSession(capturedDirectory));
                            }
                            else
                            {
                                menu.AddItem(new GUIContent("Delete"), false, () => ConfirmDeleteSession(capturedDirectory));
                            }
                            menu.ShowAsContext();
                        };
                        Repaint();
                    }
                }
            }
            EditorGUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        private void LoadSession(string directory)
        {
            ClearTextureCache();
            m_detailItemHash = -1;
            m_selectedItem = null;
            m_loadedSession = GetOrLoadSession(directory);
            m_loadedSessionError = GetSessionLoadError(directory);

            m_treeViewState = new TreeViewState();
            m_eventTreeView = new EventTreeView(m_treeViewState);
            m_eventTreeView.onSelectionChanged = OnTreeSelectionChanged;
            m_eventTreeView.SetEvents(m_loadedSession?.events);
        }

        private void OnTreeSelectionChanged(EventTreeItem item)
        {
            m_selectedItem = item;
            int hash = item != null ? item.GetHashCode() : -1;
            if (hash != m_detailItemHash)
            {
                ClearTextureCache();
                m_logFoldouts.Clear();
                m_textureArrayScrolls.Clear();
                m_detailItemHash = hash;
            }
            Repaint();
        }

        private DebugSession GetOrLoadSession(string directory)
        {
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            if (!m_sessionCache.TryGetValue(directory, out DebugSession session))
            {
                session = TryLoadSession(directory);
                m_sessionCache[directory] = session;
            }

            return session;
        }

        private string GetSessionSummary(string directory, DebugSession session)
        {
            if (session == null)
            {
                return GetSessionLoadSummary(directory);
            }

            if (session?.hardware == null)
            {
                return "Session data unavailable";
            }

            string platform = GetPlatformLabel(session.hardware.operatingSystem);
            string graphicsApi = GetGraphicsApiLabel(session.hardware.graphicsDeviceType);

            if (!string.IsNullOrEmpty(platform) && !string.IsNullOrEmpty(graphicsApi))
            {
                return $"{platform} | {graphicsApi}";
            }

            if (!string.IsNullOrEmpty(platform))
            {
                return platform;
            }

            if (!string.IsNullOrEmpty(graphicsApi))
            {
                return graphicsApi;
            }

            return "Session data unavailable";
        }

        private static string GetPlatformLabel(string operatingSystem)
        {
            if (string.IsNullOrEmpty(operatingSystem))
            {
                return null;
            }

            if (operatingSystem.StartsWith("Windows", System.StringComparison.OrdinalIgnoreCase))
                return "Windows";
            if (operatingSystem.StartsWith("Android", System.StringComparison.OrdinalIgnoreCase))
                return "Android";
            if (operatingSystem.StartsWith("iPhone", System.StringComparison.OrdinalIgnoreCase)
                || operatingSystem.StartsWith("iOS", System.StringComparison.OrdinalIgnoreCase))
                return "iOS";
            if (operatingSystem.StartsWith("Mac", System.StringComparison.OrdinalIgnoreCase)
                || operatingSystem.StartsWith("macOS", System.StringComparison.OrdinalIgnoreCase)
                || operatingSystem.StartsWith("OS X", System.StringComparison.OrdinalIgnoreCase))
                return "macOS";
            if (operatingSystem.StartsWith("Linux", System.StringComparison.OrdinalIgnoreCase))
                return "Linux";

            return operatingSystem;
        }

        private static string GetGraphicsApiLabel(string graphicsDeviceType)
        {
            if (string.IsNullOrEmpty(graphicsDeviceType))
            {
                return null;
            }

            switch (graphicsDeviceType)
            {
                case "Direct3D11":
                    return "DX11";
                case "Direct3D12":
                    return "DX12";
                case "OpenGLES2":
                    return "GLES2";
                case "OpenGLES3":
                    return "GLES3";
                case "OpenGLCore":
                    return "OpenGL";
                case "Vulkan":
                    return "Vulkan";
                case "Metal":
                    return "Metal";
                default:
                    return graphicsDeviceType;
            }
        }

        private void DrawSessionInfo(DebugSession session)
        {
            string sessionName = !string.IsNullOrEmpty(m_selectedSessionDirectory)
                ? Path.GetFileName(m_selectedSessionDirectory)
                : "Session";

            EditorGUILayout.LabelField(sessionName, EditorCommon.Styles.h2);
            EditorGUILayout.LabelField("Session Info", EditorCommon.Styles.p2);
            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("General", EditorCommon.Styles.h3);
            EditorGUILayout.LabelField($"Start Time: {session.startTime}", EditorCommon.Styles.p1);
            EditorGUILayout.LabelField($"Session ID: {session.sessionId}", EditorCommon.Styles.p1);
            EditorGUILayout.LabelField($"Events: {session.events?.Count ?? 0}", EditorCommon.Styles.p1);
            EditorGUILayout.Space(8);

            HardwareSnapshot hardware = session.hardware;
            if (hardware != null)
            {
                EditorGUILayout.LabelField("Graphics", EditorCommon.Styles.h3);
                EditorGUILayout.LabelField($"Device: {hardware.graphicsDeviceName}", EditorCommon.Styles.p1);
                EditorGUILayout.LabelField($"Type: {hardware.graphicsDeviceType}", EditorCommon.Styles.p1);
                EditorGUILayout.LabelField($"Vendor: {hardware.graphicsDeviceVendor}", EditorCommon.Styles.p1);
                EditorGUILayout.LabelField($"Version: {hardware.graphicsDeviceVersion}", EditorCommon.Styles.p1);
                EditorGUILayout.LabelField($"Memory: {hardware.graphicsMemorySizeMb} MB", EditorCommon.Styles.p1);
                EditorGUILayout.LabelField($"Shader Level: {hardware.graphicsShaderLevel}", EditorCommon.Styles.p1);
                EditorGUILayout.LabelField($"Multi-threaded: {hardware.graphicsMultiThreaded}", EditorCommon.Styles.p1);
                EditorGUILayout.Space(8);

                EditorGUILayout.LabelField("System", EditorCommon.Styles.h3);
                EditorGUILayout.LabelField($"OS: {hardware.operatingSystem}", EditorCommon.Styles.p1);
                EditorGUILayout.LabelField($"CPU: {hardware.processorType}", EditorCommon.Styles.p1);
                EditorGUILayout.LabelField($"Cores: {hardware.processorCount}", EditorCommon.Styles.p1);
                EditorGUILayout.LabelField($"Memory: {hardware.systemMemorySizeMb} MB", EditorCommon.Styles.p1);
                EditorGUILayout.LabelField($"Unity: {hardware.unityVersion}", EditorCommon.Styles.p1);
            }
            else
            {
                EditorGUILayout.LabelField("No hardware snapshot recorded.", EditorCommon.Styles.p1);
            }

            if (!string.IsNullOrEmpty(m_selectedSessionDirectory))
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Path", EditorCommon.Styles.h3);
                EditorGUILayout.LabelField(m_selectedSessionDirectory, EditorCommon.Styles.p1);
            }

            if (session.seeds != null && session.seeds.Count > 0)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Biome Seeds", EditorCommon.Styles.h3);
                for (int i = 0; i < session.seeds.Count; ++i)
                {
                    SeedSnapshot seedSnapshot = session.seeds[i];
                    EditorGUILayout.LabelField($"{seedSnapshot.label}: {seedSnapshot.seed}", EditorCommon.Styles.p1);
                }
            }
        }

        private void ConfirmDeleteSession(string directory)
        {
            string folderName = Path.GetFileName(directory);
            bool confirmed = EditorUtility.DisplayDialog(
                "Delete Session",
                $"Delete session '{folderName}'? This cannot be undone.",
                "Delete", "Cancel");
            if (confirmed)
            {
                Directory.Delete(directory, recursive: true);
                if (m_selectedSessionDirectory == directory)
                {
                    ClearTextureCache();
                    m_detailItemHash = -1;
                    m_selectedSessionDirectory = null;
                    m_loadedSession = null;
                    m_loadedSessionError = null;
                    m_eventTreeView = null;
                    m_treeViewState = null;
                    m_selectedItem = null;
                }
                RefreshSessionList();
            }
        }

        private void DrawDetail()
        {
            if (m_loadedSession == null)
            {
                if (!string.IsNullOrEmpty(m_selectedSessionDirectory))
                {
                    DrawSessionLoadError();
                }
                else
                {
                    GUILayout.Label("Select a session.", EditorStyles.centeredGreyMiniLabel);
                }
                return;
            }

            m_detailScroll = GUILayout.BeginScrollView(m_detailScroll);
            EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);

            if (m_selectedItem == null)
            {
                DrawSessionInfo(m_loadedSession);
            }
            else
            {
                DebugEvent debugEvent = m_selectedItem.debugEvent;
                bool isInstanceSampleNode = IsInstanceSampleNode(debugEvent.label);
                EditorGUILayout.LabelField(debugEvent.label, EditorCommon.Styles.h2);
                EditorGUILayout.Space(8);

                if (m_selectedItem.captures.Count > 0)
                {
                    foreach (DebugEvent capture in m_selectedItem.captures)
                    {
                        if (capture.type == DebugEventType.Log)
                            DrawLogRow(capture);
                        else if (capture.type == DebugEventType.StringCapture)
                            DrawStringCapture(capture);
                        else
                        {
                            bool useInstanceSampleFallback = isInstanceSampleNode
                                && capture.label.Contains("(Output)");
                            DrawCaptureRow(capture, useInstanceSampleFallback);
                        }
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("None", EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        private static bool IsInstanceSampleNode(string scopeLabel)
        {
            return scopeLabel.Contains("TreeOutputNode")
                || scopeLabel.Contains("ObjectOutputNode")
                || scopeLabel.Contains("DetailInstanceOutputNode");
        }

        private void DrawCaptureRow(DebugEvent capture, bool isInstanceSampleFallback)
        {
            if (capture.type == DebugEventType.TextureCapture && GetTextureCaptures(capture).Count > 1)
            {
                DrawTextureArrayCapture(capture);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("*", Styles.GetDotStyle(capture), GUILayout.Width(12f));
            EditorGUILayout.LabelField(capture.label, EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(GUILayout.Height(128f));

            if (capture.type == DebugEventType.TextureCapture)
            {
                Texture2D texture = GetOrLoadTexture(capture);
                if (texture != null)
                {
                    Rect texRect = GUILayoutUtility.GetRect(128f, 128f, GUILayout.Width(128f), GUILayout.Height(128f));
                    EditorGUI.DrawPreviewTexture(texRect, texture);
                }
                else
                {
                    GUILayout.Box(GUIContent.none, GUILayout.Width(128f), GUILayout.Height(128f));
                }
            }
            else
            {
                DebugBufferInterpretation bufferInterpretation = GetBufferInterpretation(capture, isInstanceSampleFallback);
                RenderTexture bufferRT = bufferInterpretation != DebugBufferInterpretation.Unknown
                    ? GetOrVisualizeBuffer(capture, bufferInterpretation)
                    : null;
                Rect canvasRect = GUILayoutUtility.GetRect(128f, 128f, GUILayout.Width(128f), GUILayout.Height(128f));
                if (bufferRT != null)
                {
                    EditorGUI.DrawPreviewTexture(canvasRect, bufferRT);
                }
                else
                {
                    EditorGUI.DrawRect(canvasRect, Color.black);
                }
            }

            GUILayout.Space(8f);

            EditorGUILayout.BeginVertical();
            if (capture.type == DebugEventType.TextureCapture)
            {
                DebugTextureCapture textureCapture = GetPrimaryTextureCapture(capture);
                if (textureCapture != null)
                {
                    EditorGUILayout.LabelField($"Size: {textureCapture.originalWidth} x {textureCapture.originalHeight}", EditorCommon.Styles.p2);
                    EditorGUILayout.LabelField($"RT Format: {textureCapture.format}", EditorCommon.Styles.p2);
                    string textureLoadError = GetTextureLoadError(textureCapture);
                    if (!string.IsNullOrEmpty(textureLoadError))
                    {
                        EditorGUILayout.HelpBox(textureLoadError, MessageType.Warning);
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("Null", EditorCommon.Styles.p2);
                }
            }
            else
            {
                DebugBufferInterpretation bufferInterpretation = GetBufferInterpretation(capture, isInstanceSampleFallback);
                int totalBytes = capture.elementCount * capture.stride;
                EditorGUILayout.LabelField($"Count: {capture.elementCount.ToString()}", EditorCommon.Styles.p2);
                EditorGUILayout.LabelField($"Stride: {capture.stride} bytes", EditorCommon.Styles.p2);
                EditorGUILayout.LabelField($"Mem Size: {totalBytes} bytes", EditorCommon.Styles.p2);
                EditorGUILayout.LabelField($"Interpretation: {bufferInterpretation}", EditorCommon.Styles.p2);
                if (bufferInterpretation != DebugBufferInterpretation.Unknown)
                {
                    bool isInstanceSample = bufferInterpretation == DebugBufferInterpretation.InstanceSample;
                    int sampleBytes = isInstanceSample ? INSTANCE_SAMPLE_BYTES : POSITION_SAMPLE_BYTES;
                    int sampleCount = totalBytes / sampleBytes;
                    string sampleLabel = isInstanceSample ? "Instance Samples" : "Position Samples";
                    EditorGUILayout.LabelField($"{sampleLabel}: {sampleCount.ToString()}", EditorCommon.Styles.p2);
                    if (sampleCount % 8 != 0)
                    {
                        EditorGUILayout.HelpBox($"Sample count {sampleCount} is not a multiple of 8.", MessageType.Warning);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("Buffer interpretation is unknown. Preview is disabled for this capture.", MessageType.Info);
                }

                string bufferLoadError = GetBufferLoadError(capture);
                if (!string.IsNullOrEmpty(bufferLoadError))
                {
                    EditorGUILayout.HelpBox(bufferLoadError, MessageType.Warning);
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6f);
        }

        private void DrawTextureArrayCapture(DebugEvent capture)
        {
            List<DebugTextureCapture> textureCaptures = GetTextureCaptures(capture);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("*", Styles.GetDotStyle(capture), GUILayout.Width(12f));
            EditorGUILayout.LabelField(capture.label, EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            if (!m_textureArrayScrolls.TryGetValue(capture, out Vector2 scroll))
            {
                scroll = Vector2.zero;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll, false, false, GUILayout.Height(180));
            m_textureArrayScrolls[capture] = scroll;

            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < textureCaptures.Count; ++i)
            {
                DebugTextureCapture textureCapture = textureCaptures[i];
                EditorGUILayout.BeginVertical(GUILayout.Width(128));
                EditorGUILayout.LabelField($"[{i}]", EditorStyles.miniBoldLabel, GUILayout.Width(128));

                Rect texRect = EditorGUILayout.GetControlRect(GUILayout.Width(128), GUILayout.Height(128));
                Texture2D texture = GetOrLoadTexture(textureCapture);
                if (texture != null)
                {
                    EditorGUI.DrawPreviewTexture(texRect, texture);
                    EditorGUILayout.LabelField($"{textureCapture.originalWidth} x {textureCapture.originalHeight} | {textureCapture.format}", EditorCommon.Styles.p2, GUILayout.Width(128));
                }
                else
                {
                    EditorGUI.DrawRect(texRect, Color.black);
                    EditorGUILayout.LabelField("Null", EditorCommon.Styles.p2);
                }

                string textureLoadError = GetTextureLoadError(textureCapture);
                if (!string.IsNullOrEmpty(textureLoadError))
                {
                    EditorGUILayout.HelpBox(textureLoadError, MessageType.Warning);
                }

                EditorGUILayout.EndVertical();
                GUILayout.Space(8f);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space(6f);
        }

        private static void SplitLogMessage(string message, int maxLength, out string head, out string tail)
        {
            if (message.Length <= maxLength)
            {
                head = message;
                tail = null;
                return;
            }

            int cutIndex = maxLength;
            while (cutIndex > 0 && message[cutIndex] != ' ')
                cutIndex--;

            if (cutIndex == 0)
                cutIndex = maxLength;

            head = message.Substring(0, cutIndex).TrimEnd() + "...";
            tail = "..." + message.Substring(cutIndex).TrimStart();
        }

        private void DrawLogRow(DebugEvent logEvent)
        {
            string message = logEvent.message ?? string.Empty;
            bool isStringCapture = logEvent.type == DebugEventType.StringCapture;

            SplitLogMessage(message, 100, out string foldoutLabel, out string remainder);

            if (!m_logFoldouts.TryGetValue(logEvent, out bool expanded))
                expanded = false;

            bool newExpanded = EditorGUILayout.Foldout(expanded, foldoutLabel, true, Styles.GetFoldoutStyle(isStringCapture ? LogType.Log : logEvent.logType));
            if (newExpanded != expanded)
                m_logFoldouts[logEvent] = newExpanded;

            if (newExpanded)
            {
                EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);

                if (remainder != null)
                    EditorGUILayout.LabelField(remainder, Styles.GetRemainderStyle(isStringCapture ? LogType.Log : logEvent.logType));

                if (!isStringCapture && !string.IsNullOrEmpty(logEvent.stackTrace))
                {
                    EditorGUILayout.LabelField(logEvent.stackTrace, EditorCommon.Styles.p2);
                }

                if (GUILayout.Button("Copy", GUILayout.Width(60)))
                {
                    string copyText = string.IsNullOrEmpty(logEvent.stackTrace)
                        ? message
                        : $"{message}\n\n{logEvent.stackTrace}";
                    EditorGUIUtility.systemCopyBuffer = copyText;
                }
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(2);
        }

        private void DrawStringCapture(DebugEvent logEvent)
        {
            string line = $"<b>{logEvent.label}</b> {logEvent.message}";
            EditorGUILayout.LabelField(line, EditorCommon.Styles.p1);
        }

        private Texture2D GetOrLoadTexture(DebugEvent capture)
        {
            return GetOrLoadTexture(GetPrimaryTextureCapture(capture));
        }

        private Texture2D GetOrLoadTexture(DebugTextureCapture capture)
        {
            if (capture == null || capture.textureFileIndex < 0)
                return null;

            if (m_textureCache.TryGetValue(capture.textureFileIndex, out Texture2D cached))
                return cached;

            string filePath = m_loadedSession?.GetTextureFilePath(capture.textureFileIndex);
            if (filePath == null || !File.Exists(filePath))
            {
                m_textureLoadErrors[capture.textureFileIndex] = filePath == null
                    ? "Texture file path is unavailable."
                    : $"Texture file not found: {Path.GetFileName(filePath)}";
                return null;
            }

            try
            {
                TextureFormat format = (TextureFormat)System.Enum.Parse(typeof(TextureFormat), capture.textureDataFormat);
                byte[] bytes = File.ReadAllBytes(filePath);
                Texture2D texture = new Texture2D(capture.capturedWidth, capture.capturedHeight, format, false, true);
                texture.LoadRawTextureData(bytes);
                texture.Apply();
                m_textureCache[capture.textureFileIndex] = texture;
                m_textureLoadErrors.Remove(capture.textureFileIndex);
                return texture;
            }
            catch (System.Exception ex)
            {
                m_textureLoadErrors[capture.textureFileIndex] = $"Failed to load texture: {ex.Message}";
                return null;
            }
        }

        private static string GetPrimaryTextureFormat(DebugEvent debugEvent)
        {
            if (debugEvent.textures != null)
            {
                for (int i = 0; i < debugEvent.textures.Count; ++i)
                {
                    string format = debugEvent.textures[i].format;
                    if (!string.IsNullOrEmpty(format))
                        return format;
                }
            }

            return null;
        }

        private static DebugTextureCapture GetPrimaryTextureCapture(DebugEvent capture)
        {
            List<DebugTextureCapture> textureCaptures = GetTextureCaptures(capture);
            return textureCaptures.Count > 0 ? textureCaptures[0] : null;
        }

        private static List<DebugTextureCapture> GetTextureCaptures(DebugEvent capture)
        {
            return capture.textures ?? new List<DebugTextureCapture>();
        }

        private static DebugBufferInterpretation GetBufferInterpretation(DebugEvent capture, bool isInstanceSampleFallback)
        {
            if (capture.bufferInterpretation != DebugBufferInterpretation.Unknown)
            {
                return capture.bufferInterpretation;
            }

            int totalBytes = capture.elementCount * capture.stride;
            bool matchesPositionSample = totalBytes > 0 && totalBytes % POSITION_SAMPLE_BYTES == 0;
            bool matchesInstanceSample = totalBytes > 0 && totalBytes % INSTANCE_SAMPLE_BYTES == 0;

            if (matchesPositionSample && !matchesInstanceSample)
            {
                return DebugBufferInterpretation.PositionSample;
            }

            if (matchesInstanceSample && !matchesPositionSample)
            {
                return DebugBufferInterpretation.InstanceSample;
            }

            if (isInstanceSampleFallback)
            {
                return DebugBufferInterpretation.InstanceSample;
            }

            return DebugBufferInterpretation.Unknown;
        }

        private RenderTexture GetOrVisualizeBuffer(DebugEvent capture, DebugBufferInterpretation bufferInterpretation)
        {
            if (m_bufferRTCache.TryGetValue(capture.bufferFileIndex, out RenderTexture cached))
                return cached;

            string filePath = m_loadedSession?.GetBufferFilePath(capture.bufferFileIndex);
            if (filePath == null || !File.Exists(filePath))
            {
                m_bufferLoadErrors[capture.bufferFileIndex] = filePath == null
                    ? "Buffer file path is unavailable."
                    : $"Buffer file not found: {Path.GetFileName(filePath)}";
                return null;
            }

            ComputeShader dotsShader = Resources.Load<ComputeShader>("Vista/Shaders/Graph/Dots");
            if (dotsShader == null)
            {
                m_bufferLoadErrors[capture.bufferFileIndex] = "Buffer preview shader not found.";
                return null;
            }

            ComputeBuffer computeBuffer = null;
            RenderTexture renderTexture = null;
            try
            {
                dotsShader.shaderKeywords = null;
                bool isInstanceSample = bufferInterpretation == DebugBufferInterpretation.InstanceSample;
                dotsShader.EnableKeyword(isInstanceSample ? KW_DATA_TYPE_INSTANCE_SAMPLE : KW_DATA_TYPE_POSITION_SAMPLE);

                byte[] bytes = File.ReadAllBytes(filePath);
                float[] floatData = new float[bytes.Length / sizeof(float)];
                System.Buffer.BlockCopy(bytes, 0, floatData, 0, bytes.Length);

                int sampleFloats = isInstanceSample ? InstanceSample.SIZE : PositionSample.SIZE;
                computeBuffer = new ComputeBuffer(Mathf.Max(1, floatData.Length / sampleFloats), sampleFloats * sizeof(float));
                computeBuffer.SetData(floatData);

                renderTexture = new RenderTexture(128, 128, 0, RenderTextureFormat.RFloat);
                renderTexture.enableRandomWrite = true;
                renderTexture.Create();
                GraphicsUtils.ClearWithZeros(renderTexture);

                int kernelIndex = 0;
                dotsShader.SetBuffer(kernelIndex, DOTS_POSITIONS, computeBuffer);
                dotsShader.SetTexture(kernelIndex, DOTS_TARGET_RT, renderTexture);
                dotsShader.SetVector(DOTS_RESOLUTION, new Vector4(renderTexture.width, renderTexture.height, 0, 0));

                int instanceCount = floatData.Length / sampleFloats;
                int totalThreadGroupX = (instanceCount + DOTS_THREAD_PER_GROUP - 1) / DOTS_THREAD_PER_GROUP;
                int iteration = (totalThreadGroupX + DOTS_MAX_THREAD_GROUP - 1) / DOTS_MAX_THREAD_GROUP;
                for (int i = 0; i < iteration; i++)
                {
                    int threadGroupX = Mathf.Min(DOTS_MAX_THREAD_GROUP, totalThreadGroupX);
                    totalThreadGroupX -= DOTS_MAX_THREAD_GROUP;
                    dotsShader.SetInt(DOTS_BASE_INDEX, i * DOTS_MAX_THREAD_GROUP * DOTS_THREAD_PER_GROUP);
                    dotsShader.Dispatch(kernelIndex, Mathf.Max(1, threadGroupX), 1, 1);
                }

                m_bufferRTCache[capture.bufferFileIndex] = renderTexture;
                m_bufferLoadErrors.Remove(capture.bufferFileIndex);
                return renderTexture;
            }
            catch (System.Exception ex)
            {
                if (renderTexture != null)
                {
                    renderTexture.Release();
                }
                m_bufferLoadErrors[capture.bufferFileIndex] = $"Failed to load buffer: {ex.Message}";
                return null;
            }
            finally
            {
                if (computeBuffer != null)
                {
                    computeBuffer.Release();
                }
            }
        }

        private void ClearTextureCache()
        {
            foreach (Texture2D texture in m_textureCache.Values)
            {
                if (texture != null)
                    DestroyImmediate(texture);
            }
            m_textureCache.Clear();
            m_textureLoadErrors.Clear();

            foreach (RenderTexture renderTexture in m_bufferRTCache.Values)
            {
                if (renderTexture != null)
                    renderTexture.Release();
            }
            m_bufferRTCache.Clear();
            m_bufferLoadErrors.Clear();
        }

        private void ImportExternalSession()
        {
            string directory = EditorUtility.OpenFolderPanel("Import Vista Session", SessionRoot, string.Empty);
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            string jsonPath = Path.Combine(directory, "session.json");
            if (!File.Exists(jsonPath))
            {
                EditorUtility.DisplayDialog("Vista Diagnostics", "The selected folder does not contain a session.json file.", "OK");
                return;
            }

            List<string> externalDirectories = LoadExternalSessionDirectories();
            bool exists = false;
            foreach (string path in externalDirectories)
            {
                if (string.Equals(path, directory, System.StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                externalDirectories.Add(directory);
                SaveExternalSessionDirectories(externalDirectories);
            }

            RefreshSessionList();
            m_selectedSessionDirectory = directory;
            LoadSession(directory);
            Repaint();
        }

        private List<string> LoadExternalSessionDirectories()
        {
            string json = EditorPrefs.GetString(EXTERNAL_SESSION_PATHS_PREF_KEY, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return new List<string>();
            }

            try
            {
                ExternalSessionPathStore store = JsonUtility.FromJson<ExternalSessionPathStore>(json);
                return store?.paths ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        private void SaveExternalSessionDirectories(List<string> directories)
        {
            ExternalSessionPathStore store = new ExternalSessionPathStore();
            HashSet<string> uniquePaths = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (string directory in directories)
            {
                if (!string.IsNullOrEmpty(directory) && uniquePaths.Add(directory))
                {
                    store.paths.Add(directory);
                }
            }

            string json = JsonUtility.ToJson(store);
            EditorPrefs.SetString(EXTERNAL_SESSION_PATHS_PREF_KEY, json);
        }

        private void RemoveExternalSession(string directory)
        {
            List<string> externalDirectories = LoadExternalSessionDirectories();
            externalDirectories.RemoveAll(path => string.Equals(path, directory, System.StringComparison.OrdinalIgnoreCase));
            SaveExternalSessionDirectories(externalDirectories);

            if (m_selectedSessionDirectory == directory)
            {
                ClearTextureCache();
                m_detailItemHash = -1;
                m_selectedSessionDirectory = null;
                m_loadedSession = null;
                m_loadedSessionError = null;
                m_eventTreeView = null;
                m_treeViewState = null;
                m_selectedItem = null;
            }

            RefreshSessionList();
        }

        private DebugSession TryLoadSession(string directory)
        {
            try
            {
                m_sessionLoadErrors.Remove(directory);
                if (!Directory.Exists(directory))
                {
                    m_sessionLoadErrors[directory] = "Session folder not found.";
                    return null;
                }

                string jsonPath = Path.Combine(directory, "session.json");
                if (!File.Exists(jsonPath))
                {
                    m_sessionLoadErrors[directory] = "session.json was not found.";
                    return null;
                }

                DebugSession session = DebugSession.LoadFromDirectory(directory);
                if (session == null)
                {
                    m_sessionLoadErrors[directory] = "Failed to load session data.";
                }
                return session;
            }
            catch (System.Exception ex)
            {
                m_sessionLoadErrors[directory] = $"Failed to load session: {ex.Message}";
                return null;
            }
        }

        private string GetSessionLoadError(string directory)
        {
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            m_sessionLoadErrors.TryGetValue(directory, out string error);
            return error;
        }

        private string GetSessionLoadSummary(string directory)
        {
            string error = GetSessionLoadError(directory);
            if (string.IsNullOrEmpty(error))
            {
                return "Session data unavailable";
            }

            if (error.IndexOf("folder", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Missing folder";
            }

            if (error.IndexOf("session.json", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Missing session.json";
            }

            return "Invalid session";
        }

        private void DrawSessionLoadError()
        {
            string sessionName = !string.IsNullOrEmpty(m_selectedSessionDirectory)
                ? Path.GetFileName(m_selectedSessionDirectory)
                : "Session";

            EditorGUILayout.LabelField(sessionName, EditorCommon.Styles.h2);
            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(string.IsNullOrEmpty(m_loadedSessionError) ? "Failed to load session." : m_loadedSessionError, MessageType.Warning);

            if (!string.IsNullOrEmpty(m_selectedSessionDirectory))
            {
                EditorGUILayout.LabelField("Path", EditorCommon.Styles.h3);
                EditorGUILayout.LabelField(m_selectedSessionDirectory, EditorCommon.Styles.p1);
            }
        }

        private string GetTextureLoadError(DebugTextureCapture capture)
        {
            if (capture == null || capture.textureFileIndex < 0)
            {
                return null;
            }

            m_textureLoadErrors.TryGetValue(capture.textureFileIndex, out string error);
            return error;
        }

        private string GetBufferLoadError(DebugEvent capture)
        {
            if (capture == null || capture.bufferFileIndex < 0)
            {
                return null;
            }

            m_bufferLoadErrors.TryGetValue(capture.bufferFileIndex, out string error);
            return error;
        }

        private void HandleSplitter2(Rect splitterRect, float movableSpace, float col2Start)
        {
            Rect hitRect = new Rect(splitterRect.x - 4f, splitterRect.y, splitterRect.width + 8f, splitterRect.height);
            EditorGUIUtility.AddCursorRect(hitRect, MouseCursor.ResizeHorizontal);

            Event currentEvent = Event.current;

            if (currentEvent.type == EventType.MouseDown && hitRect.Contains(currentEvent.mousePosition))
            {
                m_isDraggingSplitter = true;
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.MouseDrag && m_isDraggingSplitter)
            {
                float newCol2Width = currentEvent.mousePosition.x - col2Start;
                m_splitRatio = Mathf.Clamp(newCol2Width / Mathf.Max(movableSpace, 1f), SPLIT_RATIO_MIN, SPLIT_RATIO_MAX);
                currentEvent.Use();
                Repaint();
            }
            else if (currentEvent.type == EventType.MouseUp)
            {
                m_isDraggingSplitter = false;
            }
        }
    }
}
#endif
