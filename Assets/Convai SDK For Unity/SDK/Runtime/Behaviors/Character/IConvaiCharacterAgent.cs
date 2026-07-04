using System.Collections.Generic;
using UnityEngine;

namespace Convai.Runtime.Behaviors
{
    /// <summary>
    ///     Unity-facing abstraction exposed to character behaviours for interacting with a character instance.
    /// </summary>
    public interface IConvaiCharacterAgent
    {
        /// <summary>
        ///     Unique identifier for the Character.
        /// </summary>
        public string CharacterId { get; }

        /// <summary>
        ///     Display name for the Character.
        /// </summary>
        public string CharacterName { get; }

        /// <summary>
        ///     Gets the name tag color for transcript display.
        /// </summary>
        public Color NameTagColor { get; }

        /// <summary>
        ///     Gets whether session resume is enabled for this character.
        /// </summary>
        public bool EnableSessionResume { get; }

        /// <summary>
        ///     Gets the initial dynamic info text to include in room connection requests.
        /// </summary>
        public string InitialDynamicInfoText { get; }

        /// <summary>
        ///     Gets whether initial dynamic info should be kept in server context.
        /// </summary>
        public bool InitialDynamicInfoKeepInContext { get; }

        /// <summary>
        ///     Sends a trigger message through the Character to the backend.
        /// </summary>
        /// <param name="triggerName">Name of the trigger to dispatch.</param>
        /// <param name="triggerMessage">Optional payload accompanying the trigger.</param>
        public void SendTrigger(string triggerName, string triggerMessage = null);

        /// <summary>
        ///     Sends dynamic context information to the backend.
        ///     This is injected as a context update for the character.
        /// </summary>
        /// <param name="contextText">The dynamic context text to send.</param>
        public void SendDynamicInfo(string contextText);

        /// <summary>
        ///     Updates template keys for narrative design placeholder resolution.
        ///     Template keys like {PlayerName} in objectives will be replaced with the corresponding value.
        /// </summary>
        /// <param name="templateKeys">Dictionary of key-value pairs to update.</param>
        public void UpdateTemplateKeys(Dictionary<string, string> templateKeys);
    }
}
