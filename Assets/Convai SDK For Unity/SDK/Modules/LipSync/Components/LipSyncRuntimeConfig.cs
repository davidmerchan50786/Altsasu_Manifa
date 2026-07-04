using System.Collections.Generic;
using Convai.Domain.Models.LipSync;
using UnityEngine;

namespace Convai.Modules.LipSync
{
    internal readonly struct LipSyncRuntimeConfig
    {
        public LipSyncProfileId ProfileId { get; }
        public ConvaiLipSyncMapAsset Mapping { get; }
        public IReadOnlyList<SkinnedMeshRenderer> TargetMeshes { get; }
        public float FadeOutDuration { get; }
        public float SmoothingFactor { get; }
        public float TimeOffsetSeconds { get; }
        public float MaxBufferedSeconds { get; }
        public float MinResumeHeadroomSeconds { get; }

        public LipSyncRuntimeConfig(
            LipSyncProfileId profileId,
            ConvaiLipSyncMapAsset mapping,
            IReadOnlyList<SkinnedMeshRenderer> targetMeshes,
            float fadeOutDuration,
            float smoothingFactor,
            float timeOffsetSeconds,
            float maxBufferedSeconds,
            float minResumeHeadroomSeconds)
        {
            ProfileId = profileId;
            Mapping = mapping;
            TargetMeshes = targetMeshes;
            FadeOutDuration = fadeOutDuration;
            SmoothingFactor = smoothingFactor;
            TimeOffsetSeconds = timeOffsetSeconds;
            MaxBufferedSeconds = maxBufferedSeconds;
            MinResumeHeadroomSeconds = minResumeHeadroomSeconds;
        }

        public LipSyncEngineConfig ToEngineConfig()
        {
            return new LipSyncEngineConfig(
                FadeOutDuration,
                SmoothingFactor,
                TimeOffsetSeconds,
                MaxBufferedSeconds,
                MinResumeHeadroomSeconds);
        }
    }
}
