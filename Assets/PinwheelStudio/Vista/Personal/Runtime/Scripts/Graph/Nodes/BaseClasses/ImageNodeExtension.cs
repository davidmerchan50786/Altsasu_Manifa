#if VISTA
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Resolution helpers for <see cref="ImageNodeBase"/>.
    /// </summary>
    public static class ImageNodeExtension
    {
        /// <summary>
        /// Calculates the effective working resolution for an image node based on its override mode and multiplier settings.
        /// </summary>
        /// <param name="n">Image node to evaluate.</param>
        /// <param name="graphResolution">Base graph resolution for the current request.</param>
        /// <param name="mainInputResolution">Resolution of the node's main upstream input.</param>
        /// <returns>The final resolution rounded up to a multiple of 8.</returns>
        public static int CalculateResolution(this ImageNodeBase n, int graphResolution, int mainInputResolution)
        {
            int res;
            if (n.resolutionOverride == ResolutionOverrideOptions.RelativeToGraph)
            {
                res = Mathf.Max(8, Mathf.CeilToInt(graphResolution * n.resolutionMultiplier));
            }
            else if (n.resolutionOverride == ResolutionOverrideOptions.RelativeToMainInput)
            {
                res = Mathf.Max(8, Mathf.CeilToInt(mainInputResolution * n.resolutionMultiplier));
            }
            else
            {
                res = n.resolutionAbsolute;
            }
            res = Utilities.MultipleOf8(res);
            return res;
        }
    }
}
#endif


