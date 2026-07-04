using System;
using System.Collections.Generic;

namespace Convai.Domain.Models.LipSync
{
    /// <summary>
    ///     Packed lip sync payload optimized for runtime playback.
    /// </summary>
    public sealed class LipSyncPackedChunk
    {
        private static readonly IReadOnlyList<string> EmptyChannelNames = Array.Empty<string>();
        private static readonly float[][] EmptyFrames = Array.Empty<float[]>();

        public LipSyncPackedChunk(
            LipSyncProfileId profileId,
            float frameRate,
            IReadOnlyList<string> channelNames,
            float[][] frames)
        {
            ProfileId = profileId;
            FrameRate = frameRate;
            ChannelNames = channelNames ?? EmptyChannelNames;
            Frames = frames ?? EmptyFrames;
        }

        public LipSyncProfileId ProfileId { get; }
        public float FrameRate { get; }
        public IReadOnlyList<string> ChannelNames { get; }
        public float[][] Frames { get; }
        public int FrameCount => Frames?.Length ?? 0;
        public bool IsValid => FrameCount > 0 && FrameRate > 0f && ChannelNames != null && ChannelNames.Count > 0;
        public float Duration => IsValid ? FrameCount / FrameRate : 0f;
    }
}
