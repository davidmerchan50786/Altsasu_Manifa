using System;
using Convai.Domain.Logging;
using Convai.Domain.Models;
using Convai.Runtime.Presentation.Presenters;
using Convai.Runtime.Utilities;

namespace Convai.Runtime.Presentation.Strategies
{
    /// <summary>
    ///     Question-Answer presentation strategy for displaying Q&amp;A style transcripts.
    ///     Shows player question and character answer as a pair.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Behavior:</b>
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>Forwards all messages for UI display</description>
    ///             </item>
    ///             <item>
    ///                 <description>Emits completion when character sends a final response</description>
    ///             </item>
    ///             <item>
    ///                 <description>Designed for simple Q&amp;A interfaces (question above, answer below)</description>
    ///             </item>
    ///         </list>
    ///     </para>
    /// </remarks>
    public sealed class QAPresentationStrategy : ITranscriptPresentationStrategy
    {
        private readonly ILogger _logger;
        private string _currentAnswerId;
        private string _currentQuestionId;
        private bool _disposed;
        private bool _hasActivePlayerMessage;

        /// <summary>
        ///     Initializes a new instance of the <see cref="QAPresentationStrategy" /> class.
        /// </summary>
        /// <param name="logger">Optional logger for diagnostics.</param>
        public QAPresentationStrategy(ILogger logger = null)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public event Action<TranscriptViewModel> OnMessageUpdated;

        /// <inheritdoc />
        public event Action<string> OnMessageCompleted;

        /// <inheritdoc />
        public void HandleMessage(TranscriptViewModel viewModel)
        {
            if (_disposed) return;

            _logger?.Debug(
                $"[QAPresentationStrategy] HandleMessage: Speaker={viewModel.Speaker}, IsFinal={viewModel.IsFinal}, Text=\"{viewModel.Text}\"");

            if (viewModel.Speaker == TranscriptSpeaker.Player)
            {
                _currentQuestionId = viewModel.MessageId;
                _hasActivePlayerMessage = viewModel.Lifecycle != TranscriptLifecycle.Completed;
            }
            else
                _currentAnswerId = viewModel.MessageId;

            SafeEventInvoker.Invoke(
                OnMessageUpdated,
                viewModel,
                _logger,
                "QAPresentationStrategy.OnMessageUpdated",
                LogCategory.UI);

            if (viewModel.Lifecycle == TranscriptLifecycle.Completed)
            {
                if (viewModel.Speaker == TranscriptSpeaker.Player) _hasActivePlayerMessage = false;
                SafeEventInvoker.Invoke(
                    OnMessageCompleted,
                    viewModel.MessageId,
                    _logger,
                    "QAPresentationStrategy.OnMessageCompleted",
                    LogCategory.UI);
            }
        }

        /// <inheritdoc />
        public void CompletePlayerTurn()
        {
            if (_disposed) return;

            _logger?.Debug("[QAPresentationStrategy] CompletePlayerTurn");

            if (_hasActivePlayerMessage && !string.IsNullOrEmpty(_currentQuestionId))
            {
                SafeEventInvoker.Invoke(
                    OnMessageCompleted,
                    _currentQuestionId,
                    _logger,
                    "QAPresentationStrategy.OnMessageCompleted",
                    LogCategory.UI);
            }

            _hasActivePlayerMessage = false;
        }

        /// <inheritdoc />
        public void CompleteCharacterTurn(string characterId)
        {
            if (_disposed) return;

            _logger?.Debug($"[QAPresentationStrategy] CompleteCharacterTurn for: \"{characterId ?? "(all)"}\"");

            if (!string.IsNullOrEmpty(_currentAnswerId))
            {
                SafeEventInvoker.Invoke(
                    OnMessageCompleted,
                    _currentAnswerId,
                    _logger,
                    "QAPresentationStrategy.OnMessageCompleted",
                    LogCategory.UI);
                _currentAnswerId = null;
            }
        }

        /// <inheritdoc />
        public bool HasActivePlayerMessage() => _hasActivePlayerMessage;

        /// <inheritdoc />
        public void ClearAll()
        {
            if (!string.IsNullOrEmpty(_currentQuestionId))
            {
                SafeEventInvoker.Invoke(
                    OnMessageCompleted,
                    _currentQuestionId,
                    _logger,
                    "QAPresentationStrategy.OnMessageCompleted",
                    LogCategory.UI);
            }

            if (!string.IsNullOrEmpty(_currentAnswerId))
            {
                SafeEventInvoker.Invoke(
                    OnMessageCompleted,
                    _currentAnswerId,
                    _logger,
                    "QAPresentationStrategy.OnMessageCompleted",
                    LogCategory.UI);
            }

            _currentQuestionId = null;
            _currentAnswerId = null;
            _hasActivePlayerMessage = false;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _currentQuestionId = null;
            _currentAnswerId = null;
            _hasActivePlayerMessage = false;
        }
    }
}
