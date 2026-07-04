using Convai.Domain.Logging;
using Convai.Domain.Models;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using Convai.Runtime.Presentation.Presenters;
using Convai.Runtime.Presentation.Services;
using Convai.Runtime.Presentation.Services.Utilities;
using TMPro;
using UnityEngine;

namespace Convai.Sample.UI.Transcript
{
    /// <summary>
    ///     Sample subtitle-style transcript UI that displays centered text with auto-hide.
    ///     This is a reference implementation showing how to implement ITranscriptUI for subtitle display.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Architecture:</b> This is a Samples layer reference implementation.
    ///         Implements ITranscriptUI as a "dumb" view - all streaming logic is handled
    ///         by <see cref="Convai.Runtime.Presentation.Strategies.SubtitlePresentationStrategy" />.
    ///     </para>
    ///     <para>
    ///         <b>Mode Activation:</b> Identifier is "Subtitle" - TranscriptUIController activates this UI
    ///         when TranscriptUIMode.Subtitle is selected.
    ///     </para>
    /// </remarks>
    public class SubtitleTranscriptUI : MonoBehaviour, ITranscriptUI
    {
        [Header("UI References")] [SerializeField]
        private TMP_Text subtitleText;

        [SerializeField] private TMP_Text speakerLabel;
        [SerializeField] private GameObject subtitleContainer;

        [Header("Fade Settings")] [SerializeField]
        private CanvasFader canvasFader;

        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private float fadeDuration = 0.3f;

        [Header("Auto-Hide Settings")] [SerializeField]
        private float autoHideDelay = 3.0f;

        private string _currentMessageId;
        private float _hideTimer;
        private bool _isActive;
        private bool _isInjected;

        private void Awake()
        {
            ConvaiManager.ActiveManager?.RegisterTranscriptUI(this);
            if (canvasFader == null)
                canvasFader = GetComponentInChildren<CanvasFader>();
            if (canvasGroup == null)
                canvasGroup = GetComponentInChildren<CanvasGroup>();

            if (subtitleContainer != null)
                subtitleContainer.SetActive(false);

            _isInjected = true;
        }

        private void Start()
        {
            if (!_isInjected)
            {
                ConvaiLogger.Warning(
                    "[SubtitleTranscriptUI] Dependencies not injected - ensure ConvaiManager finished runtime setup for this scene",
                    LogCategory.UI);
            }
        }

        private void Update()
        {
            if (_hideTimer > 0)
            {
                _hideTimer -= Time.deltaTime;
                if (_hideTimer <= 0) HideSubtitle();
            }
        }

        private void OnDestroy() => ConvaiManager.ActiveManager?.UnregisterTranscriptUI(this);

        /// <summary>
        ///     Gets the unique identifier for this Subtitle UI instance.
        ///     Must match TranscriptUIMode.Subtitle for mode-based activation.
        /// </summary>
        public string Identifier => "Subtitle";

        /// <summary>
        ///     Gets whether this UI is currently active and visible.
        /// </summary>
        public bool IsActive => _isActive && gameObject.activeInHierarchy;

        private void HideSubtitle()
        {
            if (canvasFader != null && canvasGroup != null)
                canvasFader.StartFadeOut(canvasGroup, fadeDuration);
            else if (subtitleContainer != null) subtitleContainer.SetActive(false);
        }

        #region ITranscriptUI Implementation

        /// <summary>
        ///     Displays or updates a transcript message as a subtitle.
        ///     Receives view models from SubtitlePresentationStrategy.
        /// </summary>
        public void DisplayMessage(TranscriptViewModel viewModel)
        {
            _currentMessageId = viewModel.MessageId;
            _hideTimer = 0;

            if (subtitleContainer != null)
                subtitleContainer.SetActive(true);

            if (speakerLabel != null)
            {
                speakerLabel.text = viewModel.DisplayName;
                speakerLabel.color = viewModel.Speaker == TranscriptSpeaker.Character
                    ? Color.cyan
                    : Color.green;
            }

            if (subtitleText != null) subtitleText.text = viewModel.Text;

            if (canvasFader != null && canvasGroup != null) canvasFader.StartFadeIn(canvasGroup, fadeDuration);

            if (viewModel.Lifecycle == TranscriptLifecycle.Completed) _hideTimer = autoHideDelay;
        }

        /// <summary>
        ///     Marks a message as completed. Triggers auto-hide timer for subtitles.
        /// </summary>
        public void CompleteMessage(string messageId)
        {
            if (_currentMessageId == messageId) _hideTimer = autoHideDelay;
        }

        /// <summary>
        ///     Clears all displayed subtitles.
        /// </summary>
        public void ClearAll()
        {
            HideSubtitle();
            _currentMessageId = null;
            ConvaiLogger.Info("[SubtitleTranscriptUI] Subtitles cleared", LogCategory.UI);
        }

        /// <summary>
        ///     Sets the active/visible state of this UI.
        /// </summary>
        public void SetActive(bool active)
        {
            _isActive = active;
            gameObject.SetActive(active);
        }

        /// <summary>
        ///     Completes all active player messages.
        ///     For subtitles, this is a no-op since subtitles auto-hide after display.
        /// </summary>
        public void CompletePlayerTurn()
        {
        }

        #endregion
    }
}
