#if VISTA
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime;
using Pinwheel.Vista.Diagnostics;

namespace Pinwheel.Vista.Graph
{
    [CreateAssetMenu(menuName = "Vista/Terrain Graph", order = -10000)]
    [HelpURL("https://docs.pinwheelstud.io/vista/docs/terrain-graph.html")]
    /// <summary>
    /// Root graph asset that executes Vista's terrain-generation node graph at runtime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This type sits at the center of the runtime graph pipeline. It takes a set of requested output
    /// node ids together with <see cref="TerrainGenerationConfigs"/>, converts those settings into the
    /// standard <see cref="Args"/> table, builds a <see cref="GraphContext"/>, and executes the
    /// required dependency chain in the correct order.
    /// </para>
    /// <para>
    /// The graph itself does not directly decide which biome outputs will be consumed by terrain
    /// systems. Higher-level utilities such as <see cref="TerrainGraphUtilities"/> select the
    /// relevant output nodes, execute the graph through this class, then remove final textures and
    /// buffers from the returned <see cref="DataPool"/> or <see cref="ExecutionHandle"/>.
    /// </para>
    /// <para>
    /// This class supports both immediate execution and progressive coroutine-based execution. In the
    /// progressive path, nodes that opt into split execution can yield across frames unless
    /// <see cref="allowSplitExecution"/> is disabled.
    /// </para>
    /// </remarks>
    public class TerrainGraph : GraphAsset
    {
        /// <summary>
        /// Callback invoked around whole-graph execution.
        /// </summary>
        /// <param name="graph">
        /// The graph being executed.
        /// </param>
        /// <param name="data">
        /// The request configuration used for the execution.
        /// </param>
        /// <param name="nodeIds">
        /// The target node ids requested by the caller. Dependency nodes may also execute, but this
        /// list identifies the requested outputs that drove the run.
        /// </param>
        public delegate void GraphExecutionHandler(TerrainGraph graph, TerrainGenerationConfigs data, string[] nodeIds);
        /// <summary>
        /// Optional callback fired immediately before a graph execution starts.
        /// </summary>
        /// <remarks>
        /// This is a static hook that applies to all <see cref="TerrainGraph"/> instances in the
        /// process. It is typically used for diagnostics, profiling, or editor tooling.
        /// </remarks>
        public static GraphExecutionHandler onBeforeGraphExecution;
        /// <summary>
        /// Optional callback fired after a graph execution has finished processing all queued nodes.
        /// </summary>
        public static GraphExecutionHandler onAfterGraphExecution;

        /// <summary>
        /// Callback invoked around one node execution step.
        /// </summary>
        /// <param name="graph">
        /// The graph containing the node.
        /// </param>
        /// <param name="data">
        /// The request configuration used for the current graph run.
        /// </param>
        /// <param name="node">
        /// The node about to run or that has just finished running.
        /// </param>
        public delegate void NodeExecutionHandler(TerrainGraph graph, TerrainGenerationConfigs data, INode node);
        /// <summary>
        /// Optional callback fired immediately before a node executes or bypasses.
        /// </summary>
        public static NodeExecutionHandler onBeforeNodeExecution;
        /// <summary>
        /// Optional callback fired after a node finishes executing or bypassing.
        /// </summary>
        public static NodeExecutionHandler onAfterNodeExecution;

        /// <summary>
        /// Callback used to append extra execution arguments before the graph starts.
        /// </summary>
        /// <param name="graph">
        /// The graph about to execute.
        /// </param>
        /// <param name="args">
        /// The live argument table that will be attached to the resulting
        /// <see cref="GraphContext"/>.
        /// </param>
        /// <remarks>
        /// Local Procedural Biome execution uses this to inject biome-specific arguments such as
        /// biome scale, biome space, and biome bounds.
        /// </remarks>
        public delegate void FillArgumentsHandler(TerrainGraph graph, IDictionary<int, Args> args);

        [SerializeField]
        private TerrainGenerationConfigs m_debugConfigs;
        /// <summary>
        /// Serialized configuration used by debug or preview workflows that execute the graph directly.
        /// </summary>
        /// <remarks>
        /// This value is not automatically applied to runtime generation requests. It is a stored
        /// default configuration owned by the graph asset itself.
        /// </remarks>
        public TerrainGenerationConfigs debugConfigs
        {
            get
            {
                return m_debugConfigs;
            }
            set
            {
                m_debugConfigs = value;
            }
        }

        [SerializeField]
        private bool m_allowSplitExecution;
        /// <summary>
        /// Controls whether nodes that support coroutine-based execution may split work across frames.
        /// </summary>
        /// <remarks>
        /// When <see langword="false"/>, progressive execution still uses an
        /// <see cref="ExecutionHandle"/>, but nodes run through their immediate path instead of their
        /// yielding coroutine path.
        /// </remarks>
        public bool allowSplitExecution
        {
            get
            {
                return m_allowSplitExecution;
            }
            set
            {
                m_allowSplitExecution = value;
            }
        }

        /// <summary>
        /// Restores graph-level execution settings to their default values.
        /// </summary>
        /// <remarks>
        /// This resets the base <see cref="GraphAsset"/> state, recreates
        /// <see cref="debugConfigs"/> from <see cref="TerrainGenerationConfigs.Create()"/>, and
        /// re-enables split execution.
        /// </remarks>
        public override void Reset()
        {
            base.Reset();
            m_debugConfigs = TerrainGenerationConfigs.Create();
            m_allowSplitExecution = true;
        }

        /// <summary>
        /// Determines whether a node type is valid for a terrain graph.
        /// </summary>
        /// <param name="t">
        /// The node type being added or validated.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the type derives from <see cref="ExecutableNodeBase"/>;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// Terrain graphs only accept executable runtime nodes. Specialized graph subclasses can
        /// impose narrower restrictions on top of this behavior.
        /// </remarks>
        public override bool AcceptNodeType(Type t)
        {
            return typeof(ExecutableNodeBase).IsAssignableFrom(t);
        }

        private void FillArguments(TerrainGenerationConfigs configs, IDictionary<int, Args> args, FillArgumentsHandler fillArgumentCallback = null)
        {
            args.Add(Args.RESOLUTION, Args.Create(configs.resolution));
            args.Add(Args.WORLD_BOUNDS, Args.Create(new Vector4(configs.worldBounds.x, configs.worldBounds.y, configs.worldBounds.width, configs.worldBounds.height)));
            args.Add(Args.TERRAIN_HEIGHT, Args.Create(configs.terrainHeight));
            args.Add(Args.SEED, Args.Create(configs.seed));
            args.Add(Args.OUTPUT_TEMP_HEIGHT, Args.Create(configs.shouldOutputTempHeight));
            fillArgumentCallback?.Invoke(this, args);
        }

        /// <summary>
        /// Executes the requested outputs immediately on the current frame and returns the pooled results.
        /// </summary>
        /// <param name="nodeIds">
        /// The output node ids to resolve. The graph automatically executes every dependency required
        /// to produce these targets.
        /// </param>
        /// <param name="configs">
        /// Request-wide execution settings such as resolution, bounds, height scale, and seed.
        /// </param>
        /// <param name="inputContainer">
        /// Optional external inputs to bind into the execution context before node evaluation starts.
        /// </param>
        /// <param name="fillArgumentsCallback">
        /// Optional callback that appends extra values to the argument table after the standard graph
        /// arguments have been written.
        /// </param>
        /// <param name="cache">
        /// Optional externally owned cache shared across graph executions.
        /// </param>
        /// <returns>
        /// The <see cref="DataPool"/> that still owns every texture and buffer generated during the
        /// execution. Callers are expected to remove outputs they want to keep, then dispose the pool.
        /// </returns>
        /// <remarks>
        /// <para>
        /// This path executes every node synchronously by calling
        /// <see cref="ExecutableNodeBase.ExecuteImmediate(GraphContext)"/> unless the node is bypassed.
        /// It is primarily used by subgraph execution and workflows that need the result immediately.
        /// </para>
        /// <para>
        /// Static execution callbacks are fired around the whole run and around each node.
        /// </para>
        /// </remarks>
        public DataPool ExecuteImmediate(string[] nodeIds, TerrainGenerationConfigs configs, GraphInputContainer inputContainer = null, FillArgumentsHandler fillArgumentsCallback = null, GraphExecutionCache cache = null)
        {
            DataPool data = new DataPool();
            Dictionary<int, Args> arguments = new Dictionary<int, Args>();
            FillArguments(configs, arguments, fillArgumentsCallback);
            GraphContext context = new GraphContext(this, nodeIds, data, arguments, null, cache);
            if (inputContainer != null)
            {
                inputContainer.Bind(ref context);
            }

            VistaDebugger.OpenScope($"{name}", DebugScopeType.GraphExecution);
            if (onBeforeGraphExecution != null)
            {
                onBeforeGraphExecution.Invoke(this, configs, nodeIds);
            }

            Queue<INode> executionSequence = context.GetExecutionSequence();

            while (executionSequence.Count > 0)
            {
                ExecutableNodeBase n = executionSequence.Peek() as ExecutableNodeBase;

                if (n.isBypassed)
                {
                    n.Bypass(context);
                }
                else
                {
                    VistaDebugger.OnBeforeNodeExecute(n, context);
                    if (onBeforeNodeExecution != null)
                    {
                        onBeforeNodeExecution.Invoke(this, configs, n);
                    }

                    n.ExecuteImmediate(context);

                    if (onAfterNodeExecution != null)
                    {
                        onAfterNodeExecution.Invoke(this, configs, n);
                    }
                    VistaDebugger.OnAfterNodeExecute(n, context);
                }
                executionSequence.Dequeue();
            }

            if (onAfterGraphExecution != null)
            {
                onAfterGraphExecution.Invoke(this, configs, nodeIds);
            }
            VistaDebugger.CloseScope();

            return data;
        }

        /// <summary>
        /// Starts a progressive graph execution and returns a handle that can be yielded and inspected.
        /// </summary>
        /// <param name="nodeIds">
        /// The output node ids to resolve. The graph automatically expands these into the full
        /// dependency chain required for execution.
        /// </param>
        /// <param name="configs">
        /// Request-wide execution settings such as resolution, bounds, height scale, and seed.
        /// </param>
        /// <param name="inputContainer">
        /// Optional external inputs to bind into the execution context before node evaluation starts.
        /// </param>
        /// <param name="fillArgumentsCallback">
        /// Optional callback that appends extra values to the argument table after the standard graph
        /// arguments have been written.
        /// </param>
        /// <param name="cache">
        /// Optional externally owned cache shared across graph executions.
        /// </param>
        /// <returns>
        /// An <see cref="ExecutionHandle"/> that owns the running coroutine, progress state, and the
        /// <see cref="DataPool"/> for this execution.
        /// </returns>
        /// <remarks>
        /// Nodes that support split execution may run through their coroutine path here. Callers
        /// typically yield on the returned handle, then remove final outputs from
        /// <see cref="ExecutionHandle.data"/> before disposing it.
        /// </remarks>
        public ExecutionHandle Execute(string[] nodeIds, TerrainGenerationConfigs configs, GraphInputContainer inputContainer = null, FillArgumentsHandler fillArgumentsCallback = null, GraphExecutionCache cache = null)
        {
            ExecutionHandle handle = ExecutionHandle.Create();
            Dictionary<int, Args> arguments = new Dictionary<int, Args>();
            FillArguments(configs, arguments, fillArgumentsCallback);
            GraphContext context = new GraphContext(this, nodeIds, handle.data, arguments, handle.progress, cache);
            if (inputContainer != null)
            {
                inputContainer.Bind(ref context);
            }
            handle.coroutines.Add(CoroutineUtility.StartCoroutine(ExecuteProgressive(nodeIds, configs, context, handle)));
            return handle;
        }

        private IEnumerator ExecuteProgressive(string[] nodeIds, TerrainGenerationConfigs configs, GraphContext context, ExecutionHandle handle)
        {
            VistaDebugger.OpenScope($"{name}", DebugScopeType.GraphExecution);
            if (onBeforeGraphExecution != null)
            {
                onBeforeGraphExecution.Invoke(this, configs, nodeIds);
            }

            Queue<INode> executionSequence = context.GetExecutionSequence();
            int totalNodeCount = executionSequence.Count;

            while (executionSequence.Count > 0)
            {
                ExecutableNodeBase n = executionSequence.Peek() as ExecutableNodeBase;

                if (n.isBypassed)
                {
                    n.Bypass(context);
                }
                else
                {
                    VistaDebugger.OnBeforeNodeExecute(n, context);
                    if (onBeforeNodeExecution != null)
                    {
                        onBeforeNodeExecution.Invoke(this, configs, n);
                    }
                    if (n.shouldSplitExecution && m_allowSplitExecution)
                    {
                        CoroutineHandle c = CoroutineUtility.StartCoroutine(n.Execute(context));
                        handle.coroutines.Add(c);
                        yield return c.coroutine;
                    }
                    else
                    {
                        n.ExecuteImmediate(context);
                    }
                    if (onAfterNodeExecution != null)
                    {
                        onAfterNodeExecution.Invoke(this, configs, n);
                    }
                    VistaDebugger.OnAfterNodeExecute(n, context);
                }
                executionSequence.Dequeue();

                handle.progress.totalProgress = 1f - executionSequence.Count * 1.0f / totalNodeCount;
            }

            if (onAfterGraphExecution != null)
            {
                onAfterGraphExecution.Invoke(this, configs, nodeIds);
            }
            VistaDebugger.CloseScope();

            yield return null;
            handle.Complete();
        }

        /// <summary>
        /// Returns every terrain graph referenced by <see cref="TerrainSubGraphNode"/> nodes in this graph.
        /// </summary>
        /// <returns>
        /// The subgraph assets referenced directly by this graph's subgraph nodes.
        /// </returns>
        /// <remarks>
        /// <see cref="GraphContext"/> uses this to detect recursive subgraph dependencies before
        /// execution starts.
        /// </remarks>
        public override IEnumerable<GraphAsset> GetDependencySubGraphs()
        {
            List<GraphAsset> dependencyGraphs = new List<GraphAsset>();
            List<TerrainSubGraphNode> subGraphNode = GetNodes<TerrainSubGraphNode>().ConvertAll(n => n as TerrainSubGraphNode);
            foreach (TerrainSubGraphNode n in subGraphNode)
            {
                if (n.graph != null)
                {
                    dependencyGraphs.Add(n.graph);
                }
            }
            return dependencyGraphs;
        }

        /// <summary>
        /// Validates the graph and refreshes any dynamic subgraph I/O definitions.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> when validation changed either this graph's structure or at least
        /// one subgraph node's generated slot arrays; otherwise, <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// Before delegating to <see cref="GraphAsset.Validate()"/>, this method asks every
        /// <see cref="TerrainSubGraphNode"/> to rebuild its dynamic input and output slots from the
        /// currently assigned subgraph.
        /// </remarks>
        public override bool Validate()
        {
            bool subgraphChanged = false;
            foreach (INode n in m_nodes)
            {
                if (n is TerrainSubGraphNode sgn)
                {
                    subgraphChanged |= sgn.UpdateSlotsArray();
                }
            }

            bool selfChanged = base.Validate();
            return subgraphChanged || selfChanged;
        }
    }
}
#endif


