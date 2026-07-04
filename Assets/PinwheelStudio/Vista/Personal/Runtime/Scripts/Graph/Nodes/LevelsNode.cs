#if VISTA
using Pinwheel.Vista.Graphics;
using System.Collections;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "Levels",
        path = "Adjustments/Levels",
        icon = "",
        documentation = "",
        keywords = "level, bright, dark",
        description = "Apply a simple levels filter to the image.")]
    public class LevelsNode : ImageNodeBase
    {
        public readonly MaskSlot inputSlot = new MaskSlot("Input", SlotDirection.Input, 0);
        public readonly MaskSlot outputSlot = new MaskSlot("Output", SlotDirection.Output, 100);

        [SerializeField]
        private float m_inLow;
        public float inLow
        {
            get
            {
                return m_inLow;
            }
            set
            {
                float v = value;
                v = Mathf.Clamp01(Mathf.Min(v, m_inHigh));
                m_inLow = v;
            }
        }

        [SerializeField]
        private float m_inMid;
        public float inMid
        {
            get
            {
                return m_inMid;
            }
            set
            {
                m_inMid = Mathf.Clamp01(value);
            }
        }

        [SerializeField]
        private float m_inHigh;
        public float inHigh
        {
            get
            {
                return m_inHigh;
            }
            set
            {
                float v = value;
                v = Mathf.Clamp01(Mathf.Max(v, m_inLow));
                m_inHigh = v;
            }
        }

        [SerializeField]
        private float m_outLow;
        public float outLow
        {
            get
            {
                return m_outLow;
            }
            set
            {
                float v = value;
                v = Mathf.Clamp01(Mathf.Min(v, m_outHigh));
                m_outLow = v;
            }
        }

        [SerializeField]
        private float m_outHigh;
        public float outHigh
        {
            get
            {
                return m_outHigh;
            }
            set
            {
                float v = value;
                v = Mathf.Clamp01(Mathf.Max(v, m_outLow));
                m_outHigh = v;
            }
        }
        public LevelsNode() : base()
        {
            m_inLow = 0f;
            m_inMid = 0.5f;
            m_inHigh = 1f;

            m_outLow = 0f;
            m_outHigh = 1f;
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
            DataPool.RtDescriptor desc = DataPool.RtDescriptor.Create(resolution, resolution);
            SlotRef outputRef = new SlotRef(m_id, outputSlot.id);
            RenderTexture targetRt = context.CreateRenderTarget(desc, outputRef);

            NodeLibraryUtilities.LevelsNode.Execute(inputTexture, targetRt, inLow, inMid, inHigh, outLow, outHigh);

            context.ReleaseReference(inputRefLink);
        }
        public override IEnumerator Execute(GraphContext context)
        {
            ExecuteImmediate(context);
            yield return null;
        }
    }
}
#endif


