#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;
using Pinwheel.Vista.Graph;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Provides the callback hook used by Vista tools to hand a render texture to an external file-export implementation.
    /// </summary>
    public static class RenderTextureToFileUtils
    {
        /// <summary>
        /// Handles a request to export a render texture using a caller-provided file name stem.
        /// </summary>
        /// <param name="rt">The render texture that should be exported.</param>
        /// <param name="fileNameNoExtension">Suggested file name without an extension.</param>
        public delegate void RTToFileHandler(RenderTexture rt, string fileNameNoExtension);
        /// <summary>
        /// Raised when Vista requests that a render texture be written to disk by an external handler.
        /// </summary>
        public static event RTToFileHandler saveRenderTextureCallback;

        /// <summary>
        /// Notifies the registered export handler that a render texture should be saved.
        /// </summary>
        /// <param name="rt">The render texture to export.</param>
        /// <param name="fileNameNoExtension">Suggested file name without an extension.</param>
        /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="rt"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// This method only forwards the request through <see cref="saveRenderTextureCallback"/>. It does not perform any file I/O by itself.
        /// </remarks>
        public static void SignalSaveRenderTextureToFile(RenderTexture rt, string fileNameNoExtension)
        {
            if (rt == null)
            {
                throw new System.ArgumentNullException("rt cannot be null");
            }

            saveRenderTextureCallback?.Invoke(rt, fileNameNoExtension);
        }

        /// <summary>
        /// Builds the default export name for a graph output from the node title, a shortened node id, and the slot name.
        /// </summary>
        /// <param name="node">The graph node that produced the texture.</param>
        /// <param name="slot">The output slot being exported.</param>
        /// <returns>A file-name stem suitable for passing to <see cref="SignalSaveRenderTextureToFile(RenderTexture, string)"/>.</returns>
        internal static string GetFileNameNoExtension(INode node, ISlot slot)
        {
            string path = $"{NodeMetadata.Get(node.GetType()).title}_{node.id.Substring(0,8)}_{slot.name}";
            return path;
        }
    }
}
#endif


