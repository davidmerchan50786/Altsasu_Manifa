#if GASKELLGAMES
#if GASKELLGAMES_INPUTEVENTSYSTEM
using System.Collections;
using UnityEngine;
using Gaskellgames.InputEventSystem;

namespace Gaskellgames.CameraSystem
{
    /// <remarks>
    /// Code created by Gaskellgames: https://gaskellgames.com
    /// </remarks>
    
    [AddComponentMenu("Gaskellgames/Camera System/Camera Rig")]
    public class CameraRig : GgMonoBehaviour, IEditorUpdate
    {
        #region Variables
        
        [SerializeField]
        [Tooltip("Registered cameraRigs are added to the global camera list when enabled and removed when disabled")]
        private bool registerCamera = true;
        
        [SerializeField]
        [Tooltip("Toggle whether this cameraRig is a free fly camera.")]
        private bool freeFlyCamera;
        
        [SerializeField]
        [Tooltip("Toggle whether this cameraRig has camera shake enabled.")]
        private bool cameraShake;
        
        [Title("General Rig")]
        [SerializeField]
        [Tooltip("If the freelookRig is assigned, then the cameraRig will ignore follow, lookAt, turnSpeed & followOffset")]
        private CameraFreelookRig freelookRig;
        
        [SerializeField, Space]
        [Tooltip("GameObject position will be set equal to the reference Transform position plus the followOffset. Can be constrained to only follow on a specific axis.")]
        private Transform follow;
        
        [SerializeField, Indent]
        [Tooltip("Offset amount that this cameraRig will be from the follow transform.")]
        private Vector3 followOffset;
        
        [SerializeField]
        [Tooltip("GameObject rotation will be set so that the forward rotation points towards the reference Transform.")]
        private Transform lookAt;
        
        [SerializeField, Range(0, 1)]
        [Tooltip("The speed at which the rotation should occur (1 is instant)")]
        private float turnSpeed = 1.0f;
        
        [Title("FreeFly Camera")]
        [SerializeField, Required]
        [Tooltip("GMK input controller component used for camera rotation input.")]
        private GMKInputController gmkInputController;
        
        [SerializeField, Range(0, 100)]
        [Tooltip("Speed this cameraRig will move when in free fly mode.")]
        private float moveSpeed = 10;
        
        [SerializeField, Range(0, 100)]
        [Tooltip("Speed this cameraRig will move when in free fly mode with boost enabled.")]
        private float boostSpeed = 25;
        
        [SerializeField, Range(0, 100)]
        [Tooltip("The input sensitivity for x-input.")]
        private int xSensitivity = 50;
        
        [SerializeField, Range(0, 100)]
        [Tooltip("The input sensitivity for y-input.")]
        private int ySensitivity = 50;
        
        [SerializeField]
        [Tooltip("Toggle whether this cameraRig can move with user input, can only be activated when this cameraRig is set to freeFlyCamera.")]
        private bool freeFlyActive;
        
        [Title("Camera Shake")]
        [SerializeField, Range(0, 1)]
        [Tooltip("Smoothing value effects the fade time of the shake effect")]
        private float shakeSmoothing = 1;
        
        [SerializeField, Range(0, 10)]
        [Tooltip("How much the camera can move when receiving the full intensity of an incoming camera shaker effect")]
        private float positionMagnitude = 7.5f;
        
        [SerializeField, Range(0, 10)]
        [Tooltip("How much the camera can rotate when receiving the full intensity of an incoming camera shaker effect")]
        private float rotationMagnitude = 5;
        
        [Title("Camera Lens")]
        [SerializeField]
        [Tooltip("The lens settings to be applied to a camera via a camera brain component.")]
        private CameraLens lens = new CameraLens();

        [SerializeField]
        [Tooltip("Debug color used to represent the frustum of the camera lens.")]
        private Color32 frustumColor = new Color32(128, 128, 128, 128);
        
        private GMKInputs gmkInputs;
        private float horizontalInput;
        private float verticalInput;
        private float heightInput;
        private float xRotation;
        private float yRotation;
        private bool isBoosting;
        private bool isRising;
        private bool isFalling;
        
        private Quaternion rotationTarget;
        private Vector3 direction;
        
        private GgPose shakeOffset;
        private GgPose currentShake;
        private GgPose previousShake;
        private float shakeMultiplier;
        
        private float lerpSpeedMultiplier = 40f;
        private float desiredCameraTilt;
        
        #endregion

        //----------------------------------------------------------------------------------------------------

        #region On Events

        private void OnEnable()
        {
            if (registerCamera) { CameraList.Register(this); }
            if (cameraShake) { CameraList.SetShakable(this); }
        }

        private void OnDisable()
        {
            CameraList.Unregister(this);
            CameraList.UnsetShakable(this);
        }

        private void OnDestroy()
        {
            CameraList.Unregister(this);
            CameraList.UnsetShakable(this);
        }
        
        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Editor Loop

        public void EditorUpdate()
        {
            if (freeFlyCamera) { return; }
            Update_Camera();
        }

        #endregion
        
        //----------------------------------------------------------------------------------------------------

        #region Game Loop
        
        private void Start()
        {
            shakeOffset = new GgPose();
            currentShake = new GgPose();
            previousShake = new GgPose();
        }

        private void Update()
        {
            if (freeFlyCamera)
            {
                if (!freeFlyActive) { return; }
                
                Update_FreeFlyUserInputs();
                Update_FreeFlyMovement();
            }
            else
            {
                Update_Camera();
                Update_CameraShake();
            }
        }

        #endregion

        //----------------------------------------------------------------------------------------------------

#if UNITY_EDITOR
        
        #region Gizmos [EditorOnly]

        protected override void OnDrawGizmosConditional(bool selected)
        {
            // draw camera frustum
            if (!lens.showFrustum) { return; }
            Matrix4x4 resetMatrix = Gizmos.matrix;
            Gizmos.matrix = gameObject.transform.localToWorldMatrix;
            Gizmos.color = frustumColor;

            float width = Screen.width * 1.000f;
            float height = Screen.height * 1.000f;
            float aspect = width / height;

            Gizmos.DrawFrustum(Vector3.zero, lens.verticalFOV, lens.farClipPlane, lens.nearClipPlane, aspect);
            Gizmos.matrix = resetMatrix;
        }

        #endregion
        
#endif

        //----------------------------------------------------------------------------------------------------

        #region Private Methods
        
        private void Update_Camera()
        {
            // update position
            if (freelookRig)
            {
                transform.position = freelookRig.GetFreelookCameraRigPosition();
            }
            else if (follow)
            {
                transform.position = follow.position + followOffset;
            }

            // update rotation
            bool updateRotation = false;
            if (lookAt)
            {
                direction = (lookAt.position - transform.position).normalized;
                updateRotation = true;
            }
            else if (freelookRig)
            {
                direction = (freelookRig.transform.position - transform.position).normalized;
                updateRotation = true;
            }
            
            if (updateRotation)
            {
                rotationTarget = direction.Equals(Vector3.zero) ? new Quaternion() : Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, rotationTarget, turnSpeed);
            }
        }

        private void Update_CameraShake()
        {
            if (cameraShake)
            {
                float adjustedSmoothing = 0.5f + (shakeSmoothing * 0.5f);
                float shakePower = Mathf.Pow(shakeMultiplier, adjustedSmoothing);
                
                if (shakePower > 0)
                {
                    // update previous
                    previousShake.position = currentShake.position;
                    previousShake.eulerAngles = currentShake.eulerAngles;
                    
                    // generate position values
                    float positionX = positionMagnitude * Random.Range(-0.2f, 0.2f);
                    float positionY = positionMagnitude * Random.Range(-0.2f, 0.2f);
                    float positionZ = positionMagnitude * Random.Range(-0.2f, 0.2f);
                    currentShake.position = GgMaths.RoundVector3(new Vector3(positionX, positionY, positionZ) * shakePower, 3);
                    
                    // generate angle values
                    float angleX = rotationMagnitude * Random.Range(-2.0f, 2.0f);
                    float angleY = rotationMagnitude * Random.Range(-2.0f, 2.0f);
                    float angleZ = rotationMagnitude * Random.Range(-2.0f, 2.0f);
                    currentShake.eulerAngles = GgMaths.RoundVector3(new Vector3(angleX, angleY, angleZ) * shakePower, 3);
                    
                    // add shake for this frame (currentShake) and counteract shake from previous frame (previousShake)
                    transform.position += currentShake.position - previousShake.position;
                    transform.eulerAngles += currentShake.eulerAngles - previousShake.eulerAngles;
                    shakeMultiplier = Mathf.Clamp01(shakeMultiplier - Time.deltaTime);
                }
                else if (!(shakeOffset.position == Vector3.zero && shakeOffset.eulerAngles == Vector3.zero))
                {
                    // counteract shake from previous frame (previousShake)
                    transform.position -= currentShake.position;
                    transform.eulerAngles -= currentShake.eulerAngles;
                    currentShake.position = Vector3.zero;
                    currentShake.eulerAngles = Vector3.zero;
                }
            }
        }
        
        private void Update_FreeFlyUserInputs()
        {
            if (!gmkInputController) { return; }
            
            // get user inputs
            gmkInputs = gmkInputController.Inputs;
                
            // handle movement
            horizontalInput = gmkInputs.leftStick.x;
            verticalInput = gmkInputs.leftStick.y;

            // handle look rotation
            float mouseX = gmkInputs.rightStick.x * Time.unscaledDeltaTime * xSensitivity * 2;
            float mouseY = gmkInputs.rightStick.y * Time.unscaledDeltaTime * ySensitivity * 2;
            yRotation += mouseX;
            xRotation -= mouseY;
            xRotation = Mathf.Clamp(xRotation, -90, 90);
                
            // handle speed up
            isBoosting = gmkInputs.leftShoulder.keypressed;
                
            // handle height
            isRising = gmkInputs.rightStickPress.keypressed;
            isFalling = gmkInputs.leftStickPress.keypressed;
        }
        
        private void Update_FreeFlyMovement()
        {
            // calculate movement input direction
            if (isRising && !isFalling)
            {
                heightInput = 1;
            }
            else if (isFalling && !isRising)
            {
                heightInput = -1;
            }
            else
            {
                heightInput = 0;
            }
            Vector3 inputDirection = (Vector3.forward * verticalInput) + (Vector3.right * horizontalInput) + (Vector3.up * heightInput);
            
            // calculate rotation
            transform.rotation = Quaternion.Euler(xRotation, yRotation, 0);
            
            // calculate acceleration
            float speed = isBoosting ? boostSpeed : moveSpeed;
            float acceleration = speed * Time.unscaledDeltaTime;
            
            // move object
            transform.Translate(inputDirection * acceleration);
        }

        private IEnumerator SmoothlyLerpCameraTilt()
        {
            // smoothly lerp moveSpeed to moveState speed
            float time = 0;
            float difference = Mathf.Abs(desiredCameraTilt - lens.tilt);
            float startValue = lens.tilt;

            while (time < difference)
            {
                lens.tilt = Mathf.Lerp(startValue, desiredCameraTilt, time / difference);
                time += Time.deltaTime * lerpSpeedMultiplier;
                yield return null;
            }

            lens.tilt = desiredCameraTilt;
        }

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Public Methods
        
        /// <summary>
        /// Activate the camera shake for this cameraRig
        /// </summary>
        /// <param name="intensity"></param>
        public void ShakeCamera(float intensity)
        {
            shakeMultiplier = Mathf.Clamp01(shakeMultiplier + intensity);
        }
        
        /// <summary>
        /// Update tilt value for this cameraRig
        /// </summary>
        /// <param name="newCameraTilt"></param>
        public void TiltCamera(float newCameraTilt)
        {
            StopAllCoroutines();
            desiredCameraTilt = newCameraTilt;
            StartCoroutine(SmoothlyLerpCameraTilt());
        }

        #endregion
        
        //----------------------------------------------------------------------------------------------------

        #region Getters / Setters

        /// <summary>
        /// Get the CameraRig rotation including the rotational offset used to tilt the cameraRig
        /// </summary>
        public Quaternion TiltedRotation => transform.rotation * Quaternion.Euler(new Vector3(0, 0, lens.tilt));

        /// <summary>
        /// True if this cameraRig is a free fly camera, with free fly currently active, false otherwise.
        /// </summary>
        public bool IsFreeFlyActive
        {
            get => IsFreeFlyCamera && freeFlyActive;
            set => freeFlyActive = value;
        }

        // True if this cameraRig is a free fly camera, false otherwise.
        public bool IsFreeFlyCamera
        {
            get => freeFlyCamera;
            set => freeFlyCamera = value;
        }

        /// <summary>
        /// Get/Set the GMKInputController used for free fly camera user inputs
        /// </summary>
        public GMKInputController GMKInputController
        {
            get => gmkInputController;
            set => gmkInputController = value;
        }

        /// <summary>
        /// Get/Set the reference to a freelook camera rig.
        /// </summary>
        public CameraFreelookRig FreelookRig
        {
            get => freelookRig;
            set => freelookRig = value;
        }

        /// <summary>
        /// Get/Set the reference to a CameraFollow transform.
        /// </summary>
        public Transform CameraFollow
        {
            get => follow;
            set => follow = value;
        }

        /// <summary>
        /// Get/Set the reference to a LookAt transform.
        /// </summary>
        public Transform LookAt
        {
            get => lookAt;
            set => lookAt = value;
        }

        /// <summary>
        /// Get/Set the lens settings.
        /// </summary>
        public CameraLens Lens
        {
            get => lens;
            set => lens = value;
        }
        
        /// <summary>
        /// Offset amount that this cameraRig will be from the follow transform.
        /// </summary>
        public Vector3 FollowOffset
        {
            get => followOffset;
            set => followOffset = value;
        }

        #endregion
        
    } //class end
}

#endif
#endif