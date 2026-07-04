using System;
using Convai.Domain.Models;

namespace Convai.Domain.DomainEvents.Transcript
{
    /// <summary>
    ///     Raised when a processed final user transcription arrives from the backend.
    /// </summary>
    public readonly struct FinalUserTranscriptionReceived
    {
        public FinalUserTranscriptionReceived(string text, SpeakerInfo speakerInfo, DateTime timestamp)
        {
            Text = text ?? string.Empty;
            SpeakerInfo = speakerInfo.IsValid ? speakerInfo : SpeakerInfo.Empty;
            Timestamp = timestamp;
        }

        public string Text { get; }
        public SpeakerInfo SpeakerInfo { get; }
        public DateTime Timestamp { get; }
        public string SpeakerId => SpeakerInfo.SpeakerId;
        public string SpeakerName => SpeakerInfo.SpeakerName;
        public string ParticipantId => SpeakerInfo.ParticipantId;

        public static FinalUserTranscriptionReceived Create(string text, SpeakerInfo speakerInfo) =>
            new(text, speakerInfo, DateTime.UtcNow);
    }
}
