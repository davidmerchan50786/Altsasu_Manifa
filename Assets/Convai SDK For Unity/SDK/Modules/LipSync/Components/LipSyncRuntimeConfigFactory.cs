using Convai.Domain.Models.LipSync;

namespace Convai.Modules.LipSync
{
    internal sealed class LipSyncRuntimeConfigFactory : ILipSyncRuntimeConfigFactory
    {
        public void ApplyLatencyPreset(
            LipSyncLatencyMode mode,
            ref float maxBufferedSeconds,
            ref float minResumeHeadroomSeconds)
        {
            switch (mode)
            {
                case LipSyncLatencyMode.UltraLowLatency:
                    maxBufferedSeconds = 1f;
                    minResumeHeadroomSeconds = 0.05f;
                    break;
                case LipSyncLatencyMode.Balanced:
                    maxBufferedSeconds = 3f;
                    minResumeHeadroomSeconds = 0.12f;
                    break;
                case LipSyncLatencyMode.NetworkSafe:
                    maxBufferedSeconds = 6f;
                    minResumeHeadroomSeconds = 0.25f;
                    break;
                case LipSyncLatencyMode.Custom:
                    break;
            }
        }

        public LipSyncRuntimeConfig Build(
            ref LipSyncComponentConfiguration componentConfiguration,
            out LipSyncProfileId activeProfileId)
        {
            componentConfiguration.LockedProfileId = LipSyncProfileId.Normalize(componentConfiguration.LockedProfileId);
            activeProfileId = new LipSyncProfileId(componentConfiguration.LockedProfileId);

            return new LipSyncRuntimeConfig(
                activeProfileId,
                componentConfiguration.Mapping,
                componentConfiguration.TargetMeshes,
                componentConfiguration.FadeOutDuration,
                componentConfiguration.SmoothingFactor,
                componentConfiguration.TimeOffsetSeconds,
                componentConfiguration.MaxBufferedSeconds,
                componentConfiguration.MinResumeHeadroomSeconds);
        }
    }
}
