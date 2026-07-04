using Convai.Runtime.Presentation.Presenters;

namespace Convai.Runtime.Presentation.Services
{
    /// <summary>
    ///     Advanced transcript UI interface for full control over message display.
    ///     Implementations are discovered and registered automatically.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Mode-Based Activation:</b> The <see cref="Identifier" /> property determines which mode activates this UI:
    ///         <list type="bullet">
    ///             <item>
    ///                 <description><c>"Chat"</c> → Activated when <see cref="TranscriptUIMode.Chat" /> is selected</description>
    ///             </item>
    ///             <item>
    ///                 <description><c>"Subtitle"</c> → Activated when <see cref="TranscriptUIMode.Subtitle" /> is selected</description>
    ///             </item>
    ///             <item>
    ///                 <description>
    ///                     <c>"QuestionAnswer"</c> → Activated when <see cref="TranscriptUIMode.QuestionAnswer" /> is
    ///                     selected
    ///                 </description>
    ///             </item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         If your UI needs runtime services during initialization, expose an explicit
    ///         initialization method and let the composition root wire it up.
    ///     </para>
    ///     <para>
    ///         <b>Example Implementation:</b>
    ///     </para>
    ///     <code>
    /// public class MyTranscriptUI : MonoBehaviour, ITranscriptUI
    /// {
    ///     public string Identifier =&gt; "Subtitle";
    ///     public bool IsActive =&gt; gameObject.activeInHierarchy;
    /// 
    ///     public void Initialize(MyTranscriptDependencies dependencies)
    ///     {
    ///         // capture any runtime services you need here
    ///     }
    /// 
    ///     public void DisplayMessage(TranscriptViewModel viewModel)
    ///     {
    ///         _text.text = $"{viewModel.DisplayName}: {viewModel.FormattedText}";
    ///     }
    /// 
    ///     public void CompleteMessage(string messageId) { /* Finalize animations */ }
    ///     public void ClearAll() { _text.text = ""; }
    ///     public void SetActive(bool active) { gameObject.SetActive(active); }
    /// }
    /// </code>
    ///     <para>
    ///         For simpler use cases, consider using <see cref="ITranscriptListener" /> instead,
    ///         which provides a 2-method interface with auto-discovery.
    ///     </para>
    /// </remarks>
    /// <seealso cref="ITranscriptListener" />
    /// <seealso cref="TranscriptUIController" />
    /// <seealso cref="TranscriptViewModel" />
    public interface ITranscriptUI
    {
        /// <summary>
        ///     Gets the unique identifier for this transcript UI instance.
        ///     Used to differentiate between multiple UI instances (e.g., "Chat", "Subtitle", "QuestionAnswer").
        /// </summary>
        public string Identifier { get; }

        /// <summary>
        ///     Gets whether this UI is currently active and visible.
        /// </summary>
        public bool IsActive { get; }

        /// <summary>
        ///     Displays or updates a transcript message.
        ///     If a message with the same identifier exists, it should be updated.
        /// </summary>
        /// <param name="viewModel">The transcript view model to display.</param>
        public void DisplayMessage(TranscriptViewModel viewModel);

        /// <summary>
        ///     Marks a message as completed (no more updates expected).
        ///     Implementations may use this to finalize animations or styling.
        /// </summary>
        /// <param name="messageId">The identifier of the message to complete.</param>
        public void CompleteMessage(string messageId);

        /// <summary>
        ///     Completes all active player messages.
        ///     Called when the player's conversational turn ends.
        /// </summary>
        /// <remarks>
        ///     This method is called by TranscriptUIController when:
        ///     <list type="bullet">
        ///         <item>
        ///             <description><c>CharacterSpeechStateChanged</c> event indicates character started speaking (fallback)</description>
        ///         </item>
        ///         <item>
        ///             <description>Timeout occurs (no turn-end signal within X seconds, handled by UI implementation)</description>
        ///         </item>
        ///     </list>
        ///     This ensures all player speech segments (e.g., "Hello." pause "How are you?")
        ///     are aggregated into a single chat bubble instead of creating multiple bubbles.
        /// </remarks>
        public void CompletePlayerTurn();

        /// <summary>
        ///     Clears all displayed messages.
        /// </summary>
        public void ClearAll();

        /// <summary>
        ///     Sets the active/visible state of this UI.
        /// </summary>
        /// <param name="active">True to show, false to hide.</param>
        public void SetActive(bool active);
    }

    /// <summary>
    ///     Defines the type of transcript UI mode.
    /// </summary>
    public enum TranscriptUIMode
    {
        /// <summary>Chat-style UI with scrollable message history.</summary>
        Chat,

        /// <summary>Subtitle-style UI showing only the current message.</summary>
        Subtitle,

        /// <summary>Question-Answer style UI.</summary>
        QuestionAnswer
    }
}
