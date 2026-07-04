#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Provides low-level clearing helpers for render textures and compute buffers.
    /// </summary>
    public static class GraphicsUtils
    {
        /// <summary>
        /// Creates a standalone square render texture, enables random write, and clears it to zero.
        /// </summary>
        public static RenderTexture CreateBlankRT(int resolution, RenderTextureFormat format)
        {
            RenderTexture rt = new RenderTexture(resolution, resolution, 0, format, RenderTextureReadWrite.Linear);
            rt.filterMode = FilterMode.Bilinear;
            rt.wrapMode = TextureWrapMode.Clamp;
            rt.enableRandomWrite = true;
            rt.Create();
            ClearWithZeros(rt);
            return rt;
        }

        /// <summary>
        /// Creates a collection of standalone square render textures, each enabled for random write and cleared to zero.
        /// </summary>
        public static List<RenderTexture> CreateBlankRTCollection(int count, int resolution, RenderTextureFormat format)
        {
            List<RenderTexture> renderTextures = new List<RenderTexture>(count);
            for (int i = 0; i < count; ++i)
            {
                renderTextures.Add(CreateBlankRT(resolution, format));
            }

            return renderTextures;
        }

        /// <summary>
        /// Gets a temporary square render texture, enables random write, and clears it to zero.
        /// </summary>
        public static RenderTexture GetBlankTempRT(int resolution, RenderTextureFormat format)
        {
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(resolution, resolution, format, 0);
            descriptor.enableRandomWrite = true;
            descriptor.sRGB = false;

            RenderTexture rt = RenderTexture.GetTemporary(descriptor);
            rt.filterMode = FilterMode.Bilinear;
            rt.wrapMode = TextureWrapMode.Clamp;
            ClearWithZeros(rt);
            return rt;
        }

        /// <summary>
        /// Releases a temporary render texture obtained from <see cref="GetBlankTempRT"/>.
        /// </summary>
        public static void ReleaseTempRT(RenderTexture rt)
        {
            if (rt == null)
                return;

            RenderTexture.ReleaseTemporary(rt);
        }

        /// <summary>
        /// Clears a render texture to zero.
        /// </summary>
        public static void ClearWithZeros(RenderTexture rt)
        {
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = previousActive;
        }

        /// <summary>
        /// Reads the full render texture into an existing Texture2D while preserving the previous active render texture.
        /// Assumes source and destination have the same dimensions and compatible formats.
        /// </summary>
        public static void ReadRenderTexture(RenderTexture source, Texture2D destination)
        {
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = source;
            destination.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            destination.Apply();
            RenderTexture.active = previousActive;
        }

        /// <summary>
        /// Creates a standalone render texture with a copy of the source texture content.
        /// </summary>
        public static RenderTexture CloneToRenderTexture(Texture source, RenderTextureFormat fallbackFormat = RenderTextureFormat.ARGB32)
        {
            if (source == null)
            {
                throw new System.ArgumentNullException(nameof(source));
            }
            if (source.width <= 0 || source.height <= 0)
            {
                throw new System.ArgumentException($"Cannot clone texture with invalid dimensions: {source.width}x{source.height}.", nameof(source));
            }

            RenderTexture sourceRenderTexture = source as RenderTexture;
            if (sourceRenderTexture != null)
            {
                if (!sourceRenderTexture.IsCreated())
                {
                    throw new System.ArgumentException("Cannot clone a RenderTexture that has not been created.", nameof(source));
                }

                RenderTextureDescriptor descriptor = sourceRenderTexture.descriptor;
                RenderTexture copy = new RenderTexture(descriptor);
                copy.wrapMode = sourceRenderTexture.wrapMode;
                copy.filterMode = sourceRenderTexture.filterMode;
                copy.antiAliasing = sourceRenderTexture.antiAliasing;
                copy.Create();
                UnityEngine.Graphics.Blit(source, copy);
                return copy;
            }
            else
            {
                RenderTexture copy = new RenderTexture(source.width, source.height, 0, fallbackFormat, RenderTextureReadWrite.Linear);
                copy.wrapMode = TextureWrapMode.Clamp;
                copy.filterMode = FilterMode.Point;
                copy.antiAliasing = 1;
                copy.enableRandomWrite = true;
                copy.Create();
                UnityEngine.Graphics.Blit(source, copy);
                return copy;
            }
        }

        private static readonly string CLEAR_BUFFER_SHADER_NAME = "Vista/Shaders/BufferClear";
        private static readonly int BUFFER = Shader.PropertyToID("_Buffer");
        private static readonly int BASE_INDEX = Shader.PropertyToID("_BaseIndex");
        private static readonly int COUNT = Shader.PropertyToID("_Count");
        private static readonly int KERNEL = 0;

        private static ComputeShader s_clearBufferShader;

        /// <summary>
        /// Clears a compute buffer to zero using the buffer-clear compute shader.
        /// </summary>
        /// <param name="buffer">
        /// Buffer to clear.
        /// </param>
        /// <remarks>
        /// The dispatch runs in chunks for large buffers. Vista's runtime typically uses buffers whose
        /// counts are multiples of 8 because the shader kernel is organized around 8-thread groups.
        /// </remarks>
        public static void ClearWithZeros(ComputeBuffer buffer)
        {
            //#if UNITY_EDITOR
            //            if (buffer.count % 8 != 0)
            //            {
            //                Debug.LogWarning("Attempting to use shader to clear a buffer with non-multiple-of-8 count. This may failed.");
            //            }
            //#endif  

            if (s_clearBufferShader == null)
            {
                s_clearBufferShader = Resources.Load<ComputeShader>(CLEAR_BUFFER_SHADER_NAME);
            }
            s_clearBufferShader.SetBuffer(KERNEL, BUFFER, buffer);
            s_clearBufferShader.SetInt(COUNT, buffer.count);

            int maxElementPerStep = 64000 * 8;
            int remainingCount = buffer.count;
            int baseIndex = 0;
            while (remainingCount > 0)
            {
                int count = Mathf.Min(maxElementPerStep, remainingCount);
                s_clearBufferShader.SetInt(BASE_INDEX, baseIndex);
                s_clearBufferShader.Dispatch(KERNEL, (count + 7) / 8, 1, 1);
                remainingCount -= count;
                baseIndex += count;
            }
        }
    }
}
#endif
