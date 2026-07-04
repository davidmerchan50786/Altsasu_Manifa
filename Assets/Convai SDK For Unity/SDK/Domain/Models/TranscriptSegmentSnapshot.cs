using System;

namespace Convai.Domain.Models
{
    /// <summary>
    ///     Identifies the normalized source for a transcript segment.
    /// </summary>
    public enum TranscriptSegmentSourceKind
    {
        Unknown = 0,
        PlayerAsr = 1,
        PlayerProcessedFinal = 2,
        CharacterTranscript = 3
    }

    /// <summary>
    ///     Immutable snapshot of a normalized transcript segment within a turn.
    /// </summary>
    public sealed class TranscriptSegmentSnapshot
    {
        public TranscriptSegmentSnapshot(
            string segmentId,
            string turnId,
            TranscriptParticipantRef participant,
            string committedText,
            string interimText,
            string processedOverrideText,
            DateTime startedAtUtc,
            DateTime updatedAtUtc,
            DateTime? stoppedAtUtc,
            TranscriptLifecycle lifecycle,
            TranscriptSegmentSourceKind sourceKind)
        {
            SegmentId = segmentId ?? string.Empty;
            TurnId = turnId ?? string.Empty;
            Participant = participant;
            CommittedText = committedText ?? string.Empty;
            InterimText = interimText ?? string.Empty;
            ProcessedOverrideText = processedOverrideText ?? string.Empty;
            StartedAtUtc = startedAtUtc;
            UpdatedAtUtc = updatedAtUtc;
            StoppedAtUtc = stoppedAtUtc;
            Lifecycle = lifecycle;
            SourceKind = sourceKind;
        }

        public string SegmentId { get; }

        public string TurnId { get; }

        public TranscriptParticipantRef Participant { get; }

        public string CommittedText { get; }

        public string InterimText { get; }

        public string ProcessedOverrideText { get; }

        public string DisplayText => !string.IsNullOrWhiteSpace(ProcessedOverrideText)
            ? ProcessedOverrideText
            : Combine(CommittedText, InterimText);

        public DateTime StartedAtUtc { get; }

        public DateTime UpdatedAtUtc { get; }

        public DateTime? StoppedAtUtc { get; }

        public TranscriptLifecycle Lifecycle { get; }

        public TranscriptSegmentSourceKind SourceKind { get; }

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
