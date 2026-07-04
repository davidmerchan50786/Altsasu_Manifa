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

    [CustomEditor(typeof(CameraBrain)), CanEditMultipleObjects]
    public class CameraBrainEditor : GgEditor
    {
        #region Serialized Properties / OnEnable

        private SerializedProperty activeCamera;
        private SerializedProperty previousCamera;

        private SerializedProperty blendingStyle;
        private SerializedProperty fadeCurve;
        private SerializedProperty fadeColor;
        private SerializedProperty fadeSpeed;
        private SerializedProperty fadeFullScreen;
        private SerializedProperty canvasGroup;
        
        private SerializedProperty blendCurve;
        private SerializedProperty blendSpeed;

        private SerializedProperty follow;
        private SerializedProperty lookAt;
        private SerializedProperty lens;
        private SerializedProperty cameraOrbit;
        
        private static int selectedTab = 0;
        private string[] tabs = new[] { "Settings", "Debug" };
        private int settingsTab = 0;
        private int debugTab = 1;
        
        private const string packageRefName = "CameraSystem";
        private Texture banner;

        private void OnEnable()
        {
            banner = EditorWindowUtility.LoadInspectorBanner();
            
            activeCamera = serializedObject.FindProperty("activeCamera");
            previousCamera = serializedObject.FindProperty("previousCamera");

            blendingStyle = serializedObject.FindProperty("blendingStyle");
            fadeCurve = serializedObject.FindProperty("fadeCurve");
            fadeColor = serializedObject.FindProperty("fadeColor");
            fadeSpeed = serializedObject.FindProperty("fadeSpeed");
            fadeFullScreen = serializedObject.FindProperty("fadeFullScreen");
            canvasGroup = serializedObject.FindProperty("canvasGroup");

            blendCurve = serializedObject.FindProperty("blendCurve");
            blendSpeed = serializedObject.FindProperty("blendSpeed");

            follow = serializedObject.FindProperty("follow");
            lookAt = serializedObject.FindProperty("lookAt");
            lens = serializedObject.FindProperty("lens");
            cameraOrbit = serializedObject.FindProperty("cameraOrbit");
        }

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region OnInspectorGUI

        public override void OnInspectorGUI()
        {
            // get & update references
            CameraBrain cameraBrain = (CameraBrain)target;
            serializedObject.Update();

            // draw banner if turned on in Gaskellgames settings
            EditorWindowUtility.TryDrawBanner(banner, nameof(CameraBrain).NicifyName());

            // custom inspector
            selectedTab = GUILayout.Toolbar(selectedTab, tabs);
            EditorGUILayout.Space();
            if (selectedTab == settingsTab)
            {
                EditorGUILayout.PropertyField(activeCamera);
                
                EditorGUILayout.PropertyField(blendingStyle);
                EditorGUI.indentLevel++;
                if (blendingStyle.enumValueIndex == CameraBrain.CameraBlendStyle.FadeToColor.ToInt())
                {
                    EditorGUILayout.PropertyField(fadeCurve);
                    EditorGUILayout.PropertyField(fadeSpeed);
                    EditorGUILayout.PropertyField(fadeFullScreen);
                    EditorGUILayout.PropertyField(!fadeFullScreen.boolValue ? canvasGroup : fadeColor);
                }
                else if (blendingStyle.enumValueIndex == CameraBrain.CameraBlendStyle.MoveToPosition.ToInt())
                {
                    EditorGUILayout.PropertyField(blendCurve);
                    EditorGUILayout.PropertyField(blendSpeed);
                }
                EditorGUI.indentLevel--;
            }
            else if (selectedTab == debugTab)
            {
                EditorGUILayout.PropertyField(previousCamera);
                
                CameraRig rig = cameraBrain.ActiveCamera;
                if (!rig)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.HelpBox("No Active Camera Assigned", MessageType.Warning);
                }
                EditorGUILayout.PropertyField(follow);
                EditorGUILayout.PropertyField(lookAt);
                EditorGUILayout.PropertyField(lens);
                if (rig)
                {
                    CameraFreelookRig FreelookRig = rig.FreelookRig;
                    if (FreelookRig != null)
                    {
                        EditorGUILayout.PropertyField(cameraOrbit);
                    }
                }
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