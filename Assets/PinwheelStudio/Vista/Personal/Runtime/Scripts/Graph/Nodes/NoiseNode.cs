#if VISTA
using Pinwheel.Vista.Graphics;
using System.Collections;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "Noise",
        path = "Base Shape/Noise",
        icon = "",
        documentation = "",
        keywords = "perlin, billow, ridge, noise",
        description = "Generate fractal noise, useful for basic mountain shape.\n<ss>Tips: Search for the noise mode directly (eg: perlin, billow, ridge, etc.).</ss>")]
    public class NoiseNode : ImageNodeBase, ISetupWithHint, IHasSeed
    {
        public readonly MaskSlot outputSlot = new MaskSlot("Output", SlotDirection.Output, 100);
        public enum WarpMode
        {
            None, Angular, Directional
        }
        public enum LayerDerivativeMode
        {
            Pow,
            Linear
        }

        [SerializeField]
        private Vector2 m_offset;
        public Vector2 offset
        {
            get
            {
                return m_offset;
            }
            set
            {
                m_offset = value;
            }
        }

        [SerializeField]
        private float m_scale;
        public float scale
        {
            get
            {
                return m_scale;
            }
            set
            {
                m_scale = value;
            }
        }

        [SerializeField]
        private float m_lacunarity;
        public float lacunarity
        {
            get
            {
                return m_lacunarity;
            }
            set
            {
                m_lacunarity = Mathf.Max(1, value);
            }
        }

        [SerializeField]
        private float m_persistence;
        public float persistence
        {
            get
            {
                return m_persistence;
            }
            set
            {
                m_persistence = Mathf.Clamp(value, 0.01f, 1f);
            }
        }

        [SerializeField]
        private int m_layerCount;
        public int layerCount
        {
            get
            {
                return m_layerCount;
            }
            set
            {
                m_layerCount = Mathf.Clamp(value, 1, 10);
            }
        }

        [SerializeField]
        private NoiseMode m_mode;
        public NoiseMode mode
        {
            get
            {
                return m_mode;
            }
            set
            {
                m_mode = value;
            }
        }

        [SerializeField]
        private int m_seed;
        public int seed
        {
            get
            {
                return m_seed;
            }
            set
            {
                m_seed = value;
            }
        }

        [SerializeField]
        private LayerDerivativeMode m_layerDerivativeMode;
        public LayerDerivativeMode layerDerivativeMode
        {
            get
            {
                return m_layerDerivativeMode;
            }
            set
            {
                m_layerDerivativeMode = value;
            }
        }

        [SerializeField]
        private bool m_flipSign;
        public bool flipSign
        {
            get
            {
                return m_flipSign;
            }
            set
            {
                m_flipSign = value;
            }
        }

        [SerializeField]
        private WarpMode m_warpMode;
        public WarpMode warpMode
        {
            get
            {
                return m_warpMode;
            }
            set
            {
                m_warpMode = value;
            }
        }

        [SerializeField]
        private float m_warpAngleMin;
        public float warpAngleMin
        {
            get
            {
                return m_warpAngleMin;
            }
            set
            {
                m_warpAngleMin = Mathf.Clamp(Mathf.Min(value, m_warpAngleMax), -360, 360);
            }
        }

        [SerializeField]
        private float m_warpAngleMax;
        public float warpAngleMax
        {
            get
            {
                return m_warpAngleMax;
            }
            set
            {
                m_warpAngleMax = Mathf.Clamp(Mathf.Max(value, m_warpAngleMin), -360, 360);
            }
        }

        [SerializeField]
        private float m_warpIntensity;
        public float warpIntensity
        {
            get
            {
                return m_warpIntensity;
            }
            set
            {
                m_warpIntensity = value;
            }
        }

        [SerializeField]
        private AnimationCurve m_remapCurve;
        public AnimationCurve remapCurve
        {
            get
            {
                return m_remapCurve;
            }
            set
            {
                m_remapCurve = value;
            }
        }

        [SerializeField]
        private bool m_applyRemapPerLayer;
        public bool applyRemapPerLayer
        {
            get
            {
                return m_applyRemapPerLayer;
            }
            set
            {
                m_applyRemapPerLayer = value;
            }
        }
        public NoiseNode() : base()
        {
            m_offset = Vector2.zero;
            m_scale = 750;
            m_lacunarity = 2.25f;
            m_persistence = 0.335f;
            m_layerCount = 8;
            m_mode = NoiseMode.Perlin01 ;
            m_layerDerivativeMode = LayerDerivativeMode.Linear;
            m_flipSign = true;

            m_warpMode = WarpMode.None;
            m_warpAngleMin = -360;
            m_warpAngleMax = 360;
            m_warpIntensity = 1;

            m_remapCurve = AnimationCurve.Linear(0, 0, 1, 1);
            m_applyRemapPerLayer = true;

            System.Random rnd = new System.Random();
            m_seed = rnd.Next(0, 1000);
        }
        public override void ExecuteImmediate(GraphContext context)
        {
            int baseResolution = context.GetArg(Args.RESOLUTION).intValue;
            int resolution = this.CalculateResolution(baseResolution, baseResolution);
            DataPool.RtDescriptor desc = DataPool.RtDescriptor.Create(resolution, resolution);
            SlotRef outputRef = new SlotRef(m_id, outputSlot.id);
            RenderTexture targetRt = context.CreateRenderTarget(desc, outputRef);
            int baseSeed = context.GetArg(Args.SEED).intValue;
            Vector4 worldBounds = context.GetArg(Args.WORLD_BOUNDS).vectorValue;

            NodeLibraryUtilities.NoiseNode.Params p = new NodeLibraryUtilities.NoiseNode.Params();
            p.offset = m_offset;
            p.scale = m_scale;
            p.lacunarity = m_lacunarity;
            p.persistence = m_persistence;
            p.layerCount = m_layerCount;
            p.mode = m_mode;
            p.layerDerivative = (int)m_layerDerivativeMode;
            p.flipSign = m_flipSign;
            p.warpMode = (int)m_warpMode;
            p.warpAngleMin = m_warpAngleMin;
            p.warpAngleMax = m_warpAngleMax;
            p.warpIntensity = m_warpIntensity;
            p.remapCurve = m_remapCurve;
            p.applyRemapPerLayer = m_applyRemapPerLayer;
            p.seed = baseSeed ^ m_seed;
            p.worldBounds = worldBounds;

            NodeLibraryUtilities.NoiseNode.Execute(targetRt, p);
        }
        public override IEnumerator Execute(GraphContext context)
        {
            ExecuteImmediate(context);
            yield return null;
        }
        public void SetupWithHint(string hint)
        {
            if (hint.StartsWith("perlin"))
            {
                mode = NoiseMode.Perlin01;
            }
            else if (hint.StartsWith("billow"))
            {
                mode = NoiseMode.Billow;
            }
            else if (hint.StartsWith("ridge"))
            {
                mode = NoiseMode.Ridged;
            }
        }
    }
}
#endif


