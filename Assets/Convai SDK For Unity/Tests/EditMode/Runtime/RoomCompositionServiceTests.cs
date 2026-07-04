using System.Collections.Generic;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Core.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace Convai.Tests.EditMode.Runtime
{
    [TestFixture]
    public sealed class RoomCompositionServiceTests
    {
        [Test]
        public void ValidateAndComposeStartup_WhenNotInjected_DisablesManager()
        {
            var service = new RoomCompositionService();

            RoomCompositionStartupResult result = service.ValidateAndComposeStartup(
                new RoomCompositionContext(),
                new RoomCompositionState { IsInjected = false });

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.DisableManager);
            Assert.That(result.ErrorMessage, Does.Contain("dependencies were not injected"));
        }

        [Test]
        public void ValidateAndComposeStartup_WhenCredentialsMissing_ReturnsCredentialFailure()
        {
            var service = new RoomCompositionService();

            RoomCompositionStartupResult result = service.ValidateAndComposeStartup(
                new RoomCompositionContext
                {
                    CredentialProvider = new TestCredentialProvider(null, "https://core.convai.com")
                },
                new RoomCompositionState { IsInjected = true });

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(RoomStartupFailureReason.MissingRuntimeCredentials, result.FailureReason);
            Assert.AreEqual("CONFIG_API_KEY_MISSING", result.FailureErrorCode);
            Assert.AreEqual("Runtime credentials are not configured", result.FailureRecordMessage);
        }

        [Test]
        public void HandleOwnedAgentStateChanged_WhenStartupNotCompleted_DefersUntilStartup()
        {
            var service = new RoomCompositionService();

            RoomOwnershipChangeResult result = service.HandleOwnedAgentStateChanged(
                new RoomCompositionContext
                {
                    OwnershipProvider = new TestOwnershipProvider(
                        new RoomOwnershipSnapshot(
                            null,
                            new[] { new TestCharacterAgent("char-1") },
                            new ActiveConversationTarget(new TestCharacterAgent("char-1"))))
                },
                new RoomCompositionState { IsInjected = true, HasStarted = false });

            Assert.AreEqual(RoomOwnershipRebindOutcome.DeferredUntilStartup, result.Outcome);
            Assert.AreEqual("char-1", result.RequestedCharacterId);
        }

        private sealed class TestCredentialProvider : ICredentialProvider
        {
            private readonly string _apiKey;
            private readonly string _serverUrl;

            public TestCredentialProvider(string apiKey, string serverUrl)
            {
                _apiKey = apiKey;
                _serverUrl = serverUrl;
            }

            public bool HasValidCredentials => !string.IsNullOrEmpty(_apiKey);
            public string GetApiKey() => _apiKey;
            public string GetServerUrl() => _serverUrl;
            public void Refresh() { }
        }

        private sealed class TestOwnershipProvider : IRoomOwnershipProvider
        {
            private readonly RoomOwnershipSnapshot _snapshot;

            public TestOwnershipProvider(RoomOwnershipSnapshot snapshot)
            {
                _snapshot = snapshot;
            }

            public RoomOwnershipSnapshot CaptureOwnership() => _snapshot;
        }

        private sealed class TestCharacterAgent : IConvaiCharacterAgent
        {
            public TestCharacterAgent(string characterId, string characterName = "Test Character")
            {
                CharacterId = characterId;
                CharacterName = characterName;
            }

            public string CharacterId { get; }
            public string CharacterName { get; }
            public Color NameTagColor => Color.white;
            public bool EnableSessionResume => false;
            public string InitialDynamicInfoText => string.Empty;
            public bool InitialDynamicInfoKeepInContext => false;
            public void SendTrigger(string triggerName, string triggerMessage = null) { }
            public void SendDynamicInfo(string contextText) { }
            public void UpdateTemplateKeys(Dictionary<string, string> templateKeys) { }
        }
    }
}
