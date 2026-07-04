#if VISTA
using UnityEngine;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Copies packed biome sample buffers from one bounds space into another by running a compute-shader remap pass.
    /// </summary>
    /// <remarks>
    /// This utility is used when cached biome data is reused for a different target tile bounds.
    /// It currently supports buffers packed as either <see cref="InstanceSample"/> or <see cref="PositionSample"/>.
    /// </remarks>
    public static class BiomeBufferCopy
    {
        private static readonly string COMPUTE_SHADER_NAME = "Vista/Shaders/BiomeBufferCopy";
        private static readonly int SAMPLES = Shader.PropertyToID("_Samples");
        private static readonly int DEST_SAMPLES = Shader.PropertyToID("_DestSamples");
        private static readonly int IN_BOUNDS = Shader.PropertyToID("_InBounds");
        private static readonly int OUT_BOUNDS = Shader.PropertyToID("_OutBounds");
        private static readonly int COUNT = Shader.PropertyToID("_Count");
        private static readonly int BASE_INDEX = Shader.PropertyToID("_BaseIndex");

        private static readonly int THREAD_PER_GROUP = 8;
        private static readonly int MAX_THREAD_GROUP = 64000 / THREAD_PER_GROUP;

        //private static readonly int KERNEL_COUNT = 0;
        private static readonly int KERNEL_APPEND = 1;

        private static readonly string KW_DATA_TYPE_INSTANCE_SAMPLE = "DATA_TYPE_INSTANCE_SAMPLE";
        private static readonly string KW_DATA_TYPE_POSITION_SAMPLE = "DATA_TYPE_POSITION_SAMPLE";

        private static ComputeShader s_computeShader;

        /// <summary>
        /// Creates a destination buffer by remapping sample positions from source bounds into destination bounds.
        /// </summary>
        /// <typeparam name="T">
        /// Sample layout stored in the buffer. Supported values are <see cref="InstanceSample"/> and <see cref="PositionSample"/>.
        /// </typeparam>
        /// <param name="srcBuffer">
        /// Source compute buffer storing packed float data for one supported sample type.
        /// The buffer length must be an exact multiple of that sample type's float count.
        /// </param>
        /// <param name="inBounds">
        /// Source rectangle in normalized sample space. Samples are interpreted relative to this region before being remapped.
        /// </param>
        /// <param name="outBounds">
        /// Destination rectangle in normalized sample space. Output sample positions are written relative to this region.
        /// </param>
        /// <returns>
        /// A newly allocated compute buffer with the same packed layout as <paramref name="srcBuffer"/>, cleared to zero before remapped samples are appended.
        /// </returns>
        /// <exception cref="System.ArgumentException">
        /// Thrown when <typeparamref name="T"/> is not a supported packed sample type, or when the source buffer size does not match that packed layout.
        /// </exception>
        /// <remarks>
        /// The runtime allocates one float-based destination buffer sized for the full source sample count, then lets the compute shader
        /// append remapped samples into it. Callers own the returned buffer and are responsible for releasing it.
        /// </remarks>
        public static ComputeBuffer CopyFrom<T>(ComputeBuffer srcBuffer, Rect inBounds, Rect outBounds)
        {
            int structSize;
            string dataTypeKw;
            if (typeof(T).Equals(typeof(PositionSample)))
            {
                structSize = PositionSample.SIZE;
                dataTypeKw = KW_DATA_TYPE_POSITION_SAMPLE;
            }
            else if (typeof(T).Equals(typeof(InstanceSample)))
            {
                structSize = InstanceSample.SIZE;
                dataTypeKw = KW_DATA_TYPE_INSTANCE_SAMPLE;
            }
            else
            {
                throw new System.ArgumentException($"Buffer data type must be {typeof(InstanceSample).Name} or {typeof(PositionSample).Name}");
            }

            if (srcBuffer.count % structSize != 0)
            {
                throw new System.ArgumentException("Source buffer size & struct size not match");
            }

            s_computeShader = Resources.Load<ComputeShader>(COMPUTE_SHADER_NAME);
            s_computeShader.SetVector(IN_BOUNDS, new Vector4(inBounds.min.x, inBounds.min.y, inBounds.size.x, inBounds.size.y));
            s_computeShader.SetVector(OUT_BOUNDS, new Vector4(outBounds.min.x, outBounds.min.y, outBounds.size.x, outBounds.size.y));

            s_computeShader.shaderKeywords = null;
            s_computeShader.EnableKeyword(dataTypeKw);
                        
            int instanceCount = srcBuffer.count / structSize;
            ComputeBuffer destBuffer = new ComputeBuffer(instanceCount * structSize, sizeof(float));
            GraphicsUtils.ClearWithZeros(destBuffer);

            s_computeShader.SetBuffer(KERNEL_APPEND, SAMPLES, srcBuffer);
            s_computeShader.SetBuffer(KERNEL_APPEND, DEST_SAMPLES, destBuffer);

            int totalThreadGroupX = (instanceCount + THREAD_PER_GROUP - 1) / THREAD_PER_GROUP;
            int iteration = (totalThreadGroupX + MAX_THREAD_GROUP - 1) / MAX_THREAD_GROUP;
            for (int i = 0; i < iteration; ++i)
            {
                int threadGroupX = Mathf.Min(MAX_THREAD_GROUP, totalThreadGroupX);
                totalThreadGroupX -= MAX_THREAD_GROUP;
                int baseIndex = i * MAX_THREAD_GROUP * THREAD_PER_GROUP;
                s_computeShader.SetInt(BASE_INDEX, baseIndex);
                s_computeShader.Dispatch(KERNEL_APPEND, threadGroupX, 1, 1);
            }

            Resources.UnloadAsset(s_computeShader);
            return destBuffer;
        }
    }
}
#endif


