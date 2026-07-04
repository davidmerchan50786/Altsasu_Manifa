#if VISTA
using Pinwheel.Vista.Graphics;
using UnityEngine;
using UnityGraphics = UnityEngine.Graphics;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "Flatten At",
        path = "General/Flatten At",
        icon = "",
        documentation = "",
        keywords = "",
        description = "Flatten the geometry at specific locations.\nUseful when you want some flat areas to spawn game objects onto.")]
    public class FlattenAtNode : ImageNodeBase
    {
        public readonly MaskSlot inputHeightSlot = new MaskSlot("Height", SlotDirection.Input, 0);
        public readonly BufferSlot inputPositionSlot = new BufferSlot("Positions", SlotDirection.Input, 1);
        public readonly MaskSlot maskSlot = new MaskSlot("Mask", SlotDirection.Input, 2);
        public readonly MaskSlot rotationSlot = new MaskSlot("Rotation", SlotDirection.Input, 3);
        public readonly MaskSlot scaleSlot = new MaskSlot("Scale", SlotDirection.Input, 4);

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

        private static readonly string SHADER_NAME = "Hidden/Vista/Graph/FlattenAt";
        private static readonly int VERTICES = Shader.PropertyToID("_Vertices");
        private static readonly int TEXCOORD = Shader.PropertyToID("_Texcoords");
        private static readonly int HAS_MASK_MAP = Shader.PropertyToID("_HasMaskMap");
        private static readonly int MASK = Shader.PropertyToID("_Mask");
        private static readonly int TARGET_HEIGHTS = Shader.PropertyToID("_TargetHeights");

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
        //private static readonly string KW_HAS_MASK_MAP = "HAS_MASK_MAP";

        private static readonly string TEMP_VERTICES_BUFFER_NAME = "TempQuadVertices";
        private static readonly string TEMP_TEXCOORDS_BUFFER_NAME = "TempQuadTexcoords";
        private static readonly string TEMP_TARGET_HEIGHTS_BUFFER_NAME = "TempTargetHeights";

        private static readonly int PASS = 0;

        private Material m_material;
        private ComputeShader m_quadGenShader;
        private ComputeShader m_maskSamplerShader;

        public FlattenAtNode() : base()
        {
            m_minRotation = -360f;
            m_maxRotation = 360f;
            m_rotationMultiplier = 0f;

            m_size = 0.1f;
            m_scaleMultiplier = 1;
            m_sizeInWorldSpace = false;
        }

        public override void ExecuteImmediate(GraphContext context)
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

            SlotRef inputPositionRefLink = context.GetInputLink(m_id, inputPositionSlot.id);
            ComputeBuffer inputPositionBuffer = context.GetBuffer(inputPositionRefLink);

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

            int resolution = this.CalculateResolution(baseResolution, inputResolution);
            DataPool.RtDescriptor desc = DataPool.RtDescriptor.Create(resolution, resolution, RenderTextureFormat.RFloat);
            SlotRef outputRef = new SlotRef(m_id, outputSlot.id);
            RenderTexture targetRt = context.CreateRenderTarget(desc, outputRef);
            Drawing.Blit(inputHeightTexture, targetRt);

            if (inputPositionBuffer == null)
            {

            }
            else if (inputPositionBuffer.count % PositionSample.SIZE != 0)
            {
                Debug.LogError($"Unable to parse {inputPositionSlot.name} buffer, node id {m_id}");
            }
            else
            {
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
                    m_material.SetInteger(HAS_MASK_MAP, 1);
                }
                else
                {
                    m_material.SetInteger(HAS_MASK_MAP, 0);
                }

                DataPool.BufferDescriptor targetHeightsDesc = DataPool.BufferDescriptor.Create(instanceCount);
                ComputeBuffer targetHeightsBuffer = context.CreateTemporaryBuffer(targetHeightsDesc, TEMP_TARGET_HEIGHTS_BUFFER_NAME);

                m_maskSamplerShader = Resources.Load<ComputeShader>(MASK_SAMPLER_SHADER_NAME);
                m_maskSamplerShader.SetBuffer(KERNEL, POSITIONS, inputPositionBuffer);
                m_maskSamplerShader.SetBuffer(KERNEL, DATA, targetHeightsBuffer);
                m_maskSamplerShader.SetTexture(KERNEL, MAIN_TEX, inputHeightTexture);

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

                m_material.SetTexture(MAIN_TEX, inputHeightTexture);
                m_material.SetBuffer(TARGET_HEIGHTS, targetHeightsBuffer);

                RenderTexture.active = targetRt;
                GL.PushMatrix();
                m_material.SetPass(PASS);
                GL.LoadOrtho();
                UnityGraphics.DrawProceduralNow(MeshTopology.Triangles, instanceCount * vertexPerQuad);
                GL.PopMatrix();
                RenderTexture.active = null;

                Object.DestroyImmediate(m_material);
                Resources.UnloadAsset(m_quadGenShader);
                Resources.UnloadAsset(m_maskSamplerShader);
            }

            context.ReleaseReference(inputHeightRefLink);
            context.ReleaseReference(inputPositionRefLink);
            context.ReleaseReference(maskRefLink);
            context.ReleaseReference(rotationRefLink);
            context.ReleaseTemporary(TEMP_VERTICES_BUFFER_NAME);
            context.ReleaseTemporary(TEMP_TEXCOORDS_BUFFER_NAME);
            context.ReleaseTemporary(TEMP_TARGET_HEIGHTS_BUFFER_NAME);
        }
    }
}
#endif
