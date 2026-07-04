#if VISTA
using Pinwheel.Vista.Graphics;
using System.Collections;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "Convex",
        path = "Masking/Convex",
        icon = "",
        documentation = "",
        keywords = "",
        description = "Highlight the pixels that are higher than their neighbors.")]
    public class ConvexNode : ImageNodeBase
    {
        public readonly MaskSlot inputSlot = new MaskSlot("Height", SlotDirection.Input, 0);
        public readonly MaskSlot outputSlot = new MaskSlot("Output", SlotDirection.Output, 100);

        [SerializeField]
        private float m_epsilon;
        public float epsilon
        {
            get
            {
                return m_epsilon;
            }
            set
            {
                m_epsilon = Mathf.Clamp(value, -1, 1);
            }
        }

        [SerializeField]
        private int m_tolerance;
        public int tolerance
        {
            get
            {
                return m_tolerance;
            }
            set
            {
                m_tolerance = Mathf.Clamp(value, 0, 7);
            }
        }
        public ConvexNode() : base()
        {
            m_epsilon = 0;
            m_tolerance = 3;
        }
        public override IEnumerator Execute(GraphContext context)
        {
            ExecuteImmediate(context);
            yield return null;
        }
        public override void ExecuteImmediate(GraphContext context)
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
            SlotRef outputRef = new SlotRef(m_id, outputSlot.id);
            DataPool.RtDescriptor desc = DataPool.RtDescriptor.Create(resolution, resolution, RenderTextureFormat.RFloat);
            RenderTexture targetRt = context.CreateRenderTarget(desc, outputRef);

            NodeLibraryUtilities.ConvexNode.Execute(inputTexture, targetRt, epsilon, tolerance);            
            context.ReleaseReference(inputRefLink);
        }
        public override void Bypass(GraphContext context)
        {
            return;
        }
    }
}
#endif


