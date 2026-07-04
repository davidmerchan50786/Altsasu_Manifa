using System;
using Convai.Domain.Models;

namespace Convai.Domain.DomainEvents.Transcript
{
    /// <summary>
    ///     Domain event raised when a character transcript is received.
    /// </summary>
    public readonly struct CharacterTranscriptReceived
    {
        /// <summary>
        ///     The transcript message from the character.
        /// </summary>
        public TranscriptMessage Message { get; }

        /// <summary>
        ///     Creates a new CharacterTranscriptReceived event.
        /// </summary>
        public CharacterTranscriptReceived(TranscriptMessage message)
        {
            Message = message;
        }

        /// <summary>
        ///     Creates a CharacterTranscriptReceived event from individual parameters.
        /// </summary>
        /// <param name="characterId">The character's unique identifier</param>
        /// <param name="displayName">The character's display name</param>
        /// <param name="text">The transcript text</param>
        /// <param name="isFinal">Whether this is a final transcript</param>
        /// <param name="confidence">Optional confidence score</param>
        /// <returns>A new CharacterTranscriptReceived event</returns>
        public static CharacterTranscriptReceived Create(
            string characterId,
            string displayName,
            string text,
            bool isFinal,
            float? confidence = null)
        {
            var message = TranscriptMessage.Create(
                characterId,
                displayName,
                text,
                isFinal,
                confidence
            );

            return new CharacterTranscriptReceived(message);
        }

        /// <summary>
        ///     Gets the character ID from the message.
        /// </summary>
        public string CharacterId => Message.PlayerOrCharacterId;

        /// <summary>
        ///     Gets the character's display name from the message.
        /// </summary>
        public string CharacterName => Message.DisplayName;

        /// <summary>
        ///     Gets the transcript text from the message.
        /// </summary>
        public string Text => Message.Text;

        /// <summary>
        ///     Checks if this is a final transcript.
        /// </summary>
        public bool IsFinal => Message.IsFinal;

        /// <summary>
        ///     Checks if this is an interim transcript.
        /// </summary>
        public bool IsInterim => Message.IsInterim;

        /// <summary>
        ///     Gets the timestamp when the transcript was received.
        /// </summary>
        public DateTime Timestamp => Message.Timestamp;
    }
}
