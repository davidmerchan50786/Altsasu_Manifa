using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime;
using UnityEditor;

namespace Convai.Editor.ConfigurationWindow.Components.Sections.LoggerSettings
{
    /// <summary>
    ///     Business logic for the Logger Settings UI section.
    ///     Uses the new ConvaiSettings-based logging configuration.
    /// </summary>
    public class LoggerSettingsLogic
    {
        private const string GlobalLogLevelPropertyName = "_globalLogLevel";
        private const string IncludeStackTracesPropertyName = "_includeStackTraces";
        private const string ColoredOutputPropertyName = "_coloredOutput";
        private const string CategoryOverridesPropertyName = "_categoryOverrides";
        private const string CategoryPropertyName = "Category";
        private const string LevelPropertyName = "Level";
        private readonly Dictionary<LogCategory, LogLevel> _snapshotCache = new();
        private bool _savePending;

        private SerializedObject _settingsObject;

        /// <summary>
        ///     Gets the current ConvaiSettings as a SerializedObject.
        /// </summary>
        public SerializedObject SettingsObject
        {
            get
            {
                var settings = ConvaiSettings.Instance;
                if (settings == null) return null;

                if (_settingsObject == null || _settingsObject.targetObject != settings)
                    _settingsObject = new SerializedObject(settings);
                return _settingsObject;
            }
        }

        private SerializedObject GetSettingsObject(bool refresh = true)
        {
            SerializedObject settingsObject = SettingsObject;
            if (settingsObject != null && refresh) settingsObject.Update();

            return settingsObject;
        }

        /// <summary>
        ///     Gets the global log level from ConvaiSettings.
        /// </summary>
        public LogLevel GetGlobalLogLevel() => ConvaiSettings.Instance?.GlobalLogLevel ?? LogLevel.Info;

        /// <summary>
        ///     Sets the global log level in ConvaiSettings.
        /// </summary>
        /// <param name="level">New global log level.</param>
        public void SetGlobalLogLevel(LogLevel level)
        {
            SerializedObject settingsObject = GetSettingsObject();
            if (settingsObject == null) return;

            SerializedProperty property = settingsObject.FindProperty(GlobalLogLevelPropertyName);
            if (property == null) return;

            if (property.enumValueIndex != (int)level)
            {
                property.enumValueIndex = (int)level;
                ApplyChanges();
            }
        }

        /// <summary>
        ///     Gets whether stack traces are included.
        /// </summary>
        public bool GetIncludeStackTraces() => ConvaiSettings.Instance?.IncludeStackTraces ?? true;

        /// <summary>
        ///     Sets whether stack traces are included.
        /// </summary>
        /// <param name="includeStackTraces">True to include stack traces.</param>
        public void SetIncludeStackTraces(bool includeStackTraces)
        {
            SerializedObject settingsObject = GetSettingsObject();
            if (settingsObject == null) return;

            SerializedProperty property = settingsObject.FindProperty(IncludeStackTracesPropertyName);
            if (property == null) return;

            if (property.boolValue != includeStackTraces)
            {
                property.boolValue = includeStackTraces;
                ApplyChanges();
            }
        }

        /// <summary>
        ///     Gets the override log level for a category, or null if using global default.
        /// </summary>
        public LogLevel? GetCategoryOverride(LogCategory category)
        {
            SerializedObject settingsObject = GetSettingsObject();
            if (settingsObject == null) return null;

            SerializedProperty overridesProp = settingsObject.FindProperty(CategoryOverridesPropertyName);
            if (overridesProp == null) return null;

            for (int i = 0; i < overridesProp.arraySize; i++)
            {
                SerializedProperty element = overridesProp.GetArrayElementAtIndex(i);
                SerializedProperty catProp = element.FindPropertyRelative(CategoryPropertyName);
                if (catProp.enumValueIndex == (int)category)
                    return (LogLevel)element.FindPropertyRelative(LevelPropertyName).enumValueIndex;
            }

            return null;
        }

        /// <summary>
        ///     Gets whether a category has an explicit override.
        /// </summary>
        /// <param name="category">Category to check.</param>
        /// <returns>True if category override exists; otherwise false.</returns>
        public bool HasCategoryOverride(LogCategory category) => GetCategoryOverride(category).HasValue;

        /// <summary>
        ///     Gets a snapshot of category overrides for fast UI refresh/filter passes.
        ///     Reuses an internal dictionary to avoid allocations on every call.
        /// </summary>
        /// <param name="refresh">Whether to re-read serialized data from disk. Pass false after a write to avoid redundant I/O.</param>
        /// <returns>Map of category to override level.</returns>
        public IReadOnlyDictionary<LogCategory, LogLevel> GetCategoryOverridesSnapshot(bool refresh = true)
        {
            _snapshotCache.Clear();
            SerializedObject settingsObject = GetSettingsObject(refresh);
            if (settingsObject == null) return _snapshotCache;

            SerializedProperty overridesProp = settingsObject.FindProperty(CategoryOverridesPropertyName);
            if (overridesProp == null) return _snapshotCache;

            for (int i = 0; i < overridesProp.arraySize; i++)
            {
                SerializedProperty element = overridesProp.GetArrayElementAtIndex(i);
                SerializedProperty categoryProperty = element.FindPropertyRelative(CategoryPropertyName);
                SerializedProperty levelProperty = element.FindPropertyRelative(LevelPropertyName);
                if (categoryProperty == null || levelProperty == null) continue;

                _snapshotCache[(LogCategory)categoryProperty.enumValueIndex] = (LogLevel)levelProperty.enumValueIndex;
            }

            return _snapshotCache;
        }

        /// <summary>
        ///     Sets (or removes) a category override.
        /// </summary>
        /// <param name="category">The category to configure.</param>
        /// <param name="level">The specific log level, or null to use global default.</param>
        public void SetCategoryOverride(LogCategory category, LogLevel? level)
        {
            SerializedObject settingsObject = GetSettingsObject();
            if (settingsObject == null) return;

            SerializedProperty overridesProp = settingsObject.FindProperty(CategoryOverridesPropertyName);
            if (overridesProp == null) return;

            int index = -1;
            for (int i = 0; i < overridesProp.arraySize; i++)
            {
                SerializedProperty element = overridesProp.GetArrayElementAtIndex(i);
                SerializedProperty catProp = element.FindPropertyRelative(CategoryPropertyName);
                if (catProp.enumValueIndex == (int)category)
                {
                    index = i;
                    break;
                }
            }

            if (level.HasValue)
            {
                if (index != -1)
                {
                    SerializedProperty element = overridesProp.GetArrayElementAtIndex(index);
                    element.FindPropertyRelative(LevelPropertyName).enumValueIndex = (int)level.Value;
                }
                else
                {
                    int newIndex = overridesProp.arraySize;
                    overridesProp.InsertArrayElementAtIndex(newIndex);
                    SerializedProperty element = overridesProp.GetArrayElementAtIndex(newIndex);
                    element.FindPropertyRelative(CategoryPropertyName).enumValueIndex = (int)category;
                    element.FindPropertyRelative(LevelPropertyName).enumValueIndex = (int)level.Value;
                }
            }
            else
            {
                if (index != -1) overridesProp.DeleteArrayElementAtIndex(index);
            }

            ApplyChanges();
        }

        /// <summary>
        ///     Sets category overrides for all categories to a specific level.
        /// </summary>
        /// <param name="level">Override level to apply for every category.</param>
        public void SetAllCategoryOverrides(LogLevel level)
        {
            SerializedObject settingsObject = GetSettingsObject();
            if (settingsObject == null) return;

            SerializedProperty overridesProp = settingsObject.FindProperty(CategoryOverridesPropertyName);
            if (overridesProp == null) return;

            Array categories = Enum.GetValues(typeof(LogCategory));
            overridesProp.arraySize = categories.Length;
            for (int i = 0; i < categories.Length; i++)
            {
                var category = (LogCategory)categories.GetValue(i);
                SerializedProperty element = overridesProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative(CategoryPropertyName).enumValueIndex = (int)category;
                element.FindPropertyRelative(LevelPropertyName).enumValueIndex = (int)level;
            }

            ApplyChanges();
        }

        /// <summary>
        ///     Clears all category overrides so every category inherits the global level.
        /// </summary>
        public void ClearCategoryOverrides()
        {
            SerializedObject settingsObject = GetSettingsObject();
            if (settingsObject == null) return;

            SerializedProperty overridesProp = settingsObject.FindProperty(CategoryOverridesPropertyName);
            if (overridesProp == null) return;

            if (overridesProp.arraySize > 0)
            {
                overridesProp.arraySize = 0;
                ApplyChanges();
            }
        }

        /// <summary>
        ///     Gets whether colored output is enabled.
        /// </summary>
        public bool GetColoredOutput() => ConvaiSettings.Instance?.ColoredOutput ?? true;

        /// <summary>
        ///     Sets whether colored output is enabled.
        /// </summary>
        /// <param name="coloredOutput">True to enable colored output.</param>
        public void SetColoredOutput(bool coloredOutput)
        {
            SerializedObject settingsObject = GetSettingsObject();
            if (settingsObject == null) return;

            SerializedProperty property = settingsObject.FindProperty(ColoredOutputPropertyName);
            if (property == null) return;

            if (property.boolValue != coloredOutput)
            {
                property.boolValue = coloredOutput;
                ApplyChanges();
            }
        }

        /// <summary>
        ///     Resets logging settings to SDK defaults.
        /// </summary>
        public void ResetLoggingDefaults()
        {
            SerializedObject settingsObject = GetSettingsObject();
            if (settingsObject == null) return;

            SerializedProperty globalLevelProp = settingsObject.FindProperty(GlobalLogLevelPropertyName);
            SerializedProperty includeStackTracesProp = settingsObject.FindProperty(IncludeStackTracesPropertyName);
            SerializedProperty coloredOutputProp = settingsObject.FindProperty(ColoredOutputPropertyName);
            SerializedProperty overridesProp = settingsObject.FindProperty(CategoryOverridesPropertyName);

            if (globalLevelProp == null || includeStackTracesProp == null || coloredOutputProp == null ||
                overridesProp == null) return;

            globalLevelProp.enumValueIndex = (int)LogLevel.Info;
            includeStackTracesProp.boolValue = true;
            coloredOutputProp.boolValue = true;
            overridesProp.arraySize = 0;

            ApplyChanges();
        }

        /// <summary>
        ///     Applies changes to the serialized settings.
        ///     The in-memory update is immediate; the disk save is debounced so
        ///     rapid interactions batch into a single <see cref="AssetDatabase.SaveAssets" /> call.
        /// </summary>
        public void ApplyChanges()
        {
            if (_settingsObject == null) return;

            _settingsObject.ApplyModifiedProperties();
            var settings = ConvaiSettings.Instance;
            if (settings != null) EditorUtility.SetDirty(settings);

            ScheduleDebouncedSave();
        }

        private void ScheduleDebouncedSave()
        {
            if (_savePending) return;
            _savePending = true;
            EditorApplication.delayCall += FlushSave;
        }

        private void FlushSave()
        {
            _savePending = false;
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        ///     Refreshes the serialized object from disk.
        /// </summary>
        public void Refresh() => _settingsObject?.Update();
    }
}
