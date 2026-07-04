namespace Convai.Modules.LipSync
{
    internal sealed class LipSyncComponentServiceFactory : ILipSyncComponentServiceFactory
    {
        public LipSyncComponentServices Create()
        {
            ILipSyncRuntimeController runtimeController = new LipSyncRuntimeController(new PlaybackClockCoordinator());
            ILipSyncCapabilityProvider capabilityProvider = new LipSyncCapabilityProvider();
            ILipSyncLifecycleValidator validator = new LipSyncLifecycleValidator();
            ILipSyncRuntimeConfigFactory runtimeConfigFactory = new LipSyncRuntimeConfigFactory();
            ILipSyncValidationFailurePolicy validationFailurePolicy = new LipSyncDisableComponentValidationPolicy();

            ILipSyncLifecycleOrchestrator lifecycleOrchestrator = new LipSyncLifecycleOrchestrator(
                runtimeController,
                capabilityProvider,
                validator,
                runtimeConfigFactory,
                validationFailurePolicy);

            return new LipSyncComponentServices(runtimeController, lifecycleOrchestrator);
        }
    }
}
