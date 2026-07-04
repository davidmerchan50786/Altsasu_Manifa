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
    
    [CustomEditor(typeof(CameraRig)), CanEditMultipleObjects]
    public class CameraRigEditor : GgEditor
    {
        #region Serialized Properties / OnEnable

        private CameraRig targetAsType;
        
        private SerializedProperty verboseLogs;
        private SerializedProperty gizmosOnSelected;
        
        private SerializedProperty freelookRig;
        private SerializedProperty registerCamera;
        private SerializedProperty cameraShake;
        private SerializedProperty freeFlyCamera;
        private SerializedProperty gmkInputController;
        private SerializedProperty moveSpeed;
        private SerializedProperty boostSpeed;
        private SerializedProperty xSensitivity;
        private SerializedProperty ySensitivity;
        private SerializedProperty freeFlyActive;
        private SerializedProperty follow;
        private SerializedProperty followOffset;
        private SerializedProperty lookAt;
        private SerializedProperty turnSpeed;
        private SerializedProperty shakeSmoothing;
        private SerializedProperty positionMagnitude;
        private SerializedProperty rotationMagnitude;
        private SerializedProperty lens;
        private SerializedProperty frustumColor;
        
        private static int selectedTab = 0;
        private string[] tabs = new[] { "Settings", "Debug" };
        private int settingsTab = 0;
        private int debugTab = 1;
        
        private const string packageRefName = "CameraSystem";
        private Texture banner;

        private void OnEnable()
        {
            banner = EditorWindowUtility.LoadInspectorBanner();
            
            verboseLogs = serializedObject.FindProperty("verboseLogs");
            gizmosOnSelected = serializedObject.FindProperty("gizmosOnSelected");
            
            freelookRig = serializedObject.FindProperty("freelookRig");
            registerCamera = serializedObject.FindProperty("registerCamera");
            cameraShake = serializedObject.FindProperty("cameraShake");
            freeFlyCamera = serializedObject.FindProperty("freeFlyCamera");
            gmkInputController = serializedObject.FindProperty("gmkInputController");
            moveSpeed = serializedObject.FindProperty("moveSpeed");
            boostSpeed = serializedObject.FindProperty("boostSpeed");
            xSensitivity = serializedObject.FindProperty("xSensitivity");
            ySensitivity = serializedObject.FindProperty("ySensitivity");
            freeFlyActive = serializedObject.FindProperty("freeFlyActive");
            follow = serializedObject.FindProperty("follow");
            followOffset = serializedObject.FindProperty("followOffset");
            lookAt = serializedObject.FindProperty("lookAt");
            turnSpeed = serializedObject.FindProperty("turnSpeed");
            shakeSmoothing = serializedObject.FindProperty("shakeSmoothing");
            positionMagnitude = serializedObject.FindProperty("positionMagnitude");
            rotationMagnitude = serializedObject.FindProperty("rotationMagnitude");
            lens = serializedObject.FindProperty("lens");
            frustumColor = serializedObject.FindProperty("frustumColor");
        }

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region OnInspectorGUI

        public override void OnInspectorGUI()
        {
            // get & update references
            if (!targetAsType) { targetAsType = target as CameraRig; }
            serializedObject.Update();

            // draw banner if turned on in Gaskellgames settings
            EditorWindowUtility.TryDrawBanner(banner, nameof(CameraRig).NicifyName());
            
            // custom inspector
            selectedTab = GUILayout.Toolbar(selectedTab, tabs);
            EditorGUILayout.Space();
            if (selectedTab == settingsTab)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(registerCamera);
                EditorGUILayout.PropertyField(freeFlyCamera);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(cameraShake);
                bool enabled = GUI.enabled;
                GUI.enabled = freeFlyCamera.boolValue;
                EditorGUILayout.PropertyField(freeFlyActive);
                GUI.enabled = enabled;
                EditorGUILayout.EndHorizontal();
                if (freeFlyCamera.boolValue)
                {
                    EditorGUILayout.PropertyField(gmkInputController);
                    EditorGUILayout.PropertyField(moveSpeed);
                    EditorGUILayout.PropertyField(boostSpeed);
                    EditorGUILayout.PropertyField(xSensitivity);
                    EditorGUILayout.PropertyField(ySensitivity);
                }
                else
                {
                    EditorGUILayout.PropertyField(freelookRig);
                    EditorGUILayout.PropertyField(follow);
                    if (follow.objectReferenceValue) { EditorGUILayout.PropertyField(followOffset); }
                    EditorGUILayout.PropertyField(lookAt);
                    EditorGUILayout.PropertyField(turnSpeed);
                }
            
                if (cameraShake.boolValue)
                {
                    EditorGUILayout.PropertyField(shakeSmoothing);
                    EditorGUILayout.PropertyField(positionMagnitude);
                    EditorGUILayout.PropertyField(rotationMagnitude);
                }
                EditorGUILayout.PropertyField(lens);

                if (!freeFlyCamera.boolValue)
                {
                    if (freelookRig.objectReferenceValue != null)
                    {
                        if (lookAt.objectReferenceValue != null)
                        {
                            EditorGUILayout.Space();
                            EditorGUILayout.HelpBox("Follow, TurnSpeed & FollowOffset are being driven by the CameraFreelookRig", MessageType.Info);
                            EditorGUILayout.HelpBox("LookAt is being driven by the CameraRig", MessageType.Info);
                        }
                        else
                        {
                            EditorGUILayout.Space();
                            EditorGUILayout.HelpBox( "Follow, LookAt, TurnSpeed & FollowOffset are being driven by the CameraFreelookRig", MessageType.Info);
                        }
                    }
                }
            }
            else if (selectedTab == debugTab)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(verboseLogs);
                EditorGUILayout.PropertyField(gizmosOnSelected);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.PropertyField(frustumColor);
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