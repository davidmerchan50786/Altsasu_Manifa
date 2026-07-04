using System.Collections.Generic;
using UnityEngine;

namespace Convai.Modules.LipSync
{
    internal struct LipSyncComponentConfiguration
    {
        public string LockedProfileId;
        public ConvaiLipSyncMapAsset Mapping;
        public IReadOnlyList<SkinnedMeshRenderer> TargetMeshes;
        public float SmoothingFactor;
        public float FadeOutDuration;
        public float TimeOffsetSeconds;
        public float MaxBufferedSeconds;
        public float MinResumeHeadroomSeconds;
    }
}
