using System.Collections.Generic;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Outbound trigger message.</summary>
    public class RTVITriggerMessage : RTVISendMessageBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="RTVITriggerMessage" /> class.
        /// </summary>
        /// <param name="triggerName">Trigger name to execute on the server.</param>
        /// <param name="triggerMessage">Optional trigger message payload.</param>
        public RTVITriggerMessage(string triggerName, string triggerMessage = null)
        {
            Type = "trigger-message";
            Data = new Dictionary<string, string>
            {
                { "trigger_name", triggerName }, { "trigger_message", triggerMessage }
            };
        }
    }
}
