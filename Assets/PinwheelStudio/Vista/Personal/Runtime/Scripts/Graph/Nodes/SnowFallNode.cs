#if VISTA
using Pinwheel.Vista.Graphics;
using System.Collections;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "Snow Fall",
        path = "Nature/Snow Fall",
        icon = "",
        documentation = "",
        keywords = "weather, winter",
        description = "Simulate the process of snow fall and deposit, to hide the geometry below.")]
    public class SnowFallNode : ImageNodeBase
    {
        public readonly MaskSlot inputHeightSlot = new MaskSlot("Height", SlotDirection.Input, 0);
        public readonly MaskSlot snowMaskSlot = new MaskSlot("Snow Mask", SlotDirection.Input, 1);

        public readonly MaskSlot outputHeightSlot = new MaskSlot("Height", SlotDirection.Output, 100);
        public readonly MaskSlot outputSnowSlot = new MaskSlot("Snow", SlotDirection.Output, 101);

        [SerializeField]
        private float m_snowAmount;
        public float snowAmount
        {
            get
            {
                return m_snowAmount;
            }
            set
            {
                m_snowAmount = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        private float m_snowMultiplier;
        public float snowMultiplier
        {
            get
            {
                return m_snowMultiplier;
            }
            set
            {
                m_snowMultiplier = Mathf.Max(0, value);
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
        private float m_restingAngle;
        public float restingAngle
        {
            get
            {
                return m_restingAngle;
            }
            set
            {
                m_restingAngle = Mathf.Clamp(value, 0f, 90f);
            }
        }

        [SerializeField]
        private float m_restingAngleMultiplier;
        public float restingAngleMultiplier
        {
            get
            {
                return m_restingAngleMultiplier;
            }
            set
            {
                m_restingAngleMultiplier = Mathf.Max(0, value);
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
        private static readonly string HELPER_SHADER_NAME = "Hidden/Vista/Graph/SnowFallHelper";
        private static readonly int HEIGHT_MAP = Shader.PropertyToID("_HeightMap");
        private static readonly int SNOW_MASK_MAP = Shader.PropertyToID("_MaskMap");
        private static readonly int BOUNDS = Shader.PropertyToID("_Bounds");

        private static readonly int PASS_INIT = 0;
        private static readonly int PASS_OUTPUT_HEIGHT = 1;
        private static readonly int PASS_OUTPUT_SNOW = 2;

        private ComputeShader m_simulationShader;
        private static readonly string SIM_SHADER_NAME = "Vista/Shaders/Graph/SnowFall";
        private static readonly int SNOW_AMOUNT = Shader.PropertyToID("_SnowAmount");
        private static readonly int FLOW_RATE = Shader.PropertyToID("_FlowRate");
        private static readonly int RESTING_ANGLE = Shader.PropertyToID("_RestingAngle");

        private static readonly int SIM_DATA_RESOLUTION = Shader.PropertyToID("_SimDataResolution");
        private static readonly int WORLD_DATA = Shader.PropertyToID("_WorldData");
        private static readonly int OUTFLOW_VH_DATA = Shader.PropertyToID("_OutflowVHData");
        private static readonly int OUTFLOW_DIAG_DATA = Shader.PropertyToID("_OutflowDiagData");
        private static readonly int RANDOM_SEED = Shader.PropertyToID("_RandomSeed");
        private static readonly int KERNEL_INDEX_SIMULATE = 0;
        private static readonly int KERNEL_INDEX_POST_PROCESS = 1;

        private static readonly string TEX_NAME_WORLD_DATA = "WorldData";
        private static readonly string TEX_NAME_OUTFLOW_VH = "OutflowVH";
        private static readonly string TEX_NAME_OUTFLOW_DIAG = "OutflowDiag";

        private static readonly string KW_HAS_MASK = "HAS_MASK";
        private static readonly string KW_HIGH_QUALITY = "HIGH_QUALITY";

        private struct SimulationTextures
        {
            public int resolution { get; set; }
            public Texture inputHeightTexture { get; set; }
            public Texture maskTexture { get; set; }
            public RenderTexture worldData { get; set; }
            public RenderTexture outflowVHData { get; set; }
            public RenderTexture outflowDiagData { get; set; }
        }

        public SnowFallNode() : base()
        {
            m_shouldSplitExecution = true;

            m_snowAmount = 0.2f;
            m_snowMultiplier = 1;

            m_flowRate = 1;
            m_flowMultiplier = 1;

            m_restingAngle = 15f;
            m_restingAngleMultiplier = 1f;

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

            SlotRef snowMaskRefLink = context.GetInputLink(m_id, snowMaskSlot.id);
            Texture snowMaskTexture = context.GetTexture(snowMaskRefLink);

            m_materialHelper = new Material(ShaderUtilities.Find(HELPER_SHADER_NAME));
            m_materialHelper.SetTexture(HEIGHT_MAP, inputHeightTexture);
            m_materialHelper.SetTexture(SNOW_MASK_MAP, snowMaskTexture);

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
            textures.maskTexture = snowMaskTexture;

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
            m_simulationShader.SetTexture(KERNEL_INDEX_SIMULATE, WORLD_DATA, textures.worldData);
            m_simulationShader.SetTexture(KERNEL_INDEX_SIMULATE, OUTFLOW_VH_DATA, textures.outflowVHData);
            if (m_highQualityMode)
            {
                m_simulationShader.SetTexture(KERNEL_INDEX_SIMULATE, OUTFLOW_DIAG_DATA, textures.outflowDiagData);
                m_simulationShader.EnableKeyword(KW_HIGH_QUALITY);
            }
            else
            {
                m_simulationShader.DisableKeyword(KW_HIGH_QUALITY);
            }
            if (textures.maskTexture != null)
            {
                m_simulationShader.SetTexture(KERNEL_INDEX_SIMULATE, SNOW_MASK_MAP, textures.maskTexture);
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

            m_simulationShader.SetTexture(KERNEL_INDEX_POST_PROCESS, WORLD_DATA, textures.worldData);

            float wt = m_snowAmount * m_snowMultiplier;
            float fr = m_flowRate * m_flowMultiplier;
            float ra = m_restingAngle * m_restingAngleMultiplier;

            m_simulationShader.SetFloat(SNOW_AMOUNT, wt);
            m_simulationShader.SetFloat(FLOW_RATE, fr);
            m_simulationShader.SetFloat(RESTING_ANGLE, ra);
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
            Vector4 worldBounds = context.GetArg(Args.WORLD_BOUNDS).vectorValue;
            float terrainHeight = context.GetArg(Args.TERRAIN_HEIGHT).floatValue;
            Vector3 bounds = new Vector3(worldBounds.z, terrainHeight, worldBounds.w);
            m_materialHelper.SetVector(BOUNDS, bounds);

            SlotRef outputHeightRef = new SlotRef(m_id, outputHeightSlot.id);
            if (context.GetReferenceCount(outputHeightRef) > 0 || context.IsTargetNode(m_id))
            {
                DataPool.RtDescriptor outputHeightDesc = DataPool.RtDescriptor.Create(textures.resolution, textures.resolution, RenderTextureFormat.RFloat);
                RenderTexture outputHeightRt = context.CreateRenderTarget(outputHeightDesc, outputHeightRef);
                m_materialHelper.SetTexture(WORLD_DATA, textures.worldData);
                m_materialHelper.SetTexture(HEIGHT_MAP, textures.inputHeightTexture);
                Drawing.DrawQuad(outputHeightRt, m_materialHelper, PASS_OUTPUT_HEIGHT);
            }

            SlotRef outputSnowRef = new SlotRef(m_id, outputSnowSlot.id);
            if (context.GetReferenceCount(outputSnowRef) > 0)
            {
                DataPool.RtDescriptor outputSnowDesc = DataPool.RtDescriptor.Create(textures.resolution, textures.resolution, RenderTextureFormat.RFloat);
                RenderTexture outputSnowRt = context.CreateRenderTarget(outputSnowDesc, outputSnowRef);
                m_materialHelper.SetTexture(WORLD_DATA, textures.worldData);
                m_materialHelper.SetVector(BOUNDS, bounds);
                Drawing.DrawQuad(outputSnowRt, m_materialHelper, PASS_OUTPUT_SNOW);
            }
        }

        private void CleanUp(GraphContext context)
        {
            SlotRef inputHeightRefLink = context.GetInputLink(m_id, inputHeightSlot.id);
            context.ReleaseReference(inputHeightRefLink);

            SlotRef snowMaskRefLink = context.GetInputLink(m_id, snowMaskSlot.id);
            context.ReleaseReference(snowMaskRefLink);

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
                Dispatch(KERNEL_INDEX_SIMULATE, dimX, dimZ);
                if (i % 5 == 0)
                {
                    Dispatch(KERNEL_INDEX_POST_PROCESS, dimX, dimZ);
                }
            }
            Dispatch(KERNEL_INDEX_POST_PROCESS, dimX, dimZ);
            Dispatch(KERNEL_INDEX_POST_PROCESS, dimX, dimZ);

            Output(context, textures);
            CleanUp(context);
        }

        public override IEnumerator Execute(GraphContext context)
        {
            bool outputTempHeight = context.GetArg(Args.OUTPUT_TEMP_HEIGHT).boolValue;
            SimulationTextures textures = CreateSimulationTextures(context);
            InitSimulationShader(context, textures);

            int dimX = textures.worldData.width;
            int dimZ = textures.worldData.height;
            for (int i = 0; i < m_iterationCount; ++i)
            {
                Dispatch(KERNEL_INDEX_SIMULATE, dimX, dimZ);
                if (i % 5 == 0)
                {
                    Dispatch(KERNEL_INDEX_POST_PROCESS, dimX, dimZ);
                }
                if (i % iterationPerFrame == 0 && shouldSplitExecution)
                {
#if UNITY_EDITOR
                    if (outputTempHeight)
                    {
                        OutputTempHeight(context, textures);
                    }
#endif
                    context.SetCurrentProgress(i * 1.0f / m_iterationCount);
                    yield return null;
                }
            }
            Dispatch(KERNEL_INDEX_POST_PROCESS, dimX, dimZ);
            Dispatch(KERNEL_INDEX_POST_PROCESS, dimX, dimZ);

            Output(context, textures);
            CleanUp(context);

            yield return null;
        }

        private void OutputTempHeight(GraphContext context, SimulationTextures textures)
        {
            RenderTexture tempHeightTexture = context.m_dataPool.GetRT(DataPool.TEMP_HEIGHT_NAME);
            if (tempHeightTexture == null)
            {
                DataPool.RtDescriptor desc = DataPool.RtDescriptor.Create(DataPool.TEMP_HEIGHT_RESOLUTION, DataPool.TEMP_HEIGHT_RESOLUTION, RenderTextureFormat.RFloat);
                tempHeightTexture = context.m_dataPool.CreateTemporaryRT(desc, DataPool.TEMP_HEIGHT_NAME);
            }

            m_materialHelper.SetTexture(WORLD_DATA, textures.worldData);
            m_materialHelper.SetTexture(HEIGHT_MAP, textures.inputHeightTexture);
            Drawing.DrawQuad(tempHeightTexture, m_materialHelper, PASS_OUTPUT_HEIGHT);
        }
    }
}
#endif
