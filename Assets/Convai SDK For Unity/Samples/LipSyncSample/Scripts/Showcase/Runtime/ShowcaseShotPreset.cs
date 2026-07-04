using System;
using UnityEngine;

namespace Convai.ShowcaseCamera
{
    [Serializable]
    public sealed class ShowcaseShotPreset
    {
        [Tooltip("Optional display label for this preset.")]
        public string label = "Preset";

        [Tooltip("World-space yaw angle in degrees.")]
        public float yaw;

        [Tooltip("Pitch angle in degrees.")]
        public float pitch = 6f;

        [Tooltip("Distance from target anchor in meters.")]
        public float dollyDistance = 0.9f;

        [Tooltip("Physical camera focal length in mm.")]
        public float focalLength = 75f;

        [Tooltip("Offset from head/eyes anchor in local head space.")]
        public Vector3 framingOffset = new Vector3(0f, 0.01f, 0.04f);

        [Tooltip("Blend time for this preset transition in seconds.")]
        [Min(0.01f)]
        public float transitionTime = 0.55f;
    }
}
