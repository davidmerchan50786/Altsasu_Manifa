using System;
using System.Collections.Generic;
using AOT;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using LiveKit;
using UnityEngine;

namespace Convai.Infrastructure.Networking.WebGL
{
    /// <summary>
    ///     WebGL implementation of <see cref="IAudioStream" /> for browser-based audio playback.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         On WebGL, audio is played through browser HTML audio elements rather than Unity's audio system.
    ///         This implementation provides the interface but actual audio is handled by the browser.
    ///     </para>
    /// </remarks>
    internal sealed class WebGLAudioStream : IAudioStream, IAudioPlaybackStateSource
    {
        internal static Func<HTMLAudioElement, bool> ElementPlayingEvaluator = IsElementPlaying;
        private static readonly Dictionary<IntPtr, WeakReference<WebGLAudioStream>> s_streamsByElement = new();
        private static readonly object s_streamsByElementLock = new();

        #region Constructor

        /// <summary>
        ///     Creates a new WebGL audio stream.
        /// </summary>
        /// <param name="track">The remote track to stream audio from.</param>
        public WebGLAudioStream(RemoteTrack track)
        {
            _track = track ?? throw new ArgumentNullException(nameof(track));
        }

        #endregion

        #region IDisposable

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;

            TeardownPlaybackTracking();
            Detach();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        #endregion

        private sealed class ElementRegistration
        {
            public ElementRegistration(HTMLAudioElement element, JSRef playingListener, JSRef pauseListener,
                JSRef endedListener)
            {
                Element = element;
                PlayingListener = playingListener;
                PauseListener = pauseListener;
                EndedListener = endedListener;
            }

            public HTMLAudioElement Element { get; }
            public JSRef PlayingListener { get; }
            public JSRef PauseListener { get; }
            public JSRef EndedListener { get; }
        }

        #region IAudioStream Events

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, raw audio data is not accessible from JavaScript audio elements.
        ///     This event will not fire. Use the native platform for audio data access.
        /// </remarks>
#pragma warning disable CS0067 // Event is never used - required by interface but WebGL doesn't provide raw audio data
        public event Action<float[], int, int> AudioDataReceived;
#pragma warning restore CS0067

        public event Action PlaybackStarted
        {
            add
            {
                bool wasTrackingInitialized = _playbackTrackingInitialized;
                _playbackStarted += value;
                EnsurePlaybackTrackingInitialized();

                if (ShouldInvokePlaybackStartedImmediately(wasTrackingInitialized, _playingElementHandles.Count))
                    value?.Invoke();
            }
            remove => _playbackStarted -= value;
        }

        public event Action PlaybackStopped
        {
            add
            {
                _playbackStopped += value;
                EnsurePlaybackTrackingInitialized();
            }
            remove => _playbackStopped -= value;
        }

        #endregion

        #region Private Fields

        private readonly RemoteTrack _track;
        private readonly HashSet<IntPtr> _playingElementHandles = new();
        private readonly Dictionary<IntPtr, ElementRegistration> _registeredElements = new();
        private bool _isActive;
        private bool _disposed;
        private bool _playbackTrackingInitialized;
        private Action _playbackStarted;
        private Action _playbackStopped;

        // Default audio parameters (browser handles actual values)
        private const int DefaultSampleRate = 48000;
        private const int DefaultChannels = 2;

        #endregion

        #region IAudioStream Properties

        /// <inheritdoc />
        public bool IsActive => _isActive && !_disposed;

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, the actual sample rate is determined by the browser's audio context.
        ///     This returns a default value.
        /// </remarks>
        public int SampleRate => DefaultSampleRate;

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, the actual channel count is determined by the browser.
        ///     This returns a default stereo value.
        /// </remarks>
        public int Channels => DefaultChannels;

        #endregion

        #region IAudioStream Methods

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, audio cannot be attached to a Unity AudioSource. Instead, audio plays
        ///     through browser HTML audio elements. This method will activate browser audio playback.
        /// </remarks>
        public void AttachToAudioSource(AudioSource target)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WebGLAudioStream));

            if (_isActive)
            {
                ConvaiLogger.Warning("[WebGLAudioStream] Stream is already active.", LogCategory.Audio);
                return;
            }

            // Attach to browser audio element
            HTMLMediaElement attachedElement = _track.Attach();
            _isActive = true;
            EnsurePlaybackTrackingInitialized();
            RegisterAttachedElement(attachedElement);
        }

        /// <inheritdoc />
        public void Detach()
        {
            if (!_isActive) return;

            _track.Detach();
            ClearRegisteredElements();
            _isActive = false;
        }

        #endregion

        #region Playback Tracking

        [MonoPInvokeCallback(typeof(JSNative.JSDelegate))]
        private static void OnHtmlAudioPlaying(IntPtr iptr)
        {
            using (var handle = new JSHandle(iptr, true))
            {
                if (!TryGetStream(handle.DangerousGetHandle(), out WebGLAudioStream stream))
                    return;

                stream.MarkElementPlaybackStarted(handle.DangerousGetHandle());
            }
        }

        [MonoPInvokeCallback(typeof(JSNative.JSDelegate))]
        private static void OnHtmlAudioStopped(IntPtr iptr)
        {
            using (var handle = new JSHandle(iptr, true))
            {
                if (!TryGetStream(handle.DangerousGetHandle(), out WebGLAudioStream stream))
                    return;

                stream.MarkElementPlaybackStopped(handle.DangerousGetHandle());
            }
        }

        private static bool TryGetStream(IntPtr elementHandle, out WebGLAudioStream stream)
        {
            lock (s_streamsByElementLock)
            {
                if (s_streamsByElement.TryGetValue(elementHandle, out WeakReference<WebGLAudioStream> reference) &&
                    reference.TryGetTarget(out stream) &&
                    stream != null)
                    return true;

                s_streamsByElement.Remove(elementHandle);
            }

            stream = null;
            return false;
        }

        private void EnsurePlaybackTrackingInitialized()
        {
            if (_disposed || _playbackTrackingInitialized)
                return;

            _playbackTrackingInitialized = true;
            _track.ElementAttached += OnTrackElementAttached;
            _track.ElementDetached += OnTrackElementDetached;

            foreach (HTMLMediaElement element in _track.AttachedElements)
                RegisterAttachedElement(element);

            if (_registeredElements.Count > 0)
                _isActive = true;
        }

        private void TeardownPlaybackTracking()
        {
            if (!_playbackTrackingInitialized)
                return;

            _track.ElementAttached -= OnTrackElementAttached;
            _track.ElementDetached -= OnTrackElementDetached;
            _playbackTrackingInitialized = false;
            ClearRegisteredElements();
        }

        private void OnTrackElementAttached(HTMLMediaElement element)
        {
            RegisterAttachedElement(element);
            _isActive = true;
        }

        private void OnTrackElementDetached(HTMLMediaElement element)
        {
            if (element == null)
                return;

            UnregisterElement(element.NativeHandle.DangerousGetHandle());
            _isActive = _registeredElements.Count > 0;
        }

        private void RegisterAttachedElement(HTMLMediaElement element)
        {
            var audioElement = element as HTMLAudioElement;
            if (audioElement == null)
                return;

            IntPtr elementHandle = audioElement.NativeHandle.DangerousGetHandle();
            if (_registeredElements.ContainsKey(elementHandle))
                return;

            lock (s_streamsByElementLock)
                s_streamsByElement[elementHandle] = new WeakReference<WebGLAudioStream>(this);

            JSRef playingListener =
                audioElement.AddEventListener("playing", OnHtmlAudioPlaying, audioElement.NativeHandle);
            JSRef pauseListener = audioElement.AddEventListener("pause", OnHtmlAudioStopped, audioElement.NativeHandle);
            JSRef endedListener = audioElement.AddEventListener("ended", OnHtmlAudioStopped, audioElement.NativeHandle);
            _registeredElements[elementHandle] =
                new ElementRegistration(audioElement, playingListener, pauseListener, endedListener);

            SyncPlaybackState(audioElement);
        }

        private void SyncPlaybackState(HTMLAudioElement audioElement)
        {
            IntPtr elementHandle = audioElement.NativeHandle.DangerousGetHandle();
            if (ElementPlayingEvaluator(audioElement))
                MarkElementPlaybackStarted(elementHandle);
            else
                MarkElementPlaybackStopped(elementHandle);
        }

        private void ClearRegisteredElements()
        {
            var elementHandles = new IntPtr[_registeredElements.Count];
            _registeredElements.Keys.CopyTo(elementHandles, 0);

            foreach (IntPtr elementHandle in elementHandles)
                UnregisterElement(elementHandle);
        }

        private void UnregisterElement(IntPtr elementHandle)
        {
            if (!_registeredElements.TryGetValue(elementHandle, out ElementRegistration registration))
                return;

            registration.Element.RemoveEventListener("playing", registration.PlayingListener);
            registration.Element.RemoveEventListener("pause", registration.PauseListener);
            registration.Element.RemoveEventListener("ended", registration.EndedListener);
            _registeredElements.Remove(elementHandle);

            lock (s_streamsByElementLock)
                s_streamsByElement.Remove(elementHandle);

            MarkElementPlaybackStopped(elementHandle);
        }

        private void MarkElementPlaybackStarted(IntPtr elementHandle)
        {
            if (_disposed || !_registeredElements.ContainsKey(elementHandle))
                return;

            if (!_playingElementHandles.Add(elementHandle))
                return;

            if (_playingElementHandles.Count != 1)
                return;
            _playbackStarted?.Invoke();
        }

        private void MarkElementPlaybackStopped(IntPtr elementHandle)
        {
            if (!_playingElementHandles.Remove(elementHandle))
                return;

            if (_playingElementHandles.Count != 0)
                return;
            _playbackStopped?.Invoke();
        }

        private static bool IsElementPlaying(HTMLAudioElement audioElement)
        {
            JSNative.PushString("paused");
            bool isPaused = JSNative.GetBoolean(JSNative.GetProperty(audioElement.NativeHandle));
            if (isPaused)
                return false;

            JSNative.PushString("ended");
            bool isEnded = JSNative.GetBoolean(JSNative.GetProperty(audioElement.NativeHandle));
            return !isEnded;
        }

        internal static bool ShouldInvokePlaybackStartedImmediately(bool wasTrackingInitialized,
            int playingElementCount) =>
            wasTrackingInitialized && playingElementCount > 0;

        internal static void ResetTestHooks() => ElementPlayingEvaluator = IsElementPlaying;

        #endregion
    }
}
