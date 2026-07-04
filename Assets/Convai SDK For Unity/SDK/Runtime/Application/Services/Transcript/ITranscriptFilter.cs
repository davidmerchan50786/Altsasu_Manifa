using Convai.Domain.Models;

namespace Convai.Application.Services.Transcript
{
    /// <summary>
    ///     Determines whether transcript messages should be surfaced to the view layer.
    /// </summary>
    /// <remarks>
    ///     Implement this interface to create custom filtering logic for transcript display.
    ///     For example, filtering out profanity, short messages, or messages from specific speakers.
    /// </remarks>
    public interface ITranscriptFilter
    {
        /// <summary>
        ///     Determines whether a transcript message should be displayed.
        /// </summary>
        /// <param name="message">The transcript message to evaluate.</param>
        /// <returns><c>true</c> if the message should be displayed; otherwise, <c>false</c>.</returns>
        public bool ShouldDisplay(TranscriptMessage message);
    }

    /// <summary>
    ///     Default filter that allows all non-empty messages.
    /// </summary>
    /// <remarks>
    ///     This implementation filters out messages with null, empty, or whitespace-only text.
    /// </remarks>
    public sealed class DefaultTranscriptFilter : ITranscriptFilter
    {
        /// <inheritdoc />
        public bool ShouldDisplay(TranscriptMessage message) => !string.IsNullOrWhiteSpace(message.Text);
    }
}
