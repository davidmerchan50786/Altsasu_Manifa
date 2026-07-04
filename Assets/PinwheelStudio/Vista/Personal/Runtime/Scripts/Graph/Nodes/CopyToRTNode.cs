#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;
using Pinwheel.Vista.Graph;
using Type = System.Type;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "Copy To RT",
        description = "Copy texture content (interpolated) to a Render Texture asset",
        path = "IO/Copy To RT")]
    public class CopyToRTNode : ExecutableNodeBase, ISerializationCallbackReceiver, IHasDynamicSlotCount
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

        [SerializeAsset]
        private RenderTexture m_rtAsset;
        public RenderTexture rtAsset
        {
            get
            {
                return m_rtAsset;
            }
            set
            {
                m_rtAsset = value;
            }
        }
        public CopyToRTNode() : base()
        {
            m_slotType = typeof(MaskSlot);
            CreateSlot();
        }
        public void SetSlotType(Type t)
        {
            if (t != typeof(MaskSlot) &&
                t != typeof(ColorTextureSlot))
            {
                throw new System.ArgumentException($"Fail to set slot type, only {typeof(MaskSlot).Name} and {typeof(ColorTextureSlot).Name} are accepted.");
            }

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
            int id = 0;
            m_inputSlot = SlotProvider.Create(m_slotType, "Input", SlotDirection.Input, id);
        }
        public override void ExecuteImmediate(GraphContext context)
        {
            if (rtAsset == null)
                return;

            SlotRef inputRefLink = context.GetInputLink(m_id, inputSlot.id);
            Texture inputTexture = context.GetTexture(inputRefLink);
            if (inputTexture == null)
            {
                inputTexture = Texture2D.blackTexture;
            }

            Drawing.Blit(inputTexture, rtAsset);
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
            return new ISlot[] { };
        }
        public override ISlot GetSlot(int id)
        {
            if (m_inputSlot != null && m_inputSlot.id == id)
            {
                return m_inputSlot;
            }
            return null;
        }
    }
}
#endif


