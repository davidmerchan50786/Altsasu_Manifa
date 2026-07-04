#if GASKELLGAMES
#if GASKELLGAMES_INPUTEVENTSYSTEM
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Gaskellgames.CameraSystem
{
    /// <remarks>
    /// Code created by Gaskellgames: https://gaskellgames.com
    /// </remarks>
    
    [AddComponentMenu("Gaskellgames/Camera System/Camera Switcher")]
    public class CameraSwitcher : GgMonoBehaviour
    {
        #region Variables
        
        [SerializeField]
        [Tooltip("Use the global camera list")]
        private bool useRegisteredList = true;

        [SerializeField, Required]
        [Tooltip("The cameraBrain to switch active cameraRigs.")]
        private CameraBrain cameraBrain;
        
        [SerializeField, Required]
        [Tooltip("The user input used to toggle switching to the next cameraRig.")]
        private InputActionReference switchCamera;
        
        [SerializeField, Required]
        [Tooltip("List of cameraRigs to switch between when 'switchCamera' input is pressed.")]
        private List<CameraRig> customCameraRigsList;

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Reset [EditorOnly]

#if UNITY_EDITOR

        private void Reset()
        {
            if (GetComponent<CameraBrain>() != null)
            {
                cameraBrain = GetComponent<CameraBrain>();
            }
        }

#endif

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Game loop

        private void OnEnable()
        {
            switchCamera.action.performed += SwitchCameraCallback;
            switchCamera.action.Enable();
        }

        private void OnDisable()
        {
            switchCamera.action.performed -= SwitchCameraCallback;
            switchCamera.action.Disable();
        }

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Private Methods
        
        private void SwitchCameraCallback(InputAction.CallbackContext context)
        {
            SwitchToNextCamera();
        }

        private void SwitchToNextCamera()
        {
            if (cameraBrain == null) { return; }
            
            List<CameraRig> registeredCameras = CameraList.GetCameraRigList();
            if (useRegisteredList && 1 < registeredCameras.Count)
            {
                SelectNextCamera(registeredCameras);
            }
            else if (1 < customCameraRigsList.Count)
            {
                SelectNextCamera(customCameraRigsList);
            }
        }

        private void SelectNextCamera(List<CameraRig> cameraList)
        {
            CameraRig active = cameraBrain.ActiveCamera;
            int activeIndex = -1;

            for (int i = 0; i < cameraList.Count; i++)
            {
                if (cameraList[i] == active) { activeIndex = i; }
            }

            if (activeIndex != -1)
            {
                activeIndex = activeIndex == cameraList.Count - 1 ? 0 : activeIndex + 1;
                cameraBrain.ActiveCamera = cameraList[activeIndex];
            }
            else
            {
                cameraBrain.ActiveCamera = cameraList[0];
            }
        }

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Getters / Setters

        /// <summary>
        /// Switch to the next CameraRig in the list
        /// </summary>
        public void ToggleNextCamera()
        {
            SwitchToNextCamera();
        }

        #endregion


    } // class end
}



#endif
#endif