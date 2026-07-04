#if VISTA
using System;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Wraps a <see cref="ComputeBuffer"/> so the graph runtime can track it by identifier.
    /// </summary>
    /// <remarks>
    /// Vista uses this wrapper because <see cref="ComputeBuffer"/> is sealed and cannot carry the
    /// metadata needed by <see cref="DataPool"/>. The wrapper adds a stable pool identifier while
    /// preserving direct access to the underlying GPU buffer.
    /// </remarks>
    public class GraphBuffer : IDisposable //Can't derive from the sealed ComputeBuffer
    {
        private string m_identifier;
        /// <summary>
        /// Pool or input identifier associated with this buffer.
        /// </summary>
        /// <remarks>
        /// <see cref="DataPool"/> uses this name to look up, transfer, and reuse buffers. When no
        /// identifier has been assigned yet, the getter normalizes the value to an empty string.
        /// </remarks>
        public string identifier
        {
            get
            {
                if (string.IsNullOrEmpty(m_identifier))
                {
                    m_identifier = string.Empty;
                }
                return m_identifier;
            }
            set
            {
                m_identifier = value;
            }
        }

        private ComputeBuffer m_buffer;
        /// <summary>
        /// Underlying compute buffer used by graph nodes and population pipelines.
        /// </summary>
        public ComputeBuffer buffer
        {
            get
            {
                return m_buffer;
            }
        }

        /// <summary>
        /// Creates a graph buffer with the requested element count and stride.
        /// </summary>
        /// <param name="count">
        /// Number of elements to allocate in the underlying <see cref="ComputeBuffer"/>.
        /// </param>
        /// <param name="stride">
        /// Size, in bytes, of each element in the underlying <see cref="ComputeBuffer"/>.
        /// </param>
        /// <remarks>
        /// Most Vista graph buffers use a stride of <c>sizeof(float)</c> and pack structured data as
        /// flat float arrays.
        /// </remarks>
        public GraphBuffer(int count, int stride)
        {
            m_buffer = new ComputeBuffer(count, stride);
        }

        /// <summary>
        /// Disposes the underlying compute buffer.
        /// </summary>
        /// <remarks>
        /// Ownership usually belongs to <see cref="DataPool"/> until the buffer is removed from the
        /// pool. After ownership has been transferred, the caller is responsible for invoking this
        /// method.
        /// </remarks>
        public void Dispose()
        {
            if (m_buffer != null)
            {
                m_buffer.Dispose();
            }
        }

        /// <summary>
        /// Returns the identifier currently assigned to this wrapper.
        /// </summary>
        /// <returns>
        /// The value of <see cref="identifier"/>.
        /// </returns>
        public override string ToString()
        {
            return identifier;
        }
    }
}
#endif


