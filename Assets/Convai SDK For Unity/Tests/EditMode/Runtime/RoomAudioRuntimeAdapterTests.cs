using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.Errors;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Models;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Networking.Media;
using Convai.Runtime.Room;
using Convai.Shared.Abstractions;
using Convai.Shared.Types;
using Convai.Tests.EditMode.Mocks;
using NUnit.Framework;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Tests.EditMode.Runtime
{
    public class RoomAudioRuntimeAdapterTests
    {
        private sealed class ImmediateScheduler : IUnityScheduler
        {
            public void ScheduleOnMainThread(Action action) => action?.Invoke();
            public void ScheduleOnBackground(Action action) => action?.Invoke();
            public bool IsMainThread() => true;
        }

        private sealed class TestLogger : ILogger
        {
            public List<string> DebugMessages { get; } = new();
            public List<string> WarningMessages { get; } = new();
            public List<string> ErrorMessages { get; } = new();
            public bool DebugEnabled => true;

            public void Log(LogLevel level, string message, LogCategory category = LogCategory.SDK) { }

            public void Log(LogLevel level, string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            {
            }

            public void Debug(string message, LogCategory category = LogCategory.SDK) => DebugMessages.Add(message);

            public void Debug(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) => Debug(message, category);

            public void Info(string message, LogCategory category = LogCategory.SDK) { }

            public void Info(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            {
            }

            public void Warning(string message, LogCategory category = LogCategory.SDK) => WarningMessages.Add(message);

            public void Warning(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) => Warning(message, category);

            public void Error(string message, LogCategory category = LogCategory.SDK) => ErrorMessages.Add(message);

            public void Error(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) => Error(message, category);

            public void Error(Exception exception, string message = null, LogCategory category = LogCategory.SDK) =>
                ErrorMessages.Add(message ?? exception.Message);

            public void Error(Exception exception, string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) => Error(exception, message, category);

            public bool IsEnabled(LogLevel level, LogCategory category) => true;
        }

        private sealed class TestMicrophoneSource : IMicrophoneSource
        {
            public TestMicrophoneSource(string deviceName, int deviceIndex)
            {
                DeviceName = deviceName;
                DeviceIndex = deviceIndex;
                Name = string.IsNullOrWhiteSpace(deviceName) ? "default-mic" : deviceName;
            }

            public string Name { get; }
            public bool IsCapturing { get; private set; }
            public string DeviceName { get; }
            public int DeviceIndex { get; }
            public event Action<IMicrophoneSource, MicrophoneSessionInvalidationReason> SessionInvalidated;
            public bool IsMuted { get; set; }
            public bool EnableAcousticEchoCancellation { get; set; }

            public void StartCapture() => IsCapturing = true;
            public void StopCapture() => IsCapturing = false;
            public void Dispose() => StopCapture();
            public void Invalidate(MicrophoneSessionInvalidationReason reason) => SessionInvalidated?.Invoke(this, reason);
        }

        private sealed class RecordingMicrophoneSourceFactory : IMicrophoneSourceFactory
        {
            private readonly string[] _devices;

            public RecordingMicrophoneSourceFactory(params string[] devices)
            {
                _devices = devices ?? Array.Empty<string>();
            }

            public string LastCreateDeviceName { get; private set; }
            public int LastCreateDeviceIndex { get; private set; }
            public IMicrophoneSource LastCreatedSource { get; private set; }
            public List<TestMicrophoneSource> CreatedSources { get; } = new();

            public IMicrophoneSource Create(string deviceName, int deviceIndex = 0, GameObject hostObject = null)
            {
                LastCreateDeviceName = deviceName;
                LastCreateDeviceIndex = deviceIndex;
                LastCreatedSource = new TestMicrophoneSource(deviceName, deviceIndex);
                CreatedSources.Add((TestMicrophoneSource)LastCreatedSource);
                return LastCreatedSource;
            }

            public string[] GetAvailableDevices() => _devices;
        }

        private sealed class TestLocalAudioTrack : ILocalAudioTrack
        {
            public TestLocalAudioTrack(IAudioSource source)
            {
                Source = source;
                Name = source?.Name ?? "microphone";
            }

            public string Sid => "audio-track-1";
            public string Name { get; }
            public TrackKind Kind => TrackKind.Audio;
            public bool IsMuted { get; private set; }
            public bool IsPublished { get; private set; } = true;
            public IAudioSource Source { get; }
            public event Action<bool> MuteChanged;

            public void SetMuted(bool muted)
            {
                IsMuted = muted;
                MuteChanged?.Invoke(muted);
            }

            public void MarkUnpublished() => IsPublished = false;
        }

        private sealed class TestLocalParticipant : ILocalParticipant
        {
            private readonly List<ILocalTrack> _localTracks = new();

            public bool ReturnNullAudioTrack { get; set; }
            public IAudioSource LastPublishedSource { get; private set; }

            public string Sid => "local-participant";
            public string Identity => "local";
            public string Name => "Local";
            public ParticipantMetadata Metadata => default;
            public bool IsAgent => false;
            public IReadOnlyList<ILocalTrack> LocalTracks => _localTracks;

            public event Action<ParticipantMetadata> MetadataUpdated
            {
                add { }
                remove { }
            }

            public event Action<ILocalTrack> TrackPublished;
            public event Action<ILocalTrack> TrackUnpublished;

            public Task<ILocalAudioTrack> PublishAudioTrackAsync(IAudioSource source,
                AudioPublishOptions options = default, CancellationToken ct = default)
            {
                LastPublishedSource = source;

                if (ReturnNullAudioTrack)
                    return Task.FromResult<ILocalAudioTrack>(null);

                var track = new TestLocalAudioTrack(source);
                _localTracks.Add(track);
                TrackPublished?.Invoke(track);
                return Task.FromResult<ILocalAudioTrack>(track);
            }

            public Task<ILocalVideoTrack> PublishVideoTrackAsync(IVideoSource source,
                VideoPublishOptions options = default, CancellationToken ct = default) =>
                Task.FromResult<ILocalVideoTrack>(null);

            public Task UnpublishTrackAsync(ILocalTrack track, CancellationToken ct = default)
            {
                if (track is TestLocalAudioTrack audioTrack)
                    audioTrack.MarkUnpublished();

                _localTracks.Remove(track);
                TrackUnpublished?.Invoke(track);
                return Task.CompletedTask;
            }

            public void SetAudioMuted(bool muted) { }
            public void SetVideoMuted(bool muted) { }
        }

        private sealed class StubMicrophoneDeviceService : IMicrophoneDeviceService
        {
            private readonly List<ConvaiMicrophoneDevice> _devices;

            public StubMicrophoneDeviceService(params ConvaiMicrophoneDevice[] devices)
            {
                _devices = new List<ConvaiMicrophoneDevice>(devices ?? Array.Empty<ConvaiMicrophoneDevice>());
            }

            public IReadOnlyList<ConvaiMicrophoneDevice> GetAvailableDevices() => _devices;

            public ConvaiMicrophoneDevice ResolvePreferredDevice(string preferredDeviceId)
            {
                if (_devices.Count == 0)
                    return ConvaiMicrophoneDevice.None;

                if (!string.IsNullOrWhiteSpace(preferredDeviceId))
                {
                    for (int i = 0; i < _devices.Count; i++)
                    {
                        if (string.Equals(_devices[i].Id, preferredDeviceId, StringComparison.Ordinal))
                            return _devices[i];
                    }
                }

                return _devices[0];
            }

            public string ResolvePreferredDeviceId(string preferredDeviceId) =>
                ResolvePreferredDevice(preferredDeviceId).Id;

            public int ResolvePreferredDeviceIndex(string preferredDeviceId) =>
                ResolvePreferredDevice(preferredDeviceId).Index;

            public bool TryResolveDeviceId(string deviceId, out ConvaiMicrophoneDevice device)
            {
                device = ConvaiMicrophoneDevice.None;
                if (string.IsNullOrWhiteSpace(deviceId))
                    return false;

                for (int i = 0; i < _devices.Count; i++)
                {
                    if (string.Equals(_devices[i].Id, deviceId, StringComparison.Ordinal))
                    {
                        device = _devices[i];
                        return true;
                    }
                }

                return false;
            }
        }

        private sealed class TestRoomFacade : IRoomFacade
        {
            public string Sid => "room";
            public string Name => "Test Room";
            public RoomState State => RoomState.Connected;
            public bool IsConnected => true;
            public ILocalParticipant LocalParticipant { get; set; }
            public IReadOnlyList<IRemoteParticipant> RemoteParticipants => Array.Empty<IRemoteParticipant>();
            public IEnumerable<IParticipant> AllParticipants => Array.Empty<IParticipant>();
            public int RemoteParticipantCount => 0;

            public event Action<IRemoteParticipant> ParticipantJoined
            {
                add { }
                remove { }
            }

            public event Action<IRemoteParticipant> ParticipantLeft
            {
                add { }
                remove { }
            }

            public event Action<IParticipant, ParticipantMetadata> ParticipantMetadataUpdated
            {
                add { }
                remove { }
            }

            public event Action<IRemoteAudioTrack, IRemoteParticipant> AudioTrackSubscribed
            {
                add { }
                remove { }
            }

            public event Action<IRemoteAudioTrack, IRemoteParticipant> AudioTrackUnsubscribed
            {
                add { }
                remove { }
            }

            public event Action<IRemoteVideoTrack, IRemoteParticipant> VideoTrackSubscribed
            {
                add { }
                remove { }
            }

            public event Action<IRemoteVideoTrack, IRemoteParticipant> VideoTrackUnsubscribed
            {
                add { }
                remove { }
            }

            public event Action<TrackSubscriptionEventArgs> TrackSubscribed
            {
                add { }
                remove { }
            }

            public event Action<TrackSubscriptionEventArgs> TrackUnsubscribed
            {
                add { }
                remove { }
            }

            public event Action<RoomState> StateChanged
            {
                add { }
                remove { }
            }

            public event Action Reconnecting
            {
                add { }
                remove { }
            }

            public event Action Reconnected
            {
                add { }
                remove { }
            }

            public event Action<DisconnectReason> Disconnected
            {
                add { }
                remove { }
            }

            public IRemoteParticipant GetParticipantBySid(string sid) => null;
            public IRemoteParticipant GetParticipantByIdentity(string identity) => null;

            public bool TryGetParticipantBySid(string sid, out IRemoteParticipant participant)
            {
                participant = null;
                return false;
            }

            public bool TryGetParticipantByIdentity(string identity, out IRemoteParticipant participant)
            {
                participant = null;
                return false;
            }
        }

        private sealed class TestRoomController : IConvaiRoomController
        {
            public bool HasRoomDetails => true;
            public bool IsConnectedToRoom => true;
            public bool IsMicMuted => false;
            public string SessionID => "session-1";
            public string CharacterSessionID => "character-session-1";
            public string RoomName => "Test Room";
            public string RoomURL => "wss://test.invalid";
            public string Token => "token";
            public string ResolvedSpeakerId => string.Empty;
            public RTVIHandler RTVIHandler => null;
            public IRoomFacade CurrentRoom => null;

            public event Action OnRoomConnectionSuccessful
            {
                add { }
                remove { }
            }

            public event Action OnRoomConnectionFailed
            {
                add { }
                remove { }
            }

            public event Action<bool> OnMicMuteChanged
            {
                add { }
                remove { }
            }

            public event Action OnRoomReconnecting
            {
                add { }
                remove { }
            }

            public event Action OnRoomReconnected
            {
                add { }
                remove { }
            }

            public event Action OnUnexpectedRoomDisconnected
            {
                add { }
                remove { }
            }

            public event Action<IRemoteAudioTrack, string, string> OnRemoteAudioTrackSubscribed
            {
                add { }
                remove { }
            }

            public event Action<string, string> OnRemoteAudioTrackUnsubscribed
            {
                add { }
                remove { }
            }

            public Task<RoomConnectionAttemptResult> InitializeAsync(string connectionType, string coreServerUrl,
                string characterId, string storedSessionId, bool enableSessionResume, string dynamicInfoText,
                bool keepDynamicInfoInContext) => Task.FromResult(default(RoomConnectionAttemptResult));

            public Task<RoomConnectionAttemptResult> InitializeAsync(string connectionType, string coreServerUrl,
                string characterId, string storedSessionId, bool enableSessionResume, string dynamicInfoText,
                bool keepDynamicInfoInContext, RoomJoinOptions joinOptions,
                CancellationToken cancellationToken = default) => Task.FromResult(default(RoomConnectionAttemptResult));

            public void DisconnectFromRoom() { }
            public Task DisconnectFromRoomAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public void SetMicMuted(bool mute) { }
            public void ToggleMicMute() { }
            public bool SetCharacterAudioMuted(string characterId, bool mute) => true;
            public bool MuteCharacter(string characterId) => true;
            public bool UnmuteCharacter(string characterId) => true;
            public bool IsCharacterAudioMuted(string characterId) => false;
            public void SetAudioSubscriptionPolicy(Func<string, bool> policy) { }
            public void ApplyRemoteAudioPreference(string characterId, bool enabled) { }
            public void Dispose() { }
        }

        [Test]
        public async Task StartListeningAsync_WhenDeviceEnumerationIsEmpty_UsesPlatformDefaultWithoutPublishingUnavailable()
        {
            var logger = new TestLogger();
            var eventHub = new EventHub(new ImmediateScheduler());
            var localParticipant = new TestLocalParticipant();
            var room = new TestRoomFacade { LocalParticipant = localParticipant };
            using var audioTrackManager = new AudioTrackManager(
                () => room,
                new MockAgentRegistry(),
                logger,
                _ => null);

            var microphoneFactory = new RecordingMicrophoneSourceFactory();
            var adapter = new RoomAudioRuntimeAdapter(
                () => audioTrackManager,
                () => new TestRoomController(),
                () => false,
                () => true,
                () => microphoneFactory,
                _ => { },
                () => null,
                () => ResolvedTurnTakingOptions.DefaultHandsFree,
                () => string.Empty,
                () => new StubMicrophoneDeviceService(),
                () => eventHub,
                () => null,
                logger);

            SessionError? publishedError = null;
            eventHub.Subscribe<SessionError>(evt => publishedError = evt);

            await adapter.StartListeningAsync(0, CancellationToken.None);

            Assert.IsNull(microphoneFactory.LastCreateDeviceName);
            Assert.NotNull(localParticipant.LastPublishedSource);
            Assert.IsNull(publishedError, "Fallback to the platform default microphone should not publish an unavailable error when publish succeeds.");
            Assert.IsTrue(logger.DebugMessages.Exists(m => m.Contains("attempting platform default fallback")));
            Assert.IsFalse(logger.WarningMessages.Exists(m => m.Contains("No microphone devices detected")));
        }

        [Test]
        public async Task StartListeningAsync_WhenDeviceEnumerationIsEmptyAndPublishFails_PublishesMicUnavailable()
        {
            var logger = new TestLogger();
            var eventHub = new EventHub(new ImmediateScheduler());
            var localParticipant = new TestLocalParticipant { ReturnNullAudioTrack = true };
            var room = new TestRoomFacade { LocalParticipant = localParticipant };
            using var audioTrackManager = new AudioTrackManager(
                () => room,
                new MockAgentRegistry(),
                logger,
                _ => null);

            var microphoneFactory = new RecordingMicrophoneSourceFactory();
            var adapter = new RoomAudioRuntimeAdapter(
                () => audioTrackManager,
                () => new TestRoomController(),
                () => false,
                () => true,
                () => microphoneFactory,
                _ => { },
                () => null,
                () => ResolvedTurnTakingOptions.DefaultHandsFree,
                () => string.Empty,
                () => new StubMicrophoneDeviceService(),
                () => eventHub,
                () => null,
                logger);

            SessionError? publishedError = null;
            eventHub.Subscribe<SessionError>(evt => publishedError = evt);

            await adapter.StartListeningAsync(0, CancellationToken.None);

            Assert.IsNull(microphoneFactory.LastCreateDeviceName);
            Assert.IsTrue(publishedError.HasValue);
            Assert.AreEqual(SessionErrorCodes.AudioMicUnavailable, publishedError.Value.ErrorCode);
            Assert.IsTrue(logger.WarningMessages.Exists(m => m.Contains("platform default fallback failed")));
        }

        [Test]
        public async Task StartListeningAsync_WhenResolvedAecIsEnabled_PropagatesFlagToMicrophoneSource()
        {
            var logger = new TestLogger();
            var eventHub = new EventHub(new ImmediateScheduler());
            var localParticipant = new TestLocalParticipant();
            var room = new TestRoomFacade { LocalParticipant = localParticipant };
            using var audioTrackManager = new AudioTrackManager(
                () => room,
                new MockAgentRegistry(),
                logger,
                _ => null);

            var microphoneFactory = new RecordingMicrophoneSourceFactory("Built-in Mic");
            var resolvedTurnTakingOptions = TurnTakingOptionsResolver.Resolve(
                new TurnTakingOptions
                {
                    Mode = ConversationInputMode.HandsFree,
                    LocalAudioPolicy = new LocalAudioPolicy
                    {
                        EnableAcousticEchoCancellation = true
                    }
                },
                null);

            var adapter = new RoomAudioRuntimeAdapter(
                () => audioTrackManager,
                () => new TestRoomController(),
                () => false,
                () => true,
                () => microphoneFactory,
                _ => { },
                () => null,
                () => resolvedTurnTakingOptions,
                () => "Built-in Mic",
                () => new StubMicrophoneDeviceService(new ConvaiMicrophoneDevice("Built-in Mic", "Built-in Mic", 0)),
                () => eventHub,
                () => null,
                logger);

            await adapter.StartListeningAsync(0, CancellationToken.None);

            Assert.That(microphoneFactory.LastCreatedSource, Is.TypeOf<TestMicrophoneSource>());
            Assert.That(((TestMicrophoneSource)microphoneFactory.LastCreatedSource).EnableAcousticEchoCancellation,
                Is.True);
        }

        [Test]
        public async Task StartListeningAsync_WhenCalledWithNewIndex_RepublishesMicrophoneUsingRequestedDevice()
        {
            var logger = new TestLogger();
            var eventHub = new EventHub(new ImmediateScheduler());
            var localParticipant = new TestLocalParticipant();
            var room = new TestRoomFacade { LocalParticipant = localParticipant };
            using var audioTrackManager = new AudioTrackManager(
                () => room,
                new MockAgentRegistry(),
                logger,
                _ => null);

            var microphoneFactory = new RecordingMicrophoneSourceFactory("Mic A", "Mic B");
            var adapter = new RoomAudioRuntimeAdapter(
                () => audioTrackManager,
                () => new TestRoomController(),
                () => false,
                () => true,
                () => microphoneFactory,
                _ => { },
                () => null,
                () => ResolvedTurnTakingOptions.DefaultHandsFree,
                () => "Mic B",
                () => new StubMicrophoneDeviceService(
                    new ConvaiMicrophoneDevice("Mic A", "Mic A", 0),
                    new ConvaiMicrophoneDevice("Mic B", "Mic B", 1)),
                () => eventHub,
                () => null,
                logger);

            await adapter.StartListeningAsync(0, CancellationToken.None);
            await adapter.StartListeningAsync(1, CancellationToken.None);

            Assert.AreEqual(1, microphoneFactory.LastCreateDeviceIndex);
            Assert.AreEqual("Mic B", microphoneFactory.LastCreateDeviceName);
            Assert.AreEqual(2, microphoneFactory.CreatedSources.Count);
        }

        [Test]
        public async Task Invalidation_WhenRouteChanges_RepublishesCurrentMicrophoneAndPreservesMuteState()
        {
            var logger = new TestLogger();
            var eventHub = new EventHub(new ImmediateScheduler());
            var localParticipant = new TestLocalParticipant();
            var room = new TestRoomFacade { LocalParticipant = localParticipant };
            using var audioTrackManager = new AudioTrackManager(
                () => room,
                new MockAgentRegistry(),
                logger,
                _ => null);

            var microphoneFactory = new RecordingMicrophoneSourceFactory("Mic A", "Mic B");
            var adapter = new RoomAudioRuntimeAdapter(
                () => audioTrackManager,
                () => new TestRoomController(),
                () => false,
                () => true,
                () => microphoneFactory,
                _ => { },
                () => null,
                () => ResolvedTurnTakingOptions.DefaultHandsFree,
                () => "Mic A",
                () => new StubMicrophoneDeviceService(
                    new ConvaiMicrophoneDevice("Mic A", "Mic A", 0),
                    new ConvaiMicrophoneDevice("Mic B", "Mic B", 1)),
                () => eventHub,
                () => null,
                logger);

            await adapter.StartListeningAsync(0, CancellationToken.None);
            audioTrackManager.SetMicMuted(true);

            var firstSource = (TestMicrophoneSource)microphoneFactory.LastCreatedSource;
            firstSource.Invalidate(MicrophoneSessionInvalidationReason.RouteChanged);
            await Task.Delay(10);

            Assert.AreEqual(2, microphoneFactory.CreatedSources.Count);
            Assert.AreEqual(0, microphoneFactory.LastCreateDeviceIndex);
            Assert.IsTrue(((TestMicrophoneSource)microphoneFactory.LastCreatedSource).IsMuted);
        }

        [Test]
        public async Task Invalidation_WhenDeviceChanges_FallsBackToPreferredMicrophone()
        {
            var logger = new TestLogger();
            var eventHub = new EventHub(new ImmediateScheduler());
            var localParticipant = new TestLocalParticipant();
            var room = new TestRoomFacade { LocalParticipant = localParticipant };
            using var audioTrackManager = new AudioTrackManager(
                () => room,
                new MockAgentRegistry(),
                logger,
                _ => null);

            var microphoneFactory = new RecordingMicrophoneSourceFactory("Mic B");
            var adapter = new RoomAudioRuntimeAdapter(
                () => audioTrackManager,
                () => new TestRoomController(),
                () => false,
                () => true,
                () => microphoneFactory,
                _ => { },
                () => null,
                () => ResolvedTurnTakingOptions.DefaultHandsFree,
                () => "Mic B",
                () => new StubMicrophoneDeviceService(new ConvaiMicrophoneDevice("Mic B", "Mic B", 0)),
                () => eventHub,
                () => null,
                logger);

            await adapter.StartListeningAsync(0, CancellationToken.None);

            var firstSource = microphoneFactory.CreatedSources[0];
            firstSource.Invalidate(MicrophoneSessionInvalidationReason.DeviceChanged);
            await Task.Delay(10);

            Assert.AreEqual(2, microphoneFactory.CreatedSources.Count);
            Assert.AreEqual("Mic B", microphoneFactory.LastCreateDeviceName);
        }

        [Test]
        public async Task Invalidation_FromStaleSource_IsIgnored()
        {
            var logger = new TestLogger();
            var eventHub = new EventHub(new ImmediateScheduler());
            var localParticipant = new TestLocalParticipant();
            var room = new TestRoomFacade { LocalParticipant = localParticipant };
            using var audioTrackManager = new AudioTrackManager(
                () => room,
                new MockAgentRegistry(),
                logger,
                _ => null);

            var microphoneFactory = new RecordingMicrophoneSourceFactory("Mic A", "Mic B");
            var adapter = new RoomAudioRuntimeAdapter(
                () => audioTrackManager,
                () => new TestRoomController(),
                () => false,
                () => true,
                () => microphoneFactory,
                _ => { },
                () => null,
                () => ResolvedTurnTakingOptions.DefaultHandsFree,
                () => "Mic B",
                () => new StubMicrophoneDeviceService(
                    new ConvaiMicrophoneDevice("Mic A", "Mic A", 0),
                    new ConvaiMicrophoneDevice("Mic B", "Mic B", 1)),
                () => eventHub,
                () => null,
                logger);

            await adapter.StartListeningAsync(0, CancellationToken.None);
            var staleSource = microphoneFactory.CreatedSources[0];
            await adapter.StartListeningAsync(1, CancellationToken.None);

            staleSource.Invalidate(MicrophoneSessionInvalidationReason.RouteChanged);
            await Task.Delay(10);

            Assert.AreEqual(2, microphoneFactory.CreatedSources.Count);
            Assert.AreEqual(1, microphoneFactory.LastCreateDeviceIndex);
        }
    }
}
