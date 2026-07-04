#if VISTA
using System.Collections;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "Remap",
        path = "Adjustments/Remap",
        icon = "",
        documentation = "",
        keywords = "remap, normalize, auto level",
        description = "Remap the image value to an output range. Similar to normalize & auto levels filter.\nUseful when you want to brighten up a very dark image such as erosion/deposition data.")]
    public class RemapNode : ImageNodeBase
    {
        public readonly MaskSlot inputSlot = new MaskSlot("Input", SlotDirection.Input, 0);
        public readonly MaskSlot outputSlot = new MaskSlot("Output", SlotDirection.Output, 100);

        [SerializeField]
        private float m_min;
        public float min
        {
            get
            {
                return m_min;
            }
            set
            {
                m_min = Mathf.Min(value, m_max);
            }
        }

        [SerializeField]
        private float m_max;
        public float max
        {
            get
            {
                return m_max;
            }
            set
            {
                m_max = Mathf.Max(value, m_min);
            }
        }

        public RemapNode() : base()
        {
            m_min = 0;
            m_max = 1;
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
            DataPool.RtDescriptor desc = DataPool.RtDescriptor.Create(resolution, resolution);
            SlotRef outputRef = new SlotRef(m_id, outputSlot.id);
            RenderTexture targetRt = context.CreateRenderTarget(desc, outputRef);

            VistaLib.Remap(targetRt, inputTexture, m_min, m_max);

            context.ReleaseReference(inputRefLink);
        }
    }
}
#endif


