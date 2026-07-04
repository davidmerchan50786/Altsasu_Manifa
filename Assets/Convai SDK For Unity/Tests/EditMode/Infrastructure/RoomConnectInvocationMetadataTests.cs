#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.Models.LipSync;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Models;
using Convai.RestAPI;
using Convai.RestAPI.Internal;
using Convai.RestAPI.Services;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Room;
using Convai.RestAPI.Transport;
using Convai.Shared.Types;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Convai.Tests.EditMode.Infrastructure
{
    [TestFixture]
    public class RoomConnectInvocationMetadataTests
    {
        [SetUp]
        public void SetUp()
        {
            _transport = new CapturingTransport();
            var options = new ConvaiRestClientOptions("test-api-key") { CustomTransport = _transport };

            _client = new ConvaiRestClient(options);
        }

        [TearDown]
        public void TearDown() => _client.Dispose();

        private CapturingTransport _transport = null!;
        private ConvaiRestClient _client = null!;

        [Test]
        public async Task ConnectAsync_AutoInjectsInvocationMetadata_WhenAbsent()
        {
            _transport.ResponseFactory = request =>
                ConvaiHttpResponse.Success(HttpStatusCode.OK, BuildRoomDetailsResponse(), request.Url);

            var request = new RoomConnectionRequest
            {
                CharacterId = "char-123", CoreServiceUrl = "https://core.convai.com/connect", Transport = "livekit"
            };

            await _client.Rooms.ConnectAsync(request);

            ConvaiHttpRequest lastRequest = _transport.LastRequest
                                            ?? throw new AssertionException("Expected captured room connect request.");
            Assert.That(lastRequest.Body, Is.Not.Null.And.Not.Empty);

            JObject payload = JObject.Parse(lastRequest.Body!);
            Assert.That(payload["invocation_metadata"]?["source"]?.Value<string>(), Is.EqualTo("unity_sdk"));
            Assert.That(payload["invocation_metadata"]?["client_version"]?.Value<string>(), Is.EqualTo("0.1.0"));

            Assert.That(lastRequest.Headers.ContainsKey("source"), Is.False);
            Assert.That(lastRequest.Headers.ContainsKey("version"), Is.False);
        }

        [Test]
        public async Task ConnectAsync_PreservesExplicitInvocationMetadata()
        {
            _transport.ResponseFactory = request =>
                ConvaiHttpResponse.Success(HttpStatusCode.OK, BuildRoomDetailsResponse(), request.Url);

            var request = new RoomConnectionRequest
            {
                CharacterId = "char-123",
                CoreServiceUrl = "https://core.convai.com/connect",
                Transport = "livekit",
                InvocationMetadata = new RoomInvocationMetadata
                {
                    Source = "custom_source", ClientVersion = "9.9.9"
                }
            };

            await _client.Rooms.ConnectAsync(request);

            ConvaiHttpRequest lastRequest = _transport.LastRequest
                                            ?? throw new AssertionException("Expected captured room connect request.");
            Assert.That(lastRequest.Body, Is.Not.Null.And.Not.Empty);

            JObject payload = JObject.Parse(lastRequest.Body!);
            Assert.That(payload["invocation_metadata"]?["source"]?.Value<string>(), Is.EqualTo("custom_source"));
            Assert.That(payload["invocation_metadata"]?["client_version"]?.Value<string>(), Is.EqualTo("9.9.9"));
        }

        [Test]
        public async Task ValidateApiKeyAsync_DoesNotInjectInvocationMetadata()
        {
            _transport.ResponseFactory = request =>
                ConvaiHttpResponse.Success(
                    HttpStatusCode.OK,
                    "{\"referral_source_status\":\"ok\",\"status\":\"ok\"}",
                    request.Url);

            await _client.Users.ValidateApiKeyAsync();

            ConvaiHttpRequest lastRequest = _transport.LastRequest
                                            ?? throw new AssertionException(
                                                "Expected captured user validation request.");
            Assert.That(lastRequest.Url.AbsolutePath, Is.EqualTo("/user/referral-source-status"));
            Assert.That(lastRequest.Body, Is.Null);
            Assert.That(lastRequest.Headers.ContainsKey("source"), Is.False);
            Assert.That(lastRequest.Headers.ContainsKey("version"), Is.False);
        }

        [Test]
        public async Task ConnectAsync_IncludesEmotionConfig_WhenProvided()
        {
            _transport.ResponseFactory = request =>
                ConvaiHttpResponse.Success(HttpStatusCode.OK, BuildRoomDetailsResponse(), request.Url);

            var request = new RoomConnectionRequest
            {
                CharacterId = "char-123",
                CoreServiceUrl = "https://core.convai.com/connect",
                Transport = "livekit",
                EmotionConfig = RoomEmotionConfig.Create("llm")
            };

            await _client.Rooms.ConnectAsync(request);

            ConvaiHttpRequest lastRequest = _transport.LastRequest
                                            ?? throw new AssertionException("Expected captured room connect request.");
            Assert.That(lastRequest.Body, Is.Not.Null.And.Not.Empty);

            JObject payload = JObject.Parse(lastRequest.Body!);
            Assert.That(payload["emotion_config"]?["provider"]?.Value<string>(), Is.EqualTo("llm"));
        }

        [Test]
        public async Task ConnectAsync_IncludesEndUserMetadata_WhenProvided()
        {
            _transport.ResponseFactory = request =>
                ConvaiHttpResponse.Success(HttpStatusCode.OK, BuildRoomDetailsResponse(), request.Url);

            var request = new RoomConnectionRequest
            {
                CharacterId = "char-123",
                CoreServiceUrl = "https://core.convai.com/connect",
                Transport = "livekit",
                EndUserId = "player-42",
                EndUserMetadata = new Dictionary<string, object>
                {
                    ["name"] = "Rishav",
                    ["level"] = 7
                }
            };

            await _client.Rooms.ConnectAsync(request);

            ConvaiHttpRequest lastRequest = _transport.LastRequest
                                            ?? throw new AssertionException("Expected captured room connect request.");
            JObject payload = JObject.Parse(lastRequest.Body!);

            Assert.That(payload["end_user_id"]?.Value<string>(), Is.EqualTo("player-42"));
            Assert.That(payload["end_user_metadata"]?["name"]?.Value<string>(), Is.EqualTo("Rishav"));
            Assert.That(payload["end_user_metadata"]?["level"]?.Value<int>(), Is.EqualTo(7));
        }

        [Test]
        public async Task ConnectAsync_OmitsEmptyEndUserIdentityFields()
        {
            _transport.ResponseFactory = request =>
                ConvaiHttpResponse.Success(HttpStatusCode.OK, BuildRoomDetailsResponse(), request.Url);

            var request = new RoomConnectionRequest
            {
                CharacterId = "char-123",
                CoreServiceUrl = "https://core.convai.com/connect",
                Transport = "livekit",
                EndUserId = "  ",
                EndUserMetadata = new Dictionary<string, object>()
            };

            await _client.Rooms.ConnectAsync(request);

            ConvaiHttpRequest lastRequest = _transport.LastRequest
                                            ?? throw new AssertionException("Expected captured room connect request.");
            JObject payload = JObject.Parse(lastRequest.Body!);

            Assert.That(payload["end_user_id"], Is.Null);
            Assert.That(payload["end_user_metadata"], Is.Null);
        }

        [Test]
        public async Task ConnectAsync_IncludesBlendshapeContract_WhenProvided()
        {
            _transport.ResponseFactory = request =>
                ConvaiHttpResponse.Success(HttpStatusCode.OK, BuildRoomDetailsResponse(), request.Url);

            var request = new RoomConnectionRequest
            {
                CharacterId = "char-123",
                CoreServiceUrl = "https://core.convai.com/connect",
                Transport = "livekit",
                BlendshapeProvider = "neurosync",
                BlendshapeConfig = new ConvaiBlendshapeConfig(
                    true,
                    10,
                    60,
                    "arkit")
            };

            await _client.Rooms.ConnectAsync(request);

            ConvaiHttpRequest lastRequest = _transport.LastRequest
                                            ?? throw new AssertionException("Expected captured room connect request.");
            JObject payload = JObject.Parse(lastRequest.Body!);

            Assert.That(payload["blendshape_provider"]?.Value<string>(), Is.EqualTo("neurosync"));
            Assert.That(payload["blendshape_config"]?["enable_chunking"]?.Value<bool>(), Is.True);
            Assert.That(payload["blendshape_config"]?["chunk_size"]?.Value<int>(), Is.EqualTo(10));
            Assert.That(payload["blendshape_config"]?["output_fps"]?.Value<int>(), Is.EqualTo(60));
            Assert.That(payload["blendshape_config"]?["format"]?.Value<string>(), Is.EqualTo("arkit"));
        }

        [Test]
        public async Task GetDetailsAsync_UsesFixedProductionCharacterEndpoint_AndUnwrapsResponseEnvelope()
        {
            _transport.ResponseFactory = request =>
                ConvaiHttpResponse.Success(
                    HttpStatusCode.OK,
                    "{\"response\":{\"character_id\":\"char-123\",\"character_name\":\"Test Character\",\"character_emotions\":[\"joy\"]}}",
                    request.Url);

            CharacterDetails details = await _client.Characters.GetDetailsAsync("char-123");

            ConvaiHttpRequest lastRequest = _transport.LastRequest
                                            ?? throw new AssertionException(
                                                "Expected captured character details request.");
            Assert.That(lastRequest.Url.ToString(), Is.EqualTo("https://api.convai.com/character/get"));
            Assert.That(lastRequest.Body, Is.Not.Null.And.Contains("\"charID\":\"char-123\""));
            Assert.That(details.CharacterID, Is.EqualTo("char-123"));
            Assert.That(details.CharacterName, Is.EqualTo("Test Character"));
        }

        [Test]
        public void TryGetConnectEmotionConfig_ReturnsFalse_WhenNoSignalsPresent()
        {
            CharacterDetails details = BuildCharacterDetails(_ => { });

            bool result = details.TryGetConnectEmotionConfig(out RoomEmotionConfig emotionConfig);

            Assert.That(result, Is.False);
            Assert.That(emotionConfig, Is.Null);
        }

        [Test]
        public void TryGetConnectEmotionConfig_UsesDefaultProvider_WhenCharacterEmotionsExist()
        {
            CharacterDetails details = BuildCharacterDetails(root =>
            {
                root["character_emotions"] = new JArray("joy");
            });

            bool result = details.TryGetConnectEmotionConfig(out RoomEmotionConfig emotionConfig);

            Assert.That(result, Is.True);
            Assert.That(emotionConfig, Is.Not.Null);
            Assert.That(emotionConfig.Provider, Is.EqualTo("nrclex"));
        }

        [Test]
        public void TryGetConnectEmotionConfig_UsesExplicitProvider_WhenEmotionConfigExists()
        {
            CharacterDetails details = BuildCharacterDetails(root =>
            {
                root["emotion_config"] = new JObject { ["provider"] = "llm" };
            });

            bool result = details.TryGetConnectEmotionConfig(out RoomEmotionConfig emotionConfig);

            Assert.That(result, Is.True);
            Assert.That(emotionConfig, Is.Not.Null);
            Assert.That(emotionConfig.Provider, Is.EqualTo("llm"));
        }

        [Test]
        public void TryGetConnectEmotionConfig_RespectsExplicitDisableFlag()
        {
            CharacterDetails details = BuildCharacterDetails(root =>
            {
                root["character_emotions"] = new JArray("joy");
                root["emotion_config"] = new JObject { ["provider"] = "llm", ["enabled"] = false };
            });

            bool result = details.TryGetConnectEmotionConfig(out RoomEmotionConfig emotionConfig);

            Assert.That(result, Is.False);
            Assert.That(emotionConfig, Is.Null);
        }

        [Test]
        public void TryGetConnectEmotionConfig_UsesDefaultProvider_WhenEnabledFlagIsTrue()
        {
            CharacterDetails details = BuildCharacterDetails(root =>
            {
                root["state_of_mind_enabled"] = true;
            });

            bool result = details.TryGetConnectEmotionConfig(out RoomEmotionConfig emotionConfig);

            Assert.That(result, Is.True);
            Assert.That(emotionConfig, Is.Not.Null);
            Assert.That(emotionConfig.Provider, Is.EqualTo("nrclex"));
        }

        [Test]
        public void ApplyLipSyncMapper_PopulatesBlendshapeFields_WhenTransportContractIsValid()
        {
            var request = new RoomConnectionRequest();

            RoomConnectionRequestLipSyncMapper.Apply(request, CreateLipSyncOptions());

            Assert.That(request.BlendshapeProvider, Is.EqualTo("neurosync"));
            Assert.That(request.BlendshapeConfig, Is.Not.Null);
            ConvaiBlendshapeConfig blendshapeConfig = request.BlendshapeConfig!;
            Assert.That(blendshapeConfig.Format, Is.EqualTo("arkit"));
            Assert.That(blendshapeConfig.OutputFps, Is.EqualTo(60));
            Assert.That(blendshapeConfig.EnableChunking, Is.True);
            Assert.That(blendshapeConfig.ChunkSize, Is.EqualTo(10));
        }

        [Test]
        public void ApplyLipSyncMapper_ClearsBlendshapeFields_WhenTransportContractIsDisabled()
        {
            var request = new RoomConnectionRequest { BlendshapeProvider = "neurosync" };

            RoomConnectionRequestLipSyncMapper.Apply(request, LipSyncTransportOptions.Disabled);

            Assert.That(request.BlendshapeProvider, Is.Null);
            Assert.That(request.BlendshapeConfig, Is.Null);
        }

        [Test]
        public void CreateRoomConnectionRequest_AppliesSharedJoinVideoAndLipSyncFields()
        {
            RoomConnectionRequest request = RoomConnectionRequestFactory.Create(
                "char-123",
                "video",
                "https://core.convai.com/connect",
                "session-xyz",
                " user-42 ",
                "camera-main",
                RoomEmotionConfig.Create("llm"),
                new RoomJoinOptions("room-a", spawnAgent: false, maxNumParticipants: 4),
                CreateLipSyncOptions(),
                endUserMetadata: new Dictionary<string, object>
                {
                    ["name"] = "Rishav"
                });

            Assert.That(request.CharacterId, Is.EqualTo("char-123"));
            Assert.That(request.ConnectionType, Is.EqualTo("video"));
            Assert.That(request.LlmProvider, Is.EqualTo("dynamic"));
            Assert.That(request.CoreServiceUrl, Is.EqualTo("https://core.convai.com/connect"));
            Assert.That(request.CharacterSessionId, Is.EqualTo("session-xyz"));
            Assert.That(request.EndUserId, Is.EqualTo("user-42"));
            Assert.That(request.EndUserMetadata?["name"], Is.EqualTo("Rishav"));
            Assert.That(request.VideoTrackName, Is.EqualTo("camera-main"));
            Assert.That(request.Mode, Is.EqualTo("join"));
            Assert.That(request.SpawnAgent, Is.False);
            Assert.That(request.MaxNumParticipants, Is.EqualTo("4"));
            Assert.That(request.TurnDetectionConfig, Is.Not.Null);
            Assert.That(request.DefaultSttEnabled, Is.True);
            Assert.That(request.EmotionConfig?.Provider, Is.EqualTo("llm"));
            Assert.That(request.BlendshapeProvider, Is.EqualTo("neurosync"));
            Assert.That(request.BlendshapeConfig?.Format, Is.EqualTo("arkit"));
        }

        [Test]
        public void CreateRoomConnectionRequest_MapsPushToTalkResolvedOptionsToConcreteRequestFields()
        {
            ResolvedTurnTakingOptions resolvedOptions = TurnTakingOptionsResolver.Resolve(
                TurnTakingOptions.CreatePushToTalkDefault(),
                null);

            RoomConnectionRequest request = RoomConnectionRequestFactory.Create(
                "char-123",
                "audio",
                "https://core.convai.com/connect",
                "session-xyz",
                "user-42",
                "camera-main",
                RoomEmotionConfig.Create("llm"),
                RoomJoinOptions.CreateNew("session-xyz"),
                resolvedOptions,
                LipSyncTransportOptions.Disabled);

            Assert.That(request.TurnDetectionConfig, Is.Null);
            Assert.That(request.DefaultSttEnabled, Is.False);
        }

        [Test]
        public void PrepareRoomInitializationRequest_PreservesNativeStoredSessionFallback_WhenResumeIsDisabled()
        {
            PreparedRoomInitializationRequest preparation = RoomInitializationRequestPreparer.Prepare(
                "char-123",
                "audio",
                "https://core.convai.com/connect",
                "stored-session",
                false,
                null,
                " user-42 ",
                new Dictionary<string, object>
                {
                    ["name"] = "Rishav"
                },
                "camera-main",
                RoomEmotionConfig.Create("llm"),
                LipSyncTransportOptions.Disabled,
                StoredSessionFallbackPolicy.NativeCompatibility);

            Assert.That(preparation.IsJoinRequest, Is.False);
            Assert.That(preparation.Request.CharacterSessionId, Is.EqualTo("stored-session"));
            Assert.That(preparation.Request.EndUserId, Is.EqualTo("user-42"));
            Assert.That(preparation.Request.EndUserMetadata?["name"], Is.EqualTo("Rishav"));
            Assert.That(preparation.ModeLogMessage, Is.EqualTo("Room create mode (new room)"));
        }

        [Test]
        public void PrepareRoomInitializationRequest_PreservesWebGLStoredSessionFallbackPolicies()
        {
            PreparedRoomInitializationRequest resumeDisabledPreparation = RoomInitializationRequestPreparer.Prepare(
                "char-123",
                "audio",
                "https://core.convai.com/connect",
                "stored-session",
                false,
                null,
                null,
                null,
                "camera-main",
                RoomEmotionConfig.Create("llm"),
                LipSyncTransportOptions.Disabled,
                StoredSessionFallbackPolicy.WebGLCompatibility);

            PreparedRoomInitializationRequest resumeEnabledPreparation = RoomInitializationRequestPreparer.Prepare(
                "char-123",
                "audio",
                "https://core.convai.com/connect",
                "stored-session",
                true,
                null,
                null,
                null,
                "camera-main",
                RoomEmotionConfig.Create("llm"),
                LipSyncTransportOptions.Disabled,
                StoredSessionFallbackPolicy.WebGLCompatibility);

            Assert.That(resumeDisabledPreparation.Request.CharacterSessionId, Is.Null);
            Assert.That(resumeEnabledPreparation.Request.CharacterSessionId, Is.EqualTo("stored-session"));
        }

        [Test]
        public void PrepareRoomInitializationRequest_BuildsJoinMetadataAndLogMessage()
        {
            PreparedRoomInitializationRequest preparation = RoomInitializationRequestPreparer.Prepare(
                "char-123",
                "video",
                "https://core.convai.com/connect",
                "stored-session",
                true,
                new RoomJoinOptions("room-a", string.Empty, false, 4),
                "user-42",
                null,
                "camera-main",
                RoomEmotionConfig.Create("llm"),
                LipSyncTransportOptions.Disabled,
                StoredSessionFallbackPolicy.NativeCompatibility);

            Assert.That(preparation.IsJoinRequest, Is.True);
            Assert.That(preparation.Request.Mode, Is.EqualTo("join"));
            Assert.That(preparation.Request.RoomName, Is.EqualTo("room-a"));
            Assert.That(preparation.Request.SpawnAgent, Is.False);
            Assert.That(preparation.Request.MaxNumParticipants, Is.EqualTo("4"));
            Assert.That(preparation.Request.CharacterSessionId, Is.Null);
            Assert.That(
                preparation.FormatModeLogMessage("[WebGLRoomController]"),
                Is.EqualTo("[WebGLRoomController] Room join mode: room=room-a, spawnAgent=False"));
        }

        [Test]
        public async Task SerializeForTransport_MatchesRoomServicePayload_ForSharedRoomRequest()
        {
            _transport.ResponseFactory = request =>
                ConvaiHttpResponse.Success(HttpStatusCode.OK, BuildRoomDetailsResponse(), request.Url);

            RoomJoinOptions joinOptions = new("room-a", "session-xyz", false, 4);
            RoomConnectionRequest roomServiceRequest = RoomConnectionRequestFactory.Create(
                "char-123",
                "video",
                "https://core.convai.com/connect",
                "session-xyz",
                "user-42",
                "camera-main",
                RoomEmotionConfig.Create("llm"),
                joinOptions,
                CreateLipSyncOptions(),
                endUserMetadata: new Dictionary<string, object>
                {
                    ["name"] = "Rishav"
                });

            await _client.Rooms.ConnectAsync(roomServiceRequest);

            ConvaiHttpRequest lastRequest = _transport.LastRequest
                                            ?? throw new AssertionException("Expected captured room connect request.");

            RoomConnectionRequest directTransportRequest = RoomConnectionRequestFactory.Create(
                "char-123",
                "video",
                "https://core.convai.com/connect",
                "session-xyz",
                "user-42",
                "camera-main",
                RoomEmotionConfig.Create("llm"),
                joinOptions,
                CreateLipSyncOptions(),
                endUserMetadata: new Dictionary<string, object>
                {
                    ["name"] = "Rishav"
                });

            string directJson = RoomConnectionRequestTransportSerializer.SerializeForTransport(
                directTransportRequest,
                new ConvaiRestClientOptions("test-api-key"));

            Assert.That(
                JToken.DeepEquals(JToken.Parse(lastRequest.Body!), JToken.Parse(directJson)),
                Is.True,
                "Direct transport serialization must match the canonical RoomService payload.");
        }

        [Test]
        public async Task ConnectAsync_Succeeds_WhenSpeakerIdIsMissingFromResponse()
        {
            _transport.ResponseFactory = request =>
                ConvaiHttpResponse.Success(HttpStatusCode.OK, BuildRoomDetailsResponse(includeSpeakerId: false), request.Url);

            RoomDetails details = await _client.Rooms.ConnectAsync(new RoomConnectionRequest
            {
                CharacterId = "char-123",
                CoreServiceUrl = "https://core.convai.com/connect",
                Transport = "livekit"
            });

            Assert.That(details, Is.Not.Null);
            Assert.That(details.Token, Is.EqualTo("test-token"));
            Assert.That(details.SpeakerId, Is.Null.Or.Empty);
        }

        private static string BuildRoomDetailsResponse(bool includeSpeakerId = true)
        {
            return "{"
                   + "\"token\":\"test-token\","
                   + "\"room_name\":\"test-room\","
                   + "\"session_id\":\"session-123\","
                   + "\"room_url\":\"wss://example.convai.com\","
                   + "\"character_session_id\":\"char-session-123\","
                   + (includeSpeakerId ? "\"speaker_id\":\"speaker-123\"," : string.Empty)
                   + "\"transport\":\"livekit\""
                   + "}";
        }

        private static CharacterDetails BuildCharacterDetails(Action<JObject> mutator)
        {
            JObject root = JObject.FromObject(CharacterDetails.Default());
            mutator(root);

            string json = root.ToString(Formatting.None);
            var details = JsonConvert.DeserializeObject<CharacterDetails>(json);
            return details ?? throw new AssertionException("Failed to deserialize CharacterDetails for test.");
        }

        private static LipSyncTransportOptions CreateLipSyncOptions()
        {
            return new LipSyncTransportOptions(
                true,
                "neurosync",
                LipSyncProfileId.ARKit,
                "arkit",
                new[] { "EyeBlinkLeft" },
                true,
                10,
                60,
                LipSyncTransportOptions.DefaultFramesBufferDuration);
        }

        private sealed class CapturingTransport : IConvaiHttpTransport
        {
            public ConvaiHttpRequest? LastRequest { get; private set; }

            public Func<ConvaiHttpRequest, ConvaiHttpResponse>? ResponseFactory { get; set; }

            public Task<ConvaiHttpResponse> SendAsync(
                ConvaiHttpRequest request,
                CancellationToken cancellationToken = default)
            {
                LastRequest = request;

                ConvaiHttpResponse response = ResponseFactory?.Invoke(request)
                                              ?? ConvaiHttpResponse.Success(HttpStatusCode.OK, "{}", request.Url);

                return Task.FromResult(response);
            }

            public Task<byte[]> DownloadBytesAsync(Uri url, CancellationToken cancellationToken = default) =>
                Task.FromResult(Array.Empty<byte>());

            public void Dispose()
            {
            }
        }
    }
}
