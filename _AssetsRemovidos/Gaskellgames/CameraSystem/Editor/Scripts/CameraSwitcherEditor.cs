#if UNITY_EDITOR
#if GASKELLGAMES
#if GASKELLGAMES_INPUTEVENTSYSTEM
using Gaskellgames.EditorOnly;
using UnityEditor;
using UnityEngine;

namespace Gaskellgames.CameraSystem.EditorOnly
{
    /// <remarks>
    /// Code created by Gaskellgames: https://gaskellgames.com
    /// </remarks>
    
    [CustomEditor(typeof(CameraSwitcher)), CanEditMultipleObjects]
    public class CameraSwitcherEditor : GgEditor
    {
        #region Serialized Properties / OnEnable

        private CameraSwitcher targetAsType;
        
        private SerializedProperty cameraBrain;
        private SerializedProperty useRegisteredList;
        private SerializedProperty switchCamera;
        private SerializedProperty customCameraRigsList;
        
        private const string packageRefName = "CameraSystem";
        private Texture banner;

        private void OnEnable()
        {
            banner = EditorWindowUtility.LoadInspectorBanner();
            
            cameraBrain = serializedObject.FindProperty("cameraBrain");
            useRegisteredList = serializedObject.FindProperty("useRegisteredList");
            switchCamera = serializedObject.FindProperty("switchCamera");
            customCameraRigsList = serializedObject.FindProperty("customCameraRigsList");
        }

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region OnInspectorGUI

        public override void OnInspectorGUI()
        {
            // get & update references
            if (!targetAsType) { targetAsType = target as CameraSwitcher; }
            serializedObject.Update();

            // draw banner if turned on in Gaskellgames settings
            EditorWindowUtility.TryDrawBanner(banner, nameof(CameraSwitcher).NicifyName());

            // custom inspector
            EditorGUILayout.PropertyField(useRegisteredList);
            EditorGUILayout.PropertyField(cameraBrain);
            EditorGUILayout.PropertyField(switchCamera);

            if (!useRegisteredList.boolValue)
            {
                EditorGUILayout.PropertyField(customCameraRigsList);
            }

            // apply reference changes
            serializedObject.ApplyModifiedProperties();
        }

        #endregion
        
    } // class end
}

#endif
#endif
#endif