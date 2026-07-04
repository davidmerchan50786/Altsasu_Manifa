#if GASKELLGAMES
#if GASKELLGAMES_INPUTEVENTSYSTEM
using UnityEngine;

namespace Gaskellgames.CameraSystem
{
    /// <remarks>
    /// Code created by Gaskellgames: https://gaskellgames.com
    /// </remarks>
    
    [AddComponentMenu("Gaskellgames/Camera System/Camera Brain")]
    public class CameraBrain : GgMonoBehaviour, IEditorUpdate
    {
        #region Variables

        public enum CameraBlendStyle
        {
            Cut,
            FadeToColor,
            MoveToPosition
        }

        [SerializeField, Required]
        [Tooltip("The currently active cameraRig driving settings on this CameraBrain.")]
        private CameraRig activeCamera;
        
        [SerializeField, ReadOnly]
        [Tooltip("The previously active cameraRig driving settings on this CameraBrain.")]
        private CameraRig previousCamera;
        
        [SerializeField]
        [Tooltip("The blend used when switching between active camera rigs.")]
        private CameraBlendStyle blendingStyle = CameraBlendStyle.Cut;
        
        [SerializeField, CustomCurve(000, 179, 223, 255)]
        [Tooltip("The fadeCurve is used to set the fade color based on the current speed, when blendingStyle is set to FadeToColor.")]
        private AnimationCurve fadeCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(0.5f, 0.5f, -1.5f, -1.5f), new Keyframe(1, 0));
        
        [SerializeField]
        [Tooltip("The color to fade to when the blendingStyle is set to FadeToColor.")]
        private Color fadeColor = Color.black;
        
        [SerializeField, Range(0.01f, 3.0f)]
        [Tooltip("The speed used to blend to/from the fadeColor when blendingStyle is set to FadeToColor. ")]
        private float fadeSpeed = 1.5f;
        
        [SerializeField]
        [Tooltip("Toggle whether to fade the full screen to a single color, or to fade in/out a specific canvas group.")]
        private bool fadeFullScreen = true;
        
        [SerializeField]
        [Tooltip("The canvas group to fade in/out when blendingStyle is set to FadeToColor.")]
        private CanvasGroup canvasGroup;
        
        [SerializeField, CustomCurve(000, 179, 223, 255)]
        [Tooltip("The blendCurve is used to set the fade color based on the current speed, when blendingStyle is set to MoveToPosition.")]
        private AnimationCurve blendCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.5f, 0.5f, 1.5f, 1.5f), new Keyframe(1, 1));
        
        [SerializeField, Range(0.01f, 3.0f)]
        [Tooltip("The speed use to move between the previous cameraRig position and the current cameraRig position, when blendingStyle is set to MoveToPosition.")]
        private float blendSpeed = 1.5f;
        
        [SerializeField, ReadOnly]
        [Tooltip("The current active cameraRig's reference to a follow transform.")]
        private Transform follow;
        
        [SerializeField, ReadOnly]
        [Tooltip("The current active cameraRig's reference to a lookAt transform.")]
        private Transform lookAt;
        
        [SerializeField, ReadOnly]
        [Tooltip("The current active cameraRig's lens settings.")]
        private CameraLens lens;
        
        [SerializeField, ReadOnly]
        [Tooltip("The current active cameraRig's CameraOrbits.")]
        private CameraOrbits cameraOrbit;
        
        private CameraRig activeCameraCheck;
        private Camera cam;
        private Transform blendTransform;
        private CameraLens blendLens;
        private bool isBlending = false;
        private Texture2D texture;
        private bool triggerFade = false;
        private float alpha = 0f;
        private float timer = 0f;
        private int fadeDirection = 0;

        #endregion

        //----------------------------------------------------------------------------------------------------
        
        #region Editor Loop

        public void EditorUpdate()
        {
            if (Application.isPlaying || activeCamera == null) { return; }
            UpdateCameraTransform();
        }

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Game Loop

        private void Reset()
        {
            if (gameObject.GetComponent<Camera>() == null) { return; }
            cam = GetComponent<Camera>();
            activeCameraCheck = null;
        }

        private void Start()
        {
            InitializeVariables();
        }

        private void Update()
        {
            if (!activeCamera) { return; }
            if (!activeCamera.IsFreeFlyCamera) { return; }

            UpdateCameraTransform();
        }

        private void LateUpdate()
        {
            if (!isBlending)
            {
                UpdateCamera();    
            }
            else
            {
                BlendCamera();
            }
            
            if (triggerFade)
            {
                triggerFade = false;
                UpdateFade();
            }
        }

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region OnGUI

        public void OnGUI()
        {
            if (blendingStyle != CameraBlendStyle.FadeToColor) { return; }
            
            // draw texture to screen
            if (0f < alpha && fadeFullScreen)
            {
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), texture);
            }

            if (fadeDirection == 0) { return; }
            
            // set alpha based on timer value
            timer += fadeDirection * Time.deltaTime * fadeSpeed;
            alpha = fadeCurve.Evaluate(timer);

            // apply alpha to screen texture
            if (!fadeFullScreen && canvasGroup != null)
            {
                canvasGroup.alpha = 1 - alpha;
            }
            texture.SetPixel(0, 0, new Color(fadeColor.r, fadeColor.g, fadeColor.b, alpha));
            texture.Apply();

            // clamp alpha
            if (alpha <= 0f || alpha >= 1f)
            {
                fadeDirection = 0;
            }
        }

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Private Methods

        private void InitializeVariables()
        {
            cam = GetComponent<Camera>();
            alpha = 0f;
            texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, new Color(fadeColor.r, fadeColor.g, fadeColor.b, alpha));
            texture.Apply();

            if (activeCamera != null)
            {
                blendTransform = activeCamera.transform;
                blendLens = activeCamera.Lens;
                previousCamera = activeCamera;
            }
        }

        private void UpdateCamera()
        {
            if (!activeCamera) { return; }
            
            if (activeCameraCheck != activeCamera)
            {
                if (blendingStyle == CameraBlendStyle.MoveToPosition)
                {
                    if (!cam) { cam = GetComponent<Camera>(); }
                    if (!cam) { return; }
                    
                    timer = 0;
                    blendTransform.position = transform.position;
                    blendTransform.rotation = transform.rotation;
                    blendLens.verticalFOV = (int)cam.fieldOfView;
                    blendLens.nearClipPlane = cam.nearClipPlane;
                    blendLens.farClipPlane = cam.farClipPlane;
                    cam.cullingMask = activeCamera.Lens.cullingMask;
                    isBlending = true;
                }
                else if (blendingStyle == CameraBlendStyle.FadeToColor)
                {
                    if (IsScreenClear && previousCamera != null)
                    {
                        // fade to 'black'
                        triggerFade = true;
                    }
                    else if (IsScreenFaded) 
                    {
                        // unfade from 'black' after updating camera
                        activeCameraCheck = activeCamera;
                        UpdateCameraTransform();
                        
                        triggerFade = true;
                    }
                }
                else // blendingStyle == cameraBlendStyle.Cut
                {
                    activeCameraCheck = activeCamera;
                    UpdateCameraTransform();
                }
            }
            else
            {
                UpdateCameraTransform();
            }
        }

        private void UpdateCameraTransform()
        {
            if (!cam) { cam = GetComponent<Camera>(); }
            if (!cam || !activeCamera) { return; }
            
            // update debug values
            follow = activeCamera.CameraFollow;
            lookAt = activeCamera.LookAt;
            lens = activeCamera.Lens;
            if (activeCamera.FreelookRig) { cameraOrbit = activeCamera.FreelookRig.CameraOrbit; }
            
            // update camera lens
            cam.fieldOfView =  lens.verticalFOV;
            cam.nearClipPlane =  lens.nearClipPlane;
            cam.farClipPlane =  lens.farClipPlane;
            cam.cullingMask =  lens.cullingMask;
            
            // update camera transform to match active cameraRig
            transform.SetPositionAndRotation(activeCamera.transform.position, activeCamera.TiltedRotation);
        }

        private void BlendCamera()
        {
            // evaluate blend completion
            float completion = blendCurve.Evaluate(timer);
            timer += (blendSpeed * Time.deltaTime);
            
            // update position
            transform.position = Vector3.Lerp(blendTransform.position, activeCamera.transform.position, completion);

            // update rotation
            transform.rotation = Quaternion.Lerp(blendTransform.rotation, activeCamera.transform.rotation, completion);
            
            // update CameraLens
            CameraLens activeCameraLens = activeCamera.Lens;
            cam.fieldOfView =  Mathf.Lerp(blendLens.verticalFOV, activeCameraLens.verticalFOV, completion);
            cam.nearClipPlane =  Mathf.Lerp(blendLens.nearClipPlane, activeCameraLens.nearClipPlane, completion);
            cam.farClipPlane =  Mathf.Lerp(blendLens.farClipPlane, activeCameraLens.farClipPlane, completion);
            
            if (1.0f <= completion)
            {
                isBlending = false;
                blendTransform = activeCamera.transform;
                blendLens = activeCamera.Lens;
                activeCameraCheck = activeCamera;
            }
        }

        private void UpdateFade()
        {
            if (fadeDirection == 0)
            {
                if (alpha >= 1f) // Fully faded out
                {
                    alpha = 1f;
                    timer = 0f;
                    fadeDirection = 1;
                }
                else // Fully faded in
                {
                    alpha = 0f;
                    timer = 1f;
                    fadeDirection = -1;
                }
            }
        }

        #endregion

        //----------------------------------------------------------------------------------------------------

        #region Getters / Setters

        /// <summary>
        /// The blend used when switching between active camera rigs.
        /// </summary>
        public CameraBlendStyle BlendingStyle => blendingStyle;

        /// <summary>
        /// Returns true if the screen is fully faded to color.
        /// </summary>
        public bool IsScreenFaded => alpha >= 1;

        /// <summary>
        /// Returns true if the screen is fully faded to clear.
        /// </summary>
        public bool IsScreenClear => alpha <= 0;
        
        /// <summary>
        /// The currently active cameraRig driving settings on this CameraBrain.
        /// </summary>
        public CameraRig ActiveCamera
        {
            get => activeCamera;
            set
            {
                if (activeCamera == value) { return; }
                
                previousCamera = activeCamera;
                activeCamera = value;
            }
        }

        /// <summary>
        /// The previously active cameraRig driving settings on this CameraBrain.
        /// </summary>
        public CameraRig PreviousCamera => previousCamera;

        /// <summary>
        /// The canvas group to fade in/out when blendingStyle is set to FadeToColor.
        /// </summary>
        public CanvasGroup CanvasGroup
        {
            get => canvasGroup;
            set => canvasGroup = value;
        }

        #endregion

    } // class end
}

#endif
#endif