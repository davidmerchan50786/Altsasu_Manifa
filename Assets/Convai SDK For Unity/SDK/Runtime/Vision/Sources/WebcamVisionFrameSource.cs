using System;
using System.Threading.Tasks;
using Convai.Domain.Errors;
using Convai.Domain.Logging;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;
using Convai.Runtime.Logging;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Convai.Runtime.Vision.Sources
{
    /// <summary>
    ///     Vision frame source that captures from a physical webcam device.
    ///     Implements <see cref="IVisionFrameSource" /> to integrate with the SDK's video publishing system.
    /// </summary>
    /// <remarks>
    ///     Use this component to stream frames from a physical webcam device.
    ///     <para>
    ///         <b>Setup Instructions:</b>
    ///         <list type="number">
    ///             <item>Add this component to a GameObject</item>
    ///             <item>Add ConvaiVisionPublisher to the same GameObject (or assign in inspector)</item>
    ///             <item>ConvaiVisionPublisher will auto-discover this component and stream the webcam</item>
    ///             <item>Optionally add VisionDebugPreview to show the webcam feed on screen</item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         <b>Platform Notes:</b>
    ///         <list type="bullet">
    ///             <item>Android/iOS: Camera permission must be granted</item>
    ///             <item>WebGL: Not supported (AsyncGPUReadback not available)</item>
    ///             <item>macOS: May require Camera access in System Preferences</item>
    ///         </list>
    ///     </para>
    /// </remarks>
    [MovedFrom(true, "Convai.Runtime.Vision", "Convai.Runtime",
        "WebcamVisionFrameSource")]
    [AddComponentMenu("Convai/Vision/Webcam Vision Frame Source")]
    public class WebcamVisionFrameSource : MonoBehaviour, IVisionFrameSource
    {
        #region Public Properties

        /// <summary>Gets the list of available webcam devices.</summary>
        public static WebCamDevice[] AvailableDevices => WebCamTexture.devices;

        #endregion

        #region Permission Handling

        private async Task<bool> CheckWebcamPermissionAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
                await Task.Delay(500);
                return UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera);
            }
            return true;
#elif UNITY_IOS && !UNITY_EDITOR
            if (!UnityEngine.Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                await UnityEngine.Application.RequestUserAuthorization(UserAuthorization.WebCam);
                return UnityEngine.Application.HasUserAuthorization(UserAuthorization.WebCam);
            }
            return true;
#else
            await Task.CompletedTask;
            return true;
#endif
        }

        #endregion

        #region Inspector Fields

        [Header("Webcam Settings")]
        [Tooltip("Name of the webcam device. Leave empty to use the default camera.")]
        [SerializeField]
        private string _webcamDeviceName = "";

        [Tooltip("Requested capture width. Actual may differ based on device capabilities.")] [SerializeField]
        private int _requestedWidth = 640;

        [Tooltip("Requested capture height. Actual may differ based on device capabilities.")] [SerializeField]
        private int _requestedHeight = 480;

        [Tooltip("Requested frame rate. Actual may differ based on device capabilities.")] [SerializeField]
        private int _requestedFps = 15;

        [Header("Source Identification")] [Tooltip("Unique identifier for this vision source")] [SerializeField]
        private string _sourceId = "webcam";

        #endregion

        #region Private Fields

        private WebCamTexture _webCamTexture;
        private bool _isCapturing;
        private bool _isFrameReady;
        private bool _isInitializing;

        #endregion

        #region Error Properties

        /// <summary>
        ///     Gets the last error code if capture failed.
        /// </summary>
        public string LastErrorCode { get; private set; }

        /// <summary>
        ///     Gets the last error message if capture failed.
        /// </summary>
        public string LastErrorMessage { get; private set; }

        #endregion

        #region IVisionFrameSource Implementation

        /// <inheritdoc />
        public bool IsCapturing => _isCapturing && _webCamTexture != null && _webCamTexture.isPlaying;

        /// <inheritdoc />
        public bool IsFrameReady => _isFrameReady && CurrentRenderTexture != null;

        /// <inheritdoc />
        public long FrameCount { get; private set; }

        /// <inheritdoc />
        public (int Width, int Height) FrameDimensions =>
            CurrentRenderTexture != null
                ? (CurrentRenderTexture.width, CurrentRenderTexture.height)
                : (_requestedWidth, _requestedHeight);

        /// <inheritdoc />
        public float TargetFrameRate => _requestedFps;

        /// <inheritdoc />
        public string SourceId => _sourceId;

        /// <inheritdoc />
        public RenderTexture CurrentRenderTexture { get; private set; }

        /// <inheritdoc />
        public event Action FrameReady;

        /// <inheritdoc />
        public void StartCapture()
        {
            if (_isCapturing || _isInitializing) return;
            _ = StartCaptureAsync();
        }

        /// <inheritdoc />
        public void StopCapture() => StopWebcam();

        #endregion

        #region Unity Lifecycle

        private void OnDestroy() => StopWebcam();

        private void Update()
        {
            if (!IsCapturing || CurrentRenderTexture == null) return;

            BlitWebcamToRenderTexture();
        }

        #endregion

        #region Webcam Management

        /// <summary>
        ///     Starts the webcam capture asynchronously.
        /// </summary>
        private async Task StartCaptureAsync()
        {
            if (_isInitializing) return;
            _isInitializing = true;

            try
            {
                ConvaiLogger.Info("[WebcamVisionFrameSource] Starting webcam capture...", LogCategory.Vision);

                if (!await CheckWebcamPermissionAsync())
                {
                    LastErrorCode = SessionErrorCodes.VisionPermissionDenied;
                    LastErrorMessage = SessionErrorCodes.GetDescription(SessionErrorCodes.VisionPermissionDenied);
                    ConvaiLogger.Error($"[WebcamVisionFrameSource] {LastErrorMessage}", LogCategory.Vision);
                    return;
                }

                if (!InitializeWebcam()) return;

                await WaitForWebcamToStartAsync();

                InitializeRenderTexture();

                FrameCount = 0;
                _isFrameReady = false;
                _isCapturing = true;
                LastErrorCode = null;
                LastErrorMessage = null;
                ConvaiLogger.Info(
                    $"[WebcamVisionFrameSource] Webcam capture started: {FrameDimensions.Width}x{FrameDimensions.Height}",
                    LogCategory.Vision);
            }
            catch (Exception ex)
            {
                LastErrorCode = SessionErrorCodes.VisionUnknown;
                LastErrorMessage = ex.Message;
                ConvaiLogger.Error($"[WebcamVisionFrameSource] Error starting webcam: {ex.Message}",
                    LogCategory.Vision);
                _isCapturing = false;
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private bool InitializeWebcam()
        {
            string deviceName = string.IsNullOrEmpty(_webcamDeviceName) ? null : _webcamDeviceName;

            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                LastErrorCode = SessionErrorCodes.VisionDeviceNotFound;
                LastErrorMessage = SessionErrorCodes.GetDescription(SessionErrorCodes.VisionDeviceNotFound);
                ConvaiLogger.Error($"[WebcamVisionFrameSource] {LastErrorMessage}", LogCategory.Vision);
                return false;
            }

            ConvaiLogger.Info($"[WebcamVisionFrameSource] Found {devices.Length} webcam device(s):",
                LogCategory.Vision);
            foreach (WebCamDevice device in devices)
                ConvaiLogger.Info($"  - {device.name} (Front: {device.isFrontFacing})", LogCategory.Vision);

            _webCamTexture = deviceName != null
                ? new WebCamTexture(deviceName, _requestedWidth, _requestedHeight, _requestedFps)
                : new WebCamTexture(_requestedWidth, _requestedHeight, _requestedFps);

            _webCamTexture.Play();

            ConvaiLogger.Info($"[WebcamVisionFrameSource] Starting webcam: {_webCamTexture.deviceName}",
                LogCategory.Vision);
            return true;
        }

        private async Task WaitForWebcamToStartAsync()
        {
            int waitMs = 0;
            while (!_webCamTexture.didUpdateThisFrame && waitMs < 5000)
            {
                await Task.Delay(100);
                waitMs += 100;
            }

            if (!_webCamTexture.didUpdateThisFrame)
            {
                LastErrorCode = SessionErrorCodes.VisionDeviceInitTimeout;
                LastErrorMessage = SessionErrorCodes.GetDescription(SessionErrorCodes.VisionDeviceInitTimeout);
                ConvaiLogger.Warning($"[WebcamVisionFrameSource] {LastErrorMessage}", LogCategory.Vision);
            }

            ConvaiLogger.Info(
                $"[WebcamVisionFrameSource] Webcam active: {_webCamTexture.width}x{_webCamTexture.height} @ rotation {_webCamTexture.videoRotationAngle}°",
                LogCategory.Vision);
        }

        private void InitializeRenderTexture()
        {
            int outputWidth = _webCamTexture.width;
            int outputHeight = _webCamTexture.height;

            if (_webCamTexture.videoRotationAngle == 90 || _webCamTexture.videoRotationAngle == 270)
            {
                outputWidth = _webCamTexture.height;
                outputHeight = _webCamTexture.width;
            }

            CurrentRenderTexture = new RenderTexture(outputWidth, outputHeight, 24, RenderTextureFormat.ARGB32);
            CurrentRenderTexture.Create();

            ConvaiLogger.Info($"[WebcamVisionFrameSource] RenderTexture created: {outputWidth}x{outputHeight}",
                LogCategory.Vision);
        }

        private void BlitWebcamToRenderTexture()
        {
            if (_webCamTexture == null || !_webCamTexture.didUpdateThisFrame) return;

            FrameCount++;

            float rotation = _webCamTexture.videoRotationAngle;
            bool verticallyMirrored = _webCamTexture.videoVerticallyMirrored;

            bool needsYFlip = !verticallyMirrored;
            var scale = new Vector2(1, needsYFlip ? -1 : 1);
            var offset = new Vector2(0, needsYFlip ? 1 : 0);

            if (Mathf.Approximately(rotation, 0f))
                Graphics.Blit(_webCamTexture, CurrentRenderTexture, scale, offset);
            else
                BlitWithRotation(_webCamTexture, CurrentRenderTexture, rotation, needsYFlip);

            _isFrameReady = true;
            FrameReady?.Invoke();
        }

        private void BlitWithRotation(Texture source, RenderTexture dest, float rotationDegrees, bool flipVertical)
        {
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = dest;

            GL.Clear(true, true, Color.black);

            GL.PushMatrix();
            GL.LoadPixelMatrix(0, dest.width, dest.height, 0);

            float halfWidth = dest.width * 0.5f;
            float halfHeight = dest.height * 0.5f;

            Matrix4x4 translation1 = Matrix4x4.Translate(new Vector3(-halfWidth, -halfHeight, 0));
            Matrix4x4 rotation = Matrix4x4.Rotate(Quaternion.Euler(0, 0, -rotationDegrees));
            Matrix4x4 translation2 = Matrix4x4.Translate(new Vector3(halfWidth, halfHeight, 0));
            Matrix4x4 flip = flipVertical ? Matrix4x4.Scale(new Vector3(1, -1, 1)) : Matrix4x4.identity;

            Matrix4x4 matrix4X4 = translation2 * rotation * translation1 * flip;
            GL.MultMatrix(matrix4X4);

            Graphics.DrawTexture(new Rect(0, 0, dest.width, dest.height), source);

            GL.PopMatrix();
            RenderTexture.active = prevActive;
        }

        /// <summary>
        ///     Stops the webcam capture and releases resources.
        /// </summary>
        private void StopWebcam()
        {
            _isCapturing = false;
            _isFrameReady = false;

            if (_webCamTexture != null)
            {
                _webCamTexture.Stop();
                Destroy(_webCamTexture);
                _webCamTexture = null;
            }

            if (CurrentRenderTexture != null)
            {
                CurrentRenderTexture.Release();
                Destroy(CurrentRenderTexture);
                CurrentRenderTexture = null;
            }

            ConvaiLogger.Info("[WebcamVisionFrameSource] Webcam stopped", LogCategory.Vision);
        }

        #endregion

        #region Public API

        /// <summary>
        ///     Switches to a different webcam device.
        /// </summary>
        /// <param name="deviceName">Name of the webcam device to switch to.</param>
        public IConvaiOperation<Unit> SwitchWebcamAsync(string deviceName) =>
            ConvaiOperation<Unit>.FromTask(SwitchWebcamAsyncCore(deviceName));

        private async Task<Unit> SwitchWebcamAsyncCore(string deviceName)
        {
            bool wasCapturing = _isCapturing;

            StopWebcam();

            _webcamDeviceName = deviceName;

            if (wasCapturing) await StartCaptureAsync();
            return Unit.Value;
        }

        /// <summary>
        ///     Gets available webcam device names for UI dropdown population.
        /// </summary>
        public static string[] GetAvailableDeviceNames()
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            string[] names = new string[devices.Length];
            for (int i = 0; i < devices.Length; i++) names[i] = devices[i].name;
            return names;
        }

        #endregion
    }
}
