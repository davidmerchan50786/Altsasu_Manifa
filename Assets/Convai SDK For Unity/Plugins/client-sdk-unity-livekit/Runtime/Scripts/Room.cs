using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Google.Protobuf.Collections;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Proto;

namespace LiveKit
{
    public enum IceTransportType
    {
        TRANSPORT_RELAY = 0,
        TRANSPORT_NOHOST = 1,
        TRANSPORT_ALL = 2
    }

    public enum ContinualGatheringPolicy
    {
        GATHER_ONCE = 0,
        GATHER_CONTINUALLY = 1
    }

    public class IceServer
    {
        public string Password;
        public string[] Urls;
        public string Username;

        public Proto.IceServer ToProto()
        {
            var proto = new Proto.IceServer();
            proto.Username = Username;
            proto.Password = Password;
            proto.Urls.AddRange(Urls);

            return proto;
        }
    }

    public class RTCConfiguration
    {
        private readonly ContinualGatheringPolicy ContinualGatheringPolicy = ContinualGatheringPolicy.GATHER_ONCE;
        private readonly IceTransportType IceTransportType = IceTransportType.TRANSPORT_ALL;
        private IceServer[] IceServers;

        public RtcConfig ToProto()
        {
            var proto = new RtcConfig();

            switch (ContinualGatheringPolicy)
            {
                case ContinualGatheringPolicy.GATHER_ONCE:
                    proto.ContinualGatheringPolicy = Proto.ContinualGatheringPolicy.GatherOnce;
                    break;
                case ContinualGatheringPolicy.GATHER_CONTINUALLY:
                    proto.ContinualGatheringPolicy = Proto.ContinualGatheringPolicy.GatherContinually;
                    break;
            }

            switch (IceTransportType)
            {
                case IceTransportType.TRANSPORT_ALL:
                    proto.IceTransportType = Proto.IceTransportType.TransportAll;
                    break;
                case IceTransportType.TRANSPORT_RELAY:
                    proto.IceTransportType = Proto.IceTransportType.TransportRelay;
                    break;
                case IceTransportType.TRANSPORT_NOHOST:
                    proto.IceTransportType = Proto.IceTransportType.TransportNohost;
                    break;
            }

            foreach (IceServer item in IceServers) proto.IceServers.Add(item.ToProto());

            return proto;
        }
    }

    public class RoomOptions
    {
        public bool AdaptiveStream = true;
        public bool AutoSubscribe = true;
        public bool Dynacast = true;
        public E2EEOptions E2EE = null;
        public uint JoinRetries = 3;
        public RTCConfiguration RtcConfig = null;

        public Proto.RoomOptions ToProto()
        {
            var proto = new Proto.RoomOptions();

            proto.AutoSubscribe = AutoSubscribe;
            proto.Dynacast = Dynacast;
            proto.AdaptiveStream = AdaptiveStream;
            proto.JoinRetries = JoinRetries;
            proto.RtcConfig = RtcConfig?.ToProto();
            proto.Encryption = E2EE?.ToProto();

            return proto;
        }
    }

    public class Room
    {
        public delegate void ConnectionDelegate(Room room);

        public delegate void ConnectionQualityChangeDelegate(ConnectionQuality quality, Participant participant);

        public delegate void ConnectionStateChangeDelegate(ConnectionState connectionState);

        public delegate void DataDelegate(byte[] data, Participant participant, DataPacketKind kind, string topic);

        public delegate void E2EeStateChangedDelegate(Participant participant, EncryptionState state);

        public delegate void LocalPublishDelegate(TrackPublication publication, LocalParticipant participant);

        public delegate void MetaDelegate(string metaData);

        public delegate void MuteDelegate(TrackPublication publication, Participant participant);

        public delegate void ParticipantDelegate(Participant participant);

        public delegate void PublishDelegate(RemoteTrackPublication publication, RemoteParticipant participant);

        public delegate void RemoteParticipantDelegate(RemoteParticipant participant);

        public delegate void SipDtmfDelegate(Participant participant, uint code, string digit);

        public delegate void SpeakersChangeDelegate(List<Participant> speakers);

        public delegate void SubscribeDelegate(IRemoteTrack track, RemoteTrackPublication publication,
            RemoteParticipant participant);

        private readonly Dictionary<string, RemoteParticipant> _participants = new();
        private readonly StreamHandlerRegistry _streamHandlers = new();
        internal FfiHandle RoomHandle;

        public string Sid { get; private set; }
        public string Name { get; private set; }
        public string Metadata { get; private set; }
        public uint NumParticipants { get; private set; }
        public LocalParticipant LocalParticipant { get; private set; }
        public ConnectionState ConnectionState { get; private set; }
        public bool IsConnected => RoomHandle != null && ConnectionState != ConnectionState.ConnDisconnected;
        public E2EEManager E2EEManager { get; internal set; }
        public IReadOnlyDictionary<string, RemoteParticipant> RemoteParticipants => _participants;

        public event ParticipantDelegate ParticipantConnected;
        public event ParticipantDelegate ParticipantDisconnected;
        public event LocalPublishDelegate LocalTrackPublished;
        public event LocalPublishDelegate LocalTrackUnpublished;
        public event PublishDelegate TrackPublished;
        public event PublishDelegate TrackUnpublished;
        public event SubscribeDelegate TrackSubscribed;
        public event SubscribeDelegate TrackUnsubscribed;
        public event MuteDelegate TrackMuted;
        public event MuteDelegate TrackUnmuted;
        public event SpeakersChangeDelegate ActiveSpeakersChanged;
        public event ConnectionQualityChangeDelegate ConnectionQualityChanged;
        public event DataDelegate DataReceived;
        public event SipDtmfDelegate SipDtmfReceived;
        public event ConnectionStateChangeDelegate ConnectionStateChanged;
        public event ConnectionDelegate Connected;
        public event ConnectionDelegate Disconnected;
        public event ConnectionDelegate Reconnecting;
        public event ConnectionDelegate Reconnected;
        public event E2EeStateChangedDelegate E2EeStateChanged;
        public event MetaDelegate RoomMetadataChanged;
        public event ParticipantDelegate ParticipantMetadataChanged;
        public event ParticipantDelegate ParticipantNameChanged;
        public event ParticipantDelegate ParticipantAttributesChanged;

        public ConnectInstruction Connect(string url, string token, RoomOptions options)
        {
            using FfiResponseWrap response = FFIBridge.Instance.SendConnectRequest(url, token, options);
            Utils.Debug("Connect....");
            FfiResponse res = response;
            Utils.Debug($"Connect response.... {response}");
            return new ConnectInstruction(res.Connect.AsyncId, this, options);
        }

        public void Disconnect()
        {
            if (RoomHandle == null)
                return;
            using FfiResponseWrap response = FFIBridge.Instance.SendDisconnectRequest(this);
            Utils.Debug($"Disconnect.... {RoomHandle}");
            FfiResponse resp = response;
            Utils.Debug($"Disconnect response.... {resp}");
        }

        /// <summary>
        ///     Registers a handler for incoming text streams matching the given topic.
        /// </summary>
        /// <param name="topic">
        ///     Topic identifier that filters which streams will be handled.
        ///     Only streams with a matching topic will trigger the handler.
        /// </param>
        /// <param name="handler">
        ///     Handler that is invoked whenever a remote participant
        ///     opens a new stream with the matching topic. The handler receives a
        ///     <see cref="TextStreamReader" /> for consuming the stream data and the identity of
        ///     the remote participant who initiated the stream.
        /// </param>
        /// <throws>Throws a <see cref="StreamError" /> if the topic is already registered.</throws>
        public void RegisterTextStreamHandler(string topic, TextStreamHandler handler) =>
            _streamHandlers.RegisterTextStreamHandler(topic, handler);

        /// <summary>
        ///     Registers a handler for incoming byte streams matching the given topic.
        /// </summary>
        /// <param name="topic">
        ///     Topic identifier that filters which streams will be handled.
        ///     Only streams with a matching topic will trigger the handler.
        /// </param>
        /// <param name="handler">
        ///     Handler that is invoked whenever a remote participant
        ///     opens a new stream with the matching topic. The handler receives a
        ///     <see cref="ByteStreamReader" /> for consuming the stream data and the identity of
        ///     the remote participant who initiated the stream.
        /// </param>
        /// <throws>Throws a <see cref="StreamError" /> if the topic is already registered.</throws>
        public void RegisterByteStreamHandler(string topic, ByteStreamHandler handler) =>
            _streamHandlers.RegisterByteStreamHandler(topic, handler);

        /// <summary>
        ///     Unregisters a handler for incoming text streams matching the given topic.
        /// </summary>
        /// <param name="topic">Topic identifier for which the handler should be unregistered.</param>
        public void UnregisterTextStreamHandler(string topic) => _streamHandlers.UnregisterTextStreamHandler(topic);

        /// <summary>
        ///     Unregisters a handler for incoming byte streams matching the given topic.
        /// </summary>
        /// <param name="topic">Topic identifier for which the handler should be unregistered.</param>
        public void UnregisterByteStreamHandler(string topic) => _streamHandlers.UnregisterByteStreamHandler(topic);

        internal void UpdateFromInfo(RoomInfo info)
        {
            Sid = info.Sid;
            Name = info.Name;
            Metadata = info.Metadata;
            NumParticipants = info.NumParticipants;
        }

        internal void OnRpcMethodInvocationReceived(RpcMethodInvocationEvent e)
        {
            if (e.LocalParticipantHandle == (ulong)LocalParticipant.Handle.DangerousGetHandle())
            {
                LocalParticipant.HandleRpcMethodInvocation(
                    e.InvocationId,
                    e.Method,
                    e.RequestId,
                    e.CallerIdentity,
                    e.Payload,
                    e.ResponseTimeoutMs / 1000f);
            }
        }

        internal void OnEventReceived(RoomEvent e)
        {
            if (e.RoomHandle != (ulong)RoomHandle.DangerousGetHandle())
                return;

            switch (e.MessageCase)
            {
                case RoomEvent.MessageOneofCase.RoomMetadataChanged:
                    {
                        Metadata = e.RoomMetadataChanged.Metadata;
                        RoomMetadataChanged?.Invoke(e.RoomMetadataChanged.Metadata);
                    }
                    break;
                case RoomEvent.MessageOneofCase.ParticipantMetadataChanged:
                    {
                        Participant participant = GetParticipant(e.ParticipantMetadataChanged.ParticipantIdentity);
                        if (participant == null)
                        {
                            Utils.Debug(
                                $"Unable to find participant: {e.ParticipantMetadataChanged.ParticipantIdentity} in Meta data Change Event");
                            return;
                        }

                        participant._info.Metadata = e.ParticipantMetadataChanged.Metadata;
                        ParticipantMetadataChanged?.Invoke(participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.ParticipantNameChanged:
                    {
                        Participant participant = GetParticipant(e.ParticipantNameChanged.ParticipantIdentity);
                        if (participant == null)
                        {
                            Utils.Debug(
                                $"Unable to find participant: {e.ParticipantNameChanged.ParticipantIdentity} in Name Change Event");
                            return;
                        }

                        participant._info.Name = e.ParticipantNameChanged.Name;
                        ParticipantNameChanged?.Invoke(participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.ParticipantAttributesChanged:
                    {
                        Participant participant = GetParticipant(e.ParticipantAttributesChanged.ParticipantIdentity);
                        if (participant == null)
                        {
                            Utils.Debug(
                                $"Unable to find participant: {e.ParticipantAttributesChanged.ParticipantIdentity} in Attributes Change Event");
                            return;
                        }

                        participant._info.Attributes.Clear();
                        foreach (AttributesEntry entry in e.ParticipantAttributesChanged.Attributes)
                            participant._info.Attributes.Add(entry.Key, entry.Value);
                        ParticipantAttributesChanged?.Invoke(participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.ParticipantConnected:
                    {
                        RemoteParticipant participant = CreateRemoteParticipant(e.ParticipantConnected.Info);
                        ParticipantConnected?.Invoke(participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.ParticipantDisconnected:
                    {
                        string sid = e.ParticipantDisconnected.ParticipantIdentity;
                        RemoteParticipant participant = RemoteParticipants[sid];
                        _participants.Remove(sid);
                        ParticipantDisconnected?.Invoke(participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackPublished:
                    {
                        RemoteParticipant participant = RemoteParticipants[e.TrackPublished.ParticipantIdentity];
                        var publication = new RemoteTrackPublication(e.TrackPublished.Publication.Info,
                            FfiHandle.FromOwnedHandle(e.TrackPublished.Publication.Handle));
                        participant._tracks.Add(publication.Sid, publication);
                        participant.OnTrackPublished(publication);
                        TrackPublished?.Invoke(publication, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackUnpublished:
                    {
                        RemoteParticipant participant = RemoteParticipants[e.TrackUnpublished.ParticipantIdentity];
                        RemoteTrackPublication publication = participant.Tracks[e.TrackUnpublished.PublicationSid];
                        participant._tracks.Remove(publication.Sid);
                        participant.OnTrackUnpublished(publication);
                        TrackUnpublished?.Invoke(publication, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackSubscribed:
                    {
                        OwnedTrack track = e.TrackSubscribed.Track;
                        TrackInfo info = track.Info;
                        RemoteParticipant participant = RemoteParticipants[e.TrackSubscribed.ParticipantIdentity];
                        RemoteTrackPublication publication = participant.Tracks[info.Sid];

                        if (publication == null) participant._tracks.Add(publication.Sid, publication);

                        if (info.Kind == TrackKind.KindVideo)
                        {
                            var videoTrack = new RemoteVideoTrack(track, this, participant);
                            publication.UpdateTrack(videoTrack);
                            TrackSubscribed?.Invoke(videoTrack, publication, participant);
                        }
                        else if (info.Kind == TrackKind.KindAudio)
                        {
                            var audioTrack = new RemoteAudioTrack(track, this, participant);
                            publication.UpdateTrack(audioTrack);
                            TrackSubscribed?.Invoke(audioTrack, publication, participant);
                        }
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackUnsubscribed:
                    {
                        RemoteParticipant participant = RemoteParticipants[e.TrackUnsubscribed.ParticipantIdentity];
                        RemoteTrackPublication publication = participant.Tracks[e.TrackUnsubscribed.TrackSid];
                        IRemoteTrack track = publication.Track;
                        publication.UpdateTrack(null);
                        TrackUnsubscribed?.Invoke(track, publication, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.LocalTrackUnpublished:
                    {
                        if (LocalParticipant._tracks.ContainsKey(e.LocalTrackUnpublished.PublicationSid))
                        {
                            TrackPublication publication =
                                LocalParticipant._tracks[e.LocalTrackUnpublished.PublicationSid];
                            LocalTrackUnpublished?.Invoke(publication, LocalParticipant);
                        }
                        else
                            Utils.Debug("Unable to find local track after unpublish: " +
                                        e.LocalTrackPublished.TrackSid);
                    }
                    break;
                case RoomEvent.MessageOneofCase.LocalTrackPublished:
                    {
                        if (LocalParticipant._tracks.ContainsKey(e.LocalTrackPublished.TrackSid))
                        {
                            TrackPublication publication = LocalParticipant._tracks[e.LocalTrackPublished.TrackSid];
                            LocalTrackPublished?.Invoke(publication, LocalParticipant);
                        }
                        else
                            Utils.Debug("Unable to find local track after publish: " + e.LocalTrackPublished.TrackSid);
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackMuted:
                    {
                        Participant participant = GetParticipant(e.TrackMuted.ParticipantIdentity);
                        TrackPublication publication = participant.Tracks[e.TrackMuted.TrackSid];
                        publication.UpdateMuted(true);
                        TrackMuted?.Invoke(publication, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackUnmuted:
                    {
                        Participant participant = GetParticipant(e.TrackUnmuted.ParticipantIdentity);
                        TrackPublication publication = participant.Tracks[e.TrackUnmuted.TrackSid];
                        publication.UpdateMuted(false);
                        TrackUnmuted?.Invoke(publication, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.ActiveSpeakersChanged:
                    {
                        RepeatedField<string> identities = e.ActiveSpeakersChanged.ParticipantIdentities;
                        var speakers = new List<Participant>(identities.Count);

                        foreach (string id in identities)
                            speakers.Add(GetParticipant(id));

                        ActiveSpeakersChanged?.Invoke(speakers);
                    }
                    break;
                case RoomEvent.MessageOneofCase.ConnectionQualityChanged:
                    {
                        Participant participant = GetParticipant(e.ConnectionQualityChanged.ParticipantIdentity);
                        ConnectionQuality quality = e.ConnectionQualityChanged.Quality;
                        participant.ConnectionQuality = quality;
                        ConnectionQualityChanged?.Invoke(quality, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.DataPacketReceived:
                    {
                        DataPacketReceived.ValueOneofCase valueType = e.DataPacketReceived.ValueCase;
                        switch (valueType)
                        {
                            case DataPacketReceived.ValueOneofCase.None:
                                break;
                            case DataPacketReceived.ValueOneofCase.User:
                                {
                                    UserPacket dataInfo = e.DataPacketReceived.User;
                                    byte[] data = new byte[dataInfo.Data.Data.DataLen];
                                    Marshal.Copy((IntPtr)dataInfo.Data.Data.DataPtr, data, 0, data.Length);
#pragma warning disable CS0612
                                    Participant participant = GetParticipant(e.DataPacketReceived.ParticipantIdentity);
#pragma warning restore CS0612
                                    DataReceived?.Invoke(data, participant, e.DataPacketReceived.Kind, dataInfo.Topic);
                                }
                                break;
                            case DataPacketReceived.ValueOneofCase.SipDtmf:
                                {
                                    SipDTMF dtmfInfo = e.DataPacketReceived.SipDtmf;
#pragma warning disable CS0612
                                    Participant participant = GetParticipant(e.DataPacketReceived.ParticipantIdentity);
#pragma warning restore CS0612
                                    SipDtmfReceived?.Invoke(participant, dtmfInfo.Code, dtmfInfo.Digit);
                                }
                                break;
                        }
                    }
                    break;
                case RoomEvent.MessageOneofCase.ByteStreamOpened:
                    var byteReader = new ByteStreamReader(e.ByteStreamOpened.Reader);
                    _streamHandlers.Dispatch(byteReader, e.ByteStreamOpened.ParticipantIdentity);
                    break;
                case RoomEvent.MessageOneofCase.TextStreamOpened:
                    var textReader = new TextStreamReader(e.TextStreamOpened.Reader);
                    _streamHandlers.Dispatch(textReader, e.TextStreamOpened.ParticipantIdentity);
                    break;
                case RoomEvent.MessageOneofCase.ConnectionStateChanged:
                    ConnectionState = e.ConnectionStateChanged.State;
                    ConnectionStateChanged?.Invoke(e.ConnectionStateChanged.State);
                    break;
                case RoomEvent.MessageOneofCase.Disconnected:
                    Disconnected?.Invoke(this);
                    OnDisconnect();
                    break;
                case RoomEvent.MessageOneofCase.Reconnecting:
                    Reconnecting?.Invoke(this);
                    break;
                case RoomEvent.MessageOneofCase.Reconnected:
                    Reconnected?.Invoke(this);
                    break;
                case RoomEvent.MessageOneofCase.E2EeStateChanged:
                    {
                        Participant participant = GetParticipant(e.E2EeStateChanged.ParticipantIdentity);
                        E2EeStateChanged?.Invoke(participant, e.E2EeStateChanged.State);
                    }
                    break;
                case RoomEvent.MessageOneofCase.RoomUpdated:
                    {
                        UpdateFromInfo(e.RoomUpdated);
                    }
                    break;
                case RoomEvent.MessageOneofCase.Moved:
                    {
                        UpdateFromInfo(e.Moved);
                    }
                    break;
            }
        }

        internal void OnConnect(ConnectCallback info)
        {
            RoomHandle = FfiHandle.FromOwnedHandle(info.Result.Room.Handle);

            UpdateFromInfo(info.Result.Room.Info);
            LocalParticipant = new LocalParticipant(info.Result.LocalParticipant, this);

            foreach (ConnectCallback.Types.ParticipantWithTracks p in info.Result.Participants)
                CreateRemoteParticipantWithTracks(p);

            FfiClient.Instance.RoomEventReceived += OnEventReceived;
            FfiClient.Instance.DisconnectReceived += OnDisconnectReceived;
            FfiClient.Instance.RpcMethodInvocationReceived += OnRpcMethodInvocationReceived;

            Connected?.Invoke(this);
        }

        private void OnDisconnectReceived(DisconnectCallback e)
        {
            FfiClient.Instance.DisconnectReceived -= OnDisconnectReceived;
            Utils.Debug($"OnDisconnect.... {e}");
        }

        private void OnDisconnect() => FfiClient.Instance.RoomEventReceived -= OnEventReceived;

        internal RemoteParticipant CreateRemoteParticipantWithTracks(ConnectCallback.Types.ParticipantWithTracks item)
        {
            OwnedParticipant participant = item.Participant;
            RepeatedField<OwnedTrackPublication> publications = item.Publications;
            var newParticipant = new RemoteParticipant(participant, this);
            _participants.Add(participant.Info.Identity, newParticipant);
            foreach (OwnedTrackPublication pub in publications)
            {
                var publication = new RemoteTrackPublication(pub.Info, FfiHandle.FromOwnedHandle(pub.Handle));
                newParticipant._tracks.Add(publication.Sid, publication);
                newParticipant.OnTrackPublished(publication);
            }

            return newParticipant;
        }

        internal RemoteParticipant CreateRemoteParticipant(OwnedParticipant participant)
        {
            var newParticipant = new RemoteParticipant(participant, this);
            _participants.Add(participant.Info.Identity, newParticipant);
            return newParticipant;
        }

        internal Participant GetParticipant(string identity)
        {
            if (identity == LocalParticipant.Identity)
                return LocalParticipant;

            RemoteParticipants.TryGetValue(identity, out RemoteParticipant remoteParticipant);
            return remoteParticipant;
        }
    }

    public sealed class ConnectInstruction : YieldInstruction
    {
        private readonly ulong _asyncId;
        private readonly Room _room;
        private readonly RoomOptions _roomOptions;

        internal ConnectInstruction(ulong asyncId, Room room, RoomOptions options)
        {
            _asyncId = asyncId;
            _room = room;
            _roomOptions = options;
            FfiClient.Instance.ConnectReceived += OnConnect;
        }

        private void OnConnect(ConnectCallback e)
        {
            if (_asyncId != e.AsyncId)
                return;

            FfiClient.Instance.ConnectReceived -= OnConnect;

            bool success = string.IsNullOrEmpty(e.Error);
            if (success)
            {
                if (_roomOptions.E2EE != null) _room.E2EEManager = new E2EEManager(_room.RoomHandle, _roomOptions.E2EE);

                _room.OnConnect(e);
            }

            IsError = !success;
            IsDone = true;
        }
    }
}
