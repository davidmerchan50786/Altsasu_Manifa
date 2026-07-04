#if GASKELLGAMES
#if GASKELLGAMES_INPUTEVENTSYSTEM
using System.Collections.Generic;
using UnityEngine;

namespace Gaskellgames.CameraSystem
{
    /// <remarks>
    /// Code created by Gaskellgames: https://gaskellgames.com
    /// </remarks>
    
    public static class CameraList
    {
        #region Variables

        private static List<CameraRig> cameraRigs = new List<CameraRig>();
        
        private static List<CameraRig> shakableRigs = new List<CameraRig>();

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Public Methods

        /// <summary>
        /// Get the global cameraRig list (all registered cameraRigs)
        /// </summary>
        /// <returns></returns>
        public static List<CameraRig> GetCameraRigList()
        {
            return cameraRigs;
        }

        /// <summary>
        /// Add a cameraRig to the global cameraRig list
        /// </summary>
        /// <param name="cameraRig"></param>
        public static void Register(CameraRig cameraRig)
        {
            if (cameraRigs.Contains(cameraRig)) { return; }
            
            cameraRigs.Add(cameraRig);
            GgLogs.Log(null, GgLogType.Info, "{0} registered. {1} total registered camera rigs.", cameraRig.name, cameraRigs.Count);
        }

        /// <summary>
        /// Remove a cameraRig from the global cameraRig list
        /// </summary>
        /// <param name="cameraRig"></param>
        public static void Unregister(CameraRig cameraRig)
        {
            if (!cameraRigs.Contains(cameraRig)) { return; }
            
            cameraRigs.Remove(cameraRig);
        }
        
        /// <summary>
        /// Get the shakable cameraRig list
        /// </summary>
        /// <returns></returns>
        public static List<CameraRig> GetShakableRigList()
        {
            return shakableRigs;
        }
        
        /// <summary>
        /// Add a cameraRig to the shakable cameraRig list
        /// </summary>
        /// <param name="cameraRig"></param>
        public static void SetShakable(CameraRig cameraRig)
        {
            if (shakableRigs.Contains(cameraRig)) { return; }
            
            shakableRigs.Add(cameraRig);
        }

        /// <summary>
        /// Remove a cameraRig from the shakable cameraRig list
        /// </summary>
        /// <param name="cameraRig"></param>
        public static void UnsetShakable(CameraRig cameraRig)
        {
            if (!shakableRigs.Contains(cameraRig)) { return; }
            
            shakableRigs.Remove(cameraRig);
        }

        #endregion

    } //class end
}

#endif
#endif