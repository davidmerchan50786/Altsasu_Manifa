#if VISTA
using UnityEngine;

namespace Pinwheel.Vista.Geometric
{
    /// <summary>
    /// Provides small 2D geometric helper operations shared by Vista math code.
    /// </summary>
    public static class Geo2D
    {
        /// <summary>
        /// Returns the 2D scalar cross product of two vectors.
        /// </summary>
        /// <param name="lhs">The left-hand vector.</param>
        /// <param name="rhs">The right-hand vector.</param>
        /// <returns>
        /// The signed scalar cross-product value. Its sign encodes relative winding direction and its magnitude encodes the
        /// parallelogram area formed by the two vectors.
        /// </returns>
        public static float Cross(Vector2 lhs, Vector2 rhs)
        {
            return lhs.y * rhs.x - lhs.x * rhs.y;
        }
    }
}
#endif


