#if VISTA
using Pinwheel.Vista.Graphics;
using UnityEngine;
using UnityGraphics = UnityEngine.Graphics;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "Splatter",
        path = "Masking/Splatter",
        icon = "",
        documentation = "",
        keywords = "",
        description = "Stamp a mask at specific positions. Useful when you want to apply different texture where it is a village, a large rock or a large tree, etc.")]
    public class SplatterNode : ImageNodeBase
    {
        public readonly BufferSlot inputPositionSlot = new BufferSlot("Positions", SlotDirection.Input, 0);
        public readonly MaskSlot maskSlot = new MaskSlot("Mask", SlotDirection.Input, 1);
        public readonly MaskSlot rotationSlot = new MaskSlot("Rotation", SlotDirection.Input, 2);
        public readonly MaskSlot scaleSlot = new MaskSlot("Scale", SlotDirection.Input, 3);
        public readonly MaskSlot intensitySlot = new MaskSlot("Intensity", SlotDirection.Input, 4);

        public readonly MaskSlot outputSlot = new MaskSlot("Output", SlotDirection.Output, 100);

        [SerializeField]
        private float m_size;
        public float size
        {
            get
            {
                return m_size;
            }
            set
            {
                m_size = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        private float m_minRotation;
        public float minRotation
        {
            get
            {
                return m_minRotation;
            }
            set
            {
                m_minRotation = Mathf.Min(value, m_maxRotation);
            }
        }

        [SerializeField]
        private float m_maxRotation;
        public float maxRotation
        {
            get
            {
                return m_maxRotation;
            }
            set
            {
                m_maxRotation = Mathf.Max(value, m_minRotation);
            }
        }

        [SerializeField]
        private float m_rotationMultiplier;
        public float rotationMultiplier
        {
            get
            {
                return m_rotationMultiplier;
            }
            set
            {
                m_rotationMultiplier = Mathf.Clamp01(value);
            }
        }

        [SerializeField]
        private float m_scaleMultiplier;
        public float scaleMultiplier
        {
            get
            {
                return m_scaleMultiplier;
            }
            set
            {
                m_scaleMultiplier = Mathf.Clamp01(value);
            }
        }

        [SerializeField]
        private bool m_sizeInWorldSpace;
        public bool sizeInWorldSpace
        {
            get
            {
                return m_sizeInWorldSpace;
            }
            set
            {
                m_sizeInWorldSpace = value;
            }
        }

        [SerializeField]
        private float m_intensityMultiplier;
        public float intensityMultiplier
        {
            get
            {
                return m_intensityMultiplier;
            }
            set
            {
                m_intensityMultiplier = Mathf.Clamp01(value);
            }
        }

        private static readonly string SHADER_NAME = "Hidden/Vista/Graph/Splatter";
        private static readonly int VERTICES = Shader.PropertyToID("_Vertices");
        private static readonly int TEXCOORD = Shader.PropertyToID("_Texcoords");
        private static readonly int MASK = Shader.PropertyToID("_Mask");
        private static readonly int INTENSITY = Shader.PropertyToID("_Intensity");
        private static readonly int INTENSITY_MULTIPLIER = Shader.PropertyToID("_IntensityMultiplier");
        private static readonly int HAS_MASK_MAP = Shader.PropertyToID("_HasMaskMap");
        private static readonly int HAS_INTENSITY_BUFFER = Shader.PropertyToID("_HasIntensityBuffer");

        private static readonly string QUAD_GEN_SHADER_NAME = "Vista/Shaders/Graph/QuadsFromPoints";
        private static readonly int POSITIONS = Shader.PropertyToID("_Positions");
        private static readonly int QUAD_SIZE = Shader.PropertyToID("_QuadSize");
        private static readonly int MIN_ROTATION = Shader.PropertyToID("_MinRotation");
        private static readonly int MAX_ROTATION = Shader.PropertyToID("_MaxRotation");
        private static readonly int ROTATION_MULTIPLIER = Shader.PropertyToID("_RotationMultiplier");
        private static readonly int ROTATION_MAP = Shader.PropertyToID("_RotationMap");
        private static readonly int SCALE_MULTIPLIER = Shader.PropertyToID("_ScaleMultiplier");
        private static readonly int SCALE_MAP = Shader.PropertyToID("_ScaleMap");
        private static readonly int BASE_INDEX = Shader.PropertyToID("_BaseIndex");

        private static readonly string MASK_SAMPLER_SHADER_NAME = "Vista/Shaders/Graph/MaskSampler";
        private static readonly int MAIN_TEX = Shader.PropertyToID("_MainTex");
        private static readonly int DATA = Shader.PropertyToID("_Data");

        private static readonly int THREAD_PER_GROUP = 8;
        private static readonly int MAX_THREAD_GROUP = 64000 / THREAD_PER_GROUP;

        private static readonly int KERNEL = 0;

        private static readonly string KW_HAS_ROTATION_MAP = "HAS_ROTATION_MAP";
        private static readonly string KW_HAS_SCALE_MAP = "HAS_SCALE_MAP";
        //private static readonly string KW_HAS_INTENSITY_BUFFER = "HAS_INTENSITY_BUFFER";
        //private static readonly string KW_HAS_MASK_MAP = "HAS_MASK_MAP";

        private static readonly string TEMP_VERTICES_BUFFER_NAME = "TempQuadVertices";
        private static readonly string TEMP_TEXCOORDS_BUFFER_NAME = "TempQuadTexcoords";
        private static readonly string TEMP_INTENSITY_BUFFER_NAME = "TempIntensity";

        private static readonly int PASS = 0;
        private Material m_material;
        private ComputeShader m_quadGenShader;
        private ComputeShader m_maskSamplerShader;

        public SplatterNode() : base()
        {
            m_minRotation = -360f;
            m_maxRotation = 360f;
            m_rotationMultiplier = 0f;

            m_size = 0.1f;
            m_scaleMultiplier = 1;
            m_sizeInWorldSpace = false;

            m_intensityMultiplier = 1;
        }

        public override void ExecuteImmediate(GraphContext context)
        {
            SlotRef inputPositionRefLink = context.GetInputLink(m_id, inputPositionSlot.id);
            ComputeBuffer inputPositionBuffer = context.GetBuffer(inputPositionRefLink);
            if (inputPositionBuffer == null)
            {
                return;
            }
            if (inputPositionBuffer.count % PositionSample.SIZE != 0)
            {
                Debug.LogError($"Unable to parse {inputPositionSlot.name} buffer, node id {m_id}");
                return;
            }

            SlotRef maskRefLink = context.GetInputLink(m_id, maskSlot.id);
            Texture maskTexture = context.GetTexture(maskRefLink);
            if (maskTexture == null)
            {
                maskTexture = Texture2D.whiteTexture;
            }

            SlotRef rotationRefLink = context.GetInputLink(m_id, rotationSlot.id);
            Texture rotationTexture = context.GetTexture(rotationRefLink);

            SlotRef scaleRefLink = context.GetInputLink(m_id, scaleSlot.id);
            Texture scaleTexture = context.GetTexture(scaleRefLink);

            SlotRef intensityRefLink = context.GetInputLink(m_id, intensitySlot.id);
            Texture intensityTexture = context.GetTexture(intensityRefLink);

            int baseResolution = context.GetArg(Args.RESOLUTION).intValue;
            int resolution = this.CalculateResolution(baseResolution, baseResolution);
            DataPool.RtDescriptor desc = DataPool.RtDescriptor.Create(resolution, resolution, RenderTextureFormat.RFloat);
            SlotRef outputRef = new SlotRef(m_id, outputSlot.id);
            RenderTexture targetRt = context.CreateRenderTarget(desc, outputRef);
            Drawing.Blit(Texture2D.blackTexture, targetRt);

            int instanceCount = inputPositionBuffer.count / PositionSample.SIZE;
            int vertexPerQuad = 6;

            DataPool.BufferDescriptor verticesDesc = DataPool.BufferDescriptor.Create(instanceCount * vertexPerQuad * 2); //6 vertices, 2 float xy per vertex
            ComputeBuffer verticesBuffer = context.CreateTemporaryBuffer(verticesDesc, TEMP_VERTICES_BUFFER_NAME);

            DataPool.BufferDescriptor texcoordsDesc = DataPool.BufferDescriptor.Create(instanceCount * vertexPerQuad * 2); //6 vertices, 2 float xy per vertex
            ComputeBuffer texcoordsBuffer = context.CreateTemporaryBuffer(texcoordsDesc, TEMP_TEXCOORDS_BUFFER_NAME);

            m_quadGenShader = Resources.Load<ComputeShader>(QUAD_GEN_SHADER_NAME);
            m_quadGenShader.SetBuffer(KERNEL, POSITIONS, inputPositionBuffer);
            m_quadGenShader.SetBuffer(KERNEL, VERTICES, verticesBuffer);
            m_quadGenShader.SetBuffer(KERNEL, TEXCOORD, texcoordsBuffer);

            m_quadGenShader.SetFloat(MIN_ROTATION, m_minRotation * Mathf.Deg2Rad);
            m_quadGenShader.SetFloat(MAX_ROTATION, m_maxRotation * Mathf.Deg2Rad);
            m_quadGenShader.SetFloat(ROTATION_MULTIPLIER, m_rotationMultiplier);
            if (rotationTexture != null)
            {
                m_quadGenShader.EnableKeyword(KW_HAS_ROTATION_MAP);
                m_quadGenShader.SetTexture(KERNEL, ROTATION_MAP, rotationTexture);
            }
            else
            {
                m_quadGenShader.DisableKeyword(KW_HAS_ROTATION_MAP);
            }

            m_quadGenShader.SetFloat(SCALE_MULTIPLIER, m_scaleMultiplier);
            if (scaleTexture != null)
            {
                m_quadGenShader.EnableKeyword(KW_HAS_SCALE_MAP);
                m_quadGenShader.SetTexture(KERNEL, SCALE_MAP, scaleTexture);
            }
            else
            {
                m_quadGenShader.DisableKeyword(KW_HAS_SCALE_MAP);
            }

            float quadSize;
            if (sizeInWorldSpace)
            {
                Vector4 worldBounds = context.GetArg(Args.WORLD_BOUNDS).vectorValue;
                quadSize = m_size / worldBounds.z;
            }
            else
            {
                quadSize = m_size;
            }
            m_quadGenShader.SetFloat(QUAD_SIZE, quadSize);

            int totalThreadGroupX = (instanceCount + THREAD_PER_GROUP - 1) / THREAD_PER_GROUP;
            int iteration = (totalThreadGroupX + MAX_THREAD_GROUP - 1) / MAX_THREAD_GROUP;
            for (int i = 0; i < iteration; ++i)
            {
                int threadGroupX = Mathf.Min(MAX_THREAD_GROUP, totalThreadGroupX);
                totalThreadGroupX -= MAX_THREAD_GROUP;
                int baseIndex = i * MAX_THREAD_GROUP * THREAD_PER_GROUP;
                m_quadGenShader.SetInt(BASE_INDEX, baseIndex);
                m_quadGenShader.Dispatch(KERNEL, threadGroupX, 1, 1);
            }

            m_material = new Material(ShaderUtilities.Find(SHADER_NAME));
            m_material.SetBuffer(VERTICES, verticesBuffer);
            m_material.SetBuffer(TEXCOORD, texcoordsBuffer);

            if (maskTexture != null)
            {
                m_material.SetTexture(MASK, maskTexture);
                m_material.SetInt(HAS_MASK_MAP, 1);
            }
            else
            {
                m_material.SetInt(HAS_MASK_MAP, 0);
            }

            m_material.SetFloat(INTENSITY_MULTIPLIER, m_intensityMultiplier);
            if (intensityTexture != null)
            {
                DataPool.BufferDescriptor intensityDesc = DataPool.BufferDescriptor.Create(instanceCount);
                ComputeBuffer intensityBuffer = context.CreateTemporaryBuffer(intensityDesc, TEMP_INTENSITY_BUFFER_NAME);

                m_maskSamplerShader = Resources.Load<ComputeShader>(MASK_SAMPLER_SHADER_NAME);
                m_maskSamplerShader.SetBuffer(KERNEL, POSITIONS, inputPositionBuffer);
                m_maskSamplerShader.SetBuffer(KERNEL, DATA, intensityBuffer);
                m_maskSamplerShader.SetTexture(KERNEL, MAIN_TEX, intensityTexture);

                totalThreadGroupX = (instanceCount + THREAD_PER_GROUP - 1) / THREAD_PER_GROUP;
                iteration = (totalThreadGroupX + MAX_THREAD_GROUP - 1) / MAX_THREAD_GROUP;
                for (int i = 0; i < iteration; ++i)
                {
                    int threadGroupX = Mathf.Min(MAX_THREAD_GROUP, totalThreadGroupX);
                    totalThreadGroupX -= MAX_THREAD_GROUP;
                    int baseIndex = i * MAX_THREAD_GROUP * THREAD_PER_GROUP;
                    m_maskSamplerShader.SetInt(BASE_INDEX, baseIndex);
                    m_maskSamplerShader.Dispatch(KERNEL, threadGroupX, 1, 1);
                }
                Resources.UnloadAsset(m_maskSamplerShader);

                m_material.SetBuffer(INTENSITY, intensityBuffer);
                m_material.SetInt(HAS_INTENSITY_BUFFER, 1);
            }
            else
            {
                m_material.SetInt(HAS_INTENSITY_BUFFER, 0);
            }

            RenderTexture.active = targetRt;
            GL.PushMatrix();
            m_material.SetPass(PASS);
            GL.LoadOrtho();
            UnityGraphics.DrawProceduralNow(MeshTopology.Triangles, instanceCount * vertexPerQuad);
            GL.PopMatrix();
            RenderTexture.active = null;

            context.ReleaseReference(inputPositionRefLink);
            context.ReleaseReference(maskRefLink);
            context.ReleaseReference(rotationRefLink);
            context.ReleaseTemporary(TEMP_VERTICES_BUFFER_NAME);
            context.ReleaseTemporary(TEMP_TEXCOORDS_BUFFER_NAME);
            context.ReleaseTemporary(TEMP_INTENSITY_BUFFER_NAME);

            Resources.UnloadAsset(m_quadGenShader);
            Object.DestroyImmediate(m_material);
        }
    }
}
#endif
