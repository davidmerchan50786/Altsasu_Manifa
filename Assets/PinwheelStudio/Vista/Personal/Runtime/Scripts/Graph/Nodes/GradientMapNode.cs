#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;
using Pinwheel.Vista.Graph;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "Gradient Map",
        path = "General/Gradient Map",
        icon = "",
        keywords = "",
        documentation = "",
        description = "Create a color texture from a mask by mapping it to a gradient. Useful when you want to do something such as 'color by height' or 'color by normal' texturing.")]
    public class GradientMapNode : ImageNodeBase
    {
        public readonly MaskSlot inputMaskSlot = new MaskSlot("Mask", SlotDirection.Input, 0);
        public readonly ColorTextureSlot outputSlot = new ColorTextureSlot("Output", SlotDirection.Output, 100);

        [SerializeField]
        private Gradient m_gradient;
        public Gradient gradient
        {
            get
            {
                return m_gradient;
            }
            set
            {
                m_gradient = value;
            }
        }

        [SerializeField]
        private TextureWrapMode m_wrapMode;
        public TextureWrapMode wrapMode
        {
            get
            {
                return m_wrapMode;
            }
            set
            {
                m_wrapMode = value;
            }
        }

        [SerializeField]
        private float m_loop;
        public float loop
        {
            get
            {
                return m_loop;
            }
            set
            {
                m_loop = Mathf.Max(0.01f, value);
            }
        }

        private static readonly string SHADER_NAME = "Hidden/Vista/Graph/GradientMap";
        private static readonly int MASK_TEX = Shader.PropertyToID("_MaskTex");
        private static readonly int GRADIENT_TEX = Shader.PropertyToID("_GradientTex");
        private static readonly int LOOP = Shader.PropertyToID("_Loop");
        private static readonly int PASS = 0;

        private Material m_material;

        public GradientMapNode() : base()
        {
            m_gradient = Utilities.CreateGradient(Color.black, Color.white);
            m_wrapMode = TextureWrapMode.Clamp;
            m_loop = 1;
        }

        public override void ExecuteImmediate(GraphContext context)
        {
            int baseResolution = context.GetArg(Args.RESOLUTION).intValue;
            SlotRef inputRefLink = context.GetInputLink(m_id, inputMaskSlot.id);
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
            DataPool.RtDescriptor desc = DataPool.RtDescriptor.Create(resolution, resolution, RenderTextureFormat.ARGB32);
            SlotRef outputRef = new SlotRef(m_id, outputSlot.id);
            RenderTexture targetRt = context.CreateRenderTarget(desc, outputRef);

            Texture2D gradientTexture = Utilities.TextureFromGradient(m_gradient);
            gradientTexture.wrapMode = m_wrapMode;

            m_material = new Material(ShaderUtilities.Find(SHADER_NAME));
            m_material.SetTexture(MASK_TEX, inputTexture);
            m_material.SetTexture(GRADIENT_TEX, gradientTexture);
            m_material.SetFloat(LOOP, m_loop);

            Drawing.DrawQuad(targetRt, m_material, PASS);
            context.ReleaseReference(inputRefLink);

            Object.DestroyImmediate(gradientTexture);
            Object.DestroyImmediate(m_material);
        }
    }
}
#endif
