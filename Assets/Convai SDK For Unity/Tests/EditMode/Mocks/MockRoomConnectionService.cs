using System;
using System.Collections.Generic;
using System.Threading;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.Errors;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;
using Convai.Runtime.Room;
using Convai.Shared.Types;
using RtviSceneMetadata = Convai.Infrastructure.Protocol.Messages.SceneMetadata;

namespace Convai.Tests.EditMode.Mocks
{
    /// <summary>
    ///     Lightweight mock for IConvaiRoomConnectionService used in edit-mode tests.
    /// </summary>
    public sealed class MockRoomConnectionService : IConvaiRoomConnectionService
    {
        // --- Messaging stubs (record calls for assertions) ---

        public List<(string Name, string Message)> SentTriggers { get; } = new();
        public List<string> SentDynamicInfos { get; } = new();
        public List<IReadOnlyList<RtviSceneMetadata>> SentSceneMetadataUpdates { get; } = new();
        public List<Dictionary<string, string>> SentTemplateKeys { get; } = new();
        public List<(string Text, string Mode, string RunLlm)> SentDynamicContextUpdates { get; } = new();
        public List<bool> TtsToggleStates { get; } = new();
        public List<bool> SttMutedStates { get; } = new();
        public int InterruptBotCallCount { get; private set; }
        public int KillPipelineCallCount { get; private set; }
        public int ForceUserStoppedSpeakingCallCount { get; private set; }
        public int ResetIdleTimerCallCount { get; private set; }
        public event Action Connected;
        public event Action<SessionError> OnSessionError;
        public event Action<SessionStateChanged> OnSessionStateChanged;

        /// <summary>
        ///     Gets or sets the connection type for testing. Defaults to Audio.
        ///     Set to Video to test vision-related scenarios.
        /// </summary>
        public ConvaiConnectionType ConnectionType { get; set; } = ConvaiConnectionType.Audio;

        public SessionState CurrentState { get; private set; } = SessionState.Disconnected;
        public bool IsConnected { get; private set; }
        public bool HasRoomDetails { get; set; }
        public bool HasPendingOwnershipReconnect { get; set; }
        public IRoomFacade CurrentRoom { get; set; }
        public RTVIHandler RtvHandler { get; set; }

        public IConvaiOperation<RoomSession> ConnectAsync(CancellationToken cancellationToken = default)
        {
            SessionState oldState = CurrentState;
            CurrentState = SessionState.Connected;
            IsConnected = true;
            Connected?.Invoke();
            OnSessionStateChanged?.Invoke(SessionStateChanged.Create(oldState, CurrentState, "mock-session"));
            return ConvaiOperation<RoomSession>.Succeeded(new RoomSession(
                "mock-session",
                "mock-room",
                "local-player",
                DateTime.UtcNow));
        }

        public IConvaiOperation<RoomSession> ConnectAsync(
            RoomSessionConnectOptions options,
            CancellationToken cancellationToken = default) =>
            ConnectAsync(cancellationToken);

        public IConvaiOperation<Unit> DisconnectAsync(DisconnectReason reason = DisconnectReason.ClientInitiated,
            CancellationToken cancellationToken = default)
        {
            SessionState oldState = CurrentState;
            CurrentState = SessionState.Disconnected;
            IsConnected = false;
            OnSessionStateChanged?.Invoke(SessionStateChanged.Create(oldState, CurrentState, "mock-session"));
            return ConvaiOperation<Unit>.Succeeded(Unit.Value);
        }

        public bool SendTrigger(string triggerName, string triggerMessage = null)
        {
            SentTriggers.Add((triggerName, triggerMessage));
            return true;
        }

        public bool SendDynamicInfo(string contextText)
        {
            SentDynamicInfos.Add(contextText);
            return true;
        }

        public bool UpdateTemplateKeys(Dictionary<string, string> templateKeys)
        {
            SentTemplateKeys.Add(templateKeys);
            return true;
        }

        public bool UpdateSceneMetadata(IReadOnlyList<RtviSceneMetadata> sceneMetadata)
        {
            SentSceneMetadataUpdates.Add(sceneMetadata);
            return true;
        }

        public bool UpdateDynamicContext(string text, string mode = "append", string runLlm = "auto")
        {
            SentDynamicContextUpdates.Add((text, mode, runLlm));
            return true;
        }

        public bool SetTtsEnabled(bool ttsEnabled)
        {
            TtsToggleStates.Add(ttsEnabled);
            return true;
        }

        public bool SetSttMuted(bool muted)
        {
            SttMutedStates.Add(muted);
            return true;
        }

        public bool InterruptBot()
        {
            InterruptBotCallCount++;
            return true;
        }

        public bool KillPipeline()
        {
            KillPipelineCallCount++;
            return true;
        }

        public bool ForceUserStoppedSpeaking()
        {
            ForceUserStoppedSpeakingCallCount++;
            return true;
        }

        public bool ResetIdleTimer()
        {
            ResetIdleTimerCallCount++;
            return true;
        }

        public void RaiseConnected()
        {
            SessionState oldState = CurrentState;
            CurrentState = SessionState.Connected;
            IsConnected = true;
            Connected?.Invoke();
            OnSessionStateChanged?.Invoke(SessionStateChanged.Create(oldState, CurrentState, "mock-session"));
        }

        public void RaiseConnectionFailed(string code = SessionErrorCodes.ConnectionFailed,
            string message = "Mock connection failure")
        {
            SessionState oldState = CurrentState;
            CurrentState = SessionState.Error;
            var error = SessionError.Create(code, message, "mock-session");
            OnSessionError?.Invoke(error);
            OnSessionStateChanged?.Invoke(SessionStateChanged.Create(oldState, CurrentState, "mock-session", error));
        }
    }
}
