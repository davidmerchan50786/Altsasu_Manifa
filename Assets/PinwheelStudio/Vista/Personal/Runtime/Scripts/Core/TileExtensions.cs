#if VISTA
using System.Collections.Generic;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Provides helper methods for testing tile-to-biome overlap.
    /// </summary>
    public static class TileExtensions
    {
        /// <summary>
        /// Tests whether a tile overlaps at least one biome in a supplied set.
        /// </summary>
        /// <param name="tile">The tile whose <see cref="ITile.worldBounds"/> should be tested.</param>
        /// <param name="biomes">The biome collection to test against the tile bounds.</param>
        /// <returns>
        /// <see langword="true"/> when any biome reports overlap with the tile bounds; otherwise, <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// The method delegates the geometric test to <see cref="IBiome.IsOverlap(UnityEngine.Bounds)"/> for each biome and
        /// stops on the first positive result.
        /// </remarks>
        public static bool IsOverlap(this ITile tile, IBiome[] biomes)
        {
            foreach (IBiome b in biomes)
            {
                if (b.IsOverlap(tile.worldBounds))
                {
                    return true;
                }
            }
            return false;
        }

        internal static bool OverlapTest(this ITile tile, IBiome[] biomes, HashSet<KeyValuePair<ITile, IBiome>> result)
        {
            bool overlapped = false;
            foreach (IBiome b in biomes)
            {
                if (b.IsOverlap(tile.worldBounds))
                {
                    result.Add(new KeyValuePair<ITile, IBiome>(tile, b));
                    overlapped = true;
                }
            }
            return overlapped;
        }
    }
}
#endif


