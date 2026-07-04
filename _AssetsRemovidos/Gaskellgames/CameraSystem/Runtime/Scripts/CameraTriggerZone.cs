#if GASKELLGAMES
#if GASKELLGAMES_INPUTEVENTSYSTEM
using UnityEngine;

namespace Gaskellgames.CameraSystem
{
    /// <remarks>
    /// Code created by Gaskellgames: https://gaskellgames.com
    /// </remarks>
    
    [AddComponentMenu("Gaskellgames/Camera System/Camera Trigger Zone")]
    public class CameraTriggerZone : GgMonoBehaviour
    {
        #region Variables

        [SerializeField, Required]
        [Tooltip("Reference to the cameraRig this trigger zone controls.")]
        private CameraRig cameraRig;
        
        [SerializeField]
        [Tooltip("Gizmo color for the trigger.")]
        private Color32 triggerColour = new Color32(000, 179, 223, 079);
        
        [SerializeField]
        [Tooltip("Gizmo color for the trigger outline.")]
        private Color32 triggerOutlineColour = new Color32(000, 179, 223, 128);
        
        private Rigidbody rb;

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Game Loop

        private void Reset()
        {
            SetupCollider();
        }

        #endregion

        //----------------------------------------------------------------------------------------------------

#if UNITY_EDITOR

        #region Editor Gizmos

        protected override void OnDrawGizmosConditional(bool selected)
        {
            Matrix4x4 resetMatrix = Gizmos.matrix;
            Gizmos.matrix = gameObject.transform.localToWorldMatrix;

            Gizmos.color = triggerColour;
            Gizmos.DrawCube(Vector3.zero, Vector3.one);
            Gizmos.color = triggerOutlineColour;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);

            Gizmos.matrix = resetMatrix;
        }

        #endregion

#endif

        //----------------------------------------------------------------------------------------------------

        #region Private Methods

        private void SetupCollider()
        {
            if (gameObject.GetComponent<Collider>() == null)
            {
                gameObject.AddComponent<BoxCollider>();
            }
            gameObject.GetComponent<Collider>().isTrigger = true;
        }
        
        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Getter / Setter

        /// <summary>
        /// Get/Set the reference to the cameraRig this trigger zone controls.
        /// </summary>
        public CameraRig CameraRig
        {
            get => cameraRig;
            set => cameraRig = value;
        }

        #endregion

    } //class end
}

#endif
#endif