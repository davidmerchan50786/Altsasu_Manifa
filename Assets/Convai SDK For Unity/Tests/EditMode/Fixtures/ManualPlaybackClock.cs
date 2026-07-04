using Convai.Modules.LipSync;

namespace Convai.Tests.EditMode.Fixtures
{
    public sealed class ManualPlaybackClock : IPlaybackClock
    {
        public double ElapsedSeconds { get; private set; }

        public bool IsValid { get; private set; } = true;

        public void StartClock()
        {
            ElapsedSeconds = 0d;
            IsValid = true;
        }

        public void Pause() { }
        public void Resume() { }

        public void Reset() => ElapsedSeconds = 0d;

        public void SetElapsed(double seconds) => ElapsedSeconds = seconds;

        public void SetValid(bool valid) => IsValid = valid;
    }
}
