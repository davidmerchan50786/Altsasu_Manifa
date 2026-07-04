#if VISTA
using Pinwheel.Vista.Graphics;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    public static class NodeLibraryUtilities
    {
        public static class MathNode
        {
            private static readonly string SHADER_NAME = "Hidden/Vista/Graph/Math";
            private static readonly int MAIN_TEX = Shader.PropertyToID("_MainTex");
            private static readonly int CONFIGS = Shader.PropertyToID("_Configs");

            private static readonly string TEMP_RT_NAME = "~TempRT";

            private static Material s_material;
            public static void Invert(GraphContext context, RenderTexture targetRt)
            {
                DataPool.RtDescriptor tempRtDesc = DataPool.RtDescriptor.Create(targetRt.width, targetRt.height);
                RenderTexture tempRt = context.CreateTemporaryRT(tempRtDesc, TEMP_RT_NAME);
                Drawing.Blit(targetRt, tempRt);

                s_material = new Material(ShaderUtilities.Find(SHADER_NAME));
                s_material.SetTexture(MAIN_TEX, tempRt);

                Graph.MathNode.MathConfig c = new Graph.MathNode.MathConfig()
                {
                    enabled = true,
                    number = 1f,
                    ops = Graph.MathNode.Operator.OneMinus
                };
                Vector4[] configArray = new Vector4[] { c.ToVector() };
                s_material.SetVectorArray(CONFIGS, configArray);
                Drawing.DrawQuad(targetRt, s_material, 0);

                context.ReleaseTemporary(TEMP_RT_NAME);
                Object.DestroyImmediate(s_material);
            }
            public static void Multiply(GraphContext context, RenderTexture targetRt, float multiplier)
            {
                DataPool.RtDescriptor tempRtDesc = DataPool.RtDescriptor.Create(targetRt.width, targetRt.height);
                RenderTexture tempRt = context.CreateTemporaryRT(tempRtDesc, TEMP_RT_NAME);
                Drawing.Blit(targetRt, tempRt);

                s_material = new Material(ShaderUtilities.Find(SHADER_NAME));
                s_material.SetTexture(MAIN_TEX, tempRt);

                Graph.MathNode.MathConfig c = new Graph.MathNode.MathConfig()
                {
                    enabled = true,
                    number = multiplier,
                    ops = Graph.MathNode.Operator.Multiply
                };
                Vector4[] configArray = new Vector4[] { c.ToVector() };
                s_material.SetVectorArray(CONFIGS, configArray);
                Drawing.DrawQuad(targetRt, s_material, 0);

                context.ReleaseTemporary(TEMP_RT_NAME);
                Object.DestroyImmediate(s_material);
            }
        }
        public static class ThinOutNode
        {
            private static readonly string COMPUTE_SHADER_NAME = "Vista/Shaders/Graph/ThinOut";
            private static readonly int POSITION_INPUT = Shader.PropertyToID("_PositionInput");
            private static readonly int MASK = Shader.PropertyToID("_Mask");
            private static readonly int MASK_MULTIPLIER = Shader.PropertyToID("_MaskMultiplier");
            private static readonly int POSITION_OUTPUT = Shader.PropertyToID("_PositionOutput");
            private static readonly int SEED = Shader.PropertyToID("_Seed");
            private static readonly int BASE_INDEX = Shader.PropertyToID("_BaseIndex");
            private static readonly int INSTANCE_COUNT = Shader.PropertyToID("_InstanceCount");
            private static readonly int KERNEL_INDEX = 0;

            private static readonly int THREAD_PER_GROUP = 8;
            private static readonly int MAX_THREAD_GROUP = 64000 / THREAD_PER_GROUP;

            private static readonly string HAS_MASK_KW = "HAS_MASK";
            private static ComputeShader s_computeShader;
            public static void Execute(GraphContext context, ComputeBuffer inputPositionBuffer, Texture maskTexture, float maskMultiplier, int seed, ComputeBuffer outputPositionBuffer)
            {
                s_computeShader = Resources.Load<ComputeShader>(COMPUTE_SHADER_NAME);
                s_computeShader.SetBuffer(KERNEL_INDEX, POSITION_OUTPUT, outputPositionBuffer);
                s_computeShader.SetBuffer(KERNEL_INDEX, POSITION_INPUT, inputPositionBuffer);

                s_computeShader.SetFloat(MASK_MULTIPLIER, maskMultiplier);
                if (maskTexture != null)
                {
                    s_computeShader.SetTexture(KERNEL_INDEX, MASK, maskTexture);
                    s_computeShader.EnableKeyword(HAS_MASK_KW);
                }
                else
                {
                    s_computeShader.DisableKeyword(HAS_MASK_KW);
                }

                int baseSeed = context.GetArg(Args.SEED).intValue;
                System.Random rnd = new System.Random(seed ^ baseSeed);
                s_computeShader.SetVector(SEED, new Vector4((float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble()));

                int instanceCount = inputPositionBuffer.count / PositionSample.SIZE;
                s_computeShader.SetInt(INSTANCE_COUNT, instanceCount);
                int totalThreadGroupX = (instanceCount + THREAD_PER_GROUP - 1) / THREAD_PER_GROUP;
                int iteration = (totalThreadGroupX + MAX_THREAD_GROUP - 1) / MAX_THREAD_GROUP;
                for (int i = 0; i < iteration; ++i)
                {
                    int threadGroupX = Mathf.Min(MAX_THREAD_GROUP, totalThreadGroupX);
                    totalThreadGroupX -= MAX_THREAD_GROUP;
                    int baseIndex = i * MAX_THREAD_GROUP * THREAD_PER_GROUP;
                    s_computeShader.SetInt(BASE_INDEX, baseIndex);
                    s_computeShader.Dispatch(KERNEL_INDEX, threadGroupX, 1, 1);
                }
                Resources.UnloadAsset(s_computeShader);
            }
        }
        public static class LevelsNode
        {
            private static readonly string SHADER_NAME = "Hidden/Vista/Graph/Levels";
            private static readonly int MAIN_TEX = Shader.PropertyToID("_MainTex");
            private static readonly int IN_LOW = Shader.PropertyToID("_InLow");
            private static readonly int IN_MID = Shader.PropertyToID("_InMid");
            private static readonly int IN_HIGH = Shader.PropertyToID("_InHigh");
            private static readonly int OUT_LOW = Shader.PropertyToID("_OutLow");
            private static readonly int OUT_HIGH = Shader.PropertyToID("_OutHigh");
            private static readonly int PASS = 0;
            public static void Execute(Texture inputTexture, RenderTexture targetRt, float inLow, float inMid, float inHigh, float outLow, float outHigh)
            {
                Material material = new Material(ShaderUtilities.Find(SHADER_NAME));
                material.SetTexture(MAIN_TEX, inputTexture);
                material.SetFloat(IN_LOW, inLow);
                material.SetFloat(IN_MID, Mathf.Lerp(inLow, inHigh, inMid));
                material.SetFloat(IN_HIGH, inHigh);
                material.SetFloat(OUT_LOW, outLow);
                material.SetFloat(OUT_HIGH, outHigh);

                Drawing.DrawQuad(targetRt, material, PASS);
                Object.DestroyImmediate(material);
            }
        }
        public static class NoiseNode
        {
            public const int WARP_NONE = 0;
            public const int WARP_ANGULAR = 1;
            public const int WARP_DIRECTIONAL = 2;
            public const int DERIVATIVE_POW = 0;
            public const int DERIVATIVE_LINEAR = 1;
            public class Params
            {
                public Vector2 offset = Vector2.zero;
                public float scale = 500;
                public float lacunarity = 2;
                public float persistence = 1f / 3f;
                public int layerCount = 4;
                public NoiseMode mode = NoiseMode.Perlin01;
                public int layerDerivative = DERIVATIVE_LINEAR;
                public bool flipSign = false;
                public int warpMode = WARP_NONE;
                public float warpAngleMin = -360;
                public float warpAngleMax = 360;
                public float warpIntensity = 1;
                public Vector4 worldBounds = new Vector4(0, 0, 1000, 1000);
                public AnimationCurve remapCurve = AnimationCurve.Linear(0, 0, 1, 1);
                public bool applyRemapPerLayer = true;
                public int seed = 0;
            }

            private static readonly string SHADER_NAME = "Hidden/Vista/Graph/Noise";
            private static readonly int WORLD_BOUNDS = Shader.PropertyToID("_WorldBounds");
            private static readonly int OFFSET = Shader.PropertyToID("_Offset");
            private static readonly int SCALE = Shader.PropertyToID("_Scale");
            private static readonly int LACUNARITY = Shader.PropertyToID("_Lacunarity");
            private static readonly int PERSISTENCE = Shader.PropertyToID("_Persistence");
            private static readonly int LAYER_COUNT = Shader.PropertyToID("_LayerCount");
            private static readonly int NOISE_TYPE = Shader.PropertyToID("_NoiseType");
            private static readonly int WARP_ANGLE_MIN = Shader.PropertyToID("_WarpAngleMin");
            private static readonly int WARP_ANGLE_MAX = Shader.PropertyToID("_WarpAngleMax");
            private static readonly int WARP_INTENSITY = Shader.PropertyToID("_WarpIntensity");
            private static readonly int TEXEL_SIZE = Shader.PropertyToID("_TexelSize");
            private static readonly int TEXTURE_SIZE = Shader.PropertyToID("_TextureSize");
            private static readonly int REMAP_TEX = Shader.PropertyToID("_RemapTex");
            private static readonly int SEED = Shader.PropertyToID("_Seed");
            private static readonly int LAYER_DERIVATIVE = Shader.PropertyToID("_LayerDerivative");
            private static readonly int APPLY_REMAP_PER_LAYER = Shader.PropertyToID("_ApplyRemapPerLayer");
            private static readonly int FLIP_SIGN = Shader.PropertyToID("_FlipSign");

            private static readonly int PASS_NO_WARP = 0;
            private static readonly int PASS_ANGULAR_WARP = 1;
            private static readonly int PASS_DIRECTIONAL_WARP = 2;
            public static void Execute(RenderTexture targetRt, Params p)
            {
                Material m_material = new Material(ShaderUtilities.Find(SHADER_NAME));
                m_material.SetVector(OFFSET, p.offset);
                m_material.SetFloat(SCALE, p.scale);
                m_material.SetFloat(LACUNARITY, p.lacunarity);
                m_material.SetFloat(PERSISTENCE, p.persistence);
                m_material.SetInt(LAYER_COUNT, p.layerCount);
                m_material.SetInt(NOISE_TYPE, (int)p.mode);
                m_material.SetVector(TEXTURE_SIZE, new Vector4(targetRt.width, targetRt.height, 0, 0));
                m_material.SetVector(WORLD_BOUNDS, p.worldBounds);
                m_material.SetInt(SEED, p.seed);
                m_material.SetInt(LAYER_DERIVATIVE, p.layerDerivative);
                m_material.SetInt(APPLY_REMAP_PER_LAYER, p.applyRemapPerLayer ? 1 : 0);
                m_material.SetInt(FLIP_SIGN, p.flipSign ? 1 : 0);

                Texture2D remapTex = Utilities.TextureFromCurve(p.remapCurve);
                m_material.SetTexture(REMAP_TEX, remapTex);

                if (p.warpMode == WARP_ANGULAR)
                {
                    m_material.SetFloat(WARP_ANGLE_MIN, p.warpAngleMin * Mathf.Deg2Rad);
                    m_material.SetFloat(WARP_ANGLE_MAX, p.warpAngleMax * Mathf.Deg2Rad);
                    m_material.SetFloat(WARP_INTENSITY, p.warpIntensity);
                    Drawing.DrawQuad(targetRt, m_material, PASS_ANGULAR_WARP);
                }
                else if (p.warpMode == WARP_DIRECTIONAL)
                {
                    m_material.SetFloat(WARP_INTENSITY, p.warpIntensity);
                    m_material.SetVector(TEXEL_SIZE, targetRt.texelSize);
                    Drawing.DrawQuad(targetRt, m_material, PASS_DIRECTIONAL_WARP);
                }
                else
                {
                    Drawing.DrawQuad(targetRt, m_material, PASS_NO_WARP);
                }

                Object.DestroyImmediate(remapTex);
                Object.DestroyImmediate(m_material);
            }
        }
        public static class ClampNode
        {
            private static readonly string SHADER_NAME = "Hidden/Vista/Graph/Clamp";
            private static readonly int MAIN_TEX = Shader.PropertyToID("_MainTex");
            private static readonly int MIN = Shader.PropertyToID("_Min");
            private static readonly int MAX = Shader.PropertyToID("_Max");
            private static readonly int PASS = 0;
            public static void Execute(Texture inputTexture, RenderTexture targetRt, float min, float max)
            {
                Material material = new Material(ShaderUtilities.Find(SHADER_NAME));
                material.SetTexture(MAIN_TEX, inputTexture);
                material.SetFloat(MIN, min);
                material.SetFloat(MAX, max);

                Drawing.DrawQuad(targetRt, material, PASS);
                Object.DestroyImmediate(material);
            }
        }
        public static class ConvexNode
        {
            private static readonly string SHADER_NAME = "Hidden/Vista/Graph/Convex";
            private static readonly int MAIN_TEX = Shader.PropertyToID("_MainTex");
            private static readonly int EPSILON = Shader.PropertyToID("_Epsilon");
            private static readonly int TOLERANCE = Shader.PropertyToID("_Tolerance");
            private static readonly int PASS = 0;
            public static void Execute(Texture inputTexture, RenderTexture targetRt, float epsilon, int tolerance)
            {
                Material material = new Material(ShaderUtilities.Find(SHADER_NAME));
                material.SetTexture(MAIN_TEX, inputTexture);
                material.SetFloat(EPSILON, epsilon);
                material.SetFloat(TOLERANCE, tolerance);
                Drawing.DrawQuad(targetRt, material, PASS);
                Object.DestroyImmediate(material);
            }
        }
        public static class DistanceFieldNode
        {
            private static readonly string COMPUTE_SHADER_NAME = "Vista/Shaders/Graph/DistanceField";
            private static readonly int INPUT_TEX = Shader.PropertyToID("_InputTex");
            private static readonly int TEMP_TEX = Shader.PropertyToID("_TempTex");
            private static readonly int OUTPUT_TEX = Shader.PropertyToID("_OutputTex");
            private static readonly int OUTPUT_RESOLUTION = Shader.PropertyToID("_OutputResolution");
            private static readonly int EPSILON = Shader.PropertyToID("_Epsilon");
            private static readonly int ITERATION = Shader.PropertyToID("_Iteration");

            private static readonly int KERNEL_INIT = 0;
            private static readonly int KERNEL_SUM_VALUE_1 = 1;
            private static readonly int KERNEL_SUM_VALUE_2 = 2;
            private static readonly int KERNEL_WRITE_VALUE = 3;
            private static readonly int KERNEL_FINALIZE = 4;
            public static void Execute(Texture inputTexture, RenderTexture targetRt, int iterationCount)
            {
                int resolution = targetRt.width;
                RenderTextureDescriptor desc = targetRt.descriptor;
                desc.width = resolution;
                desc.height = resolution;
                desc.depthBufferBits = 0;
                desc.colorFormat = RenderTextureFormat.ARGBFloat;
                desc.enableRandomWrite = true;
                RenderTexture tempRt = RenderTexture.GetTemporary(desc);
                GraphicsUtils.ClearWithZeros(tempRt);

                ComputeShader m_computeShader = Resources.Load<ComputeShader>(COMPUTE_SHADER_NAME);
                m_computeShader.SetTexture(KERNEL_INIT, INPUT_TEX, inputTexture);
                m_computeShader.SetTexture(KERNEL_INIT, TEMP_TEX, tempRt);
                m_computeShader.SetTexture(KERNEL_SUM_VALUE_1, TEMP_TEX, tempRt);
                m_computeShader.SetTexture(KERNEL_SUM_VALUE_2, TEMP_TEX, tempRt);
                m_computeShader.SetTexture(KERNEL_WRITE_VALUE, TEMP_TEX, tempRt);
                m_computeShader.SetTexture(KERNEL_FINALIZE, TEMP_TEX, tempRt);
                m_computeShader.SetTexture(KERNEL_FINALIZE, OUTPUT_TEX, targetRt);
                m_computeShader.SetFloat(OUTPUT_RESOLUTION, targetRt.width);
                m_computeShader.SetFloat(EPSILON, 1.0f / iterationCount);

                int threadGroupX = (resolution + 7) / 8;
                int threadGroupY = 1;
                int threadGroupZ = (resolution + 7) / 8;
                m_computeShader.Dispatch(KERNEL_INIT, threadGroupX, threadGroupY, threadGroupZ);

                for (int i = 0; i < iterationCount; ++i)
                {
                    m_computeShader.SetInt(ITERATION, i);

                    int kernel = i % 2 == 0 ? KERNEL_SUM_VALUE_1 : KERNEL_SUM_VALUE_2;
                    m_computeShader.Dispatch(kernel, threadGroupX, threadGroupY, threadGroupZ);
                    m_computeShader.Dispatch(KERNEL_WRITE_VALUE, threadGroupX, threadGroupY, threadGroupZ);
                }

                m_computeShader.Dispatch(KERNEL_FINALIZE, threadGroupX, threadGroupY, threadGroupZ);

                Resources.UnloadAsset(m_computeShader);
            }
        }
        public static class CombineNode
        {
            public const int MODE_ADD = 0;
            public const int MODE_SUB = 1;
            public const int MODE_MUL = 2;
            public const int MODE_MAX = 3;
            public const int MODE_MIN = 4;
            public const int MODE_LINEAR = 5;
            public const int MODE_DIFF = 6;

            private static readonly string SHADER_NAME = "Hidden/Vista/Graph/Combine";
            private static readonly int BACKGROUND = Shader.PropertyToID("_Background");
            private static readonly int BACKGROUND_MULTIPLIER = Shader.PropertyToID("_BackgroundMultiplier");
            private static readonly int FOREGROUND = Shader.PropertyToID("_Foreground");
            private static readonly int FOREGROUND_MULTIPLIER = Shader.PropertyToID("_ForegroundMultiplier");
            private static readonly int MASK = Shader.PropertyToID("_Mask");
            private static readonly int MASK_MULTIPLIER = Shader.PropertyToID("_MaskMultiplier");
            private static readonly int MODE = Shader.PropertyToID("_Mode");
            private static readonly int PASS = 0;
            public static void Execute(Texture backgroundTexture, Texture foregroundTexture, Texture maskTexture, int mode, RenderTexture targetRt, float bgMul = 1, float fgMul = 1, float maskMul = 1)
            {
                Material material = new Material(ShaderUtilities.Find(SHADER_NAME));
                material.SetTexture(BACKGROUND, backgroundTexture);
                material.SetFloat(BACKGROUND_MULTIPLIER, bgMul);
                material.SetTexture(FOREGROUND, foregroundTexture);
                material.SetFloat(FOREGROUND_MULTIPLIER, fgMul);
                material.SetTexture(MASK, maskTexture);
                material.SetFloat(MASK_MULTIPLIER, maskMul);
                material.SetInt(MODE, mode);

                Drawing.DrawQuad(targetRt, material, PASS);
                Object.DestroyImmediate(material);
            }
        }
        public static class BlurNode
        {
            private static readonly string SHADER_NAME = "Hidden/Vista/Graph/Blur";
            private static readonly int MAIN_TEX = Shader.PropertyToID("_MainTex");
            private static readonly int RADIUS = Shader.PropertyToID("_Radius");
            public static void Execute(Texture inputTexture, RenderTexture targetRt, int radius)
            {
                Material material = new Material(ShaderUtilities.Find(SHADER_NAME));
                material.SetTexture(MAIN_TEX, inputTexture);
                material.SetFloat(RADIUS, radius);
                Drawing.DrawQuad(targetRt, material, radius);
                Object.DestroyImmediate(material);
            }
        }
    }
}
#endif


