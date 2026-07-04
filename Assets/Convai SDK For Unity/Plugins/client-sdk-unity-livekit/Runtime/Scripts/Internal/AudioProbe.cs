using System;
using UnityEngine;

namespace LiveKit
{
    /// <summary>
    /// Intercepts audio data and invokes the <see cref="AudioRead"/> event.
    /// </summary>
    internal class AudioProbe : MonoBehaviour
    {
        public delegate void OnAudioDelegate(float[] data, int channels, int sampleRate);
        public event OnAudioDelegate AudioRead;
        private int _sampleRate;

        private volatile bool _clearAfterInvocation = false;

        /// <summary>
        /// Once called, the audio data will be cleared after each invocation of
        /// the <see cref="AudioRead"/> event.
        /// </summary>
        public void ClearAfterInvocation()
        {
            _clearAfterInvocation = true;
        }

        void OnEnable()
        {
            OnAudioConfigurationChanged(false);
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        void OnDisable()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
        }

        void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            _sampleRate = AudioSettings.outputSampleRate;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            AudioRead?.Invoke(data, channels, _sampleRate);
            if (_clearAfterInvocation) data.AsSpan().Clear();
        }

        private void OnDestroy()
        {
            AudioRead = null;
        }
    }
}
