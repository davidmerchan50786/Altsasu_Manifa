using System;
using System.Collections.Generic;
using System.Linq;
using Convai.Application.Services.Transcript;
using Convai.Domain.Logging;
using Convai.Domain.Models;
using Convai.Runtime.Logging;
using Convai.Runtime.Presentation.Presenters;
using Convai.Runtime.Presentation.Strategies;

namespace Convai.Runtime.Presentation.Services
{
    /// <summary>
    ///     Controller for managing transcript UI instances with mode-specific strategies.
    ///     Transcript state is projected from <see cref="IRoomTranscriptEngine" /> snapshots.
    /// </summary>
    public class TranscriptUIController : IDisposable
    {
        private readonly ITranscriptFilter _filter;
        private readonly ITranscriptFormatter _formatter;
        private readonly ILogger _logger;
        private readonly Dictionary<string, ITranscriptUI> _registeredUIs = new();
        private readonly Dictionary<TranscriptUIMode, ITranscriptPresentationStrategy> _strategies;
        private readonly IRoomTranscriptEngine _transcriptEngine;
        private readonly List<ITranscriptListener> _transcriptListeners = new();
        private TranscriptUIMode _currentMode = TranscriptUIMode.Chat;
        private bool _disposed;

        internal TranscriptUIController(
            IRoomTranscriptEngine transcriptEngine,
            ITranscriptFormatter formatter = null,
            ITranscriptFilter filter = null,
            ILogger logger = null)
        {
            _transcriptEngine = transcriptEngine ?? throw new ArgumentNullException(nameof(transcriptEngine));
            _formatter = formatter ?? new DefaultTranscriptFormatter();
            _filter = filter ?? new DefaultTranscriptFilter();
            _logger = logger;

            _strategies = new Dictionary<TranscriptUIMode, ITranscriptPresentationStrategy>
            {
                { TranscriptUIMode.Chat, new ChatPresentationStrategy(logger) },
                { TranscriptUIMode.Subtitle, new SubtitlePresentationStrategy(logger) },
                { TranscriptUIMode.QuestionAnswer, new QAPresentationStrategy(logger) }
            };

            ActiveStrategy = _strategies[_currentMode];
            SubscribeToStrategy(ActiveStrategy);
            _transcriptEngine.Changed += OnTranscriptBatchChanged;

            IsEnabled = true;
            ConvaiLogger.Debug("[TranscriptUIController] Initialized from RoomTranscriptEngine", LogCategory.UI);
        }

        public ITranscriptUI ActiveUI { get; private set; }

        public ITranscriptPresentationStrategy ActiveStrategy { get; private set; }

        public bool IsEnabled { get; private set; } = true;

        public TranscriptUIMode CurrentMode
        {
            get => _currentMode;
            set
            {
                if (_currentMode == value) return;

                TranscriptUIMode previousMode = _currentMode;
                _currentMode = value;
                UpdateActiveStrategy();
                UpdateActiveUI();
                ConvaiLogger.Debug($"[TranscriptUIController] Mode changed from {previousMode} to {_currentMode}",
                    LogCategory.UI);
            }
        }

        public int TranscriptListenerCount => _transcriptListeners.Count;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _transcriptEngine.Changed -= OnTranscriptBatchChanged;

            if (ActiveStrategy != null) UnsubscribeFromStrategy(ActiveStrategy);

            foreach (ITranscriptPresentationStrategy strategy in _strategies.Values) strategy.Dispose();
            _strategies.Clear();

            _transcriptListeners.Clear();
            _registeredUIs.Clear();
            ActiveUI = null;

            ConvaiLogger.Debug("[TranscriptUIController] Disposed", LogCategory.UI);
        }

        public event Action<ITranscriptUI> ActiveUIChanged;

        public void Register(ITranscriptUI ui)
        {
            if (ui == null) throw new ArgumentNullException(nameof(ui));

            if (_registeredUIs.ContainsKey(ui.Identifier))
            {
                ConvaiLogger.Warning(
                    $"[TranscriptUIController] UI with identifier '{ui.Identifier}' already registered. Replacing.",
                    LogCategory.UI);
                Unregister(ui.Identifier);
            }

            _registeredUIs[ui.Identifier] = ui;
            UpdateActiveUI();
        }

        public void Unregister(string identifier)
        {
            if (!_registeredUIs.TryGetValue(identifier, out ITranscriptUI ui)) return;

            _registeredUIs.Remove(identifier);

            if (ActiveUI == ui)
            {
                ActiveUI = null;
                UpdateActiveUI();
            }
        }

        public bool TryGetUI(string identifier, out ITranscriptUI ui) => _registeredUIs.TryGetValue(identifier, out ui);

        public void ClearAll()
        {
            ActiveStrategy?.ClearAll();

            foreach (ITranscriptUI ui in _registeredUIs.Values) ui.ClearAll();
        }

        public void SetEnabled(bool enabled)
        {
            if (IsEnabled == enabled) return;

            IsEnabled = enabled;

            if (!enabled)
            {
                ActiveStrategy?.ClearAll();

                foreach (ITranscriptUI ui in _registeredUIs.Values)
                {
                    try
                    {
                        ui.ClearAll();
                        if (ui.IsActive) ui.SetActive(false);
                    }
                    catch (Exception ex)
                    {
                        ConvaiLogger.Error(
                            $"[TranscriptUIController] Error disabling UI '{ui.Identifier}': {ex.Message}",
                            LogCategory.UI);
                    }
                }

                string previousId = ActiveUI?.Identifier ?? "none";
                ActiveUI = null;
                ConvaiLogger.Debug(
                    $"[TranscriptUIController] Transcript routing disabled; active UI changed from '{previousId}' to 'none'",
                    LogCategory.UI);
                ActiveUIChanged?.Invoke(ActiveUI);
                return;
            }

            ConvaiLogger.Debug("[TranscriptUIController] Transcript routing enabled", LogCategory.UI);
            UpdateActiveUI();
        }

        public void RegisterListener(ITranscriptListener listener)
        {
            if (listener == null) throw new ArgumentNullException(nameof(listener));

            if (_transcriptListeners.Contains(listener)) return;

            _transcriptListeners.Add(listener);
            ConvaiLogger.Debug(
                $"[TranscriptUIController] Registered ITranscriptListener: {listener.GetType().Name}",
                LogCategory.UI);
        }

        public void UnregisterListener(ITranscriptListener listener)
        {
            if (listener == null || !_transcriptListeners.Remove(listener)) return;

            ConvaiLogger.Debug(
                $"[TranscriptUIController] Unregistered ITranscriptListener: {listener.GetType().Name}",
                LogCategory.UI);
        }

        private void UpdateActiveStrategy()
        {
            if (!_strategies.TryGetValue(_currentMode, out ITranscriptPresentationStrategy newStrategy)) return;
            if (newStrategy == ActiveStrategy) return;

            if (ActiveStrategy != null)
            {
                ActiveStrategy.ClearAll();
                UnsubscribeFromStrategy(ActiveStrategy);
            }

            ActiveStrategy = newStrategy;
            SubscribeToStrategy(ActiveStrategy);

            ConvaiLogger.Debug($"[TranscriptUIController] Active strategy changed to {_currentMode}",
                LogCategory.UI);
        }

        private void SubscribeToStrategy(ITranscriptPresentationStrategy strategy)
        {
            strategy.OnMessageUpdated += OnStrategyMessageUpdated;
            strategy.OnMessageCompleted += OnStrategyMessageCompleted;
        }

        private void UnsubscribeFromStrategy(ITranscriptPresentationStrategy strategy)
        {
            strategy.OnMessageUpdated -= OnStrategyMessageUpdated;
            strategy.OnMessageCompleted -= OnStrategyMessageCompleted;
        }

        private void OnStrategyMessageUpdated(TranscriptViewModel viewModel)
        {
            if (_disposed || !IsEnabled) return;

            foreach (ITranscriptUI ui in _registeredUIs.Values)
            {
                if (!ui.IsActive) continue;

                try
                {
                    ui.DisplayMessage(viewModel);
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error(
                        $"[TranscriptUIController] Error routing to UI '{ui.Identifier}': {ex.Message}",
                        LogCategory.UI);
                }
            }
        }

        private void OnStrategyMessageCompleted(string messageId)
        {
            if (_disposed || !IsEnabled) return;

            foreach (ITranscriptUI ui in _registeredUIs.Values)
            {
                if (!ui.IsActive) continue;

                try
                {
                    ui.CompleteMessage(messageId);
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error(
                        $"[TranscriptUIController] Error completing message for UI '{ui.Identifier}': {ex.Message}",
                        LogCategory.UI);
                }
            }
        }

        private void OnTranscriptBatchChanged(TranscriptUpdateBatch batch)
        {
            if (_disposed || !IsEnabled) return;

            foreach (string removedTurnId in batch.RemovedTurnIds)
                OnStrategyMessageCompleted(removedTurnId);

            foreach (TranscriptTurnSnapshot turn in batch.ChangedTurns.OrderBy(t => t.RoomSequence))
            {
                TranscriptViewModel viewModel = CreateViewModel(turn);
                if (viewModel.IsEmpty) continue;

                RouteToListeners(viewModel);
                ActiveStrategy?.HandleMessage(viewModel);
            }
        }

        private TranscriptViewModel CreateViewModel(TranscriptTurnSnapshot turn)
        {
            TranscriptSpeaker speaker = turn.Participant.Kind == TranscriptParticipantKind.Player
                ? TranscriptSpeaker.Player
                : TranscriptSpeaker.Character;

            TranscriptMessage message = speaker == TranscriptSpeaker.Player
                ? TranscriptMessage.ForPlayer(
                    turn.DisplayText,
                    turn.Lifecycle != TranscriptLifecycle.Streaming,
                    turn.Participant.PlayerOrCharacterId,
                    turn.Participant.DisplayName,
                    turn.Participant.ParticipantId)
                : TranscriptMessage.Create(
                    turn.Participant.PlayerOrCharacterId,
                    turn.Participant.DisplayName,
                    turn.DisplayText,
                    turn.Lifecycle == TranscriptLifecycle.Completed,
                    participantId: turn.Participant.ParticipantId,
                    speakerType: SpeakerType.Character);

            if (!_filter.ShouldDisplay(message))
                return default;

            string formattedText = speaker == TranscriptSpeaker.Player
                ? _formatter.FormatPlayerMessage(message)
                : _formatter.FormatCharacterMessage(message);

            return new TranscriptViewModel(
                speaker,
                message,
                formattedText,
                turn.MessageId,
                turn.Lifecycle,
                turn);
        }

        private void RouteToListeners(TranscriptViewModel viewModel)
        {
            foreach (ITranscriptListener listener in _transcriptListeners)
            {
                try
                {
                    if (!ShouldReceive(listener, viewModel)) continue;

                    if (viewModel.Speaker == TranscriptSpeaker.Character)
                    {
                        listener.OnCharacterTranscript(
                            viewModel.PlayerOrCharacterId,
                            viewModel.DisplayName,
                            viewModel.Text,
                            viewModel.IsFinal);
                        continue;
                    }

                    listener.OnPlayerTranscript(viewModel.Text, viewModel.IsFinal);

                    if (listener is IMultiUserTranscriptListener multiUserListener && viewModel.HasSpeakerInfo)
                    {
                        multiUserListener.OnPlayerTranscriptWithSpeaker(
                            viewModel.PlayerOrCharacterId,
                            viewModel.DisplayName,
                            viewModel.ParticipantId,
                            viewModel.Text,
                            viewModel.IsFinal);
                    }
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error(
                        $"[TranscriptUIController] Error routing to ITranscriptListener '{listener.GetType().Name}': {ex.Message}",
                        LogCategory.UI);
                }
            }
        }

        private static bool ShouldReceive(ITranscriptListener listener, TranscriptViewModel viewModel)
        {
            if (viewModel.Speaker == TranscriptSpeaker.Player) return true;

            string filterCharacterId = listener.FilterCharacterId;
            return filterCharacterId == null || filterCharacterId == viewModel.PlayerOrCharacterId;
        }

        private void UpdateActiveUI()
        {
            if (!IsEnabled) return;

            ITranscriptUI newActive = null;

            foreach (ITranscriptUI ui in _registeredUIs.Values)
            {
                bool shouldBeActive = MatchesMode(ui.Identifier, _currentMode);

                if (shouldBeActive != ui.IsActive)
                {
                    try
                    {
                        ui.SetActive(shouldBeActive);
                        ConvaiLogger.Debug(
                            $"[TranscriptUIController] Set UI '{ui.Identifier}' active={shouldBeActive} (mode={_currentMode})",
                            LogCategory.UI);
                    }
                    catch (Exception ex)
                    {
                        ConvaiLogger.Error(
                            $"[TranscriptUIController] Error setting active state for UI '{ui.Identifier}': {ex.Message}",
                            LogCategory.UI);
                    }
                }

                if (shouldBeActive && ui.IsActive) newActive = ui;
            }

            if (newActive == ActiveUI) return;

            string previousId = ActiveUI?.Identifier ?? "none";
            string newId = newActive?.Identifier ?? "none";
            ActiveUI = newActive;
            ConvaiLogger.Debug($"[TranscriptUIController] Active UI changed from '{previousId}' to '{newId}'",
                LogCategory.UI);
            ActiveUIChanged?.Invoke(ActiveUI);
        }

        private static bool MatchesMode(string identifier, TranscriptUIMode mode)
        {
            if (string.IsNullOrEmpty(identifier))
                return false;

            return mode switch
            {
                TranscriptUIMode.Chat => identifier.Equals("Chat", StringComparison.OrdinalIgnoreCase) ||
                                         identifier.StartsWith("Chat", StringComparison.OrdinalIgnoreCase),
                TranscriptUIMode.Subtitle => identifier.Equals("Subtitle", StringComparison.OrdinalIgnoreCase) ||
                                             identifier.StartsWith("Subtitle", StringComparison.OrdinalIgnoreCase),
                TranscriptUIMode.QuestionAnswer => identifier.Equals("QuestionAnswer",
                                                       StringComparison.OrdinalIgnoreCase) ||
                                                   identifier.StartsWith("QuestionAnswer",
                                                       StringComparison.OrdinalIgnoreCase) ||
                                                   identifier.StartsWith("QA", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        /// <summary>
        ///     Sets the current transcript mode by integer index for settings-panel compatibility.
        /// </summary>
        public void SetModeByIndex(int modeIndex)
        {
            CurrentMode = modeIndex switch
            {
                1 => TranscriptUIMode.Subtitle,
                2 => TranscriptUIMode.QuestionAnswer,
                _ => TranscriptUIMode.Chat
            };
        }
    }
}
