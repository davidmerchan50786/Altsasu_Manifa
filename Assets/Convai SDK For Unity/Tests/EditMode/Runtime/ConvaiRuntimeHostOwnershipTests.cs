using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Components;
using Convai.Runtime.Core;
using Convai.Runtime.Core.Composition;
using Convai.Runtime.Core.Coordinators;
using Convai.Runtime.Core.Registry;
using NUnit.Framework;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;
using Object = UnityEngine.Object;

namespace Convai.Tests.EditMode.Runtime
{
    [TestFixture]
    public sealed class ConvaiRuntimeHostOwnershipTests
    {
        private readonly List<Object> _createdObjects = new();
        private ConvaiRuntime _runtime;
        private ConvaiRuntimeHost _host;

        [TearDown]
        public void TearDown()
        {
            _host?.Dispose();
            _host = null;

            if (_runtime != null)
            {
                _runtime.DisposeAsync().GetAwaiter().GetResult();
                _runtime = null;
            }

            for (int i = _createdObjects.Count - 1; i >= 0; i--)
            {
                if (_createdObjects[i] != null)
                    Object.DestroyImmediate(_createdObjects[i]);
            }

            _createdObjects.Clear();
        }

        [Test]
        public void RefreshOwnedAgentState_UsesExplicitConversationTarget_WhenNoExplicitCharacterListExists()
        {
            CreateHost();

            var playerObject = new GameObject("Player");
            var characterObject = new GameObject("Character");
            _createdObjects.Add(playerObject);
            _createdObjects.Add(characterObject);

            var player = playerObject.AddComponent<ConvaiPlayer>();
            var character = characterObject.AddComponent<ConvaiCharacter>();
            character.Configure("char-1", "Guide");

            _host.RefreshOwnedAgentState(
                sceneInstaller: null,
                explicitPlayer: player,
                explicitCharacters: new List<ConvaiCharacter>(),
                explicitConversationTarget: character,
                ownershipProvider: null);

            RoomOwnershipSnapshot snapshot = _host.CaptureOwnership();

            Assert.AreSame(player, snapshot.Player);
            Assert.AreEqual(1, snapshot.Characters.Count);
            Assert.AreSame(character, snapshot.Characters[0]);
            Assert.IsNotNull(snapshot.ConversationTarget);
            Assert.AreSame(character, snapshot.ConversationTarget.Character);
        }

        [Test]
        public void RefreshOwnedAgentState_PrefersSceneInstallerOwnedCharacters()
        {
            CreateHost();

            var installerObject = new GameObject("Installer");
            var playerObject = new GameObject("Player");
            var ownedCharacterObject = new GameObject("Owned Character");
            var extraCharacterObject = new GameObject("Extra Character");
            _createdObjects.Add(installerObject);
            _createdObjects.Add(playerObject);
            _createdObjects.Add(ownedCharacterObject);
            _createdObjects.Add(extraCharacterObject);

            var installer = installerObject.AddComponent<ConvaiSceneInstaller>();
            var player = playerObject.AddComponent<ConvaiPlayer>();
            var ownedCharacter = ownedCharacterObject.AddComponent<ConvaiCharacter>();
            var extraCharacter = extraCharacterObject.AddComponent<ConvaiCharacter>();
            ownedCharacter.Configure("owned-char", "Owned");
            extraCharacter.Configure("extra-char", "Extra");

            SetPrivateField(installer, "_players", new List<ConvaiPlayer> { player });
            SetPrivateField(installer, "_characters", new List<ConvaiCharacter> { ownedCharacter, extraCharacter });
            SetPrivateField(installer, "_ownedCharacters", new List<ConvaiCharacter> { ownedCharacter });

            _host.RefreshOwnedAgentState(
                sceneInstaller: installer,
                explicitPlayer: null,
                explicitCharacters: null,
                explicitConversationTarget: null,
                ownershipProvider: null);

            RoomOwnershipSnapshot snapshot = _host.CaptureOwnership();

            Assert.AreSame(player, snapshot.Player);
            Assert.AreEqual(1, snapshot.Characters.Count);
            Assert.AreSame(ownedCharacter, snapshot.Characters[0]);
            Assert.IsNotNull(snapshot.ConversationTarget);
            Assert.AreSame(ownedCharacter, snapshot.ConversationTarget.Character);
        }

        private void CreateHost()
        {
            var scheduler = new ImmediateScheduler();
            var logger = new TestLogger();
            var eventHub = new EventHub(scheduler, logger);
            var registry = new AgentRegistry();

            _runtime = new ConvaiRuntimeBuilder()
                .UseEventHub(eventHub)
                .UseAgentRegistry(registry)
                .UseRoomRuntime(() => new RoomRuntimeBuilder()
                    .WithConnectionAdapter(new TestConnectionRuntimeAdapter())
                    .WithAudioAdapter(new TestAudioRuntimeAdapter())
                    .WithAgentRegistry(registry)
                    .Build())
                .Build();

            _host = new ConvaiRuntimeHost(_runtime);
        }

        private sealed class ImmediateScheduler : IUnityScheduler
        {
            public void ScheduleOnMainThread(Action action) => action?.Invoke();
            public void ScheduleOnBackground(Action action) => action?.Invoke();
            public bool IsMainThread() => true;
        }

        private sealed class TestLogger : ILogger
        {
            public bool IsEnabled(LogLevel level, LogCategory category) => false;
            public void Log(LogLevel level, string message, LogCategory category = LogCategory.SDK) { }
            public void Log(LogLevel level, string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
            public void Debug(string message, LogCategory category = LogCategory.SDK) { }
            public void Debug(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
            public void Info(string message, LogCategory category = LogCategory.SDK) { }
            public void Info(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
            public void Warning(string message, LogCategory category = LogCategory.SDK) { }
            public void Warning(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
            public void Error(string message, LogCategory category = LogCategory.SDK) { }
            public void Error(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
            public void Error(Exception exception, string message, LogCategory category = LogCategory.SDK) { }
            public void Error(Exception exception, string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
        }

        private static void SetPrivateField<TTarget, TValue>(TTarget target, string fieldName, TValue value)
        {
            FieldInfo field = typeof(TTarget).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Missing private field '{fieldName}'.");
            field.SetValue(target, value);
        }

        private sealed class TestConnectionRuntimeAdapter : IRoomConnectionRuntimeAdapter
        {
            public event Action<SessionStateChanged> StateChanged;

            public SessionState CurrentState => SessionState.Disconnected;
            public bool IsConnected => false;
            public string CurrentRoomName => string.Empty;
            public string CurrentSessionId => string.Empty;
            public bool CanConnect => true;
            public bool HasStarted => true;

            public Task<bool> WaitForStartCompletionAsync(CancellationToken ct) => Task.FromResult(true);
            public bool PrepareConnection() => true;
            public Task<bool> WaitForConnectionResolutionAsync(CancellationToken ct) => Task.FromResult(true);
            public Task<RoomConnectionAttemptResult> ConnectAsync(CancellationToken ct) =>
                Task.FromResult(RoomConnectionAttemptResult.Success());
            public Task DisconnectAsync(CancellationToken ct) => Task.CompletedTask;

            public void NotifyStateChanged(SessionStateChanged stateChanged) => StateChanged?.Invoke(stateChanged);
        }

        private sealed class TestAudioRuntimeAdapter : IRoomAudioRuntimeAdapter
        {
            public bool IsMicMuted => false;
            public void SetMicMuted(bool muted) { }
            public Task StartListeningAsync(int microphoneIndex, CancellationToken ct) => Task.CompletedTask;
            public Task StopListeningAsync(CancellationToken ct) => Task.CompletedTask;
            public bool SetCharacterMuted(string characterId, bool muted) => true;
            public bool IsCharacterMuted(string characterId) => false;
        }
    }
}
