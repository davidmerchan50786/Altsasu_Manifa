using System;

namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Immutable configuration for the lip sync playback engine.
    ///     Value type with no heap allocation on construction or assignment.
    /// </summary>
    public readonly struct LipSyncEngineConfig : IEquatable<LipSyncEngineConfig>
    {
        /// <summary>Default configuration with sensible production values.</summary>
        public static LipSyncEngineConfig Default => new();

        public float FadeOutDuration { get; }
        public float SmoothingFactor { get; }
        public float TimeOffsetSeconds { get; }
        public float MaxBufferedSeconds { get; }

        /// <summary>
        ///     Minimum headroom (in seconds) required before resuming from Starving to Playing.
        ///     Prevents rapid oscillation when data barely catches up to the playback position.
        /// </summary>
        public float MinResumeHeadroomSeconds { get; }

        public LipSyncEngineConfig(
            float fadeOutDuration = 0.2f,
            float smoothingFactor = 0f,
            float timeOffsetSeconds = -0.03f,
            float maxBufferedSeconds = 3f,
            float minResumeHeadroomSeconds = 0.12f)
        {
            FadeOutDuration = Math.Max(0.01f, fadeOutDuration);
            SmoothingFactor = Math.Clamp(smoothingFactor, 0f, 0.95f);
            TimeOffsetSeconds = Math.Clamp(timeOffsetSeconds, -1f, 1f);
            MaxBufferedSeconds = Math.Clamp(maxBufferedSeconds, 0.5f, 10f);
            MinResumeHeadroomSeconds = Math.Clamp(minResumeHeadroomSeconds, 0f, 1f);
        }

        public bool Equals(LipSyncEngineConfig other)
        {
            return FadeOutDuration.Equals(other.FadeOutDuration)
                   && SmoothingFactor.Equals(other.SmoothingFactor)
                   && TimeOffsetSeconds.Equals(other.TimeOffsetSeconds)
                   && MaxBufferedSeconds.Equals(other.MaxBufferedSeconds)
                   && MinResumeHeadroomSeconds.Equals(other.MinResumeHeadroomSeconds);
        }

        public override bool Equals(object obj) => obj is LipSyncEngineConfig other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = FadeOutDuration.GetHashCode();
                hash = (hash * 397) ^ SmoothingFactor.GetHashCode();
                hash = (hash * 397) ^ TimeOffsetSeconds.GetHashCode();
                hash = (hash * 397) ^ MaxBufferedSeconds.GetHashCode();
                hash = (hash * 397) ^ MinResumeHeadroomSeconds.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(LipSyncEngineConfig left, LipSyncEngineConfig right) => left.Equals(right);
        public static bool operator !=(LipSyncEngineConfig left, LipSyncEngineConfig right) => !left.Equals(right);
    }
}
