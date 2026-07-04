#if VISTA
using Pinwheel.Vista.Graphics;
using System.Collections;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "Water Flow",
        path = "Nature/Water Flow",
        icon = "",
        documentation = "",
        keywords = "hydraulic, water, flow, stream, river",
        description = "Simulate the process of water flow over the surface. No erosion or deposition happens.")]
    public class WaterFlowNode : ImageNodeBase
    {
        public readonly MaskSlot inputHeightSlot = new MaskSlot("Height", SlotDirection.Input, 0);
        public readonly MaskSlot waterMaskSlot = new MaskSlot("Water Mask", SlotDirection.Input, 1);

        public readonly MaskSlot outputSlot = new MaskSlot("Output", SlotDirection.Output, 100);

        [SerializeField]
        private float m_waterSourceAmount;
        public float waterSourceAmount
        {
            get
            {
                return m_waterSourceAmount;
            }
            set
            {
                m_waterSourceAmount = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        private float m_waterSourceMultiplier;
        public float waterSourceMultiplier
        {
            get
            {
                return m_waterSourceMultiplier;
            }
            set
            {
                m_waterSourceMultiplier = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        private float m_rainRate;
        public float rainRate
        {
            get
            {
                return m_rainRate;
            }
            set
            {
                m_rainRate = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        private float m_rainMultiplier;
        public float rainMultiplier
        {
            get
            {
                return m_rainMultiplier;
            }
            set
            {
                m_rainMultiplier = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        private float m_flowRate;
        public float flowRate
        {
            get
            {
                return m_flowRate;
            }
            set
            {
                m_flowRate = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        private float m_flowMultiplier;
        public float flowMultiplier
        {
            get
            {
                return m_flowMultiplier;
            }
            set
            {
                m_flowMultiplier = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        private float m_evaporationRate;
        public float evaporationRate
        {
            get
            {
                return m_evaporationRate;
            }
            set
            {
                m_evaporationRate = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        private float m_evaporationMultiplier;
        public float evaporationMultiplier
        {
            get
            {
                return m_evaporationMultiplier;
            }
            set
            {
                m_evaporationMultiplier = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        private bool m_highQualityMode;
        public bool highQualityMode
        {
            get
            {
                return m_highQualityMode;
            }
            set
            {
                m_highQualityMode = value;
            }
        }

        [SerializeField]
        private float m_detailLevel;
        public float detailLevel
        {
            get
            {
                return m_detailLevel;
            }
            set
            {
                m_detailLevel = Mathf.Clamp(value, 0.2f, 1f);
            }
        }

        [SerializeField]
        private int m_iterationCount;
        public int iterationCount
        {
            get
            {
                return m_iterationCount;
            }
            set
            {
                m_iterationCount = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        private int m_iterationPerFrame;
        public int iterationPerFrame
        {
            get
            {
                return Mathf.Max(1, m_iterationPerFrame);
            }
            set
            {
                m_iterationPerFrame = Mathf.Max(1, value);
            }
        }

        private Material m_materialHelper;
        private static readonly string HELPER_SHADER_NAME = "Hidden/Vista/Graph/WaterFlowHelper";
        private static readonly int HEIGHT_MAP = Shader.PropertyToID("_HeightMap");
        private static readonly int WATER_SOURCE_MAP = Shader.PropertyToID("_WaterSourceMap");
        private static readonly int OUTPUT_MULTIPLIER = Shader.PropertyToID("_OutputMultiplier");
        private static readonly int BOUNDS = Shader.PropertyToID("_Bounds");
        private static readonly int TERRAIN_POS = Shader.PropertyToID("_TerrainPos");

        private static readonly int PASS_INIT = 0;
        private static readonly int PASS_OUTPUT = 1;

        private ComputeShader m_simulationShader;
        private static readonly string SIM_SHADER_NAME = "Vista/Shaders/Graph/WaterFlow";
        private static readonly int WATER_SOURCE_AMOUNT = Shader.PropertyToID("_WaterSourceAmount");
        private static readonly int RAIN_RATE = Shader.PropertyToID("_RainRate");
        private static readonly int FLOW_RATE = Shader.PropertyToID("_FlowRate");
        private static readonly int EVAPORATION_RATE = Shader.PropertyToID("_EvaporationRate");

        private static readonly int SIM_DATA_RESOLUTION = Shader.PropertyToID("_SimDataResolution");
        private static readonly int WORLD_DATA = Shader.PropertyToID("_WorldData");
        private static readonly int MASK_MAP = Shader.PropertyToID("_MaskMap");
        private static readonly int OUTFLOW_VH_DATA = Shader.PropertyToID("_OutflowVHData");
        private static readonly int OUTFLOW_DIAG_DATA = Shader.PropertyToID("_OutflowDiagData");
        private static readonly int RANDOM_SEED = Shader.PropertyToID("_RandomSeed");
        private static readonly int KERNEL_SIMULATE = 0;
        private static readonly int KERNEL_POST_PROCESS = 1;

        private static readonly string TEX_NAME_WORLD_DATA = "WorldData";
        private static readonly string TEX_NAME_OUTFLOW_VH = "OutflowVH";
        private static readonly string TEX_NAME_OUTFLOW_DIAG = "OutflowDiag";

        private static readonly string KW_HAS_MASK = "HAS_MASK";
        private static readonly string KW_HIGH_QUALITY = "HIGH_QUALITY";

        private struct SimulationTextures
        {
            public int resolution { get; set; }
            public Texture inputHeightTexture { get; set; }
            public Texture maskMap { get; set; }
            public RenderTexture worldData { get; set; }
            public RenderTexture outflowVHData { get; set; }
            public RenderTexture outflowDiagData { get; set; }
        }

        public WaterFlowNode() : base()
        {
            m_shouldSplitExecution = true;

            m_waterSourceAmount = 0.05f;
            m_waterSourceMultiplier = 1;

            m_rainRate = 0.075f;
            m_rainMultiplier = 1;

            m_flowRate = 1;
            m_flowMultiplier = 1;

            m_evaporationRate = 0.03f;
            m_evaporationMultiplier = 1;

            m_highQualityMode = true;
            m_detailLevel = 1f;
            m_iterationCount = 15;
            m_iterationPerFrame = 1;
        }

        private SimulationTextures CreateSimulationTextures(GraphContext context)
        {
            int baseResolution = context.GetArg(Args.RESOLUTION).intValue;

            SlotRef inputHeightRefLink = context.GetInputLink(m_id, inputHeightSlot.id);
            Texture inputHeightTexture = context.GetTexture(inputHeightRefLink);
            int inputResolution;
            if (inputHeightTexture == null)
            {
                inputHeightTexture = Texture2D.blackTexture;
                inputResolution = baseResolution;
            }
            else
            {
                inputResolution = inputHeightTexture.width;
            }

            SlotRef waterSourceRefLink = context.GetInputLink(m_id, waterMaskSlot.id);
            Texture waterSourceTexture = context.GetTexture(waterSourceRefLink);

            m_materialHelper = new Material(ShaderUtilities.Find(HELPER_SHADER_NAME));
            m_materialHelper.SetTexture(HEIGHT_MAP, inputHeightTexture);
            m_materialHelper.SetTexture(WATER_SOURCE_MAP, waterSourceTexture);

            Vector4 worldBounds = context.GetArg(Args.WORLD_BOUNDS).vectorValue;
            float terrainHeight = context.GetArg(Args.TERRAIN_HEIGHT).floatValue;
            Vector3 bounds = new Vector3(worldBounds.z, terrainHeight, worldBounds.w);
            m_materialHelper.SetVector(BOUNDS, bounds);

            int resolution = this.CalculateResolution(baseResolution, inputResolution);
            float areaSize = bounds.x;
            int simDataResolution = Utilities.MultipleOf8(Mathf.CeilToInt(m_detailLevel * areaSize));
            SimulationTextures textures = new SimulationTextures();
            textures.resolution = resolution;
            textures.inputHeightTexture = inputHeightTexture;
            textures.maskMap = waterSourceTexture;

            DataPool.RtDescriptor worldDataDesc = DataPool.RtDescriptor.Create(simDataResolution, simDataResolution, RenderTextureFormat.RGFloat);
            RenderTexture worldData = context.CreateTemporaryRT(worldDataDesc, TEX_NAME_WORLD_DATA + m_id);
            Drawing.DrawQuad(worldData, m_materialHelper, PASS_INIT);
            textures.worldData = worldData;

            DataPool.RtDescriptor outflowVHDesc = DataPool.RtDescriptor.Create(simDataResolution, simDataResolution, RenderTextureFormat.ARGBFloat);
            RenderTexture outflowVHData = context.CreateTemporaryRT(outflowVHDesc, TEX_NAME_OUTFLOW_VH + m_id);
            textures.outflowVHData = outflowVHData;

            if (m_highQualityMode)
            {
                DataPool.RtDescriptor outflowDiagDesc = DataPool.RtDescriptor.Create(simDataResolution, simDataResolution, RenderTextureFormat.ARGBFloat);
                RenderTexture outflowDiagData = context.CreateTemporaryRT(outflowDiagDesc, TEX_NAME_OUTFLOW_DIAG + m_id);
                textures.outflowDiagData = outflowDiagData;
            }

            return textures;
        }

        private void InitSimulationShader(GraphContext context, SimulationTextures textures)
        {
            m_simulationShader = Resources.Load<ComputeShader>(SIM_SHADER_NAME);
            m_simulationShader.SetVector(SIM_DATA_RESOLUTION, Vector2.one * textures.worldData.width);
            m_simulationShader.SetTexture(KERNEL_SIMULATE, WORLD_DATA, textures.worldData);
            m_simulationShader.SetTexture(KERNEL_SIMULATE, OUTFLOW_VH_DATA, textures.outflowVHData);
            if (m_highQualityMode)
            {
                m_simulationShader.SetTexture(KERNEL_SIMULATE, OUTFLOW_DIAG_DATA, textures.outflowDiagData);
                m_simulationShader.EnableKeyword(KW_HIGH_QUALITY);
            }
            else
            {
                m_simulationShader.DisableKeyword(KW_HIGH_QUALITY);
            }
            if (textures.maskMap != null)
            {
                m_simulationShader.SetTexture(KERNEL_SIMULATE, MASK_MAP, textures.maskMap);
                m_simulationShader.EnableKeyword(KW_HAS_MASK);
            }
            else
            {
                m_simulationShader.DisableKeyword(KW_HAS_MASK);
            }

            int baseSeed = context.GetArg(Args.SEED).intValue;
            System.Random rnd = new System.Random(baseSeed);
            m_simulationShader.SetVector(RANDOM_SEED, new Vector4((float)rnd.NextDouble(), (float)rnd.NextDouble()));

            Vector4 worldBounds = context.GetArg(Args.WORLD_BOUNDS).vectorValue;
            float terrainHeight = context.GetArg(Args.TERRAIN_HEIGHT).floatValue;
            Vector3 bounds = new Vector3(worldBounds.z, terrainHeight, worldBounds.w);
            m_simulationShader.SetVector(BOUNDS, bounds);
            m_simulationShader.SetVector(TERRAIN_POS, new Vector4(worldBounds.x, 0, worldBounds.y, 0));

            float wt = m_waterSourceAmount * m_waterSourceMultiplier;
            float rr = m_rainRate * m_rainMultiplier;
            float fr = m_flowRate * m_flowMultiplier;
            float ev = m_evaporationRate * m_evaporationMultiplier;

            m_simulationShader.SetFloat(WATER_SOURCE_AMOUNT, wt);
            m_simulationShader.SetFloat(RAIN_RATE, rr);
            m_simulationShader.SetFloat(FLOW_RATE, fr);
            m_simulationShader.SetFloat(EVAPORATION_RATE, ev);

            m_simulationShader.SetTexture(KERNEL_POST_PROCESS, WORLD_DATA, textures.worldData);
        }

        private void Dispatch(int kernel, int dimX, int dimZ)
        {
            int threadGroupX = (dimX + 7) / 8;
            int threadGroupY = 1;
            int threadGroupZ = (dimZ + 7) / 8;

            m_simulationShader.Dispatch(kernel, threadGroupX, threadGroupY, threadGroupZ);
        }

        private void Output(GraphContext context, SimulationTextures textures)
        {
            int baseResolution = context.GetArg(Args.RESOLUTION).intValue;
            SlotRef outputHeightRef = new SlotRef(m_id, outputSlot.id);

            DataPool.RtDescriptor outputHeightDesc = DataPool.RtDescriptor.Create(textures.resolution, textures.resolution, RenderTextureFormat.RFloat);
            RenderTexture outputHeightRt = context.CreateRenderTarget(outputHeightDesc, outputHeightRef);
            m_materialHelper.SetTexture(WORLD_DATA, textures.worldData);
            Drawing.DrawQuad(outputHeightRt, m_materialHelper, PASS_OUTPUT);
        }

        private void CleanUp(GraphContext context)
        {
            SlotRef inputHeightRefLink = context.GetInputLink(m_id, inputHeightSlot.id);
            context.ReleaseReference(inputHeightRefLink);

            SlotRef waterSourceRefLink = context.GetInputLink(m_id, waterMaskSlot.id);
            context.ReleaseReference(waterSourceRefLink);

            context.ReleaseTemporary(TEX_NAME_WORLD_DATA + m_id);
            context.ReleaseTemporary(TEX_NAME_OUTFLOW_VH + m_id);
            context.ReleaseTemporary(TEX_NAME_OUTFLOW_DIAG + m_id);

            Object.DestroyImmediate(m_materialHelper);
            Resources.UnloadAsset(m_simulationShader);
        }

        public override void ExecuteImmediate(GraphContext context)
        {
            SimulationTextures textures = CreateSimulationTextures(context);
            InitSimulationShader(context, textures);

            int dimX = textures.worldData.width;
            int dimZ = textures.worldData.height;
            for (int i = 0; i < m_iterationCount; ++i)
            {
                Dispatch(KERNEL_SIMULATE, dimX, dimZ);
            }
            Dispatch(KERNEL_POST_PROCESS, dimX, dimZ);

            Output(context, textures);
            CleanUp(context);
        }

        public override IEnumerator Execute(GraphContext context)
        {
            SimulationTextures textures = CreateSimulationTextures(context);
            InitSimulationShader(context, textures);

            int dimX = textures.worldData.width;
            int dimZ = textures.worldData.height;
            for (int i = 0; i < m_iterationCount; ++i)
            {
                Dispatch(KERNEL_SIMULATE, dimX, dimZ);

                if (i % iterationPerFrame == 0 && shouldSplitExecution)
                {
                    context.SetCurrentProgress(i * 1.0f / m_iterationCount);
                    yield return null;
                }
            }
            Dispatch(KERNEL_POST_PROCESS, dimX, dimZ);

            Output(context, textures);
            CleanUp(context);

            yield return null;
        }
    }
}
#endif
