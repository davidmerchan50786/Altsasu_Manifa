using System;
using Convai.Domain.Logging;
using Convai.Domain.Models;
using Convai.Runtime.Presentation.Presenters;
using Convai.Runtime.Utilities;

namespace Convai.Runtime.Presentation.Strategies
{
    /// <summary>
    ///     Subtitle-style presentation strategy that shows streaming text with auto-clear.
    ///     Each message updates the current subtitle with no aggregation or history.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Behavior:</b>
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>Shows the latest transcript text as a subtitle overlay</description>
    ///             </item>
    ///             <item>
    ///                 <description>Interim and final messages both update the display</description>
    ///             </item>
    ///             <item>
    ///                 <description>Emits completion signal on final character messages for auto-hide</description>
    ///             </item>
    ///         </list>
    ///     </para>
    /// </remarks>
    public sealed class SubtitlePresentationStrategy : ITranscriptPresentationStrategy
    {
        private readonly ILogger _logger;
        private string _currentMessageId;
        private bool _disposed;
        private bool _hasActivePlayerMessage;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SubtitlePresentationStrategy" /> class.
        /// </summary>
        /// <param name="logger">Optional logger for diagnostics.</param>
        public SubtitlePresentationStrategy(ILogger logger = null)
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
                $"[SubtitlePresentationStrategy] HandleMessage: Speaker={viewModel.Speaker}, IsFinal={viewModel.IsFinal}, Text=\"{viewModel.Text}\"");

            if (viewModel.Speaker == TranscriptSpeaker.Player)
                _hasActivePlayerMessage = viewModel.Lifecycle != TranscriptLifecycle.Completed;

            _currentMessageId = viewModel.MessageId;

            SafeEventInvoker.Invoke(
                OnMessageUpdated,
                viewModel,
                _logger,
                "SubtitlePresentationStrategy.OnMessageUpdated",
                LogCategory.UI);

            if (viewModel.Lifecycle == TranscriptLifecycle.Completed)
            {
                if (viewModel.Speaker == TranscriptSpeaker.Player) _hasActivePlayerMessage = false;
                SafeEventInvoker.Invoke(
                    OnMessageCompleted,
                    viewModel.MessageId,
                    _logger,
                    "SubtitlePresentationStrategy.OnMessageCompleted",
                    LogCategory.UI);
            }
        }

        /// <inheritdoc />
        public void CompletePlayerTurn()
        {
            if (_disposed) return;

            _logger?.Debug("[SubtitlePresentationStrategy] CompletePlayerTurn - subtitles auto-hide, no action needed");

            if (_hasActivePlayerMessage && !string.IsNullOrEmpty(_currentMessageId))
            {
                SafeEventInvoker.Invoke(
                    OnMessageCompleted,
                    _currentMessageId,
                    _logger,
                    "SubtitlePresentationStrategy.OnMessageCompleted",
                    LogCategory.UI);
            }

            _currentMessageId = null;
            _hasActivePlayerMessage = false;
        }

        /// <inheritdoc />
        public void CompleteCharacterTurn(string characterId)
        {
            if (_disposed) return;

            _logger?.Debug(
                "[SubtitlePresentationStrategy] CompleteCharacterTurn - subtitles auto-hide, no action needed");
        }

        /// <inheritdoc />
        public bool HasActivePlayerMessage() => _hasActivePlayerMessage;

        /// <inheritdoc />
        public void ClearAll()
        {
            if (!string.IsNullOrEmpty(_currentMessageId))
            {
                SafeEventInvoker.Invoke(
                    OnMessageCompleted,
                    _currentMessageId,
                    _logger,
                    "SubtitlePresentationStrategy.OnMessageCompleted",
                    LogCategory.UI);
            }

            _currentMessageId = null;
            _hasActivePlayerMessage = false;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _currentMessageId = null;
            _hasActivePlayerMessage = false;
        }
    }
}
