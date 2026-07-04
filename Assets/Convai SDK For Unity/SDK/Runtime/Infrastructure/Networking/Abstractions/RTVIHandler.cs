using System;
using System.Collections.Generic;
using System.Text;
using Convai.Domain.Abstractions;
using Convai.Domain.DomainEvents.LipSync;
using Convai.Domain.DomainEvents.Narrative;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.Errors;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Domain.Models;
using Convai.Domain.Models.LipSync;
using Convai.Infrastructure.Networking.Transport;
using Convai.Infrastructure.Protocol;
using Convai.Infrastructure.Protocol.Messages;
using Convai.Runtime.Behaviors;
using Convai.Shared.Types;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Profiling;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Bridges RTVI (Real-Time Voice Inference) protocol messages to Character/player systems
    ///     while keeping transport concerns abstracted.
    /// </summary>
    public sealed class RTVIHandler
    {
        private const double LipSyncDropLogIntervalSeconds = 5d;

        private static readonly ProfilerMarker InboundDetectMarker = new("Convai.LipSync.Inbound.Detect");
        private static readonly ProfilerMarker InboundParseMarker = new("Convai.LipSync.Inbound.Parse");
        private static readonly ProfilerMarker InboundPublishMarker = new("Convai.LipSync.Inbound.Publish");
        private readonly IAgentRegistry _agentRegistry;
        private readonly IMainThreadDispatcher _dispatcher;
        private readonly IEventHub _eventHub;

        private readonly ProtocolGateway _gateway;
        private readonly Dictionary<string, DateTime> _lipSyncDropLastLogUtc = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _lipSyncDropSuppressedCount = new(StringComparer.Ordinal);
        private readonly float _lipSyncParseFrameRate;
        private readonly LipSyncTransportOptions _lipSyncTransportOptions;
        private readonly ILogger _logger;
        private readonly IPlayerSession _playerSession;
        private readonly object _playerSpeechStateLock = new();
        private readonly PlayerTranscriptionCoordinator _playerTranscriptionCoordinator;
        private readonly INarrativeSectionNameResolver _sectionNameResolver;
        private readonly IRealtimeTransport _transport;
        private bool _isPlayerSpeaking;

        /// <summary>Last participant ID that sent blendshapes (for stream-end event).</summary>
        private string _lastBlendshapeParticipantId;

        /// <summary>Counter for received blendshape frames (for verification against turn stats).</summary>
        private int _receivedBlendshapeFrameCount;

        /// <summary>
        ///     Initializes a new instance of the <see cref="RTVIHandler" /> class and registers inbound protocol handlers.
        /// </summary>
        /// <param name="gateway">Protocol gateway used to dispatch inbound messages.</param>
        /// <param name="transport">Realtime transport used to send outbound data packets.</param>
        /// <param name="agentRegistry">Agent registry used to resolve participants to characters.</param>
        /// <param name="playerSession">Player session used for player transcription callbacks.</param>
        /// <param name="dispatcher">Dispatcher used to marshal callbacks to the main thread.</param>
        /// <param name="logger">Logger.</param>
        /// <param name="eventHub">Optional event hub used for publishing domain events.</param>
        /// <param name="sectionNameResolver">Optional resolver for human-readable narrative section names.</param>
        /// <param name="lipSyncTransportOptions">Lip sync transport options negotiated for this room session.</param>
        public RTVIHandler(
            ProtocolGateway gateway,
            IRealtimeTransport transport,
            IAgentRegistry agentRegistry,
            IPlayerSession playerSession,
            IMainThreadDispatcher dispatcher,
            ILogger logger,
            IEventHub eventHub = null,
            INarrativeSectionNameResolver sectionNameResolver = null,
            LipSyncTransportOptions lipSyncTransportOptions = default)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
            _playerSession = playerSession ?? throw new ArgumentNullException(nameof(playerSession));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _eventHub = eventHub;
            _sectionNameResolver = sectionNameResolver;
            _lipSyncTransportOptions = lipSyncTransportOptions;
            _lipSyncParseFrameRate = _lipSyncTransportOptions.OutputFps > 0
                ? _lipSyncTransportOptions.OutputFps
                : 60f;

            _playerTranscriptionCoordinator = new PlayerTranscriptionCoordinator(playerSession, RunOnMainThread);

            RegisterInboundHandlers();
        }

        /// <summary>
        ///     Serializes and sends an outbound message over the transport data channel.
        /// </summary>
        /// <param name="data">Payload object to serialize to JSON.</param>
        public void SendData(object data)
        {
            if (data == null) return;

            string json = JsonConvert.SerializeObject(data);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            _ = _transport.SendDataAsync(bytes);

            if (_eventHub != null && data is RTVISendMessageBase outboundMessage)
                _eventHub.Publish(OutboundRtviMessageSent.Create(outboundMessage.Type, outboundMessage.Id));

            _logger.Debug($"[RTVIHandler] Sent data: {json}", LogCategory.Transport);
        }

        /// <summary>
        ///     Attempts to detect and handle a raw LipSync server message from a protocol packet,
        ///     bypassing the normal JSON-deserialization gateway path for performance.
        /// </summary>
        /// <param name="packet">The raw protocol packet to inspect.</param>
        /// <returns>
        ///     <c>true</c> if the packet was identified as a LipSync message (handled or dropped); <c>false</c> if it should
        ///     be processed normally.
        /// </returns>
        public bool TryHandleLipSyncServerMessage(in ProtocolPacket packet)
        {
            using (InboundDetectMarker.Auto())
            {
                if (!LipSyncMessageDetector.MayContainLipSyncServerMessage(packet.Payload.Span))
                    return false;
            }

            LipSyncParseResult parseResult;

            using (InboundParseMarker.Auto())
            {
                parseResult = LipSyncServerMessageParser.Parse(
                    packet.Payload,
                    _lipSyncParseFrameRate,
                    _lipSyncTransportOptions);
            }

            if (!parseResult.Handled) return false;

            if (!parseResult.Parsed)
            {
                string payloadTypeForLog = string.IsNullOrWhiteSpace(parseResult.PayloadType)
                    ? "lipsync-server-message"
                    : parseResult.PayloadType;

                if (!string.IsNullOrWhiteSpace(parseResult.DropReasonCode))
                    LogLipSyncDropRateLimited(parseResult.DropReasonCode, payloadTypeForLog);

                return true;
            }

            using (InboundPublishMarker.Auto())
            {
                PublishIncomingLipSyncChunk(
                    packet.ParticipantId,
                    parseResult.Chunk);
            }

            return true;
        }

        private void RegisterInboundHandlers()
        {
            _gateway.RegisterHandler("user-started-speaking", _ => HandlePlayerStartedSpeaking());
            _gateway.RegisterHandler<UserTranscriptionPayload>("user-transcription", HandlePlayerTranscription);
            _gateway.RegisterHandler("user-stopped-speaking", _ => HandlePlayerStoppedSpeaking());

            _gateway.RegisterHandler("bot-llm-started",
                message => HandleCharacterLlmStarted(message.Packet.ParticipantId));
            _gateway.RegisterHandler("bot-llm-stopped",
                message => HandleCharacterLlmStopped(message.Packet.ParticipantId));
            _gateway.RegisterHandler<BotTranscriptionPayload>("bot-llm-text",
                message => HandleCharacterTranscription(message.Packet.ParticipantId, message.Payload, false));
            _gateway.RegisterHandler<BotTranscriptionPayload>("bot-transcription",
                message => HandleCharacterTranscription(message.Packet.ParticipantId, message.Payload, true));

            _gateway.RegisterHandler("bot-tts-started",
                message => HandleCharacterTtsStarted(message.Packet.ParticipantId));
            _gateway.RegisterHandler("bot-tts-stopped",
                message => HandleCharacterTtsStopped(message.Packet.ParticipantId));

            _gateway.RegisterHandler("bot-started-speaking",
                message => HandleCharacterStartedSpeaking(message.Packet.ParticipantId));
            _gateway.RegisterHandler("bot-stopped-speaking",
                message => HandleCharacterStoppedSpeaking(message.Packet.ParticipantId));

            _gateway.RegisterHandler("bot-ready", message => HandleCharacterReady(message.Packet.ParticipantId));
            _gateway.RegisterHandler<BotTranscriptionPayload>("bot-tts-text",
                message => HandleCharacterTtsText(message));

            _gateway.RegisterHandler("server-message", HandleServerMessage);
            _gateway.RegisterHandler("error", HandlePipelineError);
            _gateway.RegisterHandler("bot-turn-completed",
                message => HandleCharacterTurnCompleted(message.Packet.ParticipantId, message.Envelope.Json));
            _gateway.RegisterHandler("metrics", HandleMetrics);
        }

        /// <summary>
        ///     Raised when an RTVI metrics message is received (only when debug was enabled in the connect request).
        ///     Use for troubleshooting latency (ttfb, processing) or custom metrics (e.g. NeuroSync).
        /// </summary>
        public event Action<RTVIMetricsPayload> OnMetricsReceived;

        private void HandleMetrics(ProtocolMessage message)
        {
            if (message.Envelope.Payload == null || message.Envelope.Payload.Type == JTokenType.Null)
                return;

            RTVIMetricsPayload payload;
            try
            {
                payload = message.Envelope.Payload.ToObject<RTVIMetricsPayload>();
            }
            catch (Exception ex)
            {
                _logger.Debug($"[RTVIHandler] Failed to parse metrics payload: {ex.Message}", LogCategory.Transport);
                return;
            }

            if (payload == null) return;

            void Invoke()
            {
                try
                {
                    OnMetricsReceived?.Invoke(payload);
                }
                catch (Exception ex)
                {
                    _logger.Debug($"[RTVIHandler] OnMetricsReceived subscriber threw: {ex.Message}",
                        LogCategory.Transport);
                }
            }

            if (!_dispatcher.TryDispatch(Invoke))
                Invoke();
        }

        private void HandlePlayerStartedSpeaking()
        {
            lock (_playerSpeechStateLock)
            {
                if (_isPlayerSpeaking)
                {
                    _logger.Debug("[RTVIHandler] Ignoring duplicate user-started-speaking event.", LogCategory.Player);
                    return;
                }

                _isPlayerSpeaking = true;
            }

            _logger.Info("[RTVIHandler] Player started speaking", LogCategory.Player);
            _playerTranscriptionCoordinator.HandleStart();

            _eventHub?.Publish(PlayerSpeakingStateChanged.StartedSpeaking(
                _playerTranscriptionCoordinator.CurrentSessionId));
        }

        private void HandlePlayerStoppedSpeaking()
        {
            lock (_playerSpeechStateLock)
            {
                if (!_isPlayerSpeaking)
                {
                    _logger.Debug("[RTVIHandler] Ignoring duplicate user-stopped-speaking event.", LogCategory.Player);
                    return;
                }

                _isPlayerSpeaking = false;
            }

            _logger.Info("[RTVIHandler] Player stopped speaking", LogCategory.Player);
            string sessionId = _playerTranscriptionCoordinator.CurrentSessionId;
            _playerTranscriptionCoordinator.HandleStop();

            _eventHub?.Publish(PlayerSpeakingStateChanged.StoppedSpeaking(sessionId));
        }

        private void HandlePlayerTranscription(ProtocolMessage<UserTranscriptionPayload> message)
        {
            UserTranscriptionPayload payload = message.Payload;
            if (payload == null) return;

            string text = payload.Text ?? string.Empty;
            string transcriptionType = payload.IsFinal ? "final" : "interim";
            _logger.Info($"[RTVIHandler] Player transcription ({transcriptionType}): {text}", LogCategory.Player);

            if (payload.IsFinal)
                _playerTranscriptionCoordinator.HandleAsrFinal(text);
            else
                _playerTranscriptionCoordinator.HandleInterim(text);
        }

        private void HandleCharacterLlmStarted(string participantId) => _logger.Debug(
            $"[RTVIHandler] Character LLM started for participant: {participantId}", LogCategory.Character);

        private void HandleCharacterLlmStopped(string participantId) => _logger.Debug(
            $"[RTVIHandler] Character LLM stopped for participant: {participantId}", LogCategory.Character);

        private void HandleCharacterTtsStarted(string participantId) => _logger.Debug(
            $"[RTVIHandler] Character TTS started for participant: {participantId}", LogCategory.Character);

        private void HandleCharacterTtsStopped(string participantId) => _logger.Debug(
            $"[RTVIHandler] Character TTS stopped for participant: {participantId}", LogCategory.Character);

        private void HandleCharacterStartedSpeaking(string participantId)
        {
            _logger.Info($"[RTVIHandler] Character started speaking for participant: {participantId}",
                LogCategory.Character);
            PublishSpeechStateChanged(participantId, true);
        }

        private void HandleCharacterStoppedSpeaking(string participantId)
        {
            _logger.Info($"[RTVIHandler] Character stopped speaking for participant: {participantId}",
                LogCategory.Character);
            PublishSpeechStateChanged(participantId, false);
        }

        private void PublishSpeechStateChanged(string participantId, bool isSpeaking)
        {
            if (_eventHub == null)
            {
                _logger.Debug("[RTVIHandler] EventHub is null - skipping CharacterSpeechStateChanged publish",
                    LogCategory.Events);
                return;
            }

            string characterId = ResolveCharacterId(participantId);
            var speechEvent = CharacterSpeechStateChanged.Create(characterId, isSpeaking);
            _eventHub.Publish(speechEvent);
            _logger.Debug(
                $"[RTVIHandler] Published CharacterSpeechStateChanged: characterId={characterId}, isSpeaking={isSpeaking}",
                LogCategory.Events);
        }

        private void HandleCharacterTurnCompleted(string participantId, string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                _logger.Warning("[RTVIHandler] Received bot-turn-completed with null json.", LogCategory.Character);
                return;
            }

            var payload = JsonConvert.DeserializeObject<BotTurnCompletedPayload>(json);
            if (payload == null)
            {
                _logger.Warning("[RTVIHandler] Received bot-turn-completed with null payload.", LogCategory.Character);
                return;
            }

            if (_eventHub == null)
            {
                _logger.Debug("[RTVIHandler] EventHub is null - skipping CharacterTurnCompleted publish",
                    LogCategory.Events);
                return;
            }

            string resolvedParticipantId = participantId ?? string.Empty;
            string characterId = ResolveCharacterId(participantId, true);
            if (string.IsNullOrWhiteSpace(characterId) && string.IsNullOrWhiteSpace(resolvedParticipantId))
            {
                _logger.Warning(
                    "[RTVIHandler] Dropping CharacterTurnCompleted publish: unable to resolve characterId or participantId.",
                    LogCategory.Character);
                return;
            }

            var turnCompletedEvent = CharacterTurnCompleted.Create(
                characterId,
                resolvedParticipantId,
                payload.WasInterrupted
            );
            _eventHub.Publish(turnCompletedEvent);
            _logger.Debug(
                $"[RTVIHandler] Published CharacterTurnCompleted: characterId={characterId}, interrupted={payload.WasInterrupted}",
                LogCategory.Events);
        }

        private void HandleCharacterEmotion(string participantId, RTVIBotEmotionMessage payload)
        {
            if (payload == null)
            {
                _logger.Warning("[RTVIHandler] Received bot-emotion with null payload.", LogCategory.Character);
                return;
            }

            string emotion = payload.Emotion ?? "neutral";
            int intensity = payload.Scale;

            _logger.Info(
                $"[RTVIHandler] Character emotion received for participant {participantId}: {emotion} (intensity: {intensity})",
                LogCategory.Character);

            if (_eventHub == null)
            {
                _logger.Debug("[RTVIHandler] EventHub is null - skipping CharacterEmotionChanged publish",
                    LogCategory.Events);
                return;
            }

            string characterId = ResolveCharacterId(participantId);
            var emotionEvent = CharacterEmotionChanged.Create(characterId, emotion, intensity);
            _eventHub.Publish(emotionEvent);
            _logger.Debug(
                $"[RTVIHandler] Published CharacterEmotionChanged: characterId={characterId}, emotion={emotion}, intensity={intensity}",
                LogCategory.Events);
        }

        private void HandleCharacterTranscription(string participantId, BotTranscriptionPayload payload, bool isFinal)
        {
            if (payload == null) return;

            string text = payload.Text ?? string.Empty;
            string transcriptionType = isFinal ? "final" : "interim";
            _logger.Info(
                $"[RTVIHandler] Character transcription ({transcriptionType}) for participant {participantId}: {text}",
                LogCategory.Character);

            if (_eventHub == null)
            {
                _logger.Warning("[RTVIHandler] EventHub is null - cannot publish CharacterTranscriptReceived event!",
                    LogCategory.Events);
                return;
            }

            (string characterId, string characterName) = ResolveCharacterInfo(participantId);
            if (string.IsNullOrWhiteSpace(characterId) && string.IsNullOrWhiteSpace(participantId))
            {
                _logger.Warning(
                    "[RTVIHandler] Dropping CharacterTranscriptReceived publish: unable to resolve characterId or participantId.",
                    LogCategory.Character);
                return;
            }

            var transcriptMessage = TranscriptMessage.Create(
                characterId,
                characterName,
                text,
                isFinal,
                participantId: participantId
            );
            _eventHub.Publish(new CharacterTranscriptReceived(transcriptMessage));
            _logger.Debug($"[RTVIHandler] Published CharacterTranscriptReceived ({transcriptionType}): {text}",
                LogCategory.Events);
        }

        private void HandleServerMessage(ProtocolMessage message)
        {
            if (message.Envelope.Payload is not JObject payload)
            {
                _logger.Warning("[RTVIHandler] Received server-message without payload.", LogCategory.Transport);
                return;
            }

            string innerType = payload.Value<string>("type") ?? string.Empty;
            _logger.Debug($"[RTVIHandler] Received server-message with type: {innerType}", LogCategory.Transport);

            switch (innerType)
            {
                case "behavior-tree-response":
                    {
                        var data = payload.ToObject<BehaviorTreeResponsePayload>();
                        if (data != null && !string.IsNullOrEmpty(data.NarrativeSectionId))
                        {
                            string participantId = message.Packet.ParticipantId ?? string.Empty;
                            string sectionDisplay = FormatSectionForLogging(data.NarrativeSectionId);

                            _logger.Debug(
                                $"[RTVIHandler] Behavior tree response received - Section: {sectionDisplay}, " +
                                $"BT Code: {(string.IsNullOrEmpty(data.BtCode) ? "None" : "Present")}, " +
                                $"BT Constants: {(string.IsNullOrEmpty(data.BtConstants) ? "None" : "Present")}",
                                LogCategory.Character);

                            string characterId = ResolveCharacterId(participantId);

                            if (_eventHub != null)
                            {
                                var sectionChangedEvent = NarrativeSectionChanged.Create(
                                    data.NarrativeSectionId,
                                    characterId,
                                    participantId,
                                    data.BtCode,
                                    data.BtConstants
                                );
                                _eventHub.Publish(sectionChangedEvent);
                                _logger.Debug(
                                    $"[RTVIHandler] Published NarrativeSectionChanged: section={sectionDisplay}, characterId={characterId}",
                                    LogCategory.Events);
                            }
                        }

                        break;
                    }
                case "final-user-transcription":
                    {
                        var data = payload.ToObject<FinalUserTranscriptionPayload>();
                        if (data != null)
                        {
                            string cleanedText = data.Text ?? string.Empty;

                            var speakerInfo = new SpeakerInfo(
                                data.SpeakerId,
                                data.SpeakerName,
                                data.ParticipantId
                            );

                            _eventHub?.Publish(FinalUserTranscriptionReceived.Create(cleanedText, speakerInfo));
                            _playerTranscriptionCoordinator.HandleProcessedFinal(cleanedText, speakerInfo);

                            string speakerDisplay = !string.IsNullOrEmpty(data.SpeakerName)
                                ? $" (speaker: {data.SpeakerName})"
                                : string.Empty;
                            _logger.Debug(
                                $"[RTVIHandler] Received final player transcription: {cleanedText}{speakerDisplay}",
                                LogCategory.Player);
                        }

                        break;
                    }
                case "bot-emotion":
                    {
                        var data = payload.ToObject<RTVIBotEmotionMessage>();
                        HandleCharacterEmotion(message.Packet.ParticipantId, data);
                        break;
                    }
                case "usage-limit-reached":
                    {
                        var data = payload.ToObject<UsageLimitReachedPayload>();
                        if (data != null)
                        {
                            _logger.Warning($"[RTVIHandler] Usage limit reached: {data.QuotaType} - {data.Message}",
                                LogCategory.Transport);
                            _eventHub?.Publish(UsageLimitReached.Create(
                                data.QuotaType ?? "unknown",
                                data.Message ?? string.Empty
                            ));
                            _eventHub?.Publish(SessionError.Create(
                                SessionErrorCodes.ServerUsageLimitReached,
                                $"Usage limit reached ({data.QuotaType}): {data.Message}",
                                isRecoverable: false
                            ));
                        }

                        break;
                    }
                case "user-idle-warning":
                    {
                        var data = payload.ToObject<UserIdleWarningPayload>();
                        if (data != null)
                        {
                            _logger.Warning(
                                $"[RTVIHandler] User idle warning: remaining_seconds={data.RemainingSeconds}, message={data.Message}",
                                LogCategory.Transport);
                            _eventHub?.Publish(UserIdleWarningReceived.Create(
                                data.RemainingSeconds,
                                data.Message ?? string.Empty
                            ));
                        }

                        break;
                    }
                case "llm-no-response":
                    {
                        var data = payload.ToObject<LlmNoResponsePayload>();
                        string participantId = message.Packet.ParticipantId ?? string.Empty;
                        string characterId = ResolveCharacterId(participantId, true);
                        string reason = data?.Reason ?? string.Empty;

                        _logger.Info(
                            $"[RTVIHandler] LLM no-response received. characterId={characterId}, participantId={participantId}, reason={reason}",
                            LogCategory.Character);
                        _eventHub?.Publish(LlmNoResponseReceived.Create(characterId, participantId, reason));

                        break;
                    }
                case "vad-stt-started":
                    {
                        _logger.Info("[RTVIHandler] Backend VAD STT started listening.", LogCategory.Player);
                        _eventHub?.Publish(VadSttStateChanged.Started());
                        break;
                    }
                case "vad-stt-stopped":
                    {
                        _logger.Info("[RTVIHandler] Backend VAD STT stopped listening.", LogCategory.Player);
                        _eventHub?.Publish(VadSttStateChanged.Stopped());
                        break;
                    }
                case "visemes":
                    {
                        var data = payload.ToObject<VisemesPayload>();
                        if (data?.Visemes != null && data.Visemes.Count > 0)
                        {
                            string participantId = message.Packet.ParticipantId ?? string.Empty;
                            string characterId = ResolveCharacterId(participantId);
                            var visemes = new Dictionary<string, float>(data.Visemes);

                            _eventHub?.Publish(VisemesReceived.Create(characterId, participantId, visemes));
                        }

                        break;
                    }
                case "action-response":
                    {
                        var data = payload.ToObject<ActionResponsePayload>();
                        if (data?.Actions != null && data.Actions.Count > 0)
                        {
                            string participantId = message.Packet.ParticipantId ?? string.Empty;
                            string characterId = ResolveCharacterId(participantId);

                            _logger.Debug(
                                $"[RTVIHandler] Action response for {characterId}: [{string.Join(", ", data.Actions)}]",
                                LogCategory.Character);
                            _eventHub?.Publish(CharacterActionReceived.Create(characterId, data.Actions));
                        }

                        break;
                    }
                case "moderation-response":
                    {
                        var data = payload.ToObject<ModerationResponsePayload>();
                        if (data != null)
                        {
                            _logger.Debug(
                                $"[RTVIHandler] Moderation response: flagged={data.Result}, reason={data.Reason}",
                                LogCategory.Transport);
                            _eventHub?.Publish(ModerationResponseReceived.Create(
                                data.Result,
                                data.UserInput ?? string.Empty,
                                data.Reason ?? string.Empty
                            ));
                        }

                        break;
                    }
                case "blendshape-turn-stats":
                    {
                        HandleBlendshapeTurnStats(payload, message.Packet.ParticipantId);
                        break;
                    }
                default:
                    {
                        _logger.Debug($"[RTVIHandler] Unhandled server-message type: {innerType}",
                            LogCategory.Transport);
                        break;
                    }
            }
        }

        private void HandlePipelineError(ProtocolMessage message)
        {
            if (message.Envelope.Payload is not JObject payload)
            {
                _logger.Warning("[RTVIHandler] Received error message without payload.", LogCategory.Transport);
                return;
            }

            string errorText = payload.Value<string>("error") ?? "Unknown pipeline error";
            bool isFatal = payload.Value<bool>("fatal");

            if (isFatal)
                _logger.Error($"[RTVIHandler] Fatal pipeline error: {errorText}", LogCategory.Transport);
            else
                _logger.Warning($"[RTVIHandler] Pipeline error: {errorText}", LogCategory.Transport);

            string errorCode = isFatal
                ? SessionErrorCodes.ServerFatalError
                : SessionErrorCodes.ServerError;

            _eventHub?.Publish(SessionError.Create(
                errorCode,
                errorText,
                isRecoverable: !isFatal
            ));
        }

        private void PublishIncomingLipSyncChunk(string participantId, LipSyncPackedChunk chunk)
        {
            if (chunk == null || !chunk.IsValid) return;

            _receivedBlendshapeFrameCount += chunk.FrameCount;
            _lastBlendshapeParticipantId = participantId;
            PublishLipSyncPackedDataReceived(participantId, chunk);
        }

        private void PublishLipSyncPackedDataReceived(string participantId, LipSyncPackedChunk chunk)
        {
            if (chunk == null || !chunk.IsValid || _eventHub == null) return;

            if (!TryResolveCharacterIdFromParticipant(participantId, out string characterId))
            {
                LogLipSyncDropRateLimited("lipsync.route.character_unresolved", "lipsync-packed-data");
                return;
            }

            var evt = LipSyncPackedDataReceived.Create(
                characterId,
                participantId ?? string.Empty,
                chunk);
            _eventHub.Publish(evt);

            if (IsDebugEnabled(LogCategory.LipSync))
            {
                _logger.Debug(
                    $"[RTVIHandler] Published LipSyncPackedDataReceived: characterId={characterId}, frames={chunk.FrameCount}, profile={chunk.ProfileId}",
                    LogCategory.LipSync);
            }
        }

        private void HandleBlendshapeTurnStats(JObject payload, string participantId)
        {
            var statsPayload = payload.ToObject<BlendshapeTurnStatsPayload>();
            if (statsPayload?.Stats == null) return;

            BlendshapeTurnStats stats = statsPayload.Stats;
            string resolvedParticipantId = !string.IsNullOrWhiteSpace(participantId)
                ? participantId
                : _lastBlendshapeParticipantId ?? string.Empty;
            string characterId = ResolveCharacterId(resolvedParticipantId);
            bool frameCountMatch = _receivedBlendshapeFrameCount == stats.TotalBlendshapes;
            _logger.Info(
                $"[LipSync] TurnStats - Server: {stats.TotalBlendshapes} frames | " +
                $"Received: {_receivedBlendshapeFrameCount} frames | Match: {(frameCountMatch ? "YES" : "NO")} | " +
                $"Audio: {stats.TotalAudioBytes} bytes | Turn Duration: {stats.TotalTurnDurationMs / 1000.0:F2}s | " +
                $"Audio Duration: {stats.TotalAudioDurationMs / 1000.0:F2}s | FPS: {stats.Fps:F2}",
                LogCategory.LipSync);

            if (_eventHub != null)
            {
                _eventHub.Publish(BlendshapeTurnStatsReceived.Create(
                    characterId,
                    resolvedParticipantId,
                    stats.TotalBlendshapes,
                    _receivedBlendshapeFrameCount,
                    stats.TotalAudioBytes,
                    stats.TotalTurnDurationMs,
                    stats.TotalAudioDurationMs,
                    stats.Fps));

                if (TryResolveCharacterIdFromParticipant(resolvedParticipantId, out string resolvedCharacterId))
                {
                    var streamEndEvent = CharacterSpeechStateChanged.Create(resolvedCharacterId, false);
                    _eventHub.Publish(streamEndEvent);
                    _logger.Debug($"[LipSync] Published stream-end event for character: '{resolvedCharacterId}'",
                        LogCategory.LipSync);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(resolvedParticipantId))
                    {
                        _logger.Debug(
                            "[LipSync] Skipped stream-end event: no participantId was captured for the current blendshape turn.",
                            LogCategory.LipSync);
                    }
                    else
                    {
                        _logger.Warning(
                            $"[LipSync] Dropped stream-end event: unable to resolve characterId from participantId '{resolvedParticipantId}'.",
                            LogCategory.LipSync);
                    }
                }
            }

            _receivedBlendshapeFrameCount = 0;
            _lastBlendshapeParticipantId = null;
        }

        private void LogLipSyncDropRateLimited(string dropReasonCode, string payloadType)
        {
            if (string.IsNullOrWhiteSpace(dropReasonCode)) return;

            string key = $"{payloadType}:{dropReasonCode}";
            DateTime now = DateTime.UtcNow;
            if (_lipSyncDropLastLogUtc.TryGetValue(key, out DateTime lastLoggedAtUtc) &&
                (now - lastLoggedAtUtc).TotalSeconds < LipSyncDropLogIntervalSeconds)
            {
                _lipSyncDropSuppressedCount.TryGetValue(key, out int currentSuppressedCount);
                _lipSyncDropSuppressedCount[key] = currentSuppressedCount + 1;
                return;
            }

            _lipSyncDropLastLogUtc[key] = now;
            _lipSyncDropSuppressedCount.TryGetValue(key, out int suppressedCount);
            _lipSyncDropSuppressedCount.Remove(key);

            string suppressedSuffix = suppressedCount > 0
                ? $" (suppressed {suppressedCount} similar drops in last {LipSyncDropLogIntervalSeconds:F0}s)"
                : string.Empty;
            _logger.Warning($"[{dropReasonCode}] Dropped '{payloadType}' payload.{suppressedSuffix}",
                LogCategory.LipSync);
        }

        private bool IsDebugEnabled(LogCategory category) =>
            _logger != null && _logger.IsEnabled(LogLevel.Debug, category);

        private void RunOnMainThread(Action action)
        {
            if (action == null) return;

            if (!_dispatcher.TryDispatch(action))
            {
                _logger.Warning("[RTVIHandler] Failed to enqueue work on main thread dispatcher.",
                    LogCategory.Transport);
            }
        }

        /// <summary>
        ///     Resolves a characterId from a participantId via the registry.
        ///     Falls back to the participantId itself when resolution fails.
        ///     When <paramref name="fallbackToFirst" /> is true and participantId is empty,
        ///     returns the first registered character's ID (common for bot-ready messages).
        /// </summary>
        private string ResolveCharacterId(string participantId, bool fallbackToFirst = false)
        {
            if (TryResolveCharacterAgent(participantId, out IConvaiCharacterAgent agent))
                return agent.CharacterId;

            if (fallbackToFirst && TryResolveSingleCharacter(out agent))
                return agent.CharacterId;

            if (fallbackToFirst && _agentRegistry != null && string.IsNullOrEmpty(participantId))
            {
                IReadOnlyList<IConvaiCharacterAgent> allCharacters = _agentRegistry.Characters;
                if (allCharacters.Count > 0) return allCharacters[0].CharacterId;
            }

            return participantId ?? string.Empty;
        }

        /// <summary>
        ///     Resolves a characterId and display name from a participantId via the registry.
        ///     Returns the participantId as characterId and "Character" as display name when resolution fails.
        /// </summary>
        private (string CharacterId, string DisplayName) ResolveCharacterInfo(string participantId)
        {
            if (TryResolveCharacterAgent(participantId, out IConvaiCharacterAgent agent) ||
                TryResolveSingleCharacter(out agent))
            {
                string name = !string.IsNullOrEmpty(agent.CharacterName)
                    ? agent.CharacterName
                    : agent.CharacterId;
                return (agent.CharacterId, name);
            }

            return (participantId ?? string.Empty, "Character");
        }

        /// <summary>
        ///     Strict character resolution for LipSync routing — requires a non-empty participantId
        ///     that resolves to a known character. Falls back to direct character ID lookup
        ///     for transports that surface identity/characterId instead of participant SID.
        /// </summary>
        private bool TryResolveCharacterIdFromParticipant(string participantId, out string characterId)
        {
            characterId = string.Empty;
            if (string.IsNullOrWhiteSpace(participantId)) return false;

            if (!TryResolveCharacterAgent(participantId, out IConvaiCharacterAgent agent))
                return false;

            characterId = agent.CharacterId?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(characterId);
        }

        private bool TryResolveCharacterAgent(string participantId, out IConvaiCharacterAgent agent)
        {
            agent = null;
            if (_agentRegistry == null || string.IsNullOrWhiteSpace(participantId)) return false;

            if (_agentRegistry.TryGetCharacterByParticipantId(participantId, out agent)) return agent != null;

            // Some transports surface identity/characterId instead of participant SID.
            return _agentRegistry.TryGetCharacter(participantId, out agent) && agent != null;
        }

        private bool TryResolveSingleCharacter(out IConvaiCharacterAgent agent)
        {
            agent = null;
            if (_agentRegistry == null) return false;

            IReadOnlyList<IConvaiCharacterAgent> allCharacters = _agentRegistry.Characters;
            if (allCharacters == null || allCharacters.Count != 1) return false;

            agent = allCharacters[0];
            return agent != null && !string.IsNullOrWhiteSpace(agent.CharacterId);
        }

        private void HandleCharacterReady(string participantId)
        {
            if (string.IsNullOrEmpty(participantId))
                _logger.Info("[RTVIHandler] Received bot-ready (participant ID not available)", LogCategory.Character);
            else
            {
                _logger.Info($"[RTVIHandler] Received bot-ready for participant: {participantId}",
                    LogCategory.Character);
            }

            if (_eventHub != null)
            {
                string characterId = ResolveCharacterId(participantId, true);
                _logger.Debug(
                    $"[RTVIHandler] Resolved bot-ready: participantId='{participantId ?? "(null)"}' -> characterId='{characterId}'",
                    LogCategory.Character);

                var characterReadyEvent = CharacterReady.Create(characterId, participantId ?? string.Empty);
                _eventHub.Publish(characterReadyEvent);
                _logger.Info($"[RTVIHandler] Published CharacterReady event: characterId={characterId}",
                    LogCategory.Events);
            }
        }

        private void HandleCharacterTtsText(ProtocolMessage<BotTranscriptionPayload> message)
        {
            BotTranscriptionPayload payload = message.Payload;
            string participantId = message.Packet.ParticipantId ?? string.Empty;
            string text = payload?.Text ?? string.Empty;

            _logger.Info($"[RTVIHandler] Received character TTS text for participant {participantId}: {text}",
                LogCategory.Character);

            if (_eventHub == null)
            {
                _logger.Warning("[RTVIHandler] EventHub is null - cannot publish CharacterTtsTextChunk event!",
                    LogCategory.Events);
                return;
            }

            // Avoid publishing CharacterTranscriptReceived here to prevent duplicate transcript events.
            var botTtsTextEvent = CharacterTtsTextChunk.Create(participantId, text);
            _eventHub.Publish(botTtsTextEvent);
            _logger.Info($"[RTVIHandler] Published CharacterTtsTextChunk event for participant {participantId}",
                LogCategory.Events);
        }

        /// <summary>
        ///     Formats a section ID for logging, including the human-readable name if available.
        /// </summary>
        /// <param name="sectionId">The section ID to format.</param>
        /// <returns>A formatted string like '"Section Name" (id)' or just the ID if name is unavailable.</returns>
        private string FormatSectionForLogging(string sectionId)
        {
            if (string.IsNullOrEmpty(sectionId)) return "(none)";

            if (_sectionNameResolver != null &&
                _sectionNameResolver.TryGetSectionName(sectionId, out string sectionName))
                return $"\"{sectionName}\" ({sectionId})";

            return sectionId;
        }
    }
}
