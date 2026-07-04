#if VISTA
using Pinwheel.Vista.Graphics;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "Crack",
        path = "Nature/Crack",
        icon = "",
        documentation = "",
        keywords = "",
        description = "Simulate earth cracks at specific positions.")]
    public class CrackNode : ImageNodeBase
    {
        public readonly BufferSlot positionSlot = new BufferSlot("Positions", SlotDirection.Input, 0);
        public readonly MaskSlot inputHeightSlot = new MaskSlot("Height", SlotDirection.Input, 1);

        public readonly MaskSlot outputHeightSlot = new MaskSlot("Height", SlotDirection.Output, 100);
        public readonly MaskSlot crackSlot = new MaskSlot("Crack", SlotDirection.Output, 101);

        [SerializeField]
        protected float m_smoothness;
        public float smoothness
        {
            get
            {
                return m_smoothness;
            }
            set
            {
                m_smoothness = Mathf.Clamp(value, 0.01f, 1f);
            }
        }

        [SerializeField]
        protected float m_width;
        public float width
        {
            get
            {
                return m_width;
            }
            set
            {
                m_width = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        protected float m_length;
        public float length
        {
            get
            {
                return m_length;
            }
            set
            {
                m_length = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        protected float m_depth;
        public float depth
        {
            get
            {
                return m_depth;
            }
            set
            {
                m_depth = Mathf.Clamp01(value);
            }
        }

        [SerializeField]
        protected float m_angleLimit;
        public float angleLimit
        {
            get
            {
                return m_angleLimit;
            }
            set
            {
                m_angleLimit = Mathf.Clamp(value, 0f, 90f);
            }
        }

        [SerializeField]
        protected int m_iterationCount;
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

        private static readonly string HELPER_SHADER_NAME = "Hidden/Vista/Graph/CrackHelper";
        private static readonly int HEIGHT_MAP = Shader.PropertyToID("_HeightMap");
        private static readonly int MIN_ANGLE = Shader.PropertyToID("_MinAngle");
        private static readonly int MAX_ANGLE = Shader.PropertyToID("_MaxAngle");
        private static readonly int TERRAIN_SIZE = Shader.PropertyToID("_TerrainSize");
        private static readonly int NOISE_SCALE = Shader.PropertyToID("_NoiseScale");
        private static readonly int NOISE_OFFSET = Shader.PropertyToID("_NoiseOffset");
        private static readonly int PASS_HEIGHT_MASK = 0;
        private static readonly int PASS_OUTPUT_HEIGHT = 1;

        private static readonly string COMPUTE_SHADER_NAME = "Vista/Shaders/Graph/Crack";
        private static readonly int INPUT_POSITIONS = Shader.PropertyToID("_InputPositions");
        private static readonly int CRACK_SAMPLES = Shader.PropertyToID("_Samples");
        private static readonly int HEIGHT_MASK = Shader.PropertyToID("_HeightMask");
        private static readonly int HEIGHT_MASK_TEXEL_SIZE = Shader.PropertyToID("_HeightMask_TexelSize");
        private static readonly int TRAIL_MAP = Shader.PropertyToID("_TrailMap");
        private static readonly int TRAIL_MAP_RESOLUTION = Shader.PropertyToID("_TrailMapResolution");
        private static readonly int TRAIL_INTENSITY = Shader.PropertyToID("_TrailIntensity");
        private static readonly int ITERATION = Shader.PropertyToID("_Iteration");
        private static readonly int SMOOTHNESS = Shader.PropertyToID("_Smoothness");
        private static readonly int LENGTH = Shader.PropertyToID("_Length");
        private static readonly int BASE_INDEX = Shader.PropertyToID("_BaseIndex");

        private static readonly int VERTICES = Shader.PropertyToID("_Vertices");
        private static readonly int TRAIL_WIDTH = Shader.PropertyToID("_TrailWidth");
        private static readonly int TRAIL_DEPTH = Shader.PropertyToID("_TrailDepth");

        private static readonly int CRACK_SAMPLE_STRUCT_SIZE = sizeof(float) * 12;
        private static readonly int KERNEL_INIT = 0;
        private static readonly int KERNEL_SIM = 1;
        private static readonly int KERNEL_VERTEX_GEN = 2;

        private static readonly int THREAD_PER_GROUP = 8;
        private static readonly int MAX_THREAD_GROUP = 64000 / THREAD_PER_GROUP;

        private static readonly string TRAIL_SHADER_NAME = "Hidden/Vista/Graph/CrackTrail";

        private static readonly string TEMP_CRACK_SAMPLES_NAME = "CrackTempCrackSamples";
        private static readonly string TEMP_VELOCITY_NAME = "CrackTempVelocity";
        private static readonly string TEMP_HEIGHT_MASK_NAME = "CrackTempHeightMask";
        private static readonly string TEMP_TRAIL_MAP_NAME = "CrackTempTrailMap";
        private static readonly string TEMP_VERTICES_NAME = "CrackTempVertices";

        private Material m_helperMaterial;
        private Material m_trailMaterial;
        private ComputeShader m_computeShader;

        public CrackNode() : base()
        {
            m_smoothness = 0.5f;
            m_width = 2f;
            m_length = 10f;
            m_depth = 0.05f;
            m_angleLimit = 15;
            m_iterationCount = 50;
        }

        public override void ExecuteImmediate(GraphContext context)
        {
            SlotRef positionRefLink = context.GetInputLink(m_id, positionSlot.id);
            ComputeBuffer inputPositionBuffer = context.GetBuffer(positionRefLink);

            if (inputPositionBuffer == null)
                return;

            SlotRef inputHeightRefLink = context.GetInputLink(m_id, inputHeightSlot.id);
            Texture inputHeightTexture = context.GetTexture(inputHeightRefLink);

            int baseResolution = context.GetArg(Args.RESOLUTION).intValue;
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

            int resolution = this.CalculateResolution(baseResolution, inputResolution);
            int instanceCount = inputPositionBuffer.count / PositionSample.SIZE;

            DataPool.BufferDescriptor crackSamplesDesc = DataPool.BufferDescriptor.Create(instanceCount * CRACK_SAMPLE_STRUCT_SIZE);
            ComputeBuffer crackSamplesBuffer = context.CreateTemporaryBuffer(crackSamplesDesc, TEMP_CRACK_SAMPLES_NAME);

            DataPool.RtDescriptor heightMaskDesc = DataPool.RtDescriptor.Create(resolution, resolution, RenderTextureFormat.RFloat);
            RenderTexture heightMask = context.CreateTemporaryRT(heightMaskDesc, TEMP_HEIGHT_MASK_NAME);

            DataPool.RtDescriptor trailMapDesc = DataPool.RtDescriptor.Create(resolution, resolution, RenderTextureFormat.RFloat);
            RenderTexture trailMap = context.CreateTemporaryRT(trailMapDesc, TEMP_TRAIL_MAP_NAME);
            Drawing.Blit(Texture2D.blackTexture, trailMap);

            DataPool.BufferDescriptor verticesDesc = DataPool.BufferDescriptor.Create(instanceCount * sizeof(float) * 3 * 6);
            ComputeBuffer verticesBuffer = context.CreateTemporaryBuffer(verticesDesc, TEMP_VERTICES_NAME);

            m_helperMaterial = new Material(ShaderUtilities.Find(HELPER_SHADER_NAME));
            m_helperMaterial.SetTexture(HEIGHT_MAP, inputHeightTexture);
            m_helperMaterial.SetFloat(MIN_ANGLE, 0 * Mathf.Deg2Rad);
            m_helperMaterial.SetFloat(MAX_ANGLE, m_angleLimit * Mathf.Deg2Rad);
            m_helperMaterial.SetFloat(NOISE_SCALE, Mathf.Max(0.2f, (1 - m_smoothness) * 100));

            Vector4 worldBounds = context.GetArg(Args.WORLD_BOUNDS).vectorValue;
            float terrainHeight = context.GetArg(Args.TERRAIN_HEIGHT).floatValue;
            Vector3 terrainSize = new Vector3(worldBounds.z, terrainHeight, worldBounds.w);
            m_helperMaterial.SetVector(TERRAIN_SIZE, terrainSize);

            m_computeShader = Resources.Load<ComputeShader>(COMPUTE_SHADER_NAME);
            m_computeShader.SetBuffer(KERNEL_INIT, INPUT_POSITIONS, inputPositionBuffer);
            m_computeShader.SetBuffer(KERNEL_INIT, CRACK_SAMPLES, crackSamplesBuffer);
            m_computeShader.SetTexture(KERNEL_INIT, HEIGHT_MASK, inputHeightTexture);
            m_computeShader.SetVector(HEIGHT_MASK_TEXEL_SIZE, inputHeightTexture.texelSize);

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

            m_computeShader.SetBuffer(KERNEL_SIM, CRACK_SAMPLES, crackSamplesBuffer);
            m_computeShader.SetTexture(KERNEL_SIM, HEIGHT_MASK, heightMask);
            m_computeShader.SetVector(HEIGHT_MASK_TEXEL_SIZE, heightMask.texelSize);
            m_computeShader.SetTexture(KERNEL_SIM, TRAIL_MAP, trailMap);
            m_computeShader.SetFloat(TRAIL_MAP_RESOLUTION, resolution);
            m_computeShader.SetFloat(SMOOTHNESS, m_smoothness);
            m_computeShader.SetFloat(LENGTH, m_length);

            m_computeShader.SetBuffer(KERNEL_VERTEX_GEN, VERTICES, verticesBuffer);
            m_computeShader.SetBuffer(KERNEL_VERTEX_GEN, CRACK_SAMPLES, crackSamplesBuffer);
            m_computeShader.SetFloat(TRAIL_WIDTH, m_width);

            m_trailMaterial = new Material(ShaderUtilities.Find(TRAIL_SHADER_NAME));
            m_trailMaterial.SetBuffer(VERTICES, verticesBuffer);

            for (int i = 0; i < m_iterationCount; ++i)
            {
                float f = i * 1.0f / m_iterationCount;
                m_helperMaterial.SetVector(NOISE_OFFSET, Vector2.one * i);
                Drawing.DrawQuad(heightMask, m_helperMaterial, PASS_HEIGHT_MASK);

                m_computeShader.SetFloat(ITERATION, i);
                m_computeShader.SetFloat(TRAIL_INTENSITY, 1f - Mathf.Pow(f * 2 - 1, 2));

                totalThreadGroupX = (instanceCount + THREAD_PER_GROUP - 1) / THREAD_PER_GROUP;
                pass = (totalThreadGroupX + MAX_THREAD_GROUP - 1) / MAX_THREAD_GROUP;
                for (int j = 0; j < pass; ++j)
                {
                    int threadGroupX = Mathf.Min(MAX_THREAD_GROUP, totalThreadGroupX);
                    totalThreadGroupX -= MAX_THREAD_GROUP;
                    int baseIndex = j * MAX_THREAD_GROUP * THREAD_PER_GROUP;
                    m_computeShader.SetInt(BASE_INDEX, baseIndex);
                    m_computeShader.Dispatch(KERNEL_SIM, threadGroupX, 1, 1);
                    m_computeShader.Dispatch(KERNEL_VERTEX_GEN, threadGroupX, 1, 1);
                }

                RenderTexture.active = trailMap;
                GL.PushMatrix();
                m_trailMaterial.SetPass(0);
                GL.LoadOrtho();
                UnityEngine.Graphics.DrawProceduralNow(MeshTopology.Triangles, instanceCount * 6);
                GL.PopMatrix();
                RenderTexture.active = null;
            }

            SlotRef outputHeightRef = new SlotRef(m_id, outputHeightSlot.id);
            DataPool.RtDescriptor outputHeightDesc = DataPool.RtDescriptor.Create(resolution, resolution, RenderTextureFormat.RFloat);
            RenderTexture outputHeightMap = context.CreateRenderTarget(outputHeightDesc, outputHeightRef);

            m_helperMaterial.SetTexture(HEIGHT_MAP, inputHeightTexture);
            m_helperMaterial.SetTexture(TRAIL_MAP, trailMap);
            m_helperMaterial.SetFloat(TRAIL_DEPTH, m_depth);
            Drawing.DrawQuad(outputHeightMap, m_helperMaterial, PASS_OUTPUT_HEIGHT);

            SlotRef outputCrackRef = new SlotRef(m_id, crackSlot.id);
            if (context.GetReferenceCount(outputCrackRef) > 0)
            {
                DataPool.RtDescriptor outputCrackDesc = DataPool.RtDescriptor.Create(resolution, resolution, RenderTextureFormat.RFloat);
                RenderTexture outputCrackMap = context.CreateRenderTarget(outputCrackDesc, outputCrackRef);
                Drawing.Blit(trailMap, outputCrackMap);
            }

            context.ReleaseReference(positionRefLink);
            context.ReleaseReference(inputHeightRefLink);
            context.ReleaseTemporary(TEMP_CRACK_SAMPLES_NAME);
            context.ReleaseTemporary(TEMP_VELOCITY_NAME);
            context.ReleaseTemporary(TEMP_HEIGHT_MASK_NAME);
            context.ReleaseTemporary(TEMP_TRAIL_MAP_NAME);
            context.ReleaseTemporary(TEMP_VERTICES_NAME);

            Object.DestroyImmediate(m_helperMaterial);
            Object.DestroyImmediate(m_trailMaterial);
            Resources.UnloadAsset(m_computeShader);
        }

        public override void Bypass(GraphContext context)
        {
            SlotRef inputRefLink = context.GetInputLink(m_id, inputHeightSlot.id);
            string varName = inputRefLink.ToString();

            SlotRef outputRef = new SlotRef(m_id, outputHeightSlot.id);
            if (!string.IsNullOrEmpty(varName))
            {
                if (!context.HasVariable(varName))
                {
                    context.SetVariable(varName, inputRefLink);
                }
                context.LinkToVariable(outputRef, varName);
            }
            else
            {
                context.LinkToInvalid(outputRef);
            }
        }
    }
}
#endif
