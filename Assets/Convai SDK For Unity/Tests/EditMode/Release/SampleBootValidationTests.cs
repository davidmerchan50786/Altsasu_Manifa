using System;
using System.Reflection;
using Convai.Modules.LipSync;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Components;
using Convai.Runtime.Presentation.Views.Notifications;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Convai.Tests.EditMode.Release
{
    [Category("Release")]
    public sealed class SampleBootValidationTests
    {
        private const string BasicSampleScenePath =
            "Packages/com.convai.convai-sdk-for-unity/Samples/BasicSample/Scenes/Basic Sample.unity";

        private const string LipSyncSampleScenePath =
            "Packages/com.convai.convai-sdk-for-unity/Samples/LipSyncSample/Scenes/LipSync Sample.unity";

        [TearDown]
        public void TearDown()
        {
            // Ensure subsequent tests do not inherit the sample scene state.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void BasicSample_Loads_AndContainsCoreRuntimeObjects()
        {
            Scene scene = EditorSceneManager.OpenScene(BasicSampleScenePath, OpenSceneMode.Single);
            Assert.IsTrue(scene.IsValid(), "Expected Basic sample scene to load.");

            ConvaiManager manager = Object.FindFirstObjectByType<ConvaiManager>(FindObjectsInactive.Include);
            Assert.IsNotNull(manager,
                "Basic sample should contain ConvaiManager.");
            Assert.IsNotNull(ResolveOrProvisionRoomManager(manager),
                "Basic sample should expose a ConvaiRoomManager path (serialized or manager-provisioned).");
            Assert.IsNotNull(Object.FindFirstObjectByType<ConvaiPlayer>(FindObjectsInactive.Include),
                "Basic sample should contain ConvaiPlayer.");

            ConvaiCharacter[] characters = Object.FindObjectsByType<ConvaiCharacter>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            Assert.GreaterOrEqual(characters.Length, 1, "Basic sample should contain at least one ConvaiCharacter.");

            Assert.IsNotNull(Object.FindFirstObjectByType<NotificationHandler>(FindObjectsInactive.Include),
                "Basic sample should include the NotificationSystem prefab instance.");

            AssertNoEditorOnlyBehavioursInScene();
        }

        [Test]
        public void LipSyncSample_Loads_AndContainsLipSyncComponent()
        {
            Scene scene = EditorSceneManager.OpenScene(LipSyncSampleScenePath, OpenSceneMode.Single);
            Assert.IsTrue(scene.IsValid(), "Expected LipSync sample scene to load.");

            ConvaiManager manager = Object.FindFirstObjectByType<ConvaiManager>(FindObjectsInactive.Include);
            Assert.IsNotNull(manager,
                "LipSync sample should contain ConvaiManager.");
            Assert.IsNotNull(ResolveOrProvisionRoomManager(manager),
                "LipSync sample should expose a ConvaiRoomManager path (serialized or manager-provisioned).");

            ConvaiCharacter[] characters = Object.FindObjectsByType<ConvaiCharacter>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            Assert.GreaterOrEqual(characters.Length, 1, "LipSync sample should contain at least one ConvaiCharacter.");

            Assert.IsNotNull(Object.FindFirstObjectByType<ConvaiLipSyncComponent>(FindObjectsInactive.Include),
                "LipSync sample should include at least one ConvaiLipSyncComponent.");

            AssertNoEditorOnlyBehavioursInScene();
        }

        private static void AssertNoEditorOnlyBehavioursInScene()
        {
            MonoBehaviour[] behaviours = Object.FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null) continue;

                Type type = behaviour.GetType();
                string assemblyName = type.Assembly.GetName().Name ?? string.Empty;
                if (assemblyName.EndsWith(".Editor", StringComparison.Ordinal))
                {
                    Assert.Fail(
                        $"Scene contains editor-only behaviour '{type.FullName}' from assembly '{assemblyName}'.");
                }

                string ns = type.Namespace ?? string.Empty;
                if (ns.Contains(".Editor", StringComparison.Ordinal))
                {
                    Assert.Fail($"Scene contains editor-only behaviour '{type.FullName}' (namespace '{ns}').");
                }
            }
        }

        private static ConvaiRoomManager ResolveOrProvisionRoomManager(ConvaiManager manager)
        {
            if (manager == null) return null;

            ConvaiRoomManager roomManager = Object.FindFirstObjectByType<ConvaiRoomManager>(FindObjectsInactive.Include);
            if (roomManager != null) return roomManager;

            MethodInfo ensureRoomManagerReference = typeof(ConvaiManager).GetMethod(
                "EnsureRoomManagerReference",
                BindingFlags.Instance | BindingFlags.NonPublic);
            ensureRoomManagerReference?.Invoke(manager, null);

            return manager.GetComponent<ConvaiRoomManager>()
                   ?? Object.FindFirstObjectByType<ConvaiRoomManager>(FindObjectsInactive.Include);
        }
    }
}
