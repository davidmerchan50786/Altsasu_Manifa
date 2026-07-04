#if VISTA
using Pinwheel.Vista.Graphics;
using System.Collections.Generic;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "Geometry Mask",
        path = "Masking/Geometry Mask",
        icon = "",
        documentation = "",
        keywords = "height, slope, direction",
        description = "Highlight the area which satisfies some geometry rules.")]
    public class GeometryMaskNode : ImageNodeBase
    {
        public enum BlendMode
        {
            Multiply, Max
        }

        public readonly MaskSlot inputSlot = new MaskSlot("Height", SlotDirection.Input, 0);
        public readonly MaskSlot outputSlot = new MaskSlot("Output", SlotDirection.Output, 100);

        [SerializeField]
        private BlendMode m_blendMode;
        public BlendMode blendMode
        {
            get
            {
                return m_blendMode;
            }
            set
            {
                m_blendMode = value;
            }
        }

        [SerializeField]
        private bool m_enableHeightMask;
        public bool enableHeightMask
        {
            get
            {
                return m_enableHeightMask;
            }
            set
            {
                m_enableHeightMask = value;
            }
        }

        [SerializeField]
        private float m_minHeight;
        public float minHeight
        {
            get
            {
                return m_minHeight;
            }
            set
            {
                m_minHeight = Mathf.Max(0, Mathf.Min(value, m_maxHeight));
            }
        }

        [SerializeField]
        private float m_maxHeight;
        public float maxHeight
        {
            get
            {
                return m_maxHeight;
            }
            set
            {
                m_maxHeight = Mathf.Max(0, Mathf.Max(value, m_minHeight));
            }
        }

        [SerializeField]
        private AnimationCurve m_heightTransition;
        public AnimationCurve heightTransition
        {
            get
            {
                return m_heightTransition;
            }
            set
            {
                m_heightTransition = value;
            }
        }

        [SerializeField]
        private bool m_enableSlopeMask;
        public bool enableSlopeMask
        {
            get
            {
                return m_enableSlopeMask;
            }
            set
            {
                m_enableSlopeMask = value;
            }
        }

        [SerializeField]
        private float m_minAngle;
        public float minAngle
        {
            get
            {
                return m_minAngle;
            }
            set
            {
                m_minAngle = Mathf.Clamp(Mathf.Min(value, m_maxAngle), 0, 90);
            }
        }

        [SerializeField]
        private float m_maxAngle;
        public float maxAngle
        {
            get
            {
                return m_maxAngle;
            }
            set
            {
                m_maxAngle = Mathf.Clamp(Mathf.Max(value, m_minAngle), 0, 90);
            }
        }

        [SerializeField]
        private AnimationCurve m_slopeTransition;
        public AnimationCurve slopeTransition
        {
            get
            {
                return m_slopeTransition;
            }
            set
            {
                m_slopeTransition = value;
            }
        }

        [SerializeField]
        private bool m_enableDirectionMask;
        public bool enableDirectionMask
        {
            get
            {
                return m_enableDirectionMask;
            }
            set
            {
                m_enableDirectionMask = value;
            }
        }

        [SerializeField]
        private float m_direction;
        public float direction
        {
            get
            {
                return m_direction;
            }
            set
            {
                m_direction = Mathf.Clamp(value, 0, 360);
            }
        }

        [SerializeField]
        private float m_directionTolerance;
        public float directionTolerance
        {
            get
            {
                return m_directionTolerance;
            }
            set
            {
                m_directionTolerance = Mathf.Clamp(value, 0, 180);
            }
        }

        [SerializeField]
        private AnimationCurve m_directionFalloff;
        public AnimationCurve directionFalloff
        {
            get
            {
                return m_directionFalloff;
            }
            set
            {
                m_directionFalloff = value;
            }
        }

        private static readonly string SHADER_NAME = "Hidden/Vista/Graph/GeometryMask";
        private static readonly int MAIN_TEX = Shader.PropertyToID("_MainTex");
        private static readonly int TERRAIN_SIZE = Shader.PropertyToID("_TerrainSize");

        private static readonly int MIN_HEIGHT = Shader.PropertyToID("_MinHeight");
        private static readonly int MAX_HEIGHT = Shader.PropertyToID("_MaxHeight");
        private static readonly int HEIGHT_TRANSITION = Shader.PropertyToID("_HeightTransition");

        private static readonly int MIN_ANGLE = Shader.PropertyToID("_MinAngle");
        private static readonly int MAX_ANGLE = Shader.PropertyToID("_MaxAngle");
        private static readonly int SLOPE_TRANSITION = Shader.PropertyToID("_SlopeTransition");

        private static readonly int DIRECTION = Shader.PropertyToID("_DirectionAngle");
        private static readonly int DIRECTION_TOLERANCE = Shader.PropertyToID("_DirectionTolerance");
        private static readonly int DIRECTION_FALLOFF = Shader.PropertyToID("_DirectionFalloff");

        private static readonly int USE_HEIGHT_MASK = Shader.PropertyToID("_UseHeightMask");
        private static readonly int USE_SLOPE_MASK = Shader.PropertyToID("_UseSlopeMask");
        private static readonly int USE_DIRECTION_MASK = Shader.PropertyToID("_UseDirectionMask");
        private static readonly int BLEND_MAX = Shader.PropertyToID("_BlendMax");

        private static readonly int PASS = 0;
        private Material m_material;

        public GeometryMaskNode() : base()
        {
            m_blendMode = BlendMode.Multiply;

            m_enableHeightMask = false;
            m_minHeight = 0;
            m_maxHeight = 500;
            m_heightTransition = Utilities.EaseInOutCurve();

            m_enableSlopeMask = false;
            m_minAngle = 0;
            m_maxAngle = 90;
            m_slopeTransition = Utilities.EaseInOutCurve();

            m_enableDirectionMask = false;
            m_direction = 0;
            m_directionTolerance = 180;
            m_directionFalloff = AnimationCurve.EaseInOut(0, 0, 1, 1);
        }

        public override void ExecuteImmediate(GraphContext context)
        {
            int baseResolution = context.GetArg(Args.RESOLUTION).intValue;
            SlotRef inputRefLink = context.GetInputLink(m_id, inputSlot.id);
            Texture inputTexture = context.GetTexture(inputRefLink);
            int inputResolution;
            if (inputTexture == null)
            {
                inputTexture = Texture2D.blackTexture;
                inputResolution = baseResolution;
            }
            else
            {
                inputResolution = inputTexture.width;
            }

            int resolution = this.CalculateResolution(baseResolution, inputResolution);
            SlotRef outputRef = new SlotRef(m_id, outputSlot.id);
            DataPool.RtDescriptor desc = DataPool.RtDescriptor.Create(resolution, resolution, RenderTextureFormat.RFloat);
            RenderTexture targetRt = context.CreateRenderTarget(desc, outputRef);

            m_material = new Material(ShaderUtilities.Find(SHADER_NAME));
            m_material.SetTexture(MAIN_TEX, inputTexture);

            Vector4 worldBounds = context.GetArg(Args.WORLD_BOUNDS).vectorValue;
            float terrainHeight = context.GetArg(Args.TERRAIN_HEIGHT).floatValue;
            Vector3 terrainSize = new Vector3(worldBounds.z, terrainHeight, worldBounds.w);
            m_material.SetVector(TERRAIN_SIZE, terrainSize);

            if (m_blendMode == BlendMode.Max)
            {
                m_material.SetInteger(BLEND_MAX, 1);
            }
            else
            {
                m_material.SetInteger(BLEND_MAX, 0);
            }

            List<Texture2D> textures = new List<Texture2D>();
            if (m_enableHeightMask)
            {
                Texture2D heightTransitionTex = Utilities.TextureFromCurve(m_heightTransition);
                textures.Add(heightTransitionTex);
                m_material.SetFloat(MIN_HEIGHT, m_minHeight);
                m_material.SetFloat(MAX_HEIGHT, m_maxHeight);
                m_material.SetTexture(HEIGHT_TRANSITION, heightTransitionTex);
                m_material.SetInteger(USE_HEIGHT_MASK, 1);
            }
            else
            {
                m_material.SetInteger(USE_HEIGHT_MASK, 0);
            }

            if (m_enableSlopeMask)
            {
                Texture2D slopeTransitionTexture = Utilities.TextureFromCurve(m_slopeTransition);
                textures.Add(slopeTransitionTexture);
                m_material.SetFloat(MIN_ANGLE, m_minAngle * Mathf.Deg2Rad);
                m_material.SetFloat(MAX_ANGLE, m_maxAngle * Mathf.Deg2Rad);
                m_material.SetTexture(SLOPE_TRANSITION, slopeTransitionTexture);
                m_material.SetInteger(USE_SLOPE_MASK, 1);
            }
            else
            {
                m_material.SetInteger(USE_SLOPE_MASK, 0);
            }

            if (m_enableDirectionMask)
            {
                Texture2D falloffTex = Utilities.TextureFromCurve(m_directionFalloff);
                textures.Add(falloffTex);
                m_material.SetFloat(DIRECTION, m_direction);
                m_material.SetFloat(DIRECTION_TOLERANCE, m_directionTolerance);
                m_material.SetTexture(DIRECTION_FALLOFF, falloffTex);
                m_material.SetInteger(USE_DIRECTION_MASK, 1);
            }
            else
            {
                m_material.SetInteger(USE_DIRECTION_MASK, 0);
            }

            Drawing.DrawQuad(targetRt, m_material, PASS);

            context.ReleaseReference(inputRefLink);
            foreach (Texture2D t in textures)
            {
                if (t != null)
                {
                    Object.DestroyImmediate(t);
                }
            }

            Object.DestroyImmediate(m_material);
        }

        public override void Bypass(GraphContext context)
        {
            return;
        }
    }
}
#endif
