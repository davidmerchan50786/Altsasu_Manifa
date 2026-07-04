using Convai.Runtime.Room;

namespace Convai.Runtime.Adapters.Networking
{
    internal sealed class ResolvedTurnTakingOptions
    {
        public ResolvedTurnTakingOptions(
            ConversationInputMode mode,
            SmartTurnSettings smartTurnSettings,
            bool initialServerSttEnabled,
            bool startMutedInPushToTalk,
            bool enableAcousticEchoCancellation,
            PushToTalkMicStartupMode pushToTalkStartupMode,
            bool enableServerSttToggle,
            bool interruptBotOnPress,
            bool requireTurnCompletionBeforeNextPress,
            int turnCompletionTimeoutMs,
            bool allowSpeechStoppedFallbackAfterSpeechStart)
        {
            Mode = mode;
            SmartTurnSettings = smartTurnSettings?.Clone();
            InitialServerSttEnabled = initialServerSttEnabled;
            StartMutedInPushToTalk = startMutedInPushToTalk;
            EnableAcousticEchoCancellation = enableAcousticEchoCancellation;
            PushToTalkStartupMode = pushToTalkStartupMode;
            EnableServerSttToggle = enableServerSttToggle;
            InterruptBotOnPress = interruptBotOnPress;
            RequireTurnCompletionBeforeNextPress = requireTurnCompletionBeforeNextPress;
            TurnCompletionTimeoutMs = turnCompletionTimeoutMs;
            AllowSpeechStoppedFallbackAfterSpeechStart = allowSpeechStoppedFallbackAfterSpeechStart;
        }

        public ConversationInputMode Mode { get; }
        public SmartTurnSettings SmartTurnSettings { get; }
        public bool InitialServerSttEnabled { get; }
        public bool StartMutedInPushToTalk { get; }
        public bool EnableAcousticEchoCancellation { get; }
        public PushToTalkMicStartupMode PushToTalkStartupMode { get; }
        public bool EnableServerSttToggle { get; }
        public bool InterruptBotOnPress { get; }
        public bool RequireTurnCompletionBeforeNextPress { get; }
        public int TurnCompletionTimeoutMs { get; }
        public bool AllowSpeechStoppedFallbackAfterSpeechStart { get; }

        public bool HasTurnDetectionConfig => SmartTurnSettings != null;

        public bool ShouldAutoStartMicrophoneAfterConnect =>
            Mode == ConversationInputMode.HandsFree || PushToTalkStartupMode == PushToTalkMicStartupMode.PrewarmMuted;

        public bool ShouldStartMutedAfterAutoStart =>
            Mode == ConversationInputMode.PushToTalk &&
            PushToTalkStartupMode == PushToTalkMicStartupMode.PrewarmMuted &&
            StartMutedInPushToTalk;

        public static ResolvedTurnTakingOptions DefaultHandsFree { get; } =
            TurnTakingOptionsResolver.Resolve(null, null);
    }

    internal static class TurnTakingOptionsResolver
    {
        internal static ResolvedTurnTakingOptions Resolve(
            TurnTakingOptions configuredOptions,
            RoomSessionConnectOptions connectOptions)
        {
            TurnTakingOptions source = connectOptions?.TurnTaking?.Clone() ??
                                       configuredOptions?.Clone() ??
                                       TurnTakingOptions.CreateHandsFreeDefault();

            source.CustomTurnDetection ??= SmartTurnSettings.CreateDefault();
            source.LocalAudioPolicy ??= LocalAudioPolicy.CreateDefault();
            source.PushToTalkPolicy ??= PushToTalkPolicy.CreateDefault();

            SmartTurnSettings smartTurnSettings = ResolveTurnDetection(source);
            bool initialServerSttEnabled = ResolveInitialServerStt(source);
            PushToTalkPolicy pushToTalkPolicy = source.PushToTalkPolicy;
            LocalAudioPolicy localAudioPolicy = source.LocalAudioPolicy;
            int timeoutMs = pushToTalkPolicy.TurnCompletionTimeoutMs;

            return new ResolvedTurnTakingOptions(
                source.Mode,
                smartTurnSettings,
                initialServerSttEnabled,
                localAudioPolicy.StartMutedInPushToTalk,
                localAudioPolicy.EnableAcousticEchoCancellation,
                localAudioPolicy.PushToTalkStartupMode,
                pushToTalkPolicy.EnableServerSttToggle,
                pushToTalkPolicy.InterruptBotOnPress,
                pushToTalkPolicy.RequireTurnCompletionBeforeNextPress,
                timeoutMs,
                pushToTalkPolicy.AllowSpeechStoppedFallbackAfterSpeechStart);
        }

        private static SmartTurnSettings ResolveTurnDetection(TurnTakingOptions source)
        {
            if (source.Mode == ConversationInputMode.PushToTalk)
                return null;

            switch (source.TurnDetection)
            {
                case TurnDetectionMode.Custom:
                    return source.CustomTurnDetection?.Clone() ?? SmartTurnSettings.CreateDefault();
                case TurnDetectionMode.Disabled:
                    return null;
                case TurnDetectionMode.UseDefault:
                default:
                    return source.Mode == ConversationInputMode.HandsFree
                        ? SmartTurnSettings.CreateDefault()
                        : null;
            }
        }

        private static bool ResolveInitialServerStt(TurnTakingOptions source)
        {
            switch (source.InitialServerStt)
            {
                case ServerSttInitialState.Enabled:
                    return true;
                case ServerSttInitialState.Disabled:
                    return false;
                case ServerSttInitialState.UseDefault:
                default:
                    return source.Mode == ConversationInputMode.HandsFree;
            }
        }
    }
}
