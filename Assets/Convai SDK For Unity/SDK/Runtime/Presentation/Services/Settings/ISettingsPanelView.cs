using System;
using System.Collections.Generic;
using Convai.Shared.Types;

namespace Convai.Runtime.Presentation.Services.Settings
{
    /// <summary>
    ///     View contract for runtime settings panel UI.
    /// </summary>
    public interface ISettingsPanelView
    {
        public string PlayerDisplayNameInput { get; }
        public bool TranscriptEnabledInput { get; }
        public bool NotificationsEnabledInput { get; }
        public string SelectedMicrophoneDeviceId { get; }
        public ConvaiTranscriptMode SelectedTranscriptModeInput { get; }
        public event Action SaveRequested;
        public event Action CloseRequested;

        public void SetPlayerDisplayName(string value);
        public void SetTranscriptEnabled(bool value);
        public void SetNotificationsEnabled(bool value);
        public void SetMicrophoneOptions(IReadOnlyList<ConvaiMicrophoneDevice> devices, string selectedDeviceId);
        public void SetTranscriptModes(IReadOnlyList<ConvaiTranscriptMode> modes, ConvaiTranscriptMode selectedMode);
    }
}
