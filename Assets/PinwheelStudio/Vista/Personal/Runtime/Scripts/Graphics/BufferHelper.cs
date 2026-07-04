#if VISTA
using UnityEngine;

namespace Pinwheel.Vista.Graphics
{
    /// <summary>
    /// Provides GPU-side copy helpers for Vista's float-packed compute buffers.
    /// </summary>
    public static class BufferHelper
    {
        private static readonly string COPY_SHADER_NAME = "Vista/Shaders/BufferCopy";
        private static readonly int SRC_BUFFER = Shader.PropertyToID("_SrcBuffer");
        private static readonly int DEST_BUFFER = Shader.PropertyToID("_DestBuffer");
        private static readonly int COPY_KERNEL = 0;

        private static readonly int BASE_INDEX = Shader.PropertyToID("_BaseIndex");

        private static ComputeShader s_copyBufferShader;

        /// <summary>
        /// Copies the contents of one compute buffer into another using the buffer-copy compute shader.
        /// </summary>
        /// <param name="from">
        /// Source buffer to read from.
        /// </param>
        /// <param name="to">
        /// Destination buffer to overwrite.
        /// </param>
        /// <remarks>
        /// The copy is dispatched in chunks to avoid excessively large single dispatches. Source and
        /// destination buffers are expected to have compatible counts and strides.
        /// </remarks>
        public static void Copy(ComputeBuffer from, ComputeBuffer to)
        {
            s_copyBufferShader = Resources.Load<ComputeShader>(COPY_SHADER_NAME);
            s_copyBufferShader.SetBuffer(COPY_KERNEL, SRC_BUFFER, from);
            s_copyBufferShader.SetBuffer(COPY_KERNEL, DEST_BUFFER, to);

            int maxElementPerStep = 64000*8;
            int remainingCount = from.count;
            int baseIndex = 0;
            while (remainingCount > 0)
            {
                int count = Mathf.Min(maxElementPerStep, remainingCount);
                s_copyBufferShader.SetInt(BASE_INDEX, baseIndex);
                s_copyBufferShader.Dispatch(COPY_KERNEL, (count + 7) / 8, 1, 1);
                remainingCount -= count;
                baseIndex += count;
            }
            Resources.UnloadAsset(s_copyBufferShader);
        }

        /// <summary>
        /// Allocates a new compute buffer and copies the source contents into it.
        /// </summary>
        /// <param name="src">
        /// Source buffer to clone.
        /// </param>
        /// <returns>
        /// A newly allocated buffer with the same count, stride, and contents as
        /// <paramref name="src"/>.
        /// </returns>
        public static ComputeBuffer Clone(ComputeBuffer src)
        {
            ComputeBuffer cloned = new ComputeBuffer(src.count, src.stride);
            Copy(src, cloned);
            return cloned;
        }        
    }
}
#endif


