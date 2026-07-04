using System;
using LiveKit;

namespace Convai.Infrastructure.Networking.Native
{
    /// <summary>
    ///     Wraps LiveKit <see cref="AudioStream" /> to implement <see cref="IAudioPlaybackStateSource" />
    ///     so that AudioTrackManager can subscribe to playback start/stop and publish CharacterAudioPlaybackStateChanged.
    /// </summary>
    public sealed class NativeAudioStreamPlaybackAdapter : IDisposable, IAudioPlaybackStateSource
    {
        private readonly AudioStream _inner;
        private bool _disposed;

        public NativeAudioStreamPlaybackAdapter(AudioStream inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public event Action PlaybackStarted
        {
            add => _inner.PlaybackStarted += value;
            remove => _inner.PlaybackStarted -= value;
        }

        public event Action PlaybackStopped
        {
            add => _inner.PlaybackStopped += value;
            remove => _inner.PlaybackStopped -= value;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _inner.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
