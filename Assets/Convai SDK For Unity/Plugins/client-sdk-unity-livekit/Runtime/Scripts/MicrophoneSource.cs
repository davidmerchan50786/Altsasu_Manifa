using System;
using System.Collections;
using System.Threading;
using UnityEngine;

namespace LiveKit
{
    /// <summary>
    /// An audio source which captures from the device's microphone.
    /// </summary>
    /// <remarks>
    /// Ensure microphone permissions are granted before calling <see cref="Start"/>.
    /// </remarks>
    sealed public class MicrophoneSource : RtcAudioSource
    {
        private const float AndroidAecStartupHealthCheckDelaySeconds = 0.5f;
        private const float AndroidSilentSignalThreshold = 0.000001f;

        private readonly bool _enableAcousticEchoCancellation;
        private readonly GameObject _sourceObject;
        private readonly string _deviceName;
        private string _activeDeviceName;

        public override event Action<float[], int, int> AudioRead;

        private bool _disposed = false;
        private bool _started = false;
        private int _captureAttemptId;
        private int _audioReadCountThisAttempt;
        private volatile bool _isMonitoringAndroidAecStartup;
        private bool _performedAndroidAecStartupRecovery;
        private volatile float _latestAudioPeak;

        /// <summary>
        /// Creates a new microphone source for the given device.
        /// </summary>
        /// <param name="deviceName">The name of the device to capture from. Use <see cref="Microphone.devices"/> to
        /// get the list of available devices.</param>
        /// <param name="sourceObject">The GameObject to attach the AudioSource to. The object must be kept in the scene
        /// for the duration of the source's lifetime.</param>
        /// <param name="enableAcousticEchoCancellation">Whether to enable explicit APM-based acoustic echo cancellation.</param>
        public MicrophoneSource(
            string deviceName,
            GameObject sourceObject,
            bool enableAcousticEchoCancellation = false)
            : base(2, RtcAudioSourceType.AudioSourceMicrophone, enableAcousticEchoCancellation)
        {
            _enableAcousticEchoCancellation = enableAcousticEchoCancellation;
            _deviceName = deviceName;
            _sourceObject = sourceObject;
        }

        /// <summary>
        /// Begins capturing audio from the microphone.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the microphone is not available or unauthorized.
        /// </exception>
        /// <remarks>
        /// Ensure microphone permissions are granted before calling this method
        /// by calling <see cref="Application.RequestUserAuthorization"/>.
        /// </remarks>
        public override void Start()
        {
            base.Start();
            if (_started) return;

            if (!Application.HasUserAuthorization(mode: UserAuthorization.Microphone))
                throw new InvalidOperationException("Microphone access not authorized");

            _isMonitoringAndroidAecStartup = false;
            _performedAndroidAecStartupRecovery = false;
            MonoBehaviourContext.OnApplicationPauseEvent += OnApplicationPause;
            MonoBehaviourContext.RunCoroutine(StartMicrophone());

            _started = true;
        }

        private IEnumerator StartMicrophone()
        {
            int attemptId = ++_captureAttemptId;
            _audioReadCountThisAttempt = 0;
            _latestAudioPeak = 0f;
            int sampleRate = AudioSettings.outputSampleRate;
            string resolvedDeviceName = ResolveCaptureDeviceName(_deviceName);
            AudioClip clip = null;

            try
            {
                clip = Microphone.Start(
                    resolvedDeviceName,
                    loop: true,
                    lengthSec: 1,
                    frequency: sampleRate
                );
            }
            catch (ArgumentException) when (!string.IsNullOrWhiteSpace(resolvedDeviceName))
            {
                resolvedDeviceName = null;
                clip = Microphone.Start(
                    resolvedDeviceName,
                    loop: true,
                    lengthSec: 1,
                    frequency: sampleRate
                );
            }

            if (clip == null)
                throw new InvalidOperationException("Microphone start failed");

            _activeDeviceName = resolvedDeviceName;

            var source = _sourceObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;

            var probe = _sourceObject.AddComponent<AudioProbe>();
            probe.ClearAfterInvocation();
            probe.AudioRead += OnAudioRead;

            var waitUntilReady = new WaitUntil(() => Microphone.GetPosition(_activeDeviceName) > 0);
            yield return waitUntilReady;
            bool shouldMonitorAndroidAecStartup = ShouldPerformAndroidAecStartupRecovery(attemptId);
            _isMonitoringAndroidAecStartup = shouldMonitorAndroidAecStartup;
            source.Play();

            if (shouldMonitorAndroidAecStartup)
                MonoBehaviourContext.RunCoroutine(MonitorAndroidAecStartupRecovery(attemptId));
        }

        /// <summary>
        /// Stops capturing audio from the microphone.
        /// </summary>
        public override void Stop()
        {
            base.Stop();
            _isMonitoringAndroidAecStartup = false;
            MonoBehaviourContext.RunCoroutine(StopMicrophone());
            MonoBehaviourContext.OnApplicationPauseEvent -= OnApplicationPause;
            _started = false;
        }

        private IEnumerator StopMicrophone()
        {
            if (string.IsNullOrWhiteSpace(_activeDeviceName))
            {
                Microphone.End(null);
            }
            else if (Microphone.IsRecording(_activeDeviceName))
            {
                Microphone.End(_activeDeviceName);
            }

            _activeDeviceName = null;

            var probe = _sourceObject.GetComponent<AudioProbe>();
            if (probe != null)
            {
                probe.AudioRead -= OnAudioRead;
                UnityEngine.Object.Destroy(probe);
            }

            var source = _sourceObject.GetComponent<AudioSource>();
            if (source != null)
            {
                UnityEngine.Object.Destroy(source);
            }
            yield return null;
        }

        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            if (_isMonitoringAndroidAecStartup)
            {
                float peak = 0f;
                for (int i = 0; i < data.Length; i++)
                {
                    float abs = Mathf.Abs(data[i]);
                    if (abs > peak)
                        peak = abs;
                }

                _latestAudioPeak = peak;
                Interlocked.Increment(ref _audioReadCountThisAttempt);
            }
            AudioRead?.Invoke(data, channels, sampleRate);
        }

        private void OnApplicationPause(bool pause)
        {
            if (!pause && _started)
                MonoBehaviourContext.RunCoroutine(RestartMicrophone());
        }

        private IEnumerator RestartMicrophone()
        {
            yield return StopMicrophone();
            yield return StartMicrophone();
        }

        // Some Android devices start the first AEC-backed capture session with silent PCM until the mic is restarted.
        private IEnumerator MonitorAndroidAecStartupRecovery(int attemptId)
        {
            yield return new WaitForSecondsRealtime(AndroidAecStartupHealthCheckDelaySeconds);

            if (!ShouldPerformAndroidAecStartupRecovery(attemptId))
            {
                _isMonitoringAndroidAecStartup = false;
                yield break;
            }

            bool shouldRestart =
                Volatile.Read(ref _audioReadCountThisAttempt) > 0 &&
                _latestAudioPeak <= AndroidSilentSignalThreshold;
            _isMonitoringAndroidAecStartup = false;
            if (!shouldRestart)
                yield break;

            _performedAndroidAecStartupRecovery = true;
            Debug.LogWarning(
                "[LiveKit] Android microphone started silently with AEC enabled. Restarting capture once.");
            yield return RestartMicrophone();
        }

        private static string ResolveCaptureDeviceName(string requestedDeviceName)
        {
            if (string.IsNullOrWhiteSpace(requestedDeviceName))
                return null;

            string[] devices = Microphone.devices;
            if (devices == null || devices.Length == 0)
                return null;

            for (int i = 0; i < devices.Length; i++)
                if (string.Equals(devices[i], requestedDeviceName, StringComparison.Ordinal))
                    return requestedDeviceName;

            for (int i = 0; i < devices.Length; i++)
                if (string.Equals(devices[i], requestedDeviceName, StringComparison.OrdinalIgnoreCase))
                    return devices[i];

            return null;
        }

        private bool ShouldPerformAndroidAecStartupRecovery(int attemptId) =>
            _started &&
            attemptId == _captureAttemptId &&
            attemptId == 1 &&
            !_performedAndroidAecStartupRecovery &&
            _enableAcousticEchoCancellation &&
            Application.platform == RuntimePlatform.Android;

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing) Stop();
            _disposed = true;
            base.Dispose(disposing);
        }

        ~MicrophoneSource()
        {
            Dispose(false);
        }
    }
}
