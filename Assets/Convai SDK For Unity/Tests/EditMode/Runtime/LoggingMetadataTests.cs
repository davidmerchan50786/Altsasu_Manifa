using System;
using System.Reflection;
using Convai.Domain.Logging;
using Convai.Runtime;
using Convai.Runtime.Logging;
using NUnit.Framework;

namespace Convai.Tests.EditMode.Runtime
{
    [TestFixture]
    public class LoggingMetadataTests
    {
        [SetUp]
        public void SetUp()
        {
            _sink = new TestLogSink();
            _settings = ConvaiSettings.Instance;
            Assert.IsNotNull(_settings, "ConvaiSettings instance must exist for logging metadata tests.");

            _originalGlobalLevel = _settings.GlobalLogLevel;
            _originalCategoryOverrides = CloneOverrides(_settings.CategoryOverrides);

            ConvaiLogger.ClearSinks();
            ConvaiLogger.Initialize();
            ConvaiLogger.RegisterSink(_sink);
            LoggingConfig.InvalidateCache();
        }

        [TearDown]
        public void TearDown()
        {
            if (_settings != null)
            {
                _settings.SetGlobalLogLevel(_originalGlobalLevel);
                _settings.SetCategoryOverrides(CloneOverrides(_originalCategoryOverrides));
                LoggingConfig.InvalidateCache();
            }

            ConvaiLogger.ClearSinks();
            _sink?.Dispose();
        }

        private TestLogSink _sink;
        private ConvaiSettings _settings;
        private LogLevel _originalGlobalLevel;
        private LogLevelOverride[] _originalCategoryOverrides;

        [Test]
        public void ConvaiLogger_InfoWithLipSyncCategory_FormatsLipSyncCategoryName()
        {
            const string message = "Lip sync metadata test";

            ConvaiLogger.Info(message, LogCategory.LipSync);

            Assert.That(_sink.Entries.Count, Is.GreaterThanOrEqualTo(1));

            LogEntry entry = _sink.Entries.Find(candidate => candidate.Message == message);
            Assert.That(entry.Category, Is.EqualTo(LogCategory.LipSync));

            var consoleSink = new UnityConsoleSink();
            MethodInfo formatMethod = typeof(UnityConsoleSink).GetMethod(
                "FormatLogEntry",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(formatMethod, "Expected UnityConsoleSink.FormatLogEntry to exist.");

            string formatted = (string)formatMethod.Invoke(consoleSink, new object[] { entry });
            Assert.That(formatted, Does.Contain("[LipSync]"));
        }

        [Test]
        public void LoggingConfig_IsEnabled_RespectsLipSyncOverride()
        {
            _settings.SetGlobalLogLevel(LogLevel.Info);
            _settings.SetCategoryOverrides(new[] { new LogLevelOverride(LogCategory.LipSync, LogLevel.Error) });
            LoggingConfig.InvalidateCache();

            Assert.That(LoggingConfig.IsEnabled(LogLevel.Error, LogCategory.LipSync), Is.True);
            Assert.That(LoggingConfig.IsEnabled(LogLevel.Info, LogCategory.LipSync), Is.False);
            Assert.That(LoggingConfig.IsEnabled(LogLevel.Debug, LogCategory.LipSync), Is.False);
            Assert.That(LoggingConfig.IsEnabled(LogLevel.Info, LogCategory.SDK), Is.True);
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
