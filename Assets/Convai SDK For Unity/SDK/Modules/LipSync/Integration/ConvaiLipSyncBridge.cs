using System;
using Convai.Domain.DomainEvents.LipSync;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Domain.Models.LipSync;
using Convai.Runtime.Logging;
using Convai.Runtime.Room;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Bridges Convai SDK events (IEventHub) to the LipSync playback engine.
    ///     Subscribes to LipSyncPackedDataReceived, CharacterSpeechStateChanged, and CharacterAudioPlaybackStateChanged.
    ///     Filters by character, feeds frames to the engine, and gates playback start on actual audio signal.
    /// </summary>
    internal sealed class ConvaiLipSyncBridge : IDisposable
    {
        private const string LogPrefix = "[Convai LipSync Bridge]";

        private readonly LipSyncPlaybackEngine _engine;
        private readonly LipSyncProfileId _lockedProfile;
        private readonly ILogger _logger;
        private readonly bool _requiresSpeakingStateToOpenPlaybackGate;
        private SubscriptionToken _audioPlaybackToken;
        private string _characterId;
        private SubscriptionToken _dataToken;
        private bool _disposed;

        private IEventHub _eventHub;
        private bool _isAudioPlaybackActiveForCharacter;
        private bool _isCharacterSpeaking;
        private SubscriptionToken _speechToken;

        public ConvaiLipSyncBridge(
            LipSyncPlaybackEngine engine,
            LipSyncProfileId lockedProfile,
            IConvaiRoomAudioService roomAudioService = null,
            ILogger logger = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _lockedProfile = lockedProfile;
            _requiresSpeakingStateToOpenPlaybackGate = roomAudioService?.RequiresUserGestureForAudio ?? false;
            _logger = logger;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            Unbind();
        }

        /// <summary>
        ///     Binds to the event hub and subscribes to lip sync data, speech state, and audio playback events.
        ///     Playback starts only after the platform-specific onset gate opens. Native platforms open from
        ///     <see cref="CharacterAudioPlaybackStateChanged" /> directly; WebGL additionally requires
        ///     <see cref="CharacterSpeechStateChanged" /> so track attachment does not start lip sync before speech begins.
        /// </summary>
        public void Bind(IEventHub eventHub, string characterId)
        {
            Unbind();
            _eventHub = eventHub;
            _characterId = characterId?.Trim() ?? string.Empty;
            _isAudioPlaybackActiveForCharacter = false;
            _isCharacterSpeaking = false;

            if (_eventHub == null || string.IsNullOrWhiteSpace(_characterId)) return;

            _dataToken = _eventHub.Subscribe<LipSyncPackedDataReceived>(OnPackedDataReceived);
            _speechToken = _eventHub.Subscribe<CharacterSpeechStateChanged>(OnSpeechStateChanged);
            _audioPlaybackToken = _eventHub.Subscribe<CharacterAudioPlaybackStateChanged>(OnAudioPlaybackStateChanged);
        }

        public void Unbind()
        {
            if (_eventHub != null)
            {
                if (_dataToken != default)
                {
                    _eventHub.Unsubscribe(_dataToken);
                    _dataToken = default;
                }

                if (_speechToken != default)
                {
                    _eventHub.Unsubscribe(_speechToken);
                    _speechToken = default;
                }

                if (_audioPlaybackToken != default)
                {
                    _eventHub.Unsubscribe(_audioPlaybackToken);
                    _audioPlaybackToken = default;
                }
            }

            _eventHub = null;
            _characterId = string.Empty;
            _isAudioPlaybackActiveForCharacter = false;
            _isCharacterSpeaking = false;
        }

        private void OnAudioPlaybackStateChanged(CharacterAudioPlaybackStateChanged evt)
        {
            if (_disposed || _eventHub == null) return;

            if (!IsForThisCharacter(evt.CharacterId)) return;

            _isAudioPlaybackActiveForCharacter = evt.IsPlaying;
            if (!evt.IsPlaying) return;

            if (!ShouldOpenPlaybackGate())
                return;

            OpenPlaybackGate();
        }

        private void OnPackedDataReceived(LipSyncPackedDataReceived evt)
        {
            if (_disposed || _eventHub == null) return;

            if (!evt.IsValid) return;

            if (!IsForThisCharacter(evt.CharacterId)) return;

            if (evt.ProfileId != _lockedProfile)
            {
                LogDebug($"Dropped packet: profile '{evt.ProfileId}' != locked '{_lockedProfile}'.");
                return;
            }

            LipSyncPackedChunk chunk = evt.Chunk;
            if (chunk.FrameCount <= 0) return;

            PlaybackState currentState = _engine.State;

            if (currentState == PlaybackState.Idle)
            {
                _engine.BeginStream(chunk.ChannelNames, chunk.FrameRate);

                if (ShouldOpenPlaybackGate())
                    OpenPlaybackGate();
            }

            _engine.FeedFrames(chunk.Frames);
        }

        private void OnSpeechStateChanged(CharacterSpeechStateChanged evt)
        {
            if (_disposed || _eventHub == null) return;

            if (!IsForThisCharacter(evt.CharacterId)) return;

            _isCharacterSpeaking = evt.IsSpeaking;
            if (!evt.IsSpeaking)
            {
                _engine.NotifyStreamEnd();
                return;
            }

            if (!ShouldOpenPlaybackGate())
                return;

            OpenPlaybackGate();
        }

        private bool IsForThisCharacter(string eventCharacterId)
        {
            if (string.IsNullOrWhiteSpace(_characterId)) return false;

            if (string.IsNullOrWhiteSpace(eventCharacterId)) return false;

            return string.Equals(_characterId, eventCharacterId, StringComparison.OrdinalIgnoreCase);
        }

        private bool ShouldOpenPlaybackGate()
        {
            if (!_isAudioPlaybackActiveForCharacter)
                return false;

            if (_requiresSpeakingStateToOpenPlaybackGate && !_isCharacterSpeaking)
                return false;

            return true;
        }

        private void OpenPlaybackGate() => _engine.NotifyAudioPlaybackStarted();

        private void LogDebug(string message)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || CONVAI_DEBUG_LOGGING
            if (_logger != null)
            {
                _logger.Debug($"{LogPrefix} {message}", LogCategory.LipSync);
                return;
            }

            ConvaiLogger.Debug($"{LogPrefix} {message}", LogCategory.LipSync);
#endif
        }
    }
}
