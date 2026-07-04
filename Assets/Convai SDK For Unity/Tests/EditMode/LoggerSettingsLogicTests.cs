using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Editor.ConfigurationWindow.Components.Sections.LoggerSettings;
using Convai.Runtime;
using NUnit.Framework;
using UnityEditor;

namespace Convai.Tests.EditMode
{
    public class LoggerSettingsLogicTests
    {
        private LoggerSettingsLogic _logic;
        private LogLevelOverride[] _originalCategoryOverrides;
        private bool _originalColoredOutput;

        private LogLevel _originalGlobalLevel;
        private bool _originalIncludeStackTraces;
        private ConvaiSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _logic = new LoggerSettingsLogic();
            _settings = ConvaiSettings.Instance;
            Assert.IsNotNull(_settings, "ConvaiSettings instance must exist for logger logic tests.");

            _originalGlobalLevel = _settings.GlobalLogLevel;
            _originalIncludeStackTraces = _settings.IncludeStackTraces;
            _originalColoredOutput = _settings.ColoredOutput;
            _originalCategoryOverrides = CloneOverrides(_settings.CategoryOverrides);

            _logic.ResetLoggingDefaults();
        }

        [TearDown]
        public void TearDown()
        {
            if (_logic == null || _settings == null) return;

            _logic.SetGlobalLogLevel(_originalGlobalLevel);
            _logic.SetIncludeStackTraces(_originalIncludeStackTraces);
            _logic.SetColoredOutput(_originalColoredOutput);
            _settings.SetCategoryOverrides(CloneOverrides(_originalCategoryOverrides));
            _logic.Refresh();
        }

        [Test]
        public void SetCategoryOverride_CreatesAndUpdatesSingleEntry()
        {
            _logic.ClearCategoryOverrides();

            _logic.SetCategoryOverride(LogCategory.Audio, LogLevel.Debug);
            Assert.AreEqual(LogLevel.Debug, _logic.GetCategoryOverride(LogCategory.Audio));
            Assert.AreEqual(1, CountOverridesForCategory(LogCategory.Audio));

            _logic.SetCategoryOverride(LogCategory.Audio, LogLevel.Warning);
            Assert.AreEqual(LogLevel.Warning, _logic.GetCategoryOverride(LogCategory.Audio));
            Assert.AreEqual(1, CountOverridesForCategory(LogCategory.Audio));
        }

        [Test]
        public void SetCategoryOverride_Null_RemovesOverride()
        {
            _logic.SetCategoryOverride(LogCategory.Player, LogLevel.Error);
            Assert.AreEqual(LogLevel.Error, _logic.GetCategoryOverride(LogCategory.Player));

            _logic.SetCategoryOverride(LogCategory.Player, null);

            Assert.IsNull(_logic.GetCategoryOverride(LogCategory.Player));
            Assert.AreEqual(0, CountOverridesForCategory(LogCategory.Player));
        }

        [Test]
        public void SetAllCategoryOverrides_SetsEveryCategory()
        {
            _logic.SetAllCategoryOverrides(LogLevel.Warning);

            foreach (LogCategory category in Enum.GetValues(typeof(LogCategory)))
                Assert.AreEqual(LogLevel.Warning, _logic.GetCategoryOverride(category),
                    $"Category {category} should be overridden to Warning.");

            Assert.AreEqual(Enum.GetValues(typeof(LogCategory)).Length, GetOverrideArraySize());
        }

        [Test]
        public void ClearCategoryOverrides_LeavesEmptyArray()
        {
            _logic.SetAllCategoryOverrides(LogLevel.Info);
            Assert.Greater(GetOverrideArraySize(), 0);

            _logic.ClearCategoryOverrides();

            Assert.AreEqual(0, GetOverrideArraySize());
            foreach (LogCategory category in Enum.GetValues(typeof(LogCategory)))
                Assert.IsNull(_logic.GetCategoryOverride(category),
                    $"Category {category} should inherit global level after clearing overrides.");
        }

        [Test]
        public void ResetLoggingDefaults_AppliesSdkDefaults()
        {
            _logic.SetGlobalLogLevel(LogLevel.Trace);
            _logic.SetIncludeStackTraces(false);
            _logic.SetColoredOutput(false);
            _logic.SetCategoryOverride(LogCategory.REST, LogLevel.Error);

            _logic.ResetLoggingDefaults();

            Assert.AreEqual(LogLevel.Info, _logic.GetGlobalLogLevel());
            Assert.IsTrue(_logic.GetIncludeStackTraces());
            Assert.IsTrue(_logic.GetColoredOutput());
            Assert.AreEqual(0, GetOverrideArraySize());
        }

        [Test]
        public void SetGlobalLogLevel_PersistsValue()
        {
            _logic.SetGlobalLogLevel(LogLevel.Debug);
            Assert.AreEqual(LogLevel.Debug, _logic.GetGlobalLogLevel());
        }

        [Test]
        public void SetIncludeStackTraces_PersistsValue()
        {
            _logic.SetIncludeStackTraces(false);
            Assert.IsFalse(_logic.GetIncludeStackTraces());
        }

        [Test]
        public void SetColoredOutput_PersistsValue()
        {
            _logic.SetColoredOutput(false);
            Assert.IsFalse(_logic.GetColoredOutput());
        }

        [Test]
        public void HasCategoryOverride_TracksOverridePresence()
        {
            _logic.ClearCategoryOverrides();
            Assert.IsFalse(_logic.HasCategoryOverride(LogCategory.Transport));

            _logic.SetCategoryOverride(LogCategory.Transport, LogLevel.Debug);
            Assert.IsTrue(_logic.HasCategoryOverride(LogCategory.Transport));

            _logic.SetCategoryOverride(LogCategory.Transport, null);
            Assert.IsFalse(_logic.HasCategoryOverride(LogCategory.Transport));
        }

        [Test]
        public void GetCategoryOverridesSnapshot_ReturnsCurrentOverrides()
        {
            _logic.ClearCategoryOverrides();
            _logic.SetCategoryOverride(LogCategory.Audio, LogLevel.Warning);
            _logic.SetCategoryOverride(LogCategory.Events, LogLevel.Error);

            IReadOnlyDictionary<LogCategory, LogLevel> snapshot = _logic.GetCategoryOverridesSnapshot();

            Assert.AreEqual(2, snapshot.Count);
            Assert.AreEqual(LogLevel.Warning, snapshot[LogCategory.Audio]);
            Assert.AreEqual(LogLevel.Error, snapshot[LogCategory.Events]);
            Assert.IsFalse(snapshot.ContainsKey(LogCategory.SDK));
        }

        private int GetOverrideArraySize()
        {
            SerializedObject settingsObject = _logic.SettingsObject;
            Assert.IsNotNull(settingsObject);
            settingsObject.Update();

            SerializedProperty overridesProp = settingsObject.FindProperty("_categoryOverrides");
            Assert.IsNotNull(overridesProp);
            return overridesProp.arraySize;
        }

        private int CountOverridesForCategory(LogCategory category)
        {
            SerializedObject settingsObject = _logic.SettingsObject;
            Assert.IsNotNull(settingsObject);
            settingsObject.Update();

            SerializedProperty overridesProp = settingsObject.FindProperty("_categoryOverrides");
            Assert.IsNotNull(overridesProp);

            int count = 0;
            for (int i = 0; i < overridesProp.arraySize; i++)
            {
                SerializedProperty element = overridesProp.GetArrayElementAtIndex(i);
                SerializedProperty categoryProp = element.FindPropertyRelative("Category");
                if (categoryProp.enumValueIndex == (int)category) count++;
            }

            return count;
        }

        private static LogLevelOverride[] CloneOverrides(LogLevelOverride[] source)
        {
            if (source == null || source.Length == 0) return Array.Empty<LogLevelOverride>();

            var copy = new LogLevelOverride[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }
    }
}
