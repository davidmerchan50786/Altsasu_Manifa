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
    public class CameraOrbitsHeight
    {
        [SerializeField, Min(0)]
        [Tooltip("The vertical offset distance of the top radius orbit, relative to the middle orbit.")]
        public float up;
        
        [SerializeField, Max(0)]
        [Tooltip("The vertical offset distance of the bottom radius orbit, relative to the middle orbit.")]
        public float down;

        /// <summary>
        /// Initialise the values of CameraOrbitsHeight
        /// </summary>
        /// <param name="up"></param>
        /// <param name="down"></param>
        public CameraOrbitsHeight(float up = 3, float down = -1)
        {
            this.up = up;
            this.down = down;
        }
        
    } // class end
}
#endif
#endif