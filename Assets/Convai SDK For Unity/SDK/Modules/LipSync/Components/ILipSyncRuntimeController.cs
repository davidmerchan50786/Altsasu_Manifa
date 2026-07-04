using System;
using Convai.Domain.EventSystem;
using Convai.Runtime.Room;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Modules.LipSync
{
    internal interface ILipSyncRuntimeController : IDisposable
    {
        public bool IsInitialized { get; }
        public bool IsPlaying { get; }
        public bool IsFadingOut { get; }
        public PlaybackState EngineState { get; }
        public LipSyncPlaybackEngine Engine { get; }
        public IPlaybackClock CurrentClock { get; }

        public void EnsureInitialized(Component context, LipSyncRuntimeConfig config,
            ConvaiLipSyncMapAsset effectiveMapping);

        public void Reconfigure(LipSyncRuntimeConfig config, ConvaiLipSyncMapAsset effectiveMapping);
        public void SetRoomAudioService(IConvaiRoomAudioService roomAudioService);
        public void Bind(IEventHub eventHub, string characterId, ILogger logger);
        public void UnbindAndReset();
        public void Tick(float deltaTime);

        public float GetTalkingTimeRemaining();
        public float GetTalkingTimeElapsed();
        public float GetTotalBufferedDuration();
        public float GetTotalStreamDuration();
        public float GetHeadroom();
        public BlendshapeSnapshot GetBlendshapeSnapshot();
    }
}
