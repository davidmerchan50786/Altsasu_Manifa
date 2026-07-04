#if VISTA
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Compares two GPU textures by dispatching a pixel-by-pixel compute pass.
    /// </summary>
    public static class TextureComparator
    {
        private static readonly string COMPUTE_SHADER_NAME = "Vista/Shaders/Graph/TextureComparator";
        private static readonly string KERNEL_NAME = "CSMain";

        private static readonly int TEX_A = Shader.PropertyToID("_TexA");
        private static readonly int TEX_B = Shader.PropertyToID("_TexB");
        private static readonly int WIDTH = Shader.PropertyToID("_Width");
        private static readonly int HEIGHT = Shader.PropertyToID("_Height");
        private static readonly int RESULT = Shader.PropertyToID("_Result");

        private static ComputeShader s_shader;
        private static int s_kernelIndex = -1;
        private static readonly uint[] s_resultData = new uint[1];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            s_shader = null;
            s_kernelIndex = -1;
        }

        /// <summary>
        /// Returns true when two textures have identical dimensions and exact pixel values.
        /// </summary>
        public static bool AreEqual(Texture textureA, Texture textureB)
        {
            if (textureA == null && textureB == null)
            {
                return true;
            }
            if (textureA == null || textureB == null)
            {
                return false;
            }
            if (ReferenceEquals(textureA, textureB))
            {
                return true;
            }
            if (textureA.width != textureB.width || textureA.height != textureB.height)
            {
                return false;
            }
            if (!HaveCompatibleFormats(textureA, textureB))
            {
                return false;
            }

            ComputeShader shader = GetShader();
            if (shader == null)
            {
                return false;
            }

            using (ComputeBuffer resultBuffer = new ComputeBuffer(1, sizeof(uint)))
            {
                s_resultData[0] = 0;
                resultBuffer.SetData(s_resultData);

                shader.SetTexture(s_kernelIndex, TEX_A, textureA);
                shader.SetTexture(s_kernelIndex, TEX_B, textureB);
                shader.SetInt(WIDTH, textureA.width);
                shader.SetInt(HEIGHT, textureA.height);
                shader.SetBuffer(s_kernelIndex, RESULT, resultBuffer);
                shader.Dispatch(s_kernelIndex, (textureA.width + 7) / 8, (textureA.height + 7) / 8, 1);

                resultBuffer.GetData(s_resultData);
                return s_resultData[0] == 0; 
            }
        }

        private static ComputeShader GetShader()
        {
            if (s_shader == null)
            {
                s_shader = Resources.Load<ComputeShader>(COMPUTE_SHADER_NAME);
                if (s_shader == null)
                {
                    Debug.LogWarning($"Could not load compute shader '{COMPUTE_SHADER_NAME}'.");
                    return null;
                }
                s_kernelIndex = s_shader.FindKernel(KERNEL_NAME);
            }
            return s_shader;
        }

        private static bool HaveCompatibleFormats(Texture textureA, Texture textureB)
        {
            RenderTexture renderTextureA = textureA as RenderTexture;
            RenderTexture renderTextureB = textureB as RenderTexture;
            if (renderTextureA != null && renderTextureB != null)
            {
                return renderTextureA.format == renderTextureB.format;
            }

            Texture2D texture2DA = textureA as Texture2D;
            Texture2D texture2DB = textureB as Texture2D;
            if (texture2DA != null && texture2DB != null)
            {
                return texture2DA.format == texture2DB.format;
            }

            return true;
        }
    }
}
#endif
