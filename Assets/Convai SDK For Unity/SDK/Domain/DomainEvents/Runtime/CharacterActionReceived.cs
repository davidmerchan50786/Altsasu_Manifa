using System;
using System.Collections.Generic;

namespace Convai.Domain.DomainEvents.Runtime
{
    /// <summary>
    ///     Domain event raised when the backend extracts action tags from a character's response.
    ///     Action tags (e.g., "[wave]", "[sit]") are stripped from the text stream and
    ///     delivered separately so game logic can trigger animations or state changes.
    /// </summary>
    /// <remarks>
    ///     Subscribe via EventHub or <c>ConvaiManager.Events.OnCharacterActionReceived</c>.
    ///     <code>
    /// _eventHub.Subscribe&lt;CharacterActionReceived&gt;(this, e =&gt;
    /// {
    ///     foreach (string action in e.Actions)
    ///     {
    ///         Debug.Log($"Character {e.CharacterId} action: {action}");
    ///         PlayAnimation(e.CharacterId, action);
    ///     }
    /// });
    /// </code>
    /// </remarks>
    public readonly struct CharacterActionReceived
    {
        /// <summary>The character's unique identifier (resolved from participant ID when possible).</summary>
        public string CharacterId { get; }

        /// <summary>List of action tag strings extracted from the character's response.</summary>
        public IReadOnlyList<string> Actions { get; }

        /// <summary>When the event occurred (UTC).</summary>
        public DateTime Timestamp { get; }

        /// <summary>Creates a new CharacterActionReceived event.</summary>
        public CharacterActionReceived(string characterId, IReadOnlyList<string> actions, DateTime timestamp)
        {
            CharacterId = characterId ?? string.Empty;
            Actions = actions ?? Array.Empty<string>();
            Timestamp = timestamp;
        }

        /// <summary>Creates a CharacterActionReceived event with the current UTC timestamp.</summary>
        public static CharacterActionReceived Create(string characterId, IReadOnlyList<string> actions) =>
            new(characterId, actions, DateTime.UtcNow);
    }
}
