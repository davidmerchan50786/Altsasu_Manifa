#if GASKELLGAMES
#if GASKELLGAMES_INPUTEVENTSYSTEM
using System.Collections.Generic;
using UnityEngine;

namespace Gaskellgames.CameraSystem
{
    /// <remarks>
    /// Code created by Gaskellgames: https://gaskellgames.com
    /// </remarks>
    
    [AddComponentMenu("Gaskellgames/Camera System/Camera Shaker")]
    public class CameraShaker : GgMonoBehaviour
    {
        #region Variables
        
        [SerializeField, Range(0, 1)]
        [Tooltip("Max intensity level of the shake effect")]
        private float intensity = 0.5f;
        
        [SerializeField, Range(1, 100)]
        [Tooltip("Max distance objects can be affected by this CameraShaker")]
        private float range = 10;
    
        [SerializeField]
        [Tooltip("Range gizmo")]
        private Color32 gizmoColor = new Color32(000, 179, 223, 255);
        
        [SerializeField, LineSeparator, ReadOnly]
        [Tooltip("List will be updated on component enable and at the point of activation")]
        private List<CameraRig> shakableRigs;
        
        #endregion
    
        //----------------------------------------------------------------------------------------------------

        #region Game Loop

        private void OnEnable()
        {
            UpdateTargetList();
        }

        private void OnDisable()
        {
            shakableRigs.Clear();
        }
        
        #endregion
        
        //----------------------------------------------------------------------------------------------------
        
#if UNITY_EDITOR
        
        #region Gizmos [EditorOnly]

        protected override void OnDrawGizmosConditional(bool selected)
        {
            Matrix4x4 resetMatrix = Gizmos.matrix;
            Gizmos.matrix = gameObject.transform.localToWorldMatrix;
    
            Gizmos.color = gizmoColor;
            Gizmos.DrawLine(Vector3.zero, new Vector3(range, 0, 0));
            Gizmos.DrawLine(Vector3.zero, new Vector3(-range, 0, 0));
            Gizmos.DrawLine(Vector3.zero, new Vector3(0, range, 0));
            Gizmos.DrawLine(Vector3.zero, new Vector3(0, -range, 0));
            Gizmos.DrawLine(Vector3.zero, new Vector3(0, 0, range));
            Gizmos.DrawLine(Vector3.zero, new Vector3(0, 0, -range));
            Gizmos.DrawWireSphere(Vector3.zero, range);
    
            Gizmos.matrix = resetMatrix;
        }
    
        #endregion
        
#endif
    
        //----------------------------------------------------------------------------------------------------

        #region Private Methods
        
        private void UpdateTargetList()
        {
            shakableRigs = new List<CameraRig>(CameraList.GetShakableRigList());
        }

        private void ShakeTargets()
        {
            for(int i = 0; i < shakableRigs.Count; ++i)
            {
                float distance = Vector3.Distance(transform.position, shakableRigs[i].transform.position);
                
                if (distance <= range)
                {
                    float adjustedDistance = Mathf.Clamp01(distance / range);
                    float adjustedIntensity = (1 - Mathf.Pow(adjustedDistance, 2)) * intensity;
                    shakableRigs[i].ShakeCamera(adjustedIntensity);
                }
            }
        }
        
        #endregion
        
        //----------------------------------------------------------------------------------------------------

        #region Public Methods
        
        /// <summary>
        /// Activate camera shake effect on all targets within range
        /// </summary>
        public void Activate()
        {
            UpdateTargetList();
            ShakeTargets();
        }

        #endregion
        
    } // class end
}
#endif
#endif