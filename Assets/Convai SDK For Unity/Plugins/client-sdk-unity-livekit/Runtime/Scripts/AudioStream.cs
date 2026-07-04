using System;
using System.Threading;
using UnityEngine;
using LiveKit.Internal;
using LiveKit.Proto;
using System.Runtime.InteropServices;
using LiveKit.Internal.FFIClients.Requests;

namespace LiveKit
{
    /// <summary>
    /// An audio stream from a remote participant, attached to an <see cref="AudioSource"/>
    /// in the scene. Raises PlaybackStarted/PlaybackStopped when actual audio signal is detected
    /// or silence exceeds a timeout (events are marshalled to the captured SynchronizationContext when available).
    /// </summary>
    public sealed class AudioStream : IDisposable
    {
        /// <summary>Peak (s16) above this value is considered "signal present" to start playback (~-36 dBFS on int16 full scale).</summary>
        private const int SignalStartThreshold = 500;
        /// <summary>Peak (s16) below this value is considered silence (hysteresis for stop) (~-42 dBFS on int16 full scale).</summary>
        private const int SignalStopThreshold = 250;
        /// <summary>Seconds of continuous silence before raising PlaybackStopped.</summary>
        private const double StopTimeoutSeconds = 0.5;

        internal readonly FfiHandle Handle;
        private readonly AudioSource _audioSource;
        private readonly SynchronizationContext _mainContext;
        private RingBuffer _buffer;
        private short[] _tempBuffer;
        private uint _numChannels;
        private uint _sampleRate;
        private AudioResampler _resampler = new AudioResampler();
        private object _lock = new object();
        private bool _disposed = false;
        private bool _isStreamingActive;
        private double _lastSignalDspTime;

        /// <summary>Raised when audio signal is first detected (actual playback started). Invoked on main thread when SynchronizationContext was captured.</summary>
        public event Action PlaybackStarted;
        /// <summary>Raised when silence has exceeded StopTimeoutSeconds. Invoked on main thread when SynchronizationContext was captured.</summary>
        public event Action PlaybackStopped;

        /// <summary>
        /// Creates a new audio stream from a remote audio track, attaching it to the
        /// given <see cref="AudioSource"/> in the scene.
        /// </summary>
        /// <param name="audioTrack">The remote audio track to stream.</param>
        /// <param name="source">The audio source to play the stream on.</param>
        /// <exception cref="InvalidOperationException">Thrown if the audio track's room or
        /// participant is invalid.</exception>
        public AudioStream(RemoteAudioTrack audioTrack, AudioSource source)
        {
            if (!audioTrack.Room.TryGetTarget(out var room))
                throw new InvalidOperationException("audiotrack's room is invalid");

            if (!audioTrack.Participant.TryGetTarget(out var participant))
                throw new InvalidOperationException("audiotrack's participant is invalid");

            _mainContext = SynchronizationContext.Current;

            using var request = FFIBridge.Instance.NewRequest<NewAudioStreamRequest>();
            var newAudioStream = request.request;
            newAudioStream.TrackHandle = (ulong)(audioTrack as ITrack).TrackHandle.DangerousGetHandle();
            newAudioStream.Type = AudioStreamType.AudioStreamNative;

            using var response = request.Send();
            FfiResponse res = response;
            Handle = FfiHandle.FromOwnedHandle(res.NewAudioStream.Stream.Handle);
            FfiClient.Instance.AudioStreamEventReceived += OnAudioStreamEvent;

            _audioSource = source;
            var probe = _audioSource.gameObject.AddComponent<AudioProbe>();
            probe.AudioRead += OnAudioRead;
            _audioSource.Play();
        }

        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            lock (_lock)
            {
                if (_buffer == null || channels != _numChannels || sampleRate != _sampleRate || data.Length != _tempBuffer.Length)
                {
                    int size = (int)(channels * sampleRate * 0.2);
                    _buffer?.Dispose();
                    _buffer = new RingBuffer(size * sizeof(short));
                    _tempBuffer = new short[data.Length];
                    _numChannels = (uint)channels;
                    _sampleRate = (uint)sampleRate;
                }

                static float S16ToFloat(short v)
                {
                    return v / 32768f;
                }

                var temp = MemoryMarshal.Cast<short, byte>(_tempBuffer.AsSpan().Slice(0, data.Length));
                int bytesRead = _buffer.Read(temp);
                int samplesRead = Math.Max(0, bytesRead / sizeof(short));
                if (samplesRead > data.Length)
                {
                    samplesRead = data.Length;
                }

                if (samplesRead < data.Length)
                {
                    Array.Clear(_tempBuffer, samplesRead, data.Length - samplesRead);
                }

                int peak = 0;
                for (int i = 0; i < samplesRead; i++)
                {
                    int abs = Math.Abs((int)_tempBuffer[i]);
                    if (abs > peak) peak = abs;
                }

                double dspNow = AudioSettings.dspTime;
                if (peak >= SignalStartThreshold)
                {
                    _lastSignalDspTime = dspNow;
                    if (!_isStreamingActive)
                    {
                        _isStreamingActive = true;
                        Post(() => PlaybackStarted?.Invoke());
                    }
                }
                else if (peak < SignalStopThreshold && _isStreamingActive)
                {
                    if ((dspNow - _lastSignalDspTime) > StopTimeoutSeconds)
                    {
                        _isStreamingActive = false;
                        Post(() => PlaybackStopped?.Invoke());
                    }
                }

                Array.Clear(data, 0, data.Length);
                for (int i = 0; i < samplesRead; i++)
                {
                    data[i] = S16ToFloat(_tempBuffer[i]);
                }
            }
        }

        private void Post(Action action)
        {
            if (action == null) return;
            if (_mainContext != null)
            {
                _mainContext.Post(_ => action(), null);
            }
            else
            {
                action();
            }
        }

        private void OnAudioStreamEvent(AudioStreamEvent e)
        {
            if ((ulong)Handle.DangerousGetHandle() != e.StreamHandle)
                return;

            if (e.MessageCase != AudioStreamEvent.MessageOneofCase.FrameReceived)
                return;

            var frame = new AudioFrame(e.FrameReceived.Frame);

            lock (_lock)
            {
                if (_numChannels == 0)
                    return;

                unsafe
                {
                    var uFrame = _resampler.RemixAndResample(frame, _numChannels, _sampleRate);
                    if (uFrame != null)
                    {
                        var data = new Span<byte>(uFrame.Data.ToPointer(), uFrame.Length);
                        _buffer?.Write(data);
                    }

                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                FfiClient.Instance.AudioStreamEventReceived -= OnAudioStreamEvent;

                if (_audioSource != null)
                {
                    _audioSource.Stop();
                    var probe = _audioSource.GetComponent<AudioProbe>();
                    if (probe != null)
                    {
                        UnityEngine.Object.Destroy(probe);
                    }
                }

                lock (_lock)
                {
                    _buffer?.Dispose();
                    _buffer = null;
                }
            }
            _disposed = true;
        }

        ~AudioStream()
        {
            Dispose(false);
        }
    }
}
