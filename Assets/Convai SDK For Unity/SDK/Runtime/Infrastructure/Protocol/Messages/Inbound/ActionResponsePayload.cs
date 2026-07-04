#nullable enable

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Payload for the action-response server message.</summary>
    /// <remarks>
    ///     Sent by the backend when the character's response contains action tags
    ///     (e.g., "[wave]", "[sit]"). The tag extraction processor strips these
    ///     from the text stream and sends them separately.
    /// </remarks>
    public sealed class ActionResponsePayload
    {
        /// <summary>List of action tag strings extracted from the character's response.</summary>
        [JsonProperty("actions")]
        public List<string>? Actions { get; set; }
    }
}
