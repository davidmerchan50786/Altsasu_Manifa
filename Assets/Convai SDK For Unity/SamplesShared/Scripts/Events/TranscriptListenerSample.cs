using Convai.Runtime.Presentation.Services;
using TMPro;
using UnityEngine;

namespace Convai.Sample.Events
{
    /// <summary>
    ///     Newcomer-facing sample that shows the quick transcript UI path.
    /// </summary>
    public sealed class TranscriptListenerSample : MonoBehaviour, ITranscriptListener
    {
        [SerializeField] private TMP_Text transcriptText;
        [SerializeField] private string filterCharacterId;

        public string FilterCharacterId => string.IsNullOrWhiteSpace(filterCharacterId) ? null : filterCharacterId;

        public void OnCharacterTranscript(string characterId, string characterName, string text, bool isFinal)
        {
            if (transcriptText == null) return;
            transcriptText.text = $"{characterName}: {text}";
        }

        public void OnPlayerTranscript(string text, bool isFinal)
        {
            if (transcriptText == null) return;
            transcriptText.text = $"You: {text}";
        }
    }
}
