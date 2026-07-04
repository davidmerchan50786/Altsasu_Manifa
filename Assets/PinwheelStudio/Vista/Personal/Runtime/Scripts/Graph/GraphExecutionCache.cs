#if VISTA
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Owns cached graph execution resources across multiple graph runs.
    /// </summary>
    /// <remarks>
    /// The cache is intentionally external to <see cref="TerrainGraph"/> and <see cref="GraphContext"/>.
    /// Execution callers create and dispose one cache instance, then pass it into graph execution so
    /// cache-aware nodes can reuse outputs when their inputs and settings have not changed.
    /// </remarks>
    public class GraphExecutionCache : IDisposable
    {
        /// <summary>
        /// Stores the input snapshots, argument fingerprint, settings fingerprint, and output snapshots
        /// for one cacheable graph operation.
        /// </summary>
        public class Entry : IDisposable
        {
            private Dictionary<int, RenderTexture> m_inputTextures;
            /// <summary>
            /// Cached copies of input textures, keyed by input slot id.
            /// </summary>
            public Dictionary<int, RenderTexture> inputTextures
            {
                get
                {
                    return m_inputTextures;
                }
            }

            private Dictionary<int, ComputeBuffer> m_inputBuffers;
            /// <summary>
            /// Cached copies of input buffers, keyed by input slot id.
            /// </summary>
            public Dictionary<int, ComputeBuffer> inputBuffers
            {
                get
                {
                    return m_inputBuffers;
                }
            }

            private string m_settingsJson;
            /// <summary>
            /// Serialized node settings used to determine whether this entry is still valid.
            /// </summary>
            public string settingsJson
            {
                get
                {
                    return m_settingsJson;
                }
                set
                {
                    m_settingsJson = value;
                }
            }

            private string m_argsJson;
            /// <summary>
            /// Serialized execution arguments used to determine whether this entry is still valid.
            /// </summary>
            public string argsJson
            {
                get
                {
                    return m_argsJson;
                }
                set
                {
                    m_argsJson = value;
                }
            }

            private Dictionary<int, RenderTexture> m_outputTextures;
            /// <summary>
            /// Cached copies of output textures, keyed by output slot id.
            /// </summary>
            public Dictionary<int, RenderTexture> outputTextures
            {
                get
                {
                    return m_outputTextures;
                }
            }

            private Dictionary<int, ComputeBuffer> m_outputBuffers;
            /// <summary>
            /// Cached copies of output buffers, keyed by output slot id.
            /// </summary>
            public Dictionary<int, ComputeBuffer> outputBuffers
            {
                get
                {
                    return m_outputBuffers;
                }
            }

            public Entry()
            {
                m_inputTextures = new Dictionary<int, RenderTexture>();
                m_inputBuffers = new Dictionary<int, ComputeBuffer>();
                m_outputTextures = new Dictionary<int, RenderTexture>();
                m_outputBuffers = new Dictionary<int, ComputeBuffer>();
                m_settingsJson = string.Empty;
                m_argsJson = string.Empty;
            }

            /// <summary>
            /// Releases every render texture and compute buffer owned by this entry.
            /// </summary>
            public void Dispose()
            {
                HashSet<RenderTexture> disposedTextures = new HashSet<RenderTexture>();
                HashSet<ComputeBuffer> disposedBuffers = new HashSet<ComputeBuffer>();
                DisposeTextures(m_inputTextures, disposedTextures);
                DisposeTextures(m_outputTextures, disposedTextures);
                DisposeBuffers(m_inputBuffers, disposedBuffers);
                DisposeBuffers(m_outputBuffers, disposedBuffers);
            }

            private static void DisposeTextures(Dictionary<int, RenderTexture> textures, HashSet<RenderTexture> disposed)
            {
                foreach (KeyValuePair<int, RenderTexture> pair in textures)
                {
                    RenderTexture texture = pair.Value;
                    if (texture != null && !disposed.Contains(texture))
                    {
                        texture.Release();
                        UnityEngine.Object.DestroyImmediate(texture);
                        disposed.Add(texture);
                    }
                }
                textures.Clear();
            }

            private static void DisposeBuffers(Dictionary<int, ComputeBuffer> buffers, HashSet<ComputeBuffer> disposed)
            {
                foreach (KeyValuePair<int, ComputeBuffer> pair in buffers)
                {
                    ComputeBuffer buffer = pair.Value;
                    if (buffer != null && !disposed.Contains(buffer))
                    {
                        buffer.Dispose();
                        disposed.Add(buffer);
                    }
                }
                buffers.Clear();
            }
        }

        private Dictionary<string, Entry> m_entries;

        public GraphExecutionCache()
        {
            m_entries = new Dictionary<string, Entry>();
        }

        /// <summary>
        /// Attempts to retrieve one cache entry by id.
        /// </summary>
        public bool TryGetEntry(string id, out Entry entry)
        {
            if (string.IsNullOrEmpty(id))
            {
                entry = null;
                return false;
            }
            return m_entries.TryGetValue(id, out entry);
        }

        /// <summary>
        /// Stores a cache entry, disposing any previous entry that used the same id.
        /// </summary>
        public void SetEntry(string id, Entry entry)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("Cache entry id must not be null or empty.", nameof(id));
            }

            Entry oldEntry;
            if (m_entries.TryGetValue(id, out oldEntry) && !ReferenceEquals(oldEntry, entry))
            {
                oldEntry.Dispose();
            }

            if (entry != null)
            {
                m_entries[id] = entry;
            }
            else
            {
                m_entries.Remove(id);
            }
        }

        /// <summary>
        /// Removes and disposes one cache entry.
        /// </summary>
        public void RemoveEntry(string id)
        {
            Entry entry;
            if (m_entries.TryGetValue(id, out entry))
            {
                entry.Dispose();
                m_entries.Remove(id);
            }
        }

        /// <summary>
        /// Disposes every cache entry and clears the cache.
        /// </summary>
        public void Flush()
        {
            foreach (KeyValuePair<string, Entry> pair in m_entries)
            {
                Entry entry = pair.Value;
                if (entry != null)
                {
                    entry.Dispose();
                }
            }
            m_entries.Clear();
        }

        /// <summary>
        /// Disposes every cached resource owned by this cache.
        /// </summary>
        public void Dispose()
        {
            Flush();
        }
    }
}
#endif
