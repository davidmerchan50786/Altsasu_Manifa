#if GASKELLGAMES
#if GASKELLGAMES_INPUTEVENTSYSTEM
using System;
using UnityEngine;

namespace Gaskellgames.CameraSystem
{
    /// <remarks>
    /// Code created by Gaskellgames: https://gaskellgames.com
    /// </remarks>
    
    [Serializable]
    public class CameraOrbits
    {
        [Tooltip("The vertical offset (up direction) for the whole orbit rig.")]
        public float rigOffset;
        
        [Tooltip("The vertical offset of the orbit positions (up, down).")]
        public CameraOrbitsHeight height;
        
        [Tooltip("The horizontal distance of the orbit positions (top, middle, bottom).")]
        public CameraOrbitsRadius radius;
        
        /// <summary>
        /// Initialise the values of CameraOrbits
        /// </summary>
        /// <param name="rigOffset"></param>
        /// <param name="heightUp"></param>
        /// <param name="heightDown"></param>
        /// <param name="radiusTop"></param>
        /// <param name="radiusMiddle"></param>
        /// <param name="radiusBottom"></param>
        public CameraOrbits(float rigOffset = 0, float heightUp = 3, float heightDown = -1, float radiusTop = 2, float radiusMiddle = 4, float radiusBottom = 3)
        {
            this.rigOffset = rigOffset;
            this.height = new CameraOrbitsHeight(heightUp, heightDown);
            this.radius = new CameraOrbitsRadius(radiusTop, radiusMiddle, radiusBottom);
        }
        
    } // class end
}
#endif
#endif