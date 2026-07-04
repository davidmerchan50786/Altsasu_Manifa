using System;
using Convai.Domain.EventSystem;
using Convai.Domain.Models.LipSync;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Modules.LipSync
{
    internal sealed class LipSyncLifecycleOrchestrator : ILipSyncLifecycleOrchestrator
    {
        private readonly ILipSyncCapabilityProvider _capabilityProvider;
        private readonly ILipSyncRuntimeConfigFactory _configFactory;
        private readonly ILipSyncRuntimeController _runtimeController;
        private readonly ILipSyncValidationFailurePolicy _validationFailurePolicy;
        private readonly ILipSyncLifecycleValidator _validator;
        private IEventHub _eventHub;

        private ICharacterIdentitySource _identitySource;
        private bool _isInjected;
        private ILogger _logger;
        private string _resolvedCharacterId = string.Empty;

        public LipSyncLifecycleOrchestrator(
            ILipSyncRuntimeController runtimeController,
            ILipSyncCapabilityProvider capabilityProvider,
            ILipSyncLifecycleValidator validator,
            ILipSyncRuntimeConfigFactory configFactory,
            ILipSyncValidationFailurePolicy validationFailurePolicy)
        {
            _runtimeController = runtimeController ?? throw new ArgumentNullException(nameof(runtimeController));
            _capabilityProvider = capabilityProvider ?? throw new ArgumentNullException(nameof(capabilityProvider));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _configFactory = configFactory ?? throw new ArgumentNullException(nameof(configFactory));
            _validationFailurePolicy = validationFailurePolicy ??
                                       throw new ArgumentNullException(nameof(validationFailurePolicy));
        }

        public LipSyncProfileId ActiveProfile { get; private set; }

        public void ApplyLatencyPreset(LipSyncLatencyMode mode, ref LipSyncComponentConfiguration configuration)
        {
            _configFactory.ApplyLatencyPreset(
                mode,
                ref configuration.MaxBufferedSeconds,
                ref configuration.MinResumeHeadroomSeconds);
        }

        public void HandleAwake(Component context, ref LipSyncComponentConfiguration configuration)
        {
            EnsureRuntimeInitialized(context, ref configuration);
            if (!EnsureRuntimePrerequisites(context)) return;

            TryBindRuntimeIfInjected(context);
        }

        public void HandleEnable(Component context, bool isPlaying, ref LipSyncComponentConfiguration configuration)
        {
            if (!isPlaying) return;

            EnsureRuntimeInitialized(context, ref configuration);
            if (!EnsureRuntimePrerequisites(context)) return;

            TryBindRuntimeIfInjected(context);
        }

        public void HandleDisable() => _runtimeController.UnbindAndReset();

        public void HandleDestroy() => _runtimeController.Dispose();

        public void HandleValidate(Component context, bool isPlaying, ref LipSyncComponentConfiguration configuration)
        {
            EnsureCapabilitiesConfigured(ref configuration);
            if (isPlaying && _runtimeController.IsInitialized) ConfigureRuntime(context, ref configuration, false);
        }

        public void HandleInject(
            Component context,
            ref LipSyncComponentConfiguration configuration,
            IEventHub eventHub,
            ILogger logger,
            bool shouldBindNow)
        {
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            _logger = logger;
            _isInjected = true;

            EnsureRuntimeInitialized(context, ref configuration);

            if (!EnsureRuntimePrerequisites(context)) return;

            if (shouldBindNow) TryBindRuntimeIfInjected(context);
        }

        public ConvaiLipSyncMapAsset ResolveEffectiveMapping(ref LipSyncComponentConfiguration configuration)
        {
            EnsureCapabilitiesConfigured(ref configuration);
            return _capabilityProvider.ResolveEffectiveMapping();
        }

        public bool TryGetTransportOptions(ref LipSyncComponentConfiguration configuration,
            out LipSyncTransportOptions options)
        {
            EnsureCapabilitiesConfigured(ref configuration);
            return _capabilityProvider.TryGetTransportOptions(out options);
        }

        public void Tick(float deltaTime) => _runtimeController.Tick(deltaTime);

        public bool IsPlaying => _runtimeController.IsPlaying;

        public bool IsFadingOut => _runtimeController.IsFadingOut;

        public PlaybackState EngineState => _runtimeController.EngineState;

        public float GetTalkingTimeRemaining() => _runtimeController.GetTalkingTimeRemaining();

        public float GetTalkingTimeElapsed() => _runtimeController.GetTalkingTimeElapsed();

        public float GetTotalBufferedDuration() => _runtimeController.GetTotalBufferedDuration();

        public float GetTotalStreamDuration() => _runtimeController.GetTotalStreamDuration();

        public float GetHeadroom() => _runtimeController.GetHeadroom();

        public BlendshapeSnapshot GetBlendshapeSnapshot() => _runtimeController.GetBlendshapeSnapshot();

        private void EnsureRuntimeInitialized(Component context, ref LipSyncComponentConfiguration configuration) =>
            ConfigureRuntime(context, ref configuration, true);

        private void EnsureCapabilitiesConfigured(ref LipSyncComponentConfiguration configuration)
        {
            LipSyncRuntimeConfig runtimeConfig = BuildRuntimeConfig(ref configuration);
            _capabilityProvider.Reconfigure(runtimeConfig);
        }

        private void ConfigureRuntime(
            Component context,
            ref LipSyncComponentConfiguration configuration,
            bool ensureInitialized)
        {
            LipSyncRuntimeConfig runtimeConfig = BuildRuntimeConfig(ref configuration);
            _capabilityProvider.Reconfigure(runtimeConfig);
            ConvaiLipSyncMapAsset effectiveMapping = _capabilityProvider.ResolveEffectiveMapping();

            if (ensureInitialized)
            {
                _runtimeController.EnsureInitialized(context, runtimeConfig, effectiveMapping);
                return;
            }

            if (_runtimeController.IsInitialized) _runtimeController.Reconfigure(runtimeConfig, effectiveMapping);
        }

        private LipSyncRuntimeConfig BuildRuntimeConfig(ref LipSyncComponentConfiguration configuration)
        {
            LipSyncRuntimeConfig runtimeConfig =
                _configFactory.Build(ref configuration, out LipSyncProfileId activeProfileId);
            ActiveProfile = activeProfileId;
            return runtimeConfig;
        }

        private bool EnsureRuntimePrerequisites(Component context)
        {
            LipSyncValidationResult profileValidation = _validator.ValidateProfile(ActiveProfile);
            if (!_validationFailurePolicy.Apply(context, profileValidation)) return false;

            LipSyncValidationResult bindingValidation = _validator.ValidateCharacterBinding(
                context,
                ref _identitySource,
                out _resolvedCharacterId);
            return _validationFailurePolicy.Apply(context, bindingValidation);
        }

        private void TryBindRuntimeIfInjected(Component context)
        {
            if (!_isInjected) return;

            LipSyncValidationResult bindingValidation = _validator.ValidateCharacterBinding(
                context,
                ref _identitySource,
                out _resolvedCharacterId);
            if (!bindingValidation.IsValid) return;

            _runtimeController.Bind(_eventHub, _resolvedCharacterId, _logger);
        }
    }
}
