#if VISTA
using System;
using UnityEngine;
using Pinwheel.Vista.Graphics;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "Set Variable",
        path = "IO/Set Variable",
        icon = "",
        documentation = "",
        keywords = "var",
        description = "Register a local variable in the graph.\nUse in parallel with Get Variable node to stay organized.")]
    public class SetVariableNode : ExecutableNodeBase, ISerializationCallbackReceiver, IHasDynamicSlotCount
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
        private string m_varName;
        public string varName
        {
            get
            {
                return m_varName;
            }
            set
            {
                m_varName = value;
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
        public SetVariableNode() : base()
        {
            m_slotType = typeof(MaskSlot);
            CreateSlot();
        }
        
        public void SetSlotType(Type t)
        {
            Type oldValue = m_slotType;
            Type newValue = t;
            m_slotType = newValue;
            if (oldValue != newValue)
            {
                CreateSlot();
                if (slotsChanged != null)
                {
                    slotsChanged.Invoke(this);
                }
            }
        }

        private void CreateSlot()
        {
            m_inputSlot = SlotProvider.Create(m_slotType, m_varName, SlotDirection.Input, 0);
            // Output slot is a passthrough for viewport display. Empty name keeps the UI clean.
            m_outputSlot = SlotProvider.Create(m_slotType, string.Empty, SlotDirection.Output, 100);
        }

        public override void ExecuteImmediate(GraphContext context)
        {
            if (!string.IsNullOrEmpty(m_varName))
            {
                SlotRef inputRefLink = context.GetInputLink(m_id, inputSlot.id);
                context.SetVariable(m_varName, inputRefLink);

                SlotRef outputRef = new SlotRef(m_id, m_outputSlot.id);
                bool shouldCopy = context.IsTargetNode(m_id) || context.GetReferenceCount(outputRef) > 0;
                if (shouldCopy && !inputRefLink.Equals(SlotRef.invalid))
                {
                    if (m_outputSlot is BufferSlot)
                    {
                        ComputeBuffer sourceBuffer = context.GetBuffer(inputRefLink);
                        if (sourceBuffer != null)
                        {
                            DataPool.BufferDescriptor desc = DataPool.BufferDescriptor.Create(sourceBuffer.count);
                            ComputeBuffer outputBuffer = context.CreateBuffer(desc, outputRef, false);
                            BufferHelper.Copy(sourceBuffer, outputBuffer);
                        }
                    }
                    else
                    {
                        RenderTexture sourceTexture = context.GetTexture(inputRefLink);
                        if (sourceTexture != null)
                        {
                            DataPool.RtDescriptor desc = DataPool.RtDescriptor.Create(sourceTexture.width, sourceTexture.height, sourceTexture.format);
                            RenderTexture outputTexture = context.CreateRenderTarget(desc, outputRef, false);
                            Drawing.Blit(sourceTexture, outputTexture);
                        }
                    }
                }
            }
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

            // Output slot is not serialized. It is always recreated here so that existing graphs
            // saved before this slot existed load without errors.
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


