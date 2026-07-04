using System.Collections.Generic;
using Convai.Modules.LipSync.Profiles;
using Convai.Shared.Types;

namespace Convai.Modules.LipSync
{
    internal sealed class LipSyncCapabilityProvider : ILipSyncCapabilityProvider
    {
        private ConvaiLipSyncMapAsset _cachedEffectiveMapping;

        private LipSyncTransportOptions _cachedTransportOptions;
        private LipSyncRuntimeConfig _config;
        private bool _hasCachedEffectiveMapping;
        private bool _hasCachedTransportOptions;
        private bool _hasConfig;

        public void Reconfigure(LipSyncRuntimeConfig config)
        {
            _config = config;
            _hasConfig = true;
            _hasCachedTransportOptions = false;
            _hasCachedEffectiveMapping = false;
            _cachedEffectiveMapping = null;
        }

        public ConvaiLipSyncMapAsset ResolveEffectiveMapping()
        {
            if (_hasCachedEffectiveMapping && _cachedEffectiveMapping != null) return _cachedEffectiveMapping;

            if (!_hasConfig) return null;

            _cachedEffectiveMapping = LipSyncDefaultMappingResolver.ResolveEffective(
                _config.Mapping,
                _config.ProfileId,
                out _);
            _hasCachedEffectiveMapping = true;
            return _cachedEffectiveMapping;
        }

        public bool TryGetTransportOptions(out LipSyncTransportOptions options)
        {
            if (_hasCachedTransportOptions)
            {
                options = _cachedTransportOptions;
                return options.IsValid;
            }

            if (!_hasConfig)
            {
                _cachedTransportOptions = LipSyncTransportOptions.Disabled;
                _hasCachedTransportOptions = true;
                options = _cachedTransportOptions;
                return false;
            }

            ConvaiLipSyncMapAsset effectiveMapping = ResolveEffectiveMapping();
            IReadOnlyList<string> sourceNames = effectiveMapping != null
                ? effectiveMapping.GetSourceBlendshapeNames()
                : LipSyncBuiltInProfileLibrary.GetSourceBlendshapeNamesOrEmpty(_config.ProfileId);

            if (sourceNames == null || sourceNames.Count == 0)
                sourceNames = LipSyncBuiltInProfileLibrary.GetSourceBlendshapeNamesOrEmpty(_config.ProfileId);

            bool built = LipSyncTransportDefaults.TryBuildForProfile(
                _config.ProfileId,
                sourceNames,
                out _cachedTransportOptions);
            _hasCachedTransportOptions = true;
            options = _cachedTransportOptions;
            return built && options.IsValid;
        }
    }
}
