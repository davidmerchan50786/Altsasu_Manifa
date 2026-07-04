#if VISTA
using Pinwheel.Vista.Graphics;
using System.Collections;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "Smooth",
        path = "Adjustments/Smooth",
        icon = "",
        documentation = "",
        keywords = "smooth, blur",
        description = "Gradually smooth out the image.")]
    public class SmoothNode : ImageNodeBase
    {
        public readonly MaskSlot inputSlot = new MaskSlot("Input", SlotDirection.Input, 0);
        public readonly MaskSlot maskSlot = new MaskSlot("Mask", SlotDirection.Input, 1);
        public readonly MaskSlot outputSlot = new MaskSlot("Output", SlotDirection.Output, 100);

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
                return m_iterationPerFrame;
            }
            set
            {
                m_iterationPerFrame = Mathf.Max(1, value);
            }
        }

        private static readonly string TEMP_TEXTURE_NAME = "SmoothTemp";

        private struct Textures
        {
            public RenderTexture targetRt;
            public RenderTexture tmpRt;
        }
        public SmoothNode() : base()
        {
            m_iterationCount = 5;
            m_iterationPerFrame = 10;
        }

        private Textures CreateTextures(GraphContext context)
        {
            int baseResolution = context.GetArg(Args.RESOLUTION).intValue;
            SlotRef inputRefLink = context.GetInputLink(m_id, inputSlot.id);
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
            DataPool.RtDescriptor desc = DataPool.RtDescriptor.Create(resolution, resolution);
            SlotRef outputRef = new SlotRef(m_id, outputSlot.id);
            RenderTexture targetRt = context.CreateRenderTarget(desc, outputRef);
            RenderTexture tmpRt = context.CreateTemporaryRT(desc, TEMP_TEXTURE_NAME + m_id);
            Drawing.Blit(inputTexture, targetRt);

            Textures textures = new Textures();
            textures.targetRt = targetRt;
            textures.tmpRt = tmpRt;
            return textures;
        }

        private Texture GetMaskTexture(GraphContext context)
        {
            SlotRef maskRefLink = context.GetInputLink(m_id, maskSlot.id);
            if (maskRefLink.Equals(SlotRef.invalid))
            {
                return Texture2D.whiteTexture;
            }
            
            return context.GetTexture(maskRefLink);
        }

        private void CleanUp(GraphContext context)
        {
            SlotRef inputRefLink = context.GetInputLink(m_id, inputSlot.id);
            SlotRef maskRefLink = context.GetInputLink(m_id, maskSlot.id);

            context.ReleaseReference(inputRefLink);
            context.ReleaseReference(maskRefLink);
            context.ReleaseTemporary(TEMP_TEXTURE_NAME + m_id);
        }
        public override IEnumerator Execute(GraphContext context)
        {
            Textures textures = CreateTextures(context);
            Texture maskTexture = GetMaskTexture(context);
            IEnumerator routine = VistaLib.SmoothProgressive(textures.targetRt, textures.tmpRt, maskTexture, m_iterationCount, m_iterationPerFrame);
            while (routine.MoveNext())
            {
                yield return routine.Current;
            }
            CleanUp(context);
        }
        public override void ExecuteImmediate(GraphContext context)
        {
            Textures textures = CreateTextures(context);
            Texture maskTexture = GetMaskTexture(context);
            VistaLib.Smooth(textures.targetRt, textures.tmpRt, maskTexture, m_iterationCount);
            CleanUp(context);
        }
    }
}
#endif


