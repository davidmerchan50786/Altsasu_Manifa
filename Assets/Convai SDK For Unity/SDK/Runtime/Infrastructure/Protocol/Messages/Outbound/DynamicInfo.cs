using Newtonsoft.Json;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Dynamic info payload model.</summary>
    public class DynamicInfo
    {
        /// <summary>Gets or sets the dynamic info text.</summary>
        [JsonProperty("text")]
        public string Text { get; set; }
    }
}
