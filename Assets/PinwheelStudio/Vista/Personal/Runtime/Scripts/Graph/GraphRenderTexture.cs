#if VISTA
using UnityEngine;
using System;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Wraps a <see cref="RenderTexture"/> so the graph runtime can track it by identifier.
    /// </summary>
    /// <remarks>
    /// Vista uses this wrapper for the same reason it uses <see cref="GraphBuffer"/>: the runtime
    /// needs metadata such as a pool identifier in addition to the raw Unity object. The wrapped
    /// texture is what nodes and population systems actually read and write.
    /// </remarks>
    public class GraphRenderTexture : IDisposable
    {
        private string m_identifier;
        /// <summary>
        /// Pool or input identifier associated with this render texture.
        /// </summary>
        /// <remarks>
        /// <see cref="DataPool"/> uses this name to retrieve, transfer, and recycle render targets.
        /// When no identifier has been assigned yet, the getter normalizes the value to an empty
        /// string.
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

        private RenderTexture m_renderTexture;
        /// <summary>
        /// Underlying render texture used by graph execution and downstream terrain-population code.
        /// </summary>
        public RenderTexture renderTexture
        {
            get
            {
                return m_renderTexture;
            }
        }

        private GraphRenderTexture()
        {

        }

        /// <summary>
        /// Creates a linear render texture wrapper with the requested size and format.
        /// </summary>
        /// <param name="width">
        /// Width, in pixels, of the render texture to allocate.
        /// </param>
        /// <param name="height">
        /// Height, in pixels, of the render texture to allocate.
        /// </param>
        /// <param name="format">
        /// Render texture format used for the underlying Unity texture.
        /// </param>
        public GraphRenderTexture(int width, int height, RenderTextureFormat format)
        {
            m_renderTexture = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear);
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

        /// <summary>
        /// Releases and destroys the underlying render texture.
        /// </summary>
        /// <remarks>
        /// Ownership usually belongs to <see cref="DataPool"/> until the render texture is removed
        /// from the pool. After ownership has been transferred, the caller is responsible for
        /// disposing it.
        /// </remarks>
        public void Dispose()
        {
            if (m_renderTexture != null)
            {
                m_renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(m_renderTexture);
            }
        }

        /// <summary>
        /// Returns the wrapped Unity render texture.
        /// </summary>
        /// <param name="graphRT">
        /// The graph wrapper to unwrap.
        /// </param>
        /// <returns>
        /// The underlying <see cref="RenderTexture"/>, or <see langword="null"/> when
        /// <paramref name="graphRT"/> is <see langword="null"/>.
        /// </returns>
        public static implicit operator RenderTexture(GraphRenderTexture graphRT)
        {
            if (graphRT == null)
                return null;
            else
                return graphRT.renderTexture;
        }

        /// <summary>
        /// Wraps an existing Unity render texture in a graph render-texture wrapper.
        /// </summary>
        /// <param name="rt">
        /// The Unity render texture to wrap.
        /// </param>
        /// <returns>
        /// A wrapper whose identifier is initialized from <see cref="UnityEngine.Object.name"/>, or
        /// <see langword="null"/> when <paramref name="rt"/> is <see langword="null"/>.
        /// </returns>
        /// <remarks>
        /// This conversion does not duplicate the texture; the wrapper simply points at the existing
        /// Unity object.
        /// </remarks>
        public static implicit operator GraphRenderTexture(RenderTexture rt)
        {
            if (rt == null)
            {
                return null;
            }
            else
            {
                GraphRenderTexture graphRt = new GraphRenderTexture();
                graphRt.m_identifier = rt.name;
                graphRt.m_renderTexture = rt;
                return graphRt;
            }
        }
    }
}
#endif


