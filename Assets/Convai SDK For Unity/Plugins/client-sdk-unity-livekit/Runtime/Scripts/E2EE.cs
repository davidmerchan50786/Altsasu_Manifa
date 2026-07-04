using System.Collections.Generic;
using Google.Protobuf;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Proto;

namespace LiveKit
{
    public enum EncryptionType
    {
        NONE = 0,
        GCM = 1,
        CUSTOM = 2
    }

    public class KeyProviderOptions
    {
        public int FailureTolerance;
        public byte[] RatchetSalt;
        public int RatchetWindowSize;
        public byte[] SharedKey;
    }

    public class E2EEOptions
    {
        public EncryptionType EncryptionType;
        public KeyProviderOptions KeyProviderOptions;

        public E2eeOptions ToProto()
        {
            var proto = new E2eeOptions();

            return proto;
        }
    }

    public class KeyProvider
    {
        public KeyProviderOptions KeyProviderOptions;
        internal FfiHandle RoomHandle;

        public KeyProvider(FfiHandle roomHandle, KeyProviderOptions keyProviderOptions)
        {
            RoomHandle = roomHandle;
            KeyProviderOptions = keyProviderOptions;
        }

        public void SetSharedKey(byte[] key, int keyIndex)
        {
            using FfiRequestWrap<SetSharedKeyRequest> request = FFIBridge.Instance.NewRequest<SetSharedKeyRequest>();
            SetSharedKeyRequest e2ee = request.request;
            e2ee.KeyIndex = keyIndex;
            e2ee.SharedKey = ByteString.CopyFrom(key);

            using FfiResponseWrap response = request.Send();
        }

        public byte[] GetSharedKey(int keyIndex)
        {
            using FfiRequestWrap<GetSharedKeyRequest> request = FFIBridge.Instance.NewRequest<GetSharedKeyRequest>();
            GetSharedKeyRequest e2ee = request.request;
            e2ee.KeyIndex = keyIndex;

            using FfiResponseWrap response = request.Send();
            FfiResponse resp = response;
            return resp.E2Ee.GetSharedKey.Key.ToByteArray();
        }

        public byte[] RatchetSharedKey(int keyIndex)
        {
            using FfiRequestWrap<RatchetSharedKeyRequest> request =
                FFIBridge.Instance.NewRequest<RatchetSharedKeyRequest>();
            RatchetSharedKeyRequest e2ee = request.request;
            e2ee.KeyIndex = keyIndex;

            using FfiResponseWrap response = request.Send();
            FfiResponse resp = response;
            return resp.E2Ee.RatchetSharedKey.NewKey.ToByteArray();
        }

        public void SetKey(string participantIdentity, byte[] key, int keyIndex)
        {
            using FfiRequestWrap<SetKeyRequest> request = FFIBridge.Instance.NewRequest<SetKeyRequest>();
            SetKeyRequest e2ee = request.request;
            e2ee.KeyIndex = keyIndex;
            e2ee.ParticipantIdentity = participantIdentity;

            using FfiResponseWrap response = request.Send();
        }

        public byte[] GetKey(string participantIdentity, int keyIndex)
        {
            using FfiRequestWrap<GetKeyRequest> request = FFIBridge.Instance.NewRequest<GetKeyRequest>();
            GetKeyRequest e2ee = request.request;
            e2ee.KeyIndex = keyIndex;
            e2ee.ParticipantIdentity = participantIdentity;

            using FfiResponseWrap response = request.Send();
            FfiResponse resp = response;
            return resp.E2Ee.GetKey.Key.ToByteArray();
        }

        public byte[] RatchetKey(string participantIdentity, int keyIndex)
        {
            using FfiRequestWrap<RatchetKeyRequest> request = FFIBridge.Instance.NewRequest<RatchetKeyRequest>();
            RatchetKeyRequest e2ee = request.request;
            e2ee.KeyIndex = keyIndex;
            e2ee.ParticipantIdentity = participantIdentity;

            using FfiResponseWrap response = request.Send();
            FfiResponse resp = response;
            return resp.E2Ee.RatchetKey.NewKey.ToByteArray();
        }
    }

    public class FrameCryptor
    {
        public bool Enabled;
        public int KeyIndex;
        public string ParticipantIdentity;
        internal FfiHandle RoomHandle;
        public string TrackSid;

        public FrameCryptor(FfiHandle roomHandle, string identity, string trackSid, bool enabled, int keyIndex)
        {
            RoomHandle = roomHandle;
            ParticipantIdentity = identity;
            TrackSid = trackSid;
            Enabled = enabled;
            KeyIndex = keyIndex;
        }

        public void SetKeyIndex(int keyIndex)
        {
            KeyIndex = keyIndex;
            using FfiRequestWrap<FrameCryptorSetKeyIndexRequest> request =
                FFIBridge.Instance.NewRequest<FrameCryptorSetKeyIndexRequest>();
            FrameCryptorSetKeyIndexRequest e2ee = request.request;
            e2ee.KeyIndex = keyIndex;
            e2ee.ParticipantIdentity = ParticipantIdentity;
            request.Send();
        }

        public void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            using FfiRequestWrap<FrameCryptorSetEnabledRequest> request =
                FFIBridge.Instance.NewRequest<FrameCryptorSetEnabledRequest>();
            FrameCryptorSetEnabledRequest e2ee = request.request;
            e2ee.Enabled = enabled;
            e2ee.ParticipantIdentity = ParticipantIdentity;
            request.Send();
        }
    }

    public class E2EEManager
    {
        public E2EEOptions E2EEOptions;
        public KeyProvider KeyProvider;
        internal FfiHandle RoomHandle;

        public E2EEManager(FfiHandle roomHandle, E2EEOptions e2EEOptions)
        {
            RoomHandle = roomHandle;
            KeyProvider = new KeyProvider(roomHandle, e2EEOptions.KeyProviderOptions);
            E2EEOptions = e2EEOptions;
        }

        public void setEnabled(bool enabled)
        {
            using FfiRequestWrap<E2eeManagerSetEnabledRequest> request =
                FFIBridge.Instance.NewRequest<E2eeManagerSetEnabledRequest>();
            E2eeManagerSetEnabledRequest e2ee = request.request;
            e2ee.Enabled = enabled;
            request.Send();
        }

        public List<FrameCryptor> frameCryptors()
        {
            using FfiRequestWrap<E2eeManagerSetEnabledRequest> request =
                FFIBridge.Instance.NewRequest<E2eeManagerSetEnabledRequest>();
            E2eeManagerSetEnabledRequest e2ee = request.request;

            using FfiResponseWrap response = request.Send();
            FfiResponse resp = response;

            var cryptors = new List<FrameCryptor>();

            foreach (Proto.FrameCryptor c in resp.E2Ee.ManagerGetFrameCryptors.FrameCryptors)
                cryptors.Add(new FrameCryptor(RoomHandle, c.ParticipantIdentity, c.TrackSid, c.Enabled, c.KeyIndex));
            return cryptors;
        }
    }
}
