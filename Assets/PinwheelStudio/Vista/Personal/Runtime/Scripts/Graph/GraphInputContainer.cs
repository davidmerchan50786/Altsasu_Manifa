#if VISTA
using System.Collections.Generic;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Collects external textures and buffers that should be exposed to a graph execution by name.
    /// </summary>
    /// <remarks>
    /// This is the staging object passed into <see cref="TerrainGraph.ExecuteImmediate(string[], TerrainGenerationConfigs, GraphInputContainer, FillArgumentsHandler)"/>
    /// or <see cref="TerrainGraph.Execute(string[], TerrainGenerationConfigs, GraphInputContainer, FillArgumentsHandler)"/>.
    /// During binding, the container copies its named resources into <see cref="GraphContext"/> so
    /// input nodes and externally bound slots can resolve them by input name.
    /// </remarks>
    public class GraphInputContainer
    {
        private Dictionary<string, RenderTexture> m_textures;
        private Dictionary<string, ComputeBuffer> m_buffers;

        /// <summary>
        /// Creates an empty container for external graph inputs.
        /// </summary>
        public GraphInputContainer()
        {
            m_textures = new Dictionary<string, RenderTexture>();
            m_buffers = new Dictionary<string, ComputeBuffer>();
        }

        /// <summary>
        /// Registers a texture input under the name graph input nodes will request.
        /// </summary>
        /// <param name="name">
        /// The input name used by graph nodes, such as one of the reserved values in
        /// <see cref="GraphConstants"/> or a subgraph input name.
        /// </param>
        /// <param name="texture">
        /// The texture to expose during execution.
        /// </param>
        /// <remarks>
        /// The container stores the reference only; it does not clone or take ownership of the
        /// texture.
        /// </remarks>
        public void AddTexture(string name, RenderTexture texture)
        {
            m_textures.Add(name, texture);
        }

        /// <summary>
        /// Registers a compute buffer input under the name graph input nodes will request.
        /// </summary>
        /// <param name="name">
        /// The input name used by graph nodes or subgraph forwarding code.
        /// </param>
        /// <param name="buffer">
        /// The compute buffer to expose during execution.
        /// </param>
        /// <remarks>
        /// The container stores the reference only; it does not clone or take ownership of the
        /// buffer.
        /// </remarks>
        public void AddBuffer(string name, ComputeBuffer buffer)
        {
            m_buffers.Add(name, buffer);
        }

        internal void Bind(ref GraphContext context)
        {
            foreach (string k in m_textures.Keys)
            {
                context.AddExternalTexture(k, m_textures[k]);
            }
            foreach (string k in m_buffers.Keys)
            {
                context.AddExternalBuffer(k, m_buffers[k]);
            }
        }
    }
}
#endif


