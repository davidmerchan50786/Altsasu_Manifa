using System;
using System.Collections.Generic;
using UnityEngine;

namespace Convai.Runtime.Room
{
    public enum ConversationInputMode
    {
        HandsFree = 0,
        PushToTalk = 1
    }

    public enum TurnDetectionMode
    {
        UseDefault = 0,
        Disabled = 1,
        Custom = 2
    }

    public enum ServerSttInitialState
    {
        UseDefault = 0,
        Enabled = 1,
        Disabled = 2
    }

    public enum PushToTalkMicStartupMode
    {
        PrewarmMuted = 0,
        OpenOnFirstPress = 1
    }

    [Serializable]
    public sealed class SmartTurnSettings
    {
        public const float DefaultStopSecs = 3f;
        public const int DefaultPreSpeechMs = 0;
        public const float DefaultMaxDurationSecs = 8f;

        [field: SerializeField]
        [field: Tooltip("How long the player needs to stay silent before the SDK treats the turn as finished.")]
        public float StopSecs { get; set; } = DefaultStopSecs;

        [field: SerializeField]
        [field: Tooltip("How much audio just before speech starts should be kept, in milliseconds.")]
        public int PreSpeechMs { get; set; } = DefaultPreSpeechMs;

        [field: SerializeField]
        [field: Tooltip("The maximum length of a single user turn before the SDK forces it to end.")]
        public float MaxDurationSecs { get; set; } = DefaultMaxDurationSecs;

        public SmartTurnSettings Clone() =>
            new() { StopSecs = StopSecs, PreSpeechMs = PreSpeechMs, MaxDurationSecs = MaxDurationSecs };

        public static SmartTurnSettings CreateDefault() => new();
    }

    [Serializable]
    public sealed class LocalAudioPolicy
    {
        [field: SerializeField]
        [field: Tooltip("If enabled, the local microphone begins muted when push-to-talk mode is active.")]
        public bool StartMutedInPushToTalk { get; set; } = true;

        [field: SerializeField]
        [field:
            Tooltip(
                "Opt in to acoustic echo cancellation for hands-free speakerphone use. Primarily intended for Android and iOS device speakers.")]
        public bool EnableAcousticEchoCancellation { get; set; }

        [field: SerializeField]
        [field:
            Tooltip(
                "Choose whether the microphone is prepared in advance or opened only when the player presses the push-to-talk key.")]
        public PushToTalkMicStartupMode PushToTalkStartupMode { get; set; } = PushToTalkMicStartupMode.PrewarmMuted;

        public LocalAudioPolicy Clone() =>
            new()
            {
                StartMutedInPushToTalk = StartMutedInPushToTalk,
                EnableAcousticEchoCancellation = EnableAcousticEchoCancellation,
                PushToTalkStartupMode = PushToTalkStartupMode
            };

        public static LocalAudioPolicy CreateDefault() => new();
    }

    [Serializable]
    public sealed class PushToTalkPolicy
    {
        public const int DefaultTurnCompletionTimeoutMs = 5000;

        [field: SerializeField]
        [field:
            Tooltip(
                "Mute and unmute backend speech-to-text during push-to-talk as an optimization for server cost and connection hygiene.")]
        public bool EnableServerSttToggle { get; set; } = true;

        [field: SerializeField]
        [field:
            Tooltip(
                "If enabled, pressing push-to-talk while the character is speaking interrupts the character so the player can talk immediately.")]
        public bool InterruptBotOnPress { get; set; } = true;

        [field: SerializeField]
        [field:
            Tooltip(
                "If enabled, the player must wait for the current character response to finish before starting another push-to-talk turn.")]
        public bool RequireTurnCompletionBeforeNextPress { get; set; } = true;

        [field: SerializeField]
        [field: Tooltip("Fallback timeout used to unlock push-to-talk if a final completion event never arrives.")]
        public int TurnCompletionTimeoutMs { get; set; } = DefaultTurnCompletionTimeoutMs;

        [field: SerializeField]
        [field:
            Tooltip(
                "If enabled, a character speech-stopped event can clear push-to-talk waiting state after speech has actually started.")]
        public bool AllowSpeechStoppedFallbackAfterSpeechStart { get; set; }

        public PushToTalkPolicy Clone() =>
            new()
            {
                EnableServerSttToggle = EnableServerSttToggle,
                InterruptBotOnPress = InterruptBotOnPress,
                RequireTurnCompletionBeforeNextPress = RequireTurnCompletionBeforeNextPress,
                TurnCompletionTimeoutMs = TurnCompletionTimeoutMs,
                AllowSpeechStoppedFallbackAfterSpeechStart = AllowSpeechStoppedFallbackAfterSpeechStart
            };

        public static PushToTalkPolicy CreateDefault() => new();
    }

    [Serializable]
    public sealed class TurnTakingOptions
    {
        [field: SerializeField]
        [field: Tooltip("Choose between hands-free conversation and push-to-talk behavior for this room.")]
        public ConversationInputMode Mode { get; set; } = ConversationInputMode.HandsFree;

        [field: SerializeField]
        [field:
            Tooltip(
                "Controls automatic end-of-turn detection in Hands Free mode. Push To Talk ends the turn when the player releases the push-to-talk control.")]
        public TurnDetectionMode TurnDetection { get; set; } = TurnDetectionMode.UseDefault;

        [field: SerializeField]
        [field: Tooltip("Fine-tune how the SDK decides that the player has finished speaking.")]
        public SmartTurnSettings CustomTurnDetection { get; set; } = SmartTurnSettings.CreateDefault();

        [field: SerializeField]
        [field:
            Tooltip(
                "Controls whether backend speech-to-text starts enabled when the session begins. SDK Default is enabled for Hands Free and disabled for Push To Talk.")]
        public ServerSttInitialState InitialServerStt { get; set; } = ServerSttInitialState.UseDefault;

        [field: SerializeField]
        [field: Tooltip("Controls local microphone behavior on this device, including push-to-talk startup and optional AEC for speakerphone use.")]
        public LocalAudioPolicy LocalAudioPolicy { get; set; } = LocalAudioPolicy.CreateDefault();

        [field: SerializeField]
        [field:
            Tooltip(
                "Controls how push-to-talk behaves when talking, releasing, waiting for responses, and handling fallback rules.")]
        public PushToTalkPolicy PushToTalkPolicy { get; set; } = PushToTalkPolicy.CreateDefault();

        public TurnTakingOptions Clone() =>
            new()
            {
                Mode = Mode,
                TurnDetection = TurnDetection,
                CustomTurnDetection = CustomTurnDetection?.Clone() ?? SmartTurnSettings.CreateDefault(),
                InitialServerStt = InitialServerStt,
                LocalAudioPolicy = LocalAudioPolicy?.Clone() ?? LocalAudioPolicy.CreateDefault(),
                PushToTalkPolicy = PushToTalkPolicy?.Clone() ?? PushToTalkPolicy.CreateDefault()
            };

        public static TurnTakingOptions CreateHandsFreeDefault() => new()
        {
            Mode = ConversationInputMode.HandsFree,
            TurnDetection = TurnDetectionMode.UseDefault,
            InitialServerStt = ServerSttInitialState.UseDefault,
            LocalAudioPolicy = LocalAudioPolicy.CreateDefault(),
            PushToTalkPolicy = PushToTalkPolicy.CreateDefault()
        };

        public static TurnTakingOptions CreatePushToTalkDefault() => new()
        {
            Mode = ConversationInputMode.PushToTalk,
            TurnDetection = TurnDetectionMode.UseDefault,
            InitialServerStt = ServerSttInitialState.UseDefault,
            LocalAudioPolicy = LocalAudioPolicy.CreateDefault(),
            PushToTalkPolicy = PushToTalkPolicy.CreateDefault()
        };
    }

    [Serializable]
    public sealed class RoomSessionConnectOptions
    {
        [field: SerializeField]
        public TurnTakingOptions TurnTaking { get; set; } = TurnTakingOptions.CreateHandsFreeDefault();

        [field: SerializeField] public string EndUserId { get; set; }

        public IReadOnlyDictionary<string, object> EndUserMetadata { get; set; }

        public RoomSessionConnectOptions Clone() =>
            new()
            {
                TurnTaking = TurnTaking?.Clone() ?? TurnTakingOptions.CreateHandsFreeDefault(),
                EndUserId = EndUserId,
                EndUserMetadata = EndUserMetadata == null
                    ? null
                    : new Dictionary<string, object>(EndUserMetadata)
            };
    }
}
