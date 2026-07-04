#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Provides low-level rendering helpers backed by Vista shaders and compute shaders.
    /// </summary>
    /// <remarks>
    /// These helpers are small runtime building blocks used to generate derived textures such as object-space normals,
    /// solid-color fills, and value-remapped masks without duplicating shader setup code at each call site.
    /// </remarks>
    public static class VistaLib
    {
        private class ExtractNormalMapUtils
        {
            /// <summary>
            /// Shader property ID for the source height map.
            /// </summary>
            public static readonly int HEIGHT_MAP = Shader.PropertyToID("_HeightMap");
            /// <summary>
            /// Shader property ID for the object-space size vector used to scale height gradients.
            /// </summary>
            public static readonly int SIZE = Shader.PropertyToID("_Size");
            /// <summary>
            /// Shader property ID for the source texture resolution.
            /// </summary>
            public static readonly int RESOLUTION = Shader.PropertyToID("_Resolution");
            /// <summary>
            /// Shader property ID for the destination render texture.
            /// </summary>
            public static readonly int TARGET_RT = Shader.PropertyToID("_TargetRT");
            /// <summary>
            /// Resource path of the compute shader used to extract object-space normals from a height map.
            /// </summary>
            public static readonly string SHADER_NAME = "Vista/Shaders/ExtractNormalMap";
        }

        /// <summary>
        /// Generates an object-space normal map from a height map texture.
        /// </summary>
        /// <param name="targetRT">
        /// The destination render texture that receives the generated normal map. It must have the same resolution as
        /// <paramref name="heightMap"/>.
        /// </param>
        /// <param name="heightMap">The source height map texture to sample.</param>
        /// <param name="sizeOS">
        /// The object-space size represented by the height map. This is used to scale height differentials when computing
        /// the normal vectors.
        /// </param>
        /// <remarks>
        /// The method loads the compute shader from Resources each time it runs and dispatches it with 8x8-style thread
        /// groups derived from the target resolution. A debug assertion checks that source and destination resolutions
        /// match.
        /// </remarks>
        public static void ExtractNormalMapOS(RenderTexture targetRT, Texture heightMap, Vector3 sizeOS)
        {
            Debug.Assert(targetRT.width == heightMap.width && targetRT.height == heightMap.height, "Extractring normal map: targetRT & heightMap must have the same resolution");

            ComputeShader shader = Resources.Load<ComputeShader>(ExtractNormalMapUtils.SHADER_NAME);
            shader.SetVector(ExtractNormalMapUtils.SIZE, sizeOS);
            shader.SetVector(ExtractNormalMapUtils.RESOLUTION, new Vector4(targetRT.width, targetRT.height));
            shader.SetTexture(0, ExtractNormalMapUtils.HEIGHT_MAP, heightMap);
            shader.SetTexture(0, ExtractNormalMapUtils.TARGET_RT, targetRT);

            int threadGroupX = (targetRT.width + 7) / 8;
            int threadGroupY = 1;
            int threadGroupZ = (targetRT.height + 7) / 8;
            shader.Dispatch(0, threadGroupX, threadGroupY, threadGroupZ);
        }

        private class FillColorUtils
        {
            /// <summary>
            /// Shader property ID for the fill color.
            /// </summary>
            public static readonly int COLOR = Shader.PropertyToID("_Color");
            /// <summary>
            /// Shader property ID for the destination render texture.
            /// </summary>
            public static readonly int TARGET_RT = Shader.PropertyToID("_TargetRT");
            /// <summary>
            /// Resource path of the compute shader used to write a uniform color into a render texture.
            /// </summary>
            public static readonly string SHADER_NAME = "Vista/Shaders/FillColor";
        }

        /// <summary>
        /// Fills an entire render texture with a uniform color.
        /// </summary>
        /// <param name="targetRT">The destination render texture to overwrite.</param>
        /// <param name="color">The color to write into every pixel of <paramref name="targetRT"/>.</param>
        /// <remarks>
        /// The method uses a compute shader loaded from Resources and dispatches it over the full render texture using
        /// thread groups derived from the destination resolution.
        /// </remarks>
        public static void FillColor(RenderTexture targetRT, Color color)
        {
            ComputeShader shader = Resources.Load<ComputeShader>(FillColorUtils.SHADER_NAME);
            shader.SetVector(FillColorUtils.COLOR, color);
            shader.SetTexture(0, FillColorUtils.TARGET_RT, targetRT);

            int threadGroupX = (targetRT.width + 7) / 8;
            int threadGroupY = 1;
            int threadGroupZ = (targetRT.height + 7) / 8;
            shader.Dispatch(0, threadGroupX, threadGroupY, threadGroupZ);
        }

        private class RemapUtils
        {
            /// <summary>
            /// Resource path of the compute shader that scans a texture for integer-encoded min and max values.
            /// </summary>
            public static readonly string COMPUTE_SHADER_NAME = "Vista/Shaders/Graph/Remap";
            /// <summary>
            /// Shader property ID for the input texture.
            /// </summary>
            public static readonly int MAIN_TEX = Shader.PropertyToID("_MainTex");
            /// <summary>
            /// Shader property ID for the compute buffer that stores scanned min and max values.
            /// </summary>
            public static readonly int MIN_MAX_BUFFER = Shader.PropertyToID("_MinMaxBuffer");
            /// <summary>
            /// Shader property ID for the integer normalization limit used by the min/max scan.
            /// </summary>
            public static readonly int INT_LIMIT = Shader.PropertyToID("_IntLimit");
            /// <summary>
            /// Kernel index used for the min/max scan compute shader.
            /// </summary>
            public static readonly int KERNEL_INDEX = 0;

            /// <summary>
            /// Shader name of the fullscreen remap material pass.
            /// </summary>
            public static readonly string SHADER_NAME = "Hidden/Vista/Graph/Remap";
            /// <summary>
            /// Shader property ID for the scanned input minimum.
            /// </summary>
            public static readonly int IN_MIN = Shader.PropertyToID("_InMin");
            /// <summary>
            /// Shader property ID for the scanned input maximum.
            /// </summary>
            public static readonly int IN_MAX = Shader.PropertyToID("_InMax");
            /// <summary>
            /// Shader property ID for the desired output minimum.
            /// </summary>
            public static readonly int OUT_MIN = Shader.PropertyToID("_OutMin");
            /// <summary>
            /// Shader property ID for the desired output maximum.
            /// </summary>
            public static readonly int OUT_MAX = Shader.PropertyToID("_OutMax");
            /// <summary>
            /// Material pass index used for the fullscreen remap draw.
            /// </summary>
            public static readonly int PASS = 0;
        }

        /// <summary>
        /// Remaps the normalized values of an input texture into a caller-defined output range.
        /// </summary>
        /// <param name="targetRT">The destination render texture that receives the remapped result.</param>
        /// <param name="inputTexture">The source texture whose values should be scanned and remapped.</param>
        /// <param name="outMin">The minimum value of the destination range.</param>
        /// <param name="outMax">The maximum value of the destination range.</param>
        /// <remarks>
        /// For most textures, the method first runs a compute shader that scans the source texture for integer-encoded min
        /// and max values, then converts those values back into normalized floats by dividing by <see cref="int.MaxValue"/>.
        /// The fullscreen remap shader uses that scanned input range to stretch or compress the data into the requested
        /// output range. When <paramref name="inputTexture"/> is <see cref="Texture2D.blackTexture"/>, the input range is
        /// treated as zero-to-zero and the scan step is skipped.
        /// </remarks>
        public static void Remap(RenderTexture targetRT, Texture inputTexture, float outMin, float outMax)
        {
            int[] minMaxData = new int[2];
            if (inputTexture != Texture2D.blackTexture)
            {
                minMaxData[0] = int.MaxValue;
                minMaxData[1] = int.MinValue;
                ComputeShader cs = Resources.Load<ComputeShader>(RemapUtils.COMPUTE_SHADER_NAME);
                cs.SetTexture(RemapUtils.KERNEL_INDEX, RemapUtils.MAIN_TEX, inputTexture);

                ComputeBuffer minMaxBuffer = new ComputeBuffer(2, sizeof(int));
                minMaxBuffer.SetData(minMaxData);
                cs.SetBuffer(RemapUtils.KERNEL_INDEX, RemapUtils.MIN_MAX_BUFFER, minMaxBuffer);
                cs.SetInt(RemapUtils.INT_LIMIT, int.MaxValue);
                int threadGroupX = (inputTexture.width + 7) / 8;
                int threadGroupY = (inputTexture.height + 7) / 8;
                int threadGroupZ = 1;
                cs.Dispatch(RemapUtils.KERNEL_INDEX, threadGroupX, threadGroupY, threadGroupZ);

                minMaxBuffer.GetData(minMaxData);
                minMaxBuffer.Dispose();
                Resources.UnloadAsset(cs);
            }
            else
            {
                minMaxData[0] = 0;
                minMaxData[1] = 0;
            }

            Material mat = new Material(ShaderUtilities.Find(RemapUtils.SHADER_NAME));
            mat.SetTexture(RemapUtils.MAIN_TEX, inputTexture);
            mat.SetFloat(RemapUtils.IN_MIN, minMaxData[0] * 1.0f / int.MaxValue);
            mat.SetFloat(RemapUtils.IN_MAX, minMaxData[1] * 1.0f / int.MaxValue);
            mat.SetFloat(RemapUtils.OUT_MIN, outMin);
            mat.SetFloat(RemapUtils.OUT_MAX, outMax);
            Drawing.DrawQuad(targetRT, mat, RemapUtils.PASS);
            Object.DestroyImmediate(mat);
        }

        private class SmoothUtils
        {
            public static readonly string SHADER_NAME = "Hidden/Vista/Graph/Smooth";
            public static readonly int MAIN_TEX = Shader.PropertyToID("_MainTex");
            public static readonly int MASK_MAP = Shader.PropertyToID("_MaskMap");
            public static readonly int PASS = 0;
        }

        /// <summary>
        /// Applies Vista's iterative smooth filter to a render texture in-place using a temporary ping-pong texture.
        /// </summary>
        /// <param name="targetRT">The destination texture that also acts as the initial source.</param>
        /// <param name="tempRT">Temporary texture used for ping-pong rendering. Must match <paramref name="targetRT"/> resolution.</param>
        /// <param name="maskTexture">Optional mask controlling where smoothing applies. Null is treated as white.</param>
        /// <param name="iterationCount">Number of smoothing iterations to execute.</param>
        public static void Smooth(RenderTexture targetRT, RenderTexture tempRT, Texture maskTexture, int iterationCount)
        {
            Material mat = CreateSmoothMaterial(maskTexture);
            ExecuteSmooth(targetRT, tempRT, mat, iterationCount);
            Object.DestroyImmediate(mat);
        }

        /// <summary>
        /// Applies Vista's iterative smooth filter progressively and yields after a configurable number of iterations.
        /// </summary>
        /// <param name="targetRT">The destination texture that also acts as the initial source.</param>
        /// <param name="tempRT">Temporary texture used for ping-pong rendering. Must match <paramref name="targetRT"/> resolution.</param>
        /// <param name="maskTexture">Optional mask controlling where smoothing applies. Null is treated as white.</param>
        /// <param name="iterationCount">Total number of smoothing iterations to execute.</param>
        /// <param name="iterationPerFrame">Number of iterations processed between yields.</param>
        public static IEnumerator SmoothProgressive(RenderTexture targetRT, RenderTexture tempRT, Texture maskTexture, int iterationCount, int iterationPerFrame)
        {
            Material mat = CreateSmoothMaterial(maskTexture);
            for (int i = 0; i < iterationCount; ++i)
            {
                SmoothIteration(targetRT, tempRT, mat, i);
                if (i % iterationPerFrame == 0)
                {
                    yield return null;
                }
            }

            FinalizeSmooth(targetRT, tempRT, iterationCount);
            Object.DestroyImmediate(mat);
        }

        private static Material CreateSmoothMaterial(Texture maskTexture)
        {
            Material mat = new Material(ShaderUtilities.Find(SmoothUtils.SHADER_NAME));
            mat.SetTexture(SmoothUtils.MASK_MAP, maskTexture != null ? maskTexture : Texture2D.whiteTexture);
            return mat;
        }

        private static void ExecuteSmooth(RenderTexture targetRT, RenderTexture tempRT, Material mat, int iterationCount)
        {
            for (int i = 0; i < iterationCount; ++i)
            {
                SmoothIteration(targetRT, tempRT, mat, i);
            }

            FinalizeSmooth(targetRT, tempRT, iterationCount);
        }

        private static void SmoothIteration(RenderTexture targetRT, RenderTexture tempRT, Material mat, int iterationIndex)
        {
            RenderTexture src = iterationIndex % 2 == 0 ? targetRT : tempRT;
            RenderTexture dst = iterationIndex % 2 == 0 ? tempRT : targetRT;
            mat.SetTexture(SmoothUtils.MAIN_TEX, src);
            Drawing.DrawQuad(dst, mat, SmoothUtils.PASS);
        }

        private static void FinalizeSmooth(RenderTexture targetRT, RenderTexture tempRT, int iterationCount)
        {
            if (iterationCount % 2 != 0)
            {
                Drawing.Blit(tempRT, targetRT);
            }
        }
    }
}
#endif


