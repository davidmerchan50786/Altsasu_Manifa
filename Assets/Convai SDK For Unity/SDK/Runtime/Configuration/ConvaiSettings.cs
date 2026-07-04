using System;
using System.IO;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Convai.Runtime
{
    /// <summary>
    ///     Centralized settings for the Convai SDK.
    /// </summary>
    [CreateAssetMenu(fileName = "ConvaiSettings", menuName = "Convai/SDK Settings")]
    public class ConvaiSettings : ScriptableObject
    {
        private const string ResourcePath = "ConvaiSettings";
        private const string ResourceAssetPath = "Assets/Resources/ConvaiSettings.asset";

#if UNITY_EDITOR
        /// <summary>
        ///     Called when script is loaded or a value is changed in the inspector.
        ///     Increments config version to invalidate logging caches.
        /// </summary>
        private void OnValidate()
        {
            _nativeRuntimeMode = NativeRuntimeMode.Transport;
            IncrementConfigVersion();
        }
#endif

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void EnsureSettingsAssetExists()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ConvaiSettings>(ResourceAssetPath);
            if (asset != null)
            {
                _instance = asset;
                return;
            }

            asset = Resources.Load<ConvaiSettings>(ResourcePath);
            if (asset != null)
            {
                _instance = asset;
                return;
            }

            string directory = Path.GetDirectoryName(ResourceAssetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

            asset = CreateInstance<ConvaiSettings>();
            AssetDatabase.CreateAsset(asset, ResourceAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            _instance = asset;
        }
#endif

        #region Singleton Instance

        private static ConvaiSettings _instance;

        /// <summary>
        ///     Gets the singleton instance of ConvaiSettings.
        ///     Creates a new instance if one doesn't exist (Editor only).
        /// </summary>
        public static ConvaiSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<ConvaiSettings>(ResourcePath);
#if UNITY_EDITOR
                    if (_instance == null) _instance = CreateInstance<ConvaiSettings>();
#endif
                }

                return _instance;
            }
        }

        #endregion

        #region Serialized Fields

        [Header("API Configuration")] [SerializeField] [Tooltip("Your Convai API key from the dashboard.")]
        private string _apiKey = "";

        [SerializeField]
        [Tooltip("Convai realtime server URL used for room connect requests. Leave default unless directed otherwise.")]
        private string _serverUrl = "https://live.convai.com";

        [Header("Player Settings")] [SerializeField] [Tooltip("The player's display name shown in conversations.")]
        private string _playerName = "";

        [Header("Audio Settings")]
        [SerializeField]
        [Tooltip("Default microphone device index. 0 = default device.")]
        [Range(0, 10)]
        private int _defaultMicrophoneIndex;

        [SerializeField] [Tooltip("Connection timeout in seconds.")] [Range(5f, 120f)]
        private float _connectionTimeout = 30f;

        [SerializeField] [Tooltip("Native/editor realtime uses the transport runtime path.")]
        private NativeRuntimeMode _nativeRuntimeMode = NativeRuntimeMode.Transport;

        [Header("Logging")] [SerializeField] [Tooltip("Global minimum log level. Logs below this level are filtered.")]
        private LogLevel _globalLogLevel = LogLevel.Info;

        [SerializeField] [Tooltip("Enable stack traces for Warning and Error logs.")]
        private bool _includeStackTraces = true;

        [SerializeField] [Tooltip("Enable colored output in Unity Console.")]
        private bool _coloredOutput = true;

        [SerializeField] [Tooltip("Per-category log level overrides. Empty = use global level.")]
        private LogLevelOverride[] _categoryOverrides = Array.Empty<LogLevelOverride>();

        [Header("Features")] [SerializeField] [Tooltip("Enable the transcript system.")]
        private bool _transcriptSystemEnabled = true;

        [SerializeField] [Tooltip("Enable the notification system.")]
        private bool _notificationSystemEnabled;

        [Header("UI Settings")]
        [SerializeField]
        [Tooltip("Index of the active transcript style (0-based).")]
        [Range(0, 10)]
        private int _activeTranscriptStyleIndex;

        #endregion

        #region Public Properties

        /// <summary>
        ///     Version number for cache invalidation. Increments when logging-related settings change.
        ///     Used by LoggingConfig for efficient cache validation.
        /// </summary>
        public int ConfigVersion { get; private set; }

        /// <summary>
        ///     Gets the category override array for batch processing.
        ///     Used by LoggingConfig for efficient cache building.
        /// </summary>
        public LogLevelOverride[] CategoryOverrides => _categoryOverrides;

        /// <summary>Convai API key for authentication.</summary>
        public string ApiKey => _apiKey;

        /// <summary>Convai server URL.</summary>
        public string ServerUrl => _serverUrl;

        /// <summary>The player's display name shown in conversations.</summary>
        public string PlayerName => _playerName;

        /// <summary>Default microphone device index.</summary>
        public int DefaultMicrophoneIndex => _defaultMicrophoneIndex;

        /// <summary>Connection timeout in seconds.</summary>
        public float ConnectionTimeout => _connectionTimeout;

        /// <summary>Gets the native/editor realtime runtime path.</summary>
        public NativeRuntimeMode NativeRuntimeMode => NormalizeNativeRuntimeMode(_nativeRuntimeMode);

        /// <summary>Global minimum log level.</summary>
        public LogLevel GlobalLogLevel => _globalLogLevel;

        /// <summary>Whether stack traces are included for Warning and Error logs.</summary>
        public bool IncludeStackTraces => _includeStackTraces;

        /// <summary>Whether colored output is enabled in Unity Console.</summary>
        public bool ColoredOutput => _coloredOutput;

        /// <summary>
        ///     Gets the effective log level for a specific category.
        ///     Returns the category override if set, otherwise the global level.
        /// </summary>
        /// <param name="category">The log category to check.</param>
        /// <returns>The effective log level for the category.</returns>
        public LogLevel GetLogLevel(LogCategory category)
        {
            if (_categoryOverrides != null)
            {
                foreach (LogLevelOverride over in _categoryOverrides)
                {
                    if (over.Category == category)
                        return over.Level;
                }
            }

            return _globalLogLevel;
        }

        /// <summary>Whether the transcript system is enabled.</summary>
        public bool TranscriptSystemEnabled => _transcriptSystemEnabled;

        /// <summary>Whether the notification system is enabled.</summary>
        public bool NotificationSystemEnabled => _notificationSystemEnabled;

        /// <summary>Index of the active transcript style.</summary>
        public int ActiveTranscriptStyleIndex => _activeTranscriptStyleIndex;

        /// <summary>Whether an API key is configured.</summary>
        public bool HasApiKey => !string.IsNullOrEmpty(_apiKey);

        private static NativeRuntimeMode NormalizeNativeRuntimeMode(NativeRuntimeMode runtimeMode) =>
            Enum.IsDefined(typeof(NativeRuntimeMode), runtimeMode) ? runtimeMode : NativeRuntimeMode.Transport;

        #endregion

        #region Runtime Setters

        private void MarkDirtyIfEditor()
        {
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>
        ///     Increments the config version to trigger cache invalidation.
        ///     Called when logging-related settings change.
        /// </summary>
        private void IncrementConfigVersion() => ConfigVersion++;

        /// <summary>
        ///     Sets the global log level at runtime.
        /// </summary>
        /// <param name="level">The new global log level.</param>
        public void SetGlobalLogLevel(LogLevel level)
        {
            if (_globalLogLevel != level)
            {
                _globalLogLevel = level;
                IncrementConfigVersion();
                MarkDirtyIfEditor();
            }
        }

        /// <summary>
        ///     Sets whether stack traces are included at runtime.
        /// </summary>
        /// <param name="include">Whether to include stack traces.</param>
        public void SetIncludeStackTraces(bool include)
        {
            if (_includeStackTraces != include)
            {
                _includeStackTraces = include;
                IncrementConfigVersion();
                MarkDirtyIfEditor();
            }
        }

        /// <summary>
        ///     Sets the category overrides at runtime.
        /// </summary>
        /// <param name="overrides">The new category overrides array.</param>
        public void SetCategoryOverrides(LogLevelOverride[] overrides)
        {
            _categoryOverrides = overrides ?? Array.Empty<LogLevelOverride>();
            IncrementConfigVersion();
            MarkDirtyIfEditor();
        }

        #endregion

        #region Editor-Only Setters

        /// <summary>
        ///     Sets the API key. In builds, this logs a warning and does nothing.
        ///     Use the Project Settings UI to configure the API key in the Editor.
        /// </summary>
        public void SetApiKey(string apiKey)
        {
#if UNITY_EDITOR
            _apiKey = apiKey;
            MarkDirtyIfEditor();
#else
            ConvaiLogger.Warning("[ConvaiSettings] SetApiKey is only available in the Editor. Use Project Settings to configure the API key.", LogCategory.SDK);
#endif
        }

        /// <summary>
        ///     Sets the server URL. In builds, this logs a warning and does nothing.
        /// </summary>
        public void SetServerUrl(string serverUrl)
        {
#if UNITY_EDITOR
            _serverUrl = serverUrl;
            MarkDirtyIfEditor();
#else
            ConvaiLogger.Warning("[ConvaiSettings] SetServerUrl is only available in the Editor. Use Project Settings to configure the server URL.", LogCategory.SDK);
#endif
        }

        #endregion
    }

    /// <summary>
    ///     Serializable per-category log level override.
    /// </summary>
    [Serializable]
    public struct LogLevelOverride
    {
        /// <summary>The category to override.</summary>
        [Tooltip("The log category to override.")]
        public LogCategory Category;

        /// <summary>The log level for this category.</summary>
        [Tooltip("The log level for this category.")]
        public LogLevel Level;

        /// <summary>
        ///     Creates a new log level override.
        /// </summary>
        /// <param name="category">The category to override.</param>
        /// <param name="level">The log level for this category.</param>
        public LogLevelOverride(LogCategory category, LogLevel level)
        {
            Category = category;
            Level = level;
        }
    }
}
