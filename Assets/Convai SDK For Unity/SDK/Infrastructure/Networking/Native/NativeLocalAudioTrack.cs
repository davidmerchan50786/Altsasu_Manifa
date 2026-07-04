using System;
using Convai.Infrastructure.Networking.Transport;
using LiveKit;

namespace Convai.Infrastructure.Networking.Native
{
    internal sealed class NativeLocalAudioTrack : ILocalAudioTrack
    {
        private readonly NativeMicrophoneSource _source;

        public NativeLocalAudioTrack(LocalAudioTrack track, NativeMicrophoneSource source)
        {
            UnderlyingTrack = track ?? throw new ArgumentNullException(nameof(track));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            IsPublished = true;
        }

        /// <summary>
        ///     Gets the underlying LiveKit local audio track.
        /// </summary>
        public LocalAudioTrack UnderlyingTrack { get; }

        public string Sid => UnderlyingTrack.Sid;

        public string Name => UnderlyingTrack.Name;

        public TrackKind Kind => TrackKind.Audio;

        public bool IsMuted => UnderlyingTrack.Muted;

        public event Action<bool> MuteChanged;

        public IAudioSource Source => _source;

        public bool IsPublished { get; private set; }

        public void SetMuted(bool muted)
        {
            ((LiveKit.ILocalTrack)UnderlyingTrack).SetMute(muted);
            MuteChanged?.Invoke(muted);
        }

        internal void MarkUnpublished() => IsPublished = false;
    }
}
