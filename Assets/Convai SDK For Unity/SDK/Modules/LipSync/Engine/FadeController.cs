using System;

namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Controls smooth fade-out of blendshape values to zero.
    ///     Uses clamped delta time and ease-out quadratic curve.
    ///     Pure C# -- no UnityEngine dependency.
    /// </summary>
    internal sealed class FadeController
    {
        private float _duration;

        private float[] _snapshot = Array.Empty<float>();

        public bool IsActive { get; private set; }

        public float Progress { get; private set; }

        /// <summary>Begin fading out from the given blendshape values over the specified duration.</summary>
        public void Begin(float[] currentValues, float duration)
        {
            _duration = Math.Max(0.01f, duration);
            Progress = 0f;
            IsActive = true;

            if (currentValues == null || currentValues.Length == 0)
            {
                _snapshot = Array.Empty<float>();
                return;
            }

            if (_snapshot.Length != currentValues.Length) _snapshot = new float[currentValues.Length];

            Array.Copy(currentValues, _snapshot, currentValues.Length);
        }

        /// <summary>
        ///     Advances the fade and writes interpolated values to the output buffer.
        ///     Returns true while fading is still in progress, false when complete (output zeroed).
        /// </summary>
        public bool Tick(float deltaTime, float[] output)
        {
            if (!IsActive) return false;

            float dt = Math.Clamp(deltaTime, LipSyncConstants.MinDeltaTime, LipSyncConstants.MaxDeltaTimeForFade);
            Progress += dt / _duration;

            if (Progress >= 1f)
            {
                IsActive = false;
                Progress = 1f;
                if (output != null) Array.Clear(output, 0, output.Length);

                return false;
            }

            float alpha = 1f - ((1f - Progress) * (1f - Progress));

            int outputLength = output?.Length ?? 0;
            int limit = Math.Min(_snapshot.Length, outputLength);
            float scale = 1f - alpha;

            for (int i = 0; i < limit; i++) output[i] = _snapshot[i] * scale;

            for (int i = limit; i < outputLength; i++) output[i] = 0f;

            return true;
        }

        public void Reset()
        {
            IsActive = false;
            Progress = 0f;

            if (_snapshot.Length > 0) Array.Clear(_snapshot, 0, _snapshot.Length);
        }
    }
}
