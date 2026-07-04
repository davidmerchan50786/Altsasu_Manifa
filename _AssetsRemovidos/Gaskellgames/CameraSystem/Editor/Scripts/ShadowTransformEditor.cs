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

    [CustomEditor(typeof(ShadowTransform))] [CanEditMultipleObjects]
    public class ShadowTransformEditor : Editor
    {
        #region Serialized Properties / OnEnable

        private ShadowTransform targetAsType;

        private SerializedProperty updateInEditor;
        private SerializedProperty follow;
        private SerializedProperty followX;
        private SerializedProperty followY;
        private SerializedProperty followZ;
        private SerializedProperty lookAt;
        private SerializedProperty rotateWith;
        private SerializedProperty updateType;
        private SerializedProperty rotateWithX;
        private SerializedProperty rotateWithY;
        private SerializedProperty rotateWithZ;
        private SerializedProperty turnSpeed;
        
        private static int selectedTab = 0;
        private string[] tabs = new[] { "Settings", "Debug" };
        private int settingsTab = 0;
        private int debugTab = 1;

        private bool FoldoutGroup;

        private const string packageRefName = "CameraSystem";
        private Texture banner;

        private void OnEnable()
        {
            banner = EditorWindowUtility.LoadInspectorBanner();
            
            updateInEditor = serializedObject.FindProperty("updateInEditor");
            follow = serializedObject.FindProperty("follow");
            followX = serializedObject.FindProperty("followX");
            followY = serializedObject.FindProperty("followY");
            followZ = serializedObject.FindProperty("followZ");
            lookAt = serializedObject.FindProperty("lookAt");
            rotateWith = serializedObject.FindProperty("rotateWith");
            updateType = serializedObject.FindProperty("updateType");
            rotateWithX = serializedObject.FindProperty("rotateWithX");
            rotateWithY = serializedObject.FindProperty("rotateWithY");
            rotateWithZ = serializedObject.FindProperty("rotateWithZ");
            turnSpeed = serializedObject.FindProperty("turnSpeed");
        }

        #endregion
        
        //----------------------------------------------------------------------------------------------------

        #region OnInspectorGUI

        public override void OnInspectorGUI()
        {
            // get & update references
            if (!targetAsType) { targetAsType = target as ShadowTransform; }
            serializedObject.Update();

            // draw banner if turned on in Gaskellgames settings
            EditorWindowUtility.TryDrawBanner(banner, nameof(ShadowTransform).NicifyName());

            // custom inspector
            selectedTab = GUILayout.Toolbar(selectedTab, tabs);
            EditorGUILayout.Space();
            if (selectedTab == settingsTab)
            {
                EditorGUILayout.PropertyField(follow);
                EditorGUILayout.PropertyField(lookAt);
                EditorGUILayout.PropertyField(rotateWith);
                EditorGUILayout.PropertyField(updateType);
                FoldoutGroup = EditorGUILayout.Foldout(FoldoutGroup, "Constraints");
                if (FoldoutGroup)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PrefixLabel(new GUIContent("Follow Axis", "The axis that this gameObject should shadow from the 'Follow' reference."));
                    EditorGUI.indentLevel--;
                    followX.boolValue = EditorGUILayout.ToggleLeft("X", followX.boolValue, GUILayout.Width(28), GUILayout.ExpandWidth(false));
                    followY.boolValue = EditorGUILayout.ToggleLeft("Y", followY.boolValue, GUILayout.Width(28), GUILayout.ExpandWidth(false));
                    followZ.boolValue = EditorGUILayout.ToggleLeft("Z", followZ.boolValue, GUILayout.Width(28), GUILayout.ExpandWidth(false));
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.BeginHorizontal();
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PrefixLabel(new GUIContent("Rotation Axis", "The axis that this gameObject should shadow from the 'RotateWith' reference."));
                    EditorGUI.indentLevel--;
                    rotateWithX.boolValue = EditorGUILayout.ToggleLeft("X", rotateWithX.boolValue, GUILayout.Width(28), GUILayout.ExpandWidth(false));
                    rotateWithY.boolValue = EditorGUILayout.ToggleLeft("Y", rotateWithY.boolValue, GUILayout.Width(28), GUILayout.ExpandWidth(false));
                    rotateWithZ.boolValue = EditorGUILayout.ToggleLeft("Z", rotateWithZ.boolValue, GUILayout.Width(28), GUILayout.ExpandWidth(false));
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.PropertyField(turnSpeed);
                if (lookAt.objectReferenceValue && rotateWith.objectReferenceValue)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.HelpBox("Transform rotation is being driven by LookAt. RotateWith will be ignored", MessageType.Info);
                }
            }
            else if (selectedTab == debugTab)
            {
                EditorGUILayout.PropertyField(updateInEditor);
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