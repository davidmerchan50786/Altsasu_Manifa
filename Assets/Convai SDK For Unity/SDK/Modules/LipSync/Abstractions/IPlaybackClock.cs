namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Contract for the clock that drives lip sync playback timing.
    ///     Implementations can be audio-locked (DSP), realtime, or manually driven.
    /// </summary>
    public interface IPlaybackClock
    {
        /// <summary>Elapsed playback time in seconds since Start was called, accounting for pauses.</summary>
        public double ElapsedSeconds { get; }

        /// <summary>Whether the clock source is actively providing valid time data.</summary>
        public bool IsValid { get; }

        public void StartClock();
        public void Pause();
        public void Resume();
        public void Reset();
    }
}
