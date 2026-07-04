using System;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using Convai.Runtime.Room;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Modules.LipSync
{
    internal sealed class LipSyncRuntimeController : ILipSyncRuntimeController
    {
        private readonly IPlaybackClockCoordinator _clockCoordinator;
        private string _boundCharacterId;
        private ConvaiLipSyncBridge _bridge;

        private LipSyncRuntimeConfig _config;

        private IEventHub _eventHub;
        private ILogger _logger;
        private PlaybackClockCommand _pendingClockCommand;
        private IConvaiRoomAudioService _roomAudioService;
        private IBlendshapeSink _sink;

        public LipSyncRuntimeController(IPlaybackClockCoordinator clockCoordinator)
        {
            _clockCoordinator = clockCoordinator ?? throw new ArgumentNullException(nameof(clockCoordinator));
        }

        public bool IsInitialized { get; private set; }

        public bool IsPlaying => Engine?.IsPlaying ?? false;
        public bool IsFadingOut => Engine?.IsFadingOut ?? false;
        public PlaybackState EngineState => Engine?.State ?? PlaybackState.Idle;
        public LipSyncPlaybackEngine Engine { get; private set; }

        public IPlaybackClock CurrentClock => _clockCoordinator.CurrentClock;

        public void EnsureInitialized(Component context, LipSyncRuntimeConfig config,
            ConvaiLipSyncMapAsset effectiveMapping)
        {
            if (!IsInitialized)
            {
                Engine = new LipSyncPlaybackEngine(config.ToEngineConfig());
                Engine.StateChanged += OnEngineStateChanged;
                _sink = new SkinnedMeshBlendshapeSink();
                _clockCoordinator.Initialize(context, Engine);
                IsInitialized = true;
            }

            Reconfigure(config, effectiveMapping);
        }

        public void Reconfigure(LipSyncRuntimeConfig config, ConvaiLipSyncMapAsset effectiveMapping)
        {
            if (!IsInitialized) return;

            bool profileChanged = _config.ProfileId != config.ProfileId;
            _config = config;
            Engine.Configure(config.ToEngineConfig());
            _sink?.Initialize(config.TargetMeshes, effectiveMapping);

            if (profileChanged) RecreateBridgeForCurrentProfile();
        }

        public void SetRoomAudioService(IConvaiRoomAudioService roomAudioService)
        {
            if (ReferenceEquals(_roomAudioService, roomAudioService)) return;

            _roomAudioService = roomAudioService;
            RecreateBridgeForCurrentProfile();
        }

        public void Bind(IEventHub eventHub, string characterId, ILogger logger)
        {
            _eventHub = eventHub;
            _logger = logger;
            _boundCharacterId = characterId?.Trim() ?? string.Empty;

            if (!IsInitialized || _eventHub == null || string.IsNullOrWhiteSpace(_boundCharacterId)) return;

            EnsureBridge();
            _bridge.Bind(_eventHub, _boundCharacterId);
        }

        public void UnbindAndReset()
        {
            _bridge?.Unbind();
            Engine?.Stop();
            _clockCoordinator.Reset();
        }

        public void Tick(float deltaTime)
        {
            if (!IsInitialized || Engine == null) return;

            _clockCoordinator.Tick(Engine);

            if (_clockCoordinator.CurrentClock == null) return;

            float dt = Mathf.Clamp(deltaTime, LipSyncConstants.MinDeltaTime, LipSyncConstants.MaxDeltaTimeForFade);
            bool updated = Engine.Tick(_clockCoordinator.GetElapsedSeconds(), dt);

            ApplyPendingClockCommand();

            if (updated) _sink.Apply(Engine.OutputValues, Engine.ChannelNames);
        }

        public float GetTalkingTimeRemaining()
        {
            if (Engine == null || _clockCoordinator.CurrentClock == null || !Engine.IsPlaying) return 0f;

            return Engine.GetRemainingSeconds(_clockCoordinator.GetElapsedSeconds());
        }

        public float GetTalkingTimeElapsed()
        {
            if (Engine == null || _clockCoordinator.CurrentClock == null || !Engine.IsPlaying) return 0f;

            return Mathf.Max(0f, (float)_clockCoordinator.GetElapsedSeconds());
        }

        public float GetTotalBufferedDuration() => Engine?.BufferedDuration ?? 0f;

        public float GetTotalStreamDuration() => Engine?.TotalIngressDuration ?? 0f;

        public float GetHeadroom()
        {
            if (Engine == null || _clockCoordinator.CurrentClock == null || !Engine.IsPlaying) return 0f;

            return Engine.GetHeadroomSeconds(_clockCoordinator.GetElapsedSeconds());
        }

        public BlendshapeSnapshot GetBlendshapeSnapshot()
        {
            if (Engine == null || Engine.OutputValues == null || Engine.ChannelNames == null) return default;

            return new BlendshapeSnapshot(Engine.OutputValues, Engine.ChannelNames);
        }

        public void Dispose()
        {
            if (Engine != null) Engine.StateChanged -= OnEngineStateChanged;

            _bridge?.Dispose();
            _bridge = null;
            _clockCoordinator.Dispose();
        }

        private void OnEngineStateChanged(PlaybackState prev, PlaybackState next)
        {
            if (next == PlaybackState.Playing && prev == PlaybackState.Buffering)
                _pendingClockCommand = PlaybackClockCommand.Start;

            if (next == PlaybackState.Starving) _pendingClockCommand = PlaybackClockCommand.Pause;

            if (next == PlaybackState.Playing && prev == PlaybackState.Starving)
                _pendingClockCommand = PlaybackClockCommand.Resume;

            if (next == PlaybackState.Idle)
            {
                _pendingClockCommand = PlaybackClockCommand.Reset;
                _sink?.ResetToZero(Engine?.ChannelNames);
            }
        }

        private void ApplyPendingClockCommand()
        {
            PlaybackClockCommand command = _pendingClockCommand;
            _pendingClockCommand = PlaybackClockCommand.None;
            _clockCoordinator.ApplyCommand(command);
        }

        private void RecreateBridgeForCurrentProfile()
        {
            if (_bridge == null) return;

            _bridge.Dispose();
            _bridge = null;
            if (_eventHub != null && !string.IsNullOrWhiteSpace(_boundCharacterId))
            {
                EnsureBridge();
                _bridge.Bind(_eventHub, _boundCharacterId);
            }
        }

        private void EnsureBridge()
        {
            if (_bridge != null) return;

            if (Engine == null)
            {
                ConvaiLogger.Error("[Convai LipSync] Runtime engine is not initialized. Bridge binding skipped.",
                    LogCategory.LipSync);
                return;
            }

            _bridge = new ConvaiLipSyncBridge(Engine, _config.ProfileId, _roomAudioService, _logger);
        }
    }
}
