namespace Convai.Modules.LipSync
{
    internal readonly struct LipSyncComponentServices
    {
        public ILipSyncRuntimeController RuntimeController { get; }
        public ILipSyncLifecycleOrchestrator LifecycleOrchestrator { get; }

        public LipSyncComponentServices(
            ILipSyncRuntimeController runtimeController,
            ILipSyncLifecycleOrchestrator lifecycleOrchestrator)
        {
            RuntimeController = runtimeController;
            LifecycleOrchestrator = lifecycleOrchestrator;
        }
    }
}
