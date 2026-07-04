#if VISTA
using UnityEngine;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Provides helpers for collecting scene-derived data from terrain tiles into shared render targets.
    /// </summary>
    /// <remarks>
    /// The current utility surface is focused on scene height collection. <see cref="VistaManager"/> uses these helpers to
    /// gather tile-provided height information that Local Procedural Biomes can then feed back into terrain graph inputs.
    /// </remarks>
    public static class SceneDataUtils
    {
        /// <summary>
        /// Asks overlapping tiles that implement <see cref="ISceneHeightProvider"/> to draw their height data into a target texture.
        /// </summary>
        /// <param name="tiles">
        /// The tiles to inspect. Each tile is tested against <paramref name="worldBounds"/> in XZ space before its height
        /// provider callback is invoked.
        /// </param>
        /// <param name="targetRt">
        /// The destination render texture representing the requested world region. Providers draw their own height data into
        /// this texture using coordinates derived from <paramref name="worldBounds"/>.
        /// </param>
        /// <param name="worldBounds">The world-space bounds whose height data should be collected.</param>
        /// <remarks>
        /// The method converts the requested bounds and each tile's bounds to XZ rectangles, then forwards the request to
        /// <see cref="ISceneHeightProvider.OnCollectSceneHeight(RenderTexture, Rect)"/> for tiles that overlap.
        /// Non-overlapping tiles are skipped.
        /// </remarks>
        public static void CollectWorldHeight(ITile[] tiles, RenderTexture targetRt, Bounds worldBounds)
        {
            Rect worldRect = new Rect(worldBounds.min.x, worldBounds.min.z, worldBounds.size.x, worldBounds.size.z);
            for (int i = 0; i < tiles.Length; ++i)
            {
                ITile t = tiles[i];

                Bounds tileBounds = t.worldBounds;
                Rect tileRect = new Rect(tileBounds.min.x, tileBounds.min.z, tileBounds.size.x, tileBounds.size.z);
                if (!tileRect.Overlaps(worldRect))
                    continue;

                if (t is ISceneHeightProvider shp)
                {
                    shp.OnCollectSceneHeight(targetRt, worldRect);
                }
            }
        }


    }
}
#endif


