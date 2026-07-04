#if VISTA
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [System.Serializable]
    /// <summary>
    /// Base implementation for graph nodes that participate in execution and expose slots through public slot fields.
    /// </summary>
    public abstract class ExecutableNodeBase : INode
    {
        [HideInInspector]
        [SerializeField]
        protected string m_groupId;
        [NonExposable]
        /// <summary>
        /// Identifier of the group that owns this node in the graph editor, or an empty string when ungrouped.
        /// </summary>
        public string groupId
        {
            get
            {
                return m_groupId;
            }
            set
            {
                m_groupId = value;
            }
        }

        [HideInInspector]
        [SerializeField]
        protected string m_id;
        [NonExposable]
        /// <summary>
        /// Stable node identifier used by edges, serialization, and graph execution.
        /// </summary>
        public string id
        {
            get
            {
                return m_id;
            }
        }

        [SerializeField]
        protected VisualState m_visualState;
        [NonExposable]
        /// <summary>
        /// Editor-only canvas state stored with the node.
        /// </summary>
        public VisualState visualState
        {
            get
            {
                return m_visualState;
            }
            set
            {
                m_visualState = value;
            }
        }

        [SerializeField]
        protected bool m_shouldSplitExecution;
        /// <summary>
        /// Indicates whether the graph executor may split this node's work across frames when split execution is enabled.
        /// </summary>
        public virtual bool shouldSplitExecution
        {
            get
            {
                return m_shouldSplitExecution;
            }
            set
            {
                m_shouldSplitExecution = value;
            }
        }

        [SerializeField]
        protected bool m_isBypassed;
        /// <summary>
        /// Indicates whether the node should forward data through <see cref="Bypass(GraphContext)"/> instead of executing normally.
        /// </summary>
        public virtual bool isBypassed
        {
            get
            {
                return m_isBypassed;
            }
            set
            {
                m_isBypassed = value;
            }
        }

        /// <summary>
        /// Initializes the node with a new id and default execution flags.
        /// </summary>
        public ExecutableNodeBase() : base()
        {
            this.m_groupId = string.Empty;
            this.m_id = Utilities.GenerateId();
            this.m_shouldSplitExecution = false;
            this.m_isBypassed = false;
        }

        /// <summary>
        /// Returns the public slot fields whose direction is <see cref="SlotDirection.Input"/>.
        /// </summary>
        public virtual ISlot[] GetInputSlots()
        {
            return GetSlots(SlotDirection.Input);
        }

        /// <summary>
        /// Returns the public slot fields whose direction is <see cref="SlotDirection.Output"/>.
        /// </summary>
        public virtual ISlot[] GetOutputSlots()
        {
            return GetSlots(SlotDirection.Output);
        }

        /// <summary>
        /// Finds a public slot field whose slot id matches the requested value.
        /// </summary>
        /// <param name="id">Per-node slot identifier.</param>
        /// <returns>The matching slot, or <see langword="null"/> if this node does not expose it.</returns>
        public virtual ISlot GetSlot(int id)
        {
            Type nodeType = GetType();
            FieldInfo[] fields = nodeType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; ++i)
            {
                FieldInfo f = fields[i];
                if (typeof(ISlot).IsAssignableFrom(f.FieldType))
                {
                    ISlot slot = f.GetValue(this) as ISlot;
                    if (slot.id == id)
                    {
                        return slot;
                    }
                }
            }

            return null;
        }

        private ISlot[] GetSlots(SlotDirection slotType)
        {
            List<ISlot> slots = new List<ISlot>();
            Type nodeType = GetType();
            FieldInfo[] fields = nodeType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; ++i)
            {
                FieldInfo f = fields[i];
                bool isSlot = typeof(ISlot).IsAssignableFrom(f.FieldType);
                if (isSlot)
                {
                    ISlot slot = f.GetValue(this) as ISlot;
                    if (slot.direction == slotType)
                    {
                        slots.Add(slot);
                    }
                }
            }

            return slots.ToArray();
        }

        /// <summary>
        /// Default coroutine execution path that runs <see cref="ExecuteImmediate(GraphContext)"/> and yields once.
        /// </summary>
        /// <param name="context">Execution context carrying graph resources and settings.</param>
        /// <returns>Enumerator used by progressive graph execution.</returns>
        public virtual IEnumerator Execute(GraphContext context)
        {
            ExecuteImmediate(context);
            yield return null;
        }

        /// <summary>
        /// Executes the node synchronously using the supplied graph context.
        /// </summary>
        /// <param name="context">Execution context carrying graph resources and settings.</param>
        public abstract void ExecuteImmediate(GraphContext context);

        /// <summary>
        /// Default bypass behavior that forwards the first input slot to the first output slot when both slots share the same data type.
        /// </summary>
        /// <param name="context">Execution context carrying graph resources and settings.</param>
        /// <exception cref="Exception">Thrown when called on an <see cref="IOutputNode"/>, which must define its own bypass behavior.</exception>
        /// <remarks>
        /// Generator nodes, pure input nodes, and set-variable nodes naturally fall through because they do not expose both an input slot and an output slot.
        /// Nodes with more complex slot semantics should override this method.
        /// </remarks>
        public virtual void Bypass(GraphContext context)
        {
            if (this is IOutputNode)
            {
                throw new Exception("Should not bypass an output node");
            }

            ISlot[] inputSlots = GetInputSlots();
            ISlot firstInputSlot = null;
            if (inputSlots?.Length > 0)
            {
                firstInputSlot = inputSlots[0];
            }

            ISlot[] outputSlots = GetOutputSlots();
            ISlot firstOutputSlot = null;
            if (outputSlots?.Length > 0)
            {
                firstOutputSlot = outputSlots[0];
            }

            //The node is a generator (Noise, Shape, etc.) or input (Input, GetVar)
            if (firstInputSlot == null)
            {
                return;
            }

            //The node is a SetVar
            if (firstOutputSlot == null)
            {
                return;
            }

            //First IO slot have the same data type
            if (firstInputSlot.GetAdapter().slotType == firstOutputSlot.GetAdapter().slotType)
            {
                SlotRef inputRefLink = context.GetInputLink(m_id, firstInputSlot.id);
                string varName = inputRefLink.ToString();

                SlotRef outputRef = new SlotRef(m_id, firstOutputSlot.id);
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

            //Other cases should be handle in the node override
        }

        /// <summary>
        /// Creates a shallow clone of this node for duplication workflows such as copy/paste.
        /// </summary>
        public INode ShallowCopy()
        {
            return this.MemberwiseClone() as INode;
        }
    }
}
#endif


