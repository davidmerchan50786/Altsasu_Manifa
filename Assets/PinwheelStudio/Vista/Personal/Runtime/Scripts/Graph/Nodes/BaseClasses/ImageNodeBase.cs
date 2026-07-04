#if VISTA
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [System.Serializable]
    /// <summary>
    /// Base class for nodes that produce or process image outputs and therefore support per-node resolution overrides.
    /// </summary>
    public abstract class ImageNodeBase : ExecutableNodeBase
    {
        [SerializeField]
        protected ResolutionOverrideOptions m_resolutionOverride;
        /// <summary>
        /// Selects whether this node derives its working resolution from the graph request, the main input, or an absolute value.
        /// </summary>
        public ResolutionOverrideOptions resolutionOverride
        {
            get
            {
                return m_resolutionOverride;
            }
            set
            {
                m_resolutionOverride = value;
            }
        }

        [SerializeField]
        protected float m_resolutionMultiplier;
        /// <summary>
        /// Multiplier applied when <see cref="resolutionOverride"/> uses a relative resolution mode.
        /// </summary>
        public float resolutionMultiplier
        {
            get
            {
                return m_resolutionMultiplier;
            }
            set
            {
                m_resolutionMultiplier = Mathf.Clamp(value, 0.1f, 2f);
            }
        }

        [SerializeField]
        protected int m_resolutionAbsolute;
        /// <summary>
        /// Absolute working resolution used when <see cref="resolutionOverride"/> is set to the absolute mode.
        /// </summary>
        public int resolutionAbsolute
        {
            get
            {
                return m_resolutionAbsolute;
            }
            set
            {
                m_resolutionAbsolute = Utilities.MultipleOf8(Mathf.Clamp(value, Constants.RES_MIN, Constants.RES_MAX));
            }
        }

        /// <summary>
        /// Initializes image-resolution settings to their default values.
        /// </summary>
        public ImageNodeBase() : base()
        {
            this.m_resolutionOverride = ResolutionOverrideOptions.RelativeToMainInput;
            this.m_resolutionMultiplier = 1;
            this.m_resolutionAbsolute = 1024;
        }
    }
}
#endif


