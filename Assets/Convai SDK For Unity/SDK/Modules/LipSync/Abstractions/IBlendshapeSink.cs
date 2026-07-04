using System.Collections.Generic;
using UnityEngine;

namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Contract for applying lip sync output values to a rendering target.
    ///     Decouples playback from specific Unity mesh implementations.
    /// </summary>
    public interface IBlendshapeSink
    {
        /// <summary>
        ///     Initializes the sink with target meshes and the effective mapping to compile.
        /// </summary>
        public void Initialize(IReadOnlyList<SkinnedMeshRenderer> meshes, ConvaiLipSyncMapAsset mapping);

        /// <summary>
        ///     Applies one frame of normalized source values using the provided channel layout.
        /// </summary>
        public void Apply(float[] values, IReadOnlyList<string> channelNames);

        /// <summary>
        ///     Resets any previously written target blendshape weights to neutral.
        /// </summary>
        public void ResetToZero(IReadOnlyList<string> channelNames);

        /// <summary>
        ///     Invalidates any internal compiled state so it is rebuilt on next apply.
        /// </summary>
        public void Invalidate();
    }
}
