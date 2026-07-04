using Convai.Domain.Models;

namespace Convai.Application.Services.Transcript
{
    /// <summary>
    ///     Formats transcript messages for presentation layers.
    /// </summary>
    /// <remarks>
    ///     Implement this interface to create custom formatting for transcript display.
    ///     For example, adding speaker prefixes, applying rich text formatting, or localizing content.
    /// </remarks>
    public interface ITranscriptFormatter
    {
        /// <summary>
        ///     Formats a character (AI) transcript message for display.
        /// </summary>
        /// <param name="message">The character transcript message to format.</param>
        /// <returns>The formatted message string ready for display.</returns>
        public string FormatCharacterMessage(TranscriptMessage message);

        /// <summary>
        ///     Formats a player (human) transcript message for display.
        /// </summary>
        /// <param name="message">The player transcript message to format.</param>
        /// <returns>The formatted message string ready for display.</returns>
        public string FormatPlayerMessage(TranscriptMessage message);
    }

    /// <summary>
    ///     Default formatter that returns the raw transcript text.
    /// </summary>
    /// <remarks>
    ///     This implementation returns the transcript text as-is, with null values converted to empty strings.
    /// </remarks>
    public sealed class DefaultTranscriptFormatter : ITranscriptFormatter
    {
        /// <inheritdoc />
        public string FormatCharacterMessage(TranscriptMessage message) => message.Text ?? string.Empty;

        /// <inheritdoc />
        public string FormatPlayerMessage(TranscriptMessage message) => message.Text ?? string.Empty;
    }
}
