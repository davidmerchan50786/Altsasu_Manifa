namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Outbound user text message.</summary>
    public class RTVIUserTextMessage : RTVISendMessageBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="RTVIUserTextMessage" /> class.
        /// </summary>
        /// <param name="text">User text to send.</param>
        public RTVIUserTextMessage(string text)
        {
            Type = "user_text_message";
            Data = new { text };
        }
    }
}
