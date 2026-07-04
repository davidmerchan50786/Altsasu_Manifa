namespace Convai.Shared.Types
{
    /// <summary>
    ///     Runtime settings snapshot.
    /// </summary>
    public readonly struct ConvaiRuntimeSettingsSnapshot
    {
        /// <summary>Gets the player display name.</summary>
        public string PlayerDisplayName { get; }

        /// <summary>Gets whether transcript display is enabled.</summary>
        public bool TranscriptEnabled { get; }

        /// <summary>Gets whether notifications are enabled.</summary>
        public bool NotificationsEnabled { get; }

        /// <summary>Gets the preferred microphone device identifier.</summary>
        public string PreferredMicrophoneDeviceId { get; }

        /// <summary>Gets the transcript presentation mode.</summary>
        public ConvaiTranscriptMode TranscriptMode { get; }

        /// <summary>Creates a new snapshot with the specified values.</summary>
        public ConvaiRuntimeSettingsSnapshot(
            string playerDisplayName,
            bool transcriptEnabled,
            bool notificationsEnabled,
            string preferredMicrophoneDeviceId,
            ConvaiTranscriptMode transcriptMode)
        {
            PlayerDisplayName = playerDisplayName ?? string.Empty;
            TranscriptEnabled = transcriptEnabled;
            NotificationsEnabled = notificationsEnabled;
            PreferredMicrophoneDeviceId = preferredMicrophoneDeviceId;
            TranscriptMode = transcriptMode;
        }

        /// <summary>Returns a new snapshot with the specified overrides applied.</summary>
        public ConvaiRuntimeSettingsSnapshot With(
            string playerDisplayName = null,
            bool? transcriptEnabled = null,
            bool? notificationsEnabled = null,
            string preferredMicrophoneDeviceId = null,
            ConvaiTranscriptMode? transcriptMode = null)
        {
            return new ConvaiRuntimeSettingsSnapshot(
                playerDisplayName ?? PlayerDisplayName,
                transcriptEnabled ?? TranscriptEnabled,
                notificationsEnabled ?? NotificationsEnabled,
                preferredMicrophoneDeviceId ?? PreferredMicrophoneDeviceId,
                transcriptMode ?? TranscriptMode);
        }
    }
}
