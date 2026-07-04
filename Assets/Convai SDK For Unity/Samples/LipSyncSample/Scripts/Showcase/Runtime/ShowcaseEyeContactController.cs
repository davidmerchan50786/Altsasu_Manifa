using System;
using System.Collections.Generic;
using UnityEngine;

namespace Convai.ShowcaseCamera
{
    /// <summary>
    /// Procedural eye contact controller for Reallusion CC4/iClone characters.
    /// Drives CC_Base_L_Eye and CC_Base_R_Eye bones with gaze behaviour:
    ///   • Convergence-correct per-eye vergence
    ///   • Main-sequence saccades (ballistic onset, deceleration, main-sequence amplitude/duration)
    ///   • Poisson-distributed blinks with asymmetric open/close easing
    ///   • Eyelid follow — upper lid retracts on up-gaze, droops on down-gaze
    ///   • Perlin-noise ocular micro-tremor (~30 Hz physiological noise floor)
    ///   • Periodic natural gaze aversion (look-away/look-back with smooth blending)
    ///   • Forced blink coinciding with large saccades (natural human behaviour)
    ///
    /// Execution order 100 ensures this runs after Reallusion's BoneDriver (default order 0).
    /// Eye bone rotations are therefore the last write each frame — overriding any prior
    /// animator or constraint state, which is the correct priority for a look-at override.
    ///
    /// Because BoneDriver's ExpressionTranspose mirrors blendshapes at order 0 but our blink
    /// blendshape writes happen at order 100, this controller resolves ALL SkinnedMeshRenderers
    /// in the hierarchy and writes blink/eyelid weights directly to each relevant SMR,
    /// bypassing the one-frame lag that would result from writing only to CC_Base_Body.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Convai/Showcase/Eye Contact Controller")]
    public sealed class ShowcaseEyeContactController : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────────

        [Header("Configuration")]
        [SerializeField] private ShowcaseEyeContactConfig _config;

        [Header("Target")]
        [Tooltip("The transform this character's eyes should track. Leave null to use Camera.main.")]
        [SerializeField] private Transform _primaryTarget;
        [SerializeField] private bool _autoFindCameraOnStart = true;

        [Header("Eye Bone Overrides")]
        [Tooltip("Optional: drag the left-eye bone here to skip auto-resolution.")]
        [SerializeField] private Transform _leftEyeBoneOverride;
        [Tooltip("Optional: drag the right-eye bone here to skip auto-resolution.")]
        [SerializeField] private Transform _rightEyeBoneOverride;

        [Header("Debug")]
        [SerializeField] private bool _drawGizmos = true;

        // ── Eye Bone State ────────────────────────────────────────────────────────

        private Transform _leftEyeBone;
        private Transform _rightEyeBone;
        private Transform _resolvedCharacterRoot;

        /// <summary>Local rest rotation captured at Start — defines the "eyes looking forward" pose.</summary>
        private Quaternion _restRotL;
        private Quaternion _restRotR;

        // Smoothed tracking angles (deg, rest-relative frame), no saccade/tremor included.
        private float _currentYawL, _currentPitchL;
        private float _currentYawR, _currentPitchR;
        private float _yawVelL, _pitchVelL, _yawVelR, _pitchVelR;

        // ── Blendshape Routes ─────────────────────────────────────────────────────

        // Each route is (SMR, blendshape index). We write to ALL matching SMRs so blink/eyelid
        // values are visible immediately this frame, independent of BoneDriver's propagation order.
        private readonly List<(SkinnedMeshRenderer smr, int idx)> _blinkLRoutes  = new();
        private readonly List<(SkinnedMeshRenderer smr, int idx)> _blinkRRoutes  = new();
        private readonly List<(SkinnedMeshRenderer smr, int idx)> _eyelidUpLRoutes   = new();
        private readonly List<(SkinnedMeshRenderer smr, int idx)> _eyelidUpRRoutes   = new();
        private readonly List<(SkinnedMeshRenderer smr, int idx)> _eyelidDownLRoutes = new();
        private readonly List<(SkinnedMeshRenderer smr, int idx)> _eyelidDownRRoutes = new();
        private readonly List<(SkinnedMeshRenderer smr, int idx)> _lookLeftLRoutes   = new();
        private readonly List<(SkinnedMeshRenderer smr, int idx)> _lookLeftRRoutes   = new();
        private readonly List<(SkinnedMeshRenderer smr, int idx)> _lookRightLRoutes  = new();
        private readonly List<(SkinnedMeshRenderer smr, int idx)> _lookRightRRoutes  = new();
        private readonly List<(SkinnedMeshRenderer smr, int idx)> _lookUpLRoutes     = new();
        private readonly List<(SkinnedMeshRenderer smr, int idx)> _lookUpRRoutes     = new();
        private readonly List<(SkinnedMeshRenderer smr, int idx)> _lookDownLRoutes   = new();
        private readonly List<(SkinnedMeshRenderer smr, int idx)> _lookDownRRoutes   = new();

        // ── Subsystem State ───────────────────────────────────────────────────────

        private SaccadeState   _saccade;
        private BlinkState     _blink;
        private LookAwayState  _lookAway;

        // Runtime weight override (changed via public API)
        private float _runtimeGazeWeight = 1f;

        // Per-eye, per-axis Perlin seed offsets — set once from instance ID for uniqueness.
        private float _tremorSeedLX, _tremorSeedLY;
        private float _tremorSeedRX, _tremorSeedRY;

        private bool _initialized;

        // ── Nested Types ──────────────────────────────────────────────────────────

        private struct SaccadeState
        {
            /// <summary>True while a saccade is executing its ballistic trajectory.</summary>
            public bool  IsActive;
            public float Timer;
            public float Duration;
            public float NextSaccadeCountdown;

            // The offset (deg) we're animating FROM and TO.
            public float FromYaw,    FromPitch;
            public float ToYaw,      ToPitch;

            // The value actually applied this frame (additive on top of base gaze).
            public float CurrentYaw, CurrentPitch;
        }

        private struct BlinkState
        {
            public enum Phase { Idle, Closing, Opening }
            public Phase CurrentPhase;
            public float Timer;
            public float NextBlinkCountdown;
            public float WeightL;
            public float WeightR;
        }

        private struct LookAwayState
        {
            public bool   IsActive;
            public float  HoldTimer;
            public float  NextLookAwayCountdown;
            public float  Weight;         // 0 = on-target, 1 = fully looking away
            public float  WeightVelocity;
            public float  SampledYaw;
            public float  SampledPitch;
            public Vector3 TargetPoint;   // Debug-only visualisation of the sampled aversion.
        }

        // ── Unity Lifecycle ───────────────────────────────────────────────────────

        private void Start()
        {
            if (_config == null)
            {
                Debug.LogError("[ShowcaseEyeContact] No ShowcaseEyeContactConfig assigned — component disabled.", this);
                enabled = false;
                return;
            }

            if (_autoFindCameraOnStart && _primaryTarget == null)
            {
                Camera main = Camera.main;
                if (main != null)
                    _primaryTarget = main.transform;
            }

            if (!TryResolveEyeBones())
            {
                Debug.LogError("[ShowcaseEyeContact] Eye bone resolution failed — component disabled.", this);
                enabled = false;
                return;
            }

            TryResolveBlendShapes();
            InitialiseSubsystems();
            _initialized = true;
        }

        private void LateUpdate()
        {
            if (!_initialized) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float t = Time.time;

            // ── Subsystem ticks ──────────────────────────────────────────────────
            TickLookAway(dt);
            TickSaccade(dt);
            TickBlink(dt);

            // ── Gaze target & base tracking ──────────────────────────────────────
            Vector3 effectiveTarget = ComputeEffectiveTarget();

            ComputeTargetAngles(effectiveTarget,
                out float targetYawL, out float targetPitchL,
                out float targetYawR, out float targetPitchR);

            float smoothTime = _config.gazeTrackingSmoothTime;
            float maxSpeed   = _config.gazeMaxTrackingSpeed;

            _currentYawL   = Mathf.SmoothDamp(_currentYawL,   targetYawL,   ref _yawVelL,   smoothTime, maxSpeed, dt);
            _currentPitchL = Mathf.SmoothDamp(_currentPitchL, targetPitchL, ref _pitchVelL, smoothTime, maxSpeed, dt);
            _currentYawR   = Mathf.SmoothDamp(_currentYawR,   targetYawR,   ref _yawVelR,   smoothTime, maxSpeed, dt);
            _currentPitchR = Mathf.SmoothDamp(_currentPitchR, targetPitchR, ref _pitchVelR, smoothTime, maxSpeed, dt);

            // ── Micro-tremor ─────────────────────────────────────────────────────
            float tremorLX = 0f, tremorLY = 0f, tremorRX = 0f, tremorRY = 0f;
            if (_config.enableMicroTremor)
            {
                float amp  = _config.microTremorAmplitudeDeg;
                float freq = _config.microTremorFrequency;
                tremorLX = (Mathf.PerlinNoise(_tremorSeedLX + t * freq, 0.5f) * 2f - 1f) * amp;
                tremorLY = (Mathf.PerlinNoise(_tremorSeedLY + t * freq, 0.5f) * 2f - 1f) * amp;
                tremorRX = (Mathf.PerlinNoise(_tremorSeedRX + t * freq, 0.5f) * 2f - 1f) * amp;
                tremorRY = (Mathf.PerlinNoise(_tremorSeedRY + t * freq, 0.5f) * 2f - 1f) * amp;
            }

            // ── Compose final angles (base + saccade + tremor) ───────────────────
            float finalYawL   = Mathf.Clamp(_currentYawL   + _saccade.CurrentYaw   + tremorLX, -_config.maxHorizontalAngleDeg, _config.maxHorizontalAngleDeg);
            float finalPitchL = Mathf.Clamp(_currentPitchL + _saccade.CurrentPitch + tremorLY, -_config.maxVerticalAngleDeg,   _config.maxVerticalAngleDeg);
            float finalYawR   = Mathf.Clamp(_currentYawR   + _saccade.CurrentYaw   + tremorRX, -_config.maxHorizontalAngleDeg, _config.maxHorizontalAngleDeg);
            float finalPitchR = Mathf.Clamp(_currentPitchR + _saccade.CurrentPitch + tremorRY, -_config.maxVerticalAngleDeg,   _config.maxVerticalAngleDeg);

            // ── Apply bone rotations ─────────────────────────────────────────────
            ApplyEyeRotation(_leftEyeBone,  _restRotL, finalYawL, finalPitchL);
            ApplyEyeRotation(_rightEyeBone, _restRotR, finalYawR, finalPitchR);

            // ── Eyelid follow (uses sustained angle, not saccade-perturbed) ──────
            TickBlendshapeCorrectives(_currentYawL, _currentPitchL, _currentYawR, _currentPitchR);
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>Assigns the transform the character's eyes will track.</summary>
        public void SetPrimaryTarget(Transform target) => _primaryTarget = target;

        /// <summary>Overrides the runtime gaze weight. 0 = eyes return to neutral, 1 = full tracking.</summary>
        public void SetGazeWeight(float weight) => _runtimeGazeWeight = Mathf.Clamp01(weight);

        /// <summary>Triggers an immediate blink if the character is not already blinking.</summary>
        public void ForceBlink()
        {
            if (_blink.CurrentPhase == BlinkState.Phase.Idle)
                BeginBlink();
        }

        // ── Initialisation ────────────────────────────────────────────────────────

        private bool TryResolveEyeBones()
        {
            _leftEyeBone  = _leftEyeBoneOverride;
            _rightEyeBone = _rightEyeBoneOverride;

            _resolvedCharacterRoot = ResolveCharacterRoot();

            if (_leftEyeBone == null)
                _leftEyeBone = FindBoneByName(_config.leftEyeBoneName);
            if (_rightEyeBone == null)
                _rightEyeBone = FindBoneByName(_config.rightEyeBoneName);

            // Humanoid avatar fallback
            if (_leftEyeBone == null || _rightEyeBone == null)
            {
                Animator anim = FindAnimatorForResolvedCharacter();
                if (anim != null && anim.isHuman)
                {
                    if (_leftEyeBone  == null) _leftEyeBone  = anim.GetBoneTransform(HumanBodyBones.LeftEye);
                    if (_rightEyeBone == null) _rightEyeBone = anim.GetBoneTransform(HumanBodyBones.RightEye);
                }
            }

            if (_leftEyeBone == null || _rightEyeBone == null)
            {
                Debug.LogWarning(
                    $"[ShowcaseEyeContact] Missing eye bone(s). " +
                    $"Left: {(_leftEyeBone != null ? _leftEyeBone.name : "NOT FOUND")}, " +
                    $"Right: {(_rightEyeBone != null ? _rightEyeBone.name : "NOT FOUND")}. " +
                    $"Check bone names in the config or assign overrides.", this);
                return false;
            }

            // Rest rotation = current local rotation when the component initialises.
            // For CC4 characters this corresponds to the idle/bind pose — eyes looking forward.
            _restRotL = _leftEyeBone.localRotation;
            _restRotR = _rightEyeBone.localRotation;
            return true;
        }

        private Transform FindBoneByName(string boneName)
        {
            if (string.IsNullOrWhiteSpace(boneName)) return null;
            foreach (Transform t in GetSearchTransforms())
            {
                if (string.Equals(t.name, boneName, StringComparison.OrdinalIgnoreCase))
                    return t;
            }
            return null;
        }

        private void TryResolveBlendShapes()
        {
            SkinnedMeshRenderer[] allSmrs = GetSearchRenderers();

            BuildRoutes(_blinkLRoutes,      allSmrs, _config.blinkLeftBlendshapeName);
            BuildRoutes(_blinkRRoutes,      allSmrs, _config.blinkRightBlendshapeName);
            BuildRoutes(_eyelidUpLRoutes,   allSmrs, _config.eyelidUpLeftBlendshapeName);
            BuildRoutes(_eyelidUpRRoutes,   allSmrs, _config.eyelidUpRightBlendshapeName);
            BuildRoutes(_eyelidDownLRoutes, allSmrs, _config.eyelidDownLeftBlendshapeName);
            BuildRoutes(_eyelidDownRRoutes, allSmrs, _config.eyelidDownRightBlendshapeName);
            BuildRoutes(_lookLeftLRoutes,   allSmrs, _config.lookLeftOnLeftEyeBlendshapeName);
            BuildRoutes(_lookLeftRRoutes,   allSmrs, _config.lookLeftOnRightEyeBlendshapeName);
            BuildRoutes(_lookRightLRoutes,  allSmrs, _config.lookRightOnLeftEyeBlendshapeName);
            BuildRoutes(_lookRightRRoutes,  allSmrs, _config.lookRightOnRightEyeBlendshapeName);
            BuildRoutes(_lookUpLRoutes,     allSmrs, _config.lookUpOnLeftEyeBlendshapeName);
            BuildRoutes(_lookUpRRoutes,     allSmrs, _config.lookUpOnRightEyeBlendshapeName);
            BuildRoutes(_lookDownLRoutes,   allSmrs, _config.lookDownOnLeftEyeBlendshapeName);
            BuildRoutes(_lookDownRRoutes,   allSmrs, _config.lookDownOnRightEyeBlendshapeName);

            if (_blinkLRoutes.Count == 0 && _config.enableBlinks)
                Debug.LogWarning($"[ShowcaseEyeContact] Blink blendshape '{_config.blinkLeftBlendshapeName}' not found on any SMR.", this);
            if (_eyelidUpLRoutes.Count == 0 && _config.enableEyelidFollow)
                Debug.LogWarning($"[ShowcaseEyeContact] Eyelid-up blendshape '{_config.eyelidUpLeftBlendshapeName}' not found. Vertical eyelid follow disabled.", this);
            if (_config.enableDirectionalCorrectives &&
                _lookLeftLRoutes.Count == 0 &&
                _lookRightLRoutes.Count == 0 &&
                _lookUpLRoutes.Count == 0 &&
                _lookDownLRoutes.Count == 0)
            {
                Debug.LogWarning("[ShowcaseEyeContact] Directional corrective blendshapes were enabled but no matching look blendshapes were found.", this);
            }
        }

        private static void BuildRoutes(
            List<(SkinnedMeshRenderer, int)> routes,
            SkinnedMeshRenderer[] allSmrs,
            string shapeName)
        {
            routes.Clear();
            if (string.IsNullOrWhiteSpace(shapeName)) return;
            foreach (SkinnedMeshRenderer smr in allSmrs)
            {
                if (smr == null || smr.sharedMesh == null) continue;
                int idx = smr.sharedMesh.GetBlendShapeIndex(shapeName);
                if (idx >= 0) routes.Add((smr, idx));
            }
        }

        private void InitialiseSubsystems()
        {
            // Unique per-instance seeds so characters in the same scene don't move in sync.
            var rng = new System.Random(gameObject.GetInstanceID());
            _tremorSeedLX = (float)(rng.NextDouble() * 1000.0);
            _tremorSeedLY = (float)(rng.NextDouble() * 1000.0);
            _tremorSeedRX = (float)(rng.NextDouble() * 1000.0);
            _tremorSeedRY = (float)(rng.NextDouble() * 1000.0);

            _saccade  = default;
            _blink    = default;
            _lookAway = default;

            ScheduleNextSaccade();
            ScheduleNextBlink();
            ScheduleNextLookAway();
        }

        private Transform ResolveCharacterRoot()
        {
            if (_leftEyeBoneOverride != null && _rightEyeBoneOverride != null)
            {
                return FindCommonAncestor(_leftEyeBoneOverride, _rightEyeBoneOverride) ?? _leftEyeBoneOverride.root;
            }

            Transform leftByName = _leftEyeBoneOverride != null ? _leftEyeBoneOverride : FindTransformByNameInHierarchy(_config.leftEyeBoneName);
            Transform rightByName = _rightEyeBoneOverride != null ? _rightEyeBoneOverride : FindTransformByNameInHierarchy(_config.rightEyeBoneName);

            if (leftByName != null && rightByName != null)
            {
                return FindCommonAncestor(leftByName, rightByName) ?? leftByName.root;
            }

            Animator[] animators = transform.root.GetComponentsInChildren<Animator>(true);
            Animator resolvedAnimator = null;
            for (int i = 0; i < animators.Length; i++)
            {
                Animator anim = animators[i];
                if (anim == null)
                {
                    continue;
                }

                Transform leftEye = anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.LeftEye) : null;
                Transform rightEye = anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.RightEye) : null;
                bool nameMatch = !anim.isHuman
                    && FindNamedChild(anim.transform, _config.leftEyeBoneName) != null
                    && FindNamedChild(anim.transform, _config.rightEyeBoneName) != null;

                if (leftEye == null && rightEye == null && !nameMatch)
                {
                    continue;
                }

                if (resolvedAnimator != null && resolvedAnimator != anim)
                {
                    Debug.LogWarning("[ShowcaseEyeContact] Multiple candidate characters found. Using the first match.", this);
                    break;
                }

                resolvedAnimator = anim;
            }

            return resolvedAnimator != null ? resolvedAnimator.transform : transform;
        }

        private Animator FindAnimatorForResolvedCharacter()
        {
            if (_resolvedCharacterRoot != null)
            {
                Animator anim = _resolvedCharacterRoot.GetComponentInChildren<Animator>(includeInactive: true);
                if (anim != null)
                {
                    return anim;
                }
            }

            return GetComponentInChildren<Animator>(includeInactive: true);
        }

        private Transform[] GetSearchTransforms()
        {
            Transform searchRoot = _resolvedCharacterRoot != null ? _resolvedCharacterRoot : transform;
            return searchRoot.GetComponentsInChildren<Transform>(includeInactive: true);
        }

        /// <summary>
        /// Collects all SkinnedMeshRenderers in the character hierarchy. Uses the hierarchy root
        /// (not just the eye-bone common ancestor) so that face/head SMRs that are siblings of
        /// the armature are included — e.g. when eyes are under Armature/Head but the mesh
        /// with blink/eyelid blendshapes is a direct child of the character root.
        /// </summary>
        private SkinnedMeshRenderer[] GetSearchRenderers()
        {
            Transform searchRoot = _resolvedCharacterRoot != null ? _resolvedCharacterRoot.root : transform.root;
            return searchRoot.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
        }

        private Transform FindTransformByNameInHierarchy(string boneName)
        {
            if (string.IsNullOrWhiteSpace(boneName))
            {
                return null;
            }

            Transform[] allTransforms = transform.root.GetComponentsInChildren<Transform>(true);
            Transform match = null;
            for (int i = 0; i < allTransforms.Length; i++)
            {
                Transform candidate = allTransforms[i];
                if (!string.Equals(candidate.name, boneName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (match != null && match != candidate)
                {
                    Debug.LogWarning($"[ShowcaseEyeContact] Multiple transforms named '{boneName}' found in hierarchy. Using the first match.", candidate);
                    return match;
                }

                match = candidate;
            }

            return match;
        }

        private static Transform FindNamedChild(Transform root, string boneName)
        {
            if (root == null || string.IsNullOrWhiteSpace(boneName))
            {
                return null;
            }

            Transform[] children = root.GetComponentsInChildren<Transform>(includeInactive: true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (string.Equals(child.name, boneName, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }

            return null;
        }

        private static Transform FindCommonAncestor(Transform a, Transform b)
        {
            if (a == null || b == null)
            {
                return null;
            }

            var ancestors = new HashSet<Transform>();
            for (Transform current = a; current != null; current = current.parent)
            {
                ancestors.Add(current);
            }

            for (Transform current = b; current != null; current = current.parent)
            {
                if (ancestors.Contains(current))
                {
                    return current;
                }
            }

            return null;
        }

        // ── Gaze Computation ──────────────────────────────────────────────────────

        private Vector3 ComputeEffectiveTarget()
        {
            if (_primaryTarget == null)
            {
                // Gaze at a point 3 m ahead of the head midpoint.
                Vector3 mid = (_leftEyeBone.position + _rightEyeBone.position) * 0.5f;
                return mid + (_leftEyeBone.parent != null ? _leftEyeBone.parent.forward : transform.forward) * 3f;
            }

            return ResolvePrimaryTargetPosition();
        }

        private bool ShouldUseSharedDirectionForTarget()
        {
            if (_config == null)
            {
                return false;
            }

            if (_config.disableVergenceDuringLookAway && _lookAway.Weight > 0.001f)
            {
                return true;
            }

            if (!_config.disableVergenceForCameraTargets || _primaryTarget == null)
            {
                return false;
            }

            return _primaryTarget.GetComponent<Camera>() != null;
        }

        private Vector3 ResolvePrimaryTargetPosition()
        {
            return _primaryTarget.position;
        }

        private void ComputeTargetAngles(
            Vector3 target,
            out float yawL, out float pitchL,
            out float yawR, out float pitchR)
        {
            bool useSharedDirection = ShouldUseSharedDirectionForTarget();
            ComputeRawTargetAngles(target, useSharedDirection, out yawL, out pitchL, out yawR, out pitchR);

            if (_lookAway.Weight > 0.001f)
            {
                // Apply aversion directly in the calibrated eye-local frame rather than by
                // rebuilding a world-space target point. This avoids basis flips and wrap
                // discontinuities that can push the eyes toward the corners.
                float lookAwayWeight = HermiteCurve(_lookAway.Weight);
                float yawOffset = _lookAway.SampledYaw * lookAwayWeight;
                float pitchOffset = _lookAway.SampledPitch * lookAwayWeight;

                yawL += yawOffset;
                pitchL += pitchOffset;
                yawR += yawOffset;
                pitchR += pitchOffset;
            }

            // Clamp to anatomical limits.
            yawL   = Mathf.Clamp(yawL,   -_config.maxHorizontalAngleDeg, _config.maxHorizontalAngleDeg);
            pitchL = Mathf.Clamp(pitchL, -_config.maxVerticalAngleDeg,   _config.maxVerticalAngleDeg);
            yawR   = Mathf.Clamp(yawR,   -_config.maxHorizontalAngleDeg, _config.maxHorizontalAngleDeg);
            pitchR = Mathf.Clamp(pitchR, -_config.maxVerticalAngleDeg,   _config.maxVerticalAngleDeg);

            // Master + runtime gaze weight: zero-weight = eyes sit in neutral (zero-angle) pose.
            float w = _config.masterGazeWeight * _runtimeGazeWeight;
            yawL   *= w;  pitchL *= w;
            yawR   *= w;  pitchR *= w;

        }

        private void ComputeRawTargetAngles(
            Vector3 target,
            bool useSharedDirection,
            out float yawL, out float pitchL,
            out float yawR, out float pitchR)
        {
            GetEyeBasis(out Vector3 forwardAxis, out Vector3 upAxis, out Vector3 rightAxis);

            if (_config.enableVergence && !useSharedDirection)
            {
                DecomposeEyeDirection(_leftEyeBone, _restRotL, target, forwardAxis, upAxis, rightAxis, out yawL, out pitchL);
                DecomposeEyeDirection(_rightEyeBone, _restRotR, target, forwardAxis, upAxis, rightAxis, out yawR, out pitchR);
                return;
            }

            Vector3 midpoint = (_leftEyeBone.position + _rightEyeBone.position) * 0.5f;
            Vector3 worldDir = (target - midpoint).normalized;
            DecomposeEyeDirectionFromWorldDir(_leftEyeBone, _restRotL, worldDir, forwardAxis, upAxis, rightAxis, out yawL, out pitchL);
            DecomposeEyeDirectionFromWorldDir(_rightEyeBone, _restRotR, worldDir, forwardAxis, upAxis, rightAxis, out yawR, out pitchR);
        }

        /// <summary>
        /// Decomposes the world-space direction from <paramref name="eyeBone"/> to
        /// <paramref name="targetWorld"/> into rest-relative yaw and pitch angles (degrees).
        ///
        /// The rest-relative frame is defined by <paramref name="restRot"/>: after transforming
        /// the world direction into this frame, the configured eye basis defines forward/up/right.
        /// This makes angle extraction independent of the bone's bind-pose orientation.
        /// </summary>
        private static void DecomposeEyeDirection(
            Transform eyeBone, Quaternion restRot, Vector3 targetWorld,
            Vector3 forwardAxis, Vector3 upAxis, Vector3 rightAxis,
            out float yaw, out float pitch)
        {
            Vector3 worldDir = (targetWorld - eyeBone.position).normalized;
            DecomposeEyeDirectionFromWorldDir(eyeBone, restRot, worldDir, forwardAxis, upAxis, rightAxis, out yaw, out pitch);
        }

        private static void DecomposeEyeDirectionFromWorldDir(
            Transform eyeBone, Quaternion restRot, Vector3 worldDir,
            Vector3 forwardAxis, Vector3 upAxis, Vector3 rightAxis,
            out float yaw, out float pitch)
        {
            // 1. Transform to parent-local space.
            Vector3 localDir = eyeBone.parent != null
                ? eyeBone.parent.InverseTransformDirection(worldDir)
                : worldDir;

            // 2. Transform to the rest-relative local frame.
            Vector3 restRelDir = Quaternion.Inverse(restRot) * localDir;

            // 3. Decompose against the configured eyeball basis.
            float forwardDot = Vector3.Dot(restRelDir, forwardAxis);
            float rightDot = Vector3.Dot(restRelDir, rightAxis);
            float upDot = Vector3.Dot(restRelDir, upAxis);
            float horizontalMag = Mathf.Sqrt((forwardDot * forwardDot) + (rightDot * rightDot));

            yaw = Mathf.Atan2(rightDot, forwardDot) * Mathf.Rad2Deg;
            pitch = -Mathf.Atan2(upDot, horizontalMag) * Mathf.Rad2Deg;
        }

        private void ApplyEyeRotation(Transform eyeBone, Quaternion restRot, float yaw, float pitch)
        {
            GetEyeBasis(out Vector3 forwardAxis, out Vector3 upAxis, out Vector3 rightAxis);

            float yawRad = yaw * Mathf.Deg2Rad;
            float pitchRad = pitch * Mathf.Deg2Rad;
            float cosPitch = Mathf.Cos(pitchRad);

            Vector3 desiredRestDirection =
                (forwardAxis * (cosPitch * Mathf.Cos(yawRad))) +
                (rightAxis * (cosPitch * Mathf.Sin(yawRad))) +
                (upAxis * -Mathf.Sin(pitchRad));

            Quaternion basisRotation = Quaternion.LookRotation(forwardAxis, upAxis);
            Quaternion targetRotation = Quaternion.LookRotation(desiredRestDirection.normalized, upAxis);

            // Rest rotation encodes the neutral eyeball orientation. We reconstruct a target
            // direction in the calibrated rest frame and convert it back to a local rotation exactly.
            eyeBone.localRotation = restRot * (targetRotation * Quaternion.Inverse(basisRotation));
        }

        private void GetEyeBasis(out Vector3 forwardAxis, out Vector3 upAxis, out Vector3 rightAxis)
        {
            forwardAxis = ShowcaseEyeContactConfig.AxisToVector(_config.eyeForwardAxis).normalized;
            upAxis = ShowcaseEyeContactConfig.AxisToVector(_config.eyeUpAxis).normalized;

            if (Mathf.Abs(Vector3.Dot(forwardAxis, upAxis)) > 0.999f)
            {
                upAxis = Mathf.Abs(Vector3.Dot(forwardAxis, Vector3.forward)) < 0.999f
                    ? Vector3.forward
                    : Vector3.up;
            }

            rightAxis = Vector3.Cross(upAxis, forwardAxis).normalized;
            upAxis = Vector3.Cross(forwardAxis, rightAxis).normalized;
        }

        // ── Eyelid Follow ─────────────────────────────────────────────────────────

        private void TickBlendshapeCorrectives(float yawL, float pitchL, float yawR, float pitchR)
        {
            float blinkMaskL = 1f - _blink.WeightL;
            float blinkMaskR = 1f - _blink.WeightR;

            float upL = EvaluateDirectionalWeight(pitchL, _config.maxVerticalAngleDeg);
            float downL = EvaluateDirectionalWeight(-pitchL, _config.maxVerticalAngleDeg);
            float upR = EvaluateDirectionalWeight(pitchR, _config.maxVerticalAngleDeg);
            float downR = EvaluateDirectionalWeight(-pitchR, _config.maxVerticalAngleDeg);

            if (_config.enableEyelidFollow)
            {
                WriteRoutes(_eyelidUpLRoutes, upL * _config.eyelidLookUpStrength * 100f * blinkMaskL);
                WriteRoutes(_eyelidUpRRoutes, upR * _config.eyelidLookUpStrength * 100f * blinkMaskR);
                WriteRoutes(_eyelidDownLRoutes, downL * _config.eyelidLookDownStrength * 100f * blinkMaskL);
                WriteRoutes(_eyelidDownRRoutes, downR * _config.eyelidLookDownStrength * 100f * blinkMaskR);
            }
            else
            {
                WriteRoutes(_eyelidUpLRoutes, 0f);
                WriteRoutes(_eyelidUpRRoutes, 0f);
                WriteRoutes(_eyelidDownLRoutes, 0f);
                WriteRoutes(_eyelidDownRRoutes, 0f);
            }

            if (!_config.enableDirectionalCorrectives)
            {
                WriteDirectionalCorrectiveRoutes(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
                return;
            }

            float lookAwayCorrectiveScale = Mathf.Lerp(1f, _config.lookAwayCorrectiveMultiplier, _lookAway.Weight);
            float horizontalStrength = _config.horizontalCorrectiveStrength * lookAwayCorrectiveScale;
            float verticalStrength = _config.verticalCorrectiveStrength * lookAwayCorrectiveScale;

            float leftL = EvaluateDirectionalWeight(-yawL, _config.maxHorizontalAngleDeg) * horizontalStrength * blinkMaskL;
            float leftR = EvaluateDirectionalWeight(-yawR, _config.maxHorizontalAngleDeg) * horizontalStrength * blinkMaskR;
            float rightL = EvaluateDirectionalWeight(yawL, _config.maxHorizontalAngleDeg) * horizontalStrength * blinkMaskL;
            float rightR = EvaluateDirectionalWeight(yawR, _config.maxHorizontalAngleDeg) * horizontalStrength * blinkMaskR;
            float correctiveUpL = upL * verticalStrength * blinkMaskL;
            float correctiveUpR = upR * verticalStrength * blinkMaskR;
            float correctiveDownL = downL * verticalStrength * blinkMaskL;
            float correctiveDownR = downR * verticalStrength * blinkMaskR;

            WriteDirectionalCorrectiveRoutes(
                leftL * 100f,
                leftR * 100f,
                rightL * 100f,
                rightR * 100f,
                correctiveUpL * 100f,
                correctiveUpR * 100f,
                correctiveDownL * 100f,
                correctiveDownR * 100f);
        }

        // ── Saccade Simulation ────────────────────────────────────────────────────

        private void TickSaccade(float dt)
        {
            if (!_config.enableSaccades)
            {
                _saccade.CurrentYaw   = 0f;
                _saccade.CurrentPitch = 0f;
                return;
            }

            if (_saccade.IsActive)
            {
                _saccade.Timer += dt;
                float t = Mathf.Clamp01(_saccade.Timer / _saccade.Duration);

                // Asymmetric ease: fast ballistic onset (power 0.35), smooth deceleration (SmoothStep).
                float eased = BallisticEase(t);
                _saccade.CurrentYaw   = Mathf.Lerp(_saccade.FromYaw,   _saccade.ToYaw,   eased);
                _saccade.CurrentPitch = Mathf.Lerp(_saccade.FromPitch, _saccade.ToPitch, eased);

                if (t >= 1f)
                {
                    _saccade.IsActive    = false;
                    _saccade.FromYaw     = _saccade.ToYaw;
                    _saccade.FromPitch   = _saccade.ToPitch;
                    _saccade.CurrentYaw  = _saccade.ToYaw;
                    _saccade.CurrentPitch = _saccade.ToPitch;
                    ScheduleNextSaccade();
                }
            }
            else
            {
                _saccade.NextSaccadeCountdown -= dt;

                // Slow drift back toward (0,0) between saccades (fixation drift correction).
                float returnSpeed = _config.saccadeDriftReturnSpeed * dt;
                _saccade.FromYaw   = Mathf.MoveTowards(_saccade.FromYaw,   0f, returnSpeed);
                _saccade.FromPitch = Mathf.MoveTowards(_saccade.FromPitch, 0f, returnSpeed);
                _saccade.CurrentYaw   = _saccade.FromYaw;
                _saccade.CurrentPitch = _saccade.FromPitch;

                if (_saccade.NextSaccadeCountdown <= 0f)
                    TriggerSaccade();
            }
        }

        private void TriggerSaccade()
        {
            // Random excursion from the current fixation offset.
            float amplitude = UnityEngine.Random.Range(0.3f, _config.saccadeMaxAmplitudeDeg);
            float angle     = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;

            float deltaYaw   =  Mathf.Cos(angle) * amplitude;
            float deltaPitch =  Mathf.Sin(angle) * amplitude * 0.55f; // vertical range smaller than horizontal

            float newToYaw   = Mathf.Clamp(_saccade.FromYaw   + deltaYaw,   -_config.saccadeMaxAmplitudeDeg,        _config.saccadeMaxAmplitudeDeg);
            float newToPitch = Mathf.Clamp(_saccade.FromPitch + deltaPitch, -_config.saccadeMaxAmplitudeDeg * 0.55f, _config.saccadeMaxAmplitudeDeg * 0.55f);

            float excursionMag = Mathf.Sqrt(
                (newToYaw   - _saccade.FromYaw)   * (newToYaw   - _saccade.FromYaw) +
                (newToPitch - _saccade.FromPitch) * (newToPitch - _saccade.FromPitch));

            // Main-sequence duration: empirical ~2.2 ms/deg + 21 ms intercept (Carpenter 1988).
            _saccade.Duration   = Mathf.Clamp((2.2f * excursionMag + 21f) * 0.001f, 0.014f, 0.11f);
            _saccade.ToYaw      = newToYaw;
            _saccade.ToPitch    = newToPitch;
            _saccade.Timer      = 0f;
            _saccade.IsActive   = true;

            // Blink synchronisation: humans blink during saccades ~20–30% of the time
            // (more often for larger excursions).
            float blinkProbability = Mathf.Lerp(0.1f, 0.3f, excursionMag / _config.saccadeMaxAmplitudeDeg);
            if (UnityEngine.Random.value < blinkProbability)
                ForceBlink();
        }

        private void ScheduleNextSaccade()
        {
            _saccade.NextSaccadeCountdown = SampleGaussianPositive(
                _config.saccadeMeanInterval,
                _config.saccadeIntervalStdDev);
        }

        /// <summary>
        /// Ballistic saccade ease: sharp acceleration phase (power 0.35) followed by
        /// a smooth deceleration (smoothstep). Matches the asymmetric velocity profile
        /// of real saccades (fast onset, slow tail).
        /// </summary>
        private static float BallisticEase(float t)
        {
            // Fast onset: t^0.35 climbs quickly
            float onset = Mathf.Pow(t, 0.35f);
            // Smooth deceleration: standard smoothstep
            float smooth = t * t * (3f - 2f * t);
            // Blend: onset dominates early, smooth dominates late
            return Mathf.Lerp(onset, smooth, t);
        }

        // ── Blink Controller ──────────────────────────────────────────────────────

        private void TickBlink(float dt)
        {
            if (!_config.enableBlinks)
            {
                WriteRoutes(_blinkLRoutes, 0f);
                WriteRoutes(_blinkRRoutes, 0f);
                return;
            }

            switch (_blink.CurrentPhase)
            {
                case BlinkState.Phase.Idle:
                    _blink.NextBlinkCountdown -= dt;
                    if (_blink.NextBlinkCountdown <= 0f)
                        BeginBlink();
                    break;

                case BlinkState.Phase.Closing:
                    _blink.Timer += dt;
                    float closeT = Mathf.Clamp01(_blink.Timer / _config.blinkCloseDuration);
                    float closedWeight = HermiteCurve(closeT);
                    _blink.WeightL = closedWeight;
                    _blink.WeightR = closedWeight;
                    if (closeT >= 1f)
                    {
                        _blink.CurrentPhase = BlinkState.Phase.Opening;
                        _blink.Timer        = 0f;
                    }
                    break;

                case BlinkState.Phase.Opening:
                    _blink.Timer += dt;
                    float openT = Mathf.Clamp01(_blink.Timer / _config.blinkOpenDuration);
                    float openWeight = HermiteCurve(1f - openT);
                    _blink.WeightL = openWeight;
                    _blink.WeightR = openWeight;
                    if (openT >= 1f)
                    {
                        _blink.WeightL      = 0f;
                        _blink.WeightR      = 0f;
                        _blink.CurrentPhase = BlinkState.Phase.Idle;
                        ScheduleNextBlink();
                    }
                    break;
            }

            WriteRoutes(_blinkLRoutes, _blink.WeightL * 100f);
            WriteRoutes(_blinkRRoutes, _blink.WeightR * 100f);
        }

        private void BeginBlink()
        {
            _blink.CurrentPhase = BlinkState.Phase.Closing;
            _blink.Timer        = 0f;
        }

        private void ScheduleNextBlink()
        {
            float meanInterval = 60f / _config.blinkRatePerMinute;
            _blink.NextBlinkCountdown = SampleGaussianPositive(meanInterval, _config.blinkIntervalStdDev);
        }

        // ── Look-Away Behaviour ───────────────────────────────────────────────────

        private void TickLookAway(float dt)
        {
            if (!_config.enableLookAway)
            {
                _lookAway.IsActive = false;
                _lookAway.Weight = 0f;
                _lookAway.WeightVelocity = 0f;
                return;
            }

            float targetWeight;

            if (_lookAway.IsActive)
            {
                _lookAway.HoldTimer -= dt;
                targetWeight = _lookAway.HoldTimer > 0f ? 1f : 0f;

                _lookAway.Weight = Mathf.SmoothDamp(
                    _lookAway.Weight, targetWeight,
                    ref _lookAway.WeightVelocity,
                    _config.lookAwaySmoothTime,
                    float.PositiveInfinity, dt);

                // Return is complete once weight has settled back to ~zero.
                if (_lookAway.HoldTimer <= 0f && _lookAway.Weight < 0.01f)
                {
                    _lookAway.IsActive = false;
                    _lookAway.Weight   = 0f;
                    ScheduleNextLookAway();
                }
            }
            else
            {
                _lookAway.NextLookAwayCountdown -= dt;
                if (_lookAway.NextLookAwayCountdown <= 0f)
                    TriggerLookAway();
            }
        }

        private void TriggerLookAway()
        {
            _lookAway.IsActive = true;
            _lookAway.HoldTimer = Mathf.Max(
                0.1f,
                SampleGaussianPositive(_config.lookAwayHoldDuration, _config.lookAwayHoldDurationStdDev));
            _lookAway.WeightVelocity = 0f;

            Vector3 eyeMid = (_leftEyeBone.position + _rightEyeBone.position) * 0.5f;
            Vector3 focusPoint = ResolveLookAwayFocusPoint(eyeMid);
            SampleLookAwayAngles(out _lookAway.SampledYaw, out _lookAway.SampledPitch);
            _lookAway.TargetPoint = BuildLookAwayTargetPoint(eyeMid, focusPoint, _lookAway.SampledYaw, _lookAway.SampledPitch);
        }

        private Vector3 ResolveLookAwayFocusPoint(Vector3 eyeMid)
        {
            if (_primaryTarget != null)
            {
                return ResolvePrimaryTargetPosition();
            }

            Transform headRef = _leftEyeBone != null && _leftEyeBone.parent != null ? _leftEyeBone.parent : transform;
            return eyeMid + (headRef.forward * Mathf.Max(3f, _config.lookAwayTargetDistance));
        }

        private void SampleLookAwayAngles(out float yaw, out float pitch)
        {
            float horizontalLimit = Mathf.Max(1f, _config.maxHorizontalAngleDeg - _config.lookAwaySafeMarginDeg);
            float verticalLimit = Mathf.Max(0f, _config.maxVerticalAngleDeg - _config.lookAwaySafeMarginDeg);

            float maxHorizontal = Mathf.Min(_config.lookAwayHorizontalAngleDeg, horizontalLimit);
            float maxVertical = Mathf.Min(_config.lookAwayVerticalRangeDeg, verticalLimit);
            if (maxHorizontal <= 0f)
            {
                yaw = 0f;
                pitch = 0f;
                return;
            }

            float minHorizontal = Mathf.Min(_config.lookAwayMinHorizontalAngleDeg, maxHorizontal);

            // Look-away as a direct angular offset (rather than a sampled point on an ellipse)
            // keeps behaviour stable. Bias toward smaller offsets so aversion reads as subtle.
            float horizontalMagnitude = Mathf.Lerp(minHorizontal, maxHorizontal, Mathf.Pow(UnityEngine.Random.value, 1.2f));
            yaw = horizontalMagnitude * (UnityEngine.Random.value < 0.5f ? -1f : 1f);

            if (maxVertical <= 0f)
            {
                pitch = 0f;
                return;
            }

            float verticalMagnitude = maxVertical * Mathf.Pow(UnityEngine.Random.value, 2.4f) * 0.45f;
            float verticalSign = UnityEngine.Random.value < 0.65f ? 1f : -1f;
            pitch = Mathf.Clamp(verticalMagnitude * verticalSign, -maxVertical, maxVertical);
        }

        private Vector3 BuildLookAwayTargetPoint(Vector3 eyeMid, Vector3 focusPoint, float yaw, float pitch)
        {
            Vector3 toFocus = focusPoint - eyeMid;
            Vector3 viewDirection = toFocus.sqrMagnitude > 1e-6f ? toFocus.normalized : transform.forward;
            GetLookAwayPlaneBasis(viewDirection, out Vector3 planeRight, out Vector3 planeUp);

            float focusDistance = Mathf.Max(0.25f, toFocus.magnitude);
            float rightOffset = Mathf.Tan(yaw * Mathf.Deg2Rad) * focusDistance;
            float upOffset = Mathf.Tan(-pitch * Mathf.Deg2Rad) * focusDistance;
            return focusPoint + (planeRight * rightOffset) + (planeUp * upOffset);
        }

        private void GetLookAwayPlaneBasis(Vector3 viewDirection, out Vector3 planeRight, out Vector3 planeUp)
        {
            Transform basisRef = _primaryTarget != null ? _primaryTarget : (_leftEyeBone != null && _leftEyeBone.parent != null ? _leftEyeBone.parent : transform);
            planeRight = Vector3.ProjectOnPlane(basisRef.right, viewDirection).normalized;
            if (planeRight.sqrMagnitude < 1e-6f)
            {
                planeRight = Vector3.ProjectOnPlane(transform.right, viewDirection).normalized;
            }

            if (planeRight.sqrMagnitude < 1e-6f)
            {
                planeRight = Vector3.Cross(Vector3.up, viewDirection).normalized;
            }

            if (planeRight.sqrMagnitude < 1e-6f)
            {
                planeRight = Vector3.Cross(Vector3.forward, viewDirection).normalized;
            }

            planeUp = Vector3.Cross(viewDirection, planeRight).normalized;
            if (planeUp.sqrMagnitude < 1e-6f)
            {
                planeUp = Vector3.ProjectOnPlane(basisRef.up, viewDirection).normalized;
            }
        }

        private void ScheduleNextLookAway()
        {
            _lookAway.NextLookAwayCountdown = SampleGaussianPositive(
                _config.lookAwayMeanInterval,
                _config.lookAwayIntervalStdDev);
        }

        // ── Blendshape Utilities ──────────────────────────────────────────────────

        private static void WriteRoutes(List<(SkinnedMeshRenderer smr, int idx)> routes, float value)
        {
            float clampedValue = Mathf.Clamp(value, 0f, 100f);
            foreach ((SkinnedMeshRenderer smr, int idx) in routes)
            {
                if (smr != null)
                    smr.SetBlendShapeWeight(idx, clampedValue);
            }
        }

        private void WriteDirectionalCorrectiveRoutes(
            float lookLeftL,
            float lookLeftR,
            float lookRightL,
            float lookRightR,
            float lookUpL,
            float lookUpR,
            float lookDownL,
            float lookDownR)
        {
            WriteRoutes(_lookLeftLRoutes, lookLeftL);
            WriteRoutes(_lookLeftRRoutes, lookLeftR);
            WriteRoutes(_lookRightLRoutes, lookRightL);
            WriteRoutes(_lookRightRRoutes, lookRightR);
            WriteRoutes(_lookUpLRoutes, lookUpL);
            WriteRoutes(_lookUpRRoutes, lookUpR);
            WriteRoutes(_lookDownLRoutes, lookDownL);
            WriteRoutes(_lookDownRRoutes, lookDownR);
        }

        private static float EvaluateDirectionalWeight(float signedAngle, float maxAngle)
        {
            if (maxAngle <= 0f)
            {
                return 0f;
            }

            float normalized = Mathf.Clamp01(signedAngle / maxAngle);
            return Mathf.Pow(normalized, 1.35f);
        }

        // ── Math Utilities ────────────────────────────────────────────────────────

        /// <summary>
        /// Hermite smooth-step S-curve: zero derivative at both endpoints.
        /// Used for blink easing — natural eyelid motion profile.
        /// </summary>
        private static float HermiteCurve(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Box-Muller Gaussian sample clamped to a plausible positive range.
        /// Used for biologically-realistic inter-event timing (blinks, saccades, look-aways).
        /// </summary>
        private static float SampleGaussianPositive(float mean, float stdDev)
        {
            float u1 = Mathf.Max(1e-6f, UnityEngine.Random.value);
            float u2 = UnityEngine.Random.value;
            float z  = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
            // Clamp to [mean * 0.2, mean * 3] to avoid degenerate near-zero or infinite waits.
            return Mathf.Clamp(mean + z * stdDev, mean * 0.2f, mean * 3f);
        }

        // ── Debug Gizmos ──────────────────────────────────────────────────────────

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!_drawGizmos || (!_initialized && Application.isPlaying)) return;

            Transform lEye = _leftEyeBone  ?? _leftEyeBoneOverride;
            Transform rEye = _rightEyeBone ?? _rightEyeBoneOverride;

            if (lEye != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(lEye.position, 0.005f);
                UnityEditor.Handles.color = Color.cyan;
                UnityEditor.Handles.ArrowHandleCap(0, lEye.position, lEye.rotation, 0.04f, EventType.Repaint);
            }

            if (rEye != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(rEye.position, 0.005f);
                UnityEditor.Handles.color = Color.cyan;
                UnityEditor.Handles.ArrowHandleCap(0, rEye.position, rEye.rotation, 0.04f, EventType.Repaint);
            }

            if (_primaryTarget != null && lEye != null && rEye != null)
            {
                Vector3 mid = (lEye.position + rEye.position) * 0.5f;
                Vector3 primaryTargetPosition = ResolvePrimaryTargetPosition();
                Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.7f);
                Gizmos.DrawLine(mid, primaryTargetPosition);
                Gizmos.DrawWireSphere(primaryTargetPosition, 0.015f);
            }

            if (_initialized && _lookAway.IsActive)
            {
                Gizmos.color = new Color(1f, 0.55f, 0f, 0.9f);
                Gizmos.DrawWireSphere(_lookAway.TargetPoint, 0.02f);
                if (lEye != null && rEye != null)
                {
                    Vector3 mid = (lEye.position + rEye.position) * 0.5f;
                    Gizmos.DrawLine(mid, _lookAway.TargetPoint);
                }
            }
        }
#endif
    }
}
