using System;
using UnityEngine;

namespace Convai.Modules.LipSync
{
    internal enum PlaybackClockCommand : byte
    {
        None = 0,
        Start,
        Pause,
        Resume,
        Reset
    }

    internal interface IPlaybackClockCoordinator : IDisposable
    {
        public IPlaybackClock CurrentClock { get; }
        public void Initialize(Component context, LipSyncPlaybackEngine engine);
        public void Tick(LipSyncPlaybackEngine engine);
        public double GetElapsedSeconds();
        public void ApplyCommand(PlaybackClockCommand command);
        public void Reset();
    }
}
