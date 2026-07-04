#if VISTA
using Pinwheel.Vista.Graphics;
using System.Collections.Generic;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "Layered Noise",
        path = "Base Shape/Layered Noise",
        icon = "",
        keywords = "",
        description = "Generate multiple noise layer and stack them on top of other, each layer can have different noise type",
        documentation = "")]
    public class LayeredNoiseNode : ImageNodeBase, IHasSeed
    {
        public readonly MaskSlot outputSlot = new MaskSlot("Output", SlotDirection.Output, 100);

        [SerializeField]
        protected float m_baseScale;
        public float baseScale
        {
            get
            {
                return m_baseScale;
            }
            set
            {
                m_baseScale = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        protected int m_seed;
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
        protected List<LayerConfig> m_layers;
        public List<LayerConfig> layers
        {
            get
            {
                if (m_layers == null)
                {
                    m_layers = new List<LayerConfig>();
                }
                m_layers.RemoveAll(l => l == null);
                if (m_layers.Count == 0)
                {
                    LayerConfig c = new LayerConfig()
                    {
                        mode = NoiseMode.Perlin01,
                        strength = 1f
                    };
                    m_layers.Add(c);
                }
                return m_layers;
            }
            set
            {
                m_layers = value;
            }
        }

        [System.Serializable]
        public class LayerConfig
        {
            [SerializeField]
            protected NoiseMode m_mode;
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
            protected float m_strenth;
            public float strength
            {
                get
                {
                    return m_strenth;
                }
                set
                {
                    m_strenth = Mathf.Clamp01(value);
                }
            }

            public LayerConfig()
            {
                m_mode = NoiseMode.Perlin01;
                strength = 1;
            }
        }

        public LayeredNoiseNode() : base()
        {
            m_baseScale = 1000;
            m_seed = 0;
        }

        private static readonly string SHADER = "Vista/Shaders/Graph/LayeredNoise";
        private static readonly int TARGET_RT = Shader.PropertyToID("_TargetRT");
        private static readonly int TEXTURE_SIZE = Shader.PropertyToID("_TextureSize");
        private static readonly int WORLD_BOUNDS = Shader.PropertyToID("_WorldBounds");
        private static readonly int SCALE = Shader.PropertyToID("_Scale");
        private static readonly int AMPLITUDE = Shader.PropertyToID("_Amplitude");
        private static readonly int SEED = Shader.PropertyToID("_Seed");
        private static readonly int NOISE_TYPE = Shader.PropertyToID("_NoiseType");

        private static readonly int KERNEL = 0;

        private ComputeShader m_shader;

        public override void ExecuteImmediate(GraphContext context)
        {
            int baseResolution = context.GetArg(Args.RESOLUTION).intValue;
            int resolution = this.CalculateResolution(baseResolution, baseResolution);
            DataPool.RtDescriptor desc = DataPool.RtDescriptor.Create(resolution, resolution);
            SlotRef outputRef = new SlotRef(m_id, outputSlot.id);
            RenderTexture targetRt = context.CreateRenderTarget(desc, outputRef);
            Drawing.Blit(Texture2D.blackTexture, targetRt);

            m_shader = Resources.Load<ComputeShader>(SHADER);
            m_shader.SetTexture(KERNEL, TARGET_RT, targetRt);
            m_shader.SetVector(TEXTURE_SIZE, new Vector4(targetRt.width, targetRt.height, 0, 0));

            Vector4 worldBounds = context.GetArg(Args.WORLD_BOUNDS).vectorValue;
            m_shader.SetVector(WORLD_BOUNDS, worldBounds);

            int baseSeed = context.GetArg(Args.SEED).intValue;
            m_shader.SetInt(SEED, baseSeed ^ m_seed);

            int threadGroupX = (resolution + 7) / 8;
            int threadGroupY = 1;
            int threadGroupZ = (resolution + 7) / 8;
            for (int i = 0; i < layers.Count; ++i)
            {
                LayerConfig layer = layers[i];
                m_shader.SetInt(NOISE_TYPE, (int)layer.mode);
                m_shader.SetFloat(AMPLITUDE, layer.strength);
                m_shader.SetFloat(SCALE, m_baseScale / Mathf.Pow(2, i));

                m_shader.Dispatch(KERNEL, threadGroupX, threadGroupY, threadGroupZ);
            }

            Resources.UnloadAsset(m_shader);
        }
    }
}
#endif
