using UnityEngine;

namespace Convai.ShowcaseCamera
{
    /// <summary>
    /// Shared configuration for <see cref="ShowcaseEyeContactController"/>.
    /// One asset can be shared across multiple characters in the same scene.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ShowcaseEyeContactConfig",
        menuName = "Convai/Showcase/Eye Contact Config")]
    public sealed class ShowcaseEyeContactConfig : ScriptableObject
    {
        public enum LocalAxisDirection
        {
            PositiveX,
            NegativeX,
            PositiveY,
            NegativeY,
            PositiveZ,
            NegativeZ
        }

        // ── Bone Names ────────────────────────────────────────────────────────────

        [Header("Eye Bone Names")]
        [Tooltip("Transform name of the left-eye bone (CC4 default: CC_Base_L_Eye).")]
        public string leftEyeBoneName = "CC_Base_L_Eye";

        [Tooltip("Transform name of the right-eye bone (CC4 default: CC_Base_R_Eye).")]
        public string rightEyeBoneName = "CC_Base_R_Eye";

        [Header("Eye Bone Basis")]
        [Tooltip("Which local axis points forward from the eyeball in the eye bone rest pose. Reallusion CC eyes typically use -Y.")]
        public LocalAxisDirection eyeForwardAxis = LocalAxisDirection.NegativeY;

        [Tooltip("Which local axis points upward from the eyeball in the eye bone rest pose. Reallusion CC eyes typically use +Z.")]
        public LocalAxisDirection eyeUpAxis = LocalAxisDirection.PositiveZ;

        // ── Gaze ──────────────────────────────────────────────────────────────────

        [Header("Gaze")]
        [Tooltip("Smooth time (seconds) for eye tracking via SmoothDamp. Lower = snappier acquisition.")]
        [Range(0.01f, 0.3f)]
        public float gazeTrackingSmoothTime = 0.055f;

        [Tooltip("When targeting a camera, use a shared midpoint direction instead of per-eye vergence.")]
        public bool disableVergenceForCameraTargets = true;

        [Tooltip("Use a shared midpoint direction during look-away so the eyes do not over-converge on the off-axis target.")]
        public bool disableVergenceDuringLookAway = true;

        [Tooltip("Maximum eye tracking speed (deg/sec). Clamps SmoothDamp overshoot.")]
        [Range(30f, 720f)]
        public float gazeMaxTrackingSpeed = 220f;

        [Tooltip("Maximum horizontal rotation from neutral (anatomical limit ~35°).")]
        [Range(10f, 50f)]
        public float maxHorizontalAngleDeg = 32f;

        [Tooltip("Maximum vertical rotation from neutral (anatomical limit ~25°).")]
        [Range(5f, 40f)]
        public float maxVerticalAngleDeg = 20f;

        [Tooltip("Master gaze weight. 0 = eyes rest in neutral pose, 1 = full tracking.")]
        [Range(0f, 1f)]
        public float masterGazeWeight = 1f;

        // ── Vergence ──────────────────────────────────────────────────────────────

        [Header("Vergence")]
        [Tooltip("Per-eye vergence: each eye aims independently at the target, " +
                 "producing realistic convergence at close distances.")]
        public bool enableVergence = true;

        // ── Saccades ──────────────────────────────────────────────────────────────

        [Header("Saccades")]
        [Tooltip("Enable biologically-accurate saccadic eye movements during fixation.")]
        public bool enableSaccades = true;

        [Tooltip("Mean inter-saccade interval (seconds). Human fixation mean ~2–4 s.")]
        [Range(0.5f, 8f)]
        public float saccadeMeanInterval = 3.2f;

        [Tooltip("Standard deviation of inter-saccade interval (Gaussian approximation).")]
        [Range(0f, 3f)]
        public float saccadeIntervalStdDev = 0.85f;

        [Tooltip("Maximum saccade excursion amplitude (degrees). Human microsaccade range ~0.5–4°.")]
        [Range(0.3f, 8f)]
        public float saccadeMaxAmplitudeDeg = 2.1f;

        [Tooltip("Speed at which the saccade offset drifts back to zero between saccades (deg/sec).")]
        [Range(0.1f, 5f)]
        public float saccadeDriftReturnSpeed = 1.1f;

        // ── Micro-Tremor ──────────────────────────────────────────────────────────

        [Header("Micro-Tremor")]
        [Tooltip("Enable high-frequency physiological noise (~30 Hz ocular micro-tremor).")]
        public bool enableMicroTremor = true;

        [Tooltip("Amplitude of micro-tremor in degrees. Human baseline ~0.06–0.10°.")]
        [Range(0f, 0.5f)]
        public float microTremorAmplitudeDeg = 0.035f;

        [Tooltip("Perlin noise sampling frequency controlling the perceived tremor rate.")]
        [Range(5f, 80f)]
        public float microTremorFrequency = 24f;

        // ── Blink ─────────────────────────────────────────────────────────────────

        [Header("Blink")]
        public bool enableBlinks = true;

        [Tooltip("Mean blinks per minute (human resting average ~12–20; showcase: 15).")]
        [Range(4f, 30f)]
        public float blinkRatePerMinute = 14f;

        [Tooltip("Standard deviation of inter-blink interval (Gaussian approximation).")]
        [Range(0.1f, 5f)]
        public float blinkIntervalStdDev = 1.1f;

        [Tooltip("Duration of the eyelid CLOSING phase (seconds). Human ~80 ms.")]
        [Range(0.04f, 0.2f)]
        public float blinkCloseDuration = 0.06f;

        [Tooltip("Duration of the eyelid OPENING phase (seconds). Human ~180 ms.")]
        [Range(0.08f, 0.45f)]
        public float blinkOpenDuration = 0.14f;

        [Header("Blink Blendshape Names")]
        [Tooltip("Blendshape name for left-eye blink on any SkinnedMeshRenderer in the hierarchy.")]
        public string blinkLeftBlendshapeName = "Eye_Blink_L";

        [Tooltip("Blendshape name for right-eye blink.")]
        public string blinkRightBlendshapeName = "Eye_Blink_R";

        // ── Eyelid Follow ─────────────────────────────────────────────────────────

        [Header("Eyelid Follow")]
        [Tooltip("Eyelid follow: upper lid retracts slightly when looking up, " +
                 "droops slightly when looking down. Improves natural appearance when looking up or down.")]
        public bool enableEyelidFollow = true;

        [Tooltip("Strength of upper-lid retraction when looking upward (0–1).")]
        [Range(0f, 1f)]
        public float eyelidLookUpStrength = 0.18f;

        [Tooltip("Strength of upper-lid droop when looking downward (0–1).")]
        [Range(0f, 1f)]
        public float eyelidLookDownStrength = 0.16f;

        [Tooltip("Eyelid-up blendshape for left eye (lid retraction / eye-widening on upward gaze). " +
                 "Empty = skip.")]
        public string eyelidUpLeftBlendshapeName = "Eye_Wide_L";

        [Tooltip("Eyelid-up blendshape for right eye.")]
        public string eyelidUpRightBlendshapeName = "Eye_Wide_R";

        [Tooltip("Eyelid-down blendshape for left eye (lid droop on downward gaze). " +
                 "Empty = skip (CC4 lid-droop is typically handled by the head mesh deform).")]
        public string eyelidDownLeftBlendshapeName = "Eye_Squint_L";

        [Tooltip("Eyelid-down blendshape for right eye. Empty = skip.")]
        public string eyelidDownRightBlendshapeName = "Eye_Squint_R";

        [Header("Directional Corrective Blendshapes")]
        [Tooltip("Drives corrective look blendshapes alongside bone rotation. Useful for CC/Reallusion eyes that expose directional eye shapes.")]
        public bool enableDirectionalCorrectives = true;

        [Tooltip("Strength of horizontal corrective look blendshapes.")]
        [Range(0f, 1f)]
        public float horizontalCorrectiveStrength = 0.14f;

        [Tooltip("Strength of vertical corrective look blendshapes.")]
        [Range(0f, 1f)]
        public float verticalCorrectiveStrength = 0.1f;

        [Tooltip("How much directional corrective blendshapes remain active during look-away. Low values prevent the eyes from snapping into corner shapes.")]
        [Range(0f, 1f)]
        public float lookAwayCorrectiveMultiplier;

        [Tooltip("Blendshape for left eye looking left.")]
        public string lookLeftOnLeftEyeBlendshapeName = "Eye_L_Look_L";

        [Tooltip("Blendshape for right eye looking left.")]
        public string lookLeftOnRightEyeBlendshapeName = "Eye_R_Look_L";

        [Tooltip("Blendshape for left eye looking right.")]
        public string lookRightOnLeftEyeBlendshapeName = "Eye_L_Look_R";

        [Tooltip("Blendshape for right eye looking right.")]
        public string lookRightOnRightEyeBlendshapeName = "Eye_R_Look_R";

        [Tooltip("Blendshape for left eye looking up.")]
        public string lookUpOnLeftEyeBlendshapeName = "Eye_L_Look_Up";

        [Tooltip("Blendshape for right eye looking up.")]
        public string lookUpOnRightEyeBlendshapeName = "Eye_R_Look_Up";

        [Tooltip("Blendshape for left eye looking down.")]
        public string lookDownOnLeftEyeBlendshapeName = "Eye_L_Look_Down";

        [Tooltip("Blendshape for right eye looking down.")]
        public string lookDownOnRightEyeBlendshapeName = "Eye_R_Look_Down";

        // ── Look-Away Behavior ────────────────────────────────────────────────────

        [Header("Look-Away Behavior")]
        [Tooltip("Periodic natural gaze aversion — the character briefly breaks eye contact. " +
                 "Adds natural variation so the character does not hold a fixed stare.")]
        public bool enableLookAway;

        [Tooltip("Mean time between look-away events (seconds).")]
        [Range(3f, 30f)]
        public float lookAwayMeanInterval = 9f;

        [Tooltip("Standard deviation of look-away interval.")]
        [Range(0f, 8f)]
        public float lookAwayIntervalStdDev = 1.6f;

        [Tooltip("Duration the character holds the off-camera gaze before returning (seconds).")]
        [Range(0.2f, 4f)]
        public float lookAwayHoldDuration = 0.35f;

        [Tooltip("Standard deviation for look-away hold duration. Each aversion samples a different hold time.")]
        [Range(0f, 1.5f)]
        public float lookAwayHoldDurationStdDev = 0.12f;

        [Tooltip("Horizontal angular offset for look-away targets (degrees).")]
        [Range(0.05f, 50f)]
        public float lookAwayHorizontalAngleDeg = 4.5f;

        [Tooltip("Minimum horizontal offset for look-away targets. Keeps aversions from collapsing into tiny micro-shifts.")]
        [Range(0f, 30f)]
        public float lookAwayMinHorizontalAngleDeg = 1.5f;

        [Tooltip("Vertical range for look-away target variation (degrees, symmetric ±).")]
        [Range(0f, 20f)]
        public float lookAwayVerticalRangeDeg = 0.85f;

        [Tooltip("Safety buffer from the anatomical eye-rotation limit when generating look-away targets.")]
        [Range(0f, 15f)]
        public float lookAwaySafeMarginDeg = 12f;

        [Tooltip("Distance to the off-axis fixation point. Farther targets reduce cross-eyed appearance.")]
        [Range(1f, 20f)]
        public float lookAwayTargetDistance = 8f;

        [Tooltip("SmoothDamp time for blending in/out of look-away. Low = snappy cut, high = gentle.")]
        [Range(0.05f, 0.6f)]
        public float lookAwaySmoothTime = 0.12f;

        // ── Validation ────────────────────────────────────────────────────────────

        private void OnValidate()
        {
            gazeTrackingSmoothTime = Mathf.Max(0.005f, gazeTrackingSmoothTime);
            gazeMaxTrackingSpeed = Mathf.Max(10f, gazeMaxTrackingSpeed);
            saccadeMeanInterval = Mathf.Max(0.2f, saccadeMeanInterval);
            saccadeIntervalStdDev = Mathf.Max(0f, saccadeIntervalStdDev);
            blinkRatePerMinute = Mathf.Clamp(blinkRatePerMinute, 1f, 60f);
            blinkIntervalStdDev = Mathf.Max(0.05f, blinkIntervalStdDev);
            blinkCloseDuration = Mathf.Clamp(blinkCloseDuration, 0.03f, 0.3f);
            blinkOpenDuration = Mathf.Clamp(blinkOpenDuration, blinkCloseDuration, 0.6f);
            lookAwayMeanInterval = Mathf.Max(1f, lookAwayMeanInterval);
            lookAwayHoldDuration = Mathf.Max(0.1f, lookAwayHoldDuration);
            lookAwayHoldDurationStdDev = Mathf.Clamp(lookAwayHoldDurationStdDev, 0f, Mathf.Max(0f, lookAwayHoldDuration * 0.9f));
            lookAwaySmoothTime = Mathf.Max(0.02f, lookAwaySmoothTime);
            lookAwaySafeMarginDeg = Mathf.Clamp(lookAwaySafeMarginDeg, 0f, 15f);
            lookAwayTargetDistance = Mathf.Clamp(lookAwayTargetDistance, 1f, 20f);
            microTremorFrequency = Mathf.Max(1f, microTremorFrequency);
            saccadeDriftReturnSpeed = Mathf.Max(0.05f, saccadeDriftReturnSpeed);
            horizontalCorrectiveStrength = Mathf.Clamp01(horizontalCorrectiveStrength);
            verticalCorrectiveStrength = Mathf.Clamp01(verticalCorrectiveStrength);
            lookAwayCorrectiveMultiplier = Mathf.Clamp01(lookAwayCorrectiveMultiplier);

            float safeHorizontalLookAwayLimit = Mathf.Max(1f, maxHorizontalAngleDeg - lookAwaySafeMarginDeg);
            float safeVerticalLookAwayLimit = Mathf.Max(0f, maxVerticalAngleDeg - lookAwaySafeMarginDeg);
            lookAwayHorizontalAngleDeg = Mathf.Clamp(lookAwayHorizontalAngleDeg, 0.05f, safeHorizontalLookAwayLimit);
            lookAwayMinHorizontalAngleDeg = Mathf.Clamp(lookAwayMinHorizontalAngleDeg, 0f, lookAwayHorizontalAngleDeg);
            lookAwayVerticalRangeDeg = Mathf.Clamp(lookAwayVerticalRangeDeg, 0f, safeVerticalLookAwayLimit);

            Vector3 forward = AxisToVector(eyeForwardAxis);
            Vector3 up = AxisToVector(eyeUpAxis);
            if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.999f)
            {
                eyeUpAxis = eyeForwardAxis == LocalAxisDirection.PositiveY || eyeForwardAxis == LocalAxisDirection.NegativeY
                    ? LocalAxisDirection.PositiveZ
                    : LocalAxisDirection.PositiveY;
            }
        }

        public static Vector3 AxisToVector(LocalAxisDirection axis)
        {
            return axis switch
            {
                LocalAxisDirection.PositiveX => Vector3.right,
                LocalAxisDirection.NegativeX => Vector3.left,
                LocalAxisDirection.PositiveY => Vector3.up,
                LocalAxisDirection.NegativeY => Vector3.down,
                LocalAxisDirection.PositiveZ => Vector3.forward,
                LocalAxisDirection.NegativeZ => Vector3.back,
                _ => Vector3.forward
            };
        }
    }
}
