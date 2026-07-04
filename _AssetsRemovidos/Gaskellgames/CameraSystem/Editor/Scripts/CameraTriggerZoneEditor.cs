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
    
    [CustomEditor(typeof(CameraTriggerZone)), CanEditMultipleObjects]
    public class CameraTriggerZoneEditor : GgEditor
    {
        #region Serialized Properties / OnEnable

        private CameraTriggerZone targetAsType;
        
        private SerializedProperty gizmosOnSelected;
        private SerializedProperty cameraRig;
        private SerializedProperty triggerColour;
        private SerializedProperty triggerOutlineColour;
        
        private static int selectedTab = 0;
        private string[] tabs = new[] { "Settings", "Debug" };
        private int settingsTab = 0;
        private int debugTab = 1;
        
        private const string packageRefName = "CameraSystem";
        private Texture banner;

        private void OnEnable()
        {
            banner = EditorWindowUtility.LoadInspectorBanner();
            
            gizmosOnSelected = serializedObject.FindProperty("gizmosOnSelected");
            cameraRig = serializedObject.FindProperty("cameraRig");
            triggerColour = serializedObject.FindProperty("triggerColour");
            triggerOutlineColour = serializedObject.FindProperty("triggerOutlineColour");
        }

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region OnInspectorGUI

        public override void OnInspectorGUI()
        {
            // get & update references
            if (!targetAsType) { targetAsType = target as CameraTriggerZone; }
            serializedObject.Update();

            // draw banner if turned on in Gaskellgames settings
            EditorWindowUtility.TryDrawBanner(banner, nameof(CameraTriggerZone).NicifyName());

            // custom inspector
            selectedTab = GUILayout.Toolbar(selectedTab, tabs);
            EditorGUILayout.Space();
            if (selectedTab == settingsTab)
            {
                EditorGUILayout.PropertyField(cameraRig);
            }
            else if (selectedTab == debugTab)
            {
                EditorGUILayout.PropertyField(gizmosOnSelected);
                EditorGUILayout.PropertyField(triggerColour);
                EditorGUILayout.PropertyField(triggerOutlineColour);
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