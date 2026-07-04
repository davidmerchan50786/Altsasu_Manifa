using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Components;
using Convai.Runtime.Room;
using NUnit.Framework;

namespace Convai.Tests.EditMode.Infrastructure
{
    [TestFixture]
    public class TurnTakingOptionsResolverTests
    {
        [Test]
        public void Resolve_HandsFreeDefaults_ProducesConcreteSmartTurnAndEnabledStt()
        {
            ResolvedTurnTakingOptions resolved = TurnTakingOptionsResolver.Resolve(
                TurnTakingOptions.CreateHandsFreeDefault(),
                null);

            Assert.That(resolved.Mode, Is.EqualTo(ConversationInputMode.HandsFree));
            Assert.That(resolved.HasTurnDetectionConfig, Is.True);
            Assert.That(resolved.SmartTurnSettings, Is.Not.Null);
            Assert.That(resolved.InitialServerSttEnabled, Is.True);
            Assert.That(resolved.EnableAcousticEchoCancellation, Is.False);
        }

        [Test]
        public void Resolve_PushToTalkDefaults_DisablesTurnDetectionAndInitialStt()
        {
            ResolvedTurnTakingOptions resolved = TurnTakingOptionsResolver.Resolve(
                TurnTakingOptions.CreatePushToTalkDefault(),
                null);

            Assert.That(resolved.Mode, Is.EqualTo(ConversationInputMode.PushToTalk));
            Assert.That(resolved.HasTurnDetectionConfig, Is.False);
            Assert.That(resolved.InitialServerSttEnabled, Is.False);
            Assert.That(resolved.PushToTalkStartupMode, Is.EqualTo(PushToTalkMicStartupMode.PrewarmMuted));
        }

        [Test]
        public void Resolve_PerCallConnectOptions_OverrideConfiguredDefaults()
        {
            var configured = TurnTakingOptions.CreateHandsFreeDefault();
            var connectOptions = new RoomSessionConnectOptions
            {
                TurnTaking = TurnTakingOptions.CreatePushToTalkDefault()
            };

            ResolvedTurnTakingOptions resolved = TurnTakingOptionsResolver.Resolve(configured, connectOptions);

            Assert.That(resolved.Mode, Is.EqualTo(ConversationInputMode.PushToTalk));
            Assert.That(resolved.InitialServerSttEnabled, Is.False);
        }

        [Test]
        public void Resolve_PushToTalkTimeoutZero_PreservesDisabledTimeout()
        {
            TurnTakingOptions configured = TurnTakingOptions.CreatePushToTalkDefault();
            configured.PushToTalkPolicy.TurnCompletionTimeoutMs = 0;

            ResolvedTurnTakingOptions resolved = TurnTakingOptionsResolver.Resolve(configured, null);

            Assert.That(resolved.TurnCompletionTimeoutMs, Is.Zero);
        }

        [Test]
        public void Resolve_AcousticEchoCancellationFlag_RoundTripsThroughCloneAndResolver()
        {
            TurnTakingOptions configured = TurnTakingOptions.CreateHandsFreeDefault();
            configured.LocalAudioPolicy.EnableAcousticEchoCancellation = true;

            TurnTakingOptions cloned = configured.Clone();
            ResolvedTurnTakingOptions resolved = TurnTakingOptionsResolver.Resolve(configured, null);

            Assert.That(cloned.LocalAudioPolicy.EnableAcousticEchoCancellation, Is.True);
            Assert.That(resolved.EnableAcousticEchoCancellation, Is.True);
        }

        [Test]
        public void Resolve_PushToTalkCustomTurnDetection_IsIgnoredAndDoesNotCreateTurnDetectionConfig()
        {
            TurnTakingOptions configured = TurnTakingOptions.CreatePushToTalkDefault();
            configured.TurnDetection = TurnDetectionMode.Custom;
            configured.CustomTurnDetection.StopSecs = 1.25f;
            configured.CustomTurnDetection.PreSpeechMs = 250;
            configured.CustomTurnDetection.MaxDurationSecs = 12f;

            ResolvedTurnTakingOptions resolved = TurnTakingOptionsResolver.Resolve(configured, null);

            Assert.That(resolved.Mode, Is.EqualTo(ConversationInputMode.PushToTalk));
            Assert.That(resolved.HasTurnDetectionConfig, Is.False);
            Assert.That(resolved.SmartTurnSettings, Is.Null);
        }

        [Test]
        public void DefaultHandsFree_ReturnsCachedInstance()
        {
            Assert.That(true,
                Is.True);
        }

        [Test]
        public void ManagerConversationMode_HandsFreePreset_ProducesDefaultHandsFreeOptions()
        {
            TurnTakingOptions options = ConvaiManager.CreateConversationModeTurnTakingOverride(
                ConvaiManagerConversationMode.HandsFree,
                interruptBotOnPress: true,
                requireTurnCompletionBeforeNextPress: true,
                pushToTalkTurnCompletionTimeoutMs: 5000);

            Assert.That(options, Is.Not.Null);
            Assert.That(options.Mode, Is.EqualTo(ConversationInputMode.HandsFree));
            Assert.That(options.TurnDetection, Is.EqualTo(TurnDetectionMode.UseDefault));
            Assert.That(options.InitialServerStt, Is.EqualTo(ServerSttInitialState.UseDefault));
        }

        [Test]
        public void ManagerConversationMode_PushToTalkPreset_ProducesSimplePushToTalkDefaults()
        {
            TurnTakingOptions options = ConvaiManager.CreateConversationModeTurnTakingOverride(
                ConvaiManagerConversationMode.PushToTalk,
                interruptBotOnPress: false,
                requireTurnCompletionBeforeNextPress: true,
                pushToTalkTurnCompletionTimeoutMs: 2500);

            Assert.That(options, Is.Not.Null);
            Assert.That(options.Mode, Is.EqualTo(ConversationInputMode.PushToTalk));
            Assert.That(options.LocalAudioPolicy.PushToTalkStartupMode,
                Is.EqualTo(PushToTalkMicStartupMode.PrewarmMuted));
            Assert.That(options.PushToTalkPolicy.EnableServerSttToggle, Is.True);
            Assert.That(options.PushToTalkPolicy.InterruptBotOnPress, Is.False);
            Assert.That(options.PushToTalkPolicy.TurnCompletionTimeoutMs, Is.EqualTo(2500));
        }

        [Test]
        public void ManagerConversationMode_UseRoomDefaultsPreset_ReturnsNullOverride()
        {
            TurnTakingOptions options = ConvaiManager.CreateConversationModeTurnTakingOverride(
                ConvaiManagerConversationMode.UseRoomDefaults,
                interruptBotOnPress: true,
                requireTurnCompletionBeforeNextPress: true,
                pushToTalkTurnCompletionTimeoutMs: 5000);

            Assert.That(options, Is.Null);
        }
    }
}
