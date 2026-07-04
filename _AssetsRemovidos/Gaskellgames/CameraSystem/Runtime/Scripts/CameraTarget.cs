#if GASKELLGAMES
#if GASKELLGAMES_INPUTEVENTSYSTEM
using UnityEngine;

namespace Gaskellgames.CameraSystem
{
    /// <remarks>
    /// Code created by Gaskellgames: https://gaskellgames.com
    /// </remarks>
    
    [AddComponentMenu("Gaskellgames/Camera System/Camera Target")]
    public class CameraTarget : GgMonoBehaviour
    {
        #region Variables

        public enum TargetTypes
        {
            None,
            OnEnable,
            OnTrigger
        }

        [SerializeField]
        [Tooltip("The type of camera target:\n- None: does nothing.\n- OnEnable: Auto-add this camera target to a targetingRig during OnEnable.\n- OnTrigger: Auto-add this camera target to a targetingRig during OnTrigger.")]
        private TargetTypes targetType = TargetTypes.None;
        
        [SerializeField]
        [Tooltip("Automatically find the single Multi-Targeting Rig in the scene.")]
        private bool autoFindMultiTarget = true;
        
        [SerializeField, Required, Indent]
        [Tooltip("Reference to a specific Multi-Targeting Rig in the scene.")]
        private CameraMultiTargetingRig multiTargetingRig;
        
        [SerializeField, TagDropdown]
        [Tooltip("Tag of the CameraTriggerZone that should trigger this camera target.")]
        private string triggerTag = "";
        
        [SerializeField]
        [Tooltip("Add a cameraBrain to set the active camera to the 'CameraSensor' cameraRig during OnTriggerEnter.")]
        private CameraBrain cameraBrain;
        
        [SerializeField, Indent]
        [Tooltip("Sets the active camera to the previously used cameraRig during OnTriggerExit.")]
        private bool revertOnExit;
        
        [Tooltip("Event is invoked upon successfully entering a CameraTriggerZone.")]
        public GgEvent<CameraTarget> OnEnterTag;
        
        [Tooltip("Event is invoked upon successfully exit of a CameraTriggerZone.")]
        public GgEvent<CameraTarget> OnExitTag;
        
        private CameraRig previousCamera;

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region OnEvents

        private void OnEnable()
        {
            if (targetType != TargetTypes.OnEnable) { return; }
            if (autoFindMultiTarget)
            {
                multiTargetingRig = FindObjectOfType<CameraMultiTargetingRig>();
            }
            multiTargetingRig.AddTargetToList(transform);
        }

        private void OnDisable()
        {
            if (targetType != TargetTypes.OnEnable) { return; }
            multiTargetingRig.RemoveTargetFromList(transform);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (targetType != TargetTypes.OnTrigger) { return; }
            if (!string.IsNullOrEmpty(triggerTag))
            {
                if (!other.CompareTag(triggerTag)) { return; }
            }
            
            if (cameraBrain != null)
            {
                CameraTriggerZone cameraTriggerZone = other.GetComponent<CameraTriggerZone>();
                if (cameraTriggerZone != null)
                {
                    previousCamera = cameraBrain.ActiveCamera;
                    cameraBrain.ActiveCamera = cameraTriggerZone.CameraRig;
                }
            }

            CameraMultiTargetingRig cameraMultiTargetingRig = other.GetComponent<CameraMultiTargetingRig>();
            if (cameraMultiTargetingRig != null)
            {
                cameraMultiTargetingRig.AddTargetToList(transform);
            }

            OnEnterTag?.Invoke(this);
        }

        private void OnTriggerExit(Collider other)
        {
            if (targetType != TargetTypes.OnTrigger) { return; }
            if (!string.IsNullOrEmpty(triggerTag))
            {
                if (!other.CompareTag(triggerTag)) { return; }
            }
            
            if (revertOnExit)
            {
                cameraBrain.ActiveCamera = previousCamera;
            }

            CameraMultiTargetingRig cameraMultiTargetingRig = other.GetComponent<CameraMultiTargetingRig>();
            if (cameraMultiTargetingRig != null)
            {
                cameraMultiTargetingRig.RemoveTargetFromList(transform);
            }

            OnExitTag?.Invoke(this);
        }

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Getter / Setter

        /// <summary>
        /// The type of camera target:
        /// None: does nothing.
        /// OnEnable: Auto-add this camera target to a targetingRig during OnEnable.
        /// OnEnable: Auto-add this camera target to a targetingRig during OnTrigger.
        /// </summary>
        public TargetTypes TargetType
        {
            get => targetType;
            set => targetType = value;
        }

        #endregion

    } //class end
}

#endif
#endif