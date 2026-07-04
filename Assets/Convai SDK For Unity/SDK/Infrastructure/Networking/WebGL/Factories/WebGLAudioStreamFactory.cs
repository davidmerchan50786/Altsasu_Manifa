using System;
using UnityEngine;

namespace Convai.Infrastructure.Networking.WebGL
{
    /// <summary>
    ///     WebGL implementation of <see cref="IAudioStreamFactory" />.
    ///     Creates audio streams that route remote audio through browser audio elements.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         On WebGL, audio playback is handled by the browser through HTML audio elements,
    ///         not Unity's AudioSource. The AudioSource parameter is accepted for interface
    ///         compatibility but is not used - audio plays through the browser instead.
    ///     </para>
    /// </remarks>
    internal sealed class WebGLAudioStreamFactory : IAudioStreamFactory
    {
        /// <inheritdoc />
        /// <remarks>
        ///     Creates a WebGL audio stream. The audioSource parameter is accepted for
        ///     interface compatibility but audio is routed through browser audio elements.
        /// </remarks>
        public IDisposable Create(IRemoteAudioTrack track, AudioSource audioSource)
        {
            if (track == null) throw new ArgumentNullException(nameof(track));

            // Unwrap WebGL track to get the underlying LiveKit RemoteTrack
            if (track is WebGLRemoteAudioTrack webglTrack)
                return new WebGLAudioStream(webglTrack.UnderlyingTrack);

            throw new ArgumentException(
                $"Expected WebGLRemoteAudioTrack but got {track.GetType().Name}. " +
                "Cannot create audio stream for non-WebGL track types.",
                nameof(track));
        }
    }
}
