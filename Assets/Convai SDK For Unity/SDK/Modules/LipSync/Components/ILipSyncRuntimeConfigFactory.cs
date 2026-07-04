using Convai.Domain.Models.LipSync;

namespace Convai.Modules.LipSync
{
    internal interface ILipSyncRuntimeConfigFactory
    {
        public void ApplyLatencyPreset(
            LipSyncLatencyMode mode,
            ref float maxBufferedSeconds,
            ref float minResumeHeadroomSeconds);

        public LipSyncRuntimeConfig Build(
            ref LipSyncComponentConfiguration componentConfiguration,
            out LipSyncProfileId activeProfileId);
    }
}
