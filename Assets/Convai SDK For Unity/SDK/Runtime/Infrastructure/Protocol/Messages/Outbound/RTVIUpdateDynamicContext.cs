namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>
    ///     Message to update the bot's temporary runtime (ephemeral) context in a unified way.
    /// </summary>
    public class RTVIUpdateDynamicContext : RTVISendMessageBase
    {
        public RTVIUpdateDynamicContext(DynamicContext dynamicContext)
        {
            Type = "context-update";
            Data = new { text = dynamicContext.Text, mode = dynamicContext.Mode, run_llm = dynamicContext.RunLlm };
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RTVIUpdateDynamicContext" /> class.
        /// </summary>
        /// <param name="text">The new context text to be applied. Required when <paramref name="mode" /> is not "reset".</param>
        /// <param name="mode">
        ///     How the new context is applied. "append" (default): add to existing; "replace": replace entirely;
        ///     "reset": clear ephemeral context (text optional).
        /// </param>
        /// <param name="runLlm">
        ///     Whether to trigger an LLM response after the update. "true": always; "false": never; "auto"
        ///     (default): server decides.
        /// </param>
        public RTVIUpdateDynamicContext(string text, string mode = "append", string runLlm = "auto")
        {
            Type = "context-update";
            Data = new { text, mode, run_llm = runLlm };
        }
    }
}
