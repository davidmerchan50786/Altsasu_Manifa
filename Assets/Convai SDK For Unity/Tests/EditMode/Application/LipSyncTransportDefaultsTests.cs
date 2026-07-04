using System.Collections.Generic;
using Convai.Domain.Models.LipSync;
using Convai.Modules.LipSync;
using Convai.Modules.LipSync.Profiles;
using Convai.Shared.Types;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Convai.Tests.EditMode.Application
{
    [TestFixture]
    public class LipSyncTransportDefaultsTests
    {
        [SetUp]
        public void SetUp() => LipSyncProfileCatalog.ClearCachesForTests();

        [TearDown]
        public void TearDown()
        {
            LipSyncProfileCatalog.ClearCachesForTests();
            for (int i = 0; i < _cleanup.Count; i++)
                if (_cleanup[i] != null)
                    Object.DestroyImmediate(_cleanup[i]);

            _cleanup.Clear();
        }

        private readonly List<Object> _cleanup = new();

        [Test]
        public void TryBuildForProfile_WithUnknownProfile_ReturnsFalseAndDisabledOptions()
        {
            // Arrange
            LipSyncProfileCatalog.SetRegistryOverridesForTests(null, new ConvaiLipSyncProfileRegistryAsset[0]);

            // Act
            bool built = LipSyncTransportDefaults.TryBuildForProfile(
                new LipSyncProfileId("unknown_profile"),
                new[] { "A" },
                out LipSyncTransportOptions options);

            // Assert
            Assert.IsFalse(built);
            Assert.IsFalse(options.Enabled);
        }

        [Test]
        public void TryBuildForProfile_WithKnownBuiltInProfile_UsesCanonicalSourceChannelOrder()
        {
            // Arrange
            ConvaiLipSyncProfileRegistryAsset builtInRegistry = Track(CreateRegistry(
                "BuiltIn",
                0,
                CreateProfile(LipSyncProfileId.ARKitValue, "arkit")));
            LipSyncProfileCatalog.SetRegistryOverridesForTests(builtInRegistry,
                new ConvaiLipSyncProfileRegistryAsset[0]);

            // Act
            bool built = LipSyncTransportDefaults.TryBuildForProfile(
                LipSyncProfileId.ARKit,
                new[] { "EyeBlinkRight", "EyeBlinkLeft" },
                out LipSyncTransportOptions options);

            // Assert
            Assert.IsTrue(built);
            Assert.AreEqual("EyeBlinkLeft", options.SourceBlendshapeNames[0]);
        }

        [Test]
        public void TryBuildForProfile_WithValidProfile_ProducesValidEnabledContract()
        {
            // Arrange
            ConvaiLipSyncProfileRegistryAsset builtInRegistry = Track(CreateRegistry(
                "BuiltIn",
                0,
                CreateProfile(LipSyncProfileId.MetaHumanValue, "mha")));
            LipSyncProfileCatalog.SetRegistryOverridesForTests(builtInRegistry,
                new ConvaiLipSyncProfileRegistryAsset[0]);

            // Act
            bool built = LipSyncTransportDefaults.TryBuildForProfile(
                LipSyncProfileId.MetaHuman,
                new[] { "jawOpen" },
                out LipSyncTransportOptions options);

            // Assert
            Assert.IsTrue(built);
            Assert.IsTrue(options.IsValid);
            Assert.IsTrue(options.EnableChunking);
            Assert.AreEqual(60, options.OutputFps);
        }

        private ConvaiLipSyncProfileRegistryAsset CreateRegistry(string name, int priority,
            params ConvaiLipSyncProfileAsset[] profiles)
        {
            var registry = ScriptableObject.CreateInstance<ConvaiLipSyncProfileRegistryAsset>();
            registry.name = name;
            Track(registry);

            SerializedObject serialized = new(registry);
            serialized.FindProperty("_priority").intValue = priority;
            SerializedProperty list = serialized.FindProperty("_profiles");
            list.arraySize = profiles.Length;
            for (int i = 0; i < profiles.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = profiles[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return registry;
        }

        private ConvaiLipSyncProfileAsset CreateProfile(string id, string format)
        {
            var profile = ScriptableObject.CreateInstance<ConvaiLipSyncProfileAsset>();
            Track(profile);
            SerializedObject serialized = new(profile);
            serialized.FindProperty("_profileId").stringValue = id;
            serialized.FindProperty("_displayName").stringValue = id;
            serialized.FindProperty("_transportFormat").stringValue = format;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return profile;
        }

        private T Track<T>(T obj) where T : Object
        {
            if (obj != null) _cleanup.Add(obj);

            return obj;
        }
    }
}
