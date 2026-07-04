using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking;
using Convai.Runtime.Behaviors;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Runtime.Networking.Media
{
    /// <summary>
    ///     Manages audio track operations for Convai room connections.
    ///     Handles microphone publishing, track subscription, and Character audio routing.
    ///     Uses platform-agnostic abstractions for cross-platform compatibility.
    ///     Implements IAudioTrackManager for dependency injection and mocking.
    /// </summary>
    internal class AudioTrackManager : IAudioTrackManager
    {
        private readonly IAgentRegistry _agentRegistry;
        private readonly bool _allowNullAudioTrackInFactory;
        private readonly Func<string, AudioSource> _audioSourceResolver;
        private readonly IAudioStreamFactory _audioStreamFactory;
        private readonly Dictionary<string, IDisposable> _characterAudioStreams = new();
        private readonly IEventHub _eventHub;
        private readonly ILogger _logger;
        private readonly Dictionary<string, (Action started, Action stopped)> _playbackHandlers = new();

        private readonly Dictionary<string, (IRemoteAudioTrack track, IRemoteParticipant participant)>
            _remoteAudioTrackByParticipantSid = new();

        private readonly SemaphoreSlim _microphoneOperationLock = new(1, 1);
        private readonly IRemotePlayerRegistry _remotePlayerRegistry;
        private readonly Func<IRoomFacade> _roomFacadeProvider;
        private readonly object _syncRoot = new();
        private ILocalAudioTrack _currentAudioTrack;
        private AudioPublishOptions _currentMicrophonePublishOptions;
        private bool _disposed;
        private bool _hasCurrentMicrophonePublishOptions;

        private IMicrophoneSource _microphoneSource;

        /// <summary>
        ///     Initializes a new instance of the <see cref="AudioTrackManager" /> class.
        /// </summary>
        /// <param name="roomFacadeProvider">
        ///     Provider function that returns the current room facade.
        ///     Using a provider allows the room to be recreated between connections while maintaining the same AudioTrackManager.
        /// </param>
        /// <param name="agentRegistry">Registry for Character audio routing.</param>
        /// <param name="logger">Logger for diagnostics.</param>
        /// <param name="audioSourceResolver">Function to resolve AudioSource for a character ID. Required for audio routing.</param>
        /// <param name="remotePlayerRegistry">Optional registry for remote player audio (multiplayer).</param>
        /// <param name="audioStreamFactory">Factory for creating audio streams. Required for character audio routing.</param>
        /// <param name="eventHub">
        ///     Optional event hub to publish CharacterAudioPlaybackStateChanged. If null, playback events are
        ///     not published.
        /// </param>
        public AudioTrackManager(
            Func<IRoomFacade> roomFacadeProvider,
            IAgentRegistry agentRegistry,
            ILogger logger,
            Func<string, AudioSource> audioSourceResolver,
            IRemotePlayerRegistry remotePlayerRegistry = null,
            IAudioStreamFactory audioStreamFactory = null,
            IEventHub eventHub = null)
        {
            _roomFacadeProvider = roomFacadeProvider ?? throw new ArgumentNullException(nameof(roomFacadeProvider));
            _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
            _audioSourceResolver = audioSourceResolver ?? throw new ArgumentNullException(nameof(audioSourceResolver));
            _logger = logger;
            _remotePlayerRegistry = remotePlayerRegistry;
            _audioStreamFactory = audioStreamFactory;
            _allowNullAudioTrackInFactory = audioStreamFactory != null;
            _eventHub = eventHub;
        }

        /// <summary>
        ///     Gets the current room facade instance from the provider.
        /// </summary>
        private IRoomFacade RoomFacade => _roomFacadeProvider();

        /// <summary>
        ///     Raised when the microphone mute state changes.
        /// </summary>
        public event Action<bool> OnMicMuteChanged;

        /// <summary>
        ///     Raised when an audio track is subscribed from a remote participant.
        /// </summary>
        public event Action<IRemoteAudioTrack, IRemoteParticipant> OnAudioTrackSubscribed;

        /// <summary>
        ///     Raised when an audio track is unsubscribed from a remote participant.
        /// </summary>
        public event Action<IRemoteAudioTrack, IRemoteParticipant> OnAudioTrackUnsubscribed;

        /// <summary>
        ///     Gets a value indicating whether the microphone is currently muted.
        /// </summary>
        public bool IsMicMuted { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether the microphone is currently publishing.
        /// </summary>
        public bool IsPublishing => _currentAudioTrack != null;

        /// <summary>
        ///     Clears internal track and microphone references.
        ///     Attempts to stop active microphone capture before dropping references to avoid
        ///     transport-side capture callbacks after room teardown.
        ///     Also clears all remote audio streams to ensure complete cleanup.
        /// </summary>
        public void ClearState()
        {
            IMicrophoneSource sourceToStop = null;

            lock (_syncRoot)
            {
                sourceToStop = _microphoneSource;
                _currentAudioTrack = null;
                _microphoneSource = null;
            }

            if (sourceToStop != null)
            {
                try
                {
                    sourceToStop.StopCapture();
                }
                catch (Exception ex)
                {
                    _logger?.Warning(
                        $"[AudioTrackManager] Failed to stop microphone source during ClearState: {ex.Message}");
                }
            }

            ClearRemoteAudio();

            _logger?.Debug("[AudioTrackManager] State cleared (track, microphone, and remote audio streams reset)");
        }

        /// <summary>
        ///     Publishes a microphone audio track to the room using platform-agnostic types.
        /// </summary>
        /// <param name="microphoneSource">The microphone source abstraction to publish.</param>
        /// <param name="options">Audio publish options.</param>
        /// <returns>A task that completes with true if publishing succeeded; otherwise, false.</returns>
        public async Task<bool> PublishMicrophoneAsync(IMicrophoneSource microphoneSource, AudioPublishOptions options)
        {
            ThrowIfDisposed();

            if (microphoneSource == null) throw new ArgumentNullException(nameof(microphoneSource));

            await _microphoneOperationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                return await PublishMicrophoneCoreAsync(microphoneSource, options).ConfigureAwait(false);
            }
            finally
            {
                _microphoneOperationLock.Release();
            }
        }

        internal async Task<bool> RepublishMicrophoneAsync(IMicrophoneSource microphoneSource,
            AudioPublishOptions? options = null)
        {
            ThrowIfDisposed();

            if (microphoneSource == null) throw new ArgumentNullException(nameof(microphoneSource));

            await _microphoneOperationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                return await PublishMicrophoneCoreAsync(microphoneSource,
                    options ?? GetCurrentMicrophonePublishOptionsOrDefault()).ConfigureAwait(false);
            }
            finally
            {
                _microphoneOperationLock.Release();
            }
        }

        internal bool IsCurrentMicrophoneSource(IMicrophoneSource microphoneSource)
        {
            lock (_syncRoot)
                return ReferenceEquals(_microphoneSource, microphoneSource);
        }

        internal AudioPublishOptions GetCurrentMicrophonePublishOptionsOrDefault()
        {
            lock (_syncRoot)
                return _hasCurrentMicrophonePublishOptions
                    ? _currentMicrophonePublishOptions
                    : AudioPublishOptions.DefaultMicrophone;
        }

        private async Task<bool> PublishMicrophoneCoreAsync(IMicrophoneSource microphoneSource, AudioPublishOptions options)
        {
            ThrowIfDisposed();

            if (microphoneSource == null) throw new ArgumentNullException(nameof(microphoneSource));

            IRoomFacade room = RoomFacade;
            if (room?.LocalParticipant == null)
            {
                _logger?.Error("[AudioTrackManager] PublishMicrophoneAsync aborted: LocalParticipant is null");
                return false;
            }

            try
            {
                await UnpublishMicrophoneCoreAsync(clearPublishOptions: false).ConfigureAwait(false);

                // Delegate to the platform-specific local participant implementation
                ILocalAudioTrack track = await RunOnMainThreadAsync(() =>
                        room.LocalParticipant.PublishAudioTrackAsync(
                            microphoneSource,
                            options,
                            CancellationToken.None))
                    .ConfigureAwait(false);

                if (track == null)
                {
                    _logger?.Error("[AudioTrackManager] PublishMicrophoneAsync failed: track is null");
                    return false;
                }

                lock (_syncRoot)
                {
                    _microphoneSource = microphoneSource;
                    _currentAudioTrack = track;
                    _currentMicrophonePublishOptions = options;
                    _hasCurrentMicrophonePublishOptions = true;

                    if (IsMicMuted && _microphoneSource != null) _microphoneSource.IsMuted = true;
                }

                _logger?.Info("[AudioTrackManager] Microphone published successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.Error($"[AudioTrackManager] Exception in PublishMicrophoneAsync: {ex}");
                return false;
            }
        }

        /// <summary>
        ///     Unpublishes the current microphone audio track.
        /// </summary>
        /// <returns>A task that completes when unpublishing is done.</returns>
        public async Task UnpublishMicrophoneAsync()
        {
            ThrowIfDisposed();

            await _microphoneOperationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await UnpublishMicrophoneCoreAsync(clearPublishOptions: true).ConfigureAwait(false);
            }
            finally
            {
                _microphoneOperationLock.Release();
            }
        }

        private async Task UnpublishMicrophoneCoreAsync(bool clearPublishOptions)
        {
            ThrowIfDisposed();

            ILocalAudioTrack trackToUnpublish;
            IMicrophoneSource sourceToStop;

            lock (_syncRoot)
            {
                trackToUnpublish = _currentAudioTrack;
                sourceToStop = _microphoneSource;
                _currentAudioTrack = null;
                _microphoneSource = null;
                if (clearPublishOptions)
                    _hasCurrentMicrophonePublishOptions = false;
            }

            if (trackToUnpublish != null)
            {
                IRoomFacade room = RoomFacade;
                if (room?.LocalParticipant != null)
                {
                    try
                    {
                        await room.LocalParticipant.UnpublishTrackAsync(trackToUnpublish, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning(
                            $"[AudioTrackManager] Exception in UnpublishMicrophoneAsync while unpublishing track (may be stale): {ex.Message}");
                    }
                }
                else
                {
                    _logger?.Debug(
                        "[AudioTrackManager] UnpublishMicrophoneAsync: clearing stale track reference (room not available)");
                }
            }

            if (sourceToStop != null)
            {
                try
                {
                    await RunOnMainThreadAsync(() => sourceToStop.StopCapture()).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.Error(
                        $"[AudioTrackManager] Exception in UnpublishMicrophoneAsync while stopping microphone: {ex}");
                }
            }
        }

        /// <summary>
        ///     Sets the microphone mute state.
        /// </summary>
        /// <param name="muted">True to mute the microphone; false to unmute.</param>
        public void SetMicMuted(bool muted)
        {
            ThrowIfDisposed();

            bool changed;
            lock (_syncRoot)
            {
                changed = IsMicMuted != muted;
                IsMicMuted = muted;

                if (_microphoneSource != null)
                {
                    try
                    {
                        _microphoneSource.IsMuted = muted;
                        _logger?.Info(
                            $"[AudioTrackManager] Microphone mute state changed: {(muted ? "MUTED" : "UNMUTED")}");
                    }
                    catch (Exception ex)
                    {
                        _logger?.Error($"[AudioTrackManager] SetMicMuted failed to set mute on MicrophoneSource: {ex}");
                    }
                }
                else
                {
                    _logger?.Debug(
                        $"[AudioTrackManager] SetMicMuted called but MicrophoneSource is null (muted={muted})");
                }
            }

            if (changed)
            {
                _logger?.Debug($"[AudioTrackManager] Microphone mute state changed event fired: muted={muted}");
                OnMicMuteChanged?.Invoke(muted);
            }
        }

        /// <summary>
        ///     Toggles the microphone mute state.
        /// </summary>
        public void ToggleMicMute() => SetMicMuted(!IsMicMuted);

        /// <summary>
        ///     Releases all resources used by the <see cref="AudioTrackManager" />.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            ClearRemoteAudio();

            lock (_syncRoot)
            {
                if (_microphoneSource != null)
                {
                    try
                    {
                        _microphoneSource.StopCapture();
                    }
                    catch (Exception ex)
                    {
                        _logger?.Log(LogLevel.Debug,
                            $"[AudioTrackManager] StopCapture failed during Dispose: {ex.Message}");
                    }

                    _microphoneSource = null;
                }

                _currentAudioTrack = null;
                _hasCurrentMicrophonePublishOptions = false;
            }

            _remotePlayerRegistry?.Clear();
            _microphoneOperationLock.Dispose();

            GC.SuppressFinalize(this);
        }

        private static void ConfigureAudioSource(AudioSource source, bool isMuted)
        {
            source.playOnAwake = false;
            source.loop = false;
            source.volume = 1f;
            source.priority = 128;
            source.spatialBlend = 0f;
            source.mute = isMuted;
        }

        private void SubscribePlaybackHandlers(string characterId, IAudioPlaybackStateSource source)
        {
            if (string.IsNullOrEmpty(characterId) || source == null) return;

            UnsubscribePlaybackHandlers(characterId);

            Action startedHandler = () => _eventHub?.Publish(CharacterAudioPlaybackStateChanged.Started(characterId));
            Action stoppedHandler = () => _eventHub?.Publish(CharacterAudioPlaybackStateChanged.Stopped(characterId));
            source.PlaybackStarted += startedHandler;
            source.PlaybackStopped += stoppedHandler;
            _playbackHandlers[characterId] = (startedHandler, stoppedHandler);
        }

        private void UnsubscribePlaybackHandlers(string characterId)
        {
            if (string.IsNullOrEmpty(characterId) ||
                !_playbackHandlers.TryGetValue(characterId, out (Action started, Action stopped) handlers)) return;

            _playbackHandlers.Remove(characterId);
            if (_characterAudioStreams.TryGetValue(characterId, out IDisposable stream) &&
                stream is IAudioPlaybackStateSource source)
            {
                source.PlaybackStarted -= handlers.started;
                source.PlaybackStopped -= handlers.stopped;
            }
        }

        private bool TryResolveCharacter(string participantSid, string participantIdentity,
            out IConvaiCharacterAgent agent)
        {
            if (!string.IsNullOrEmpty(participantSid) &&
                _agentRegistry.TryGetCharacterByParticipantId(participantSid, out agent))
                return true;

            if (!string.IsNullOrEmpty(participantIdentity) &&
                _agentRegistry.TryGetCharacter(participantIdentity, out agent))
                return true;

            IReadOnlyList<IConvaiCharacterAgent> all = _agentRegistry.Characters;
            if (all != null && all.Count > 0)
            {
                agent = all[0];
                return true;
            }

            agent = null;
            return false;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AudioTrackManager));
        }

        /// <summary>
        ///     Runs an action on the Unity main thread and waits for completion.
        ///     Required because Unity microphone APIs must be called from the main thread.
        /// </summary>
        private static Task RunOnMainThreadAsync(Action action)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            UnityScheduler.Instance.ScheduleOnMainThread(() =>
            {
                try
                {
                    action();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task;
        }

        private static Task<T> RunOnMainThreadAsync<T>(Func<Task<T>> action)
        {
            var tcs = new TaskCompletionSource<Task<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
            UnityScheduler.Instance.ScheduleOnMainThread(() =>
            {
                try
                {
                    tcs.TrySetResult(action());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            return AwaitScheduledTaskAsync(tcs.Task);
        }

        private static async Task<T> AwaitScheduledTaskAsync<T>(Task<Task<T>> scheduledTask)
        {
            Task<T> innerTask = await scheduledTask.ConfigureAwait(false);
            return await innerTask.ConfigureAwait(false);
        }

        #region Remote Player Management (Future Multiplayer)

        /// <summary>
        ///     Registers a remote player for audio routing.
        /// </summary>
        /// <param name="participantId">The participant identifier.</param>
        /// <param name="displayName">The display name of the remote player.</param>
        public void RegisterRemotePlayer(string participantId, string displayName)
        {
            ThrowIfDisposed();
            _remotePlayerRegistry?.RegisterPlayer(participantId, displayName);
            _logger?.Debug($"[AudioTrackManager] Registered remote player: {participantId} ({displayName})");
        }

        /// <summary>
        ///     Unregisters a remote player from audio routing.
        /// </summary>
        /// <param name="participantId">The participant identifier.</param>
        public void UnregisterRemotePlayer(string participantId)
        {
            ThrowIfDisposed();
            _remotePlayerRegistry?.UnregisterPlayer(participantId);
            _logger?.Debug($"[AudioTrackManager] Unregistered remote player: {participantId}");
        }

        /// <summary>
        ///     Subscribes to audio from a remote player.
        /// </summary>
        /// <param name="participantId">The participant identifier.</param>
        /// <remarks>
        ///     In the current implementation, track subscription is handled automatically by the transport layer.
        ///     This method is provided for future explicit subscription control.
        /// </remarks>
        public void SubscribeToPlayerAudio(string participantId)
        {
            ThrowIfDisposed();
            _logger?.Debug($"[AudioTrackManager] Subscribe to player audio requested: {participantId}");
        }

        /// <summary>
        ///     Unsubscribes from audio from a remote player.
        /// </summary>
        /// <param name="participantId">The participant identifier.</param>
        /// <remarks>
        ///     In the current implementation, track unsubscription is handled automatically by the transport layer.
        ///     This method is provided for future explicit subscription control.
        /// </remarks>
        public void UnsubscribeFromPlayerAudio(string participantId)
        {
            ThrowIfDisposed();
            _logger?.Debug($"[AudioTrackManager] Unsubscribe from player audio requested: {participantId}");
        }

        #endregion

        #region Character Audio Management

        /// <summary>
        ///     Sets the mute state for a Character's audio output.
        /// </summary>
        /// <param name="characterId">The Character identifier.</param>
        /// <param name="muted">True to mute; false to unmute.</param>
        /// <returns>True if the Character was found and mute state was set; otherwise, false.</returns>
        public bool SetCharacterAudioMuted(string characterId, bool muted)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(characterId))
            {
                _logger?.Debug("[AudioTrackManager] Attempted to set mute on null/empty Character ID");
                return false;
            }

            if (!_agentRegistry.TryGetCharacter(characterId, out IConvaiCharacterAgent _))
            {
                _logger?.Debug(
                    $"[AudioTrackManager] Character '{characterId}' is not registered; cannot update mute state.");
                return false;
            }

            _agentRegistry.SetCharacterMuted(characterId, muted);

            AudioSource audioSource = _audioSourceResolver(characterId);
            if (audioSource != null)
            {
                audioSource.mute = muted;
                _logger?.Info(
                    $"[AudioTrackManager] Character audio mute state changed: characterId={characterId}, muted={muted}");
            }

            return true;
        }

        /// <summary>
        ///     Gets the mute state for a Character's audio output.
        /// </summary>
        /// <param name="characterId">The Character identifier.</param>
        /// <returns>True if the Character's audio is muted; false otherwise.</returns>
        public bool IsCharacterAudioMuted(string characterId)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(characterId)) return false;

            return _agentRegistry.TryGetCharacter(characterId, out IConvaiCharacterAgent _) &&
                   _agentRegistry.IsCharacterMuted(characterId);
        }

        /// <summary>
        ///     Handles the event when a remote audio track is subscribed for a Character participant.
        ///     Uses platform-agnostic abstraction types.
        /// </summary>
        /// <param name="audioTrack">The remote audio track abstraction that was subscribed.</param>
        /// <param name="participantSid">The unique session identifier for the participant.</param>
        /// <param name="participantIdentity">The identity string of the participant (typically the Character ID).</param>
        /// <remarks>
        ///     Call this method when a remote audio track is received for a Character, such as when joining a session or when a
        ///     new track is published.
        /// </remarks>
        public void HandleRemoteAudioTrackSubscribed(IRemoteAudioTrack audioTrack, string participantSid,
            string participantIdentity)
        {
            ThrowIfDisposed();

            bool isDebugEnabled = _logger?.IsEnabled(LogLevel.Debug, LogCategory.Audio) ?? false;

            _logger?.Info(
                $"[AudioTrackManager] Audio track subscription started for participant: {participantIdentity}");

            if (isDebugEnabled)
            {
                _logger.Debug("[AudioTrackManager] HandleRemoteAudioTrackSubscribed called:");
                _logger.Debug($"[AudioTrackManager]   - Participant SID: {participantSid}");
                _logger.Debug($"[AudioTrackManager]   - Participant Identity: {participantIdentity}");
                _logger.Debug(
                    $"[AudioTrackManager]   - AudioTrack: {(audioTrack != null ? $"valid (Name: {audioTrack.Name}, Sid: {audioTrack.Sid})" : "NULL")}");
                _logger.Debug($"[AudioTrackManager]   - Room reference: {(RoomFacade != null ? "valid" : "NULL")}");
            }

            if (!_allowNullAudioTrackInFactory && audioTrack == null)
            {
                _logger?.Warning(
                    "[AudioTrackManager] Remote audio track is null and no custom factory provided. ABORTING.");
                return;
            }

            if (isDebugEnabled)
            {
                IReadOnlyList<IConvaiCharacterAgent> allCharacters = _agentRegistry.Characters;
                _logger.Debug(
                    $"[AudioTrackManager] Character Registry state: {allCharacters.Count} Characters registered");
                foreach (IConvaiCharacterAgent character in allCharacters)
                {
                    AudioSource charAudioSource = _audioSourceResolver(character.CharacterId);
                    _agentRegistry.TryGetParticipantId(character.CharacterId, out string participantIdStr);
                    bool isMuted = _agentRegistry.IsCharacterMuted(character.CharacterId);
                    _logger.Debug(
                        $"[AudioTrackManager]   - CharacterId: {character.CharacterId}, ParticipantId: '{participantIdStr}', HasAudioSource: {charAudioSource != null}, IsMuted: {isMuted}");
                }
            }

            if (!TryResolveCharacter(participantSid, participantIdentity, out IConvaiCharacterAgent agent))
            {
                _logger?.Error(
                    $"[AudioTrackManager] FAILED to resolve Character for incoming audio track. SID: {participantSid}, Identity: {participantIdentity}. Audio will NOT play!");
                return;
            }

            string characterId = agent.CharacterId;
            _logger?.Info(
                $"[AudioTrackManager] Successfully resolved Character: {characterId}");

            AudioSource targetSource = _audioSourceResolver(characterId);
            if (targetSource == null)
            {
                _logger?.Error(
                    $"[AudioTrackManager] Character '{characterId}' does not have an AudioSource assigned. Audio will NOT play!");
                return;
            }

            if (isDebugEnabled)
            {
                _logger.Debug($"[AudioTrackManager] Found AudioSource for Character '{characterId}':");
                _logger.Debug($"[AudioTrackManager]   - AudioSource.enabled: {targetSource.enabled}");
                _logger.Debug($"[AudioTrackManager]   - AudioSource.volume: {targetSource.volume}");
                _logger.Debug($"[AudioTrackManager]   - AudioSource.mute: {targetSource.mute}");
                _logger.Debug($"[AudioTrackManager]   - AudioSource.isPlaying: {targetSource.isPlaying}");
                _logger.Debug(
                    $"[AudioTrackManager] Configuring AudioSource for Character '{characterId}'...");
            }

            bool isMutedState = _agentRegistry.IsCharacterMuted(characterId);
            ConfigureAudioSource(targetSource, isMutedState);

            if (isDebugEnabled)
            {
                _logger.Debug("[AudioTrackManager] AudioSource configured:");
                _logger.Debug($"[AudioTrackManager]   - AudioSource.volume: {targetSource.volume}");
                _logger.Debug($"[AudioTrackManager]   - AudioSource.mute: {targetSource.mute}");
                _logger.Debug($"[AudioTrackManager]   - AudioSource.playOnAwake: {targetSource.playOnAwake}");
                _logger.Debug($"[AudioTrackManager]   - AudioSource.spatialBlend: {targetSource.spatialBlend}");
            }

            _agentRegistry.TryGetParticipantId(characterId, out string currentParticipantId);
            if (!string.IsNullOrEmpty(participantSid) && currentParticipantId != participantSid)
            {
                if (isDebugEnabled)
                {
                    _logger.Debug(
                        $"[AudioTrackManager] Updating ParticipantId from '{currentParticipantId}' to '{participantSid}'");
                }

                _agentRegistry.SetParticipantId(characterId, participantSid);
            }

            if (_characterAudioStreams.TryGetValue(characterId, out IDisposable existingStream))
            {
                if (isDebugEnabled)
                {
                    _logger.Debug(
                        $"[AudioTrackManager] Disposing existing audio stream for Character '{characterId}'");
                }

                UnsubscribePlaybackHandlers(characterId);
                existingStream?.Dispose();
            }

            if (isDebugEnabled) _logger.Debug("[AudioTrackManager] Creating AudioStream with factory...");

            // Use the platform-agnostic audio stream factory
            IDisposable stream = _audioStreamFactory?.Create(audioTrack, targetSource);
            if (stream != null)
            {
                if (stream is IAudioPlaybackStateSource playbackSource)
                    SubscribePlaybackHandlers(characterId, playbackSource);
                else
                {
                    _logger?.Warning(
                        $"[AudioTrackManager] Audio stream does not expose playback-state source: character='{characterId}', streamType='{stream.GetType().Name}'.");
                }

                _characterAudioStreams[characterId] = stream;
                _logger?.Info(
                    $"[AudioTrackManager] AudioStream created successfully for Character '{characterId}'");
            }
            else
            {
                _characterAudioStreams.Remove(characterId);
                _logger?.Error(
                    $"[AudioTrackManager] FAILED! AudioStream factory returned null for Character '{characterId}'. Audio will NOT play!");
            }

            _logger?.Info(
                $"[AudioTrackManager] Audio track subscription completed for participant: {participantIdentity}");

            // Fire abstraction-based event
            if (audioTrack?.Participant != null)
            {
                string mapKey = !string.IsNullOrEmpty(participantSid)
                    ? participantSid
                    : audioTrack.Participant.Sid;

                if (!string.IsNullOrEmpty(mapKey))
                    _remoteAudioTrackByParticipantSid[mapKey] = (audioTrack, audioTrack.Participant);

                OnAudioTrackSubscribed?.Invoke(audioTrack, audioTrack.Participant);
            }
        }

        /// <summary>
        ///     Handles the event when a remote audio track is unsubscribed for a given participant.
        ///     Disposes of the associated audio stream and stops the audio source if present.
        /// </summary>
        /// <param name="participantSid">The unique identifier of the participant whose audio track was unsubscribed.</param>
        public void HandleRemoteAudioTrackUnsubscribed(string participantSid)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(participantSid)) return;

            if (!_agentRegistry.TryGetCharacterByParticipantId(participantSid, out IConvaiCharacterAgent agent))
                return;

            string characterIdStr = agent.CharacterId;
            if (_characterAudioStreams.TryGetValue(characterIdStr, out IDisposable stream))
            {
                UnsubscribePlaybackHandlers(characterIdStr);
                stream?.Dispose();
                _characterAudioStreams.Remove(characterIdStr);
            }

            AudioSource audioSource = _audioSourceResolver(characterIdStr);
            if (audioSource != null)
            {
                audioSource.Stop();
                audioSource.clip = null;
            }

            if (_remoteAudioTrackByParticipantSid.TryGetValue(participantSid,
                    out (IRemoteAudioTrack track, IRemoteParticipant participant) entry))
            {
                _remoteAudioTrackByParticipantSid.Remove(participantSid);
                OnAudioTrackUnsubscribed?.Invoke(entry.track, entry.participant);
            }
        }

        /// <summary>
        ///     Disposes and clears all remote audio streams managed by this instance.
        ///     Call this method to release resources associated with remote audio tracks.
        /// </summary>
        public void ClearRemoteAudio()
        {
            foreach (KeyValuePair<string, IDisposable> entry in _characterAudioStreams)
            {
                UnsubscribePlaybackHandlers(entry.Key);
                entry.Value?.Dispose();
            }

            _characterAudioStreams.Clear();
            _playbackHandlers.Clear();
            _remoteAudioTrackByParticipantSid.Clear();
        }

        #endregion
    }
}
