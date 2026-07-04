using System;
using System.Collections.Generic;

namespace Convai.Domain.Models
{
    /// <summary>
    ///     Immutable room-scoped transcript turn snapshot.
    /// </summary>
    public sealed class TranscriptTurnSnapshot
    {
        public TranscriptTurnSnapshot(
            string turnId,
            long roomSequence,
            TranscriptParticipantRef participant,
            DateTime startedAtUtc,
            DateTime lastUpdatedAtUtc,
            DateTime? completedAtUtc,
            TranscriptLifecycle lifecycle,
            string committedText,
            string interimText,
            bool wasInterrupted,
            IReadOnlyList<TranscriptSegmentSnapshot> segments,
            string conversationTargetCharacterId = null)
        {
            TurnId = turnId ?? string.Empty;
            RoomSequence = roomSequence;
            Participant = participant;
            StartedAtUtc = startedAtUtc;
            LastUpdatedAtUtc = lastUpdatedAtUtc;
            CompletedAtUtc = completedAtUtc;
            Lifecycle = lifecycle;
            CommittedText = committedText ?? string.Empty;
            InterimText = interimText ?? string.Empty;
            WasInterrupted = wasInterrupted;
            Segments = segments ?? Array.Empty<TranscriptSegmentSnapshot>();
            ConversationTargetCharacterId = conversationTargetCharacterId ?? string.Empty;
        }

        public string TurnId { get; }

        public string MessageId => TurnId;

        public long RoomSequence { get; }

        public TranscriptParticipantRef Participant { get; }

        public DateTime StartedAtUtc { get; }

        public DateTime LastUpdatedAtUtc { get; }

        public DateTime? CompletedAtUtc { get; }

        public TranscriptLifecycle Lifecycle { get; }

        public string CommittedText { get; }

        public string InterimText { get; }

        public string DisplayText => Combine(CommittedText, InterimText);

        public bool WasInterrupted { get; }

        public IReadOnlyList<TranscriptSegmentSnapshot> Segments { get; }

        public string ConversationTargetCharacterId { get; }

        public bool HasText => !string.IsNullOrWhiteSpace(DisplayText);

        private static string Combine(string committedText, string interimText)
        {
            bool hasCommitted = !string.IsNullOrWhiteSpace(committedText);
            bool hasInterim = !string.IsNullOrWhiteSpace(interimText);

            if (!hasCommitted) return interimText ?? string.Empty;
            if (!hasInterim) return committedText ?? string.Empty;

            char lastChar = committedText[committedText.Length - 1];
            return char.IsWhiteSpace(lastChar)
                ? committedText + interimText
                : committedText + " " + interimText;
        }
    }
}
