#if GASKELLGAMES
#if GASKELLGAMES_INPUTEVENTSYSTEM
using UnityEngine;

namespace Gaskellgames.CameraSystem
{
    /// <remarks>
    /// Code created by Gaskellgames: https://gaskellgames.com
    /// </remarks>
    
    [AddComponentMenu("Gaskellgames/Camera System/Shadow Transform")]
    public class ShadowTransform : MonoBehaviour, IEditorUpdate
    {
        #region variables

        private enum UpdateType
        {
            Update,
            FixedUpdate
        }
        
        [SerializeField]
        [Tooltip("toggle whether follow, look at and rotate with should be updated in editor.")]
        private bool updateInEditor;
        
        [SerializeField]
        [Tooltip("GameObject position will be set equal to the reference Transform position. Can be constrained to only follow on a specific axis.")]
        private Transform follow;
        
        [SerializeField]
        [Tooltip("GameObject rotation will be set so that the forward rotation points towards the reference Transform.")]
        private Transform lookAt;
        
        [SerializeField]
        [Tooltip("GameObject rotation will be set equal to the reference Transform rotation. Can be constrained to only rotate on a specific axis.")]
        private Transform rotateWith;
        
        [SerializeField]
        [Tooltip("Select the update loop to be used for this shadow transform.")]
        private UpdateType updateType = UpdateType.Update;
        
        [SerializeField]
        [Tooltip("Toggle position constraint on/off for x axis")]
        private bool followX = true;
        
        [SerializeField]
        [Tooltip("Toggle position constraint on/off for y axis")]
        private bool followY = true;
        
        [SerializeField]
        [Tooltip("Toggle position constraint on/off for z axis")]
        private bool followZ = true;
        
        [SerializeField]
        [Tooltip("Toggle rotation constraint on/off for x axis")]
        private bool rotateWithX = true;
        
        [SerializeField]
        [Tooltip("Toggle rotation constraint on/off for y axis")]
        private bool rotateWithY = true;
        
        [SerializeField]
        [Tooltip("Toggle rotation constraint on/off for z axis")]
        private bool rotateWithZ = true;
        
        [SerializeField, Range(0, 1)]
        [Tooltip("The speed at which the rotation should occur (1 is instant)")]
        private float turnSpeed = 0.05f;
        
        private Quaternion rotationTarget;
        private Vector3 direction;

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Editor Loop

        public void EditorUpdate()
        {
            if (!(updateInEditor & !Application.isPlaying)) { return; }
            
            UpdatePosition();
            UpdateRotation();
        }

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Game Loop

        private void Update()
        {
            if (updateType != UpdateType.Update) { return; }
            UpdatePosition();
            UpdateRotation();
        }
        
        private void FixedUpdate()
        {
            if (updateType != UpdateType.FixedUpdate) { return; }
            UpdatePosition();
            UpdateRotation();
        }

        #endregion
        
        //----------------------------------------------------------------------------------------------------

        #region Private Methods

        private void UpdatePosition()
        {
            if (follow)
            {
                float newX = transform.position.x;
                float newY = transform.position.y;
                float newZ = transform.position.z;

                if (followX)
                {
                    newX = follow.position.x;
                }
                if (followY)
                {
                    newY = follow.position.y;
                }
                if (followZ)
                {
                    newZ = follow.position.z;
                }

                transform.position = new Vector3(newX, newY, newZ);
            }
        }
        
        private void UpdateRotation()
        {
            if (lookAt)
            {
                direction = (lookAt.position - transform.position).normalized;
                rotationTarget = Quaternion.LookRotation(direction);

                transform.rotation = Quaternion.Slerp(transform.rotation, rotationTarget, turnSpeed);
            }
            else if (rotateWith)
            {
                float newX = transform.eulerAngles.x;
                float newY = transform.eulerAngles.y;
                float newZ = transform.eulerAngles.z;

                if (rotateWithX)
                {
                    newX = rotateWith.eulerAngles.x;
                }
                if (rotateWithY)
                {
                    newY = rotateWith.eulerAngles.y;
                }
                if (rotateWithZ)
                {
                    newZ = rotateWith.eulerAngles.z;
                }
                
                rotationTarget = Quaternion.Euler(newX, newY, newZ);
                transform.rotation = Quaternion.Slerp(transform.rotation, rotationTarget, turnSpeed);
            }
        }

        #endregion
        
        //----------------------------------------------------------------------------------------------------

        #region Getter / Setter

        /// <summary>
        /// GameObject position will be set equal to the reference Transform position. Can be constrained to only follow on a specific axis.
        /// </summary>
        public Transform Follow
        {
            get => follow;
            set => follow = value;
        }

        /// <summary>
        /// Toggle position constraint on/off for x axis
        /// </summary>
        public bool FollowX
        {
            get => followX;
            set => followX = value;
        }

        /// <summary>
        /// Toggle position constraint on/off for y axis
        /// </summary>
        public bool FollowY
        {
            get => followY;
            set => followY = value;
        }

        /// <summary>
        /// Toggle position constraint on/off for z axis
        /// </summary>
        public bool FollowZ
        {
            get => followZ;
            set => followZ = value;
        }

        /// <summary>
        /// GameObject rotation will be set so that the forward rotation points towards the reference Transform.
        /// </summary>
        public Transform LookAt
        {
            get => lookAt;
            set => lookAt = value;
        }

        /// <summary>
        /// GameObject rotation will be set equal to the reference Transform rotation. Can be constrained to only rotate on a specific axis.
        /// </summary>
        public Transform RotateWith
        {
            get => rotateWith;
            set => rotateWith = value;
        }

        /// <summary>
        /// Toggle rotation constraint on/off for x axis
        /// </summary>
        public bool RotateWithX
        {
            get => rotateWithX;
            set => rotateWithX = value;
        }

        /// <summary>
        /// Toggle rotation constraint on/off for y axis
        /// </summary>
        public bool RotateWithY
        {
            get => rotateWithY;
            set => rotateWithY = value;
        }

        /// <summary>
        /// Toggle rotation constraint on/off for z axis
        /// </summary>
        public bool RotateWithZ
        {
            get => rotateWithZ;
            set => rotateWithZ = value;
        }

        #endregion
        
    } // class end
}

#endif
#endif