using System;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking;
using Convai.Runtime.Logging;
using Convai.Runtime.Room;
using Convai.Runtime.Vision.Sources;
using Convai.Runtime.Vision.Transport;
using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Runtime.Vision.Publishing
{
    internal sealed class VisionPublishCoordinator : IDisposable
    {
        private readonly Func<RuntimePlatform> _platformProvider;
        private readonly object _syncRoot = new();

        private IConvaiRoomConnectionService _connectionService;
        private bool _disposed;
        private IEventHub _eventHub;
        private IVisionFrameSource _frameSource;
        private CancellationTokenSource _publishCts;
        private bool _publishingEnabled = true;
        private int _publishVersion;
        private bool _subscribedToFrameSource;
        private bool _subscribedToRoomEvents;
        private IVideoSourceFactory _videoSourceFactory;
        private IVideoTrackManager _videoTrackManager;

        public VisionPublishCoordinator(Func<RuntimePlatform> platformProvider = null)
        {
            _platformProvider = platformProvider != null ? platformProvider : () => UnityEngine.Application.platform;
        }

        public bool IsPublishing => _videoTrackManager?.IsPublishing ?? false;

        public VisionPublishPolicy PublishPolicy { get; private set; } = VisionPublishPolicy.AutoCompatible;

        public int FrameRateOverride { get; private set; }

        public int MaxBitrateOverride { get; private set; }

        public string VideoTrackName { get; private set; } = VideoPublishOptions.Default.TrackName;

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            CancelPendingPublish();
            UnsubscribeFromConnectionEvents();
            UnsubscribeFromFrameSource();

            try
            {
                if (_frameSource != null && _frameSource.IsCapturing)
                    _frameSource.StopCapture();
            }
            catch
            {
                // Best-effort shutdown.
            }

            try
            {
                _videoTrackManager?.Dispose();
            }
            catch
            {
                // Best-effort shutdown.
            }

            _videoTrackManager = null;
        }

        public void ConfigureDependencies(
            IEventHub eventHub,
            IVideoSourceFactory videoSourceFactory,
            IConvaiRoomConnectionService connectionService)
        {
            ThrowIfDisposed();

            _eventHub = eventHub;
            _videoSourceFactory = videoSourceFactory;

            if (ReferenceEquals(_connectionService, connectionService))
                return;

            UnsubscribeFromConnectionEvents();
            _connectionService = connectionService;
            SubscribeToConnectionEvents();
        }

        public void SetFrameSource(IVisionFrameSource frameSource)
        {
            ThrowIfDisposed();

            if (ReferenceEquals(_frameSource, frameSource))
                return;

            UnsubscribeFromFrameSource();
            _frameSource = frameSource;
            SubscribeToFrameSource();
        }

        public void ApplyConfiguration(
            VisionPublishPolicy publishPolicy,
            int frameRateOverride,
            int maxBitrateOverride,
            string videoTrackName)
        {
            ThrowIfDisposed();

            PublishPolicy = publishPolicy;
            FrameRateOverride = frameRateOverride;
            MaxBitrateOverride = maxBitrateOverride;
            VideoTrackName = string.IsNullOrWhiteSpace(videoTrackName)
                ? VideoPublishOptions.Default.TrackName
                : videoTrackName.Trim();
            _publishingEnabled = publishPolicy != VisionPublishPolicy.Manual;
        }

        public void SetPublishPolicy(VisionPublishPolicy publishPolicy)
        {
            ThrowIfDisposed();

            ApplyConfiguration(publishPolicy, FrameRateOverride, MaxBitrateOverride, VideoTrackName);

            if (_publishingEnabled)
                SchedulePublish();
            else
                _ = StopAsync();
        }

        public void SetPublishingEnabled(bool enabled)
        {
            ThrowIfDisposed();

            _publishingEnabled = enabled;
            if (enabled)
                SchedulePublish();
            else
                _ = StopAsync();
        }

        public ValueTask StartAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            SubscribeToConnectionEvents();
            SubscribeToFrameSource();

            if (_publishingEnabled && _connectionService?.IsConnected == true)
                SchedulePublish();

            return default;
        }

        public async ValueTask StopAsync(CancellationToken ct = default)
        {
            if (_disposed)
                return;

            CancelPendingPublish();
            UnsubscribeFromConnectionEvents();
            UnsubscribeFromFrameSource();

            if (_frameSource != null && _frameSource.IsCapturing)
                _frameSource.StopCapture();

            if (_videoTrackManager == null)
                return;

            IVideoTrackManager manager = _videoTrackManager;
            _videoTrackManager = null;

            try
            {
                await manager.UnpublishVideoAsync(ct);
            }
            finally
            {
                manager.Dispose();
            }
        }

        private void SubscribeToConnectionEvents()
        {
            if (_subscribedToRoomEvents || _connectionService == null)
                return;

            _connectionService.Connected += OnConnected;
            _connectionService.OnSessionStateChanged += OnSessionStateChanged;
            _subscribedToRoomEvents = true;
        }

        private void UnsubscribeFromConnectionEvents()
        {
            if (!_subscribedToRoomEvents || _connectionService == null)
                return;

            _connectionService.Connected -= OnConnected;
            _connectionService.OnSessionStateChanged -= OnSessionStateChanged;
            _subscribedToRoomEvents = false;
        }

        private void SubscribeToFrameSource()
        {
            if (_subscribedToFrameSource || _frameSource == null)
                return;

            _frameSource.FrameReady += OnFrameReady;
            _subscribedToFrameSource = true;
        }

        private void UnsubscribeFromFrameSource()
        {
            if (!_subscribedToFrameSource || _frameSource == null)
                return;

            _frameSource.FrameReady -= OnFrameReady;
            _subscribedToFrameSource = false;
        }

        private void OnConnected() => SchedulePublish();

        private void OnSessionStateChanged(SessionStateChanged stateChanged)
        {
            switch (stateChanged.NewState)
            {
                case SessionState.Connected:
                    SchedulePublish();
                    return;
                case SessionState.Connecting:
                case SessionState.Reconnecting:
                    // Keep coordinator active while transport/session is progressing.
                    // Stopping here would unsubscribe before Connected is emitted.
                    return;
                default:
                    _ = StopAsync();
                    return;
            }
        }

        private void OnFrameReady() => SchedulePublish();

        private void SchedulePublish()
        {
            // Vision publishing is only allowed when connection type is Video.
            // This prevents vision components from activating when the room is configured for audio-only mode,
            // respecting the ConvaiRoomManager.EffectiveConnectionType setting.
            if (_disposed || !_publishingEnabled || _connectionService?.IsConnected != true ||
                _videoTrackManager?.IsPublishing == true)
                return;

            // Guard: Only publish video when connection type is explicitly set to Video
            if (_connectionService?.ConnectionType != ConvaiConnectionType.Video)
            {
                ConvaiLogger.Debug(
                    "[VisionPublishCoordinator] Vision publishing blocked: Connection type is Audio. " +
                    "Set Connection Type to Video in ConvaiRoomManager to enable vision.",
                    LogCategory.Vision);
                return;
            }

            lock (_syncRoot)
            {
                if (_publishCts != null)
                    return;

                _publishCts = new CancellationTokenSource();
                _publishVersion++;
                _ = PublishWhenReadyAsync(_publishVersion, _publishCts.Token);
            }
        }

        private async Task PublishWhenReadyAsync(int publishVersion, CancellationToken ct)
        {
            try
            {
                VisionPublishProfile profile = VisionPublishProfileResolver.Resolve(
                    PublishPolicy,
                    FrameRateOverride,
                    MaxBitrateOverride,
                    _platformProvider());

                if (UsesWebGLCanvasPublishPath())
                {
                    await PublishWebGLAsync(profile, ct);
                    return;
                }

                if (_frameSource == null)
                {
                    ConvaiLogger.Error("[VisionPublishCoordinator] Frame source is null; cannot publish video.",
                        LogCategory.Vision);
                    return;
                }

                if (!_frameSource.IsCapturing)
                    _frameSource.StartCapture();

                if (!_frameSource.IsCapturing)
                {
                    ConvaiLogger.Error(
                        "[VisionPublishCoordinator] Frame source failed to start capture; aborting publish.",
                        LogCategory.Vision);
                    return;
                }

                if (!_frameSource.IsFrameReady)
                    await WaitForFrameReadyAsync(ct);

                if (ct.IsCancellationRequested || publishVersion != _publishVersion)
                    return;

                RenderTexture renderTexture = _frameSource.CurrentRenderTexture;
                if (renderTexture == null)
                {
                    ConvaiLogger.Error("[VisionPublishCoordinator] Frame source never produced a RenderTexture.",
                        LogCategory.Vision);
                    return;
                }

                EnsureVideoTrackManager();

                VideoPublishOptions options = profile.ApplyTo(VideoPublishOptions.Default
                    .WithTrackName(VideoTrackName)
                    .WithSource(VideoTrackSource.Camera));

                await _videoTrackManager.PublishVideoAsync(renderTexture, options, ct);
            }
            catch (OperationCanceledException)
            {
                // Expected during teardown or reconfiguration.
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"[VisionPublishCoordinator] Failed to publish video: {ex.Message}",
                    LogCategory.Vision);
            }
            finally
            {
                lock (_syncRoot)
                {
                    _publishCts?.Dispose();
                    _publishCts = null;
                }
            }
        }

        private async Task PublishWebGLAsync(VisionPublishProfile profile, CancellationToken ct)
        {
            if (_videoSourceFactory == null)
                throw new InvalidOperationException("VideoSourceFactory not available.");

            EnsureVideoTrackManager();

            IVideoSource canvasSource = _videoSourceFactory.CreateFromCanvasCapture(VideoTrackName, profile.FrameRate);
            VideoPublishOptions options = profile.ApplyTo(VideoPublishOptions.Default
                .WithTrackName(VideoTrackName)
                .WithSource(VideoTrackSource.ScreenShare));

            await _videoTrackManager.PublishVideoAsync(canvasSource, options, ct);
        }

        private async Task WaitForFrameReadyAsync(CancellationToken ct)
        {
            if (_frameSource == null || _frameSource.IsFrameReady)
                return;

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler()
            {
                if (_frameSource.IsFrameReady)
                    tcs.TrySetResult(true);
            }

            _frameSource.FrameReady += Handler;
            try
            {
                if (_frameSource.IsFrameReady)
                    return;

                using (ct.Register(() => tcs.TrySetCanceled(ct)))
                    await tcs.Task;
            }
            finally
            {
                _frameSource.FrameReady -= Handler;
            }
        }

        private void EnsureVideoTrackManager()
        {
            if (_videoTrackManager != null)
                return;

            if (_connectionService == null)
                throw new InvalidOperationException("Room connection service not available.");

            if (_eventHub == null)
                throw new InvalidOperationException("EventHub not available.");

            if (_videoSourceFactory == null)
                throw new InvalidOperationException("VideoSourceFactory not available.");

            _videoTrackManager = new VideoTrackManager(
                () => _connectionService.CurrentRoom,
                _eventHub,
                null,
                _videoSourceFactory);
        }

        private void CancelPendingPublish()
        {
            lock (_syncRoot)
            {
                _publishVersion++;
                _publishCts?.Cancel();
            }
        }

        private bool UsesWebGLCanvasPublishPath() =>
            _platformProvider() == RuntimePlatform.WebGLPlayer;

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(VisionPublishCoordinator));
        }
    }
}
