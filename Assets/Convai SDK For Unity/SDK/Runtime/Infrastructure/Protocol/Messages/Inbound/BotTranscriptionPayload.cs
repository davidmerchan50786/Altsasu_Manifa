#nullable enable

using Newtonsoft.Json;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Payload for bot transcription events emitted by the server.</summary>
    public class BotTranscriptionPayload
    {
        /// <summary>Gets or sets the transcribed bot text.</summary>
        [JsonProperty("text")]
        public string? Text { get; set; }
    }
}
