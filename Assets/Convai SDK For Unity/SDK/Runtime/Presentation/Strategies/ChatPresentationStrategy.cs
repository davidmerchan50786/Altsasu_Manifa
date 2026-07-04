using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Domain.Models;
using Convai.Runtime.Presentation.Presenters;
using Convai.Runtime.Utilities;

namespace Convai.Runtime.Presentation.Strategies
{
    /// <summary>
    ///     Chat presentation strategy that renders one transcript turn per message bubble.
    ///     Aggregation happens in <c>RoomTranscriptEngine</c>, not in the UI layer.
    /// </summary>
    public sealed class ChatPresentationStrategy : ITranscriptPresentationStrategy
    {
        private readonly Dictionary<string, TranscriptViewModel> _activeMessages = new();
        private readonly ILogger _logger;
        private bool _disposed;

        public ChatPresentationStrategy(ILogger logger = null)
        {
            _logger = logger;
        }

        public event Action<TranscriptViewModel> OnMessageUpdated;
        public event Action<string> OnMessageCompleted;

        public void HandleMessage(TranscriptViewModel viewModel)
        {
            if (_disposed) return;

            _logger?.Debug(
                $"[ChatPresentationStrategy] HandleMessage: Speaker={viewModel.Speaker}, MessageId={viewModel.MessageId}, Lifecycle={viewModel.Lifecycle}, Text=\"{viewModel.Text}\"");

            _activeMessages[viewModel.MessageId] = viewModel;
            SafeEventInvoker.Invoke(
                OnMessageUpdated,
                viewModel,
                _logger,
                "ChatPresentationStrategy.OnMessageUpdated",
                LogCategory.UI);

            if (viewModel.Lifecycle != TranscriptLifecycle.Completed) return;

            _activeMessages.Remove(viewModel.MessageId);
            SafeEventInvoker.Invoke(
                OnMessageCompleted,
                viewModel.MessageId,
                _logger,
                "ChatPresentationStrategy.OnMessageCompleted",
                LogCategory.UI);
        }

        public void CompletePlayerTurn()
        {
            if (_disposed) return;

            foreach (KeyValuePair<string, TranscriptViewModel> pair in _activeMessages)
            {
                if (pair.Value.Speaker != TranscriptSpeaker.Player) continue;
                SafeEventInvoker.Invoke(
                    OnMessageCompleted,
                    pair.Key,
                    _logger,
                    "ChatPresentationStrategy.OnMessageCompleted",
                    LogCategory.UI);
            }

            RemoveMessagesBySpeaker(TranscriptSpeaker.Player);
        }

        public void CompleteCharacterTurn(string characterId)
        {
            if (_disposed) return;

            foreach (KeyValuePair<string, TranscriptViewModel> pair in _activeMessages)
            {
                if (pair.Value.Speaker != TranscriptSpeaker.Character) continue;
                if (!string.IsNullOrWhiteSpace(characterId) &&
                    !string.Equals(pair.Value.PlayerOrCharacterId, characterId, StringComparison.Ordinal)) continue;

                SafeEventInvoker.Invoke(
                    OnMessageCompleted,
                    pair.Key,
                    _logger,
                    "ChatPresentationStrategy.OnMessageCompleted",
                    LogCategory.UI);
            }

            RemoveMessagesBySpeaker(TranscriptSpeaker.Character, characterId);
        }

        public bool HasActivePlayerMessage()
        {
            foreach (TranscriptViewModel viewModel in _activeMessages.Values)
                if (viewModel.Speaker == TranscriptSpeaker.Player)
                    return true;

            return false;
        }

        public void ClearAll()
        {
            foreach (string messageId in _activeMessages.Keys)
            {
                SafeEventInvoker.Invoke(
                    OnMessageCompleted,
                    messageId,
                    _logger,
                    "ChatPresentationStrategy.OnMessageCompleted",
                    LogCategory.UI);
            }

            _activeMessages.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _activeMessages.Clear();
        }

        private void RemoveMessagesBySpeaker(TranscriptSpeaker speaker, string playerOrCharacterId = null)
        {
            List<string> toRemove = new();

            foreach (KeyValuePair<string, TranscriptViewModel> pair in _activeMessages)
            {
                if (pair.Value.Speaker != speaker) continue;
                if (!string.IsNullOrWhiteSpace(playerOrCharacterId) &&
                    !string.Equals(pair.Value.PlayerOrCharacterId, playerOrCharacterId,
                        StringComparison.Ordinal)) continue;

                toRemove.Add(pair.Key);
            }

            foreach (string messageId in toRemove)
                _activeMessages.Remove(messageId);
        }
    }
}
