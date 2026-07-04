#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.RestAPI.Internal;
using Convai.RestAPI.Transport;
using Newtonsoft.Json;

namespace Convai.RestAPI.Services
{
    /// <summary>
    /// Service for room connection API operations.
    /// </summary>
    public sealed class RoomService : ConvaiServiceBase
    {
        private const string DuplicateSpeakerErrorPattern = "uq_speaker_user_id_end_user_id";

        internal RoomService(ConvaiRestClientOptions options, IConvaiHttpTransport transport)
            : base(options, transport)
        {
        }

        /// <summary>
        /// Connects to a room and gets the connection details.
        /// </summary>
        /// <param name="request">The room connection request.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The room connection details.</returns>
        public async Task<RoomDetails> ConnectAsync(
            RoomConnectionRequest request,
            CancellationToken cancellationToken = default)
        {
            RoomConnectionRequestTransportSerializer.PrepareForTransport(request, Options);

            return await PostToUrlAsync<RoomDetails>(
                request.CoreServiceUrl,
                request,
                useXApiKey: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Request model for room connection.
    /// </summary>
    public sealed class RoomConnectionRequest
    {
        /// <summary>
        /// The character ID to connect to.
        /// </summary>
        [JsonProperty("character_id")]
        public string CharacterId { get; set; } = string.Empty;

        /// <summary>
        /// The transport type (e.g., "livekit").
        /// </summary>
        [JsonProperty("transport")]
        public string Transport { get; set; } = "livekit";

        /// <summary>
        /// The connection type.
        /// </summary>
        [JsonProperty("connection_type")]
        public string ConnectionType { get; set; } = string.Empty;

        /// <summary>
        /// The LLM provider sent to the backend. Unity currently always uses "dynamic".
        /// </summary>
        [JsonProperty("llm_provider")]
        public string LlmProvider { get; set; } = "dynamic";

        /// <summary>
        /// The core service URL to connect to.
        /// </summary>
        [JsonProperty("core_service_url")]
        public string CoreServiceUrl { get; set; } = string.Empty;

        /// <summary>
        /// Optional character session ID for session continuity.
        /// </summary>
        [JsonProperty("character_session_id", NullValueHandling = NullValueHandling.Ignore)]
        public string? CharacterSessionId { get; set; }

        /// <summary>
        /// Optional invocation metadata logged by core services.
        /// </summary>
        [JsonProperty("invocation_metadata", NullValueHandling = NullValueHandling.Ignore)]
        public RoomInvocationMetadata? InvocationMetadata { get; set; }

        /// <summary>
        /// Optional stable identifier for the developer's end user.
        /// Used for identity tracking, analytics, management, and memory lookup.
        /// </summary>
        [JsonProperty("end_user_id", NullValueHandling = NullValueHandling.Ignore)]
        public string? EndUserId { get; set; }

        /// <summary>
        /// Optional metadata associated with the end user.
        /// This is typically used for display and end-user management fields such as name.
        /// </summary>
        [JsonProperty("end_user_metadata", NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyDictionary<string, object>? EndUserMetadata { get; set; }

        /// <summary>
        /// Optional maximum number of participants.
        /// </summary>
        [JsonProperty("max_num_participants", NullValueHandling = NullValueHandling.Ignore)]
        public string? MaxNumParticipants { get; set; }

        /// <summary>
        /// Optional room name.
        /// </summary>
        [JsonProperty("room_name", NullValueHandling = NullValueHandling.Ignore)]
        public string? RoomName { get; set; }

        /// <summary>
        /// Optional video track name.
        /// </summary>
        [JsonProperty("video_track_name", NullValueHandling = NullValueHandling.Ignore)]
        public string? VideoTrackName { get; set; }

        /// <summary>
        /// Optional mode.
        /// </summary>
        [JsonProperty("mode", NullValueHandling = NullValueHandling.Ignore)]
        public string? Mode { get; set; }

        /// <summary>
        /// Whether to spawn the agent.
        /// </summary>
        [JsonProperty("spawn_agent", NullValueHandling = NullValueHandling.Ignore)]
        public bool? SpawnAgent { get; set; }

        /// <summary>
        /// Optional turn detection configuration.
        /// </summary>
        [JsonProperty("turn_detection_config", NullValueHandling = NullValueHandling.Ignore)]
        public TurnDetectionConfig? TurnDetectionConfig { get; set; }

        /// <summary>
        /// Optional initial STT enabled state for the session.
        /// </summary>
        [JsonProperty("default_stt_enabled", NullValueHandling = NullValueHandling.Ignore)]
        public bool? DefaultSttEnabled { get; set; }

        /// <summary>
        /// Optional blendshape provider to use for lip sync.
        /// When set, the server may stream blendshape data alongside audio.
        /// </summary>
        [JsonProperty("blendshape_provider", NullValueHandling = NullValueHandling.Ignore)]
        public string? BlendshapeProvider { get; set; }

        /// <summary>
        /// Optional blendshape transport configuration.
        /// Serialized under <c>blendshape_config</c>.
        /// </summary>
        [JsonProperty("blendshape_config", NullValueHandling = NullValueHandling.Ignore)]
        public ConvaiBlendshapeConfig? BlendshapeConfig { get; set; }

        /// <summary>
        /// Optional emotion configuration for server-side emotion streaming.
        /// </summary>
        [JsonProperty("emotion_config", NullValueHandling = NullValueHandling.Ignore)]
        public RoomEmotionConfig? EmotionConfig { get; set; }

        /// <summary>
        /// When true, the backend enables RTVI metrics for this session (debug/analytics).
        /// </summary>
        [JsonProperty("debug", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Debug { get; set; }

        /// <summary>
        /// Optional dynamic info included with the initial connection request.
        /// </summary>
        [JsonProperty("dynamic_info", NullValueHandling = NullValueHandling.Ignore)]
        public RoomDynamicInfo? DynamicInfo { get; set; }
    }

    /// <summary>
    /// Dynamic info payload for initial room-connect requests.
    /// </summary>
    public sealed class RoomDynamicInfo
    {
        /// <summary>
        /// Dynamic info text value.
        /// </summary>
        [JsonProperty("text")]
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// When true, backend keeps this dynamic info in context.
        /// </summary>
        [JsonProperty("keep_in_context")]
        public bool KeepInContext { get; set; }
    }

    /// <summary>
    /// Invocation metadata sent in room connect requests.
    /// </summary>
    public sealed class RoomInvocationMetadata
    {
        /// <summary>
        /// Source identifier for the SDK or client.
        /// </summary>
        [JsonProperty("source")]
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// Optional SDK or client version string.
        /// </summary>
        [JsonProperty("client_version", NullValueHandling = NullValueHandling.Ignore)]
        public string? ClientVersion { get; set; }

        /// <summary>
        /// Optional caller-provided extra metadata.
        /// </summary>
        [JsonProperty("extra_metadata", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string>? ExtraMetadata { get; set; }
    }
}
