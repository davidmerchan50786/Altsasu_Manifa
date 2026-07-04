#if VISTA
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Carries all runtime state required to execute a graph or subgraph.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="TerrainGraph"/> builds one context per execution request. The context resolves the
    /// execution order for the requested target nodes, maps input slots to their upstream sources,
    /// tracks variable indirection, exposes external inputs, forwards argument values, and provides
    /// access to the shared <see cref="DataPool"/>.
    /// </para>
    /// <para>
    /// Nodes do not inspect the serialized graph directly during execution. They query and mutate the
    /// runtime state exposed here.
    /// </para>
    /// </remarks>
    public struct GraphContext
    {
        private GraphAsset m_graph;
        private GraphAsset graph
        {
            get
            {
                return m_graph;
            }
        }

        internal DataPool m_dataPool;

        private List<string> m_nodeIds;
        private Dictionary<SlotRef, SlotRef> m_inputLinks;
        private Queue<INode> m_executionSequence;
        private Dictionary<string, int> m_refCount;
        private Dictionary<string, SlotRef> m_variables; //set a name for a slot
        private Dictionary<SlotRef, string> m_variableLinks; //many slots can point to a named slot (var slot)

        private Dictionary<SlotRef, string> m_externalBindings; //is a slot point to an internal (data pool) or external (graph inputs) resources
        private Dictionary<string, RenderTexture> m_externalTextures;
        private Dictionary<string, ComputeBuffer> m_externalBuffers;

        private Dictionary<int, Args> m_arguments;
        private ExecutionProgress m_progress;
        private GraphExecutionCache m_cache;

        /// <summary>
        /// Creates a runtime execution context for a set of target nodes.
        /// </summary>
        /// <param name="graph">
        /// The graph asset being executed.
        /// </param>
        /// <param name="nodeIds">
        /// The target node identifiers requested by the caller. The context expands these into the
        /// full dependency execution sequence.
        /// </param>
        /// <param name="pool">
        /// Resource pool used to allocate and retain transient graph outputs.
        /// </param>
        /// <param name="args">
        /// Request-wide execution arguments such as resolution, bounds, height scale, and seed.
        /// </param>
        /// <param name="progress">
        /// Optional progress object updated while the graph runs.
        /// </param>
        /// <param name="cache">
        /// Optional externally owned cache that cache-aware nodes can query and write to.
        /// </param>
        /// <exception cref="RecursiveGraphReferenceException">
        /// Thrown when subgraph dependencies form a recursive graph reference chain.
        /// </exception>
        /// <exception cref="System.ArgumentException">
        /// Thrown when one of the requested node identifiers cannot be resolved to a node.
        /// </exception>
        public GraphContext(GraphAsset graph, string[] nodeIds, DataPool pool = null, IDictionary<int, Args> args = null, ExecutionProgress progress = null, GraphExecutionCache cache = null)
        {
            m_graph = graph;
            m_inputLinks = new Dictionary<SlotRef, SlotRef>();
            m_refCount = new Dictionary<string, int>();
            m_executionSequence = new Queue<INode>();
            m_nodeIds = nodeIds.Distinct().ToList();
            m_variables = new Dictionary<string, SlotRef>();
            m_variableLinks = new Dictionary<SlotRef, string>();
            m_externalBindings = new Dictionary<SlotRef, string>();
            m_externalTextures = new Dictionary<string, RenderTexture>();
            m_externalBuffers = new Dictionary<string, ComputeBuffer>();
            m_dataPool = pool;
            if (args != null)
            {
                m_arguments = new Dictionary<int, Args>(args);
            }
            else
            {
                m_arguments = new Dictionary<int, Args>();
            }
            m_progress = progress;
            m_cache = cache;

            CheckRecursiveGraph();
            BuildInputLinks();
            BuildExecutionSequence();
            RemoveRedundantInputLinks();
            BuildReferenceCount();
        }

        private void CheckRecursiveGraph()
        {
            Stack<GraphAsset> graphTrace = new Stack<GraphAsset>();
            Stack<bool> flags = new Stack<bool>();
            graphTrace.Push(graph);
            flags.Push(false);

            while (graphTrace.Count > 0)
            {
                GraphAsset g = graphTrace.Peek();
                bool f = flags.Peek();
                if (f == true)
                {
                    graphTrace.Pop();
                    flags.Pop();
                }
                else
                {
                    flags.Pop();
                    flags.Push(true);

                    IEnumerable<GraphAsset> dependency = g.GetDependencySubGraphs().Distinct();
                    foreach (GraphAsset d in dependency)
                    {
                        if (graphTrace.Contains(d))
                        {
                            graphTrace.Push(d);
                            string sequence = StackToString<GraphAsset>(graphTrace, (g0) => { return g0.name; }, "->");
                            string errorMessage = $"Cannot resolve graph execution sequence, there is recursive sub-graph reference(s): {sequence}";
                            throw new RecursiveGraphReferenceException(errorMessage);
                        }
                        graphTrace.Push(d);
                        flags.Push(false);
                    }
                }
            }
        }

        private string StackToString<T>(Stack<T> stack, System.Func<T, string> toStringFunc, string separator)
        {
            Stack<T> st = new Stack<T>(stack);
            StringBuilder sb = new StringBuilder();
            while (st.Count > 0)
            {
                T o = st.Pop();
                if (toStringFunc != null)
                {
                    sb.Append(toStringFunc(o));
                }
                else
                {
                    sb.Append(o.ToString());
                }
                sb.Append("~");
            }
            string s = sb.ToString();
            return s.Trim('~').Replace("~", separator);
        }

        private void BuildInputLinks()
        {
            List<IEdge> edges = m_graph.m_edges;
            foreach (IEdge e in edges)
            {
                m_inputLinks[e.inputSlot] = e.outputSlot;
            }
        }

        private void RemoveRedundantInputLinks()
        {
            List<string> ids = new List<string>();
            foreach (INode n in m_executionSequence)
            {
                ids.Add(n.id);
            }

            List<SlotRef> toRemove = new List<SlotRef>();
            List<IEdge> edges = m_graph.m_edges;
            foreach (IEdge e in edges)
            {
                if (!ids.Contains(e.inputSlot.nodeId))
                {
                    toRemove.Add(e.inputSlot);
                }
            }

            foreach (SlotRef s in toRemove)
            {
                m_inputLinks.Remove(s);
            }
        }

        /// <summary>
        /// Marks a slot as forwarding to a named variable instead of using its original upstream slot directly.
        /// </summary>
        /// <param name="slotRef">
        /// The slot whose data source should resolve through a named variable.
        /// </param>
        /// <param name="varName">
        /// The registered variable name to resolve at read time.
        /// </param>
        /// <exception cref="System.ArgumentException">
        /// Thrown when <paramref name="varName"/> is null or empty.
        /// </exception>
        /// <remarks>
        /// This indirection is used by the Get/Set Variable node pair and by bypassed nodes that
        /// preserve a pass-through connection through the variable system.
        /// </remarks>
        public void LinkToVariable(SlotRef slotRef, string varName)
        {
            if (string.IsNullOrEmpty(varName))
            {
                throw new System.ArgumentException("varName must not be null or empty");
            }
            m_variableLinks[slotRef] = varName;
        }

        /// <summary>
        /// Marks a slot as intentionally resolving to no valid upstream data.
        /// </summary>
        /// <param name="slotRef">
        /// The slot that should resolve to <see cref="SlotRef.invalid"/>.
        /// </param>
        /// <remarks>
        /// This is mainly used when bypass logic needs to preserve the notion of a disconnected path.
        /// </remarks>
        public void LinkToInvalid(SlotRef slotRef)
        {
            m_variableLinks[slotRef] = "";
        }

        /// <summary>
        /// Resolves the upstream source for an input slot.
        /// </summary>
        /// <param name="inputSlot">
        /// The input slot to resolve.
        /// </param>
        /// <returns>
        /// The connected upstream output slot, the slot registered to a linked variable, or
        /// <see cref="SlotRef.invalid"/> when the input is unconnected.
        /// </returns>
        /// <remarks>
        /// Variable indirection is resolved here, so callers receive the final effective source slot
        /// rather than the raw edge stored in the graph.
        /// </remarks>
        public SlotRef GetInputLink(SlotRef inputSlot)
        {
            SlotRef s;
            if (m_inputLinks.TryGetValue(inputSlot, out s))
            {
                if (m_variableLinks.ContainsKey(s))
                {
                    string varName = m_variableLinks[s];
                    return GetVariable(varName);
                }
                else
                {
                    return s;
                }
            }
            else
            {
                return SlotRef.invalid;
            }
        }

        /// <summary>
        /// Resolves the upstream source for a specific input slot identified by node and slot id.
        /// </summary>
        public SlotRef GetInputLink(string nodeId, int slotId)
        {
            return GetInputLink(new SlotRef(nodeId, slotId));
        }

        private void BuildExecutionSequence()
        {
            Stack<INode>[] stacks = new Stack<INode>[m_nodeIds.Count];
            for (int i = 0; i < m_nodeIds.Count; ++i)
            {
                INode node = graph.GetNode(m_nodeIds[i]);
                if (node == null)
                {
                    throw new System.ArgumentException($"Cannot execute a null node with id {m_nodeIds[i]}");
                }
                stacks[i] = CreateExecutionStack(node);
            }

            m_executionSequence = new Queue<INode>();
            while (HasItem(stacks))
            {
                for (int i = 0; i < stacks.Length; ++i)
                {
                    if (stacks[i].Count > 0)
                    {
                        INode n = stacks[i].Pop();
                        if (!m_executionSequence.Contains(n))
                        {
                            m_executionSequence.Enqueue(n);
                        }
                    }
                }
            }
        }

        private bool HasItem<T>(Stack<T>[] stacks)
        {
            for (int i = 0; i < stacks.Length; ++i)
            {
                if (stacks[i].Count > 0)
                    return true;
            }
            return false;
        }

        private Stack<INode> CreateExecutionStack(INode node)
        {
            Stack<INode> nodeTrace = new Stack<INode>();
            Stack<INode> result = new Stack<INode>();
            nodeTrace.Push(node);
            while (nodeTrace.Count > 0)
            {
                INode n = nodeTrace.Pop();
                if (result.Count > 1 && n == node)
                {
                    throw new System.Exception($"Unable to resolve execution stack. Make sure there is no loop in the connection, especially Get/Set Variable nodes.");
                }
                result.Push(n);

                ISlot[] inputSlots = n.GetInputSlots();
                foreach (ISlot inputSlot in inputSlots)
                {
                    SlotRef inputRef = new SlotRef(n.id, inputSlot.id);
                    SlotRef outputRef = GetInputLink(inputRef);
                    if (outputRef.Equals(SlotRef.invalid))
                        continue;

                    INode connectedNode = graph.GetNode(outputRef.nodeId);
                    nodeTrace.Push(connectedNode);
                }

                if (n is IHasDependencyNodes hdn)
                {
                    IEnumerable<INode> dependencies = hdn.GetDependencies(graph.m_nodes);
                    if (dependencies != null)
                    {
                        foreach (INode d in dependencies)
                        {
                            nodeTrace.Push(d);
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Returns the execution sequence calculated for this context.
        /// </summary>
        /// <returns>
        /// A copy of the queue of nodes that should be executed, ordered so dependencies appear
        /// before the target nodes that consume them.
        /// </returns>
        /// <remarks>
        /// The returned queue is a copy, so dequeuing it does not mutate the context's internal
        /// sequence.
        /// </remarks>
        public Queue<INode> GetExecutionSequence()
        {
            return new Queue<INode>(m_executionSequence);
        }

        private void BuildReferenceCount()
        {
            foreach (SlotRef s in m_inputLinks.Values)
            {
                string name = DataPool.GetName(s.nodeId, s.slotId);
                if (m_refCount.ContainsKey(name))
                {
                    m_refCount[name] += 1;
                }
                else
                {
                    m_refCount.Add(name, 1);
                }
            }

            foreach (string id in m_nodeIds)
            {
                INode node = graph.GetNode(id);
                ISlot[] outputs = node.GetOutputSlots();
                foreach (ISlot s in outputs)
                {
                    string name = DataPool.GetName(node.id, s.id);
                    m_refCount[name] = int.MaxValue;
                }
            }

            if (m_dataPool != null)
            {
                m_dataPool.SetReferenceCount(m_refCount);
            }
        }

        /// <summary>
        /// Returns the initial reference-count table computed for this execution.
        /// </summary>
        /// <returns>
        /// A map from pooled output names to the number of downstream consumers that initially depend
        /// on them.
        /// </returns>
        /// <remarks>
        /// Outputs requested as final targets are pinned to <see cref="int.MaxValue"/> so they are
        /// not recycled before the caller extracts them from the <see cref="DataPool"/>.
        /// </remarks>
        public Dictionary<string, int> GetInitialReferenceCount()
        {
            return m_refCount;
        }

        /// <summary>
        /// Returns one execution argument by its well-known key.
        /// </summary>
        /// <param name="id">
        /// The argument key, typically one of the constants defined in <see cref="Args"/>.
        /// </param>
        /// <returns>
        /// The stored argument payload, or the default <see cref="Args"/> value when the key is not
        /// present in this context.
        /// </returns>
        public Args GetArg(int id)
        {
            Args args;
            if (m_arguments != null && m_arguments.TryGetValue(id, out args))
            {
                return args;
            }
            else
            {
                return default;
            }
        }

        /// <summary>
        /// Returns the argument table attached to this execution.
        /// </summary>
        /// <returns>
        /// The dictionary of execution arguments keyed by <see cref="Args"/> identifiers.
        /// </returns>
        /// <remarks>
        /// The returned dictionary is the context's live argument store, not a defensive copy.
        /// </remarks>
        public Dictionary<int, Args> GetArgs()
        {
            return m_arguments;
        }

        /// <summary>
        /// Returns whether this context has an externally owned execution cache.
        /// </summary>
        public bool hasCache
        {
            get
            {
                return m_cache != null;
            }
        }

        /// <summary>
        /// Tries to retrieve the cache entry for a node in this graph.
        /// </summary>
        public bool TryGetCacheEntry(string nodeId, out GraphExecutionCache.Entry entry)
        {
            if (m_cache == null)
            {
                entry = null;
                return false;
            }
            return m_cache.TryGetEntry(GetCacheKey(nodeId), out entry);
        }

        /// <summary>
        /// Stores the cache entry for a node in this graph.
        /// </summary>
        public bool SetCacheEntry(string nodeId, GraphExecutionCache.Entry entry)
        {
            if (m_cache == null)
            {
                return false;
            }
            m_cache.SetEntry(GetCacheKey(nodeId), entry);
            return true;
        }

        /// <summary>
        /// Executes a subgraph immediately while preserving this context's private cache reference.
        /// </summary>
        internal DataPool ExecuteSubGraphImmediate(TerrainGraph graph, string[] nodeIds, TerrainGenerationConfigs configs, GraphInputContainer inputContainer = null, TerrainGraph.FillArgumentsHandler fillArgumentsCallback = null)
        {
            return graph.ExecuteImmediate(nodeIds, configs, inputContainer, fillArgumentsCallback, m_cache);
        }

        /// <summary>
        /// Executes a subgraph progressively while preserving this context's private cache reference.
        /// </summary>
        internal ExecutionHandle ExecuteSubGraph(TerrainGraph graph, string[] nodeIds, TerrainGenerationConfigs configs, GraphInputContainer inputContainer = null, TerrainGraph.FillArgumentsHandler fillArgumentsCallback = null)
        {
            return graph.Execute(nodeIds, configs, inputContainer, fillArgumentsCallback, m_cache);
        }

        private string GetCacheKey(string nodeId)
        {
            int graphId = m_graph != null ? m_graph.GetInstanceID() : 0;
            return $"{graphId}:{nodeId}";
        }

        /// <summary>
        /// Allocates or reuses a pooled render texture for a node output slot.
        /// </summary>
        public RenderTexture CreateRenderTarget(DataPool.RtDescriptor desc, SlotRef slotRef, bool clearContent = true)
        {
            return m_dataPool.CreateRenderTarget(desc, slotRef, clearContent);
        }

        /// <summary>
        /// Allocates or reuses a pooled compute buffer for a node output slot.
        /// </summary>
        public ComputeBuffer CreateBuffer(DataPool.BufferDescriptor desc, SlotRef slotRef, bool clearContent = true)
        {
            return m_dataPool.CreateBuffer(desc, slotRef, clearContent).buffer;
        }

        /// <summary>
        /// Allocates or reuses a temporary pooled render texture addressed by a caller-defined name.
        /// </summary>
        public RenderTexture CreateTemporaryRT(DataPool.RtDescriptor desc, string uniqueName, bool clearContent = true)
        {
            return m_dataPool.CreateTemporaryRT(desc, uniqueName, clearContent);
        }

        /// <summary>
        /// Allocates or reuses a temporary pooled compute buffer addressed by a caller-defined name.
        /// </summary>
        public ComputeBuffer CreateTemporaryBuffer(DataPool.BufferDescriptor desc, string uniqueName, bool clearContent = true)
        {
            return m_dataPool.CreateTemporaryBuffer(desc, uniqueName, clearContent).buffer;
        }

        /// <summary>
        /// Resolves a texture-producing slot to either an external input or a pooled render texture.
        /// </summary>
        /// <param name="slotRef">
        /// The slot to resolve.
        /// </param>
        /// <returns>
        /// The externally bound texture when the slot has an external binding; otherwise the pooled
        /// render texture currently registered for that slot, or <see langword="null"/> when no
        /// texture is available.
        /// </returns>
        public RenderTexture GetTexture(SlotRef slotRef)
        {
            bool isExternal = m_externalBindings.ContainsKey(slotRef);
            if (isExternal)
            {
                string name = m_externalBindings[slotRef];
                RenderTexture t;
                if (m_externalTextures.TryGetValue(name, out t))
                {
                    return t;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                return m_dataPool.GetRT(slotRef);
            }
        }

        /// <summary>
        /// Resolves a buffer-producing slot to either an external input or a pooled compute buffer.
        /// </summary>
        public ComputeBuffer GetBuffer(SlotRef slotRef)
        {
            bool isExternal = m_externalBindings.ContainsKey(slotRef);
            if (isExternal)
            {
                string name = m_externalBindings[slotRef];
                ComputeBuffer buffer;
                if (m_externalBuffers.TryGetValue(name, out buffer))
                {
                    return buffer;
                }
                return null;
            }
            else
            {
                GraphBuffer b = m_dataPool.GetBuffer(slotRef);
                if (b != null)
                {
                    return b.buffer;
                }
                else
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Releases one reference to a temporary resource identified by a custom name.
        /// </summary>
        /// <param name="name">
        /// The temporary resource name previously passed to
        /// <see cref="CreateTemporaryRT(DataPool.RtDescriptor, string, bool)"/> or
        /// <see cref="CreateTemporaryBuffer(DataPool.BufferDescriptor, string, bool)"/>.
        /// </param>
        public void ReleaseTemporary(string name)
        {
            m_dataPool.ReleaseReference(name);
        }

        /// <summary>
        /// Releases one reference to the pooled output bound to a slot.
        /// </summary>
        public void ReleaseReference(SlotRef slotRef)
        {
            m_dataPool.ReleaseReference(slotRef);
        }

        /// <summary>
        /// Returns the remaining tracked consumer count for a slot-bound pooled output.
        /// </summary>
        public int GetReferenceCount(SlotRef slotRef)
        {
            return m_dataPool.GetUsageCount(slotRef);
        }

        /// <summary>
        /// Determines whether a node id belongs to the target set requested for this execution.
        /// </summary>
        public bool IsTargetNode(string id)
        {
            return m_nodeIds.Contains(id);
        }

        /// <summary>
        /// Determines whether a named runtime variable has already been registered in this context.
        /// </summary>
        public bool HasVariable(string varName)
        {
            return m_variables.ContainsKey(varName);
        }

        /// <summary>
        /// Registers a named variable and binds it to a slot output.
        /// </summary>
        /// <param name="varName">
        /// The unique variable name.
        /// </param>
        /// <param name="slotRef">
        /// The slot whose output should become the value source for the variable.
        /// </param>
        /// <exception cref="System.ArgumentException">
        /// Thrown when the variable name is empty or already registered in this context.
        /// </exception>
        /// <remarks>
        /// Variable outputs are pinned to <see cref="int.MaxValue"/> in the data pool so they remain
        /// available to later Get Variable lookups regardless of normal downstream reference counts.
        /// </remarks>
        public void SetVariable(string varName, SlotRef slotRef)
        {
            if (string.IsNullOrEmpty(varName))
            {
                throw new System.ArgumentException("varName must not be null or empty");
            }
            if (m_variables.ContainsKey(varName))
            {
                throw new System.ArgumentException($"Variable {varName} is already exist");
            }
            m_variables.Add(varName, slotRef);
            m_dataPool.SetReferenceCount(slotRef, int.MaxValue);
        }

        /// <summary>
        /// Resolves a named runtime variable to the slot that currently defines it.
        /// </summary>
        /// <param name="varName">
        /// The variable name to resolve.
        /// </param>
        /// <returns>
        /// The slot registered for the variable, or <see cref="SlotRef.invalid"/> when the variable
        /// is missing or the name is empty.
        /// </returns>
        public SlotRef GetVariable(string varName)
        {
            if (string.IsNullOrEmpty(varName))
            {
                return SlotRef.invalid;
            }
            SlotRef slotRef;
            if (m_variables.TryGetValue(varName, out slotRef))
            {
                return slotRef;
            }
            return SlotRef.invalid;
        }

        /// <summary>
        /// Marks a slot as reading from a named external resource instead of the internal data pool.
        /// </summary>
        /// <param name="slotRef">
        /// The slot that should resolve externally.
        /// </param>
        /// <param name="name">
        /// The external resource name that will later be matched against
        /// <see cref="AddExternalTexture(string, RenderTexture)"/> or
        /// <see cref="AddExternalBuffer(string, ComputeBuffer)"/>.
        /// </param>
        /// <exception cref="System.ArgumentException">
        /// Thrown when <paramref name="name"/> is null or empty.
        /// </exception>
        public void SetExternal(SlotRef slotRef, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new System.ArgumentException("External resource name must not be null or empty");
            }
            m_externalBindings[slotRef] = name;
        }

        /// <summary>
        /// Registers an external texture input that can be resolved by name during execution.
        /// </summary>
        /// <param name="name">
        /// The binding name used by input nodes or external slot bindings.
        /// </param>
        /// <param name="texture">
        /// The texture to expose to the graph.
        /// </param>
        public void AddExternalTexture(string name, RenderTexture texture)
        {
            m_externalTextures.Add(name, texture);
        }

        /// <summary>
        /// Registers an external compute buffer input that can be resolved by name during execution.
        /// </summary>
        public void AddExternalBuffer(string name, ComputeBuffer buffer)
        {
            m_externalBuffers.Add(name, buffer);
        }

        internal void BindTexture(SlotRef slotRef, GraphRenderTexture texture)
        {
            m_dataPool.BindTexture(slotRef, texture);
        }

        internal void BindBuffer(SlotRef slotRef, GraphBuffer buffer)
        {
            m_dataPool.BindBuffer(slotRef, buffer);
        }

        /// <summary>
        /// Updates the fine-grained progress of the node that is currently executing.
        /// </summary>
        /// <param name="p">
        /// The local node progress value to publish.
        /// </param>
        /// <remarks>
        /// This writes into <see cref="ExecutionProgress.currentProgress"/> when the context was
        /// created with a progress object.
        /// </remarks>
        public void SetCurrentProgress(float p)
        {
            if (m_progress != null)
            {
                m_progress.currentProgress = p;
            }
        }
    }
}
#endif


