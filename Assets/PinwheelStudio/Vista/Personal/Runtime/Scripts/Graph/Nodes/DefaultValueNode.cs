#if VISTA
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "Default Value",
        path = "General/Default Value",
        icon = "",
        keywords = "",
        documentation = "",
        description = "Switch to a default texture/buffer if the provided input is null.")]
    public class DefaultValueNode : ExecutableNodeBase, ISerializationCallbackReceiver, IHasDynamicSlotCount
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

        [SerializeField]
        private Serializer.JsonObject m_inputSlotData;

        [System.NonSerialized]
        private ISlot m_defaultValueSlot;
        public ISlot defaultValueSlot
        {
            get
            {
                return m_defaultValueSlot;
            }
        }

        [SerializeField]
        private Serializer.JsonObject m_defaultValueSlotData;

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
        private Serializer.JsonObject m_outputSlotData;

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

        public DefaultValueNode() : base()
        {
            m_slotType = typeof(MaskSlot);
            CreateSlot();
        }

        public DefaultValueNode(Type slotType) : base()
        {
            m_slotType = slotType;
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
            m_inputSlot = SlotProvider.Create(m_slotType, "Input", SlotDirection.Input, 0);
            m_defaultValueSlot = SlotProvider.Create(m_slotType, "Default Value", SlotDirection.Input, 1);
            m_outputSlot = SlotProvider.Create(m_slotType, "Output", SlotDirection.Output, 100);
        }

        public override void ExecuteImmediate(GraphContext context)
        {
            SlotRef inputRefLink = context.GetInputLink(m_id, m_inputSlot.id);
            if (IsInputNull(context, inputRefLink))
            {
                inputRefLink = context.GetInputLink(m_id, m_defaultValueSlot.id);
            }
            string varName = inputRefLink.ToString();

            SlotRef outputRef = new SlotRef(m_id, m_outputSlot.id);
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

        private bool IsInputNull(GraphContext context, SlotRef inputRefLink)
        {
            if (slotType.Equals(typeof(BufferSlot)))
            {
                return context.GetBuffer(inputRefLink) == null;
            }
            else
            {
                return context.GetTexture(inputRefLink) == null;
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

            if (m_defaultValueSlot != null)
            {
                m_defaultValueSlotData = Serializer.Serialize<ISlot>(m_defaultValueSlot);
            }
            else
            {
                m_defaultValueSlotData = default;
            }

            if (m_outputSlot != null)
            {
                m_outputSlotData = Serializer.Serialize<ISlot>(m_outputSlot);
            }
            else
            {
                m_outputSlotData = default;
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

            if (!m_defaultValueSlotData.Equals(default))
            {
                m_defaultValueSlot = Serializer.Deserialize<ISlot>(m_defaultValueSlotData);
            }
            else
            {
                m_defaultValueSlot = null;
            }

            if (!m_outputSlotData.Equals(default))
            {
                m_outputSlot = Serializer.Deserialize<ISlot>(m_outputSlotData);
            }
            else
            {
                m_outputSlot = null;
            }

            if (!string.IsNullOrEmpty(m_slotTypeName))
            {
                m_slotType = Type.GetType(m_slotTypeName);
            }
        }

        public override ISlot[] GetInputSlots()
        {
            List<ISlot> inputSlots = new List<ISlot>();
            if (m_inputSlot != null)
            {
                inputSlots.Add(m_inputSlot);
            }
            if (m_defaultValueSlot != null)
            {
                inputSlots.Add(m_defaultValueSlot);
            }
            return inputSlots.ToArray();
        }

        public override ISlot[] GetOutputSlots()
        {
            if (m_outputSlot != null)
            {
                return new ISlot[] { m_outputSlot };
            }
            else
            {
                return new ISlot[] { };
            }
        }

        public override ISlot GetSlot(int id)
        {
            if (m_inputSlot != null && m_inputSlot.id == id)
            {
                return m_inputSlot;
            }
            if (m_defaultValueSlot != null && m_defaultValueSlot.id == id)
            {
                return m_defaultValueSlot;
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
