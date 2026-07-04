using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.LipSync;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Domain.Models;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Transport;
using Convai.Infrastructure.Protocol;
using Convai.Infrastructure.Protocol.Messages;
using Convai.Runtime.Behaviors;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Convai.Tests.EditMode.Infrastructure
{
    public class RTVIHandlerTests
    {
        [Test]
        public void SendData_Publishes_OutboundRtviMessageSent()
        {
            EventHub eventHub = CreateEventHub();
            RTVIHandler handler = CreateHandler(eventHub, out _, out _);
            OutboundRtviMessageSent captured = default;
            eventHub.Subscribe<OutboundRtviMessageSent>(e => captured = e);

            handler.SendData(new RTVIResetIdleTimer());

            Assert.AreEqual("reset-idle-timer", captured.MessageType);
            Assert.IsFalse(string.IsNullOrWhiteSpace(captured.MessageId));
        }

        [Test]
        public void ServerMessage_UserIdleWarning_Publishes_Event()
        {
            EventHub eventHub = CreateEventHub();
            CreateHandler(eventHub, out ProtocolGateway gateway, out _);
            UserIdleWarningReceived captured = default;
            eventHub.Subscribe<UserIdleWarningReceived>(e => captured = e);

            gateway.ProcessIncoming(CreateServerMessagePacket(
                "user-idle-warning",
                new JObject
                {
                    ["remaining_seconds"] = 300,
                    ["message"] = "Idle warning"
                }));

            Assert.AreEqual(300, captured.RemainingSeconds);
            Assert.AreEqual("Idle warning", captured.Message);
        }

        [Test]
        public void ServerMessage_LlmNoResponse_Publishes_Character_Context()
        {
            EventHub eventHub = CreateEventHub();
            CreateHandler(eventHub, out ProtocolGateway gateway, out TestAgentRegistry registry);
            registry.RegisterCharacter(new TestCharacterAgent("char-1", "Camila"));
            registry.SetParticipantId("char-1", "participant-1");

            LlmNoResponseReceived captured = default;
            eventHub.Subscribe<LlmNoResponseReceived>(e => captured = e);

            gateway.ProcessIncoming(CreateServerMessagePacket(
                "llm-no-response",
                new JObject
                {
                    ["reason"] = "abstain"
                },
                "participant-1"));

            Assert.AreEqual("char-1", captured.CharacterId);
            Assert.AreEqual("participant-1", captured.ParticipantId);
            Assert.AreEqual("abstain", captured.Reason);
        }

        [Test]
        public void ServerMessage_FinalUserTranscription_Publishes_Dedicated_Event()
        {
            EventHub eventHub = CreateEventHub();
            CreateHandler(eventHub, out ProtocolGateway gateway, out _);
            FinalUserTranscriptionReceived captured = default;
            eventHub.Subscribe<FinalUserTranscriptionReceived>(e => captured = e);

            gateway.ProcessIncoming(CreateServerMessagePacket(
                "final-user-transcription",
                new JObject
                {
                    ["text"] = "Hello there",
                    ["speaker_id"] = "speaker-1",
                    ["speaker_name"] = "Rishav",
                    ["participant_id"] = "PA_1"
                }));

            Assert.AreEqual("Hello there", captured.Text);
            Assert.AreEqual("speaker-1", captured.SpeakerId);
            Assert.AreEqual("Rishav", captured.SpeakerName);
            Assert.AreEqual("PA_1", captured.ParticipantId);
        }

        [Test]
        public void ServerMessage_VadSttStarted_Publishes_Event()
        {
            EventHub eventHub = CreateEventHub();
            CreateHandler(eventHub, out ProtocolGateway gateway, out _);
            VadSttStateChanged captured = default;
            eventHub.Subscribe<VadSttStateChanged>(e => captured = e);

            gateway.ProcessIncoming(CreateServerMessagePacket("vad-stt-started", new JObject()));

            Assert.IsTrue(captured.IsActive);
        }

        [Test]
        public void ServerMessage_VadSttStopped_Publishes_Event()
        {
            EventHub eventHub = CreateEventHub();
            CreateHandler(eventHub, out ProtocolGateway gateway, out _);
            VadSttStateChanged captured = default;
            eventHub.Subscribe<VadSttStateChanged>(e => captured = e);

            gateway.ProcessIncoming(CreateServerMessagePacket("vad-stt-stopped", new JObject()));

            Assert.IsFalse(captured.IsActive);
        }

        [Test]
        public void ServerMessage_Visemes_Publishes_Raw_Event()
        {
            EventHub eventHub = CreateEventHub();
            CreateHandler(eventHub, out ProtocolGateway gateway, out TestAgentRegistry registry);
            registry.RegisterCharacter(new TestCharacterAgent("char-1", "Camila"));
            registry.SetParticipantId("char-1", "participant-1");

            VisemesReceived captured = default;
            eventHub.Subscribe<VisemesReceived>(e => captured = e);

            gateway.ProcessIncoming(CreateServerMessagePacket(
                "visemes",
                new JObject
                {
                    ["visemes"] = new JObject
                    {
                        ["pp"] = 0.8f,
                        ["aa"] = 0.2f
                    }
                },
                "participant-1"));

            Assert.AreEqual("char-1", captured.CharacterId);
            Assert.AreEqual("participant-1", captured.ParticipantId);
            Assert.AreEqual(0.8f, captured.Visemes["pp"]);
            Assert.AreEqual(0.2f, captured.Visemes["aa"]);
        }

        [Test]
        public void ServerMessage_BlendshapeTurnStats_Publishes_Event_With_AudioDuration()
        {
            EventHub eventHub = CreateEventHub();
            CreateHandler(eventHub, out ProtocolGateway gateway, out TestAgentRegistry registry);
            registry.RegisterCharacter(new TestCharacterAgent("char-1", "Camila"));
            registry.SetParticipantId("char-1", "participant-1");

            BlendshapeTurnStatsReceived captured = default;
            eventHub.Subscribe<BlendshapeTurnStatsReceived>(e => captured = e);

            gateway.ProcessIncoming(CreateServerMessagePacket(
                "blendshape-turn-stats",
                new JObject
                {
                    ["stats"] = new JObject
                    {
                        ["total_blendshapes"] = 150,
                        ["total_audio_bytes"] = 48000,
                        ["total_turn_duration_ms"] = 3000.0,
                        ["total_audio_duration_ms"] = 2800.0,
                        ["fps"] = 50.0
                    }
                },
                "participant-1"));

            Assert.AreEqual("char-1", captured.CharacterId);
            Assert.AreEqual("participant-1", captured.ParticipantId);
            Assert.AreEqual(150, captured.TotalBlendshapes);
            Assert.AreEqual(2800d, captured.TotalAudioDurationMs);
            Assert.IsFalse(captured.FrameCountMatches);
        }

        private static EventHub CreateEventHub() => new(new ImmediateScheduler(), new TestLogger());

        private static RTVIHandler CreateHandler(EventHub eventHub, out ProtocolGateway gateway,
            out TestAgentRegistry agentRegistry)
        {
            gateway = new ProtocolGateway();
            agentRegistry = new TestAgentRegistry();
            return new RTVIHandler(
                gateway,
                new RecordingTransport(),
                agentRegistry,
                new RecordingPlayerSession(),
                new ImmediateDispatcher(),
                new TestLogger(),
                eventHub);
        }

        private static ProtocolPacket CreateServerMessagePacket(string innerType, JObject payload,
            string participantId = "")
        {
            payload ??= new JObject();
            payload["type"] = innerType;

            JObject outer = new()
            {
                ["type"] = "server-message",
                ["data"] = payload
            };

            return new ProtocolPacket(
                Encoding.UTF8.GetBytes(outer.ToString()),
                participantId,
                "rtvi-ai",
                true);
        }

        private sealed class ImmediateScheduler : IUnityScheduler
        {
            public void ScheduleOnMainThread(Action action) => action?.Invoke();
            public void ScheduleOnBackground(Action action) => action?.Invoke();
            public bool IsMainThread() => true;
        }

        private sealed class ImmediateDispatcher : IMainThreadDispatcher
        {
            public bool TryDispatch(Action action)
            {
                action?.Invoke();
                return true;
            }
        }

        private sealed class RecordingPlayerSession : IPlayerSession
        {
            public string PlayerId => "player-1";
            public string PlayerName => "Player";
            public bool IsMicMuted { get; private set; }
            public event Action<string> MicrophoneStreamStarted;
            public event Action<string> MicrophoneStreamStopped;

            public void StartListening(int microphoneIndex = 0) { }
            public void StopListening() { }
            public void SetMicMuted(bool mute) => IsMicMuted = mute;
            public void SetMicrophoneIndex(int index) { }
            public void OnPlayerTranscriptionReceived(string transcript, TranscriptionPhase transcriptionPhase) { }
            public void OnPlayerTranscriptionReceived(string transcript, TranscriptionPhase transcriptionPhase,
                SpeakerInfo speakerInfo)
            { }
            public void OnPlayerStartedSpeaking(string sessionId) => MicrophoneStreamStarted?.Invoke(sessionId);
            public void OnPlayerStoppedSpeaking(string sessionId, bool didProduceFinalTranscript) =>
                MicrophoneStreamStopped?.Invoke(sessionId);
        }

        private sealed class RecordingTransport : IRealtimeTransport
        {
            public readonly List<string> SentPayloads = new();

            public event Action<DataPacket> DataReceived
            {
                add { }
                remove { }
            }

            public event Action<TransportSessionInfo> Connected
            {
                add { }
                remove { }
            }

            public event Action<DisconnectReason> Disconnected
            {
                add { }
                remove { }
            }

            public event Action<TransportError> ConnectionFailed
            {
                add { }
                remove { }
            }

            public event Action Reconnecting
            {
                add { }
                remove { }
            }

            public event Action Reconnected
            {
                add { }
                remove { }
            }

            public event Action<TransportState> StateChanged
            {
                add { }
                remove { }
            }

            public event Action<TransportParticipantInfo> ParticipantConnected
            {
                add { }
                remove { }
            }

            public event Action<TransportParticipantInfo> ParticipantDisconnected
            {
                add { }
                remove { }
            }

            public event Action<TrackInfo> TrackSubscribed
            {
                add { }
                remove { }
            }

            public event Action<TrackInfo> TrackUnsubscribed
            {
                add { }
                remove { }
            }

            public event Action<bool> MicrophoneEnabledChanged
            {
                add { }
                remove { }
            }

            public event Action<bool> MicrophoneMuteChanged
            {
                add { }
                remove { }
            }

            public event Action<bool> AudioPlaybackStateChanged
            {
                add { }
                remove { }
            }

            public Task SendDataAsync(ReadOnlyMemory<byte> payload, bool reliable = true, string topic = null,
                string[] destinationIdentities = null, CancellationToken ct = default)
            {
                SentPayloads.Add(Encoding.UTF8.GetString(payload.Span));
                return Task.CompletedTask;
            }

            public TransportState State => TransportState.Connected;
            public TransportSessionInfo? CurrentSession => null;
            public TransportCapabilities Capabilities => default;
            public AudioRuntimeState AudioState => default;
            public bool IsConnected => true;
            public IRoomFacade Room => null;
            public Task<bool> ConnectAsync(string url, string token, TransportConnectOptions options = null,
                CancellationToken ct = default) => Task.FromResult(true);
            public Task DisconnectAsync(DisconnectReason reason = DisconnectReason.ClientInitiated,
                CancellationToken ct = default) => Task.CompletedTask;
            public void EnableAudio() { }
            public Task<bool> EnableMicrophoneAsync(int microphoneDeviceIndex = 0, CancellationToken ct = default) =>
                Task.FromResult(true);
            public Task DisableMicrophoneAsync(CancellationToken ct = default) => Task.CompletedTask;
            public void SetMicrophoneMuted(bool muted) { }
            public bool IsMicrophoneEnabled => true;
            public bool IsMicrophoneMuted => false;
            public bool CanEnableMicrophone() => true;
            public bool CanEnableAudio() => true;
            public void Dispose() { }
        }

        private sealed class TestCharacterAgent : IConvaiCharacterAgent
        {
            public TestCharacterAgent(string characterId, string characterName)
            {
                CharacterId = characterId;
                CharacterName = characterName;
            }

            public string CharacterId { get; }
            public string CharacterName { get; }
            public Color NameTagColor => Color.white;
            public bool EnableSessionResume => false;
            public string InitialDynamicInfoText => string.Empty;
            public bool InitialDynamicInfoKeepInContext => false;
            public void SendTrigger(string triggerName, string triggerMessage = null) { }
            public void SendDynamicInfo(string contextText) { }
            public void UpdateTemplateKeys(Dictionary<string, string> templateKeys) { }
        }

        private sealed class TestAgentRegistry : IAgentRegistry
        {
            private readonly Dictionary<string, IConvaiCharacterAgent> _characters = new();
            private readonly Dictionary<string, string> _characterToParticipant = new();
            private readonly Dictionary<string, string> _participantToCharacter = new();
            private readonly List<IConvaiPlayerAgent> _players = new();

            public IReadOnlyList<IConvaiCharacterAgent> Characters => new List<IConvaiCharacterAgent>(_characters.Values);
            public IReadOnlyList<IConvaiPlayerAgent> Players => _players;
            public IConvaiPlayerAgent LocalPlayer => _players.Count > 0 ? _players[0] : null;
            public event Action<IConvaiCharacterAgent> CharacterRegistered;
            public event Action<IConvaiCharacterAgent> CharacterUnregistered;
            public event Action<IConvaiPlayerAgent> PlayerRegistered;

            public void RegisterCharacter(IConvaiCharacterAgent character, string ownerId = null)
            {
                _characters[character.CharacterId] = character;
                CharacterRegistered?.Invoke(character);
            }

            public void RegisterPlayer(IConvaiPlayerAgent player)
            {
                _players.Add(player);
                PlayerRegistered?.Invoke(player);
            }

            public void Unregister(IConvaiCharacterAgent character)
            {
                if (character == null) return;
                _characters.Remove(character.CharacterId);
                CharacterUnregistered?.Invoke(character);
            }

            public void Unregister(IConvaiPlayerAgent player) => _players.Remove(player);
            public bool TryGetCharacter(string characterId, out IConvaiCharacterAgent agent) =>
                _characters.TryGetValue(characterId ?? string.Empty, out agent);
            public string GetOwner(IConvaiCharacterAgent character) => null;
            public IReadOnlyList<IConvaiCharacterAgent> GetCharactersByOwner(string ownerId) => Array.Empty<IConvaiCharacterAgent>();
            public int GetCharacterCountByOwner(string ownerId) => 0;
            public bool TryGetCharacterById(string characterId, out IConvaiCharacterAgent agent) =>
                TryGetCharacter(characterId, out agent);
            public bool TryGetAudioSource(string characterId, out AudioSource source)
            {
                source = null;
                return false;
            }

            public void SetAudioSource(string characterId, AudioSource source) { }

            public void SetParticipantId(string characterId, string participantId)
            {
                if (string.IsNullOrWhiteSpace(characterId))
                    return;

                if (_characterToParticipant.TryGetValue(characterId, out string existingParticipant) &&
                    !string.IsNullOrWhiteSpace(existingParticipant))
                    _participantToCharacter.Remove(existingParticipant);

                if (string.IsNullOrWhiteSpace(participantId))
                {
                    _characterToParticipant.Remove(characterId);
                    return;
                }

                _characterToParticipant[characterId] = participantId;
                _participantToCharacter[participantId] = characterId;
            }

            public bool TryGetParticipantId(string characterId, out string participantId) =>
                _characterToParticipant.TryGetValue(characterId ?? string.Empty, out participantId);

            public bool TryGetCharacterByParticipantId(string participantId, out IConvaiCharacterAgent agent)
            {
                agent = null;
                if (string.IsNullOrWhiteSpace(participantId) ||
                    !_participantToCharacter.TryGetValue(participantId, out string characterId))
                    return false;

                return _characters.TryGetValue(characterId, out agent);
            }

            public void ClearTransportBindings()
            {
                _characterToParticipant.Clear();
                _participantToCharacter.Clear();
            }

            public void SetCharacterMuted(string characterId, bool muted) { }
            public bool IsCharacterMuted(string characterId) => false;
        }

        private sealed class TestLogger : Convai.Domain.Logging.ILogger
        {
            public bool IsEnabled(LogLevel level, LogCategory category) => false;
            public void Log(LogLevel level, string message, LogCategory category = LogCategory.SDK) { }
            public void Log(LogLevel level, string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
            public void Debug(string message, LogCategory category = LogCategory.SDK) { }
            public void Debug(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
            public void Info(string message, LogCategory category = LogCategory.SDK) { }
            public void Info(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
            public void Warning(string message, LogCategory category = LogCategory.SDK) { }
            public void Warning(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
            public void Error(string message, LogCategory category = LogCategory.SDK) { }
            public void Error(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
            public void Error(Exception exception, string message, LogCategory category = LogCategory.SDK) { }
            public void Error(Exception exception, string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
        }
    }
}
