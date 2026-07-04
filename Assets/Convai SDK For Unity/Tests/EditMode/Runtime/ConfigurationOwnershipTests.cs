using System.Collections.Generic;
using System.Reflection;
using Convai.Runtime;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Components;
using Convai.Runtime.Room;
using Convai.Shared.Types;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Tests.EditMode.Runtime
{
    public class ConfigurationOwnershipTests
    {
        private readonly List<Object> _createdObjects = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _createdObjects.Count; i++)
                if (_createdObjects[i] != null)
                    Object.DestroyImmediate(_createdObjects[i]);
            _createdObjects.Clear();
        }

        [Test]
        public void RoomManager_UsesInlineDefaults_WhenNoRoomConfigAssetIsAssigned()
        {
            var go = new GameObject("RoomManager");
            _createdObjects.Add(go);
            var roomManager = go.AddComponent<ConvaiRoomManager>();

            SetAutoProperty(roomManager, "ConnectionType", ConvaiConnectionType.Audio);
            SetAutoProperty(roomManager, "VideoTrackName", "inline-track");
            SetAutoProperty(roomManager, "ServerEndpoint", ConvaiServerEndpoint.Connect);
            SetAutoProperty(roomManager, "ConnectOnStart", true);
            SetPrivateField(roomManager, "_roomRejoinTtlSeconds", 90f);
            SetPrivateField(roomManager, "_maxReconnectAttempts", 4);

            Assert.AreEqual(ConvaiConfigSourceMode.Inline, roomManager.ConfigurationSource);
            Assert.IsNull(roomManager.RoomConfigAsset);
            Assert.AreEqual(ConvaiConnectionType.Audio, roomManager.EffectiveConnectionType);
            Assert.AreEqual("inline-track", roomManager.EffectiveVideoTrackName);
            Assert.AreEqual(ConvaiServerEndpoint.Connect, roomManager.EffectiveServerEndpoint);
            Assert.IsTrue(roomManager.EffectiveConnectOnStart);
            Assert.That(roomManager.EffectiveReconnectPolicyDescription, Does.Contain("TTL=90"));
            Assert.That(roomManager.EffectiveReconnectPolicyDescription, Does.Contain("MaxAttempts=4"));
        }

        [Test]
        public void RoomManager_AssignedLegacyRoomConfigDefaultsToAssetMode()
        {
            var go = new GameObject("RoomManager");
            _createdObjects.Add(go);
            var roomManager = go.AddComponent<ConvaiRoomManager>();

            SetAutoProperty(roomManager, "ConnectionType", ConvaiConnectionType.Audio);
            SetAutoProperty(roomManager, "ServerEndpoint", ConvaiServerEndpoint.Connect);
            SetAutoProperty(roomManager, "ConnectOnStart", true);

            var roomConfig = ScriptableObject.CreateInstance<ConvaiRoomManagerProfile>();
            _createdObjects.Add(roomConfig);

            SerializedObject serializedRoomConfig = new(roomConfig);
            serializedRoomConfig.FindProperty("_connectionType").enumValueIndex = (int)ConvaiConnectionType.Video;
            serializedRoomConfig.FindProperty("_videoTrackName").stringValue = "profile-track";
            serializedRoomConfig.FindProperty("_serverEndpoint").enumValueIndex = (int)ConvaiServerEndpoint.RoomSession;
            serializedRoomConfig.FindProperty("_connectOnStart").boolValue = false;
            serializedRoomConfig.FindProperty("_maxReconnectAttempts").intValue = 7;
            serializedRoomConfig.ApplyModifiedPropertiesWithoutUndo();

            SetPrivateField(roomManager, "_roomConfigAsset", roomConfig);

            Assert.AreEqual(ConvaiConfigSourceMode.Asset, roomManager.ConfigurationSource);
            Assert.AreSame(roomConfig, roomManager.RoomConfigAsset);
            Assert.AreEqual(ConvaiConnectionType.Video, roomManager.EffectiveConnectionType);
            Assert.AreEqual("profile-track", roomManager.EffectiveVideoTrackName);
            Assert.AreEqual(ConvaiServerEndpoint.RoomSession, roomManager.EffectiveServerEndpoint);
            Assert.IsFalse(roomManager.EffectiveConnectOnStart);
            Assert.That(roomManager.EffectiveReconnectPolicyDescription, Does.Contain("MaxAttempts=7"));
        }

        [Test]
        public void RoomManager_ExplicitInlineSelection_WinsOverAssignedRoomConfigAsset()
        {
            var go = new GameObject("RoomManager");
            _createdObjects.Add(go);
            var roomManager = go.AddComponent<ConvaiRoomManager>();

            SetAutoProperty(roomManager, "ConnectionType", ConvaiConnectionType.Audio);
            SetAutoProperty(roomManager, "ServerEndpoint", ConvaiServerEndpoint.Connect);
            SetAutoProperty(roomManager, "ConnectOnStart", true);

            var roomConfig = ScriptableObject.CreateInstance<ConvaiRoomManagerProfile>();
            _createdObjects.Add(roomConfig);

            SerializedObject serializedRoomConfig = new(roomConfig);
            serializedRoomConfig.FindProperty("_connectionType").enumValueIndex = (int)ConvaiConnectionType.Video;
            serializedRoomConfig.FindProperty("_connectOnStart").boolValue = false;
            serializedRoomConfig.ApplyModifiedPropertiesWithoutUndo();

            SetPrivateField(roomManager, "_roomConfigAsset", roomConfig);
            SetPrivateField(roomManager, "_configurationSource", ConvaiConfigSourceMode.Inline);
            SetPrivateField(roomManager, "_configurationSourceInitialized", true);

            Assert.AreEqual(ConvaiConfigSourceMode.Inline, roomManager.ConfigurationSource);
            Assert.AreSame(roomConfig, roomManager.RoomConfigAsset);
            Assert.AreEqual(ConvaiConnectionType.Audio, roomManager.EffectiveConnectionType);
            Assert.IsTrue(roomManager.EffectiveConnectOnStart);
        }

        [Test]
        public void RoomManager_InlineAecOverride_PreventsLegacyAssetAutoSelection()
        {
            var go = new GameObject("RoomManager");
            _createdObjects.Add(go);
            var roomManager = go.AddComponent<ConvaiRoomManager>();

            TurnTakingOptions inlineTurnTaking = TurnTakingOptions.CreateHandsFreeDefault();
            inlineTurnTaking.LocalAudioPolicy.EnableAcousticEchoCancellation = true;
            SetPrivateField(roomManager, "_turnTakingOptions", inlineTurnTaking);

            var roomConfig = ScriptableObject.CreateInstance<ConvaiRoomManagerProfile>();
            _createdObjects.Add(roomConfig);
            SetPrivateField(roomManager, "_roomConfigAsset", roomConfig);

            Assert.AreEqual(ConvaiConfigSourceMode.Inline, roomManager.ConfigurationSource);
            Assert.That(roomManager.EffectiveTurnTakingOptions.LocalAudioPolicy.EnableAcousticEchoCancellation, Is.True);
        }

        [Test]
        public void Character_UsesInlineFields_WhenNoCharacterConfigAssetIsAssigned()
        {
            var go = new GameObject("Character");
            _createdObjects.Add(go);
            var character = go.AddComponent<ConvaiCharacter>();
            character.Configure("local-char", "Local Character");
            SetPrivateField(character, "_nameTagColor", Color.cyan);
            SetPrivateField(character, "_enableRemoteAudio", true);
            SetPrivateField(character, "_enableSessionResume", true);

            Assert.AreEqual(ConvaiConfigSourceMode.Inline, character.ConfigurationSource);
            Assert.IsNull(character.CharacterConfigAsset);
            Assert.AreEqual("local-char", character.CharacterId);
            Assert.AreEqual("Local Character", character.CharacterName);
            Assert.AreEqual(Color.cyan, character.NameTagColor);
            Assert.IsTrue(character.EnableRemoteAudioOnStart);
            Assert.IsTrue(character.EnableSessionResume);
        }

        [Test]
        public void Character_AssignedLegacyCharacterConfigDefaultsToAssetMode()
        {
            var go = new GameObject("Character");
            _createdObjects.Add(go);
            var character = go.AddComponent<ConvaiCharacter>();
            character.Configure("local-char", "Local Character");
            SetPrivateField(character, "_nameTagColor", Color.green);
            SetPrivateField(character, "_enableRemoteAudio", false);
            SetPrivateField(character, "_enableSessionResume", false);

            var definition = ScriptableObject.CreateInstance<ConvaiCharacterProfile>();
            definition.name = "GuideAgent";
            _createdObjects.Add(definition);

            SerializedObject serializedDefinition = new(definition);
            serializedDefinition.FindProperty("_characterId").stringValue = "definition-char";
            serializedDefinition.FindProperty("_characterName").stringValue = "Guide";
            serializedDefinition.FindProperty("_nameTagColor").colorValue = Color.magenta;
            serializedDefinition.FindProperty("_enableRemoteAudioOnStart").boolValue = true;
            serializedDefinition.FindProperty("_enableSessionResume").boolValue = true;
            serializedDefinition.ApplyModifiedPropertiesWithoutUndo();

            SetPrivateField(character, "_characterConfigAsset", definition);

            Assert.AreEqual(ConvaiConfigSourceMode.Asset, character.ConfigurationSource);
            Assert.AreSame(definition, character.CharacterConfigAsset);
            Assert.AreEqual("definition-char", character.CharacterId);
            Assert.AreEqual("Guide", character.CharacterName);
            Assert.AreEqual(Color.magenta, character.NameTagColor);
            Assert.IsTrue(character.EnableRemoteAudioOnStart);
            Assert.IsTrue(character.EnableSessionResume);
        }

        [Test]
        public void Character_ExplicitInlineSelection_WinsOverAssignedCharacterConfigAsset()
        {
            var go = new GameObject("Character");
            _createdObjects.Add(go);
            var character = go.AddComponent<ConvaiCharacter>();
            character.Configure("local-char", "Local Character");

            var definition = ScriptableObject.CreateInstance<ConvaiCharacterProfile>();
            definition.name = "GuideAgent";
            _createdObjects.Add(definition);

            SerializedObject serializedDefinition = new(definition);
            serializedDefinition.FindProperty("_characterId").stringValue = "definition-char";
            serializedDefinition.FindProperty("_characterName").stringValue = "Guide";
            serializedDefinition.ApplyModifiedPropertiesWithoutUndo();

            SetPrivateField(character, "_characterConfigAsset", definition);
            SetPrivateField(character, "_configurationSource", ConvaiConfigSourceMode.Inline);
            SetPrivateField(character, "_configurationSourceInitialized", true);

            Assert.AreEqual(ConvaiConfigSourceMode.Inline, character.ConfigurationSource);
            Assert.AreSame(definition, character.CharacterConfigAsset);
            Assert.AreEqual("local-char", character.CharacterId);
            Assert.AreEqual("Local Character", character.CharacterName);
        }

        [Test]
        public void RoomManager_ConnectAsyncWithOptions_ClearsPendingOverrides_WhenConnectReturnsEarly()
        {
            var go = new GameObject("RoomManager");
            _createdObjects.Add(go);
            var roomManager = go.AddComponent<ConvaiRoomManager>();

            var operation = roomManager.ConnectAsync(new RoomSessionConnectOptions
            {
                TurnTaking = TurnTakingOptions.CreatePushToTalkDefault()
            });

            Assert.ThrowsAsync<System.Exception>(async () => await operation.AsTask());
            Assert.IsNull(GetPrivateField<ConvaiRoomManager, RoomSessionConnectOptions>(roomManager, "_pendingConnectOptions"));
        }

        [Test]
        public void RoomManager_ConnectAsyncWithOptions_DoesNotOverwriteExistingPendingOverrides()
        {
            var go = new GameObject("RoomManager");
            _createdObjects.Add(go);
            var roomManager = go.AddComponent<ConvaiRoomManager>();

            var firstOptions = new RoomSessionConnectOptions
            {
                TurnTaking = TurnTakingOptions.CreatePushToTalkDefault()
            };

            SetPrivateField(roomManager, "_pendingConnectOptions", firstOptions);

            var operation = roomManager.ConnectAsync(new RoomSessionConnectOptions
            {
                TurnTaking = TurnTakingOptions.CreateHandsFreeDefault()
            });

            Assert.ThrowsAsync<System.Exception>(async () => await operation.AsTask());

            RoomSessionConnectOptions pending =
                GetPrivateField<ConvaiRoomManager, RoomSessionConnectOptions>(roomManager, "_pendingConnectOptions");
            Assert.IsNotNull(pending);
            Assert.AreEqual(ConversationInputMode.PushToTalk, pending.TurnTaking.Mode);
        }

        [Test]
        public void RoomManager_AdoptsLegacyManagerConversationSettings_WhenEditorInitializedButConversationStillDefault()
        {
            var go = new GameObject("RoomManager");
            _createdObjects.Add(go);
            var roomManager = go.AddComponent<ConvaiRoomManager>();

            SetPrivateField(roomManager, "_configurationSource", ConvaiConfigSourceMode.Inline);
            SetPrivateField(roomManager, "_configurationSourceInitialized", true);

            roomManager.AdoptLegacyManagerConversationSettings(
                ConvaiManagerConversationMode.PushToTalk,
                KeyCode.B,
                interruptBotOnPress: false,
                requireTurnCompletionBeforeNextPress: true,
                pushToTalkTurnCompletionTimeoutMs: 4200);

            Assert.AreEqual(ConvaiConfigSourceMode.Inline, roomManager.ConfigurationSource);
            Assert.AreEqual(KeyCode.B, roomManager.PushToTalkKey);
            Assert.AreEqual(ConversationInputMode.PushToTalk, roomManager.EffectiveTurnTakingOptions.Mode);
            Assert.AreEqual(4200, roomManager.EffectiveTurnTakingOptions.PushToTalkPolicy.TurnCompletionTimeoutMs);
            Assert.IsFalse(roomManager.EffectiveTurnTakingOptions.PushToTalkPolicy.InterruptBotOnPress);
        }

        private static void SetAutoProperty<TTarget, TValue>(TTarget target, string propertyName, TValue value)
        {
            FieldInfo field = typeof(TTarget).GetField($"<{propertyName}>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Missing auto-property backing field for {propertyName}.");
            field.SetValue(target, value);
        }

        private static void SetPrivateField<TTarget>(TTarget target, string fieldName, object value)
        {
            FieldInfo field = typeof(TTarget).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Missing private field {fieldName}.");
            field.SetValue(target, value);
        }

        private static TValue GetPrivateField<TTarget, TValue>(TTarget target, string fieldName)
        {
            FieldInfo field = typeof(TTarget).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Missing private field {fieldName}.");
            return (TValue)field.GetValue(target);
        }
    }
}
