using System.Collections;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using Convai.Runtime.Room;
using Convai.Runtime.Components;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Convai.SampleCommon.UI.DynamicContext
{
    /// <summary>
    ///     Runtime tester for <see cref="IConvaiRoomConnectionService.UpdateDynamicContext" />.
    ///     Attach to a GameObject in a scene with an active Convai session to exercise append, replace, reset, and run_llm options.
    /// </summary>
    public class SampleDynamicContextUI : MonoBehaviour
    {

        [SerializeField]
        [Tooltip("Initial context text.")]
        private TextMeshProUGUI _initialContextText;

        [SerializeField]
        [Tooltip("Optional ConvaiCharacter source for initial dynamic info. If not set, first character in scene is used.")]
        private ConvaiCharacter _convaiCharacter;
        [SerializeField]
        [TextArea(2, 6)]
        [Tooltip("Fallback context text used when no InputField is assigned.")]
        private string _contextText = string.Empty;

        [SerializeField]
        [Tooltip("Fallback mode used when mode toggles are not assigned.")]
        private DynamicContextMode _mode = DynamicContextMode.Append;

        [SerializeField]
        [Tooltip("Fallback run_llm option used when run_llm toggles are not assigned.")]
        private RunLlmOption _runLlm = RunLlmOption.Auto;

        [Header("UI Fields")]
        [SerializeField]
        [Tooltip("Input field used to edit context text.")]
        private TMP_InputField _contextInputField;

        [SerializeField]
        [Tooltip("Mode toggle: Append.")]
        private Toggle _modeAppendToggle;

        [SerializeField]
        [Tooltip("Mode toggle: Replace.")]
        private Toggle _modeReplaceToggle;

        [SerializeField]
        [Tooltip("Mode toggle: Reset.")]
        private Toggle _modeResetToggle;

        [SerializeField]
        [Tooltip("run_llm toggle: Auto.")]
        private Toggle _runLlmAutoToggle;

        [SerializeField]
        [Tooltip("run_llm toggle: True.")]
        private Toggle _runLlmTrueToggle;

        [SerializeField]
        [Tooltip("run_llm toggle: False.")]
        private Toggle _runLlmFalseToggle;

        [SerializeField]
        [Tooltip("Send button.")]
        private Button _sendButton;

        [SerializeField]
        [Tooltip("Reset button.")]
        private Button _resetButton;
        [SerializeField]
        [Tooltip("Microphone toggle.")]
        private Toggle _micToggle;

        private IConvaiRoomConnectionService _connectionService;
        private IConvaiRoomAudioService _audioService;

        IEnumerator Start()
        {
            const float resolveTimeoutSeconds = 30f;
            float deadline = Time.realtimeSinceStartup + resolveTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                ConvaiManager manager = ConvaiManager.ActiveManager;
                if (manager != null &&
                    manager.TryGetRoomConnectionService(out IConvaiRoomConnectionService connection) &&
                    manager.TryGetRoomAudioService(out IConvaiRoomAudioService audio))
                {
                    _connectionService = connection;
                    _audioService = audio;
                    break;
                }

                yield return null;
            }

            ApplyStateToUi();
            UpdateInitialContextLabelFromCharacter();
            if (_micToggle != null)
                _micToggle.isOn = _audioService?.IsMicMuted ?? false;

            if (_sendButton != null)
                _sendButton.onClick.AddListener(SendContent);
            if (_resetButton != null)
                _resetButton.onClick.AddListener(ResetContent);
            if (_micToggle != null)
                _micToggle.onValueChanged.AddListener(OnMicToggleValueChanged);
        }

        private void OnMicToggleValueChanged(bool isOn)
        {
            if (_audioService == null)
            {
                ConvaiLogger.Warning("[UpdateDynamicContextTester] No IConvaiRoomAudioService; connect to a room first.", LogCategory.SDK);
                return;
            }
            _audioService.SetMicMuted(isOn);
        }
        private void UpdateInitialContextLabelFromCharacter()
        {
            if (_initialContextText == null) return;

            ConvaiCharacter character = _convaiCharacter != null ? _convaiCharacter : FindFirstObjectByType<ConvaiCharacter>();
            if (character == null)
            {
                _initialContextText.text = "Initial context not set";
                return;
            }

            bool keepInContext = character.InitialDynamicInfoKeepInContext;
            string initialText = character.InitialDynamicInfoText;

            _initialContextText.text = keepInContext && !string.IsNullOrWhiteSpace(initialText)
                ? initialText
                : "Initial context not set";
        }

        /// <summary>Sends content based on current toggle selections (mode and runLlm) and contextText.</summary>
        public void SendContent()
        {
            UpdateStateFromUi();

            string modeStr = _mode switch
            {
                DynamicContextMode.Append => "append",
                DynamicContextMode.Replace => "replace",
                DynamicContextMode.Reset => "reset",
                _ => "append"
            };
            string runLlmStr = _runLlm switch
            {
                RunLlmOption.Auto => "auto",
                RunLlmOption.True => "true",
                RunLlmOption.False => "false",
                _ => "auto"
            };

            if (modeStr == "reset")
                SendResetInternal(runLlmStr);
            else
                Send(_contextText, modeStr, runLlmStr);
        }

        /// <summary>Resets UI fields to defaults and sends reset command to server.</summary>
        public void ResetContent()
        {
            // Reset internal state and UI fields to defaults.
            _contextText = string.Empty;
            _mode = DynamicContextMode.Append;
            _runLlm = RunLlmOption.Auto;
            ApplyStateToUi();

            // Send reset command to server
            SendResetInternal("auto");
        }

        private void UpdateStateFromUi()
        {
            if (_contextInputField != null)
                _contextText = _contextInputField.text;

            if (_modeAppendToggle != null || _modeReplaceToggle != null || _modeResetToggle != null)
            {
                if (_modeResetToggle != null && _modeResetToggle.isOn)
                    _mode = DynamicContextMode.Reset;
                else if (_modeReplaceToggle != null && _modeReplaceToggle.isOn)
                    _mode = DynamicContextMode.Replace;
                else
                    _mode = DynamicContextMode.Append;
            }

            if (_runLlmAutoToggle != null || _runLlmTrueToggle != null || _runLlmFalseToggle != null)
            {
                if (_runLlmTrueToggle != null && _runLlmTrueToggle.isOn)
                    _runLlm = RunLlmOption.True;
                else if (_runLlmFalseToggle != null && _runLlmFalseToggle.isOn)
                    _runLlm = RunLlmOption.False;
                else
                    _runLlm = RunLlmOption.Auto;
            }
        }

        private void ApplyStateToUi()
        {
            if (_contextInputField != null)
                _contextInputField.text = _contextText;

            if (_modeAppendToggle != null)
                _modeAppendToggle.isOn = _mode == DynamicContextMode.Append;
            if (_modeReplaceToggle != null)
                _modeReplaceToggle.isOn = _mode == DynamicContextMode.Replace;
            if (_modeResetToggle != null)
                _modeResetToggle.isOn = _mode == DynamicContextMode.Reset;

            if (_runLlmAutoToggle != null)
                _runLlmAutoToggle.isOn = _runLlm == RunLlmOption.Auto;
            if (_runLlmTrueToggle != null)
                _runLlmTrueToggle.isOn = _runLlm == RunLlmOption.True;
            if (_runLlmFalseToggle != null)
                _runLlmFalseToggle.isOn = _runLlm == RunLlmOption.False;
        }

        private void Send(string text, string modeStr, string runLlmStr)
        {
            if (_connectionService == null)
            {
                ConvaiLogger.Warning("[UpdateDynamicContextTester] No IConvaiRoomConnectionService; connect to a room first.", LogCategory.SDK);
                return;
            }

            bool sent = _connectionService.UpdateDynamicContext(text, modeStr, runLlmStr);
            if (sent)
                ConvaiLogger.Info($"[UpdateDynamicContextTester] Sent: mode={modeStr}, run_llm={runLlmStr}, text=\"{text}\"", LogCategory.SDK);
            else
                ConvaiLogger.Warning("[UpdateDynamicContextTester] Send failed (not connected?).", LogCategory.SDK);
        }

        private void SendResetInternal(string runLlmStr)
        {
            if (_connectionService == null)
            {
                ConvaiLogger.Warning("[UpdateDynamicContextTester] No IConvaiRoomConnectionService; connect to a room first.", LogCategory.SDK);
                return;
            }

            bool sent = _connectionService.UpdateDynamicContext(null, "reset", runLlmStr);
            if (sent)
                ConvaiLogger.Info($"[UpdateDynamicContextTester] Sent reset (run_llm={runLlmStr}).", LogCategory.SDK);
            else
                ConvaiLogger.Warning("[UpdateDynamicContextTester] Send failed (not connected?).", LogCategory.SDK);
        }

        /// <summary>Mode for dynamic context update.</summary>
        public enum DynamicContextMode
        {
            Append,
            Replace,
            Reset
        }

        /// <summary>Run LLM option after context update.</summary>
        public enum RunLlmOption
        {
            Auto,
            True,
            False
        }
    }
}
