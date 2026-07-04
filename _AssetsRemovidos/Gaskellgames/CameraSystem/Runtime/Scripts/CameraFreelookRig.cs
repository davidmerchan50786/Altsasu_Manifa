#if GASKELLGAMES
#if GASKELLGAMES_INPUTEVENTSYSTEM
using UnityEngine;
using UnityEngine.InputSystem;
using Gaskellgames.InputEventSystem;

namespace Gaskellgames.CameraSystem
{
    /// <remarks>
    /// Code created by Gaskellgames: https://gaskellgames.com
    /// </remarks>
    
    [AddComponentMenu("Gaskellgames/Camera System/Camera Freelook Rig")]
    public class CameraFreelookRig : GgMonoBehaviour, IEditorUpdate
    {
        #region Variables
        
        [SerializeField]
        [Tooltip("Toggle whether the resultant camera rig position takes the local rotation of this gameObject into account.")]
        private bool relativePosition;
        
        [SerializeField]
        [Tooltip("Toggle whether the resultant camera rig position should be effected by collisions.")]
        private bool cameraCollisions;
        
        [SerializeField]
        [Tooltip("Toggle whether to use a custom input action for camera rotation input, or a GMK input controller component.")]
        private bool customInputAction;
        
        [SerializeField, Required]
        [Tooltip("Custom input action used for camera rotation input.")]
        private InputActionReference moveCamera;
        
        [SerializeField, Required]
        [Tooltip("GMK input controller component used for camera rotation input.")]
        private GMKInputController gmkInputController;
        
        [SerializeField]
        [Tooltip("The size and shape of the camera's orbits.")]
        private CameraOrbits cameraOrbit;

        [SerializeField, Range(0, 100)]
        [Tooltip("The input sensitivity for x-input.")]
        private int xSensitivity = 80;
        
        [SerializeField, Range(0, 100)]
        [Tooltip("The input sensitivity for y-input.")]
        private int ySensitivity = 80;
        
        [SerializeField, Range(-180, 180)]
        [Tooltip("Add an offset to the default rotation. (Useful if your model doesn't have z-axis as forward vector)")]
        private float rotationOffset = 0f;
        
        [SerializeField, Range(0, 1)]
        [Tooltip("The distance the camera should be offset from a collision to avoid clipping.")]
        private float collisionOffset = 0.2f;
        
        [SerializeField]
        [Tooltip("The layers this freelook rig can collide with when calculating the camera rig position.")]
        private LayerMask collisionLayers = default;

        [SerializeField]
        [Tooltip("Gizmo color used to show the orbit extents.")]
        private Color orbitExtentsColor = InspectorExtensions.cyanColor;

        [SerializeField]
        [Tooltip("Gizmo color used to show the current orbit.")]
        private Color currentOrbitColor = InspectorExtensions.yellowColor;

        [SerializeField]
        [Tooltip("Gizmo color used to show the orbit collision.")]
        private Color collisionColor = InspectorExtensions.redColor;
        
        private GMKInputs gmkInputs;
        private float xRotation;
        private float yRotation;
        private float xClampTop;
        private float xClampBottom;
        private Vector3 cameraRigOffsetTarget;
        private Vector3 cameraRigOffset;
        private bool collisionDetected;

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Editor Loop [IEditorUpdate]
        
        public void EditorUpdate()
        {
            Update_CameraRigOffsetTarget();
            Update_CameraRigOffset();
        }

        #endregion

        //----------------------------------------------------------------------------------------------------

#if UNITY_EDITOR

        #region OnValidate [EditorOnly]
        
        private void Reset()
        {
            CameraOrbit = new CameraOrbits();
            CameraRig newCameraRig = GetComponentInChildren<CameraRig>();
            GameObject newFreelookRig;

            if (!newCameraRig)
            {
                newFreelookRig = new GameObject("CameraRig");
                newFreelookRig.transform.parent = gameObject.transform;
                newFreelookRig.transform.localPosition = Vector3.zero;
                CameraRig cameraRig = newFreelookRig.AddComponent<CameraRig>();
                cameraRig.FreelookRig = this;
            }
            else
            {
                newCameraRig.FreelookRig = this;
            }
        }
        
        #endregion
        
        #region Gizmos [EditorOnly]

        protected override void OnDrawGizmosConditional(bool selected)
        {
            DrawGizmos_OrbitExtents();
            DrawGizmos_CurrentOffset();
        }

        private void DrawGizmos_OrbitExtents()
        {
            Gizmos.color = orbitExtentsColor;
            
            // cache values
            Vector3 origin = transform.position + (cameraOrbit.rigOffset * transform.up);
            Vector3 transformUp = relativePosition ? transform.up : Vector3.up;
            Vector3 transformForward = relativePosition ? transform.forward : Vector3.forward;
            
            // draw orbits
            Vector3 originTop = origin + (cameraOrbit.height.up * transformUp);
            Vector3 originBottom = origin + (cameraOrbit.height.down * transformUp);
            GizmosExtensions.DrawCircle(originTop, transformUp, cameraOrbit.radius.top);
            GizmosExtensions.DrawCircle(origin, transformUp, cameraOrbit.radius.middle);
            GizmosExtensions.DrawCircle(originBottom, transformUp, cameraOrbit.radius.bottom);
            
            // draw lines connecting orbits
            Vector3 pointTop = originTop - (cameraOrbit.radius.top * transformForward);
            Vector3 pointMiddle = origin - (cameraOrbit.radius.middle * transformForward);
            Vector3 pointBottom = originBottom - (cameraOrbit.radius.bottom * transformForward);
            Gizmos.DrawLine(pointMiddle, pointTop);
            Gizmos.DrawLine(pointMiddle, pointBottom);
            
            // draw offset position
            Gizmos.DrawLine(origin, transform.position);
        }

        private void DrawGizmos_CurrentOffset()
        {
            // cache relative values
            Vector3 origin = transform.position + (cameraOrbit.rigOffset * transform.up);
            Vector3 localOffsetDirection = cameraRigOffsetTarget.normalized;
            Vector3 globalOffsetDirection = transform.TransformDirection(cameraRigOffsetTarget).normalized;
            Vector3 rayDirection = relativePosition ? globalOffsetDirection : localOffsetDirection;
            Vector3 targetPoint = origin + (rayDirection * cameraRigOffsetTarget.magnitude);
            Vector3 currentPoint = origin + (rayDirection * cameraRigOffset.magnitude);
            Vector3 transformUp = relativePosition ? transform.up : Vector3.up;
            Vector3 transformForward = relativePosition ? transform.forward : Vector3.forward;
            Vector3 transformRight = relativePosition ? transform.right : Vector3.right;
            float radius = ((cameraRigOffsetTarget.x * transformForward) + (cameraRigOffsetTarget.z * transformRight)).magnitude;
            Vector3 horizontalTarget = new Vector3(cameraRigOffsetTarget.x, 0, cameraRigOffsetTarget.z);
            Vector3 localHorizontalDirection = horizontalTarget.normalized;
            Vector3 globalHorizontalDirection = transform.TransformDirection(horizontalTarget).normalized;
            Vector3 horizontalDirection = relativePosition ? globalHorizontalDirection : localHorizontalDirection;
            Vector3 horizontalPoint = origin + (horizontalDirection * radius);
            
            // draw link lines
            Gizmos.color = collisionDetected ? collisionColor : currentOrbitColor;
            Gizmos.DrawLine(origin, horizontalPoint);
            Gizmos.DrawLine(horizontalPoint, targetPoint);
            
            // draw cameraRigPosition offset collision lines
            if (collisionDetected)
            {
                Gizmos.color = collisionColor;
                Gizmos.DrawLine(currentPoint, targetPoint);
            }
            Gizmos.color = currentOrbitColor;
            Gizmos.DrawLine(origin, currentPoint);
            
            // draw current orbit
            Gizmos.color = collisionDetected ? collisionColor : currentOrbitColor;
            GizmosExtensions.DrawCircle(origin + (cameraRigOffsetTarget.y * transformUp), transformUp, radius);
        }

        #endregion

#endif
        
        //----------------------------------------------------------------------------------------------------

        #region Game Loop

        private void OnEnable()
        {
            if (moveCamera) { moveCamera.action.Enable(); }
        }

        private void OnDisable()
        {
            if (moveCamera) { moveCamera.action.Disable(); }
        }

        private void Update()
        {
            Update_UserInput();
            Update_CameraRigOffsetTarget();
            Update_CameraRigOffset();
        }

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Private Methods

        private void Update_UserInput()
        {
            // get mouse input
            float mouseX;
            float mouseY;
            if (customInputAction)
            {
                mouseX = moveCamera.action.ReadValue<Vector2>().x * Time.deltaTime * xSensitivity * 2;
                mouseY = moveCamera.action.ReadValue<Vector2>().y * Time.deltaTime * ySensitivity * 2;
            }
            else
            {
                if (gmkInputController) { gmkInputs = gmkInputController.Inputs; }
                mouseX = gmkInputs.rightStick.x * Time.deltaTime * xSensitivity * 2;
                mouseY = gmkInputs.rightStick.y * Time.deltaTime * ySensitivity * 2;
            }

            // set rotation & maximum up/down rotation
            yRotation += mouseX;
            xRotation -= mouseY;
            xRotation = Mathf.Clamp(xRotation, xClampBottom, xClampTop);
        }
        
        private void Update_CameraRigOffsetTarget()
        {
            // calculate input rotation min-max clamps
            float angleTop = CalculateAngleOnRightAngleTriangle(Mathf.Abs(cameraOrbit.radius.top), Mathf.Abs(cameraOrbit.height.up));
            float angleBottom = CalculateAngleOnRightAngleTriangle(Mathf.Abs(cameraOrbit.radius.bottom), Mathf.Abs(cameraOrbit.height.down));
            xClampTop = (90.0f - angleTop) * Mathf.Sign(cameraOrbit.height.up);
            xClampBottom = (90.0f - angleBottom) * Mathf.Sign(cameraOrbit.height.down);
            
            // calculate camera orbit radius for current input rotation
            float radius;
            if (0 < xRotation)
            {
                float ratio = xRotation / xClampTop;
                float magnitudeTop = Vector3.Magnitude(new Vector2(cameraOrbit.radius.top, cameraOrbit.height.up));
                radius = (magnitudeTop * ratio) + (cameraOrbit.radius.middle * (1.0f - ratio));
            }
            else if (0 > xRotation)
            {
                float ratio = xRotation / xClampBottom;
                float magnitudeBottom = Vector3.Magnitude(new Vector2(cameraOrbit.radius.bottom, cameraOrbit.height.down));
                radius = (cameraOrbit.radius.middle * (1.0f - ratio)) + (magnitudeBottom * ratio);
            }
            else
            {
                radius = cameraOrbit.radius.middle;
            }
            
            // project input rotation to spherical orbit
            float longitude = (rotationOffset - yRotation - 90) * Mathf.Deg2Rad;
            float latitude = xRotation * Mathf.Deg2Rad;
            float equatorX = Mathf.Cos(longitude);
            float equatorZ = Mathf.Sin(longitude);
            float multiplier = Mathf.Cos(latitude);
            float x = multiplier * equatorX;
            float y = Mathf.Sin(latitude);
            float z = multiplier * equatorZ;
            
            // map camera orbit radius to spherical orbit position
            cameraRigOffsetTarget = new Vector3(x, y, z) * radius;
        }

        private void Update_CameraRigOffset()
        {
            if (cameraCollisions)
            {
                Vector3 origin = transform.position + (cameraOrbit.rigOffset * transform.up);
                Vector3 localDirection = cameraRigOffsetTarget.normalized;
                Vector3 globalDirection = transform.TransformDirection(cameraRigOffsetTarget).normalized;
                Vector3 rayDirection = relativePosition ? globalDirection : localDirection;
                float maxDistance = cameraRigOffsetTarget.magnitude;

                float radius = collisionOffset;
                Ray ray = new Ray(origin, rayDirection);
                if (Physics.SphereCast(ray, radius, out RaycastHit hit, maxDistance + 1, collisionLayers))
                {
                    if (hit.distance < maxDistance)
                    {
                        cameraRigOffset = localDirection * hit.distance;
                        collisionDetected = true;
                    }
                    else
                    {
                        cameraRigOffset = cameraRigOffsetTarget;
                        collisionDetected = false;
                    }
                }
                else
                {
                    cameraRigOffset = cameraRigOffsetTarget;
                    collisionDetected = false;
                }
            }
            else
            {
                cameraRigOffset = cameraRigOffsetTarget;
                collisionDetected = false;
            }
        }
        
        private float CalculateAngleOnRightAngleTriangle(float opposite, float adjacent)
        {
            float angle = Mathf.Atan2(opposite, adjacent);
            angle *= Mathf.Rad2Deg;

            return angle;
        }

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Getters / Setters

        /// <summary>
        /// Get the resultant position of this freelook rig's CameraRig.
        /// </summary>
        /// <returns></returns>
        public Vector3 GetFreelookCameraRigPosition()
        {
            Vector3 relativePosition = cameraRigOffset + (cameraOrbit.rigOffset * transform.up);
            return this.relativePosition ? GgMaths.TransformPoint(relativePosition, transform) : transform.position + relativePosition;
        }

        /// <summary>
        /// Get/Set the size and shape of the camera's orbits.
        /// </summary>
        public CameraOrbits CameraOrbit
        {
            get => cameraOrbit;
            set => cameraOrbit = value;
        }

        /// <summary>
        /// Get/Set the sensitivity used for calculating the user inputs.
        /// </summary>
        public Vector2 Sensitivity
        {
            get => new Vector2(xSensitivity, ySensitivity);
            set
            {
                xSensitivity = (int)value.x;
                ySensitivity = (int)value.y;
            }
        }

        #endregion
        
    } //class end
}

#endif
#endif