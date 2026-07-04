using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using Convai.Runtime.Vision.Sources;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.UI;

namespace Convai.Runtime.Vision.Debug
{
    /// <summary>
    ///     Debug preview component for vision capture.
    ///     In the Unity Editor it displays captured frames and statistics for debugging.
    ///     In player builds it disables itself (so it is safe to leave on prefabs/scenes).
    /// </summary>
    /// <remarks>
    ///     Use this to debug "What does the AI actually see?" during development.
    ///     Features:
    ///     - Displays captured RenderTexture as an overlay
    ///     - Shows capture statistics (FPS, resolution, frame count)
    ///     - Configurable corner position and offset
    ///     - Works with any <see cref="IVisionFrameSource" /> (CameraVisionFrameSource, WebcamVisionFrameSource, etc.)
    ///     - Maintains correct aspect ratio (16:9 by default, or matches frame source)
    /// </remarks>
    [MovedFrom(true, "Convai.Runtime.Vision", "Convai.Runtime",
        "VisionDebugPreview")]
    [AddComponentMenu("Convai/Vision/Vision Debug Preview (Editor Only)")]
    public class VisionDebugPreview : MonoBehaviour
    {
        #region Constants

        /// <summary>Default aspect ratio (16:9) for preview overlay.</summary>
        private const float DefaultAspectRatio = 16f / 9f;

        #endregion

        #region Inspector Fields

#pragma warning disable CS0649
        [HideInInspector]
        [Tooltip("RawImage to display the captured texture. If not set, creates an overlay.")]
        [SerializeField]
        private RawImage _previewImage;
#pragma warning restore CS0649

        [Tooltip("Vision frame source to preview. If not set, finds any IVisionFrameSource in scene.")] [SerializeField]
        private MonoBehaviour _frameSourceComponent;

        [Header("Overlay Layout")] [Tooltip("Corner position for the overlay preview.")] [SerializeField]
        private PreviewPosition _overlayPosition = PreviewPosition.BottomRight;

        [Tooltip("Width of the overlay preview in pixels. Height is calculated based on aspect ratio.")]
        [SerializeField]
        [Range(160, 640)]
        private int _overlayWidth = 320;

        [Tooltip("Horizontal offset from the selected corner in pixels.")] [SerializeField] [Range(0, 200)]
        private int _offsetX = 10;

        [Tooltip("Vertical offset from the selected corner in pixels.")] [SerializeField] [Range(0, 200)]
        private int _offsetY = 10;

        [Header("Aspect Ratio")]
        [Tooltip(
            "When enabled, uses the frame source's aspect ratio. When disabled, uses the custom aspect ratio below.")]
        [SerializeField]
        private bool _useSourceAspectRatio = true;

        [Tooltip("Custom aspect ratio (width / height). Only used when 'Use Source Aspect Ratio' is disabled.")]
        [SerializeField]
        [Range(1f, 3f)]
        private float _customAspectRatio = DefaultAspectRatio;

        #endregion

        #region Private Fields

        private IVisionFrameSource _frameSource;

        private long _lastKnownFrameCount;

        private const float FpsUpdateInterval = 0.5f;
        private long _fpsFrameCountStart;
        private float _fpsTimeStart;

        private GUIStyle _statsStyle;
        private bool _styleInitialized;

        #endregion

        #region Public Properties

        /// <summary>Gets or sets whether the preview is visible.</summary>
        [field: Header("Preview Settings")]
        [field: Tooltip("Enable or disable the debug preview.")]
        [field: SerializeField]
        public bool ShowPreview { get; set; } = true;

        /// <summary>Gets or sets whether statistics are shown.</summary>
        [field: Header("Statistics")]
        [field: Tooltip("Show capture statistics overlay.")]
        [field: SerializeField]
        public bool ShowStats { get; set; } = true;

        /// <summary>Gets the current capture FPS.</summary>
        public float CurrentFps { get; private set; }

        /// <summary>Gets the total frame count from the frame source.</summary>
        public long FrameCount => _frameSource?.FrameCount ?? 0;

        /// <summary>Gets whether capture is currently active.</summary>
        public bool IsCapturing => _frameSource?.IsCapturing ?? false;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
#if !UNITY_EDITOR
            enabled = false;
            return;
#else
            ResolveFrameSource();
#endif
        }

        private void OnEnable()
        {
#if !UNITY_EDITOR
            return;
#else
            SubscribeToFrameSource();
            RefreshPreviewTexture();
#endif
        }

        private void Update()
        {
#if !UNITY_EDITOR
            return;
#else
            if (_frameSource == null) return;

            UpdateFpsCalculation();
#endif
        }

        private void OnDisable()
        {
#if !UNITY_EDITOR
            return;
#else
            UnsubscribeFromFrameSource();
#endif
        }

        private void OnGUI()
        {
#if !UNITY_EDITOR
            return;
#else
            if (!ShowPreview && !ShowStats) return;

            InitializeStyles();

            if (ShowPreview && _previewImage == null && _frameSource != null) DrawOverlayPreview();

            if (ShowStats) DrawStatistics();
#endif
        }

        #endregion

        #region Private Methods

        private void ResolveFrameSource()
        {
            if (_frameSourceComponent != null)
            {
                _frameSource = _frameSourceComponent as IVisionFrameSource;
                if (_frameSource == null)
                {
                    ConvaiLogger.Warning(
                        $"[VisionDebugPreview] Assigned component '{_frameSourceComponent.GetType().Name}' does not implement IVisionFrameSource",
                        LogCategory.Vision);
                }
            }

            if (_frameSource == null)
            {
                var vcs = FindFirstObjectByType<CameraVisionFrameSource>();
                if (vcs != null)
                {
                    _frameSource = vcs;
                    _frameSourceComponent = vcs;
                }
                else if (TryFindFirstFrameSource(out IVisionFrameSource discoveredSource))
                {
                    _frameSource = discoveredSource;
                    _frameSourceComponent = discoveredSource as MonoBehaviour;
                }
            }

            if (_frameSource != null)
            {
                ConvaiLogger.Info($"[VisionDebugPreview] Using frame source: {_frameSourceComponent.GetType().Name}",
                    LogCategory.Vision);
            }
        }

        private void SubscribeToFrameSource()
        {
            if (_frameSource == null)
                return;

            _frameSource.FrameReady -= HandleFrameReady;
            _frameSource.FrameReady += HandleFrameReady;
        }

        private void UnsubscribeFromFrameSource()
        {
            if (_frameSource == null)
                return;

            _frameSource.FrameReady -= HandleFrameReady;
        }

        private void HandleFrameReady() => RefreshPreviewTexture();

        private void RefreshPreviewTexture()
        {
            if (!ShowPreview || _previewImage == null || _frameSource == null)
                return;

            RenderTexture currentRt = _frameSource.CurrentRenderTexture;
            if (currentRt != null && _previewImage.texture != currentRt)
                _previewImage.texture = currentRt;
        }

        private static bool TryFindFirstFrameSource(out IVisionFrameSource frameSource)
        {
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IVisionFrameSource discoveredSource)
                {
                    frameSource = discoveredSource;
                    return true;
                }
            }

            frameSource = null;
            return false;
        }

        private void UpdateFpsCalculation()
        {
            float now = Time.realtimeSinceStartup;
            float elapsed = now - _fpsTimeStart;

            if (elapsed >= FpsUpdateInterval)
            {
                long currentFrameCount = _frameSource.FrameCount;
                long framesDelta = currentFrameCount - _fpsFrameCountStart;

                CurrentFps = framesDelta / elapsed;

                _fpsFrameCountStart = currentFrameCount;
                _fpsTimeStart = now;
            }
        }

        private void InitializeStyles()
        {
            if (_styleInitialized) return;

            _statsStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 12, alignment = TextAnchor.UpperLeft, padding = new RectOffset(8, 8, 8, 8)
            };
            _statsStyle.normal.textColor = Color.white;
            _styleInitialized = true;
        }

        private void DrawOverlayPreview()
        {
            RenderTexture rt = _frameSource.CurrentRenderTexture;
            if (rt == null) return;

            Rect previewRect = GetOverlayRect();

            GUI.DrawTextureWithTexCoords(previewRect, rt, new Rect(0, 1, 1, -1));
        }

        private void DrawStatistics()
        {
            Rect statsRect = GetStatsRect();

            (int w, int h) = _frameSource?.FrameDimensions ?? (0, 0);
            float targetFps = _frameSource?.TargetFrameRate ?? 0;
            long frameCount = _frameSource?.FrameCount ?? 0;

            string statsText = "Vision Capture Debug\n" +
                               $"Status: {(IsCapturing ? "Capturing" : "Stopped")}\n" +
                               $"Resolution: {w}x{h}\n" +
                               $"FPS: {CurrentFps:F1} (target: {targetFps:F0})\n" +
                               $"Frames: {frameCount}";

            GUI.Box(statsRect, statsText, _statsStyle);
        }

        private Rect GetOverlayRect()
        {
            Vector2 overlaySize = CalculateOverlaySize();

            float x = 0, y = 0;

            switch (_overlayPosition)
            {
                case PreviewPosition.TopLeft:
                    x = _offsetX;
                    y = _offsetY;
                    break;
                case PreviewPosition.TopRight:
                    x = Screen.width - overlaySize.x - _offsetX;
                    y = _offsetY;
                    break;
                case PreviewPosition.BottomLeft:
                    x = _offsetX;
                    y = Screen.height - overlaySize.y - _offsetY;
                    break;
                case PreviewPosition.BottomRight:
                    x = Screen.width - overlaySize.x - _offsetX;
                    y = Screen.height - overlaySize.y - _offsetY;
                    break;
            }

            return new Rect(x, y, overlaySize.x, overlaySize.y);
        }

        /// <summary>
        ///     Calculates overlay size maintaining the correct aspect ratio.
        /// </summary>
        /// <returns>Overlay size with width from inspector and height calculated from aspect ratio.</returns>
        private Vector2 CalculateOverlaySize()
        {
            float aspectRatio = GetEffectiveAspectRatio();
            float height = _overlayWidth / aspectRatio;
            return new Vector2(_overlayWidth, height);
        }

        /// <summary>
        ///     Gets the effective aspect ratio based on settings.
        ///     Uses frame source dimensions if available and enabled, otherwise uses custom or default ratio.
        /// </summary>
        /// <returns>The aspect ratio (width / height) to use for overlay sizing.</returns>
        private float GetEffectiveAspectRatio()
        {
            if (_useSourceAspectRatio && _frameSource != null)
            {
                (int width, int height) = _frameSource.FrameDimensions;
                if (width > 0 && height > 0) return (float)width / height;
            }

            return _customAspectRatio;
        }

        private Rect GetStatsRect()
        {
            float statsHeight = 100f;
            float statsWidth = 180f;
            float statsGap = 5f;

            if (ShowPreview && _previewImage == null)
            {
                Rect overlayRect = GetOverlayRect();

                float statsY = overlayRect.yMax - statsHeight;
                float statsX = _overlayPosition switch
                {
                    PreviewPosition.TopRight or PreviewPosition.BottomRight => overlayRect.x - statsWidth - statsGap,
                    _ => overlayRect.xMax + statsGap
                };
                return new Rect(statsX, statsY, statsWidth, statsHeight);
            }

            float x, y;
            switch (_overlayPosition)
            {
                case PreviewPosition.TopLeft:
                    x = _offsetX;
                    y = _offsetY;
                    break;
                case PreviewPosition.TopRight:
                    x = Screen.width - statsWidth - _offsetX;
                    y = _offsetY;
                    break;
                case PreviewPosition.BottomLeft:
                    x = _offsetX;
                    y = Screen.height - statsHeight - _offsetY;
                    break;
                case PreviewPosition.BottomRight:
                default:
                    x = Screen.width - statsWidth - _offsetX;
                    y = Screen.height - statsHeight - _offsetY;
                    break;
            }

            return new Rect(x, y, statsWidth, statsHeight);
        }

        #endregion
    }

    /// <summary>
    ///     Position options for the debug preview overlay.
    /// </summary>
    public enum PreviewPosition
    {
        /// <summary>Top-left corner.</summary>
        TopLeft,

        /// <summary>Top-right corner.</summary>
        TopRight,

        /// <summary>Bottom-left corner.</summary>
        BottomLeft,

        /// <summary>Bottom-right corner.</summary>
        BottomRight
    }
}
