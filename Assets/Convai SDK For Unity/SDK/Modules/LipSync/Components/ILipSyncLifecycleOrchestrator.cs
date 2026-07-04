using Convai.Domain.EventSystem;
using Convai.Domain.Models.LipSync;
using Convai.Shared.Types;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Modules.LipSync
{
    internal interface ILipSyncLifecycleOrchestrator
    {
        public LipSyncProfileId ActiveProfile { get; }
        public bool IsPlaying { get; }
        public bool IsFadingOut { get; }
        public PlaybackState EngineState { get; }

        public void ApplyLatencyPreset(LipSyncLatencyMode mode, ref LipSyncComponentConfiguration configuration);
        public void HandleAwake(Component context, ref LipSyncComponentConfiguration configuration);
        public void HandleEnable(Component context, bool isPlaying, ref LipSyncComponentConfiguration configuration);
        public void HandleDisable();
        public void HandleDestroy();
        public void HandleValidate(Component context, bool isPlaying, ref LipSyncComponentConfiguration configuration);

        public void HandleInject(
            Component context,
            ref LipSyncComponentConfiguration configuration,
            IEventHub eventHub,
            ILogger logger,
            bool shouldBindNow);

        public ConvaiLipSyncMapAsset ResolveEffectiveMapping(ref LipSyncComponentConfiguration configuration);

        public bool TryGetTransportOptions(ref LipSyncComponentConfiguration configuration,
            out LipSyncTransportOptions options);

        public void Tick(float deltaTime);
        public float GetTalkingTimeRemaining();
        public float GetTalkingTimeElapsed();
        public float GetTotalBufferedDuration();
        public float GetTotalStreamDuration();
        public float GetHeadroom();
        public BlendshapeSnapshot GetBlendshapeSnapshot();
    }
}
