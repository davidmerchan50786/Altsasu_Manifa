using Newtonsoft.Json;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>
    ///     Payload for updating the bot's ephemeral (temporary runtime) context.
    /// </summary>
    public class DynamicContext
    {
        /// <summary>New context text to apply. Required when <see cref="Mode" /> is not "reset".</summary>
        [JsonProperty("text")]
        public string Text { get; set; }

        /// <summary>How to apply the context: "append", "replace", or "reset". Default "append".</summary>
        [JsonProperty("mode")]
        public string Mode { get; set; }

        /// <summary>Whether to trigger an LLM response: "true", "false", or "auto". Default "auto".</summary>
        [JsonProperty("run_llm")]
        public string RunLlm { get; set; }
    }
}
