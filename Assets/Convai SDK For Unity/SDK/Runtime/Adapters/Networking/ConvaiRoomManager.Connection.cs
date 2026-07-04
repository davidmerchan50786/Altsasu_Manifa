using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.LipSync;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.Errors;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Models;
using Convai.Infrastructure.Networking.Transport;
using Convai.Infrastructure.Protocol.Messages;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;
using Convai.Runtime.Logging;
using Convai.Runtime.Room;
using UnityEngine;

namespace Convai.Runtime.Adapters.Networking
{
    public partial class ConvaiRoomManager
    {
        internal ResolvedTurnTakingOptions CurrentResolvedTurnTakingOptions =>
            _currentResolvedTurnTakingOptions ?? ResolvedTurnTakingOptions.DefaultHandsFree;

        public IConvaiOperation<RoomSession> ConnectAsync(CancellationToken cancellationToken = default) =>
            ConnectionCoordinator?.ConnectAsync(cancellationToken) ??
            ConvaiOperation<RoomSession>.Failed(
                new ConvaiOperationException(SessionErrorCodes.ConnectionFailed,
                    "[ConvaiRoomManager] ConnectAsync called before room coordinators were initialized."));

        public IConvaiOperation<RoomSession> ConnectAsync(
            RoomSessionConnectOptions options,
            CancellationToken cancellationToken = default)
        {
            RoomSessionConnectOptions pendingOptions = options?.Clone();
            IConvaiOperation<RoomSession> operation = ConnectAsync(cancellationToken);
            if (!TryQueuePendingConnectOptions(pendingOptions))
                return operation;

            return ConvaiOperation<RoomSession>.FromTask(
                ClearPendingConnectOptionsWhenUnusedAsync(operation.AsTask(), pendingOptions));
        }

        public IConvaiOperation<Unit> DisconnectAsync(DisconnectReason reason = DisconnectReason.ClientInitiated,
            CancellationToken cancellationToken = default) =>
            ConnectionCoordinator?.DisconnectAsync(cancellationToken) ??
            ConvaiOperation<Unit>.Succeeded(Unit.Value);

        public void DisconnectFromRoom()
        {
            DisconnectAsync().AsTask().ContinueWith(
                static t => ConvaiLogger.Error(
                    $"[ConvaiRoomManager] DisconnectAsync failed: {t.Exception?.GetBaseException().Message}",
                    LogCategory.SDK),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void HandleCoordinatorStateChanged(SessionStateChanged stateChanged)
        {
            OnSessionStateChanged?.Invoke(stateChanged);

            if (stateChanged.NewState == SessionState.Connected &&
                stateChanged.OldState != SessionState.Connected)
            {
                SubscribeToRtvMetrics();
                RequestAutoStartMicrophone("session-connected");
                Connected?.Invoke();
            }
        }

        private static string DescribeSessionState(SessionState state) => state switch
        {
            SessionState.Connecting => "transport connecting",
            SessionState.Connected => "character ready",
            SessionState.Reconnecting => "transport reconnecting",
            SessionState.Disconnecting => "disconnecting",
            SessionState.Disconnected => "disconnected",
            SessionState.Error => "error",
            _ => state.ToString()
        };

        private void SubscribeToRtvMetrics()
        {
            UnsubscribeFromRtvMetrics();
            RTVIHandler handler = _convaiRoomController?.RTVIHandler;
            if (handler == null) return;
            _metricsRtviHandler = handler;
            handler.OnMetricsReceived += ForwardRtvMetrics;
        }

        private void UnsubscribeFromRtvMetrics()
        {
            if (_metricsRtviHandler == null) return;

            _metricsRtviHandler.OnMetricsReceived -= ForwardRtvMetrics;
            _metricsRtviHandler = null;
        }

        private void ForwardRtvMetrics(RTVIMetricsPayload payload)
        {
            if (EffectiveDebug)
            {
                LogRtvMetrics(payload);
                if (_debugMetricsFileWriter != null && payload != null)
                {
                    var parts = new List<string>();
                    if (payload.Ttfb != null && payload.Ttfb.HasValues) parts.Add($"ttfb={payload.Ttfb}");
                    if (payload.Processing != null && payload.Processing.HasValues)
                        parts.Add($"processing={payload.Processing}");
                    if (payload.Custom != null && payload.Custom.HasValues) parts.Add($"custom={payload.Custom}");
                    if (parts.Count > 0)
                        _debugMetricsFileWriter.WriteLine("rtvi_metrics", string.Join(" ", parts));
                }
            }

            OnRtvMetricsReceived?.Invoke(payload);
        }

        private void LogRtvMetrics(RTVIMetricsPayload payload)
        {
            if (payload == null) return;

            bool hasTtfb = payload.Ttfb != null && payload.Ttfb.HasValues;
            bool hasProcessing = payload.Processing != null && payload.Processing.HasValues;
            bool hasCustom = payload.Custom != null && payload.Custom.HasValues;
            if (!hasTtfb && !hasProcessing && !hasCustom) return;

            var parts = new List<string>();
            if (hasTtfb) parts.Add($"ttfb={payload.Ttfb}");
            if (hasProcessing) parts.Add($"processing={payload.Processing}");
            if (hasCustom) parts.Add($"custom={payload.Custom}");
            ConvaiLogger.Info($"[ConvaiRoomManager] RTVI metrics: {string.Join(" | ", parts)}", LogCategory.Transport);
        }

        private void HandleUnexpectedRoomDisconnected()
        {
            ConvaiLogger.Info(
                "[ConvaiRoomManager] Room disconnected unexpectedly; clearing runtime media/session state.",
                LogCategory.SDK);
            ResetConnectionScopedRuntimeState();
            if (_roomDisconnectRuntimeAdapter != null)
            {
                _roomDisconnectRuntimeAdapter.HandleUnexpectedDisconnect(
                    CurrentState != SessionState.Disconnected,
                    "Handled unexpected room disconnect");
                return;
            }

            _audioTrackManager?.SetMicMuted(true);
            _audioTrackManager?.ClearState();
            CompleteDisconnectionTracking(CurrentState != SessionState.Disconnected,
                "Handled unexpected room disconnect");
        }

        private void HandlePlayerTextMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                ConvaiLogger.Warning("[ConvaiRoomManager] HandlePlayerTextMessage received empty text; ignoring.",
                    LogCategory.SDK);
                return;
            }

            if (!IsConnected)
            {
                ConvaiLogger.Warning(
                    "[ConvaiRoomManager] HandlePlayerTextMessage called while not connected; message dropped.",
                    LogCategory.SDK);
                return;
            }

            if (RtvHandler == null)
            {
                ConvaiLogger.Warning(
                    "[ConvaiRoomManager] HandlePlayerTextMessage: RtvHandler is null; message dropped.",
                    LogCategory.SDK);
                return;
            }

            RtvHandler.SendData(new RTVIUserTextMessage(text));
            ConvaiLogger.Debug($"[ConvaiRoomManager] Sent user text message: {text}", LogCategory.SDK);
        }

        private void HandleRemoteAudioTrackSubscribed(IRemoteAudioTrack audioTrack, string participantSid,
            string characterId) =>
            _characterLifecycleCoordinator?.HandleRemoteAudioTrackSubscribed(audioTrack, participantSid, characterId);

        private void HandleRemoteAudioTrackUnsubscribed(string participantSid, string characterId) =>
            _characterLifecycleCoordinator?.HandleRemoteAudioTrackUnsubscribed(participantSid);

        private void RequestAutoStartMicrophone(string reason)
        {
            if (_autoStartMicrophoneCompleted)
            {
                ConvaiLogger.Debug(
                    $"[ConvaiRoomManager] Auto-start microphone already completed for current connection; ignoring trigger ({reason}).",
                    LogCategory.SDK);
                return;
            }

            if (_autoStartMicrophonePending)
            {
                ConvaiLogger.Debug(
                    $"[ConvaiRoomManager] Auto-start microphone already scheduled; ignoring duplicate trigger ({reason}).",
                    LogCategory.SDK);
                return;
            }

            _autoStartMicrophonePending = true;
            _autoStartMicrophoneRequestId++;
            _autoStartMicrophoneCoroutine = StartCoroutine(AutoStartMicrophoneCoroutine(_autoStartMicrophoneRequestId));
            ConvaiLogger.Debug(
                $"[ConvaiRoomManager] Scheduled auto-start microphone request {_autoStartMicrophoneRequestId} ({reason}).",
                LogCategory.SDK);
        }

        private void ResetAutoStartMicrophoneState()
        {
            _autoStartMicrophoneRequestId++;
            _autoStartMicrophoneCompleted = false;
            _autoStartMicrophonePending = false;

            if (_autoStartMicrophoneCoroutine == null)
                return;

            StopCoroutine(_autoStartMicrophoneCoroutine);
            _autoStartMicrophoneCoroutine = null;
        }

        private IEnumerator AutoStartMicrophoneCoroutine(int requestId)
        {
            yield return new WaitForSeconds(_reconnectPolicy.AutoMicStartDelaySeconds);

            if (requestId != _autoStartMicrophoneRequestId)
                yield break;

            if (RequiresUserGestureForAudio && !IsAudioPlaybackActive)
            {
                ConvaiLogger.Debug(
                    "[ConvaiRoomManager] Skipping auto-start microphone: platform requires a user gesture first. " +
                    "Call EnableAudioAndStartListening() from a UI button.", LogCategory.SDK);
                _autoStartMicrophonePending = false;
                _autoStartMicrophoneCoroutine = null;
                yield break;
            }

            if (!IsConnected)
            {
                _autoStartMicrophonePending = false;
                _autoStartMicrophoneCoroutine = null;
                yield break;
            }

            if (!_currentResolvedTurnTakingOptions.ShouldAutoStartMicrophoneAfterConnect)
            {
                ConvaiLogger.Debug(
                    "[ConvaiRoomManager] Skipping auto-start microphone because the resolved turn-taking policy " +
                    "uses open-on-first-press push-to-talk startup.",
                    LogCategory.SDK);
                _autoStartMicrophonePending = false;
                _autoStartMicrophoneCoroutine = null;
                yield break;
            }

            EnsureRuntimeSettingsDependencies();

            int microphoneIndex = 0;
            if (_runtimeSettingsService != null && _microphoneDeviceService != null)
            {
                string preferredDeviceId = _runtimeSettingsService.Current.PreferredMicrophoneDeviceId;
                int resolvedIndex = _microphoneDeviceService.ResolvePreferredDeviceIndex(preferredDeviceId);
                if (resolvedIndex >= 0) microphoneIndex = resolvedIndex;
            }

            if (_currentResolvedTurnTakingOptions.ShouldStartMutedAfterAutoStart)
                SetMicMuted(true);

            StartListeningAsync(microphoneIndex).AsTask().ContinueWith(
                static t => ConvaiLogger.Error(
                    $"[ConvaiRoomManager] StartListeningAsync failed: {t.Exception?.GetBaseException().Message}",
                    LogCategory.SDK),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.FromCurrentSynchronizationContext());

            _autoStartMicrophoneCompleted = true;
            _autoStartMicrophonePending = false;
            _autoStartMicrophoneCoroutine = null;
        }

        private void UpdateSessionState(SessionState newState, SessionError? error = null) =>
            _sessionDiagnostics?.UpdateSessionState(newState, error);

        private void RecordConnectionSuccess(string roomName, string characterSessionId, string sessionId,
            string characterId)
        {
            IConvaiCharacterAgent activeCharacter = _activeCharacter;
            bool enableSessionResume = activeCharacter?.EnableSessionResume ?? false;
            ResetConnectionScopedRuntimeState();
            _lastSessionErrorCode = null;
            _lastSessionErrorMessage = null;
            LastSuccessfulConnectionUtc = DateTime.UtcNow;
            _connectionContext = _sessionDiagnostics?.RecordConnectionSuccess(
                roomName,
                characterSessionId,
                sessionId,
                characterId,
                enableSessionResume) ?? ConnectionContext.Empty;
            RequestAutoStartMicrophone("room-connected");
        }

        private void RecordConnectionFailure(ConnectionFailure failure)
        {
            _lastSessionErrorCode = failure.Code;
            _lastSessionErrorMessage = failure.Message;
            SessionError sessionError = _sessionDiagnostics?.RecordConnectionFailure(failure) ??
                                        failure.ToSessionError(CurrentSessionId);
            OnSessionError?.Invoke(sessionError);
        }

        private ReconnectPolicy ResolveConfiguredReconnectPolicy() =>
            CreateConfiguredReconnectPolicy();

        private void CompleteDisconnectionTracking(bool updateSessionState, string completionMessage)
        {
            ResetConnectionScopedRuntimeState();
            _connectionContext = _sessionDiagnostics?.CompleteDisconnectionTracking(
                _connectionContext,
                updateSessionState,
                completionMessage) ?? ConnectionContext.Empty;
        }

        private void ResetConnectionScopedRuntimeState()
        {
            ResetAutoStartMicrophoneState();
            _characterLifecycleCoordinator?.ResetRecoveredReadinessState();
        }

        internal RoomSessionConnectOptions ConsumePendingConnectOptions()
        {
            lock (_connectOptionsLock)
            {
                RoomSessionConnectOptions options = _pendingConnectOptions;
                _pendingConnectOptions = null;
                return options;
            }
        }

        private bool TryQueuePendingConnectOptions(RoomSessionConnectOptions pendingOptions)
        {
            if (pendingOptions == null)
                return false;

            lock (_connectOptionsLock)
            {
                if (_pendingConnectOptions != null)
                    return false;

                _pendingConnectOptions = pendingOptions;
                return true;
            }
        }

        private async Task<RoomSession> ClearPendingConnectOptionsWhenUnusedAsync(
            Task<RoomSession> connectTask,
            RoomSessionConnectOptions pendingOptions)
        {
            try
            {
                return await connectTask;
            }
            finally
            {
                ClearPendingConnectOptionsIfUnconsumed(pendingOptions);
            }
        }

        private void ClearPendingConnectOptionsIfUnconsumed(RoomSessionConnectOptions pendingOptions)
        {
            if (pendingOptions == null)
                return;

            lock (_connectOptionsLock)
            {
                if (ReferenceEquals(_pendingConnectOptions, pendingOptions))
                    _pendingConnectOptions = null;
            }
        }

        internal void SetCurrentResolvedTurnTakingOptions(ResolvedTurnTakingOptions options) =>
            _currentResolvedTurnTakingOptions = options ?? ResolvedTurnTakingOptions.DefaultHandsFree;

        private void OnSessionStateMachineStateChanged(SessionStateChanged stateChanged)
        {
            _sessionDiagnostics?.HandleStateChanged(stateChanged);
            ConvaiLogger.Debug(
                $"[ConvaiRoomManager] Session lifecycle transition: {DescribeSessionState(stateChanged.OldState)} -> {DescribeSessionState(stateChanged.NewState)}",
                LogCategory.SDK);
            _roomConnectionRuntimeAdapter?.NotifyStateChanged(stateChanged);
        }

        private void HandleCharacterReadyEvent(CharacterReady readyEvent) =>
            _characterLifecycleCoordinator?.HandleCharacterReady(readyEvent);

        private void HandleCharacterSpeechEvidence(CharacterSpeechStateChanged speechEvent)
        {
            if (!speechEvent.IsSpeaking)
                return;

            PublishRecoveredCharacterReady(
                _characterLifecycleCoordinator?.TryRecoverCharacterReadyFromSpeech(speechEvent),
                "speech");
        }

        private void HandleCharacterTtsEvidence(CharacterTtsTextChunk ttsEvent) =>
            PublishRecoveredCharacterReady(
                _characterLifecycleCoordinator?.TryRecoverCharacterReadyFromTts(ttsEvent),
                "tts-text");

        private void HandleLipSyncEvidence(LipSyncPackedDataReceived lipSyncEvent) =>
            PublishRecoveredCharacterReady(
                _characterLifecycleCoordinator?.TryRecoverCharacterReadyFromLipSync(lipSyncEvent),
                "lip-sync");

        private void PublishRecoveredCharacterReady(CharacterReady? recoveredReady, string source)
        {
            if (!recoveredReady.HasValue || _eventHub == null)
                return;

            CharacterReady readyEvent = recoveredReady.Value;
            ConvaiLogger.Info(
                $"[ConvaiRoomManager] Recovering missing CharacterReady from {source}: characterId={readyEvent.CharacterId}, participantId={readyEvent.ParticipantId}",
                LogCategory.SDK);
            _eventHub.Publish(readyEvent);
        }

        private void OnRemoteAudioPreferenceChanged(string characterId, bool enabled) =>
            RemoteAudioEnabledChanged?.Invoke(characterId, enabled);
    }
}
