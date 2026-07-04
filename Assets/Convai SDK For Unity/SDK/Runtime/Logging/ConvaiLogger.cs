using System;
using System.Collections.Generic;
using System.Diagnostics;
using Convai.Domain.Logging;
using Unity.Profiling;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Runtime.Logging
{
    /// <summary>
    ///     Unified logger that implements ILogger interface while maintaining static convenience methods.
    ///     Use this class for all logging in the Convai SDK.
    /// </summary>
    /// <remarks>
    ///     This class serves dual purposes:
    ///     1. Static methods (Debug, Info, Warning, Error) for direct logging in Unity components
    ///     2. ILogger implementation for dependency injection in domain/service layers
    ///     Log level filtering is controlled by ConvaiSettings.GlobalLogLevel and per-category overrides.
    ///     Performance optimizations:
    ///     - Copy-on-write pattern for lock-free sink dispatch (P0-1)
    ///     - Cached category level lookup via LoggingConfig (P0-2)
    ///     - Redundant ShouldLog checks eliminated (P0-4)
    ///     - Conditional compilation for debug logs (P1-2)
    ///     - Unity Profiler integration for performance analysis (P2-1)
    ///     Structured Logging:
    ///     - Supports context dictionaries for key-value pairs
    ///     - Integrates with LogContext for correlation IDs and scoped properties
    ///     - Context is automatically captured from LogContext when available
    /// </remarks>
    public sealed class ConvaiLogger : ILogger
    {
        private static ConvaiLogger _instance;

        private static readonly List<ILogSink> _sinksList = new();
        private static volatile ILogSink[] _sinksSnapshot = Array.Empty<ILogSink>();
        private static readonly object _sinksLock = new();

        private static readonly ProfilerMarker _logMessageMarker = new("ConvaiLogger.LogMessage");
        private static readonly ProfilerMarker _dispatchToSinksMarker = new("ConvaiLogger.DispatchToSinks");
        private static readonly ProfilerMarker _shouldLogMarker = new("ConvaiLogger.ShouldLog");

        /// <summary>
        ///     Raised after the singleton logger instance has been initialized.
        /// </summary>
        public static Action<ConvaiLogger> OnInitializationCompleted = delegate { };

        private static readonly Dictionary<LogLevel, string> _levelColors = new()
        {
            { LogLevel.Debug, "cyan" },
            { LogLevel.Info, "grey" },
            { LogLevel.Warning, "yellow" },
            { LogLevel.Error, "red" }
        };

        private static readonly Dictionary<LogLevel, string> _levelNames = new()
        {
            { LogLevel.Trace, nameof(LogLevel.Trace) },
            { LogLevel.Debug, nameof(LogLevel.Debug) },
            { LogLevel.Info, nameof(LogLevel.Info) },
            { LogLevel.Warning, nameof(LogLevel.Warning) },
            { LogLevel.Error, nameof(LogLevel.Error) },
            { LogLevel.Off, nameof(LogLevel.Off) }
        };

        private static readonly Dictionary<LogCategory, string> _categoryNames = new()
        {
            { LogCategory.SDK, nameof(LogCategory.SDK) },
            { LogCategory.Character, nameof(LogCategory.Character) },
            { LogCategory.Audio, nameof(LogCategory.Audio) },
            { LogCategory.UI, nameof(LogCategory.UI) },
            { LogCategory.REST, nameof(LogCategory.REST) },
            { LogCategory.Transport, nameof(LogCategory.Transport) },
            { LogCategory.Events, nameof(LogCategory.Events) },
            { LogCategory.Player, nameof(LogCategory.Player) },
            { LogCategory.Editor, nameof(LogCategory.Editor) },
            { LogCategory.Vision, nameof(LogCategory.Vision) },
            { LogCategory.Bootstrap, nameof(LogCategory.Bootstrap) },
            { LogCategory.Transcript, nameof(LogCategory.Transcript) },
            { LogCategory.Narrative, nameof(LogCategory.Narrative) },
            { LogCategory.LipSync, nameof(LogCategory.LipSync) }
        };

        /// <summary>
        ///     Gets the singleton logger instance. Auto-initializes if not already initialized.
        /// </summary>
        public static ConvaiLogger Instance
        {
            get
            {
                if (_instance == null) Initialize();
                return _instance;
            }
        }

        /// <summary>
        ///     Initializes the singleton logger instance and registers the default sinks.
        /// </summary>
        public static void Initialize()
        {
            _instance = new ConvaiLogger();

            LogEntry.SetFrameCountProvider(() => Time.frameCount);

            lock (_sinksLock)
            {
                if (_sinksList.Count == 0)
                {
                    _sinksList.Add(new UnityConsoleSink());
                    UpdateSinksSnapshot();
                }
            }

            OnInitializationCompleted?.Invoke(_instance);
            Debug("SDK Level Logger attached", LogCategory.SDK);
        }

        /// <summary>
        ///     Determines if a message should be logged based on LoggingConfig.
        ///     Uses the Domain LogLevel system with per-category overrides.
        /// </summary>
        private static bool ShouldLog(LogLevel level, LogCategory category)
        {
            using ProfilerMarker.AutoScope _ = _shouldLogMarker.Auto();
            return LoggingConfig.IsEnabled(level, category);
        }

        /// <summary>
        ///     Main logging method with ShouldLog check.
        ///     For string messages where the check hasn't been done yet.
        ///     Instrumented with ProfilerMarker for performance analysis (P2-1).
        /// </summary>
        private static void LogMessage(string message, LogLevel level, LogCategory category, Exception exception = null)
        {
            using ProfilerMarker.AutoScope _ = _logMessageMarker.Auto();

            if (!ShouldLog(level, category)) return;

            LogMessageUnchecked(message, level, category, exception);
        }

        /// <summary>
        ///     Main logging method with ShouldLog check and context support.
        /// </summary>
        private static void LogMessage(string message, LogLevel level, LogCategory category,
            IReadOnlyDictionary<string, object> context, Exception exception = null)
        {
            using ProfilerMarker.AutoScope _ = _logMessageMarker.Auto();

            if (!ShouldLog(level, category)) return;

            LogMessageUnchecked(message, level, category, context, exception);
        }

        /// <summary>
        ///     Internal logging method that skips ShouldLog check.
        ///     Use when caller has already verified ShouldLog (P0-4 optimization).
        /// </summary>
        private static void LogMessageUnchecked(string message, LogLevel level, LogCategory category,
            Exception exception = null)
        {
            string correlationId = LogContext.CorrelationId;

            LogEntry entry = exception != null
                ? LogEntry.CreateWithException(level, category, message, exception, correlationId)
                : LogEntry.Create(level, category, message, correlationId);

            DispatchToSinks(entry);
        }

        /// <summary>
        ///     Internal logging method with context that skips ShouldLog check.
        ///     Merges provided context with LogContext properties.
        /// </summary>
        private static void LogMessageUnchecked(string message, LogLevel level, LogCategory category,
            IReadOnlyDictionary<string, object> context, Exception exception = null)
        {
            string correlationId = LogContext.CorrelationId;

            IReadOnlyDictionary<string, object> mergedContext = MergeContext(context);

            LogEntry entry;
            if (exception != null)
            {
                entry = mergedContext != null
                    ? LogEntry.CreateWithExceptionAndContext(level, category, message, exception, mergedContext,
                        correlationId)
                    : LogEntry.CreateWithException(level, category, message, exception, correlationId);
            }
            else
            {
                entry = mergedContext != null
                    ? LogEntry.CreateWithContext(level, category, message, mergedContext, correlationId)
                    : LogEntry.Create(level, category, message, correlationId);
            }

            DispatchToSinks(entry);
        }

        /// <summary>
        ///     Merges provided context with LogContext global/scoped properties.
        ///     Provided context takes precedence over LogContext properties.
        /// </summary>
        private static IReadOnlyDictionary<string, object> MergeContext(IReadOnlyDictionary<string, object> provided)
        {
            IReadOnlyDictionary<string, object> logContextProps = LogContext.GetAllProperties();

            if (provided == null && logContextProps == null) return null;

            if (provided == null) return logContextProps;

            if (logContextProps == null) return provided;

            var merged = new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> kvp in logContextProps) merged[kvp.Key] = kvp.Value;
            foreach (KeyValuePair<string, object> kvp in provided) merged[kvp.Key] = kvp.Value;
            return merged;
        }

        /// <summary>
        ///     Dispatches a log entry to all registered sinks.
        ///     Uses lock-free read of volatile snapshot for high-frequency performance.
        ///     Instrumented with ProfilerMarker for performance analysis (P2-1).
        /// </summary>
        private static void DispatchToSinks(in LogEntry entry)
        {
            using ProfilerMarker.AutoScope _ = _dispatchToSinksMarker.Auto();

            ILogSink[] sinks = _sinksSnapshot;
            for (int i = 0; i < sinks.Length; i++)
            {
                try
                {
                    sinks[i].WriteIfEnabled(entry);
                }
                catch
                {
                    // Swallow sink write failures to prevent a broken sink from
                    // disrupting all other sinks or the calling code path.
                }
            }
        }

        /// <summary>Logs a message using the default SDK category at debug level.</summary>
        /// <param name="message">Message object.</param>
        public void Log(object message) => Debug(message, LogCategory.SDK);

        #region Sink Management

        /// <summary>
        ///     Updates the volatile snapshot array from the list.
        ///     Must be called under _sinksLock.
        /// </summary>
        private static void UpdateSinksSnapshot() => _sinksSnapshot = _sinksList.ToArray();

        /// <summary>
        ///     Registers a log sink to receive log entries.
        ///     Uses copy-on-write pattern for lock-free dispatch.
        /// </summary>
        /// <param name="sink">The sink to register.</param>
        public static void RegisterSink(ILogSink sink)
        {
            if (sink == null) return;

            lock (_sinksLock)
            {
                if (!_sinksList.Contains(sink))
                {
                    _sinksList.Add(sink);
                    UpdateSinksSnapshot();
                }
            }
        }

        /// <summary>
        ///     Unregisters a log sink.
        ///     Uses copy-on-write pattern for lock-free dispatch.
        /// </summary>
        /// <param name="sink">The sink to unregister.</param>
        public static void UnregisterSink(ILogSink sink)
        {
            if (sink == null) return;

            lock (_sinksLock)
            {
                if (_sinksList.Remove(sink))
                    UpdateSinksSnapshot();
            }
        }

        /// <summary>
        ///     Removes all registered sinks and disposes them.
        /// </summary>
        public static void ClearSinks()
        {
            ILogSink[] sinksToDispose;
            lock (_sinksLock)
            {
                sinksToDispose = _sinksList.ToArray();
                _sinksList.Clear();
                UpdateSinksSnapshot();
            }

            foreach (ILogSink sink in sinksToDispose)
            {
                try { sink.Dispose(); }
                catch
                {
                    /* swallow disposal errors */
                }
            }
        }

        /// <summary>
        ///     Flushes all registered sinks.
        ///     Lock-free read of snapshot.
        /// </summary>
        public static void FlushAllSinks()
        {
            ILogSink[] sinks = _sinksSnapshot;
            foreach (ILogSink sink in sinks)
            {
                try { sink.Flush(); }
                catch
                {
                    /* swallow flush errors */
                }
            }
        }

        /// <summary>
        ///     Gets the count of registered sinks.
        ///     Lock-free read of snapshot.
        /// </summary>
        public static int SinkCount => _sinksSnapshot.Length;

        #endregion

        #region Static Convenience Methods

        /// <summary>
        ///     Logs an info message. Checks if logging is enabled before converting to string.
        ///     Calls LogMessageUnchecked to avoid redundant check (P0-4).
        /// </summary>
        public static void Info(object message, LogCategory category)
        {
            if (!ShouldLog(LogLevel.Info, category)) return;
            LogMessageUnchecked(message.ToString(), LogLevel.Info, category);
        }

        /// <summary>
        ///     Logs a debug message. Checks if logging is enabled before converting to string.
        ///     Calls LogMessageUnchecked to avoid redundant check (P0-4).
        ///     Conditionally compiled - stripped in release builds unless CONVAI_DEBUG_LOGGING is defined (P1-2).
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("CONVAI_DEBUG_LOGGING")]
        public static void Debug(object message, LogCategory category)
        {
            if (!ShouldLog(LogLevel.Debug, category)) return;
            LogMessageUnchecked(message.ToString(), LogLevel.Debug, category);
        }

        /// <summary>
        ///     Logs a warning message. Checks if logging is enabled before converting to string.
        ///     Calls LogMessageUnchecked to avoid redundant check (P0-4).
        /// </summary>
        public static void Warning(object message, LogCategory category)
        {
            if (!ShouldLog(LogLevel.Warning, category)) return;
            LogMessageUnchecked(message.ToString(), LogLevel.Warning, category);
        }

        /// <summary>
        ///     Logs an error message. Checks if logging is enabled before converting to string.
        ///     Calls LogMessageUnchecked to avoid redundant check (P0-4).
        /// </summary>
        public static void Error(object message, LogCategory category)
        {
            if (!ShouldLog(LogLevel.Error, category)) return;
            LogMessageUnchecked(message.ToString(), LogLevel.Error, category);
        }

        /// <summary>Logs an informational message.</summary>
        /// <param name="message">Message text.</param>
        /// <param name="category">Log category.</param>
        public static void Info(string message, LogCategory category) => LogMessage(message, LogLevel.Info, category);

        /// <summary>
        ///     Logs a debug message (string overload).
        ///     Conditionally compiled - stripped in release builds unless CONVAI_DEBUG_LOGGING is defined (P1-2).
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("CONVAI_DEBUG_LOGGING")]
        public static void Debug(string message, LogCategory category) => LogMessage(message, LogLevel.Debug, category);

        /// <summary>Logs a warning message.</summary>
        /// <param name="message">Message text.</param>
        /// <param name="category">Log category.</param>
        public static void Warning(string message, LogCategory category) =>
            LogMessage(message, LogLevel.Warning, category);

        /// <summary>Logs an error message.</summary>
        /// <param name="message">Message text.</param>
        /// <param name="category">Log category.</param>
        public static void Error(string message, LogCategory category) => LogMessage(message, LogLevel.Error, category);

        /// <summary>Logs an exception message.</summary>
        /// <param name="message">Message text.</param>
        /// <param name="category">Log category.</param>
        public static void Exception(string message, LogCategory category) =>
            LogMessage(message, LogLevel.Error, category);

        /// <summary>
        ///     Logs an exception. Checks if error logging is enabled before formatting exception.
        ///     Calls LogMessageUnchecked to avoid redundant check (P0-4).
        /// </summary>
        public static void Exception(Exception ex, LogCategory category)
        {
            if (!ShouldLog(LogLevel.Error, category)) return;
            string message = $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
            LogMessageUnchecked(message, LogLevel.Error, category);
        }

        #endregion

        #region ILogger Implementation

        /// <summary>
        ///     Implements ILogger.Log with typed LogCategory.
        /// </summary>
        public void Log(LogLevel level, string message, LogCategory category = LogCategory.SDK)
        {
            switch (level)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                    Debug(message, category);
                    break;
                case LogLevel.Info:
                    Info(message, category);
                    break;
                case LogLevel.Warning:
                    Warning(message, category);
                    break;
                case LogLevel.Error:
                    Error(message, category);
                    break;
            }
        }

        /// <summary>
        ///     Implements ILogger.Log with structured context.
        /// </summary>
        public void Log(LogLevel level, string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK) => LogMessage(message, level, category, context);

        void ILogger.Debug(string message, LogCategory category) => Debug(message, category);

        /// <summary>
        ///     Implements ILogger.Debug with structured context.
        /// </summary>
        public void Debug(string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK)
        {
            if (!ShouldLog(LogLevel.Debug, category)) return;
            LogMessageUnchecked(message, LogLevel.Debug, category, context);
        }

        void ILogger.Info(string message, LogCategory category) => Info(message, category);

        /// <summary>
        ///     Implements ILogger.Info with structured context.
        /// </summary>
        public void Info(string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK)
        {
            if (!ShouldLog(LogLevel.Info, category)) return;
            LogMessageUnchecked(message, LogLevel.Info, category, context);
        }

        void ILogger.Warning(string message, LogCategory category) => Warning(message, category);

        /// <summary>
        ///     Implements ILogger.Warning with structured context.
        /// </summary>
        public void Warning(string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK)
        {
            if (!ShouldLog(LogLevel.Warning, category)) return;
            LogMessageUnchecked(message, LogLevel.Warning, category, context);
        }

        void ILogger.Error(string message, LogCategory category) => Error(message, category);

        /// <summary>
        ///     Implements ILogger.Error with structured context.
        /// </summary>
        public void Error(string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK)
        {
            if (!ShouldLog(LogLevel.Error, category)) return;
            LogMessageUnchecked(message, LogLevel.Error, category, context);
        }

        /// <summary>
        ///     Implements ILogger.Error with exception support.
        ///     Checks if error logging is enabled before formatting exception to avoid allocation.
        /// </summary>
        public void Error(Exception exception, string message = null, LogCategory category = LogCategory.SDK)
        {
            if (!IsEnabled(LogLevel.Error, category)) return;

            string fullMessage = string.IsNullOrEmpty(message)
                ? $"{exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}"
                : $"{message}\n{exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}";
            Error(fullMessage, category);
        }

        /// <summary>
        ///     Implements ILogger.Error with exception and structured context.
        /// </summary>
        public void Error(Exception exception, string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK)
        {
            if (!IsEnabled(LogLevel.Error, category)) return;

            string fullMessage = string.IsNullOrEmpty(message)
                ? $"{exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}"
                : $"{message}\n{exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}";
            LogMessageUnchecked(fullMessage, LogLevel.Error, category, context);
        }

        /// <summary>
        ///     Implements ILogger.IsEnabled for performance optimization.
        /// </summary>
        public bool IsEnabled(LogLevel level, LogCategory category) => LoggingConfig.IsEnabled(level, category);

        #endregion

        #region Static Context Convenience Methods

        /// <summary>
        ///     Logs an info message with structured context (static convenience method).
        /// </summary>
        public static void InfoWithContext(string message, IReadOnlyDictionary<string, object> context,
            LogCategory category) => LogMessage(message, LogLevel.Info, category, context);

        /// <summary>
        ///     Logs a warning message with structured context (static convenience method).
        /// </summary>
        public static void WarningWithContext(string message, IReadOnlyDictionary<string, object> context,
            LogCategory category) => LogMessage(message, LogLevel.Warning, category, context);

        /// <summary>
        ///     Logs an error message with structured context (static convenience method).
        /// </summary>
        public static void ErrorWithContext(string message, IReadOnlyDictionary<string, object> context,
            LogCategory category) => LogMessage(message, LogLevel.Error, category, context);

        /// <summary>
        ///     Logs a debug message with structured context (static convenience method).
        ///     Conditionally compiled - stripped in release builds unless CONVAI_DEBUG_LOGGING is defined.
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("CONVAI_DEBUG_LOGGING")]
        public static void DebugWithContext(string message, IReadOnlyDictionary<string, object> context,
            LogCategory category) => LogMessage(message, LogLevel.Debug, category, context);

        #endregion

        #region String Template Methods (Zero-allocation when disabled)

        /// <summary>
        ///     Logs a debug message with a template and one argument.
        ///     Zero-allocation when debug logging is disabled.
        /// </summary>
        /// <example>
        ///     ConvaiLogger.Debug(LogCategory.Transport, "Received {0} bytes", byteCount);
        /// </example>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("CONVAI_DEBUG_LOGGING")]
        public static void Debug<T0>(LogCategory category, string template, T0 arg0)
        {
            if (!ShouldLog(LogLevel.Debug, category)) return;
            LogMessageUnchecked(string.Format(template, arg0), LogLevel.Debug, category);
        }

        /// <summary>
        ///     Logs a debug message with a template and two arguments.
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("CONVAI_DEBUG_LOGGING")]
        public static void Debug<T0, T1>(LogCategory category, string template, T0 arg0, T1 arg1)
        {
            if (!ShouldLog(LogLevel.Debug, category)) return;
            LogMessageUnchecked(string.Format(template, arg0, arg1), LogLevel.Debug, category);
        }

        /// <summary>
        ///     Logs a debug message with a template and three arguments.
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("CONVAI_DEBUG_LOGGING")]
        public static void Debug<T0, T1, T2>(LogCategory category, string template, T0 arg0, T1 arg1, T2 arg2)
        {
            if (!ShouldLog(LogLevel.Debug, category)) return;
            LogMessageUnchecked(string.Format(template, arg0, arg1, arg2), LogLevel.Debug, category);
        }

        /// <summary>
        ///     Logs an info message with a template and one argument.
        ///     Zero-allocation when info logging is disabled.
        /// </summary>
        public static void Info<T0>(LogCategory category, string template, T0 arg0)
        {
            if (!ShouldLog(LogLevel.Info, category)) return;
            LogMessageUnchecked(string.Format(template, arg0), LogLevel.Info, category);
        }

        /// <summary>
        ///     Logs an info message with a template and two arguments.
        /// </summary>
        public static void Info<T0, T1>(LogCategory category, string template, T0 arg0, T1 arg1)
        {
            if (!ShouldLog(LogLevel.Info, category)) return;
            LogMessageUnchecked(string.Format(template, arg0, arg1), LogLevel.Info, category);
        }

        /// <summary>
        ///     Logs an info message with a template and three arguments.
        /// </summary>
        public static void Info<T0, T1, T2>(LogCategory category, string template, T0 arg0, T1 arg1, T2 arg2)
        {
            if (!ShouldLog(LogLevel.Info, category)) return;
            LogMessageUnchecked(string.Format(template, arg0, arg1, arg2), LogLevel.Info, category);
        }

        /// <summary>
        ///     Logs a warning message with a template and one argument.
        ///     Zero-allocation when warning logging is disabled.
        /// </summary>
        public static void Warning<T0>(LogCategory category, string template, T0 arg0)
        {
            if (!ShouldLog(LogLevel.Warning, category)) return;
            LogMessageUnchecked(string.Format(template, arg0), LogLevel.Warning, category);
        }

        /// <summary>
        ///     Logs a warning message with a template and two arguments.
        /// </summary>
        public static void Warning<T0, T1>(LogCategory category, string template, T0 arg0, T1 arg1)
        {
            if (!ShouldLog(LogLevel.Warning, category)) return;
            LogMessageUnchecked(string.Format(template, arg0, arg1), LogLevel.Warning, category);
        }

        /// <summary>
        ///     Logs a warning message with a template and three arguments.
        /// </summary>
        public static void Warning<T0, T1, T2>(LogCategory category, string template, T0 arg0, T1 arg1, T2 arg2)
        {
            if (!ShouldLog(LogLevel.Warning, category)) return;
            LogMessageUnchecked(string.Format(template, arg0, arg1, arg2), LogLevel.Warning, category);
        }

        /// <summary>
        ///     Logs an error message with a template and one argument.
        ///     Zero-allocation when error logging is disabled.
        /// </summary>
        public static void Error<T0>(LogCategory category, string template, T0 arg0)
        {
            if (!ShouldLog(LogLevel.Error, category)) return;
            LogMessageUnchecked(string.Format(template, arg0), LogLevel.Error, category);
        }

        /// <summary>
        ///     Logs an error message with a template and two arguments.
        /// </summary>
        public static void Error<T0, T1>(LogCategory category, string template, T0 arg0, T1 arg1)
        {
            if (!ShouldLog(LogLevel.Error, category)) return;
            LogMessageUnchecked(string.Format(template, arg0, arg1), LogLevel.Error, category);
        }

        /// <summary>
        ///     Logs an error message with a template and three arguments.
        /// </summary>
        public static void Error<T0, T1, T2>(LogCategory category, string template, T0 arg0, T1 arg1, T2 arg2)
        {
            if (!ShouldLog(LogLevel.Error, category)) return;
            LogMessageUnchecked(string.Format(template, arg0, arg1, arg2), LogLevel.Error, category);
        }

        #endregion
    }
}
