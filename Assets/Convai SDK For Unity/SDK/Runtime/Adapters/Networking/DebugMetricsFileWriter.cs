#nullable enable
using System;
using System.IO;

namespace Convai.Runtime.Adapters.Networking
{
    /// <summary>
    ///     Writes debug metrics to a single text file. Each line: UTC (ISO8601) | eventType | payload.
    ///     Thread-safe; flushes after each write so data is durable for analytics.
    /// </summary>
    internal sealed class DebugMetricsFileWriter : IDebugMetricsFileWriter, IDisposable
    {
        private readonly object _lock = new();
        private readonly StreamWriter _writer;
        private bool _disposed;

        public DebugMetricsFileWriter(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path is required.", nameof(filePath));

            string directory = Path.GetDirectoryName(filePath) ?? "";
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            bool append = File.Exists(filePath);
            _writer = new StreamWriter(new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true
            };
            if (!append)
            {
                lock (_lock)
                    _writer.WriteLine("# Convai debug metrics. Each line: UTC (ISO8601) | event_type | payload");
            }
        }

        public void WriteLine(string eventType, string payload)
        {
            if (_disposed) return;

            string utc = DateTime.UtcNow.ToString("o");
            string safeType = (eventType ?? "").Replace("|", "_").Replace("\n", " ").Replace("\r", " ");
            string safePayload = (payload ?? "").Replace("\n", " ").Replace("\r", " ").Replace("|", ";");

            lock (_lock)
            {
                if (_disposed) return;
                _writer.WriteLine($"{utc} | {safeType} | {safePayload}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                try
                {
                    _writer?.Dispose();
                }
                catch
                {
                    // ignore on shutdown
                }
            }
        }
    }
}
