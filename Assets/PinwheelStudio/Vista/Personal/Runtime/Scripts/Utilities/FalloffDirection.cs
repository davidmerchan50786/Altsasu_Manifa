#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Pinwheel.Vista
{
    [System.Serializable]
    /// <summary>
    /// Specifies whether a biome or polygon falloff region expands away from the base shape or contracts into it.
    /// </summary>
    public enum FalloffDirection
    {
        /// <summary>
        /// Places the falloff band outside the base polygon.
        /// </summary>
        Outer,
        /// <summary>
        /// Places the falloff band inside the base polygon.
        /// </summary>
        Inner
    }
}
#endif


