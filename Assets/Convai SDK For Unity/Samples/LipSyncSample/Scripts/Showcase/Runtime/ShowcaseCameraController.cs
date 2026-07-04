using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Convai.ShowcaseCamera
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Convai/Showcase/Showcase Camera Controller")]
    public sealed class ShowcaseCameraController : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private ShowcaseCameraConfig _config;
        [SerializeField] private bool _autoFindTargetOnStart = true;
        [SerializeField] private bool _hideCursorWhileControlling = true;
        [SerializeField] private bool _lockCursorWhileOrbit = true;
        [SerializeField] private bool _allowGlobalDofToggle = true;

        [Header("Target")]
        [SerializeField] private Transform _targetRoot;
        [SerializeField] private Transform _head;
        [SerializeField] private Transform _leftEye;
        [SerializeField] private Transform _rightEye;
        [Tooltip("Assign the character's root GameObject to enable auto-resolve without scene-wide search.")]
        [SerializeField] private Transform _characterSearchRoot;
        [Tooltip("Optional: assign the global Volume for depth-of-field toggle (key 0).")]
        [SerializeField] private Volume _globalVolume;

        [Header("Rig")]
        [SerializeField] private Transform _targetAnchor;
        [SerializeField] private Transform _yawPivot;
        [SerializeField] private Transform _pitchPivot;
        [SerializeField] private Transform _dolly;
        [SerializeField] private Transform _renderCameraTransform;
        [SerializeField] private Camera _renderCamera;
        [SerializeField] private UniversalAdditionalCameraData _urpCameraData;

        private Vector3 _currentAnchorPosition;
        private Vector3 _targetAnchorPosition;
        private Vector3 _anchorVelocity;
        private Vector3 _currentFramingOffset;
        private Vector3 _targetFramingOffset;
        private Vector3 _framingOffsetVelocity;

        private float _currentYaw;
        private float _targetYaw;
        private float _yawVelocity;
        private float _currentPitch;
        private float _targetPitch;
        private float _pitchVelocity;

        private float _currentDollyDistance;
        private float _targetDollyDistance;
        private float _dollyVelocity;
        private float _currentFocalLength;
        private float _targetFocalLength;
        private float _focalVelocity;
        private float _targetZoomNormalized;

        private bool _anchorInitialized;
        private bool _initialized;
        private bool _isPresetTransitionActive;
        private float _presetTransitionElapsed;
        private float _presetTransitionDuration;

        private float _presetFromYaw;
        private float _presetFromPitch;
        private float _presetFromDollyDistance;
        private float _presetFromFocalLength;
        private Vector3 _presetFromFramingOffset;

        private float _presetToYaw;
        private float _presetToPitch;
        private float _presetToDollyDistance;
        private float _presetToFocalLength;
        private Vector3 _presetToFramingOffset;

        private bool _cursorStateCached;
        private bool _cachedCursorVisible;
        private CursorLockMode _cachedCursorLockMode;
        private bool _cursorRevealRequested;
        private static Type _tmpInputFieldType;
        private const float FallbackFocusHeight = 1.6f;
        private const float RendererUpperBodyBias = 0.55f;

        public Camera RenderCamera => _renderCamera;
        public UniversalAdditionalCameraData RenderCameraData => _urpCameraData;

        public void SetConfig(ShowcaseCameraConfig config)
        {
            _config = config;
            EnsureConfig();
        }

        public void SetTarget(Transform head, Transform leftEye, Transform rightEye)
        {
            _head = head;
            _leftEye = leftEye;
            _rightEye = rightEye;
            _targetRoot = head != null ? head.root : (leftEye != null ? leftEye.root : (rightEye != null ? rightEye.root : null));
            _anchorInitialized = false;
        }

        public void SetPreset(int index)
        {
            if (_config == null || !_config.TryGetPreset(index, out ShowcaseShotPreset preset))
            {
                return;
            }

            BeginPresetTransition(preset);
        }

        public void ResetView()
        {
            if (_config == null)
            {
                return;
            }

            CancelPresetTransition();

            _targetYaw = _config.defaultYaw;
            _targetPitch = Mathf.Clamp(_config.defaultPitch, _config.minPitch, _config.maxPitch);
            _targetDollyDistance = Mathf.Clamp(_config.defaultDollyDistance, _config.minDollyDistance, _config.maxDollyDistance);
            _targetFocalLength = Mathf.Clamp(_config.defaultFocalLength, _config.minFocalLength, _config.maxFocalLength);
            _targetFramingOffset = _config.defaultFramingOffset;
            _targetZoomNormalized = Mathf.InverseLerp(_config.maxDollyDistance, _config.minDollyDistance, _targetDollyDistance);
            _targetFocalLength = GetFocalLengthForZoomNormalized(_targetZoomNormalized);
        }

        public void BuildRigIfNeeded()
        {
            EnsureConfig();
            EnsureRigHierarchy();
            EnsureCameraSetup();
            if (_autoFindTargetOnStart && _head == null && _targetRoot == null)
            {
                TryAutoResolveTarget();
            }
            InitializeStateIfNeeded();
        }

        public bool TryAutoResolveTarget()
        {
            if (_head != null || _targetRoot != null)
            {
                return true;
            }

            if (_characterSearchRoot == null)
            {
                return false;
            }

            Animator[] animators = _characterSearchRoot.GetComponentsInChildren<Animator>(true);
            foreach (Animator animator in animators)
            {
                if (animator == null || !animator.isHuman)
                {
                    continue;
                }

                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null)
                {
                    Transform leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
                    Transform rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
                    SetTarget(head, leftEye, rightEye);
                    return true;
                }
            }

            foreach (Animator animator in animators)
            {
                if (animator == null)
                {
                    continue;
                }

                Transform root = animator.transform;
                if (root == null)
                {
                    continue;
                }

                _targetRoot = root;
                _anchorInitialized = false;

                if (TryResolveByName(root, "head", out Transform namedHead))
                {
                    _head = namedHead;
                }

                if (TryResolveByName(root, "lefteye", out Transform namedLeftEye) ||
                    TryResolveByName(root, "eye_l", out namedLeftEye))
                {
                    _leftEye = namedLeftEye;
                }

                if (TryResolveByName(root, "righteye", out Transform namedRightEye) ||
                    TryResolveByName(root, "eye_r", out namedRightEye))
                {
                    _rightEye = namedRightEye;
                }

                return true;
            }

            SkinnedMeshRenderer[] skinnedMeshes = _characterSearchRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Transform bestRoot = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < skinnedMeshes.Length; i++)
            {
                SkinnedMeshRenderer smr = skinnedMeshes[i];
                if (smr == null)
                {
                    continue;
                }

                Transform root = smr.rootBone != null ? smr.rootBone.root : smr.transform.root;
                if (root == null || IsPartOfRig(root))
                {
                    continue;
                }

                Bounds bounds = smr.bounds;
                float score = (bounds.extents.x + bounds.extents.z) * bounds.extents.y;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestRoot = root;
                }
            }

            if (bestRoot != null)
            {
                _targetRoot = bestRoot;
                _anchorInitialized = false;

                if (TryResolveByName(bestRoot, "head", out Transform namedHead))
                {
                    _head = namedHead;
                }

                if (TryResolveByName(bestRoot, "lefteye", out Transform namedLeftEye) ||
                    TryResolveByName(bestRoot, "eye_l", out namedLeftEye))
                {
                    _leftEye = namedLeftEye;
                }

                if (TryResolveByName(bestRoot, "righteye", out Transform namedRightEye) ||
                    TryResolveByName(bestRoot, "eye_r", out namedRightEye))
                {
                    _rightEye = namedRightEye;
                }

                return true;
            }

            return false;
        }

        [ContextMenu("Convai/Showcase/Auto Find Target")]
        private void AutoFindTargetContext()
        {
            TryAutoResolveTarget();
        }

        [ContextMenu("Convai/Showcase/Reset View")]
        private void ResetViewContext()
        {
            ResetView();
        }

        private void Awake()
        {
            BuildRigIfNeeded();
        }

        private void OnEnable()
        {
            _cursorRevealRequested = false;
            BuildRigIfNeeded();
        }

        private void Start()
        {
            if (_autoFindTargetOnStart)
            {
                TryAutoResolveTarget();
            }
        }

        private void LateUpdate()
        {
            if (_config == null || !_initialized)
            {
                BuildRigIfNeeded();
                if (!_initialized)
                {
                    return;
                }
            }

            if (_autoFindTargetOnStart && _head == null && _targetRoot == null)
            {
                TryAutoResolveTarget();
            }

            float deltaTime = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            UpdatePresetTransition(deltaTime);
            ProcessInput();
            UpdateRuntimeState(deltaTime);
            ApplyRigState();
        }

        private void OnDisable()
        {
            _cursorRevealRequested = false;
            RestoreCursorState();
        }

        private void EnsureConfig()
        {
            if (_config == null)
            {
                _config = ScriptableObject.CreateInstance<ShowcaseCameraConfig>();
                _config.hideFlags = HideFlags.DontSave;
            }

            _config.Sanitize();
        }

        private void InitializeStateIfNeeded()
        {
            if (_initialized || _config == null)
            {
                return;
            }

            _targetYaw = _config.defaultYaw;
            _targetPitch = Mathf.Clamp(_config.defaultPitch, _config.minPitch, _config.maxPitch);
            _targetDollyDistance = Mathf.Clamp(_config.defaultDollyDistance, _config.minDollyDistance, _config.maxDollyDistance);
            _targetFocalLength = Mathf.Clamp(_config.defaultFocalLength, _config.minFocalLength, _config.maxFocalLength);
            _targetFramingOffset = _config.defaultFramingOffset;
            _targetZoomNormalized = Mathf.InverseLerp(_config.maxDollyDistance, _config.minDollyDistance, _targetDollyDistance);
            _targetFocalLength = GetFocalLengthForZoomNormalized(_targetZoomNormalized);

            _currentYaw = _targetYaw;
            _currentPitch = _targetPitch;
            _currentDollyDistance = _targetDollyDistance;
            _currentFocalLength = _targetFocalLength;
            _currentFramingOffset = GetDesiredFramingOffset();
            if (_head != null || _targetRoot != null)
            {
                _currentAnchorPosition = GetDesiredAnchorPosition(_currentFramingOffset);
                _targetAnchorPosition = _currentAnchorPosition;
                _anchorInitialized = true;
            }

            _initialized = true;
            ApplyRigState();
        }

        private void ProcessInput()
        {
            bool blockCameraInput = IsPointerOverUi() || IsTextInputFocused();
            if (WasCursorRevealPressed())
            {
                _cursorRevealRequested = true;
                HandleCursorLock(false, true);
                return;
            }

            if (blockCameraInput)
            {
                HandleCursorLock(false, true);
                return;
            }

            if (_allowGlobalDofToggle && WasGlobalDofTogglePressed())
            {
                ToggleGlobalVolumeDepthOfField();
            }

            int presetIndex = ReadPresetKeyIndex();
            if (presetIndex >= 0)
            {
                SetPreset(presetIndex);
                _cursorRevealRequested = false;
            }

            bool orbitHeld = IsOrbitHeld();
            if (orbitHeld)
            {
                _cursorRevealRequested = false;
            }

            float scrollDelta = ReadScrollDelta();
            if (Mathf.Abs(scrollDelta) > 0.0001f)
            {
                _cursorRevealRequested = false;
            }

            HandleCursorLock(orbitHeld, _cursorRevealRequested);

            if (orbitHeld)
            {
                CancelPresetTransition();
                Vector2 delta = ReadLookDelta();
                _targetYaw += delta.x * _config.orbitSensitivityX;
                _targetPitch = Mathf.Clamp(_targetPitch - (delta.y * _config.orbitSensitivityY), _config.minPitch, _config.maxPitch);
            }

            if (Mathf.Abs(scrollDelta) > 0.0001f)
            {
                CancelPresetTransition();
                if (Mathf.Abs(scrollDelta) > 10f)
                {
                    scrollDelta *= 0.01f;
                }

                _targetZoomNormalized = Mathf.Clamp01(_targetZoomNormalized + (scrollDelta * _config.zoomSensitivity));
                UpdateHybridZoomTargets();
            }
        }

        private void UpdateRuntimeState(float deltaTime)
        {
            if (!_anchorInitialized && (_head != null || _targetRoot != null))
            {
                _currentAnchorPosition = GetDesiredAnchorPosition(_currentFramingOffset);
                _targetAnchorPosition = _currentAnchorPosition;
                _anchorInitialized = true;
            }

            Vector3 desiredFramingOffset = GetDesiredFramingOffset();
            _currentFramingOffset = Vector3.SmoothDamp(
                _currentFramingOffset,
                desiredFramingOffset,
                ref _framingOffsetVelocity,
                _config.orbitSmoothTime,
                Mathf.Infinity,
                deltaTime);

            Vector3 desiredAnchor = GetDesiredAnchorPosition(_currentFramingOffset);
            float threshold = Mathf.Max(0f, _config.anchorFollowThreshold);
            if (threshold <= 0f || Vector3.SqrMagnitude(desiredAnchor - _currentAnchorPosition) > threshold * threshold)
            {
                _targetAnchorPosition = desiredAnchor;
            }

            _currentAnchorPosition = Vector3.SmoothDamp(
                _currentAnchorPosition,
                _targetAnchorPosition,
                ref _anchorVelocity,
                _config.anchorSmoothTime,
                Mathf.Infinity,
                deltaTime);

            _currentYaw = Mathf.SmoothDampAngle(_currentYaw, _targetYaw, ref _yawVelocity, _config.orbitSmoothTime, Mathf.Infinity, deltaTime);
            _currentPitch = Mathf.SmoothDampAngle(_currentPitch, _targetPitch, ref _pitchVelocity, _config.orbitSmoothTime, Mathf.Infinity, deltaTime);
            _currentDollyDistance = Mathf.SmoothDamp(_currentDollyDistance, _targetDollyDistance, ref _dollyVelocity, _config.zoomSmoothTime, Mathf.Infinity, deltaTime);
            _currentFocalLength = Mathf.SmoothDamp(_currentFocalLength, _targetFocalLength, ref _focalVelocity, _config.zoomSmoothTime, Mathf.Infinity, deltaTime);
            _currentPitch = Mathf.Clamp(_currentPitch, _config.minPitch, _config.maxPitch);
            _currentDollyDistance = Mathf.Clamp(_currentDollyDistance, _config.minDollyDistance, _config.maxDollyDistance);
            _currentFocalLength = Mathf.Clamp(_currentFocalLength, _config.minFocalLength, _config.maxFocalLength);
        }

        private void ApplyRigState()
        {
            if (_targetAnchor == null || _yawPivot == null || _pitchPivot == null || _dolly == null || _renderCamera == null)
            {
                return;
            }

            _targetAnchor.position = _currentAnchorPosition;
            _yawPivot.position = _currentAnchorPosition;
            _yawPivot.rotation = Quaternion.Euler(0f, _currentYaw, 0f);
            _pitchPivot.localRotation = Quaternion.Euler(_currentPitch, 0f, 0f);
            _dolly.localPosition = new Vector3(0f, 0f, -_currentDollyDistance);
            _renderCameraTransform.localPosition = Vector3.zero;
            _renderCameraTransform.localRotation = Quaternion.identity;

            _renderCamera.focalLength = _currentFocalLength;
            _renderCamera.aperture = _config.aperture;
            _renderCamera.shutterSpeed = _config.shutterSpeed;
            _renderCamera.iso = _config.iso;
        }

        private void UpdateHybridZoomTargets()
        {
            float dollyLerp = _targetZoomNormalized;
            _targetDollyDistance = Mathf.Lerp(_config.maxDollyDistance, _config.minDollyDistance, dollyLerp);
            _targetFocalLength = GetFocalLengthForZoomNormalized(_targetZoomNormalized);
        }

        /// <summary>
        /// Returns the focal length for a given normalized zoom (0 = wide, 1 = tele) using the same curve as scroll zoom.
        /// Keeps initial/reset FOV consistent with zoom so the first scroll does not jump.
        /// </summary>
        private float GetFocalLengthForZoomNormalized(float zoomNormalized)
        {
            float focalLerp = Mathf.Clamp01((zoomNormalized - 0.45f) / 0.55f);
            focalLerp = focalLerp * focalLerp * (3f - (2f * focalLerp));
            return Mathf.Lerp(_config.minFocalLength, _config.maxFocalLength, focalLerp);
        }

        private Vector3 GetFocusPoint()
        {
            if (_leftEye != null && _rightEye != null)
            {
                Vector3 eyeMid = (_leftEye.position + _rightEye.position) * 0.5f;
                if (_config != null && _config.enableFaceCenterFocusOffset)
                {
                    eyeMid = EstimateFaceCenterPoint(eyeMid);
                }

                if (_config != null && _config.enableLipSyncMouthFocusBias)
                {
                    Vector3 estimatedMouth = EstimateMouthPoint(eyeMid);
                    return Vector3.Lerp(eyeMid, estimatedMouth, _config.mouthFocusBlend);
                }

                return eyeMid;
            }

            if (_head != null)
            {
                Vector3 headPoint = _head.position;
                if (_config != null && _config.enableFaceCenterFocusOffset)
                {
                    headPoint = EstimateFaceCenterPoint(headPoint);
                }

                if (_config != null && _config.enableLipSyncMouthFocusBias)
                {
                    return EstimateMouthPoint(headPoint);
                }

                return headPoint;
            }

            if (_targetRoot != null)
            {
                return GetFallbackFocusPoint(_targetRoot);
            }

            return _targetAnchor != null ? _targetAnchor.position : transform.position;
        }

        private Quaternion GetTargetBasisRotation()
        {
            if (_head != null)
            {
                return _head.rotation;
            }

            if (_targetRoot != null)
            {
                return _targetRoot.rotation;
            }

            return Quaternion.identity;
        }

        private Vector3 GetDesiredAnchorPosition(Vector3 framingOffset)
        {
            return GetFocusPoint() + (GetTargetBasisRotation() * framingOffset);
        }

        private Vector3 GetDesiredFramingOffset()
        {
            if (_config == null)
            {
                return _targetFramingOffset;
            }

            if (!_config.enableZoomFramingCompensation)
            {
                return _targetFramingOffset;
            }

            float t = Mathf.InverseLerp(_config.zoomCompensationStart, 1f, _targetZoomNormalized);
            return _targetFramingOffset + new Vector3(0f, _config.zoomCompensationY * t, _config.zoomCompensationZ * t);
        }

        private Vector3 EstimateMouthPoint(Vector3 basePoint)
        {
            if (_config == null)
            {
                return basePoint;
            }

            Quaternion basis = GetTargetBasisRotation();
            Vector3 mouthOffset = new Vector3(0f, _config.estimatedMouthVerticalOffset, _config.estimatedMouthForwardOffset);
            return basePoint + (basis * mouthOffset);
        }

        private Vector3 EstimateFaceCenterPoint(Vector3 basePoint)
        {
            if (_config == null)
            {
                return basePoint;
            }

            Quaternion basis = GetTargetBasisRotation();
            Vector3 offset = new Vector3(0f, _config.faceCenterVerticalOffset, _config.faceCenterForwardOffset);
            return basePoint + (basis * offset);
        }

        private static bool TryResolveByName(Transform root, string containsToken, out Transform found)
        {
            found = null;
            if (root == null || string.IsNullOrEmpty(containsToken))
            {
                return false;
            }

            string token = containsToken.ToLowerInvariant();
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == null)
                {
                    continue;
                }

                string childName = child.name;
                if (string.IsNullOrEmpty(childName))
                {
                    continue;
                }

                if (childName.ToLowerInvariant().Contains(token))
                {
                    found = child;
                    return true;
                }
            }

            return false;
        }

        private bool IsPartOfRig(Transform candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            return candidate == transform || candidate.IsChildOf(transform);
        }

        private static Vector3 GetFallbackFocusPoint(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds mergedBounds = new Bounds(root.position, Vector3.zero);

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Bounds bounds = renderer.bounds;
                if (!hasBounds)
                {
                    mergedBounds = bounds;
                    hasBounds = true;
                }
                else
                {
                    mergedBounds.Encapsulate(bounds);
                }
            }

            if (hasBounds)
            {
                return mergedBounds.center + (root.up * (mergedBounds.extents.y * RendererUpperBodyBias));
            }

            return root.position + (root.up * FallbackFocusHeight);
        }

        private void BeginPresetTransition(ShowcaseShotPreset preset)
        {
            _isPresetTransitionActive = true;
            _presetTransitionElapsed = 0f;
            _presetTransitionDuration = Mathf.Max(0.01f, preset.transitionTime);

            _presetFromYaw = _targetYaw;
            _presetFromPitch = _targetPitch;
            _presetFromDollyDistance = _targetDollyDistance;
            _presetFromFocalLength = _targetFocalLength;
            _presetFromFramingOffset = _targetFramingOffset;

            _presetToYaw = preset.yaw;
            _presetToPitch = Mathf.Clamp(preset.pitch, _config.minPitch, _config.maxPitch);
            _presetToDollyDistance = Mathf.Clamp(preset.dollyDistance, _config.minDollyDistance, _config.maxDollyDistance);
            _presetToFocalLength = Mathf.Clamp(preset.focalLength, _config.minFocalLength, _config.maxFocalLength);
            _presetToFramingOffset = preset.framingOffset;

            _targetZoomNormalized = Mathf.InverseLerp(_config.maxDollyDistance, _config.minDollyDistance, _presetToDollyDistance);
        }

        private void UpdatePresetTransition(float deltaTime)
        {
            if (!_isPresetTransitionActive)
            {
                return;
            }

            _presetTransitionElapsed += deltaTime;
            float t = Mathf.Clamp01(_presetTransitionElapsed / _presetTransitionDuration);
            float smoothT = t * t * (3f - (2f * t));

            _targetYaw = Mathf.LerpAngle(_presetFromYaw, _presetToYaw, smoothT);
            _targetPitch = Mathf.Lerp(_presetFromPitch, _presetToPitch, smoothT);
            _targetDollyDistance = Mathf.Lerp(_presetFromDollyDistance, _presetToDollyDistance, smoothT);
            _targetFocalLength = Mathf.Lerp(_presetFromFocalLength, _presetToFocalLength, smoothT);
            _targetFramingOffset = Vector3.Lerp(_presetFromFramingOffset, _presetToFramingOffset, smoothT);

            if (t >= 1f)
            {
                _isPresetTransitionActive = false;
            }
        }

        private void CancelPresetTransition()
        {
            _isPresetTransitionActive = false;
            _presetTransitionElapsed = 0f;
            _presetTransitionDuration = 0f;
        }

        private void EnsureRigHierarchy()
        {
            _targetAnchor = GetOrCreateChild(transform, "TargetAnchor");
            _yawPivot = GetOrCreateChild(transform, "YawPivot");
            _pitchPivot = GetOrCreateChild(_yawPivot, "PitchPivot");
            _dolly = GetOrCreateChild(_pitchPivot, "Dolly");
            _renderCameraTransform = GetOrCreateChild(_dolly, "RenderCamera");

            if (_pitchPivot.parent != _yawPivot)
            {
                _pitchPivot.SetParent(_yawPivot, false);
            }

            if (_dolly.parent != _pitchPivot)
            {
                _dolly.SetParent(_pitchPivot, false);
            }

            if (_renderCameraTransform.parent != _dolly)
            {
                _renderCameraTransform.SetParent(_dolly, false);
            }

            _targetAnchor.localPosition = Vector3.zero;
            _targetAnchor.localRotation = Quaternion.identity;
            _yawPivot.localPosition = Vector3.zero;
            _pitchPivot.localPosition = Vector3.zero;
            _renderCamera = _renderCameraTransform.GetComponent<Camera>();
            if (_renderCamera == null)
            {
                _renderCamera = _renderCameraTransform.gameObject.AddComponent<Camera>();
            }

            if (_renderCameraTransform.GetComponent<AudioListener>() == null)
            {
                _renderCameraTransform.gameObject.AddComponent<AudioListener>();
            }
        }

        private void EnsureCameraSetup()
        {
            if (_renderCamera == null || _config == null)
            {
                return;
            }

            _renderCamera.usePhysicalProperties = true;
            _renderCamera.sensorSize = _config.sensorSize;
            _renderCamera.nearClipPlane = Mathf.Max(0.01f, _renderCamera.nearClipPlane);
            _renderCamera.focalLength = Mathf.Clamp(_renderCamera.focalLength, _config.minFocalLength, _config.maxFocalLength);
            _renderCamera.aperture = _config.aperture;
            _renderCamera.shutterSpeed = _config.shutterSpeed;
            _renderCamera.iso = _config.iso;
            _renderCamera.depth = 0f;
            _renderCameraTransform.gameObject.tag = "MainCamera";

            _urpCameraData = _renderCamera.GetComponent<UniversalAdditionalCameraData>();
            if (_urpCameraData == null)
            {
                _urpCameraData = _renderCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }

            _urpCameraData.volumeTrigger = _renderCameraTransform;
            _urpCameraData.volumeLayerMask |= (1 << _renderCamera.gameObject.layer);
        }

        private static Transform GetOrCreateChild(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            if (child != null)
            {
                return child;
            }

            GameObject childObject = new GameObject(childName);
            child = childObject.transform;
            child.SetParent(parent, false);
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            return child;
        }

        private bool IsPointerOverUi()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            if (eventSystem.IsPointerOverGameObject())
            {
                return true;
            }

#if ENABLE_INPUT_SYSTEM
            if (TryGetMouseDeviceId(out int deviceId)
                && eventSystem.IsPointerOverGameObject(deviceId))
            {
                return true;
            }
#endif

            return false;
        }

        private static bool IsTextInputFocused()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            GameObject selected = eventSystem.currentSelectedGameObject;
            if (selected == null)
            {
                return false;
            }

            InputField inputField = selected.GetComponentInParent<InputField>();
            if (inputField != null && inputField.isActiveAndEnabled && inputField.interactable)
            {
                return true;
            }

            if (_tmpInputFieldType == null)
            {
                _tmpInputFieldType = Type.GetType("TMPro.TMP_InputField, Unity.TextMeshPro");
            }

            if (_tmpInputFieldType != null)
            {
                Component tmpInput = selected.GetComponent(_tmpInputFieldType);
                if (tmpInput == null)
                {
                    tmpInput = selected.GetComponentInParent(_tmpInputFieldType);
                }
                if (tmpInput != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void HandleCursorLock(bool orbitHeld, bool forceVisible = false)
        {
            if (forceVisible)
            {
                CacheCursorStateIfNeeded();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            CacheCursorStateIfNeeded();
            if (_lockCursorWhileOrbit && orbitHeld)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else if (!_hideCursorWhileControlling)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = false;
            }
        }

        private void CacheCursorStateIfNeeded()
        {
            if (_cursorStateCached)
            {
                return;
            }

            _cachedCursorVisible = Cursor.visible;
            _cachedCursorLockMode = Cursor.lockState;
            _cursorStateCached = true;
        }

        private void RestoreCursorState()
        {
            if (!_cursorStateCached)
            {
                return;
            }

            Cursor.lockState = _cachedCursorLockMode;
            Cursor.visible = _cachedCursorVisible;
            _cursorStateCached = false;
        }

        private static float NormalizeScroll(float scrollY)
        {
            if (Mathf.Abs(scrollY) <= 0.0001f)
            {
                return 0f;
            }

            if (Mathf.Abs(scrollY) > 10f)
            {
                return scrollY * 0.01f;
            }

            return scrollY;
        }

        private Vector2 ReadLookDelta()
        {
#if ENABLE_INPUT_SYSTEM
            if (TryReadMouseDelta(out Vector2 delta))
            {
                return delta;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#else
            return Vector2.zero;
#endif
        }

        private float ReadScrollDelta()
        {
#if ENABLE_INPUT_SYSTEM
            if (TryReadMouseScrollY(out float scrollY))
            {
                return NormalizeScroll(scrollY);
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return NormalizeScroll(Input.mouseScrollDelta.y * 120f);
#else
            return 0f;
#endif
        }

        private bool IsOrbitHeld()
        {
#if ENABLE_INPUT_SYSTEM
            if (TryReadMouseRightButtonPressed(out bool isPressed))
            {
                return isPressed;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(1);
#else
            return false;
#endif
        }

        private static int ReadPresetKeyIndex()
        {
#if ENABLE_INPUT_SYSTEM
            if (WasInputSystemKeyPressedThisFrame("digit1Key")) return 0;
            if (WasInputSystemKeyPressedThisFrame("digit2Key")) return 1;
            if (WasInputSystemKeyPressedThisFrame("digit3Key")) return 2;
            if (WasInputSystemKeyPressedThisFrame("digit4Key")) return 3;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Alpha1)) return 0;
            if (Input.GetKeyDown(KeyCode.Alpha2)) return 1;
            if (Input.GetKeyDown(KeyCode.Alpha3)) return 2;
            if (Input.GetKeyDown(KeyCode.Alpha4)) return 3;
#endif
            return -1;
        }

        private static bool WasCursorRevealPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (WasInputSystemKeyPressedThisFrame("escapeKey"))
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Escape);
#else
            return false;
#endif
        }

        private static bool WasGlobalDofTogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (WasInputSystemKeyPressedThisFrame("digit0Key"))
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Alpha0);
#else
            return false;
#endif
        }

        private void ToggleGlobalVolumeDepthOfField()
        {
            if (_globalVolume == null)
                _globalVolume = FindFirstObjectByType<Volume>();

            Volume[] volumes = _globalVolume != null ? new[] { _globalVolume } : Array.Empty<Volume>();
            foreach (Volume volume in volumes)
            {
                if (volume == null || !volume.enabled || !volume.isGlobal)
                {
                    continue;
                }

                VolumeProfile profile = volume.profile;
                if (profile == null)
                {
                    continue;
                }

                if (profile.TryGet(out DepthOfField dof))
                {
                    dof.active = !dof.active;
                }
            }
        }

        private static bool TryGetMouseDeviceId(out int deviceId)
        {
            deviceId = 0;
            object mouse = InputSystemReflectionCache.GetMouseCurrent();
            if (mouse == null)
            {
                return false;
            }

            if (InputSystemReflectionCache.MouseDeviceIdProperty?.GetValue(mouse) is int id)
            {
                deviceId = id;
                return true;
            }

            return false;
        }

        private static bool TryReadMouseDelta(out Vector2 delta)
        {
            delta = Vector2.zero;
            object mouse = InputSystemReflectionCache.GetMouseCurrent();
            if (mouse == null)
            {
                return false;
            }

            object deltaControl = InputSystemReflectionCache.MouseDeltaProperty?.GetValue(mouse);
            if (deltaControl == null)
            {
                return false;
            }

            if (InputSystemReflectionCache.MouseDeltaReadValueMethod?.Invoke(deltaControl, null) is Vector2 value)
            {
                delta = value;
                return true;
            }

            return false;
        }

        private static bool TryReadMouseScrollY(out float scrollY)
        {
            scrollY = 0f;
            object mouse = InputSystemReflectionCache.GetMouseCurrent();
            if (mouse == null)
            {
                return false;
            }

            object scrollControl = InputSystemReflectionCache.MouseScrollProperty?.GetValue(mouse);
            if (scrollControl == null)
            {
                return false;
            }

            if (InputSystemReflectionCache.MouseScrollReadValueMethod?.Invoke(scrollControl, null) is Vector2 value)
            {
                scrollY = value.y;
                return true;
            }

            return false;
        }

        private static bool TryReadMouseRightButtonPressed(out bool isPressed)
        {
            isPressed = false;
            object mouse = InputSystemReflectionCache.GetMouseCurrent();
            if (mouse == null)
            {
                return false;
            }

            object rightButton = InputSystemReflectionCache.MouseRightButtonProperty?.GetValue(mouse);
            if (rightButton == null)
            {
                return false;
            }

            if (InputSystemReflectionCache.ButtonIsPressedProperty?.GetValue(rightButton) is bool pressed)
            {
                isPressed = pressed;
                return true;
            }

            return false;
        }

        private static bool WasInputSystemKeyPressedThisFrame(string keyPropertyName)
        {
            object keyboard = InputSystemReflectionCache.GetKeyboardCurrent();
            if (keyboard == null)
            {
                return false;
            }

            PropertyInfo keyProperty = InputSystemReflectionCache.GetKeyboardKeyProperty(keyPropertyName);
            object keyControl = keyProperty?.GetValue(keyboard);
            if (keyControl == null)
            {
                return false;
            }

            return InputSystemReflectionCache.KeyWasPressedThisFrameProperty?.GetValue(keyControl) is bool wasPressed
                   && wasPressed;
        }

        private static class InputSystemReflectionCache
        {
            private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
            private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

            private static readonly Type MouseType = Type.GetType("UnityEngine.InputSystem.Mouse, Unity.InputSystem");
            private static readonly Type KeyboardType = Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem");

            private static readonly PropertyInfo MouseCurrentProperty = MouseType?.GetProperty("current", PublicStatic);
            private static readonly PropertyInfo KeyboardCurrentProperty = KeyboardType?.GetProperty("current", PublicStatic);

            internal static readonly PropertyInfo MouseDeviceIdProperty = MouseType?.GetProperty("deviceId", PublicInstance);
            internal static readonly PropertyInfo MouseDeltaProperty = MouseType?.GetProperty("delta", PublicInstance);
            internal static readonly PropertyInfo MouseScrollProperty = MouseType?.GetProperty("scroll", PublicInstance);
            internal static readonly PropertyInfo MouseRightButtonProperty = MouseType?.GetProperty("rightButton", PublicInstance);

            internal static readonly MethodInfo MouseDeltaReadValueMethod = GetReadValueMethod(MouseDeltaProperty);
            internal static readonly MethodInfo MouseScrollReadValueMethod = GetReadValueMethod(MouseScrollProperty);

            internal static readonly PropertyInfo ButtonIsPressedProperty =
                MouseRightButtonProperty?.PropertyType?.GetProperty("isPressed", PublicInstance);

            private static readonly PropertyInfo Digit0KeyProperty = KeyboardType?.GetProperty("digit0Key", PublicInstance);
            private static readonly PropertyInfo Digit1KeyProperty = KeyboardType?.GetProperty("digit1Key", PublicInstance);
            private static readonly PropertyInfo Digit2KeyProperty = KeyboardType?.GetProperty("digit2Key", PublicInstance);
            private static readonly PropertyInfo Digit3KeyProperty = KeyboardType?.GetProperty("digit3Key", PublicInstance);
            private static readonly PropertyInfo Digit4KeyProperty = KeyboardType?.GetProperty("digit4Key", PublicInstance);
            private static readonly PropertyInfo EscapeKeyProperty = KeyboardType?.GetProperty("escapeKey", PublicInstance);

            internal static readonly PropertyInfo KeyWasPressedThisFrameProperty = ResolveKeyWasPressedProperty();

            internal static object GetMouseCurrent() => MouseCurrentProperty?.GetValue(null);

            internal static object GetKeyboardCurrent() => KeyboardCurrentProperty?.GetValue(null);

            internal static PropertyInfo GetKeyboardKeyProperty(string keyPropertyName)
            {
                switch (keyPropertyName)
                {
                    case "digit0Key": return Digit0KeyProperty;
                    case "digit1Key": return Digit1KeyProperty;
                    case "digit2Key": return Digit2KeyProperty;
                    case "digit3Key": return Digit3KeyProperty;
                    case "digit4Key": return Digit4KeyProperty;
                    case "escapeKey": return EscapeKeyProperty;
                    default: return null;
                }
            }

            private static MethodInfo GetReadValueMethod(PropertyInfo controlProperty)
            {
                Type controlType = controlProperty?.PropertyType;
                return controlType?.GetMethod("ReadValue", PublicInstance, null, Type.EmptyTypes, null);
            }

            private static PropertyInfo ResolveKeyWasPressedProperty()
            {
                Type keyControlType = Digit0KeyProperty?.PropertyType
                                      ?? Digit1KeyProperty?.PropertyType
                                      ?? Digit2KeyProperty?.PropertyType
                                      ?? Digit3KeyProperty?.PropertyType
                                      ?? Digit4KeyProperty?.PropertyType
                                      ?? EscapeKeyProperty?.PropertyType;
                return keyControlType?.GetProperty("wasPressedThisFrame", PublicInstance);
            }
        }

        private void OnValidate()
        {
            if (_config != null)
            {
                _config.Sanitize();
            }
        }
    }
}
