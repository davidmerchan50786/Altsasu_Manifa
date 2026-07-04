#if VISTA

namespace Pinwheel.Vista
{
    /// <summary>
    /// Asynchronous request handle for biome data generation.
    /// </summary>
    /// <remarks>
    /// This type extends <see cref="ProgressiveTask"/> and carries the <see cref="BiomeData"/> payload being filled by
    /// graph execution or biome post-processing coroutines. Callers typically create the request synchronously, assign
    /// an empty <see cref="BiomeData"/> instance to <see cref="data"/>, then yield on the request until <c>Complete()</c> is called.
    /// </remarks>
    public class BiomeDataRequest : ProgressiveTask
    {
        /// <summary>
        /// Gets or sets the biome data payload produced by this request.
        /// </summary>
        /// <remarks>
        /// The payload is usually allocated before the request starts and populated in-place as the coroutine progresses.
        /// Ownership of the contained resources follows normal <see cref="BiomeData"/> lifetime rules.
        /// </remarks>
        public BiomeData data { get; set; }
    }
}
#endif


