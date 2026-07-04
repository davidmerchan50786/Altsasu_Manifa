using UnityEngine;

namespace Convai.ShowcaseCamera
{
    [CreateAssetMenu(fileName = "ShowcaseCameraConfig", menuName = "Convai/Showcase/Camera Config")]
    public sealed class ShowcaseCameraConfig : ScriptableObject
    {
        [Header("Default Pose")]
        public float defaultYaw;
        public float defaultPitch = 6f;
        public float defaultDollyDistance = 0.95f;
        public float defaultFocalLength = 70f;
        public Vector3 defaultFramingOffset = new Vector3(0f, 0.01f, 0.04f);

        [Header("Orbit Limits")]
        public float minPitch = -12f;
        public float maxPitch = 20f;

        [Header("Zoom Limits")]
        public float minDollyDistance = 0.55f;
        public float maxDollyDistance = 1.40f;
        public float minFocalLength = 55f;
        public float maxFocalLength = 105f;

        [Header("Input")]
        public float orbitSensitivityX = 0.12f;
        public float orbitSensitivityY = 0.10f;
        public float zoomSensitivity = 0.08f;

        [Header("Smoothing")]
        public float orbitSmoothTime = 0.10f;
        public float zoomSmoothTime = 0.12f;
        [Tooltip("Time to recenter the orbit anchor when it does follow. Higher = slower, less sensitive to target movement.")]
        public float anchorSmoothTime = 0.35f;
        [Tooltip("Anchor only follows target when it has moved at least this far (meters). Reduces camera jitter from small movements.")]
        [Min(0f)]
        public float anchorFollowThreshold = 0.18f;

        [Header("Physical Camera")]
        public Vector2 sensorSize = new Vector2(36f, 24f);
        public float aperture = 5.0f;
        public float shutterSpeed = 0.008f;
        public int iso = 100;

        [Header("LipSync Focus")]
        public bool enableLipSyncMouthFocusBias;
        [Range(0f, 1f)] public float mouthFocusBlend = 0.45f;
        public float estimatedMouthVerticalOffset = -0.095f;
        public float estimatedMouthForwardOffset = 0.02f;

        [Header("Face Focus")]
        public bool enableFaceCenterFocusOffset = true;
        public float faceCenterVerticalOffset = -0.022f;
        public float faceCenterForwardOffset = 0.006f;

        [Header("Zoom Framing Compensation")]
        public bool enableZoomFramingCompensation = true;
        [Range(0f, 1f)] public float zoomCompensationStart = 0.40f;
        public float zoomCompensationY = -0.015f;
        public float zoomCompensationZ;

        [Header("Presets")]
        public ShowcaseShotPreset[] shotPresets =
        {
            new ShowcaseShotPreset
            {
                label = "Portrait",
                yaw = 0f,
                pitch = 6f,
                dollyDistance = 0.95f,
                focalLength = 72f,
                framingOffset = new Vector3(0f, 0.01f, 0.04f),
                transitionTime = 0.55f
            },
            new ShowcaseShotPreset
            {
                label = "Three Quarter",
                yaw = -20f,
                pitch = 7f,
                dollyDistance = 1.0f,
                focalLength = 80f,
                framingOffset = new Vector3(0.01f, 0.01f, 0.04f),
                transitionTime = 0.6f
            },
            new ShowcaseShotPreset
            {
                label = "Hero Close",
                yaw = 15f,
                pitch = 4f,
                dollyDistance = 0.75f,
                focalLength = 95f,
                framingOffset = new Vector3(0f, 0.015f, 0.03f),
                transitionTime = 0.65f
            },
            new ShowcaseShotPreset
            {
                label = "Side Profile",
                yaw = 40f,
                pitch = 5f,
                dollyDistance = 1.05f,
                focalLength = 85f,
                framingOffset = new Vector3(0f, 0.01f, 0.04f),
                transitionTime = 0.7f
            }
        };

        public void Sanitize()
        {
            if (maxPitch < minPitch)
            {
                float swap = maxPitch;
                maxPitch = minPitch;
                minPitch = swap;
            }

            if (maxDollyDistance < minDollyDistance)
            {
                float swap = maxDollyDistance;
                maxDollyDistance = minDollyDistance;
                minDollyDistance = swap;
            }

            if (maxFocalLength < minFocalLength)
            {
                float swap = maxFocalLength;
                maxFocalLength = minFocalLength;
                minFocalLength = swap;
            }

            orbitSmoothTime = Mathf.Max(0.01f, orbitSmoothTime);
            zoomSmoothTime = Mathf.Max(0.01f, zoomSmoothTime);
            anchorSmoothTime = Mathf.Max(0.01f, anchorSmoothTime);
            anchorFollowThreshold = Mathf.Max(0f, anchorFollowThreshold);
            mouthFocusBlend = Mathf.Clamp01(mouthFocusBlend);
            zoomCompensationStart = Mathf.Clamp01(zoomCompensationStart);
            defaultDollyDistance = Mathf.Clamp(defaultDollyDistance, minDollyDistance, maxDollyDistance);
            defaultFocalLength = Mathf.Clamp(defaultFocalLength, minFocalLength, maxFocalLength);
            defaultPitch = Mathf.Clamp(defaultPitch, minPitch, maxPitch);
        }

        public bool TryGetPreset(int index, out ShowcaseShotPreset preset)
        {
            preset = null;
            if (shotPresets == null || index < 0 || index >= shotPresets.Length || shotPresets[index] == null)
            {
                return false;
            }

            preset = shotPresets[index];
            return true;
        }

        private void OnValidate()
        {
            Sanitize();
        }
    }
}
