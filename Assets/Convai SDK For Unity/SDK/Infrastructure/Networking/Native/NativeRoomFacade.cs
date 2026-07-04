using System;
using System.Collections.Generic;
using System.Linq;
using Convai.Infrastructure.Networking.Transport;
using LiveKit;
using LKRemoteTrack = LiveKit.IRemoteTrack;
using LKConnectionState = LiveKit.Proto.ConnectionState;

#pragma warning disable CS0067

namespace Convai.Infrastructure.Networking.Native
{
    /// <summary>
    ///     Native implementation of <see cref="IRoomFacade" /> wrapping LiveKit.Room.
    /// </summary>
    internal class NativeRoomFacade : IRoomFacade, IDisposable
    {
        #region Constructor

        /// <summary>
        ///     Creates a new native room facade wrapping a LiveKit room.
        /// </summary>
        /// <param name="room">The LiveKit room to wrap.</param>
        public NativeRoomFacade(Room room)
        {
            UnderlyingRoom = room ?? throw new ArgumentNullException(nameof(room));

            // Create local participant wrapper
            if (UnderlyingRoom.LocalParticipant != null)
                _localParticipant = new NativeLocalParticipant(UnderlyingRoom.LocalParticipant);

            // Create remote participant wrappers
            foreach (RemoteParticipant participant in UnderlyingRoom.RemoteParticipants.Values)
            {
                var wrapper = new NativeRemoteParticipant(participant);
                _remoteParticipants[participant.Sid] = wrapper;
            }

            SubscribeToRoomEvents();
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                UnsubscribeFromRoomEvents();
            }
            catch
            {
                // Best-effort cleanup.
            }

            foreach (NativeRemoteParticipant participant in _remoteParticipants.Values)
            {
                try
                {
                    participant.Dispose();
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }

            _remoteParticipants.Clear();
            _remoteParticipantsSnapshot = Array.Empty<IRemoteParticipant>();
            _remoteParticipantsDirty = false;
            _localParticipant = null;

            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private Fields

        private readonly Dictionary<string, NativeRemoteParticipant> _remoteParticipants = new();
        private NativeLocalParticipant _localParticipant;
        private IRemoteParticipant[] _remoteParticipantsSnapshot = Array.Empty<IRemoteParticipant>();
        private bool _remoteParticipantsDirty = true;
        private bool _disposed;

        #endregion

        #region IRoomFacade Properties

        /// <inheritdoc />
        public string Sid => UnderlyingRoom.Sid;

        /// <inheritdoc />
        public string Name => UnderlyingRoom.Name;

        /// <inheritdoc />
        public RoomState State => MapRoomState(UnderlyingRoom.ConnectionState);

        /// <inheritdoc />
        public bool IsConnected => UnderlyingRoom.IsConnected;

        /// <inheritdoc />
        public ILocalParticipant LocalParticipant
        {
            get
            {
                if (_localParticipant == null && UnderlyingRoom.LocalParticipant != null)
                    _localParticipant = new NativeLocalParticipant(UnderlyingRoom.LocalParticipant);
                return _localParticipant;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<IRemoteParticipant> RemoteParticipants
        {
            get
            {
                if (!_remoteParticipantsDirty) return _remoteParticipantsSnapshot;

                int count = _remoteParticipants.Count;
                if (count == 0)
                {
                    _remoteParticipantsSnapshot = Array.Empty<IRemoteParticipant>();
                    _remoteParticipantsDirty = false;
                    return _remoteParticipantsSnapshot;
                }

                var snapshot = new IRemoteParticipant[count];
                int i = 0;
                foreach (NativeRemoteParticipant participant in _remoteParticipants.Values) snapshot[i++] = participant;

                _remoteParticipantsSnapshot = snapshot;
                _remoteParticipantsDirty = false;
                return _remoteParticipantsSnapshot;
            }
        }

        /// <inheritdoc />
        public IEnumerable<IParticipant> AllParticipants
        {
            get
            {
                if (_localParticipant != null) yield return _localParticipant;
                foreach (NativeRemoteParticipant participant in _remoteParticipants.Values) yield return participant;
            }
        }

        /// <inheritdoc />
        public int RemoteParticipantCount => _remoteParticipants.Count;

        #endregion

        #region IRoomFacade Participant Lookup

        /// <inheritdoc />
        public IRemoteParticipant GetParticipantBySid(string sid) =>
            _remoteParticipants.TryGetValue(sid, out NativeRemoteParticipant participant) ? participant : null;

        /// <inheritdoc />
        public IRemoteParticipant GetParticipantByIdentity(string identity) =>
            _remoteParticipants.Values.FirstOrDefault(p => p.Identity == identity);

        /// <inheritdoc />
        public bool TryGetParticipantBySid(string sid, out IRemoteParticipant participant)
        {
            if (_remoteParticipants.TryGetValue(sid, out NativeRemoteParticipant nativeParticipant))
            {
                participant = nativeParticipant;
                return true;
            }

            participant = null;
            return false;
        }

        /// <inheritdoc />
        public bool TryGetParticipantByIdentity(string identity, out IRemoteParticipant participant)
        {
            participant = GetParticipantByIdentity(identity);
            return participant != null;
        }

        #endregion

        #region IRoomFacade Events

        /// <inheritdoc />
        public event Action<IRemoteParticipant> ParticipantJoined;

        /// <inheritdoc />
        public event Action<IRemoteParticipant> ParticipantLeft;

        /// <inheritdoc />
        public event Action<IParticipant, ParticipantMetadata> ParticipantMetadataUpdated;

        /// <inheritdoc />
        public event Action<IRemoteAudioTrack, IRemoteParticipant> AudioTrackSubscribed;

        /// <inheritdoc />
        public event Action<IRemoteAudioTrack, IRemoteParticipant> AudioTrackUnsubscribed;

        /// <inheritdoc />
        public event Action<IRemoteVideoTrack, IRemoteParticipant> VideoTrackSubscribed;

        /// <inheritdoc />
        public event Action<IRemoteVideoTrack, IRemoteParticipant> VideoTrackUnsubscribed;

        /// <inheritdoc />
        public event Action<TrackSubscriptionEventArgs> TrackSubscribed;

        /// <inheritdoc />
        public event Action<TrackSubscriptionEventArgs> TrackUnsubscribed;

        /// <inheritdoc />
        public event Action<RoomState> StateChanged;

        /// <inheritdoc />
        public event Action Reconnecting;

        /// <inheritdoc />
        public event Action Reconnected;

        /// <inheritdoc />
        public event Action<DisconnectReason> Disconnected;

        #endregion

        #region Event Handlers

        private void OnParticipantConnected(Participant participant)
        {
            if (participant is not RemoteParticipant remoteParticipant) return;

            var wrapper = new NativeRemoteParticipant(remoteParticipant);

            if (_remoteParticipants.TryGetValue(remoteParticipant.Sid, out NativeRemoteParticipant existing))
            {
                try
                {
                    existing.Dispose();
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }

            _remoteParticipants[remoteParticipant.Sid] = wrapper;
            _remoteParticipantsDirty = true;
            ParticipantJoined?.Invoke(wrapper);
        }

        private void OnParticipantDisconnected(Participant participant)
        {
            if (participant is not RemoteParticipant remoteParticipant) return;

            if (_remoteParticipants.TryGetValue(remoteParticipant.Sid, out NativeRemoteParticipant wrapper))
            {
                _remoteParticipants.Remove(remoteParticipant.Sid);
                _remoteParticipantsDirty = true;
                ParticipantLeft?.Invoke(wrapper);

                try
                {
                    wrapper.Dispose();
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }
        }

        private void OnTrackSubscribed(LKRemoteTrack track, RemoteTrackPublication publication,
            RemoteParticipant participant)
        {
            if (!_remoteParticipants.TryGetValue(participant.Sid, out NativeRemoteParticipant participantWrapper))
                return;

            if (track is RemoteAudioTrack audioTrack)
            {
                var trackWrapper = new NativeRemoteAudioTrack(audioTrack, participantWrapper);
                participantWrapper.AddSubscribedTrack(trackWrapper);

                var pubInfo = new TrackPublicationInfo(publication.Sid, publication.Name, TrackKind.Audio,
                    publication.Muted, true);
                AudioTrackSubscribed?.Invoke(trackWrapper, participantWrapper);
                TrackSubscribed?.Invoke(new TrackSubscriptionEventArgs(trackWrapper, participantWrapper, pubInfo));
            }
            else if (track is RemoteVideoTrack videoTrack)
            {
                var trackWrapper = new NativeRemoteVideoTrack(videoTrack, participantWrapper);
                participantWrapper.AddSubscribedTrack(trackWrapper);

                var pubInfo = new TrackPublicationInfo(publication.Sid, publication.Name, TrackKind.Video,
                    publication.Muted, true);
                VideoTrackSubscribed?.Invoke(trackWrapper, participantWrapper);
                TrackSubscribed?.Invoke(new TrackSubscriptionEventArgs(trackWrapper, participantWrapper, pubInfo));
            }
        }

        private void OnTrackUnsubscribed(LKRemoteTrack track, RemoteTrackPublication publication,
            RemoteParticipant participant)
        {
            if (!_remoteParticipants.TryGetValue(participant.Sid, out NativeRemoteParticipant participantWrapper))
                return;

            IRemoteTrack trackWrapper = participantWrapper.RemoveSubscribedTrack(track.Sid);
            if (trackWrapper == null) return;

            var pubInfo = new TrackPublicationInfo(publication.Sid, publication.Name,
                track is RemoteAudioTrack ? TrackKind.Audio : TrackKind.Video, publication.Muted);

            if (trackWrapper is IRemoteAudioTrack audioTrack)
                AudioTrackUnsubscribed?.Invoke(audioTrack, participantWrapper);
            else if (trackWrapper is IRemoteVideoTrack videoTrack)
                VideoTrackUnsubscribed?.Invoke(videoTrack, participantWrapper);

            TrackUnsubscribed?.Invoke(new TrackSubscriptionEventArgs(trackWrapper, participantWrapper, pubInfo));
        }

        private void OnReconnecting(Room room)
        {
            StateChanged?.Invoke(RoomState.Reconnecting);
            Reconnecting?.Invoke();
        }

        private void OnReconnected(Room room)
        {
            StateChanged?.Invoke(RoomState.Connected);
            Reconnected?.Invoke();
        }

        private void OnDisconnected(Room room)
        {
            StateChanged?.Invoke(RoomState.Disconnected);
            Disconnected?.Invoke(DisconnectReason.RemoteHangUp);
        }

        #endregion

        #region Event Subscriptions

        private void SubscribeToRoomEvents()
        {
            UnderlyingRoom.ParticipantConnected += OnParticipantConnected;
            UnderlyingRoom.ParticipantDisconnected += OnParticipantDisconnected;
            UnderlyingRoom.TrackSubscribed += OnTrackSubscribed;
            UnderlyingRoom.TrackUnsubscribed += OnTrackUnsubscribed;
            UnderlyingRoom.Reconnecting += OnReconnecting;
            UnderlyingRoom.Reconnected += OnReconnected;
            UnderlyingRoom.Disconnected += OnDisconnected;
        }

        internal void UnsubscribeFromRoomEvents()
        {
            UnderlyingRoom.ParticipantConnected -= OnParticipantConnected;
            UnderlyingRoom.ParticipantDisconnected -= OnParticipantDisconnected;
            UnderlyingRoom.TrackSubscribed -= OnTrackSubscribed;
            UnderlyingRoom.TrackUnsubscribed -= OnTrackUnsubscribed;
            UnderlyingRoom.Reconnecting -= OnReconnecting;
            UnderlyingRoom.Reconnected -= OnReconnected;
            UnderlyingRoom.Disconnected -= OnDisconnected;
        }

        #endregion

        #region Helper Methods

        private static RoomState MapRoomState(LKConnectionState state)
        {
            return state switch
            {
                LKConnectionState.ConnDisconnected => RoomState.Disconnected,
                LKConnectionState.ConnConnected => RoomState.Connected,
                LKConnectionState.ConnReconnecting => RoomState.Reconnecting,
                _ => RoomState.Disconnected
            };
        }

        /// <summary>
        ///     Gets the underlying LiveKit room.
        /// </summary>
        public Room UnderlyingRoom { get; }

        #endregion
    }
}
