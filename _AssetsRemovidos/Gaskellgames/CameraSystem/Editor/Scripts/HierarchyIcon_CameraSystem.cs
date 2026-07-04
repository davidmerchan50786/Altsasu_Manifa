#if UNITY_EDITOR
#if GASKELLGAMES
#if GASKELLGAMES_INPUTEVENTSYSTEM
using Gaskellgames.EditorOnly;
using Gaskellgames.InputEventSystem;
using UnityEditor;
using UnityEngine;

namespace Gaskellgames.CameraSystem.EditorOnly
{
    /// <remarks>
    /// Code created by Gaskellgames: https://gaskellgames.com
    /// </remarks>
    
    [InitializeOnLoad]
    public class HierarchyIcon_CameraSystem
    {
        #region Variables

        private const string icon_CameraBrain = "Icon_CameraBrain";
        private const string icon_CameraRig = "Icon_CameraRig";
        private const string icon_CameraFreelookRig = "Icon_CameraFreelookRig";
        private const string icon_CameraSwitcher = "Icon_CameraSwitcher";
        private const string icon_CameraShaker = "Icon_CameraShaker";
        private const string icon_CameraTriggerZone = "Icon_CameraTriggerZone";
        private const string icon_CameraTarget = "Icon_CameraTarget";
        private const string icon_CameraMultiTargetingRig = "Icon_CameraMultiTargetingRig";
        private const string icon_ShadowTransform = "Icon_ShadowTransform";

        private const string packageRefName = "CameraSystem";
        private const string relativePath = "/Editor/Icons/";

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Editor Loop

        static HierarchyIcon_CameraSystem()
        {
            HierarchyUtility.onCacheHierarchyIcons -= CacheHierarchyIcons;
            HierarchyUtility.onCacheHierarchyIcons += CacheHierarchyIcons;
        }

        private static void CacheHierarchyIcons()
        {
            if (!GgPackageRef.TryGetFullFilePath(packageRefName, relativePath, out string filePath)) { return; }
            
            HierarchyUtility.TryAddHierarchyIcon(typeof(CameraBrain), icon_CameraBrain, AssetDatabase.LoadAssetAtPath(filePath + $"{icon_CameraBrain}.png", typeof(Texture)) as Texture);
            HierarchyUtility.TryAddHierarchyIcon(typeof(CameraRig), icon_CameraRig, AssetDatabase.LoadAssetAtPath(filePath + $"{icon_CameraRig}.png", typeof(Texture)) as Texture);
            HierarchyUtility.TryAddHierarchyIcon(typeof(CameraFreelookRig), icon_CameraFreelookRig, AssetDatabase.LoadAssetAtPath(filePath + $"{icon_CameraFreelookRig}.png", typeof(Texture)) as Texture);
            HierarchyUtility.TryAddHierarchyIcon(typeof(CameraSwitcher), icon_CameraSwitcher, AssetDatabase.LoadAssetAtPath(filePath + $"{icon_CameraSwitcher}.png", typeof(Texture)) as Texture);
            HierarchyUtility.TryAddHierarchyIcon(typeof(CameraShaker), icon_CameraShaker, AssetDatabase.LoadAssetAtPath(filePath + $"{icon_CameraShaker}.png", typeof(Texture)) as Texture);
            HierarchyUtility.TryAddHierarchyIcon(typeof(CameraTriggerZone), icon_CameraTriggerZone, AssetDatabase.LoadAssetAtPath(filePath + $"{icon_CameraTriggerZone}.png", typeof(Texture)) as Texture);
            HierarchyUtility.TryAddHierarchyIcon(typeof(CameraTarget), icon_CameraTarget, AssetDatabase.LoadAssetAtPath(filePath + $"{icon_CameraTarget}.png", typeof(Texture)) as Texture);
            HierarchyUtility.TryAddHierarchyIcon(typeof(CameraMultiTargetingRig), icon_CameraMultiTargetingRig, AssetDatabase.LoadAssetAtPath(filePath + $"{icon_CameraMultiTargetingRig}.png", typeof(Texture)) as Texture);
            HierarchyUtility.TryAddHierarchyIcon(typeof(ShadowTransform), icon_ShadowTransform, AssetDatabase.LoadAssetAtPath(filePath + $"{icon_ShadowTransform}.png", typeof(Texture)) as Texture);
        }

        #endregion
        
    } // class end
}

#endif
#endif
#endif