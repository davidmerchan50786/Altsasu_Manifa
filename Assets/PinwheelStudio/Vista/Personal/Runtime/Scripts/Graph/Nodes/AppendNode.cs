#if VISTA
using System.Collections.Generic;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "Append",
        path = "General/Append",
        icon = "",
        documentation = "",
        keywords = "add, combine",
        description = "Append multiple buffers into one.")]
    public class AppendNode : ExecutableNodeBase, IHasDynamicSlotCount
    {
        [SerializeField]
        private BufferSlot[] m_inputSlots;
        public BufferSlot[] inputSlots
        {
            get
            {
                return m_inputSlots;
            }
        }

        public readonly BufferSlot outputSlot = new BufferSlot("Output", SlotDirection.Output, 100);

        [SerializeField]
        private int m_inputCount;
        [NonExposable]
        public int inputCount
        {
            get
            {
                return m_inputCount;
            }
            set
            {
                int oldValue = m_inputCount;
                int newValue = Mathf.Clamp(value, MIN_INPUT, MAX_INPUT);
                m_inputCount = newValue;
                if (oldValue != newValue)
                {
                    UpdateSlotArrays();
                    if (slotsChanged != null)
                    {
                        slotsChanged.Invoke(this);
                    }
                }
            }
        }

        public const int MIN_INPUT = 2;
        public const int MAX_INPUT = 100;
        public event IHasDynamicSlotCount.SlotsChangedHandler slotsChanged;

        private static readonly string COMPUTE_SHADER_NAME = "Vista/Shaders/Graph/Append";
        private static readonly int SOURCE_BUFFER = Shader.PropertyToID("_SrcBuffer");
        private static readonly int DEST_BUFFER = Shader.PropertyToID("_DestBuffer");
        // Offset within the current source buffer, advanced per dispatch iteration when sample count exceeds a single dispatch.
        private static readonly int BASE_INDEX = Shader.PropertyToID("_BaseIndex");
        // Offset within the destination buffer, advanced per source buffer so each input is written after the previous one.
        private static readonly int DEST_OFFSET = Shader.PropertyToID("_DestOffset");
        private static readonly int KERNEL = 0;

        private static readonly int THREAD_PER_GROUP = 8;
        private static readonly int MAX_THREAD_GROUP = 64000 / THREAD_PER_GROUP;

        private ComputeShader m_shader;

        public AppendNode() : base()
        {
            m_inputCount = 2;
            UpdateSlotArrays();
        }

        private void UpdateSlotArrays()
        {
            if (m_inputCount < MIN_INPUT)
            {
                m_inputCount = MIN_INPUT;
            }
            m_inputSlots = new BufferSlot[m_inputCount];
            for (int i = 0; i < m_inputCount; ++i)
            {
                m_inputSlots[i] = new BufferSlot($"Input {i}", SlotDirection.Input, i);
            }
        }

        public override ISlot[] GetInputSlots()
        {
            if (m_inputSlots == null || m_inputSlots.Length < MIN_INPUT)
            {
                UpdateSlotArrays();
            }
            ISlot[] slots = new ISlot[m_inputSlots.Length];
            for (int i = 0; i < slots.Length; ++i)
            {
                slots[i] = m_inputSlots[i];
            }
            return slots;
        }

        public override ISlot[] GetOutputSlots()
        {
            return new ISlot[] { outputSlot };
        }

        public override ISlot GetSlot(int id)
        {
            if (id == outputSlot.id)
            {
                return outputSlot;
            }
            if (m_inputSlots == null || m_inputSlots.Length < MIN_INPUT)
            {
                UpdateSlotArrays();
            }
            for (int i = 0; i < m_inputSlots.Length; ++i)
            {
                if (m_inputSlots[i].id == id)
                {
                    return m_inputSlots[i];
                }
            }
            return null;
        }

        public override void ExecuteImmediate(GraphContext context)
        {
            if (m_inputSlots == null || m_inputSlots.Length < MIN_INPUT)
            {
                UpdateSlotArrays();
            }

            int slotCount = m_inputSlots.Length;
            SlotRef[] inputRefLinks = new SlotRef[slotCount];
            ComputeBuffer[] inputBuffers = new ComputeBuffer[slotCount];
            int totalCount = 0;

            for (int i = 0; i < slotCount; ++i)
            {
                SlotRef refLink = context.GetInputLink(m_id, m_inputSlots[i].id);
                inputRefLinks[i] = refLink;
                ComputeBuffer buffer = context.GetBuffer(refLink);
                if (buffer != null && buffer.count % PositionSample.SIZE != 0)
                {
                    Debug.LogError($"Cannot parse buffer {m_inputSlots[i].name}, node id {m_id}. Skipping this input.");
                    continue;
                }
                inputBuffers[i] = buffer;
                if (buffer != null)
                {
                    totalCount += buffer.count;
                }
            }

            SlotRef outputRef = new SlotRef(m_id, outputSlot.id);
            if (totalCount > 0)
            {
                m_shader = Resources.Load<ComputeShader>(COMPUTE_SHADER_NAME);
                DataPool.BufferDescriptor desc = DataPool.BufferDescriptor.Create(totalCount);
                ComputeBuffer destBuffer = context.CreateBuffer(desc, outputRef);

                m_shader.SetBuffer(KERNEL, DEST_BUFFER, destBuffer);

                int destOffset = 0;
                for (int i = 0; i < slotCount; ++i)
                {
                    ComputeBuffer inputBuffer = inputBuffers[i];
                    if (inputBuffer == null)
                    {
                        continue;
                    }

                    m_shader.SetBuffer(KERNEL, SOURCE_BUFFER, inputBuffer);
                    m_shader.SetInt(DEST_OFFSET, destOffset);

                    int instanceCount = inputBuffer.count / PositionSample.SIZE;
                    int totalThreadGroupX = (instanceCount + THREAD_PER_GROUP - 1) / THREAD_PER_GROUP;
                    int iteration = (totalThreadGroupX + MAX_THREAD_GROUP - 1) / MAX_THREAD_GROUP;

                    for (int it = 0; it < iteration; ++it)
                    {
                        int threadGroupX = Mathf.Min(MAX_THREAD_GROUP, totalThreadGroupX);
                        totalThreadGroupX -= MAX_THREAD_GROUP;
                        int baseIndex = it * MAX_THREAD_GROUP * THREAD_PER_GROUP;
                        m_shader.SetInt(BASE_INDEX, baseIndex);
                        m_shader.Dispatch(KERNEL, threadGroupX, 1, 1);
                    }

                    destOffset += instanceCount;
                }

                Resources.UnloadAsset(m_shader);
            }

            for (int i = 0; i < slotCount; ++i)
            {
                context.ReleaseReference(inputRefLinks[i]);
            }
        }
    }
}
#endif
