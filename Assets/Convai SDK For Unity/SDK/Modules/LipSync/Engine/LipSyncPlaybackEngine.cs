using System;
using System.Collections.Generic;

namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Pure C# lip sync playback engine. Transport, clock, and output agnostic.
    ///     Receives timestamped frames via FeedFrames and produces interpolated values via Tick.
    ///     Uses a single runtime path with zero allocations in the hot path and Catmull-Rom interpolation.
    ///     Playback is gated by an external audio-start signal to keep output aligned with audible speech.
    /// </summary>
    public sealed class LipSyncPlaybackEngine
    {
        private readonly FrameRingBuffer _buffer = new();
        private readonly FadeController _fade = new();
        private bool _audioPlaybackStarted;

        private LipSyncEngineConfig _config = LipSyncEngineConfig.Default;

        private float[] _sampledValues = Array.Empty<float>();
        private bool _streamEndNotified;

        public LipSyncPlaybackEngine() { }

        public LipSyncPlaybackEngine(LipSyncEngineConfig config)
        {
            _config = config;
        }

        public PlaybackState State { get; private set; } = PlaybackState.Idle;

        public float[] OutputValues { get; private set; } = Array.Empty<float>();

        public IReadOnlyList<string> ChannelNames => _buffer.ChannelNames;
        public int ChannelCount => _buffer.ChannelCount;
        public float BufferedDuration => _buffer.Duration;
        public float FrameRate { get; private set; } = 60f;

        public bool IsPlaying => State == PlaybackState.Playing || State == PlaybackState.Starving;
        public bool IsFadingOut => State == PlaybackState.FadingOut;

        /// <summary>Total duration of all frames ingested since stream start (not limited by ring buffer capacity).</summary>
        public float TotalIngressDuration { get; private set; }

        public event Action<PlaybackState, PlaybackState> StateChanged;

        public void Configure(LipSyncEngineConfig config) => _config = config;

        /// <summary>Begin a new stream. Resets all playback state.</summary>
        public void BeginStream(IReadOnlyList<string> channelNames, float frameRate)
        {
            FullReset();
            FrameRate = Math.Max(1f, frameRate);
            _buffer.SetChannelLayout(channelNames);

            int channelCount = channelNames?.Count ?? 0;
            EnsureOutputArrays(channelCount);

            TransitionTo(PlaybackState.Buffering);
        }

        /// <summary>Feed new frames into the buffer. Timestamps are auto-computed from frame rate.</summary>
        public void FeedFrames(float[][] frames)
        {
            if (frames == null || frames.Length == 0) return;

            if (State == PlaybackState.Idle) return;

            if (State == PlaybackState.FadingOut) return;

            float startTime = TotalIngressDuration;
            _buffer.AppendFrames(frames, startTime, FrameRate, _config.MaxBufferedSeconds);
            TotalIngressDuration = startTime + (frames.Length / FrameRate);

            EnsureOutputArrays(_buffer.ChannelCount);
        }

        /// <summary>Signal that no more frames will arrive for this stream.</summary>
        public void NotifyStreamEnd() => _streamEndNotified = true;

        /// <summary>
        ///     Signals that remote audio playback has started (for example, CharacterAudioPlaybackStateChanged).
        ///     Unlocks the Buffering -> Playing transition so lip sync starts with actual audio output.
        /// </summary>
        public void NotifyAudioPlaybackStarted() => _audioPlaybackStarted = true;

        /// <summary>
        ///     Advances playback by one frame. Call once per LateUpdate.
        ///     Returns true if output values were updated this tick.
        /// </summary>
        /// <param name="clockElapsed">Elapsed seconds from the playback clock.</param>
        /// <param name="deltaTime">Frame delta time (for optional smoothing and fade).</param>
        public bool Tick(double clockElapsed, float deltaTime)
        {
            if (State == PlaybackState.FadingOut) return TickFadeOut(deltaTime);
            if (State == PlaybackState.Idle) return false;

            if (!_buffer.HasContent)
            {
                HandleBufferExhausted();
                return false;
            }

            if (!_audioPlaybackStarted)
            {
                TransitionTo(PlaybackState.Buffering);
                return false;
            }

            double elapsed = Math.Max(0d, clockElapsed + _config.TimeOffsetSeconds);
            float endTime = _buffer.EndTime;

            if (elapsed > endTime)
            {
                HandleBufferExhausted();
                elapsed = endTime;
            }
            else if (State == PlaybackState.Starving || State == PlaybackState.Buffering)
            {
                float headroom = endTime - (float)elapsed;
                if (State == PlaybackState.Starving)
                {
                    if (headroom >= _config.MinResumeHeadroomSeconds) TransitionTo(PlaybackState.Playing);
                }
                else if (headroom < _config.MinResumeHeadroomSeconds && !_streamEndNotified) return false;
            }

            bool sampled = SampleAtTime(elapsed, deltaTime);

            if (sampled && State == PlaybackState.Buffering) TransitionTo(PlaybackState.Playing);

            return sampled;
        }

        /// <summary>Immediately stop and zero output.</summary>
        public void Stop()
        {
            PlaybackState prev = State;
            FullReset();
            if (prev != PlaybackState.Idle) TransitionTo(PlaybackState.Idle);
        }

        /// <summary>Begin smooth fade-out from current values.</summary>
        public void StopSmooth()
        {
            if (State == PlaybackState.Idle || State == PlaybackState.FadingOut) return;

            _fade.Begin(OutputValues, _config.FadeOutDuration);
            TransitionTo(PlaybackState.FadingOut);
        }

        /// <summary>Remaining playback time based on buffer end minus logical elapsed.</summary>
        public float GetRemainingSeconds(double clockElapsed)
        {
            if (State == PlaybackState.Idle || State == PlaybackState.FadingOut) return 0f;

            double elapsed = Math.Max(0d, clockElapsed + _config.TimeOffsetSeconds);
            return Math.Max(0f, _buffer.EndTime - (float)elapsed);
        }

        /// <summary>Current headroom: how far the buffer end is ahead of the logical playback position.</summary>
        public float GetHeadroomSeconds(double clockElapsed)
        {
            if (State == PlaybackState.Idle || State == PlaybackState.FadingOut) return 0f;

            double elapsed = Math.Max(0d, clockElapsed + _config.TimeOffsetSeconds);
            return (float)(_buffer.EndTime - elapsed);
        }

        private bool SampleAtTime(double elapsed, float deltaTime)
        {
            int channelCount = _buffer.ChannelCount;
            if (channelCount <= 0) return false;

            EnsureOutputArrays(channelCount);

            bool usesSmoothing = _config.SmoothingFactor > 0f;

            if (!_buffer.TryGetFrameWindow(elapsed,
                    out float[] p0, out float[] p1, out float[] p2, out float[] p3,
                    out float alpha))
                return false;

            if (usesSmoothing)
            {
                FrameSampler.EvaluateCatmullRom(p0, p1, p2, p3, alpha, _sampledValues, channelCount);
                FrameSampler.ApplyTemporalSmoothing(_sampledValues, OutputValues, _config.SmoothingFactor, deltaTime,
                    channelCount);
            }
            else
                FrameSampler.EvaluateCatmullRom(p0, p1, p2, p3, alpha, OutputValues, channelCount);

            return true;
        }

        private void HandleBufferExhausted()
        {
            if (_streamEndNotified)
            {
                _fade.Begin(OutputValues, _config.FadeOutDuration);
                TransitionTo(PlaybackState.FadingOut);
                return;
            }

            if (State != PlaybackState.Starving) TransitionTo(PlaybackState.Starving);
        }

        private bool TickFadeOut(float deltaTime)
        {
            bool stillFading = _fade.Tick(deltaTime, OutputValues);
            if (!stillFading)
            {
                FullReset();
                TransitionTo(PlaybackState.Idle);
            }

            return true;
        }

        private void EnsureOutputArrays(int channelCount)
        {
            if (channelCount <= 0) return;

            if (OutputValues.Length != channelCount) OutputValues = new float[channelCount];

            if (_config.SmoothingFactor > 0f && _sampledValues.Length != channelCount)
                _sampledValues = new float[channelCount];
        }

        private void FullReset()
        {
            _buffer.Clear();
            _fade.Reset();
            FrameRate = 60f;
            TotalIngressDuration = 0f;
            _streamEndNotified = false;
            _audioPlaybackStarted = false;

            if (OutputValues.Length > 0) Array.Clear(OutputValues, 0, OutputValues.Length);

            if (_sampledValues.Length > 0) Array.Clear(_sampledValues, 0, _sampledValues.Length);
        }

        private void TransitionTo(PlaybackState next)
        {
            if (State == next) return;

            PlaybackState prev = State;
            State = next;
            StateChanged?.Invoke(prev, next);
        }
    }
}
