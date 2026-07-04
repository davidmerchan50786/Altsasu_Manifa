using System;
using Convai.Runtime.Presentation.Presenters;

namespace Convai.Runtime.Presentation.Strategies
{
    /// <summary>
    ///     Strategy interface for transcript presentation logic.
    ///     Different UI modes (Chat, Subtitle, Q&amp;A) implement different strategies
    ///     for processing and aggregating transcript events.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The <see cref="Services.TranscriptUIController" /> owns the active strategy and routes
    ///         transcript events to it.
    ///     </para>
    ///     <para>
    ///         <b>Implementations include:</b>
    ///         <list type="bullet">
    ///             <item>
    ///                 <description><c>ChatPresentationStrategy</c>: Aggregates chunks into message bubbles</description>
    ///             </item>
    ///             <item>
    ///                 <description><c>SubtitlePresentationStrategy</c>: Streaming text with auto-clear</description>
    ///             </item>
    ///             <item>
    ///                 <description><c>QAPresentationStrategy</c>: Question-answer pair handling</description>
    ///             </item>
    ///         </list>
    ///     </para>
    /// </remarks>
    public interface ITranscriptPresentationStrategy : IDisposable
    {
        /// <summary>
        ///     Processes a new transcript chunk (interim or final).
        ///     The strategy decides how to aggregate, buffer, or forward the message.
        /// </summary>
        /// <param name="viewModel">The transcript view model to process.</param>
        public void HandleMessage(TranscriptViewModel viewModel);

        /// <summary>
        ///     Handles player turn completion, triggered when character starts speaking.
        ///     Finalizes any in-progress player message bubbles using accumulated text.
        /// </summary>
        public void CompletePlayerTurn();

        /// <summary>
        ///     Handles character turn completion, typically triggered by CharacterTurnCompleted.
        ///     Completes the character's message bubble so that the next turn creates a new bubble.
        /// </summary>
        /// <param name="characterId">
        ///     The character ID whose turn has ended, or null to complete all active character messages.
        /// </param>
        public void CompleteCharacterTurn(string characterId);

        /// <summary>
        ///     Resets all internal state. Called when clearing the UI or switching modes.
        /// </summary>
        public void ClearAll();

        /// <summary>
        ///     Checks if there are any active (in-progress) player messages.
        /// </summary>
        /// <returns>True if there's at least one player message that hasn't been completed.</returns>
        public bool HasActivePlayerMessage();

        /// <summary>
        ///     Raised when a message is updated (new text, aggregation change).
        ///     The view model contains the current aggregated state ready for display.
        /// </summary>
        public event Action<TranscriptViewModel> OnMessageUpdated;

        /// <summary>
        ///     Raised when a message is completed (speaker changed, turn ended).
        ///     The string parameter is the speaker/message ID.
        /// </summary>
        public event Action<string> OnMessageCompleted;
    }
}
