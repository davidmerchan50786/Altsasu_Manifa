using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Transport;
using Convai.Modules.Vision;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Core;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;
using Convai.Runtime.Core.Configuration;
using Convai.Runtime.Core.Modules;
using Convai.Runtime.Core.Registry;
using Convai.Runtime.Room;
using Convai.Runtime.Vision.Publishing;
using Convai.Runtime.Vision.Sources;
using Convai.Shared.Types;
using NUnit.Framework;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;
using RtviSceneMetadata = Convai.Infrastructure.Protocol.Messages.SceneMetadata;

namespace Convai.Tests.EditMode.Runtime
{
    [TestFixture]
    public class ConvaiVisionPublisherModuleTests
    {
        private sealed class FakeVisionFrameSource : MonoBehaviour, IVisionFrameSource
        {
            public bool IsCapturing { get; private set; }
            public bool IsFrameReady { get; private set; }
            public long FrameCount => 0;
            public (int Width, int Height) FrameDimensions => (0, 0);
            public float TargetFrameRate => 15f;
            public string SourceId => "fake";
            public RenderTexture CurrentRenderTexture => null;
            public event Action FrameReady
            {
                add { }
                remove { }
            }

            public void StartCapture() => IsCapturing = true;
            public void StopCapture() => IsCapturing = false;
        }

        private sealed class FakeRoomConnectionService : IConvaiRoomConnectionService
        {
            public int ConnectedSubscriberCount { get; private set; }

            /// <summary>Returns Video connection type to enable vision publishing in tests.</summary>
            public ConvaiConnectionType ConnectionType => ConvaiConnectionType.Video;

            public SessionState CurrentState => SessionState.Disconnected;
            public bool IsConnected { get; set; }
            public bool HasRoomDetails => false;
            public bool HasPendingOwnershipReconnect => false;
            public IRoomFacade CurrentRoom => null;
            public RTVIHandler RtvHandler => null;

            public event Action Connected
            {
                add
                {
                    ConnectedSubscriberCount++;
                }
                remove
                {
                    ConnectedSubscriberCount--;
                }
            }

#pragma warning disable CS0067
            public event Action<SessionError> OnSessionError;
            public event Action<SessionStateChanged> OnSessionStateChanged;
#pragma warning restore CS0067

            public IConvaiOperation<RoomSession> ConnectAsync(CancellationToken cancellationToken = default) =>
                ConvaiOperation<RoomSession>.Succeeded(new RoomSession(
                    "video-test-session",
                    "video-test-room",
                    "local-player",
                    DateTime.UtcNow));

            public IConvaiOperation<RoomSession> ConnectAsync(
                RoomSessionConnectOptions options,
                CancellationToken cancellationToken = default) =>
                ConnectAsync(cancellationToken);

            public IConvaiOperation<Unit> DisconnectAsync(
                DisconnectReason reason = DisconnectReason.ClientInitiated,
                CancellationToken cancellationToken = default) =>
                ConvaiOperation<Unit>.Succeeded(
                    Unit.Value);

            public bool SendTrigger(string triggerName, string triggerMessage = null) => false;
            public bool SendDynamicInfo(string contextText) => false;
            public bool UpdateSceneMetadata(IReadOnlyList<RtviSceneMetadata> sceneMetadata) => false;
            public bool UpdateDynamicContext(string text, string mode = "append", string runLlm = "auto") => false;
            public bool UpdateTemplateKeys(Dictionary<string, string> templateKeys) => false;
            public bool SetTtsEnabled(bool ttsEnabled) => false;
            public bool SetSttMuted(bool muted) => false;
            public bool InterruptBot() => false;
            public bool KillPipeline() => false;
            public bool ForceUserStoppedSpeaking() => false;
            public bool ResetIdleTimer() => false;
        }

        private sealed class FakeVideoSourceFactory : IVideoSourceFactory
        {
            public IVideoSource CreateFromRenderTexture(RenderTexture texture, string name = null) => null;
            public IVideoSource CreateFromCamera(Camera camera, int width, int height, string name = null) => null;
            public IVideoSource CreateFromCanvasCapture(string name = null, int targetFrameRate = 15) => null;
        }

        private sealed class FakeTransportProvider : ITransportProvider
        {
            private readonly IVideoSourceFactory _videoSourceFactory = new FakeVideoSourceFactory();

            public TransportCapabilities Capabilities => TransportCapabilities.Native();
            public IRealtimeTransport CreateTransport() => null;
            public IMicrophoneSourceFactory CreateMicrophoneFactory() => null;
            public IAudioStreamFactory CreateAudioStreamFactory() => null;
            public IVideoSourceFactory CreateVideoSourceFactory() => _videoSourceFactory;
            public IConvaiRoomControllerFactory CreateRoomControllerFactory() => null;
        }

        private sealed class FakeModuleContext : IModuleContext
        {
            private readonly Dictionary<Type, object> _services = new();

            public FakeModuleContext(ConvaiRuntime runtime) => Runtime = runtime;

            public ConvaiRuntime Runtime { get; }
            public IEventHub Events => Runtime.Events;
            public IAgentRegistry Agents => Runtime.Agents;
            public ITransportProvider Transport => Runtime.Transport;
            public IRuntimePreferences Preferences => Runtime.RuntimePreferences;
            public ILogger Logger => null;
            public IConvaiRoomAudioService RoomAudio => null;
            public ICredentialProvider Credentials => null;

            public bool TryGetModuleService<TService>(out TService service) where TService : class
            {
                if (_services.TryGetValue(typeof(TService), out object value))
                {
                    service = value as TService;
                    return service != null;
                }

                service = null;
                return false;
            }

            public void ProvideModuleService<TService>(TService instance) where TService : class =>
                _services[typeof(TService)] = instance;
        }

        private static ConvaiRuntime CreateRuntime()
        {
            return new ConvaiRuntimeBuilder()
                .UseEventHub(new EventHub(new ImmediateScheduler(), new TestLogger()))
                .UseAgentRegistry(new AgentRegistry())
                .UseTransport(new FakeTransportProvider())
                .Build();
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

        [Test]
        public async Task RegisterAsync_ResolvesLocalFrameSource()
        {
            var runtime = CreateRuntime();
            var context = new FakeModuleContext(runtime);
            var go = new GameObject("publisher");

            try
            {
                var publisher = go.AddComponent<ConvaiVisionPublisher>();
                var frameSource = go.AddComponent<FakeVisionFrameSource>();

                await publisher.RegisterAsync(context);

                Assert.That(publisher.FrameSource, Is.SameAs(frameSource));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public async Task StartAsync_UsesModuleContextConnectionService_And_StopAsync_UnregistersCallbacks()
        {
            var runtime = CreateRuntime();
            var context = new FakeModuleContext(runtime);
            var roomConnection = new FakeRoomConnectionService();
            context.ProvideModuleService<IConvaiRoomConnectionService>(roomConnection);

            var go = new GameObject("publisher");

            try
            {
                var publisher = go.AddComponent<ConvaiVisionPublisher>();
                go.AddComponent<FakeVisionFrameSource>();

                await publisher.RegisterAsync(context);
                await publisher.StartAsync(context);

                Assert.That(roomConnection.ConnectedSubscriberCount, Is.EqualTo(1));

                await publisher.StopAsync();

                Assert.That(roomConnection.ConnectedSubscriberCount, Is.EqualTo(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public async Task StartAsync_ManualPolicy_DoesNotAutoStartCapture_WhenAlreadyConnected()
        {
            var runtime = CreateRuntime();
            var context = new FakeModuleContext(runtime);
            var roomConnection = new FakeRoomConnectionService
            {
                IsConnected = true
            };
            context.ProvideModuleService<IConvaiRoomConnectionService>(roomConnection);

            var go = new GameObject("publisher");

            try
            {
                var publisher = go.AddComponent<ConvaiVisionPublisher>();
                var frameSource = go.AddComponent<FakeVisionFrameSource>();

                publisher.SetPublishPolicy(VisionPublishPolicy.Manual);

                await publisher.RegisterAsync(context);
                await publisher.StartAsync(context);
                await Task.Yield();

                Assert.That(frameSource.IsCapturing, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public async Task EnablePublishing_ManualPolicy_StartsCapture_WhenConnected()
        {
            var runtime = CreateRuntime();
            var context = new FakeModuleContext(runtime);
            var roomConnection = new FakeRoomConnectionService
            {
                IsConnected = true
            };
            context.ProvideModuleService<IConvaiRoomConnectionService>(roomConnection);

            var go = new GameObject("publisher");

            try
            {
                var publisher = go.AddComponent<ConvaiVisionPublisher>();
                var frameSource = go.AddComponent<FakeVisionFrameSource>();

                publisher.SetPublishPolicy(VisionPublishPolicy.Manual);

                await publisher.RegisterAsync(context);
                await publisher.StartAsync(context);

                publisher.EnablePublishing(true);
                await Task.Yield();

                Assert.That(frameSource.IsCapturing, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void OnValidate_ClampsPublishOverridesToNonNegativeValues()
        {
            var go = new GameObject("publisher");

            try
            {
                var publisher = go.AddComponent<ConvaiVisionPublisher>();
                typeof(ConvaiVisionPublisher).GetField("publishFrameRateOverride")?.SetValue(publisher, -3);
                typeof(ConvaiVisionPublisher).GetField("publishBitrateOverride")?.SetValue(publisher, -99);

                typeof(ConvaiVisionPublisher)
                    .GetMethod("OnValidate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.Invoke(publisher, null);

                Assert.That(publisher.publishFrameRateOverride, Is.EqualTo(0));
                Assert.That(publisher.publishBitrateOverride, Is.EqualTo(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
