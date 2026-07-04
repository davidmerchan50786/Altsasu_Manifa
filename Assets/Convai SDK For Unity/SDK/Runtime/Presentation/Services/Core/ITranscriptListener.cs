using Convai.Domain.Models;

namespace Convai.Runtime.Presentation.Services
{
    /// <summary>
    ///     Simple transcript listener interface for quick transcript UI hookups.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is a convenience adapter over <c>ConvaiManager.Transcripts</c> for subtitle or chat-style UI.
    ///         Implement it on a MonoBehaviour and the <see cref="TranscriptUIController" /> will register it automatically.
    ///     </para>
    ///     <para>
    ///         <b>Example Usage:</b>
    ///     </para>
    ///     <code>
    /// public class MySubtitles : MonoBehaviour, ITranscriptListener
    /// {
    ///     [SerializeField] private TMP_Text _subtitleText;
    ///
    ///     public void OnCharacterTranscript(string characterId, string characterName, string text, bool isFinal)
    ///     {
    ///         _subtitleText.text = $"{characterName}: {text}";
    ///     }
    ///
    ///     public void OnPlayerTranscript(string text, bool isFinal)
    ///     {
    ///         _subtitleText.text = $"You: {text}";
    ///     }
    /// }
    /// </code>
    ///     <para>
    ///         For richer transcript presentation, formatting, filtering, or room history,
    ///         use <see cref="ITranscriptUI" /> or <c>ConvaiManager.Transcripts</c>.
    ///         For code-driven reactions to individual transcript messages, use <c>ConvaiManager.Events</c>.
    ///     </para>
    ///     <para>
    ///         For multi-user scenarios with speaker attribution, use <see cref="IMultiUserTranscriptListener" />.
    ///     </para>
    /// </remarks>
    /// <seealso cref="ITranscriptUI" />
    /// <seealso cref="IMultiUserTranscriptListener" />
    public interface ITranscriptListener
    {
        /// <summary>
        ///     Optional: Filter to only receive transcripts from a specific character.
        ///     Return null to receive all transcripts (both character and player).
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Filtering Behavior:</b>
        ///         <list type="bullet">
        ///             <item>
        ///                 <description>
        ///                     When set to a character ID, only transcripts from that character will trigger
        ///                     <see cref="OnCharacterTranscript" />.
        ///                 </description>
        ///             </item>
        ///             <item>
        ///                 <description>
        ///                     Player transcripts (<see cref="OnPlayerTranscript" />) are always received regardless of
        ///                     this filter.
        ///                 </description>
        ///             </item>
        ///             <item>
        ///                 <description>When null (default), all character and player transcripts are received.</description>
        ///             </item>
        ///         </list>
        ///     </para>
        /// </remarks>
        public string FilterCharacterId => null;

        /// <summary>
        ///     Called when a character speaks.
        /// </summary>
        /// <param name="characterId">Character's unique ID</param>
        /// <param name="characterName">Character's display name</param>
        /// <param name="text">Transcript text (may be partial)</param>
        /// <param name="isFinal">True if this is the final version of the message</param>
        public void OnCharacterTranscript(string characterId, string characterName, string text, bool isFinal);

        /// <summary>
        ///     Called when the player speaks.
        /// </summary>
        /// <param name="text">Player's speech text</param>
        /// <param name="isFinal">True if this is the final version</param>
        public void OnPlayerTranscript(string text, bool isFinal);
    }

    /// <summary>
    ///     Extended transcript listener interface with multi-user speaker attribution support.
    ///     Implement this for scenarios where multiple players interact with the same character.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This interface extends <see cref="ITranscriptListener" /> with an additional callback that
    ///         provides full speaker attribution data for player transcripts in multi-user scenarios.
    ///     </para>
    ///     <para>
    ///         <b>Example Usage:</b>
    ///     </para>
    ///     <code>
    /// public class MultiPlayerChat : MonoBehaviour, IMultiUserTranscriptListener
    /// {
    ///     public void OnCharacterTranscript(string characterId, string characterName, string text, bool isFinal)
    ///     {
    ///         AddMessage(characterName, text, isFinal, isPlayer: false);
    ///     }
    ///
    ///     public void OnPlayerTranscript(string text, bool isFinal)
    ///     {
    ///
    ///         AddMessage("You", text, isFinal, isPlayer: true);
    ///     }
    ///
    ///     public void OnPlayerTranscriptWithSpeaker(
    ///         string speakerId, string speakerName, string participantId, string text, bool isFinal)
    ///     {
    ///
    ///         string displayName = string.IsNullOrEmpty(speakerName) ? "Player" : speakerName;
    ///         AddMessage(displayName, text, isFinal, isPlayer: true, speakerId: speakerId);
    ///     }
    /// }
    /// </code>
    ///     <para>
    ///         <b>When to use this interface:</b>
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>Multiple players can talk to the same character simultaneously</description>
    ///             </item>
    ///             <item>
    ///                 <description>You need to identify which player said what in a shared session</description>
    ///             </item>
    ///             <item>
    ///                 <description>Your UI displays speaker names/avatars for each player</description>
    ///             </item>
    ///         </list>
    ///     </para>
    /// </remarks>
    /// <seealso cref="ITranscriptListener" />
    /// <seealso cref="SpeakerInfo" />
    public interface IMultiUserTranscriptListener : ITranscriptListener
    {
        /// <summary>
        ///     Called when a player speaks in a multi-user scenario with speaker attribution.
        /// </summary>
        /// <param name="speakerId">Unique speaker ID from the backend's speaker directory</param>
        /// <param name="speakerName">Human-readable speaker name for display</param>
        /// <param name="participantId">LiveKit participant ID (SID)</param>
        /// <param name="text">Player's speech text</param>
        /// <param name="isFinal">True if this is the final version</param>
        /// <remarks>
        ///     This callback is invoked in addition to <see cref="ITranscriptListener.OnPlayerTranscript" />
        ///     when speaker attribution data is available. If your implementation handles this callback,
        ///     you may want to skip processing in <see cref="ITranscriptListener.OnPlayerTranscript" />
        ///     to avoid duplicate handling.
        /// </remarks>
        public void OnPlayerTranscriptWithSpeaker(
            string speakerId,
            string speakerName,
            string participantId,
            string text,
            bool isFinal);
    }
}
