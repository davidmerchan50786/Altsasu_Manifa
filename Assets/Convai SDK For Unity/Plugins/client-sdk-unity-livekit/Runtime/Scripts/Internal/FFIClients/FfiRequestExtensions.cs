using System;
using System.Runtime.CompilerServices;
using LiveKit.Proto;

namespace LiveKit.Internal.FFIClients
{
    public static class FfiRequestExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Inject<T>(this FfiRequest ffiRequest, T request)
        {
            switch (request)
            {
                case DisposeRequest disposeRequest:
                    ffiRequest.Dispose = disposeRequest;
                    break;
                case ConnectRequest connectRequest:
                    ffiRequest.Connect = connectRequest;
                    break;
                case DisconnectRequest disconnectRequest:
                    ffiRequest.Disconnect = disconnectRequest;
                    break;
                case PublishTrackRequest publishTrackRequest:
                    ffiRequest.PublishTrack = publishTrackRequest;
                    break;
                case UnpublishTrackRequest unpublishTrackRequest:
                    ffiRequest.UnpublishTrack = unpublishTrackRequest;
                    break;
                case PublishDataRequest publishDataRequest:
                    ffiRequest.PublishData = publishDataRequest;
                    break;
                case SetSubscribedRequest setSubscribedRequest:
                    ffiRequest.SetSubscribed = setSubscribedRequest;
                    break;
                case SetLocalMetadataRequest updateLocalMetadataRequest:
                    ffiRequest.SetLocalMetadata = updateLocalMetadataRequest;
                    break;
                case SetLocalNameRequest updateLocalNameRequest:
                    ffiRequest.SetLocalName = updateLocalNameRequest;
                    break;
                case SetLocalAttributesRequest setLocalAttributesRequest:
                    ffiRequest.SetLocalAttributes = setLocalAttributesRequest;
                    break;
                case GetSessionStatsRequest getSessionStatsRequest:
                    ffiRequest.GetSessionStats = getSessionStatsRequest;
                    break;
                case CreateVideoTrackRequest createVideoTrackRequest:
                    ffiRequest.CreateVideoTrack = createVideoTrackRequest;
                    break;
                case CreateAudioTrackRequest createAudioTrackRequest:
                    ffiRequest.CreateAudioTrack = createAudioTrackRequest;
                    break;
                case GetStatsRequest getStatsRequest:
                    ffiRequest.GetStats = getStatsRequest;
                    break;
                case NewVideoStreamRequest newVideoStreamRequest:
                    ffiRequest.NewVideoStream = newVideoStreamRequest;
                    break;
                case NewVideoSourceRequest newVideoSourceRequest:
                    ffiRequest.NewVideoSource = newVideoSourceRequest;
                    break;
                case CaptureVideoFrameRequest captureVideoFrameRequest:
                    ffiRequest.CaptureVideoFrame = captureVideoFrameRequest;
                    break;
                case VideoConvertRequest videoConvertRequest:
                    ffiRequest.VideoConvert = videoConvertRequest;
                    break;
                case NewAudioStreamRequest wewAudioStreamRequest:
                    ffiRequest.NewAudioStream = wewAudioStreamRequest;
                    break;
                case NewAudioSourceRequest newAudioSourceRequest:
                    ffiRequest.NewAudioSource = newAudioSourceRequest;
                    break;
                case CaptureAudioFrameRequest captureAudioFrameRequest:
                    ffiRequest.CaptureAudioFrame = captureAudioFrameRequest;
                    break;
                case NewAudioResamplerRequest newAudioResamplerRequest:
                    ffiRequest.NewAudioResampler = newAudioResamplerRequest;
                    break;
                case RemixAndResampleRequest remixAndResampleRequest:
                    ffiRequest.RemixAndResample = remixAndResampleRequest;
                    break;
                case LocalTrackMuteRequest localTrackMuteRequest:
                    ffiRequest.LocalTrackMute = localTrackMuteRequest;
                    break;
                case E2eeRequest e2EeRequest:
                    ffiRequest.E2Ee = e2EeRequest;
                    break;
                case NewApmRequest newApmRequest:
                    ffiRequest.NewApm = newApmRequest;
                    break;
                case ApmProcessStreamRequest apmProcessStreamRequest:
                    ffiRequest.ApmProcessStream = apmProcessStreamRequest;
                    break;
                case ApmProcessReverseStreamRequest apmProcessReverseStreamRequest:
                    ffiRequest.ApmProcessReverseStream = apmProcessReverseStreamRequest;
                    break;
                case ApmSetStreamDelayRequest apmSetStreamDelayRequest:
                    ffiRequest.ApmSetStreamDelay = apmSetStreamDelayRequest;
                    break;
                case RegisterRpcMethodRequest registerRpcMethodRequest:
                    ffiRequest.RegisterRpcMethod = registerRpcMethodRequest;
                    break;
                case UnregisterRpcMethodRequest unregisterRpcMethodRequest:
                    ffiRequest.UnregisterRpcMethod = unregisterRpcMethodRequest;
                    break;
                case PerformRpcRequest performRpcRequest:
                    ffiRequest.PerformRpc = performRpcRequest;
                    break;
                case RpcMethodInvocationResponseRequest rpcMethodInvocationResponseRequest:
                    ffiRequest.RpcMethodInvocationResponse = rpcMethodInvocationResponseRequest;
                    break;
                case TextStreamReaderReadIncrementalRequest textStreamReaderReadIncrementalRequest:
                    ffiRequest.TextReadIncremental = textStreamReaderReadIncrementalRequest;
                    break;
                case TextStreamReaderReadAllRequest textStreamReaderReadAllRequest:
                    ffiRequest.TextReadAll = textStreamReaderReadAllRequest;
                    break;
                case ByteStreamReaderReadIncrementalRequest byteStreamReaderReadIncrementalRequest:
                    ffiRequest.ByteReadIncremental = byteStreamReaderReadIncrementalRequest;
                    break;
                case ByteStreamReaderReadAllRequest byteStreamReaderReadAllRequest:
                    ffiRequest.ByteReadAll = byteStreamReaderReadAllRequest;
                    break;
                case ByteStreamReaderWriteToFileRequest byteStreamReaderWriteToFileRequest:
                    ffiRequest.ByteWriteToFile = byteStreamReaderWriteToFileRequest;
                    break;
                case StreamSendFileRequest streamSendFileRequest:
                    ffiRequest.SendFile = streamSendFileRequest;
                    break;
                case StreamSendTextRequest streamSendTextRequest:
                    ffiRequest.SendText = streamSendTextRequest;
                    break;
                case ByteStreamOpenRequest byteStreamOpenRequest:
                    ffiRequest.ByteStreamOpen = byteStreamOpenRequest;
                    break;
                case ByteStreamWriterWriteRequest byteStreamWriterWriteRequest:
                    ffiRequest.ByteStreamWrite = byteStreamWriterWriteRequest;
                    break;
                case ByteStreamWriterCloseRequest byteStreamWriterCloseRequest:
                    ffiRequest.ByteStreamClose = byteStreamWriterCloseRequest;
                    break;
                case TextStreamOpenRequest textStreamOpenRequest:
                    ffiRequest.TextStreamOpen = textStreamOpenRequest;
                    break;
                case TextStreamWriterWriteRequest textStreamWriterWriteRequest:
                    ffiRequest.TextStreamWrite = textStreamWriterWriteRequest;
                    break;
                case TextStreamWriterCloseRequest textStreamWriterCloseRequest:
                    ffiRequest.TextStreamClose = textStreamWriterCloseRequest;
                    break;
                case SetRemoteTrackPublicationQualityRequest setRemoteTrackPublicationQualityRequest:
                    ffiRequest.SetRemoteTrackPublicationQuality = setRemoteTrackPublicationQualityRequest;
                    break;
                default:
                    throw new Exception($"Unknown request type: {request?.GetType().FullName ?? "null"}");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureClean(this FfiRequest request)
        {

            if (
                request.Dispose != null
                ||

                request.Connect != null
                || request.Disconnect != null
                || request.PublishTrack != null
                || request.UnpublishTrack != null
                || request.PublishData != null
                || request.SetSubscribed != null
                || request.SetLocalMetadata != null
                || request.SetLocalName != null
                || request.SetLocalAttributes != null
                || request.GetSessionStats != null
                ||

                request.CreateVideoTrack != null
                || request.CreateAudioTrack != null
                || request.GetStats != null
                ||

                request.NewVideoStream != null
                || request.NewVideoSource != null
                || request.CaptureVideoFrame != null
                || request.VideoConvert != null
                ||

                request.NewAudioStream != null
                || request.NewAudioSource != null
                || request.CaptureAudioFrame != null
                || request.NewApm != null
                || request.ApmProcessStream != null
                || request.ApmProcessReverseStream != null
                || request.ApmSetStreamDelay != null
                || request.NewAudioResampler != null
                || request.RemixAndResample != null
                || request.E2Ee != null
                ||

                request.RegisterRpcMethod != null
                || request.UnregisterRpcMethod != null
                || request.PerformRpc != null
                || request.RpcMethodInvocationResponse != null
            )
            {
                throw new InvalidOperationException("Request is not cleared");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureClean(this FfiResponse response)
        {

            if (
                response.Dispose != null
                ||

                response.Connect != null
                || response.Disconnect != null
                || response.PublishTrack != null
                || response.UnpublishTrack != null
                || response.PublishData != null
                || response.SetSubscribed != null
                || response.SetLocalMetadata != null
                || response.SetLocalName != null
                || response.SetLocalAttributes != null
                || response.GetSessionStats != null
                ||

                response.CreateVideoTrack != null
                || response.CreateAudioTrack != null
                || response.GetStats != null
                ||

                response.NewVideoStream != null
                || response.NewVideoSource != null
                || response.CaptureVideoFrame != null
                || response.VideoConvert != null
                ||

                response.NewAudioStream != null
                || response.NewAudioSource != null
                || response.CaptureAudioFrame != null
                || response.NewApm != null
                || response.ApmProcessStream != null
                || response.ApmProcessReverseStream != null
                || response.ApmSetStreamDelay != null
                || response.NewAudioResampler != null
                || response.RemixAndResample != null
                || response.E2Ee != null
                ||

                response.RegisterRpcMethod != null
                || response.UnregisterRpcMethod != null
                || response.PerformRpc != null
                || response.RpcMethodInvocationResponse != null
            )
            {
                throw new InvalidOperationException("Response is not cleared: ");
            }
        }
    }
}
