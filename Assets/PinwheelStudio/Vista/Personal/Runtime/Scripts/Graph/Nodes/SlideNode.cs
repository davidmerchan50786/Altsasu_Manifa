#if VISTA
using Pinwheel.Vista.Graphics;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "Slide",
        path = "Nature/Slide",
        icon = "",
        documentation = "",
        keywords = "gravity",
        description = "Simulate the process of objects (rock, debris, etc.) slide down the slope.")]
    public class SlideNode : ExecutableNodeBase
    {
        public readonly BufferSlot inputPositionSlot = new BufferSlot("Positions", SlotDirection.Input, 0);
        public readonly MaskSlot heightMapSlot = new MaskSlot("Height Map", SlotDirection.Input, 1);

        public readonly BufferSlot outputPositionSlot = new BufferSlot("Output", SlotDirection.Output, 100);
        public readonly MaskSlot trailSlot = new MaskSlot("Trail", SlotDirection.Output, 101);

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
        private float m_trailCurvature;
        public float trailCurvature
        {
            get
            {
                return m_trailCurvature;
            }
            set
            {
                m_trailCurvature = Mathf.Clamp01(value);
            }
        }

        private static readonly string COMPUTE_SHADER = "Vista/Shaders/Graph/Slide";
        private static readonly int INPUT_POSITIONS = Shader.PropertyToID("_InputPositions");
        private static readonly int HEIGHT_MAP = Shader.PropertyToID("_HeightMap");
        private static readonly int HEIGHT_MAP_TEXEL_SIZE = Shader.PropertyToID("_HeightMap_TexelSize");
        private static readonly int OFFSET_BUFFER = Shader.PropertyToID("_Offsets");
        private static readonly int OUTPUT_POSITIONS = Shader.PropertyToID("_OutputPositions");
        private static readonly int TRAIL_MAP = Shader.PropertyToID("_TrailMap");
        private static readonly int TRAIL_MAP_RESOLUTION = Shader.PropertyToID("_TrailMapResolution");
        private static readonly int TRAIL_INTENSITY = Shader.PropertyToID("_TrailIntensity");
        private static readonly int TRAIL_CURVATURE_FACTOR = Shader.PropertyToID("_TrailCurvatureFactor");
        private static readonly int BASE_INDEX = Shader.PropertyToID("_BaseIndex");
        private static readonly int INSTANCE_COUNT = Shader.PropertyToID("_InstanceCount");

        private static readonly string TMP_OFFSET_BUFFER_NAME = "TmpSlideOffset";

        private static readonly int THREAD_PER_GROUP = 8;
        private static readonly int MAX_THREAD_GROUP = 64000 / THREAD_PER_GROUP;

        private static readonly string KW_HAS_HEIGHT_MAP = "HAS_HEIGHT_MAP";
        private static readonly string KW_HAS_TRAIL_MAP = "HAS_TRAIL_MAP";
        private static readonly int KERNEL_INIT = 0;
        private static readonly int KERNEL_SIM = 1;

        private ComputeShader m_computeShader;

        public SlideNode() : base()
        {
            m_iterationCount = 10;
        }

        public override void ExecuteImmediate(GraphContext context)
        {
            SlotRef inputPositionRefLink = context.GetInputLink(m_id, inputPositionSlot.id);
            ComputeBuffer inputPositionBuffer = context.GetBuffer(inputPositionRefLink);
            if (inputPositionBuffer == null)
            {
                return;
            }
            else if (inputPositionBuffer.count % PositionSample.SIZE != 0)
            {
                Debug.LogError($"Cannot parse {inputPositionSlot.name} buffer, node id {m_id}");
                return;
            }

            int instanceCount = inputPositionBuffer.count / PositionSample.SIZE;

            SlotRef heightMapRefLink = context.GetInputLink(m_id, heightMapSlot.id);
            Texture heightMap = context.GetTexture(heightMapRefLink);

            SlotRef outputPositionRef = new SlotRef(m_id, outputPositionSlot.id);
            DataPool.BufferDescriptor desc = DataPool.BufferDescriptor.Create(inputPositionBuffer.count);
            ComputeBuffer outputPositionBuffer = context.CreateBuffer(desc, outputPositionRef);

            DataPool.BufferDescriptor tmpOffsetDesc = DataPool.BufferDescriptor.Create(instanceCount * sizeof(float) * 2);
            ComputeBuffer tmpOffsetBuffer = context.CreateTemporaryBuffer(tmpOffsetDesc, TMP_OFFSET_BUFFER_NAME);

            m_computeShader = Resources.Load<ComputeShader>(COMPUTE_SHADER);
            m_computeShader.SetBuffer(KERNEL_INIT, INPUT_POSITIONS, inputPositionBuffer);
            m_computeShader.SetBuffer(KERNEL_INIT, OUTPUT_POSITIONS, outputPositionBuffer);
            m_computeShader.SetBuffer(KERNEL_INIT, OFFSET_BUFFER, tmpOffsetBuffer);

            m_computeShader.SetBuffer(KERNEL_SIM, OUTPUT_POSITIONS, outputPositionBuffer);
            m_computeShader.SetBuffer(KERNEL_SIM, OFFSET_BUFFER, tmpOffsetBuffer);
            m_computeShader.SetInt(INSTANCE_COUNT, instanceCount);

            if (heightMap != null)
            {
                m_computeShader.SetTexture(KERNEL_SIM, HEIGHT_MAP, heightMap);
                m_computeShader.SetVector(HEIGHT_MAP_TEXEL_SIZE, heightMap.texelSize);
                m_computeShader.EnableKeyword(KW_HAS_HEIGHT_MAP);
            }
            else
            {
                m_computeShader.DisableKeyword(KW_HAS_HEIGHT_MAP);
            }

            SlotRef trailRef = new SlotRef(m_id, trailSlot.id);
            if (context.GetReferenceCount(trailRef) > 0)
            {
                int trailResolution;
                if (heightMap != null)
                {
                    trailResolution = heightMap.width;
                }
                else
                {
                    trailResolution = context.GetArg(Args.RESOLUTION).intValue;
                }
                DataPool.RtDescriptor trailDesc = DataPool.RtDescriptor.Create(trailResolution, trailResolution, RenderTextureFormat.RFloat);
                RenderTexture trailTex = context.CreateRenderTarget(trailDesc, trailRef);
                Drawing.Blit(Texture2D.blackTexture, trailTex);
                m_computeShader.SetTexture(KERNEL_SIM, TRAIL_MAP, trailTex);
                m_computeShader.SetFloat(TRAIL_MAP_RESOLUTION, trailResolution);
                m_computeShader.SetFloat(TRAIL_CURVATURE_FACTOR, 1.0f / ((1f - trailCurvature) * 100f + 1f));
                m_computeShader.EnableKeyword(KW_HAS_TRAIL_MAP);
            }
            else
            {
                m_computeShader.DisableKeyword(KW_HAS_TRAIL_MAP);
            }

            int totalThreadGroupX = (instanceCount + THREAD_PER_GROUP - 1) / THREAD_PER_GROUP;
            int pass = (totalThreadGroupX + MAX_THREAD_GROUP - 1) / MAX_THREAD_GROUP;
            for (int i = 0; i < pass; ++i)
            {
                int threadGroupX = Mathf.Min(MAX_THREAD_GROUP, totalThreadGroupX);
                totalThreadGroupX -= MAX_THREAD_GROUP;
                int baseIndex = i * MAX_THREAD_GROUP * THREAD_PER_GROUP;
                m_computeShader.SetInt(BASE_INDEX, baseIndex);
                m_computeShader.Dispatch(KERNEL_INIT, threadGroupX, 1, 1);
            }

            for (int i = 0; i < m_iterationCount; ++i)
            {
                m_computeShader.SetFloat(TRAIL_INTENSITY, i * 1.0f / (m_iterationCount - 1));

                totalThreadGroupX = (instanceCount + THREAD_PER_GROUP - 1) / THREAD_PER_GROUP;
                pass = (totalThreadGroupX + MAX_THREAD_GROUP - 1) / MAX_THREAD_GROUP;
                for (int j = 0; j < pass; ++j)
                {
                    int threadGroupX = Mathf.Min(MAX_THREAD_GROUP, totalThreadGroupX);
                    totalThreadGroupX -= MAX_THREAD_GROUP;
                    int baseIndex = j * MAX_THREAD_GROUP * THREAD_PER_GROUP;
                    m_computeShader.SetInt(BASE_INDEX, baseIndex);
                    m_computeShader.Dispatch(KERNEL_SIM, threadGroupX, 1, 1);
                }
            }

            context.ReleaseReference(inputPositionRefLink);
            context.ReleaseReference(heightMapRefLink);
            context.ReleaseTemporary(TMP_OFFSET_BUFFER_NAME);
            Resources.UnloadAsset(m_computeShader);
        }
    }
}
#endif
