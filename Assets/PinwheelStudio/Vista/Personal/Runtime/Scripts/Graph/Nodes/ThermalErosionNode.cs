#if VISTA
using Pinwheel.Vista.Graphics;
using System.Collections;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "Thermal Erosion",
        path = "Nature/Thermal Erosion",
        icon = "",
        documentation = "",
        keywords = "erosion, thermal, heat, slide, weather",
        description = "Simulate the erosion caused by high temperature.\nBest practice: Chain up 2 or more of this node with Detail Level from low to high to have better result with eroded features at different size.")]
    public class ThermalErosionNode : ImageNodeBase
    {
        public readonly MaskSlot inputHeightSlot = new MaskSlot("Height", SlotDirection.Input, 0);
        public readonly MaskSlot hardnessSlot = new MaskSlot("Hardness", SlotDirection.Input, 1);

        public readonly MaskSlot outputHeightSlot = new MaskSlot("Height", SlotDirection.Output, 100);
        public readonly MaskSlot erosionSlot = new MaskSlot("Erosion", SlotDirection.Output, 101);
        public readonly MaskSlot depositionSlot = new MaskSlot("Deposition", SlotDirection.Output, 102);

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
                m_detailLevel = Mathf.Clamp(value, 0.1f, 1f);
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

        [SerializeField]
        private float m_erosionRate;
        public float erosionRate
        {
            get
            {
                return m_erosionRate;
            }
            set
            {
                m_erosionRate = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        private float m_erosionMultiplier;
        public float erosionMultiplier
        {
            get
            {
                return m_erosionMultiplier;
            }
            set
            {
                m_erosionMultiplier = Mathf.Max(0, value);
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
        private float m_heightScale;
        public float heightScale
        {
            get
            {
                return m_heightScale;
            }
            set
            {
                m_heightScale = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        private float m_erosionBoost;
        public float erosionBoost
        {
            get
            {
                return m_erosionBoost;
            }
            set
            {
                m_erosionBoost = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        private float m_depositionBoost;
        public float depositionBoost
        {
            get
            {
                return m_depositionBoost;
            }
            set
            {
                m_depositionBoost = Mathf.Max(0, value);
            }
        }

        private Material m_materialHelper;
        private static readonly string HELPER_SHADER_NAME = "Hidden/Vista/Graph/ErosionHelper";
        private static readonly int HEIGHT_MAP = Shader.PropertyToID("_HeightMap");
        private static readonly int HARDNESS_MAP = Shader.PropertyToID("_HardnessMap");
        private static readonly int BOUNDS = Shader.PropertyToID("_Bounds");
        private static readonly int TEXTURE_SIZE = Shader.PropertyToID("_TextureSize");
        private static readonly int HEIGHT_SCALE = Shader.PropertyToID("_HeightScale");
        private static readonly int EROSION_BOOST = Shader.PropertyToID("_ErosionBoost");
        private static readonly int DEPOSITION_BOOST = Shader.PropertyToID("_DepositionBoost");

        private static readonly int PASS_INIT_HEIGHT = 0;
        //private static readonly int PASS_INIT_MASK = 1;
        private static readonly int PASS_OUTPUT_HEIGHT = 2;
        private static readonly int PASS_OUTPUT_EROSION = 3;
        private static readonly int PASS_OUTPUT_DEPOSITION = 4;

        private ComputeShader m_simulationShader;
        private static readonly string SIM_SHADER_NAME = "Vista/Shaders/Graph/ThermalErosion";
        private static readonly int EROSION_RATE = Shader.PropertyToID("_ErosionRate");
        private static readonly int RESTING_ANGLE = Shader.PropertyToID("_RestingAngle");

        private static readonly int SIM_DATA_RESOLUTION = Shader.PropertyToID("_SimDataResolution");
        private static readonly int WORLD_DATA = Shader.PropertyToID("_WorldData");
        private static readonly int HEIGHT_CHANGE_DATA = Shader.PropertyToID("_HeightChangeData");
        private static readonly int SOIL_VH_DATA = Shader.PropertyToID("_SoilVHData");
        private static readonly int SOIL_DIAG_DATA = Shader.PropertyToID("_SoilDiagData");
        private static readonly int KERNEL_INDEX_SIMULATE = 0;
        private static readonly int KERNEL_INDEX_POST_PROCESS = 1;

        private static readonly string TEX_NAME_WORLD_DATA = "WorldData";
        private static readonly string TEX_NAME_SOIL_VH = "SoilVH";
        private static readonly string TEX_NAME_SOIL_DIAG = "SoilDiag";
        private static readonly string TEX_NAME_HEIGHT_CHANGE = "HeightChange";

        private static readonly string KW_HIGH_QUALITY = "HIGH_QUALITY";
        private static readonly string KW_HAS_HARDNESS_MAP = "HAS_HARDNESS_MAP";

        private struct SimulationTextures
        {
            public int resolution { get; set; }
            public Texture inputHeightTexture { get; set; }
            public Texture hardnessTexture { get; set; }
            public RenderTexture worldData { get; set; }
            public RenderTexture soilVHData { get; set; }
            public RenderTexture soilDiagData { get; set; }
            public RenderTexture heightChangeData { get; set; }
        }

        public ThermalErosionNode() : base()
        {
            m_shouldSplitExecution = true;

            m_highQualityMode = true;
            m_detailLevel = 1f;
            m_iterationCount = 10;
            m_iterationPerFrame = 1;

            m_erosionRate = 0.1f;
            m_erosionMultiplier = 1f;

            m_restingAngle = 15f;
            m_restingAngleMultiplier = 1f;

            m_heightScale = 1f;
            m_erosionBoost = 1f;
            m_depositionBoost = 1f;
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

            SlotRef hardnessRefLink = context.GetInputLink(m_id, hardnessSlot.id);
            Texture hardnessTexture = context.GetTexture(hardnessRefLink);

            m_materialHelper = new Material(ShaderUtilities.Find(HELPER_SHADER_NAME));
            m_materialHelper.SetTexture(HEIGHT_MAP, inputHeightTexture);

            Vector4 worldBounds = context.GetArg(Args.WORLD_BOUNDS).vectorValue;
            float terrainHeight = context.GetArg(Args.TERRAIN_HEIGHT).floatValue;
            Vector3 bounds = new Vector3(worldBounds.z, terrainHeight, worldBounds.w);
            m_materialHelper.SetVector(BOUNDS, bounds);
            m_materialHelper.SetFloat(HEIGHT_SCALE, m_heightScale);
            m_materialHelper.SetFloat(EROSION_BOOST, m_erosionBoost);
            m_materialHelper.SetFloat(DEPOSITION_BOOST, m_depositionBoost);

            SimulationTextures textures = new SimulationTextures();
            int resolution = this.CalculateResolution(baseResolution, inputResolution);
            textures.resolution = resolution;
            textures.inputHeightTexture = inputHeightTexture;
            textures.hardnessTexture = hardnessTexture;

            float areaSize = bounds.x;
            int simDataResolution = Utilities.MultipleOf8(Mathf.CeilToInt(m_detailLevel * areaSize));
            m_materialHelper.SetVector(TEXTURE_SIZE, new Vector2(simDataResolution, simDataResolution));

            DataPool.RtDescriptor worldDataDesc = DataPool.RtDescriptor.Create(simDataResolution, simDataResolution, RenderTextureFormat.RFloat);
            RenderTexture worldData = context.CreateTemporaryRT(worldDataDesc, TEX_NAME_WORLD_DATA + m_id);
            Drawing.DrawQuad(worldData, m_materialHelper, PASS_INIT_HEIGHT);
            textures.worldData = worldData;

            DataPool.RtDescriptor outflowVHDesc = DataPool.RtDescriptor.Create(simDataResolution, simDataResolution, RenderTextureFormat.ARGBFloat);
            RenderTexture outflowVHData = context.CreateTemporaryRT(outflowVHDesc, TEX_NAME_SOIL_VH + m_id);
            textures.soilVHData = outflowVHData;

            if (m_highQualityMode)
            {
                DataPool.RtDescriptor outflowDiagDesc = DataPool.RtDescriptor.Create(simDataResolution, simDataResolution, RenderTextureFormat.ARGBFloat);
                RenderTexture outflowDiagData = context.CreateTemporaryRT(outflowDiagDesc, TEX_NAME_SOIL_DIAG + m_id);
                textures.soilDiagData = outflowDiagData;
            }

            DataPool.RtDescriptor heightChangeDesc = DataPool.RtDescriptor.Create(simDataResolution, simDataResolution, RenderTextureFormat.RGFloat);
            RenderTexture heightChangeData = context.CreateTemporaryRT(heightChangeDesc, TEX_NAME_HEIGHT_CHANGE + m_id);
            Drawing.Blit(Texture2D.blackTexture, heightChangeData);
            textures.heightChangeData = heightChangeData;

            return textures;
        }

        private void InitSimulationShader(GraphContext context, SimulationTextures textures)
        {
            m_simulationShader = Resources.Load<ComputeShader>(SIM_SHADER_NAME);
            m_simulationShader.SetVector(SIM_DATA_RESOLUTION, Vector2.one * textures.worldData.width);
            m_simulationShader.SetTexture(KERNEL_INDEX_SIMULATE, WORLD_DATA, textures.worldData);
            m_simulationShader.SetTexture(KERNEL_INDEX_SIMULATE, HEIGHT_CHANGE_DATA, textures.heightChangeData);
            m_simulationShader.SetTexture(KERNEL_INDEX_SIMULATE, SOIL_VH_DATA, textures.soilVHData);
            if (m_highQualityMode)
            {
                m_simulationShader.SetTexture(KERNEL_INDEX_SIMULATE, SOIL_DIAG_DATA, textures.soilDiagData);
                m_simulationShader.EnableKeyword(KW_HIGH_QUALITY);
            }
            else
            {
                m_simulationShader.DisableKeyword(KW_HIGH_QUALITY);
            }
            if (textures.hardnessTexture != null)
            {
                m_simulationShader.SetTexture(KERNEL_INDEX_SIMULATE, HARDNESS_MAP, textures.hardnessTexture);
                m_simulationShader.EnableKeyword(KW_HAS_HARDNESS_MAP);
            }
            else
            {
                m_simulationShader.DisableKeyword(KW_HAS_HARDNESS_MAP);
            }

            Vector4 worldBounds = context.GetArg(Args.WORLD_BOUNDS).vectorValue;
            float terrainHeight = context.GetArg(Args.TERRAIN_HEIGHT).floatValue;
            Vector3 bounds = new Vector3(worldBounds.z, terrainHeight, worldBounds.w);
            m_simulationShader.SetVector(BOUNDS, bounds);

            float er = m_erosionRate * m_erosionMultiplier;
            float ra = m_restingAngle * m_restingAngleMultiplier;

            m_simulationShader.SetFloat(EROSION_RATE, er);
            m_simulationShader.SetFloat(RESTING_ANGLE, ra);

            m_simulationShader.SetTexture(KERNEL_INDEX_POST_PROCESS, HEIGHT_CHANGE_DATA, textures.heightChangeData);
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

            SlotRef outputHeightRef = new SlotRef(m_id, outputHeightSlot.id);
            if (context.GetReferenceCount(outputHeightRef) > 0 || context.IsTargetNode(m_id))
            {
                DataPool.RtDescriptor outputHeightDesc = DataPool.RtDescriptor.Create(textures.resolution, textures.resolution, RenderTextureFormat.RFloat);
                RenderTexture outputHeightRt = context.CreateRenderTarget(outputHeightDesc, outputHeightRef);
                m_materialHelper.SetTexture(HEIGHT_MAP, textures.inputHeightTexture);
                m_materialHelper.SetTexture(HEIGHT_CHANGE_DATA, textures.heightChangeData);
                m_materialHelper.SetVector(BOUNDS, bounds);
                Drawing.DrawQuad(outputHeightRt, m_materialHelper, PASS_OUTPUT_HEIGHT);
            }

            SlotRef outputErosionRef = new SlotRef(m_id, erosionSlot.id);
            if (context.GetReferenceCount(outputErosionRef) > 0)
            {
                DataPool.RtDescriptor outputErosionDesc = DataPool.RtDescriptor.Create(textures.resolution, textures.resolution, RenderTextureFormat.RFloat);
                RenderTexture outputErosionRt = context.CreateRenderTarget(outputErosionDesc, outputErosionRef);
                m_materialHelper.SetTexture(HEIGHT_CHANGE_DATA, textures.heightChangeData);
                m_materialHelper.SetVector(BOUNDS, bounds);
                Drawing.DrawQuad(outputErosionRt, m_materialHelper, PASS_OUTPUT_EROSION);
            }

            SlotRef outputDepositionRef = new SlotRef(m_id, depositionSlot.id);
            if (context.GetReferenceCount(outputDepositionRef) > 0)
            {
                DataPool.RtDescriptor outputDepositionDesc = DataPool.RtDescriptor.Create(textures.resolution, textures.resolution, RenderTextureFormat.RFloat);
                RenderTexture outputDepositionRt = context.CreateRenderTarget(outputDepositionDesc, outputDepositionRef);
                m_materialHelper.SetTexture(HEIGHT_CHANGE_DATA, textures.heightChangeData);
                m_materialHelper.SetVector(BOUNDS, bounds);
                Drawing.DrawQuad(outputDepositionRt, m_materialHelper, PASS_OUTPUT_DEPOSITION);
            }
        }

        private void CleanUp(GraphContext context)
        {
            SlotRef inputHeightRefLink = context.GetInputLink(m_id, inputHeightSlot.id);
            context.ReleaseReference(inputHeightRefLink);

            SlotRef hardnessRefLink = context.GetInputLink(m_id, hardnessSlot.id);
            context.ReleaseReference(hardnessRefLink);

            context.ReleaseTemporary(TEX_NAME_WORLD_DATA + m_id);
            context.ReleaseTemporary(TEX_NAME_SOIL_VH + m_id);
            context.ReleaseTemporary(TEX_NAME_SOIL_DIAG + m_id);
            context.ReleaseTemporary(TEX_NAME_HEIGHT_CHANGE + m_id);

            Object.DestroyImmediate(m_materialHelper);
            Resources.UnloadAsset(m_simulationShader);
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

        private void OutputTempHeight(GraphContext context, SimulationTextures textures)
        {
            RenderTexture tempHeightTexture = context.m_dataPool.GetRT(DataPool.TEMP_HEIGHT_NAME);
            if (tempHeightTexture == null)
            {
                DataPool.RtDescriptor desc = DataPool.RtDescriptor.Create(DataPool.TEMP_HEIGHT_RESOLUTION, DataPool.TEMP_HEIGHT_RESOLUTION, RenderTextureFormat.RFloat);
                tempHeightTexture = context.m_dataPool.CreateTemporaryRT(desc, DataPool.TEMP_HEIGHT_NAME);
            }

            Vector4 worldBounds = context.GetArg(Args.WORLD_BOUNDS).vectorValue;
            float terrainHeight = context.GetArg(Args.TERRAIN_HEIGHT).floatValue;
            Vector3 bounds = new Vector3(worldBounds.z, terrainHeight, worldBounds.w);

            m_materialHelper.SetTexture(HEIGHT_MAP, textures.inputHeightTexture);
            m_materialHelper.SetTexture(HEIGHT_CHANGE_DATA, textures.heightChangeData);
            m_materialHelper.SetVector(BOUNDS, bounds);
            Drawing.DrawQuad(tempHeightTexture, m_materialHelper, PASS_OUTPUT_HEIGHT);
        }
    }
}
#endif
