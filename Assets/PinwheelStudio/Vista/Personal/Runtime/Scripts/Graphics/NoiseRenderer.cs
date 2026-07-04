#if VISTA
using Pinwheel.Vista.Graph;
using UnityEngine;

namespace Pinwheel.Vista.Graphics
{
    /// <summary>
    /// Renders a default layered noise pattern into a render texture.
    /// </summary>
    public static class NoiseRenderer
    {
        /// <summary>
        /// Parameters used by <see cref="Render(RenderTexture, Configs)"/>.
        /// </summary>
        public class Configs
        {
            /// <summary>
            /// Noise scale passed to the shader.
            /// </summary>
            public float scale { get; set; }
            /// <summary>
            /// World-bounds rectangle encoded as a vector for UV-to-world conversion in the noise shader.
            /// </summary>
            public Vector4 worldBounds { get; set; }
        }

        private static readonly string SHADER_NAME = "Hidden/Vista/Graph/Noise";
        private static readonly int WORLD_BOUNDS = Shader.PropertyToID("_WorldBounds");
        private static readonly int OFFSET = Shader.PropertyToID("_Offset");
        private static readonly int SCALE = Shader.PropertyToID("_Scale");
        private static readonly int LACUNARITY = Shader.PropertyToID("_Lacunarity");
        private static readonly int PERSISTENCE = Shader.PropertyToID("_Persistence");
        private static readonly int LAYER_COUNT = Shader.PropertyToID("_LayerCount");
        private static readonly int RANDOM_OFFSET = Shader.PropertyToID("_RandomOffset");
        private static readonly int NOISE_TYPE = Shader.PropertyToID("_NoiseType");
        private static readonly int TEXTURE_SIZE = Shader.PropertyToID("_TextureSize");
        private static readonly int REMAP_TEX = Shader.PropertyToID("_RemapTex");

        /// <summary>
        /// Renders the built-in default noise configuration into a target render texture.
        /// </summary>
        /// <param name="targetRt">
        /// Render target that receives the generated noise.
        /// </param>
        /// <param name="configs">
        /// Noise parameters controlling scale and world-bounds mapping.
        /// </param>
        /// <remarks>
        /// The current implementation always renders four-layer Perlin noise with fixed lacunarity,
        /// persistence, zero random offset, and a linear remap curve.
        /// </remarks>
        public static void Render(RenderTexture targetRt, Configs configs)
        {
            Material material = new Material(ShaderUtilities.Find(SHADER_NAME));
            material.SetVector(OFFSET, Vector4.zero);
            material.SetFloat(SCALE, configs.scale);
            material.SetFloat(LACUNARITY, 2);
            material.SetFloat(PERSISTENCE, 1f / 3f);
            material.SetInt(LAYER_COUNT, 4);
            material.SetInt(NOISE_TYPE, (int)NoiseMode.Perlin01);
            material.SetVector(TEXTURE_SIZE, new Vector4(targetRt.width, targetRt.height, 0, 0));
            material.SetVector(WORLD_BOUNDS, configs.worldBounds);
            material.SetVector(RANDOM_OFFSET, Vector4.zero);

            Texture2D remapTex = Utilities.TextureFromCurve(AnimationCurve.Linear(0, 0, 1, 1));
            material.SetTexture(REMAP_TEX, remapTex);
            Drawing.DrawQuad(targetRt, material, 0);
            Object.DestroyImmediate(remapTex);
            Object.DestroyImmediate(material);
        }
    }
}
#endif


