namespace Convai.Runtime
{
    /// <summary>
    ///     Represents the supported native realtime runtime path.
    /// </summary>
    public enum NativeRuntimeMode
    {
        /// <summary>
        ///     Route native room control through the transport-centered runtime path.
        ///     Value 1 preserves the serialized asset value used by earlier rollout phases.
        /// </summary>
        Transport = 1
    }
}
