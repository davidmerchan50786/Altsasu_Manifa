using System;
using UnityEngine;

namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Owns the lifecycle of the active <see cref="IPlaybackClock" /> and routes
    ///     <see cref="PlaybackClockCommand" /> values from the engine state machine to it.
    ///     The clock is created once in <see cref="Initialize" /> and remains stable
    ///     for the lifetime of the coordinator.
    /// </summary>
    internal sealed class PlaybackClockCoordinator : IPlaybackClockCoordinator
    {
        public IPlaybackClock CurrentClock { get; private set; }

        public void Initialize(Component context, LipSyncPlaybackEngine engine) =>
            CurrentClock = LipSyncClockResolver.Create();

        /// <summary>
        ///     Intentionally empty. The active clock instance is fixed after initialization.
        ///     This method is retained to satisfy the coordinator interface contract.
        /// </summary>
        public void Tick(LipSyncPlaybackEngine engine) { }

        public double GetElapsedSeconds() => Math.Max(0d, CurrentClock?.ElapsedSeconds ?? 0d);

        public void ApplyCommand(PlaybackClockCommand command)
        {
            if (CurrentClock == null)
                return;

            switch (command)
            {
                case PlaybackClockCommand.Start: CurrentClock.StartClock(); break;
                case PlaybackClockCommand.Pause: CurrentClock.Pause(); break;
                case PlaybackClockCommand.Resume: CurrentClock.Resume(); break;
                case PlaybackClockCommand.Reset: CurrentClock.Reset(); break;
            }
        }

        public void Reset() => CurrentClock?.Reset();

        public void Dispose() { }
    }
}
