using System;
using System.Diagnostics;
using System.Threading;
using Convai.Domain.Logging;
using Convai.Domain.Models;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Coordinates player transcription state transitions for a single speaking session.
    /// </summary>
    internal sealed class PlayerTranscriptionCoordinator
    {
        private const int ProcessedFinalGracePeriodMs = 200;

        private readonly ILogger _logger;
        private readonly IConvaiPlayerEvents _playerEvents;
        private readonly Action<Action> _scheduleOnMainThread;
        private readonly object _stateLock = new();

        private string _asrFinalText = string.Empty;
        private bool _completionDispatched;
        private DateTime _ignoreProcessedFinalUntilUtc = DateTime.MinValue;
        private Timer _processedFinalGraceTimer;
        private string _processedFinalText = string.Empty;
        private bool _receivedAsrFinal;
        private bool _receivedProcessedFinal;
        private bool _sessionActive;
        private string _sessionId = string.Empty;
        private bool _stopPending;

        public PlayerTranscriptionCoordinator(IConvaiPlayerEvents playerEvents, Action<Action> scheduleOnMainThread,
            ILogger logger = null)
        {
            _playerEvents = playerEvents ?? throw new ArgumentNullException(nameof(playerEvents));
            _scheduleOnMainThread =
                scheduleOnMainThread ?? throw new ArgumentNullException(nameof(scheduleOnMainThread));
            _logger = logger;
        }

        /// <summary>
        ///     Gets the current speaker info for the session.
        /// </summary>
        public SpeakerInfo CurrentSpeakerInfo { get; private set; } = SpeakerInfo.Empty;

        /// <summary>
        ///     Gets the current speaking session identifier.
        /// </summary>
        public string CurrentSessionId
        {
            get
            {
                lock (_stateLock) return _sessionId;
            }
        }

        public void HandleStart()
        {
            bool shouldCompletePrevious;
            lock (_stateLock)
            {
                if (_sessionActive && !_stopPending) return;
                shouldCompletePrevious = _stopPending && HasAnyFinalLocked();
            }

            if (shouldCompletePrevious) CompleteSession();
            StartNewSession();
        }

        public void HandleInterim(string interimText)
        {
            EnsureSession();

            string safeText = interimText ?? string.Empty;
            Dispatch(() => _playerEvents.OnPlayerTranscriptionReceived(safeText, TranscriptionPhase.Interim));
        }

        public void HandleAsrFinal(string finalText)
        {
            EnsureSession();

            lock (_stateLock)
            {
                _asrFinalText = finalText ?? string.Empty;
                _receivedAsrFinal = _asrFinalText.Length > 0;
            }

            Dispatch(() => _playerEvents.OnPlayerTranscriptionReceived(_asrFinalText, TranscriptionPhase.AsrFinal));
        }

        public void HandleProcessedFinal(string cleanedText, SpeakerInfo speakerInfo)
        {
            bool ignoreLateProcessedFinal;
            EnsureSession();

            lock (_stateLock)
            {
                ignoreLateProcessedFinal = ShouldIgnoreLateProcessedFinalLocked();
                if (!ignoreLateProcessedFinal)
                {
                    _processedFinalText = cleanedText ?? string.Empty;
                    _receivedProcessedFinal = _processedFinalText.Length > 0;
                    CurrentSpeakerInfo = speakerInfo;
                    CancelGraceTimerLocked();
                }
            }

            if (ignoreLateProcessedFinal)
            {
                _logger?.Debug(
                    $"[PlayerTranscriptionCoordinator] Ignoring late processed final after grace window: \"{cleanedText}\"",
                    LogCategory.Player);
                return;
            }

            Dispatch(() =>
                _playerEvents.OnPlayerTranscriptionReceived(_processedFinalText, TranscriptionPhase.ProcessedFinal,
                    speakerInfo));

            bool shouldComplete;
            lock (_stateLock) shouldComplete = _stopPending;
            if (shouldComplete) CompleteSession();
        }

        public void HandleProcessedFinal(string cleanedText) => HandleProcessedFinal(cleanedText, SpeakerInfo.Empty);

        public void HandleStop()
        {
            string sessionId;
            bool completeImmediately;
            bool dispatchStopOnly;
            bool scheduleGraceTimer;

            lock (_stateLock)
            {
                if (string.IsNullOrEmpty(_sessionId)) return;

                sessionId = _sessionId;
                _stopPending = true;
                _sessionActive = false;

                completeImmediately = _receivedProcessedFinal;
                scheduleGraceTimer = !_receivedProcessedFinal && _receivedAsrFinal;
                dispatchStopOnly = !_receivedProcessedFinal && !_receivedAsrFinal;

                if (completeImmediately || dispatchStopOnly) CancelGraceTimerLocked();
                if (scheduleGraceTimer) ScheduleGraceTimerLocked(sessionId);

                if (dispatchStopOnly) ResetStateLocked(false);
            }

            if (dispatchStopOnly)
            {
                Dispatch(() => _playerEvents.OnPlayerStoppedSpeaking(sessionId, false));
                return;
            }

            if (completeImmediately) CompleteSession();
        }

        public void Reset()
        {
            lock (_stateLock)
            {
                CancelGraceTimerLocked();
                ResetStateLocked(true);
            }
        }

        private void StartNewSession()
        {
            string newSessionId;

            lock (_stateLock)
            {
                CancelGraceTimerLocked();
                ResetStateLocked(true);
                _sessionId = Guid.NewGuid().ToString("N");
                _sessionActive = true;
                newSessionId = _sessionId;
            }

            Dispatch(() => _playerEvents.OnPlayerStartedSpeaking(newSessionId));
            Dispatch(() => _playerEvents.OnPlayerTranscriptionReceived(string.Empty, TranscriptionPhase.Listening));
        }

        private void EnsureSession()
        {
            lock (_stateLock)
                if (!string.IsNullOrEmpty(_sessionId))
                    return;

            StartNewSession();
        }

        private void CompleteSession()
        {
            string sessionId;
            bool producedFinal;
            SpeakerInfo speakerInfo;

            lock (_stateLock)
            {
                if (_completionDispatched || string.IsNullOrEmpty(_sessionId))
                {
                    ResetStateLocked(false);
                    return;
                }

                sessionId = _sessionId;
                producedFinal = _receivedAsrFinal || _receivedProcessedFinal;
                speakerInfo = CurrentSpeakerInfo;
                _completionDispatched = true;

                if (_stopPending && _receivedAsrFinal && !_receivedProcessedFinal)
                    _ignoreProcessedFinalUntilUtc = DateTime.UtcNow.AddMilliseconds(ProcessedFinalGracePeriodMs);

                CancelGraceTimerLocked();
                ResetStateLocked(false);
            }

            Dispatch(() =>
                _playerEvents.OnPlayerTranscriptionReceived(string.Empty, TranscriptionPhase.Completed, speakerInfo));
            Dispatch(() => _playerEvents.OnPlayerStoppedSpeaking(sessionId, producedFinal));
        }

        private void ScheduleGraceTimerLocked(string sessionId)
        {
            CancelGraceTimerLocked();
            _processedFinalGraceTimer = new Timer(_ => OnProcessedFinalGraceExpired(sessionId), null,
                ProcessedFinalGracePeriodMs, Timeout.Infinite);
        }

        private void OnProcessedFinalGraceExpired(string sessionId)
        {
            bool shouldComplete;

            lock (_stateLock)
            {
                shouldComplete = !_completionDispatched &&
                                 _stopPending &&
                                 _receivedAsrFinal &&
                                 !_receivedProcessedFinal &&
                                 _sessionId == sessionId;
            }

            if (shouldComplete) CompleteSession();
        }

        private bool HasAnyFinalLocked() => _receivedAsrFinal || _receivedProcessedFinal;

        private bool ShouldIgnoreLateProcessedFinalLocked()
        {
            if (!string.IsNullOrEmpty(_sessionId)) return false;
            return DateTime.UtcNow <= _ignoreProcessedFinalUntilUtc;
        }

        private void CancelGraceTimerLocked()
        {
            _processedFinalGraceTimer?.Dispose();
            _processedFinalGraceTimer = null;
        }

        private void ResetStateLocked(bool clearIgnoreWindow)
        {
            _sessionId = string.Empty;
            _sessionActive = false;
            _receivedAsrFinal = false;
            _receivedProcessedFinal = false;
            _stopPending = false;
            _completionDispatched = false;
            _asrFinalText = string.Empty;
            _processedFinalText = string.Empty;
            CurrentSpeakerInfo = SpeakerInfo.Empty;

            if (clearIgnoreWindow) _ignoreProcessedFinalUntilUtc = DateTime.MinValue;
        }

        private void Dispatch(Action action)
        {
            void SafeInvoke()
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PlayerTranscriptionCoordinator] Exception in dispatch callback: {ex}");
                }
            }

            _scheduleOnMainThread(SafeInvoke);
        }
    }
}
