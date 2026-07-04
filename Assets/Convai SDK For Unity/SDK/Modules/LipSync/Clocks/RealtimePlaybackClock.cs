using UnityEngine;

namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Fallback playback clock using <see cref="Time.realtimeSinceStartupAsDouble" />.
    ///     Used when DSP-locked timing is unavailable.
    /// </summary>
    public sealed class RealtimePlaybackClock : IPlaybackClock
    {
        private PlaybackClockCore _core;

        public double ElapsedSeconds => _core.GetElapsed(Time.realtimeSinceStartupAsDouble);

        public bool IsValid => true;

        public void StartClock() => _core.Start(Time.realtimeSinceStartupAsDouble);

        public void Pause() => _core.Pause(Time.realtimeSinceStartupAsDouble);

        public void Resume() => _core.Resume(Time.realtimeSinceStartupAsDouble);

        public void Reset() => _core.Reset();
    }
}
