#if UNITY_EDITOR
#if GASKELLGAMES
#if GASKELLGAMES_INPUTEVENTSYSTEM
using Gaskellgames.EditorOnly;
using Gaskellgames.InputEventSystem;
using Gaskellgames.InputEventSystem.EditorOnly;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Gaskellgames.CameraSystem.EditorOnly
{
    /// <remarks>
    /// Code created by Gaskellgames: https://gaskellgames.com
    /// </remarks>
    
    public class MenuItem_CameraSystem : MenuItemUtility
    {
        #region GameObject Menu
        
        [MenuItem(CameraSystem_GameObjectMenu_Path + "/Camera Brain", false, GgGameObjectMenu_Priority)]
        private static void Gaskellgames_GameObjectMenu_CameraBrain(MenuCommand menuCommand)
        {
            // create a custom gameObject, register in the undo system, parent and set position relative to context
            GameObject go = SetupMenuItemInContext(menuCommand, "CameraBrain");
            
            // add scripts & components
            go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
            go.AddComponent<CameraBrain>();
            
            // select newly created gameObject
            Selection.activeObject = go;
        }
        
        [MenuItem(CameraSystem_GameObjectMenu_Path + "/Camera Rig", false, GgGameObjectMenu_Priority + 15)]
        private static void Gaskellgames_GameObjectMenu_CameraRig(MenuCommand menuCommand)
        {
            // create a custom gameObject, register in the undo system, parent and set position relative to context
            GameObject go = SetupMenuItemInContext(menuCommand, "CameraRig");
            
            // add scripts & components
            go.AddComponent<CameraRig>();
            
            // select newly created gameObject
            Selection.activeObject = go;
        }
        
        [MenuItem(CameraSystem_GameObjectMenu_Path + "/Camera Rig (Free Fly)", false, GgGameObjectMenu_Priority + 15)]
        private static void Gaskellgames_GameObjectMenu_CameraRigFreeFly(MenuCommand menuCommand)
        {
            // create a custom gameObject, register in the undo system, parent and set position relative to context
            GMKInputController gmk = MenuItem_InputEventSystem.AddPlayerInputController(menuCommand);
            GameObject go = gmk.gameObject;
            go.name = "CameraRig (Free Fly)";
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            GameObject context = (GameObject)menuCommand.context;
            if (context != null) { go.transform.SetParent(context.transform); }
            go.transform.localPosition = Vector3.zero;
            
            // add scripts & components
            CameraRig cr = go.AddComponent<CameraRig>();
            cr.GMKInputController = gmk;
            cr.IsFreeFlyCamera = true;
            ComponentUtility.MoveComponentUp(cr);
            ComponentUtility.MoveComponentUp(cr);
            
            // select newly created gameObject
            Selection.activeObject = go;
        }
        
        [MenuItem(CameraSystem_GameObjectMenu_Path + "/Camera Rig (Freelook)", false, GgGameObjectMenu_Priority + 15)]
        private static void Gaskellgames_GameObjectMenu_CameraFreelookRig(MenuCommand menuCommand)
        {
            // add input action manager
            MenuItem_InputEventSystem.AddInputEventManager(menuCommand);
            
            // create a custom gameObject, register in the undo system, parent and set position relative to context
            GameObject go = SetupMenuItemInContext(menuCommand, "CameraRig (Freelook)");
            
            // add scripts & components
            go.AddComponent<CameraFreelookRig>();
            
            // select newly created gameObject
            Selection.activeObject = go;
        }
        
        [MenuItem(CameraSystem_GameObjectMenu_Path + "/Camera Shaker", false, GgGameObjectMenu_Priority + 45)]
        private static void Gaskellgames_GameObjectMenu_CameraShaker(MenuCommand menuCommand)
        {
            // create a custom gameObject, register in the undo system, parent and set position relative to context
            GameObject go = SetupMenuItemInContext(menuCommand, "CameraShaker");
            
            // add scripts & components
            go.AddComponent<CameraShaker>();
            
            // select newly created gameObject
            Selection.activeObject = go;
        }
        
        [MenuItem(CameraSystem_GameObjectMenu_Path + "/Camera Trigger Zone", false, GgGameObjectMenu_Priority + 45)]
        private static void Gaskellgames_GameObjectMenu_CameraTriggerZone(MenuCommand menuCommand)
        {
            // create a custom gameObject, register in the undo system, parent and set position relative to context
            GameObject go = SetupMenuItemInContext(menuCommand, "CameraTriggerZone");
            
            // Create child gameObjects
            GameObject goChild1 = new GameObject("CameraRig");
            GameObject goChild2 = new GameObject("Ref: CamLookAt");
            GameObject goChild3 = new GameObject("CameraTriggerZone");
            goChild1.transform.SetParent(go.transform);
            goChild2.transform.SetParent(go.transform);
            goChild3.transform.SetParent(go.transform);
            
            // add scripts & components
            goChild1.transform.position = new Vector3(-3, 2, -3);
            CameraRig cr = goChild1.AddComponent<CameraRig>();
            cr.LookAt = goChild2.transform;
            goChild2.transform.position = Vector3.zero;
            goChild3.transform.position = Vector3.zero;
            goChild3.transform.localScale = new Vector3(3, 0.2f, 3);
            BoxCollider box = goChild3.AddComponent<BoxCollider>();
            box.isTrigger = true;
            CameraTriggerZone ctz = goChild3.AddComponent<CameraTriggerZone>();
            ctz.CameraRig = cr;
            
            // select newly created gameObject
            Selection.activeObject = go;
        }
        
        [MenuItem(CameraSystem_GameObjectMenu_Path + "/Camera Trigger Zone (Multi Target)", false, GgGameObjectMenu_Priority + 45)]
        private static void Gaskellgames_GameObjectMenu_CameraTriggerZoneMultiTarget(MenuCommand menuCommand)
        {
            // create a custom gameObject, register in the undo system, parent and set position relative to context
            GameObject go = SetupMenuItemInContext(menuCommand, "CameraTriggerZone (Multi Target)");
            
            // create child gameObjects
            GameObject goChild1 = new GameObject("CameraRig");
            GameObject goChild2 = new GameObject("Ref: CamLookAt");
            GameObject goChild3 = new GameObject("CameraTriggerZone");
            goChild1.transform.SetParent(go.transform);
            goChild2.transform.SetParent(go.transform);
            goChild3.transform.SetParent(go.transform);
            
            // add scripts & components
            goChild1.transform.position = new Vector3(-3, 2, -3);
            CameraRig cr = goChild1.AddComponent<CameraRig>();
            cr.LookAt = goChild2.transform;
            goChild2.transform.position = Vector3.zero;
            goChild3.transform.position = Vector3.zero;
            goChild3.transform.localScale = new Vector3(3, 0.2f, 3);
            BoxCollider box = goChild3.AddComponent<BoxCollider>();
            box.isTrigger = true;
            CameraMultiTargetingRig cmt = goChild3.AddComponent<CameraMultiTargetingRig>();
            cmt.RefCamLookAt = goChild2.transform;
            CameraTriggerZone ctz = goChild3.GetComponent<CameraTriggerZone>();
            ctz.CameraRig = cr;
            
            // select newly created gameObject
            Selection.activeObject = go;
        }
        
        #endregion
        
    } // class end
}

#endif
#endif
#endif