using Convai.Shared.Types;

namespace Convai.Modules.LipSync
{
    internal interface ILipSyncCapabilityProvider
    {
        public void Reconfigure(LipSyncRuntimeConfig config);
        public ConvaiLipSyncMapAsset ResolveEffectiveMapping();
        public bool TryGetTransportOptions(out LipSyncTransportOptions options);
    }
}
