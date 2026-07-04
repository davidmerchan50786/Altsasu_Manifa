#if VISTA
using UnityEngine;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Defines a contract for tiles that can contribute their height data to a shared scene-height texture.
    /// </summary>
    public interface ISceneHeightProvider
    {
        /// <summary>
        /// Draws this tile's height contribution into a destination texture representing a requested world-space rectangle.
        /// </summary>
        /// <param name="targetRt">The destination texture that receives scene-height data.</param>
        /// <param name="requestedWorldRect">The world-space rectangle encoded by <paramref name="targetRt"/>.</param>
        void OnCollectSceneHeight(RenderTexture targetRt, Rect requestedWorldRect);
    }
}
#endif


