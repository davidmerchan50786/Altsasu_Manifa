using System;
using LiveKit.Proto;
using UnityEngine;
using Google.Protobuf;
using System.Threading;
using LiveKit.Internal.FFIClients;
using LiveKit.Internal.FFIClients.Pools;
using LiveKit.Internal.FFIClients.Pools.Memory;
using UnityEngine.Pool;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LiveKit.Internal
{
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    internal sealed class FfiClient : IFFIClient
    {
        private static bool initialized = false;
        private static readonly Lazy<FfiClient> instance = new(() => new FfiClient());
        public static FfiClient Instance => instance.Value;

        internal SynchronizationContext? _context;

        private static bool _isDisposed = false;
#if UNITY_EDITOR
        private const int EditorInitMaxRetries = 6000;
        private static bool _editorInitRetryScheduled = false;
        private static bool _editorInitGaveUpLogged = false;
        private static int _editorInitRetryCount = 0;
        private static bool _lifecycleHooksRegistered = false;
#endif

        private readonly IObjectPool<FfiResponse> ffiResponsePool;
        private readonly MessageParser<FfiResponse> responseParser;
        private readonly IMemoryPool memoryPool;

        public event PublishTrackDelegate? PublishTrackReceived;
        public event UnpublishTrackDelegate? UnpublishTrackReceived;
        public event ConnectReceivedDelegate? ConnectReceived;
        public event DisconnectReceivedDelegate? DisconnectReceived;
        public event GetSessionStatsDelegate? GetSessionStatsReceived;
        public event RoomEventReceivedDelegate? RoomEventReceived;
        public event TrackEventReceivedDelegate? TrackEventReceived;
        public event RpcMethodInvocationReceivedDelegate? RpcMethodInvocationReceived;
        public event SetLocalMetadataReceivedDelegate? SetLocalMetadataReceived;
        public event SetLocalNameReceivedDelegate? SetLocalNameReceived;
        public event SetLocalAttributesReceivedDelegate? SetLocalAttributesReceived;

        public event VideoStreamEventReceivedDelegate? VideoStreamEventReceived;
        public event AudioStreamEventReceivedDelegate? AudioStreamEventReceived;
        public event CaptureAudioFrameReceivedDelegate? CaptureAudioFrameReceived;

        public event PerformRpcReceivedDelegate? PerformRpcReceived;

        public event ByteStreamReaderEventReceivedDelegate? ByteStreamReaderEventReceived;
        public event ByteStreamReaderReadAllReceivedDelegate? ByteStreamReaderReadAllReceived;
        public event ByteStreamReaderWriteToFileReceivedDelegate? ByteStreamReaderWriteToFileReceived;
        public event ByteStreamOpenReceivedDelegate? ByteStreamOpenReceived;
        public event ByteStreamWriterWriteReceivedDelegate? ByteStreamWriterWriteReceived;
        public event ByteStreamWriterCloseReceivedDelegate? ByteStreamWriterCloseReceived;
        public event SendFileReceivedDelegate? SendFileReceived;
        public event TextStreamReaderEventReceivedDelegate? TextStreamReaderEventReceived;
        public event TextStreamReaderReadAllReceivedDelegate? TextStreamReaderReadAllReceived;
        public event TextStreamOpenReceivedDelegate? TextStreamOpenReceived;
        public event TextStreamWriterWriteReceivedDelegate? TextStreamWriterWriteReceived;
        public event TextStreamWriterCloseReceivedDelegate? TextStreamWriterCloseReceived;
        public event SendTextReceivedDelegate? SendTextReceived;

        public FfiClient() : this(Pools.NewFfiResponsePool(), new ArrayMemoryPool())
        {
        }

        public FfiClient(
            IObjectPool<FfiResponse> ffiResponsePool,
            IMemoryPool memoryPool
        ) : this(
            ffiResponsePool,
            new MessageParser<FfiResponse>(ffiResponsePool.Get), memoryPool)
        {
        }

        public FfiClient(
            IObjectPool<FfiResponse> ffiResponsePool,
            MessageParser<FfiResponse> responseParser,
            IMemoryPool memoryPool
        )
        {
            this.responseParser = responseParser;
            this.memoryPool = memoryPool;
            this.ffiResponsePool = ffiResponsePool;
        }

#if UNITY_EDITOR
        static FfiClient()
        {
            RegisterLifecycleHooks();
        }

        private static void RegisterLifecycleHooks()
        {
            if (_lifecycleHooksRegistered)
                return;

            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
            EditorApplication.quitting += Quit;
            Application.quitting += Quit;
            _lifecycleHooksRegistered = true;
        }

        private static void OnBeforeAssemblyReload()
        {
            Instance.Dispose();
        }

        private static void OnAfterAssemblyReload()
        {
            InitializeSdk();
        }
#else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            Application.quitting += Quit;
            InitializeSdk();
        }
#endif

        private static void Quit()
        {
#if UNITY_EDITOR
            if (_lifecycleHooksRegistered)
            {
                AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
                AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
                EditorApplication.quitting -= Quit;
                Application.quitting -= Quit;
                _lifecycleHooksRegistered = false;
            }
#endif
            Instance.Dispose();

        }

        [RuntimeInitializeOnLoadMethod]
        private static void GetMainContext()
        {
#if UNITY_EDITOR
            RegisterLifecycleHooks();
#endif
            Instance._context = SynchronizationContext.Current;

            if (_isDisposed || !initialized)
            {
                _isDisposed = false;
                InitializeSdk();
            }

            Utils.Debug("Main Context created");
        }

        private static void InitializeSdk()
        {
#if NO_LIVEKIT_MODE
            return;
#endif

            if (initialized)
                return;

#if LK_VERBOSE
            const bool captureLogs = true;
#else
            const bool captureLogs = false;
#endif

            try
            {
                NativeMethods.LiveKitInitialize(FFICallback, captureLogs, "unity", "");
            }
            catch (DllNotFoundException e)
            {
#if UNITY_EDITOR
                _ = e;
                if (_editorInitRetryCount < EditorInitMaxRetries)
                {
                    if (!_editorInitRetryScheduled)
                    {
                        _editorInitRetryScheduled = true;
                        EditorApplication.delayCall += RetryEditorInitializeSdk;
                    }
                    return;
                }

                if (!_editorInitGaveUpLogged)
                {
                    _editorInitGaveUpLogged = true;
                    Debug.LogWarning("[LiveKit] Could not initialize livekit_ffi in editor. Ensure FFI download/import has completed.");
                }
                return;
#else
                Debug.LogError($"[LiveKit] DllNotFoundException: The native library is missing or could not be loaded. {e}");
                throw;
#endif
            }
            catch (EntryPointNotFoundException e)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[LiveKit] Could not bind LiveKit FFI entry points in editor. The downloaded native plugin may be stale or mismatched for this platform/editor architecture. {e.Message}");
                return;
#else
                Debug.LogError($"[LiveKit] EntryPointNotFoundException: The native library was found but is missing expected LiveKit symbols. {e}");
                throw;
#endif
            }

            Utils.Debug("FFIServer - Initialized");
            initialized = true;
#if UNITY_EDITOR
            _editorInitRetryCount = 0;
            _editorInitRetryScheduled = false;
            _editorInitGaveUpLogged = false;
#endif
        }

        public void Initialize()
        {
            InitializeSdk();
        }

        public bool Initialized()
        {
            return initialized;
        }

        public void Dispose()
        {
#if NO_LIVEKIT_MODE
            return;
#endif

            if (_isDisposed && !initialized)
                return;

            // If native initialization never completed, there is no valid FFI session to dispose.
            if (!initialized)
            {
                _isDisposed = true;
                return;
            }

            _isDisposed = true;

            try
            {
                SendRequest(
                    new FfiRequest
                    {
                        Dispose = new DisposeRequest()
                    }
                );
                Utils.Debug("FFIServer - Disposed");
            }
            finally
            {
                initialized = false;
            }
        }

#if UNITY_EDITOR
        private static void RetryEditorInitializeSdk()
        {
            _editorInitRetryScheduled = false;
            if (initialized || _isDisposed || _editorInitRetryCount >= EditorInitMaxRetries)
                return;

            _editorInitRetryCount++;
            InitializeSdk();
        }
#endif

        public void Release(FfiResponse response)
        {
            ffiResponsePool.Release(response);
        }

        public FfiResponse SendRequest(FfiRequest request)
        {
            if (!initialized)
            {
                throw new InvalidOperationException("LiveKit FFI is not initialized.");
            }

            try
            {
                unsafe
                {
                    using var memory = memoryPool.Memory(request);
                    var data = memory.Span();
                    request.WriteTo(data);
                    fixed (byte* requestDataPtr = data)
                    {
                        var handle = NativeMethods.FfiNewRequest(
                            requestDataPtr,
                            data.Length,
                            out byte* dataPtr,
                            out UIntPtr dataLen
                        );
                        var dataSpan = new Span<byte>(dataPtr, (int)dataLen.ToUInt64());
                        var response = responseParser.ParseFrom(dataSpan)!;
                        NativeMethods.FfiDropHandle(handle);
                        return response;
                    }
                }
            }
            catch (EntryPointNotFoundException e)
            {
                initialized = false;
                Utils.Error($"[LiveKit] Missing native FFI entry point while sending request. The native plugin may be stale or incompatible with the current platform. {e}");
                throw new Exception("Cannot send request because the native LiveKit FFI binding is incomplete.", e);
            }
            catch (Exception e)
            {
                Utils.Error(e);
                throw new Exception("Cannot send request", e);
            }
        }

        [AOT.MonoPInvokeCallback(typeof(FFICallbackDelegate))]
        private static unsafe void FFICallback(UIntPtr data, UIntPtr size)
        {
#if NO_LIVEKIT_MODE
            return;
#endif

            if (_isDisposed) return;

            var respData = new Span<byte>(data.ToPointer()!, (int)size.ToUInt64());
            var response = FfiEvent.Parser!.ParseFrom(respData);

            Instance._context?.Post((resp) =>
            {
                var r = resp as FfiEvent;
#if LK_VERBOSE
                if (r?.MessageCase != FfiEvent.MessageOneofCase.Logs)
                    Utils.Debug("Callback: " + r?.MessageCase);
#endif
                switch (r?.MessageCase)
                {
                    case FfiEvent.MessageOneofCase.Logs:
                        break;
                    case FfiEvent.MessageOneofCase.PublishData:
                        break;
                    case FfiEvent.MessageOneofCase.Connect:
                        Instance.ConnectReceived?.Invoke(r.Connect!);
                        break;
                    case FfiEvent.MessageOneofCase.PublishTrack:
                        Instance.PublishTrackReceived?.Invoke(r.PublishTrack!);
                        break;
                    case FfiEvent.MessageOneofCase.UnpublishTrack:
                        Instance.UnpublishTrackReceived?.Invoke(r.UnpublishTrack!);
                        break;
                    case FfiEvent.MessageOneofCase.RoomEvent:
                        Instance.RoomEventReceived?.Invoke(r.RoomEvent);
                        break;
                    case FfiEvent.MessageOneofCase.SetLocalName:
                        Instance.SetLocalNameReceived?.Invoke(r.SetLocalName!);
                        break;
                    case FfiEvent.MessageOneofCase.SetLocalMetadata:
                        Instance.SetLocalMetadataReceived?.Invoke(r.SetLocalMetadata!);
                        break;
                    case FfiEvent.MessageOneofCase.SetLocalAttributes:
                        Instance.SetLocalAttributesReceived?.Invoke(r.SetLocalAttributes!);
                        break;
                    case FfiEvent.MessageOneofCase.TrackEvent:
                        Instance.TrackEventReceived?.Invoke(r.TrackEvent!);
                        break;
                    case FfiEvent.MessageOneofCase.RpcMethodInvocation:
                        Instance.RpcMethodInvocationReceived?.Invoke(r.RpcMethodInvocation);
                        break;
                    case FfiEvent.MessageOneofCase.Disconnect:
                        Instance.DisconnectReceived?.Invoke(r.Disconnect!);
                        break;
                    case FfiEvent.MessageOneofCase.GetStats:
                        Instance.GetSessionStatsReceived?.Invoke(r.GetStats);
                        break;
                    case FfiEvent.MessageOneofCase.PublishTranscription:
                        break;
                    case FfiEvent.MessageOneofCase.VideoStreamEvent:
                        Instance.VideoStreamEventReceived?.Invoke(r.VideoStreamEvent!);
                        break;
                    case FfiEvent.MessageOneofCase.AudioStreamEvent:
                        Instance.AudioStreamEventReceived?.Invoke(r.AudioStreamEvent!);
                        break;
                    case FfiEvent.MessageOneofCase.CaptureAudioFrame:
                         Instance.CaptureAudioFrameReceived?.Invoke(r.CaptureAudioFrame!);
                        break;
                    case FfiEvent.MessageOneofCase.PerformRpc:
                        Instance.PerformRpcReceived?.Invoke(r.PerformRpc!);
                        break;
                    case FfiEvent.MessageOneofCase.ByteStreamReaderEvent:
                        Instance.ByteStreamReaderEventReceived?.Invoke(r.ByteStreamReaderEvent!);
                        break;
                    case FfiEvent.MessageOneofCase.ByteStreamReaderReadAll:
                        Instance.ByteStreamReaderReadAllReceived?.Invoke(r.ByteStreamReaderReadAll!);
                        break;
                    case FfiEvent.MessageOneofCase.ByteStreamReaderWriteToFile:
                        Instance.ByteStreamReaderWriteToFileReceived?.Invoke(r.ByteStreamReaderWriteToFile!);
                        break;
                    case FfiEvent.MessageOneofCase.ByteStreamOpen:
                        Instance.ByteStreamOpenReceived?.Invoke(r.ByteStreamOpen!);
                        break;
                    case FfiEvent.MessageOneofCase.ByteStreamWriterWrite:
                        Instance.ByteStreamWriterWriteReceived?.Invoke(r.ByteStreamWriterWrite!);
                        break;
                    case FfiEvent.MessageOneofCase.ByteStreamWriterClose:
                        Instance.ByteStreamWriterCloseReceived?.Invoke(r.ByteStreamWriterClose!);
                        break;
                    case FfiEvent.MessageOneofCase.SendFile:
                        Instance.SendFileReceived?.Invoke(r.SendFile!);
                        break;
                    case FfiEvent.MessageOneofCase.TextStreamReaderEvent:
                        Instance.TextStreamReaderEventReceived?.Invoke(r.TextStreamReaderEvent!);
                        break;
                    case FfiEvent.MessageOneofCase.TextStreamReaderReadAll:
                        Instance.TextStreamReaderReadAllReceived?.Invoke(r.TextStreamReaderReadAll!);
                        break;
                    case FfiEvent.MessageOneofCase.TextStreamOpen:
                        Instance.TextStreamOpenReceived?.Invoke(r.TextStreamOpen!);
                        break;
                    case FfiEvent.MessageOneofCase.TextStreamWriterWrite:
                        Instance.TextStreamWriterWriteReceived?.Invoke(r.TextStreamWriterWrite!);
                        break;
                    case FfiEvent.MessageOneofCase.TextStreamWriterClose:
                        Instance.TextStreamWriterCloseReceived?.Invoke(r.TextStreamWriterClose!);
                        break;
                    case FfiEvent.MessageOneofCase.SendText:
                        Instance.SendTextReceived?.Invoke(r.SendText!);
                        break;
                    case FfiEvent.MessageOneofCase.Panic:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException($"Unknown message type: {r?.MessageCase.ToString() ?? "null"}");
                }
            }, response);
        }
    }
}
