using System;

namespace Convai.Domain.DomainEvents.LipSync
{
    /// <summary>
    ///     Raised when the backend reports end-of-turn blendshape generation statistics.
    /// </summary>
    public readonly struct BlendshapeTurnStatsReceived
    {
        public BlendshapeTurnStatsReceived(
            string characterId,
            string participantId,
            int totalBlendshapes,
            int receivedBlendshapeFrames,
            int totalAudioBytes,
            double totalTurnDurationMs,
            double totalAudioDurationMs,
            double fps,
            DateTime timestamp)
        {
            CharacterId = characterId ?? string.Empty;
            ParticipantId = participantId ?? string.Empty;
            TotalBlendshapes = totalBlendshapes;
            ReceivedBlendshapeFrames = receivedBlendshapeFrames;
            TotalAudioBytes = totalAudioBytes;
            TotalTurnDurationMs = totalTurnDurationMs;
            TotalAudioDurationMs = totalAudioDurationMs;
            Fps = fps;
            Timestamp = timestamp;
        }

        public string CharacterId { get; }
        public string ParticipantId { get; }
        public int TotalBlendshapes { get; }
        public int ReceivedBlendshapeFrames { get; }
        public int TotalAudioBytes { get; }
        public double TotalTurnDurationMs { get; }
        public double TotalAudioDurationMs { get; }
        public double Fps { get; }
        public DateTime Timestamp { get; }
        public bool FrameCountMatches => TotalBlendshapes == ReceivedBlendshapeFrames;

        public static BlendshapeTurnStatsReceived Create(
            string characterId,
            string participantId,
            int totalBlendshapes,
            int receivedBlendshapeFrames,
            int totalAudioBytes,
            double totalTurnDurationMs,
            double totalAudioDurationMs,
            double fps)
        {
            return new BlendshapeTurnStatsReceived(
                characterId,
                participantId,
                totalBlendshapes,
                receivedBlendshapeFrames,
                totalAudioBytes,
                totalTurnDurationMs,
                totalAudioDurationMs,
                fps,
                DateTime.UtcNow);
        }
    }
}
