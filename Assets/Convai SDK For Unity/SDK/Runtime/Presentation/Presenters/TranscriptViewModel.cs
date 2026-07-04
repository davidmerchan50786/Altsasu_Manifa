using System;
using Convai.Domain.Models;

namespace Convai.Runtime.Presentation.Presenters
{
    /// <summary>
    ///     Identifies the speaker associated with a transcript message.
    /// </summary>
    public enum TranscriptSpeaker
    {
        /// <summary>
        ///     The transcript is from an AI character.
        /// </summary>
        Character,

        /// <summary>
        ///     The transcript is from the player (user).
        /// </summary>
        Player
    }

    /// <summary>
    ///     View-model representation of a transcript message consumed by Unity UI layers.
    /// </summary>
    /// <remarks>
    ///     This struct provides a UI-friendly projection of a room transcript turn.
    ///     TranscriptViewModel is immutable and thread-safe.
    /// </remarks>
    public readonly struct TranscriptViewModel
    {
        /// <summary>
        ///     Creates a new TranscriptViewModel.
        /// </summary>
        /// <param name="speaker">The speaker type (Character or Player).</param>
        /// <param name="message">The underlying transcript message.</param>
        /// <param name="formattedText">Pre-formatted text for display.</param>
        /// <param name="messageId">Unique identifier for the rendered message bubble.</param>
        /// <param name="lifecycle">Current lifecycle of the transcript update.</param>
        /// <param name="turn">Optional source room-turn snapshot.</param>
        public TranscriptViewModel(
            TranscriptSpeaker speaker,
            TranscriptMessage message,
            string formattedText,
            string messageId = null,
            TranscriptLifecycle lifecycle = TranscriptLifecycle.Streaming,
            TranscriptTurnSnapshot turn = null)
        {
            Speaker = speaker;
            Message = NormalizeMessage(message);
            FormattedText = formattedText ?? string.Empty;
            MessageId = string.IsNullOrWhiteSpace(messageId) ? Message.PlayerOrCharacterId : messageId;
            Lifecycle = ResolveLifecycle(Message, lifecycle, turn);
            Turn = turn;
        }

        /// <summary>
        ///     Gets the speaker type (Character or Player).
        /// </summary>
        public TranscriptSpeaker Speaker { get; }

        /// <summary>
        ///     Gets the underlying transcript message.
        /// </summary>
        public TranscriptMessage Message { get; }

        /// <summary>
        ///     Gets the pre-formatted text for display.
        /// </summary>
        public string FormattedText { get; }

        /// <summary>
        ///     Gets the unique identifier for the rendered message bubble.
        /// </summary>
        public string MessageId { get; }

        /// <summary>
        ///     Gets the lifecycle for this transcript update.
        /// </summary>
        public TranscriptLifecycle Lifecycle { get; }

        /// <summary>
        ///     Gets the source room-turn snapshot, when available.
        /// </summary>
        public TranscriptTurnSnapshot Turn { get; }

        /// <summary>
        ///     Gets the unique identifier of the speaker.
        /// </summary>
        public string PlayerOrCharacterId => Message.PlayerOrCharacterId;

        /// <summary>
        ///     Gets the display name of the speaker.
        /// </summary>
        public string DisplayName => Message.DisplayName;

        /// <summary>
        ///     Gets the transcript text content.
        /// </summary>
        public string Text => Turn?.DisplayText ?? Message.Text;

        /// <summary>
        ///     Gets whether this is the final transcript for the current utterance.
        /// </summary>
        public bool IsFinal => Lifecycle != TranscriptLifecycle.Streaming;

        /// <summary>
        ///     Gets the timestamp when the transcript was received.
        /// </summary>
        public DateTime Timestamp => Message.Timestamp;

        /// <summary>
        ///     Gets whether the transcript text is empty or whitespace.
        /// </summary>
        public bool IsEmpty => string.IsNullOrWhiteSpace(Text);

        /// <summary>
        ///     Gets the LiveKit participant ID for multi-user scenarios.
        /// </summary>
        public string ParticipantId => Message.ParticipantId;

        /// <summary>
        ///     Gets the speaker type (Character, Player, System, Unknown).
        /// </summary>
        public SpeakerType SpeakerType => Message.SpeakerType;

        /// <summary>
        ///     Gets whether this transcript has multi-user speaker attribution.
        /// </summary>
        public bool HasSpeakerInfo =>
            !string.IsNullOrEmpty(ParticipantId) || !string.IsNullOrEmpty(PlayerOrCharacterId);

        private static TranscriptLifecycle ResolveLifecycle(
            TranscriptMessage message,
            TranscriptLifecycle lifecycle,
            TranscriptTurnSnapshot turn)
        {
            if (turn != null) return turn.Lifecycle;
            return message.IsFinal ? TranscriptLifecycle.Completed : TranscriptLifecycle.Streaming;
        }

        private static TranscriptMessage NormalizeMessage(TranscriptMessage message)
        {
            string text = message.Text ?? string.Empty;
            string displayName = message.DisplayName ?? string.Empty;
            string playerOrCharacterId = message.PlayerOrCharacterId ?? string.Empty;
            string participantId = message.ParticipantId ?? string.Empty;
            DateTime timestamp = message.Timestamp == default ? DateTime.UtcNow : message.Timestamp;

            return new TranscriptMessage(
                playerOrCharacterId,
                displayName,
                text,
                message.IsFinal,
                timestamp,
                message.Confidence,
                participantId,
                message.SpeakerType);
        }
    }
}
