using System;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Internal;
using LiveKit.Proto;
using UnityEngine;

namespace LiveKit
{
    /// <summary>
    /// Defines the type of audio source, influencing processing behavior.
    /// </summary>
    public enum RtcAudioSourceType
    {
        AudioSourceCustom = 0,
        AudioSourceMicrophone = 1
    }

    /// <summary>
    /// Capture source for a local audio track.
    /// </summary>
    public abstract class RtcAudioSource : IRtcSource, IDisposable
    {
        /// <summary>
        /// Event triggered when audio samples are captured from the underlying source.
        /// Provides the audio data, channel count, and sample rate.
        /// </summary>
        /// <remarks>
        /// This event is not guaranteed to be called on the main thread.
        /// </remarks>
        public abstract event Action<float[], int, int> AudioRead;

#if UNITY_IOS && !UNITY_EDITOR
        public static uint DefaultMicrophoneSampleRate = 24000;

        public static uint DefaultSampleRate = 48000;
#else
        public static uint DefaultSampleRate = 48000;
        public static uint DefaultMicrophoneSampleRate = DefaultSampleRate;
#endif
        public static uint DefaultChannels = 2;

        private readonly RtcAudioSourceType _sourceType;
        public RtcAudioSourceType SourceType => _sourceType;

        internal readonly FfiHandle Handle;
        protected AudioSourceInfo _info;
        private readonly AudioBuffer _captureBuffer = new();
        private readonly AudioProcessingModule _apm;
        private readonly ApmReverseStream _apmReverseStream;

        private bool _muted = false;
        public override bool Muted => _muted;

        private bool _started = false;
        private bool _disposed = false;

        protected RtcAudioSource(
            int channels = 2,
            RtcAudioSourceType audioSourceType = RtcAudioSourceType.AudioSourceCustom,
            bool enableAcousticEchoCancellation = false)
        {
            _sourceType = audioSourceType;
            bool isMicrophone = _sourceType == RtcAudioSourceType.AudioSourceMicrophone;

            if (enableAcousticEchoCancellation && isMicrophone)
            {
                try
                {
                    _apm = new AudioProcessingModule(true, true, true, true);
                    _apm.SetStreamDelayMs(EstimateStreamDelayMs());
                    _apmReverseStream = new ApmReverseStream(_apm);
                }
                catch (Exception ex)
                {
                    Utils.Error($"Failed to initialize acoustic echo cancellation: {ex.Message}");
                }
            }

            using var request = FFIBridge.Instance.NewRequest<NewAudioSourceRequest>();
            var newAudioSource = request.request;
            newAudioSource.Type = AudioSourceType.AudioSourceNative;
            newAudioSource.NumChannels = (uint)channels;
            newAudioSource.SampleRate = (uint)UnityEngine.AudioSettings.outputSampleRate;
            if (!enableAcousticEchoCancellation)
            {
                newAudioSource.Options = request.TempResource<AudioSourceOptions>();
                newAudioSource.Options.EchoCancellation = true;
                newAudioSource.Options.AutoGainControl = true;
                newAudioSource.Options.NoiseSuppression = true;
            }
            using var response = request.Send();
            FfiResponse res = response;
            _info = res.NewAudioSource.Source.Info;
            Handle = FfiHandle.FromOwnedHandle(res.NewAudioSource.Source.Handle);
        }

        /// <summary>
        /// Begin capturing audio samples from the underlying source.
        /// </summary>
        public virtual void Start()
        {
            if (_started) return;
            _apmReverseStream?.Start();
            AudioRead += OnAudioRead;
            _started = true;
        }

        /// <summary>
        /// Stop capturing audio samples from the underlying source.
        /// </summary>
        public virtual void Stop()
        {
            if (!_started) return;
            AudioRead -= OnAudioRead;
            _apmReverseStream?.Stop();
            _started = false;
        }

        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            if (_muted)
            {
                _captureBuffer.Clear();

                if (_sourceType == RtcAudioSourceType.AudioSourceMicrophone)
                {
                    Span<float> mutedSpan = data;
                    mutedSpan.Clear();
                }

                return;
            }

            _captureBuffer.Write(data, (uint)channels, (uint)sampleRate);
            while (true)
            {
                AudioFrame frame = _captureBuffer.ReadDuration(AudioProcessingModule.FrameDurationMs);
                if (frame == null)
                    break;

                try
                {
                    _apm?.ProcessStream(frame);
                    Capture(frame);
                }
                catch (Exception ex)
                {
                    frame.Dispose();
                    Utils.Error($"Audio processing failed: {ex.Message}");
                    break;
                }
            }

            if (_sourceType == RtcAudioSourceType.AudioSourceMicrophone)
            {
                Span<float> span = data;
                span.Clear();
            }
        }

        /// <summary>
        /// Mutes or unmutes the audio source.
        /// </summary>
        public override void SetMute(bool muted)
        {
            _muted = muted;
        }

        /// <summary>
        /// Disposes of the audio source, stopping it first if necessary.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing) Stop();
            _captureBuffer.Dispose();
            _apmReverseStream?.Dispose();
            _disposed = true;
        }

        ~RtcAudioSource()
        {
            Dispose(false);
        }

        private void Capture(AudioFrame frame)
        {
            using var request = FFIBridge.Instance.NewRequest<CaptureAudioFrameRequest>();
            using var audioFrameBufferInfo = request.TempResource<AudioFrameBufferInfo>();

            CaptureAudioFrameRequest pushFrame = request.request;
            pushFrame.SourceHandle = (ulong)Handle.DangerousGetHandle();
            pushFrame.Buffer = audioFrameBufferInfo;
            pushFrame.Buffer.DataPtr = (ulong)frame.Data.ToInt64();
            pushFrame.Buffer.NumChannels = frame.NumChannels;
            pushFrame.Buffer.SampleRate = frame.SampleRate;
            pushFrame.Buffer.SamplesPerChannel = frame.SamplesPerChannel;

            try
            {
                using var response = request.Send();
                FfiResponse ffiResponse = response;
                ulong asyncId = ffiResponse.CaptureAudioFrame.AsyncId;

                void Callback(CaptureAudioFrameCallback callback)
                {
                    if (callback.AsyncId != asyncId)
                        return;

                    if (callback.HasError)
                        Utils.Error($"Audio capture failed: {callback.Error}");

                    frame.Dispose();
                    FfiClient.Instance.CaptureAudioFrameReceived -= Callback;
                }

                FfiClient.Instance.CaptureAudioFrameReceived += Callback;
            }
            catch
            {
                frame.Dispose();
                throw;
            }
        }

        private static int EstimateStreamDelayMs()
        {
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int numBuffers);
            int sampleRate = AudioSettings.outputSampleRate;
            return sampleRate <= 0 ? 0 : 2 * (int)(1000f * bufferLength * numBuffers / sampleRate);
        }
    }
}
