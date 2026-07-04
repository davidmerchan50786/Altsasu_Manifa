#if VISTA

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Marks a node or data source that exposes a seed value controlling deterministic random behavior.
    /// </summary>
    public interface IHasSeed
    {
        /// <summary>
        /// Seed value used by the implementing type when generating randomized results.
        /// </summary>
        public int seed { get; set; }
    }
}
#endif


