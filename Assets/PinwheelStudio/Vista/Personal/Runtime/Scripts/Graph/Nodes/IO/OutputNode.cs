#if VISTA
using Pinwheel.Vista.Graphics;
using System;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "Output",
        path = "IO/Graph Output",
        icon = "",
        documentation = "",
        keywords = "",
        description = "A generic exit point of the graph which can be used to take out data with sub-graph or in code.\nThis will create a correspond slot in the sub-graph node.")]
    public class OutputNode : ExecutableNodeBase, IOutputNode, IHasDynamicSlotCount, ISerializationCallbackReceiver
    {
        [System.NonSerialized]
        private ISlot m_inputSlot;
        public ISlot inputSlot
        {
            get
            {
                return m_inputSlot;
            }
        }

        [System.NonSerialized]
        private ISlot m_outputSlot;
        public ISlot outputSlot
        {
            get
            {
                return m_outputSlot;
            }
        }

        [SerializeField]
        private Serializer.JsonObject m_inputSlotData;

        [SerializeField]
        private string m_outputName;
        public string outputName
        {
            get
            {
                return m_outputName;
            }
            set
            {
                m_outputName = value;
            }
        }

        [System.NonSerialized]
        private Type m_slotType;
        public Type slotType
        {
            get
            {
                return m_slotType;
            }
        }

        [SerializeField]
        private string m_slotTypeName;
        public event IHasDynamicSlotCount.SlotsChangedHandler slotsChanged;
        public SlotRef mainOutputSlot
        {
            get
            {
                return new SlotRef(m_id, outputSlot.id);
            }
        }
        public override bool isBypassed
        {
            get
            {
                return false;
            }
            set
            {
                m_isBypassed = false;
            }
        }
        public OutputNode() : base()
        {
            m_slotType = typeof(MaskSlot);
            CreateInputSlot();
        }
        public void SetSlotType(Type t)
        {
            Type oldValue = m_slotType;
            Type newValue = t;
            m_slotType = newValue;
            if (oldValue != newValue)
            {
                CreateInputSlot();
                if (slotsChanged != null)
                {
                    slotsChanged.Invoke(this);
                }
            }
        }

        private void CreateInputSlot()
        {
            m_inputSlot = SlotProvider.Create(m_slotType, m_outputName, SlotDirection.Input, 0);
            // Output slot is a passthrough for viewport display. Empty name keeps the UI clean.
            m_outputSlot = SlotProvider.Create(m_slotType, string.Empty, SlotDirection.Output, 100);
        }
        public override void ExecuteImmediate(GraphContext context)
        {
            SlotRef inputRefLink = context.GetInputLink(m_id, inputSlot.id);
            if (m_slotType == typeof(BufferSlot))
            {
                ComputeBuffer buffer = context.GetBuffer(inputRefLink);
                if (buffer != null)
                {
                    SlotRef outputRef = new SlotRef(m_id, outputSlot.id);
                    DataPool.BufferDescriptor desc = DataPool.BufferDescriptor.Create(buffer.count);
                    ComputeBuffer targetBuffer = context.CreateBuffer(desc, outputRef);
                    BufferHelper.Copy(buffer, targetBuffer);
                }
            }
            else
            {
                RenderTexture inputTexture = context.GetTexture(inputRefLink);
                if (inputTexture != null)
                {
                    SlotRef outputRef = new SlotRef(m_id, outputSlot.id);
                    DataPool.RtDescriptor desc = DataPool.RtDescriptor.Create(inputTexture.width, inputTexture.height, inputTexture.format);
                    RenderTexture targetRT = context.CreateRenderTarget(desc, outputRef);
                    Drawing.Blit(inputTexture, targetRT);
                }
            }

            context.ReleaseReference(inputRefLink);
        }
        public void OnBeforeSerialize()
        {
            if (m_inputSlot != null)
            {
                m_inputSlotData = Serializer.Serialize<ISlot>(m_inputSlot);
            }
            else
            {
                m_inputSlotData = default;
            }

            if (m_slotType != null)
            {
                m_slotTypeName = m_slotType.FullName;
            }
        }
        public void OnAfterDeserialize()
        {
            if (!m_inputSlotData.Equals(default))
            {
                m_inputSlot = Serializer.Deserialize<ISlot>(m_inputSlotData);
            }
            else
            {
                m_inputSlot = null;
            }

            if (!string.IsNullOrEmpty(m_slotTypeName))
            {
                m_slotType = Type.GetType(m_slotTypeName);
            }

            // Output slot is not serialized. Always recreate it here so graphs saved before
            // this slot existed load without errors.
            if (m_slotType != null)
            {
                m_outputSlot = SlotProvider.Create(m_slotType, string.Empty, SlotDirection.Output, 100);
            }
        }
        public override ISlot[] GetInputSlots()
        {
            if (m_inputSlot != null)
            {
                return new ISlot[] { m_inputSlot };
            }
            else
            {
                return new ISlot[] { };
            }
        }
        public override ISlot[] GetOutputSlots()
        {
            if (m_outputSlot != null)
            {
                return new ISlot[] { m_outputSlot };
            }
            return new ISlot[] { };
        }
        public override ISlot GetSlot(int id)
        {
            if (m_inputSlot != null && m_inputSlot.id == id)
            {
                return m_inputSlot;
            }
            if (m_outputSlot != null && m_outputSlot.id == id)
            {
                return m_outputSlot;
            }
            return null;
        }
    }
}
#endif


