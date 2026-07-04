#if GASKELLGAMES
#if GASKELLGAMES_INPUTEVENTSYSTEM
using UnityEngine;
using System.Collections.Generic;

namespace Gaskellgames.CameraSystem
{
    /// <remarks>
    /// Code created by Gaskellgames: https://gaskellgames.com
    /// </remarks>
    
    [RequireComponent(typeof(CameraTriggerZone))]
    [AddComponentMenu("Gaskellgames/Camera System/Camera Multi-Targeting Rig")]
    public class CameraMultiTargetingRig : GgMonoBehaviour, IEditorUpdate
    {
        #region Variables
        
        [SerializeField]
        [Tooltip("Toggle whether this Multi-TargetingRig can override the lens settings for a referenced cameraRig.")]
        private bool zoomCamera;
        
        [SerializeField, Required]
        [Tooltip("The reference transform that the cameraRig should look at.")]
        private Transform refCamLookAt;
        
        [SerializeField, Range(0, 10)]
        [Tooltip("The movement speed of the refCamLookAt point.")]
        private float moveSpeed = 2.0f;
        
        [SerializeField]
        [Tooltip("Reference to all transforms currently being targeted by the cameraRig.")]
        private List<Transform> targetObjects;
        
        [Title("Camera Zoom")]
        [SerializeField, Required]
        [Tooltip("Reference to a cameraRig, that this Multi-TargetingRig should override.")]
        private CameraRig cameraRig;
        
        [SerializeField]
        [Tooltip("Toggle whether this Multi-TargetingRig should calculate the bounds on the x-axis.")]
        private bool boundsX;
        
        [SerializeField]
        [Tooltip("Toggle whether this Multi-TargetingRig should calculate the bounds on the y-axis.")]
        private bool boundsY;
        
        [SerializeField]
        [Tooltip("Toggle whether this Multi-TargetingRig should calculate the bounds on the z-axis.")]
        private bool boundsZ;
        
        [SerializeField, Range(0, 50)]
        [Tooltip("Extra offset spacing around the bounding area.")]
        private int padding = 10;
        
        [SerializeField, Range(0, 120)]
        [Tooltip("The minimum zoom this Multi-TargetingRig can set the referenced cameraRig to.")]
        private int minZoom = 50;
        
        [SerializeField, Range(0, 120)]
        [Tooltip("The maximum zoom this Multi-TargetingRig can set the referenced cameraRig to.")]
        private int maxZoom = 10;
        
        [SerializeField, Range(0, 10)]
        [Tooltip("The speed this Multi-TargetingRig can change the zoom at.")]
        private float zoomSpeed = 2.0f;

        [SerializeField]
        [Tooltip("Gizmo debug color.")]
        private Color defaultColor = new Color32(000, 079, 223, 255);
        
        [SerializeField]
        [Tooltip("Gizmo debug color.")]
        private Color trackedColor = new Color32(000, 179, 223, 255);
        
        private Vector3 defaultPosition;
        private Vector3 averagePosition;
        private Vector3 targetPosition;

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Editor Loop [IEditorUpdate]
        
        public void EditorUpdate()
        {
            if (refCamLookAt) { defaultPosition = refCamLookAt.position; }
        }
        
        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Game Loop

        private void Reset()
        {
            targetObjects = new List<Transform>();
        }

        private void Start()
        {
            if (refCamLookAt) { defaultPosition = refCamLookAt.position; }
        }

        private void Update()
        {
            UpdateRefCamLookAt();
        }

        #endregion

        //----------------------------------------------------------------------------------------------------

#if UNITY_EDITOR

        #region Editor Gizmos

        protected override void OnDrawGizmosConditional(bool selected)
        {
            if (refCamLookAt)
            {
                for (int i = 0; i < targetObjects.Count; i++)
                {
                    Gizmos.color = trackedColor;
                    Gizmos.DrawLine(refCamLookAt.position, targetObjects[i].position);
                }
            
                Gizmos.color = defaultColor;
                Gizmos.DrawLine(refCamLookAt.position, defaultPosition);
            }
        }

        #endregion

#endif
        
        //----------------------------------------------------------------------------------------------------

        #region Private Methods
        
        private void UpdateRefCamLookAt()
        {
            // calculate center point of the targets
            targetPosition = GetBoundsCenter();

            // move refCamLookAt to target position
            if (refCamLookAt.position != targetPosition + new Vector3(0.01f, 0.01f, 0.01f))
            {
                // lerp refCamLookAt position
                float step = moveSpeed * Time.deltaTime;
                refCamLookAt.position = Vector3.MoveTowards(refCamLookAt.position, targetPosition, step);
            }
            
            // update cameraRig zoom
            if (zoomCamera && cameraRig)
            {
                float newZoom = Mathf.Clamp(GetGreatestDistance() + padding, maxZoom, minZoom);
                float step = zoomSpeed * 10f * Time.deltaTime;
                cameraRig.Lens.verticalFOV = Mathf.Lerp(cameraRig.Lens.verticalFOV, newZoom, step);
            }
        }

        private Bounds GetBounds()
        {
            Bounds bounds = new Bounds(targetObjects[0].position, Vector3.zero);
            for (int i = 0; i < targetObjects.Count; i++)
            {
                bounds.Encapsulate(targetObjects[i].position);
            }

            return bounds;
        }

        private Vector3 GetBoundsCenter()
        {
            if (0 < targetObjects.Count)
            {
                if (1 == targetObjects.Count)
                {
                    return targetObjects[0].position;
                }
                else
                {
                    return GetBounds().center;
                }
            }
            else
            {
                return defaultPosition;
            }
        }

        private float GetGreatestDistance()
        {
            if (1 < targetObjects.Count)
            {
                float X = 0;
                float Y = 0;
                float Z = 0;
                
                if (boundsX) { X = GetBounds().size.x; }
                if (boundsX) { Y = GetBounds().size.y; }
                if (boundsX) { Z = GetBounds().size.z; }

                float maxAxis = Mathf.Max(X, Y);
                maxAxis = Mathf.Max(maxAxis, Z);
                
                return maxAxis;
            }
            else
            {
                return 0;
            }
        }

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Public Methods

        /// <summary>
        /// Add a target transform to the list of target objects
        /// </summary>
        /// <param name="newTarget"></param>
        public void AddTargetToList(Transform newTarget)
        {
            if (targetObjects.Contains(newTarget)) { return; }
            targetObjects.Add(newTarget);
        }

        /// <summary>
        /// Remove a target transform from the list of target objects
        /// </summary>
        /// <param name="oldTarget"></param>
        public void RemoveTargetFromList(Transform oldTarget)
        {
            if (!targetObjects.Contains(oldTarget)) { return; }
            targetObjects.Remove(oldTarget);
        }

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Getter / Setter

        /// <summary>
        /// The reference transform that the cameraRig should look at.
        /// </summary>
        public Transform RefCamLookAt
        {
            get => refCamLookAt;
            set => refCamLookAt = value;
        }
        
        /// <summary>
        /// Toggle whether this Multi-TargetingRig should calculate the bounds on the x-axis.
        /// </summary>
        public bool BoundsX
        {
            get => boundsX;
            set => boundsX = value;
        }
        
        /// <summary>
        /// Toggle whether this Multi-TargetingRig should calculate the bounds on the y-axis.
        /// </summary>
        public bool BoundsY
        {
            get => boundsY;
            set => boundsY = value;
        }
        
        /// <summary>
        /// Toggle whether this Multi-TargetingRig should calculate the bounds on the z-axis.
        /// </summary>
        public bool BoundsZ
        {
            get => boundsZ;
            set => boundsZ = value;
        }

        #endregion

    } //class end
}

#endif
#endif