using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Convai.Domain.Logging;
using Convai.Domain.Models;
using Convai.Runtime.Utilities;

namespace Convai.Application.Services.Transcript
{
    /// <summary>
    ///     Export format for conversation history.
    /// </summary>
    public enum ConversationExportFormat
    {
        /// <summary>Simple text format with timestamps.</summary>
        PlainText,

        /// <summary>JSON array format.</summary>
        Json,

        /// <summary>Markdown format with speaker headers.</summary>
        Markdown
    }

    /// <summary>
    ///     Entry in the conversation history.
    /// </summary>
    public sealed class TranscriptEntry
    {
        /// <summary>
        ///     Creates a new transcript entry.
        /// </summary>
        public TranscriptEntry(TranscriptMessage message, SpeakerType speaker)
        {
            Id = Guid.NewGuid().ToString("N")[..8];
            Message = message;
            Speaker = speaker;
        }

        /// <summary>
        ///     Unique identifier for this entry.
        /// </summary>
        public string Id { get; }

        /// <summary>
        ///     The transcript message content.
        /// </summary>
        public TranscriptMessage Message { get; }

        /// <summary>
        ///     Speaker type (Character or Player).
        /// </summary>
        public SpeakerType Speaker { get; }
    }

    /// <summary>
    ///     Interface for conversation history management.
    /// </summary>
    internal interface IConversationHistory : IDisposable
    {
        /// <summary>
        ///     Gets all transcript entries in chronological order.
        /// </summary>
        public IReadOnlyList<TranscriptEntry> Entries { get; }

        /// <summary>
        ///     Gets the number of entries in the history.
        /// </summary>
        public int Count { get; }

        /// <summary>
        ///     Gets entries for a specific player or character identifier.
        /// </summary>
        public IReadOnlyList<TranscriptEntry> GetEntriesByPlayerOrCharacterId(string playerOrCharacterId);

        /// <summary>
        ///     Clears all history entries.
        /// </summary>
        public void Clear();

        /// <summary>
        ///     Exports history in the specified format.
        /// </summary>
        public string Export(ConversationExportFormat format);

        /// <summary>
        ///     Raised when a new entry is added.
        /// </summary>
        public event Action<TranscriptEntry> EntryAdded;
    }

    /// <summary>
    ///     In-memory implementation of conversation history.
    ///     Subscribes to transcript events and maintains history automatically.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This service automatically captures final transcripts from both characters and players,
    ///         storing them in chronological order. It provides export functionality for saving
    ///         conversation logs in various formats.
    ///     </para>
    ///     This service consumes committed turns from the canonical room transcript engine.
    /// </remarks>
    public sealed class ConversationHistoryService : IConversationHistory
    {
        private readonly IRoomTranscriptEngine _engine;
        private readonly List<TranscriptEntry> _entries = new();
        private readonly object _lock = new();
        private readonly int _maxEntries;
        private readonly HashSet<string> _storedTurnIds = new();
        private bool _disposed;

        internal ConversationHistoryService(IRoomTranscriptEngine engine, int maxEntries = 0)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _maxEntries = maxEntries;
            _engine.Changed += OnTranscriptBatchChanged;
        }

        /// <inheritdoc />
        public event Action<TranscriptEntry> EntryAdded;

        /// <inheritdoc />
        public IReadOnlyList<TranscriptEntry> Entries
        {
            get
            {
                lock (_lock) return _entries.ToList().AsReadOnly();
            }
        }

        /// <inheritdoc />
        public int Count
        {
            get
            {
                lock (_lock) return _entries.Count;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<TranscriptEntry> GetEntriesByPlayerOrCharacterId(string playerOrCharacterId)
        {
            lock (_lock)
            {
                return _entries
                    .Where(e => e.Message.PlayerOrCharacterId == playerOrCharacterId)
                    .ToList()
                    .AsReadOnly();
            }
        }

        /// <inheritdoc />
        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
                _storedTurnIds.Clear();
            }
        }

        /// <inheritdoc />
        public string Export(ConversationExportFormat format)
        {
            List<TranscriptEntry> snapshot;
            lock (_lock) snapshot = _entries.ToList();

            return format switch
            {
                ConversationExportFormat.PlainText => ExportPlainText(snapshot),
                ConversationExportFormat.Json => ExportJson(snapshot),
                ConversationExportFormat.Markdown => ExportMarkdown(snapshot),
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _engine.Changed -= OnTranscriptBatchChanged;
        }

        private void AddEntry(TranscriptEntry entry)
        {
            lock (_lock)
            {
                if (_maxEntries > 0 && _entries.Count >= _maxEntries) _entries.RemoveAt(0);
                _entries.Add(entry);
            }

            SafeEventInvoker.Invoke(EntryAdded, entry, null, "ConversationHistoryService.EntryAdded", LogCategory.UI);
        }

        private void AddTurnEntry(string turnId, TranscriptEntry entry)
        {
            lock (_lock)
            {
                string key = string.IsNullOrWhiteSpace(turnId) ? entry.Message.PlayerOrCharacterId : turnId;
                if (_storedTurnIds.Contains(key)) return;

                _storedTurnIds.Add(key);

                if (_maxEntries > 0 && _entries.Count >= _maxEntries) _entries.RemoveAt(0);
                _entries.Add(entry);
            }

            SafeEventInvoker.Invoke(EntryAdded, entry, null, "ConversationHistoryService.EntryAdded", LogCategory.UI);
        }

        private void OnTranscriptBatchChanged(TranscriptUpdateBatch batch)
        {
            foreach (TranscriptTurnSnapshot turn in batch.ChangedTurns)
            {
                if (turn.Lifecycle != TranscriptLifecycle.Completed) continue;
                if (!turn.HasText) continue;

                TranscriptMessage message = turn.Participant.Kind == TranscriptParticipantKind.Player
                    ? TranscriptMessage.ForPlayer(
                        turn.DisplayText,
                        true,
                        turn.Participant.PlayerOrCharacterId,
                        turn.Participant.DisplayName,
                        turn.Participant.ParticipantId)
                    : TranscriptMessage.Create(
                        turn.Participant.PlayerOrCharacterId,
                        turn.Participant.DisplayName,
                        turn.DisplayText,
                        true,
                        participantId: turn.Participant.ParticipantId,
                        speakerType: SpeakerType.Character);

                SpeakerType speakerType = turn.Participant.Kind == TranscriptParticipantKind.Player
                    ? SpeakerType.Player
                    : SpeakerType.Character;

                AddTurnEntry(turn.TurnId, new TranscriptEntry(message, speakerType));
            }
        }

        private static string ExportPlainText(List<TranscriptEntry> entries)
        {
            StringBuilder sb = new();
            foreach (TranscriptEntry entry in entries)
            {
                sb.AppendLine(
                    $"[{entry.Message.Timestamp:HH:mm:ss}] {entry.Message.DisplayName}: {entry.Message.Text}");
            }

            return sb.ToString();
        }

        private static string ExportJson(List<TranscriptEntry> entries)
        {
            StringBuilder sb = new();
            sb.Append('[');
            for (int i = 0; i < entries.Count; i++)
            {
                TranscriptEntry e = entries[i];
                if (i > 0) sb.Append(',');
                sb.Append('{');
                sb.Append($"\"id\":\"{e.Id}\",");
                sb.Append($"\"speaker\":\"{EscapeJson(e.Message.DisplayName)}\",");
                sb.Append($"\"playerOrCharacterId\":\"{EscapeJson(e.Message.PlayerOrCharacterId)}\",");
                sb.Append($"\"speakerType\":\"{e.Speaker}\",");
                sb.Append($"\"text\":\"{EscapeJson(e.Message.Text)}\",");
                sb.Append($"\"timestamp\":\"{e.Message.Timestamp:O}\",");
                if (!string.IsNullOrEmpty(e.Message.ParticipantId))
                    sb.Append($"\"participantId\":\"{EscapeJson(e.Message.ParticipantId)}\",");
                sb.Append($"\"isFinal\":{(e.Message.IsFinal ? "true" : "false")}");
                sb.Append('}');
            }

            sb.Append(']');
            return sb.ToString();
        }

        private static string ExportMarkdown(List<TranscriptEntry> entries)
        {
            StringBuilder sb = new();
            sb.AppendLine("# Conversation Transcript");
            sb.AppendLine();
            sb.AppendLine($"*Exported at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            foreach (TranscriptEntry entry in entries)
            {
                string speakerType = entry.Speaker == SpeakerType.Character ? "AI" : "Player";
                sb.AppendLine($"**{entry.Message.DisplayName}** ({speakerType}) - {entry.Message.Timestamp:HH:mm:ss}");
                sb.AppendLine();
                sb.AppendLine($"> {entry.Message.Text}");
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine($"*Total messages: {entries.Count}*");

            return sb.ToString();
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}
