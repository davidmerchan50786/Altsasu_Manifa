#if VISTA
using System.Collections.Generic;
using UnityEngine;
using Pinwheel.Vista;
using Pinwheel.Vista.Diagnostics;
using Pinwheel.Vista.Graphics;

namespace Pinwheel.Vista.BigWorld
{
    /// <summary>
    /// Blends texture-based biome outputs into one composite <see cref="BiomeData"/> instance.
    /// </summary>
    /// <remarks>
    /// This class handles height maps, hole maps, mesh density maps, albedo maps, metallic maps,
    /// terrain texture weights, detail density maps, and generic textures. All blend operations are
    /// performed on the GPU with a shared compute shader.
    /// </remarks>
    /// <remarks>
    /// Call <see cref="Begin"/> once before invoking texture blend methods, pass the returned
    /// <see cref="BlendContext"/> into every texture blend call, then call <see cref="End"/> once
    /// to release shader resources and temporary height-win masks.
    /// </remarks>
    internal static class BiomeTextureBlend
    {
        private static readonly int SRC_TEXTURE = Shader.PropertyToID("_SrcTexture");
        private static readonly int WIN_TEXTURE = Shader.PropertyToID("_WinTexture");
        private static readonly int DEST_TEXTURE = Shader.PropertyToID("_DestTexture");
        private static readonly int BIOME_MASK_TEXTURE = Shader.PropertyToID("_BiomeMaskTexture");
        private static readonly int DEST_RESOLUTION = Shader.PropertyToID("_DestResolution");
        private static readonly int SRC_REMAP_OFFSET = Shader.PropertyToID("_SrcRemapOffset");
        private static readonly int SRC_REMAP_SCALE = Shader.PropertyToID("_SrcRemapScale");

        private static readonly string KW_SRC_IS_NULL = "SRC_IS_NULL";
        private const int HEIGHT_WIN_MASK_SMOOTH_ITERATION = 3;

        /// <summary>
        /// Stores resources shared by all texture blend calls in one biome blend pass.
        /// </summary>
        /// <remarks>
        /// The context keeps the compute shader loaded, caches all kernel indices, and stores height-win masks generated
        /// by <see cref="BlendHeightMap"/> for later texture, density, and population filtering.
        /// Create it with <see cref="Begin"/> and release it with <see cref="End"/>.
        /// </remarks>
        internal struct BlendContext
        {
            private static readonly string SHADER_NAME = "Vista/Shaders/BiomeTextureBlend";

            // Kernel names must match the #pragma kernel declarations in BiomeTextureBlend.compute.
            private static readonly string[] KERNEL_NAMES = new string[]
            {
                "BlendLinear",
                "BlendAdditive",
                "BlendSubtractive",
                "BlendMax",
                "BlendMin",
            };

            private static readonly string CLAMP_KERNEL_NAME = "Clamp";
            private static readonly string FILTER_BY_HEIGHT_KERNEL_NAME = "FilterByHeight";
            private static readonly string WIN_MASK_MAX_KERNEL_NAME = "WinMaskMax";
            private static readonly string WIN_MASK_MIN_KERNEL_NAME = "WinMaskMin";

            internal ComputeShader shader;
            /// <summary>
            /// Pre-resolved blend kernel indices in slot order: linear, additive, subtractive, max, and min.
            /// </summary>
            internal int[] kernels;
            internal int clampKernel;
            internal int filterByHeightKernel;
            internal int winMaskMaxKernel;
            internal int winMaskMinKernel;
            internal Dictionary<int, RenderTexture> heightWinMasksByBiome;

            /// <summary>
            /// Loads the texture blend shader and resolves all kernels required by the texture blend pipeline.
            /// </summary>
            /// <returns>A fully initialized texture blend context.</returns>
            internal static BlendContext Create()
            {
                BlendContext ctx = new BlendContext();
                ctx.shader = Resources.Load<ComputeShader>(SHADER_NAME);
                ctx.kernels = new int[KERNEL_NAMES.Length];
                for (int i = 0; i < KERNEL_NAMES.Length; ++i)
                {
                    ctx.kernels[i] = ctx.shader.FindKernel(KERNEL_NAMES[i]);
                }
                ctx.clampKernel = ctx.shader.FindKernel(CLAMP_KERNEL_NAME);
                ctx.filterByHeightKernel = ctx.shader.FindKernel(FILTER_BY_HEIGHT_KERNEL_NAME);
                ctx.winMaskMaxKernel = ctx.shader.FindKernel(WIN_MASK_MAX_KERNEL_NAME);
                ctx.winMaskMinKernel = ctx.shader.FindKernel(WIN_MASK_MIN_KERNEL_NAME);
                ctx.heightWinMasksByBiome = new Dictionary<int, RenderTexture>();
                return ctx;
            }

            /// <summary>
            /// Gets the height-win mask generated for one source biome.
            /// </summary>
            /// <param name="biomeIndex">Index of the source biome in the blend order.</param>
            /// <returns>The stored height-win mask, or null when none was generated for the biome.</returns>
            /// <remarks>
            /// Height-win masks are created only when a biome uses Height Win texture or population blending and its
            /// height blend mode is Keep Higher or Keep Lower.
            /// </remarks>
            internal RenderTexture GetHeightWinMask(int biomeIndex)
            {
                RenderTexture winMask;
                heightWinMasksByBiome.TryGetValue(biomeIndex, out winMask);
                return winMask;
            }

            /// <summary>
            /// Stores a height-win mask for one source biome, replacing any previous mask for that biome.
            /// </summary>
            /// <param name="biomeIndex">Index of the source biome in the blend order.</param>
            /// <param name="winMask">Mask texture owned by this context after the call.</param>
            internal void SetHeightWinMask(int biomeIndex, RenderTexture winMask)
            {
                RenderTexture existingWinMask = GetHeightWinMask(biomeIndex);
                if (existingWinMask != null)
                {
                    existingWinMask.Release();
                    Object.DestroyImmediate(existingWinMask);
                }

                heightWinMasksByBiome[biomeIndex] = winMask;
            }
        }

        /// <summary>
        /// Creates a texture blend context for one multi-biome blend pass.
        /// </summary>
        /// <returns>A context containing the loaded compute shader, resolved kernels, and height-win mask storage.</returns>
        /// <remarks>
        /// The returned context owns a loaded shader asset and any height-win masks stored during blending.
        /// Call <see cref="End"/> after all texture and buffer channels that need height-win masks have been processed.
        /// </remarks>
        internal static BlendContext Begin()
        {
            return BlendContext.Create();
        }

        /// <summary>
        /// Releases shader resources and temporary height-win masks held by a texture blend context.
        /// </summary>
        /// <param name="ctx">Context previously created by <see cref="Begin"/>.</param>
        internal static void End(BlendContext ctx)
        {
            foreach (RenderTexture rt in ctx.heightWinMasksByBiome.Values)
            {
                rt.Release();
                Object.DestroyImmediate(rt);
            }
            ctx.heightWinMasksByBiome.Clear();
            Resources.UnloadAsset(ctx.shader);
        }

        /// <summary>
        /// Records one texture blend pass in the Vista debugger.
        /// </summary>
        /// <param name="passIndex">Index of the source biome pass.</param>
        /// <param name="dest">Destination texture before the pass is applied.</param>
        /// <param name="src">Source texture used by the pass. Null is recorded explicitly.</param>
        /// <param name="biomeMask">Biome coverage mask used by the pass.</param>
        /// <param name="blendMode">Blend mode label recorded with the pass.</param>
        private static void DebuggerCapturePass(int passIndex, RenderTexture dest, Texture src, Texture biomeMask, object blendMode)
        {
            VistaDebugger.CaptureString($"Pass {passIndex}", "");
            VistaDebugger.Capture("Dest", dest);
            if (src != null && src is RenderTexture srcRT)
            {
                VistaDebugger.Capture("Src", srcRT);
            }
            else if (src != null)
            {
                VistaDebugger.CaptureString("Src", src.name);
            }
            else
            {
                VistaDebugger.CaptureString("Src", "null");
            }

            if (biomeMask != null && biomeMask is RenderTexture biomeMaskRT)
            {
                VistaDebugger.Capture("Biome mask", biomeMaskRT);
            }
            else if (biomeMask != null)
            {
                VistaDebugger.CaptureString("Biome mask", biomeMask.name);
            }
            else
            {
                VistaDebugger.CaptureString("Biome mask", "null");
            }
            VistaDebugger.CaptureString($"Blend mode: {blendMode.ToString()}\n--------------------------------\n");
        }

        /// <summary>
        /// Blends height maps from all source biomes into <paramref name="destData"/>.
        /// </summary>
        /// <param name="destData">Destination biome data receiving the blended height map.</param>
        /// <param name="srcBiomes">Ordered biome list that provides height blend options and transform context.</param>
        /// <param name="srcDatas">Biome output data parallel to <paramref name="srcBiomes"/>.</param>
        /// <param name="ctx">Texture blend context created by <see cref="Begin"/>.</param>
        /// <remarks>
        /// Biomes without height maps are skipped. A missing biome mask is treated as full coverage. The final result is
        /// clamped to [0, 1] after all height blend modes have been applied.
        /// </remarks>
        /// <remarks>
        /// When a biome uses Height Win texture or population blending and the height blend mode is Keep Higher or Keep
        /// Lower, this method also generates a per-biome height-win mask before the biome is blended into the destination.
        /// That mask is smoothed and stored in <paramref name="ctx"/> for later texture, density, and buffer filtering.
        /// </remarks>
        /// <remarks>
        /// If no source biome provides a height map, this method leaves the height channel unset in
        /// <paramref name="destData"/>.
        /// </remarks>
        internal static void BlendHeightMap(
            BiomeData destData, List<IBiome> srcBiomes, List<BiomeData> srcDatas, BlendContext ctx)
        {
            VistaDebugger.OpenScope("Height Map", DebugScopeType.BlendPass);

            // Find the highest resolution among all source height maps.
            int res = 0;
            RenderTextureFormat format = RenderTextureFormat.RFloat;
            for (int i = 0; i < srcDatas.Count; ++i)
            {
                RenderTexture rt = srcDatas[i].heightMap;
                if (rt != null)
                {
                    res = Mathf.Max(res, rt.width);
                    format = rt.format;
                }
            }

            if (res <= 0)
            {
                VistaDebugger.CaptureString("Missing output", "No biome produced a height map, leaving channel unset");
            }
            else
            {
                RenderTexture dest = GraphicsUtils.CreateBlankRT(res, format);

                for (int i = 0; i < srcDatas.Count; ++i)
                {
                    BiomeData srcData = srcDatas[i];
                    Texture mask = srcData.biomeMaskMap != null ? (Texture)srcData.biomeMaskMap : Texture2D.whiteTexture;
                    BiomeBlendOptions.HeightBlendMode blendMode = srcBiomes[i].blendOptions.heightMapBlendMode;

                    DebuggerCapturePass(i, dest, srcData.heightMap, mask, blendMode);

                    if (srcData.heightMap == null)
                        continue;

                    int kernelIndex = GetKernel(srcBiomes[i].blendOptions.heightMapBlendMode, ctx);
                    float normalizedOffset = 0f;
                    float normalizedScale = 1f;
                    if (srcBiomes[i].blendOptions.useTransformForHeightBlend)
                    {
                        Transform biomeTransform = srcBiomes[i].gameObject.transform;
                        VistaManager manager = srcBiomes[i].GetVistaManagerInstance();
                        if (manager != null && manager.terrainMaxHeight > 0)
                        {
                            normalizedOffset = biomeTransform.position.y / manager.terrainMaxHeight;
                            normalizedScale = biomeTransform.lossyScale.y;
                        }
                    }

                    if (srcBiomes[i].blendOptions.textureBlendMode == BiomeBlendOptions.TextureBlendMode.HeightWin ||
                        srcBiomes[i].blendOptions.populationBlendMode == BiomeBlendOptions.PopulationBlendMode.HeightWin)
                    {
                        if (blendMode == BiomeBlendOptions.HeightBlendMode.KeepHigher ||
                            blendMode == BiomeBlendOptions.HeightBlendMode.KeepLower)
                        {
                            RenderTexture heightWinMask = GraphicsUtils.CreateBlankRT(res, format);
                            RenderTexture tempWinMask = GraphicsUtils.GetBlankTempRT(res, format);
                            int winMaskKernel = blendMode == BiomeBlendOptions.HeightBlendMode.KeepHigher ? ctx.winMaskMaxKernel : ctx.winMaskMinKernel;
                            DispatchHeightWinMask(ctx.shader, srcData.heightMap, heightWinMask, dest, mask, winMaskKernel, normalizedOffset, normalizedScale);
                            VistaLib.Smooth(heightWinMask, tempWinMask, null, HEIGHT_WIN_MASK_SMOOTH_ITERATION);
                            GraphicsUtils.ReleaseTempRT(tempWinMask);
                            ctx.SetHeightWinMask(i, heightWinMask);
                        }
                    }

                    DispatchBlend(ctx.shader, srcData.heightMap, dest, mask, kernelIndex, normalizedOffset, normalizedScale);
                }

                // Clamp to [0, 1]. Raise mode can push values above 1.
                DispatchClamp(ctx.shader, dest, ctx.clampKernel);
                destData.heightMap = dest;
                VistaDebugger.Capture("Final (clamped)", dest);
            }

            VistaDebugger.CloseScope();
        }

        /// <summary>
        /// Blends hole maps from all source biomes into <paramref name="destData"/>.
        /// </summary>
        /// <param name="destData">Destination biome data receiving the blended hole map.</param>
        /// <param name="srcBiomes">Ordered biome list that provides hole blend options.</param>
        /// <param name="srcDatas">Biome output data parallel to <paramref name="srcBiomes"/>.</param>
        /// <param name="ctx">Texture blend context created by <see cref="Begin"/>.</param>
        /// <remarks>
        /// Hole maps use the convention 1.0 = hole and 0.0 = solid surface. Biomes without hole maps are skipped.
        /// A missing biome mask is treated as full coverage. No final clamp pass is applied because hole blend modes are
        /// expected to keep values in [0, 1].
        /// </remarks>
        /// <remarks>
        /// If no source biome provides a hole map, this method leaves the hole channel unset in
        /// <paramref name="destData"/>.
        /// </remarks>
        internal static void BlendHoleMap(
            BiomeData destData, List<IBiome> srcBiomes, List<BiomeData> srcDatas, BlendContext ctx)
        {
            VistaDebugger.OpenScope("Hole Map", DebugScopeType.BlendPass);

            int res = 0;
            RenderTextureFormat format = RenderTextureFormat.RFloat;
            for (int i = 0; i < srcDatas.Count; ++i)
            {
                RenderTexture rt = srcDatas[i].holeMap;
                if (rt != null)
                {
                    res = Mathf.Max(res, rt.width);
                    format = rt.format;
                }
            }

            if (res <= 0)
            {
                VistaDebugger.CaptureString("Missing output", "No biome produced a hole map, leaving channel unset");
            }
            else
            {
                RenderTexture dest = GraphicsUtils.CreateBlankRT(res, format);

                for (int i = 0; i < srcDatas.Count; ++i)
                {
                    BiomeData srcData = srcDatas[i];
                    Texture mask = srcData.biomeMaskMap != null ? (Texture)srcData.biomeMaskMap : Texture2D.whiteTexture;
                    BiomeBlendOptions.HoleBlendMode blendMode = srcBiomes[i].blendOptions.holeMapBlendMode;

                    DebuggerCapturePass(i, dest, srcData.holeMap, mask, blendMode);

                    if (srcData.holeMap == null)
                        continue;

                    int kernelIndex = GetKernel(blendMode, ctx);
                    DispatchBlend(ctx.shader, srcData.holeMap, dest, mask, kernelIndex);
                }

                destData.holeMap = dest;
                VistaDebugger.Capture("Final", dest);
            }

            VistaDebugger.CloseScope();
        }

        /// <summary>
        /// Blends mesh density maps from all source biomes into <paramref name="destData"/>.
        /// </summary>
        /// <param name="destData">Destination biome data receiving the blended mesh density map.</param>
        /// <param name="srcBiomes">Ordered biome list that provides mesh density blend options.</param>
        /// <param name="srcDatas">Biome output data parallel to <paramref name="srcBiomes"/>.</param>
        /// <param name="ctx">Texture blend context created by <see cref="Begin"/>.</param>
        /// <remarks>
        /// Mesh density controls polygon density on supported terrain backends such as Polaris. Biomes without mesh
        /// density maps are skipped. A missing biome mask is treated as full coverage. The final result is clamped to
        /// [0, 1] because additive modes can push density above the valid range.
        /// </remarks>
        /// <remarks>
        /// If no source biome provides a mesh density map, this method leaves the mesh density channel unset in
        /// <paramref name="destData"/>.
        /// </remarks>
        internal static void BlendMeshDensityMap(
            BiomeData destData, List<IBiome> srcBiomes, List<BiomeData> srcDatas, BlendContext ctx)
        {
            VistaDebugger.OpenScope("Mesh Density Map", DebugScopeType.BlendPass);

            int res = 0;
            RenderTextureFormat format = RenderTextureFormat.RFloat;
            for (int i = 0; i < srcDatas.Count; ++i)
            {
                RenderTexture rt = srcDatas[i].meshDensityMap;
                if (rt != null)
                {
                    res = Mathf.Max(res, rt.width);
                    format = rt.format;
                }
            }

            if (res <= 0)
            {
                VistaDebugger.CaptureString("Missing output", "No biome produced a mesh density map, leaving channel unset");
            }
            else
            {
                RenderTexture dest = GraphicsUtils.CreateBlankRT(res, format);

                for (int i = 0; i < srcDatas.Count; ++i)
                {
                    BiomeData srcData = srcDatas[i];
                    Texture mask = srcData.biomeMaskMap != null ? (Texture)srcData.biomeMaskMap : Texture2D.whiteTexture;
                    BiomeBlendOptions.MeshDensityBlendMode blendMode = srcBiomes[i].blendOptions.meshDensityBlendMode;

                    DebuggerCapturePass(i, dest, srcData.meshDensityMap, mask, blendMode);

                    if (srcData.meshDensityMap == null)
                        continue;

                    int kernelIndex = GetKernel(blendMode, ctx);
                    DispatchBlend(ctx.shader, srcData.meshDensityMap, dest, mask, kernelIndex);
                }

                // Clamp to [0, 1]. Add mode can push values above 1.
                DispatchClamp(ctx.shader, dest, ctx.clampKernel);
                destData.meshDensityMap = dest;
                VistaDebugger.Capture("Final (clamped)", dest);
            }
            VistaDebugger.CloseScope();
        }

        /// <summary>
        /// Blends albedo maps from all source biomes into <paramref name="destData"/>.
        /// </summary>
        /// <param name="destData">Destination biome data receiving the blended albedo map.</param>
        /// <param name="srcBiomes">Ordered biome list that provides texture blend options.</param>
        /// <param name="srcDatas">Biome output data parallel to <paramref name="srcBiomes"/>.</param>
        /// <param name="ctx">Texture blend context created by <see cref="Begin"/>.</param>
        /// <remarks>
        /// Albedo maps use Replace blending by default. In Height Win mode, the biome mask is first filtered by the
        /// height-win mask generated during <see cref="BlendHeightMap"/>. Biomes without albedo maps or without biome
        /// masks are skipped. If Height Win is requested but no height-win mask exists, the biome contributes no albedo
        /// and a configuration message is logged.
        /// </remarks>
        /// <remarks>
        /// If no source biome provides an albedo map, this method leaves the albedo channel unset in
        /// <paramref name="destData"/>.
        /// </remarks>
        internal static void BlendAlbedoMap(
            BiomeData destData, List<IBiome> srcBiomes, List<BiomeData> srcDatas, BlendContext ctx)
        {
            VistaDebugger.OpenScope("Albedo Map", DebugScopeType.BlendPass);

            int res = 0;
            RenderTextureFormat format = RenderTextureFormat.ARGB32;
            for (int i = 0; i < srcDatas.Count; ++i)
            {
                RenderTexture rt = srcDatas[i].albedoMap;
                if (rt != null)
                {
                    res = Mathf.Max(res, rt.width);
                    format = rt.format;
                }
            }

            if (res <= 0)
            {
                VistaDebugger.CaptureString("Missing output", "No biome produced an albedo map, leaving channel unset");
            }
            else
            {
                RenderTexture dest = GraphicsUtils.CreateBlankRT(res, format);
                int linearKernel = ctx.kernels[0];

                for (int i = 0; i < srcDatas.Count; ++i)
                {
                    BiomeData srcData = srcDatas[i];
                    RenderTexture srcMap = srcData.albedoMap;
                    Texture biomeMask = srcData.biomeMaskMap;

                    if (srcMap == null || biomeMask == null)
                    {
                        continue;
                    }

                    IBiome biome = srcBiomes[i];
                    if (biome.blendOptions.textureBlendMode == BiomeBlendOptions.TextureBlendMode.Replace)
                    {
                        DispatchBlend(ctx.shader, srcMap, dest, biomeMask, linearKernel);
                    }
                    else if (biome.blendOptions.textureBlendMode == BiomeBlendOptions.TextureBlendMode.HeightWin)
                    {
                        Texture heightWinMask = ctx.GetHeightWinMask(i);
                        if (heightWinMask == null)
                        {
                            Debug.Log(
                                $"[Biome Blend/Albedo] Biome '{biome.gameObject.name}' uses Height Win texture blending, but it has no height output. " +
                                $"Enable Height in the Data Mask and add a Height Output node.",
                                biome.gameObject);
                            continue;
                        }

                        RenderTexture filteredBiomeMask = GraphicsUtils.GetBlankTempRT(res, RenderTextureFormat.RFloat);
                        FilterBiomeMaskWithHeightWinMask(ctx, biomeMask, heightWinMask, filteredBiomeMask);
                        DispatchBlend(ctx.shader, srcMap, dest, filteredBiomeMask, linearKernel);

                        GraphicsUtils.ReleaseTempRT(filteredBiomeMask);
                    }
                    else
                    {
                        throw new System.NotImplementedException(
                            $"No albedo blend implementation for TextureBlendMode.{biome.blendOptions.textureBlendMode}. Add a case here.");
                    }
                }

                destData.albedoMap = dest;
                VistaDebugger.Capture("Final", dest);
            }
            VistaDebugger.CloseScope();
        }

        /// <summary>
        /// Blends metallic maps from all source biomes into <paramref name="destData"/>.
        /// </summary>
        /// <param name="destData">Destination biome data receiving the blended metallic map.</param>
        /// <param name="srcBiomes">Ordered biome list that provides texture blend options.</param>
        /// <param name="srcDatas">Biome output data parallel to <paramref name="srcBiomes"/>.</param>
        /// <param name="ctx">Texture blend context created by <see cref="Begin"/>.</param>
        /// <remarks>
        /// Metallic maps use Replace blending by default. In Height Win mode, the biome mask is first filtered by the
        /// height-win mask generated during <see cref="BlendHeightMap"/>. Biomes without metallic maps or without biome
        /// masks are skipped. If Height Win is requested but no height-win mask exists, the biome contributes no metallic
        /// map and a configuration message is logged.
        /// </remarks>
        /// <remarks>
        /// If no source biome provides a metallic map, this method leaves the metallic channel unset in
        /// <paramref name="destData"/>.
        /// </remarks>
        internal static void BlendMetallicMap(
            BiomeData destData, List<IBiome> srcBiomes, List<BiomeData> srcDatas, BlendContext ctx)
        {
            VistaDebugger.OpenScope("Metallic Map", DebugScopeType.BlendPass);

            int res = 0;
            RenderTextureFormat format = RenderTextureFormat.ARGB32;
            for (int i = 0; i < srcDatas.Count; ++i)
            {
                RenderTexture rt = srcDatas[i].metallicMap;
                if (rt != null)
                {
                    res = Mathf.Max(res, rt.width);
                    format = rt.format;
                }
            }

            if (res <= 0)
            {
                VistaDebugger.CaptureString("Missing output", "No biome produced a metallic map, leaving channel unset");
            }
            else
            {
                RenderTexture dest = GraphicsUtils.CreateBlankRT(res, format);
                int linearKernel = ctx.kernels[0];

                for (int i = 0; i < srcDatas.Count; ++i)
                {
                    BiomeData srcData = srcDatas[i];
                    RenderTexture srcMap = srcData.metallicMap;
                    Texture biomeMask = srcData.biomeMaskMap;
                    if (srcMap == null || biomeMask == null)
                    {
                        continue;
                    }

                    IBiome biome = srcBiomes[i];
                    if (biome.blendOptions.textureBlendMode == BiomeBlendOptions.TextureBlendMode.Replace)
                    {
                        DispatchBlend(ctx.shader, srcMap, dest, biomeMask, linearKernel);
                    }
                    else if (biome.blendOptions.textureBlendMode == BiomeBlendOptions.TextureBlendMode.HeightWin)
                    {
                        Texture heightWinMask = ctx.GetHeightWinMask(i);
                        if (heightWinMask == null)
                        {
                            Debug.Log(
                                $"[Biome Blend/Metallic] Biome '{biome.gameObject.name}' uses Height Win texture blending, but it has no height output. " +
                                $"Enable Height in the Data Mask and add a Height Output node.",
                                biome.gameObject);
                            continue;
                        }

                        RenderTexture filteredBiomeMask = GraphicsUtils.GetBlankTempRT(res, RenderTextureFormat.RFloat);
                        FilterBiomeMaskWithHeightWinMask(ctx, biomeMask, heightWinMask, filteredBiomeMask);
                        DispatchBlend(ctx.shader, srcMap, dest, filteredBiomeMask, linearKernel);
                        GraphicsUtils.ReleaseTempRT(filteredBiomeMask);
                    }
                    else
                    {
                        throw new System.NotImplementedException(
                            $"No metallic blend implementation for TextureBlendMode.{biome.blendOptions.textureBlendMode}. Add a case here.");
                    }
                }

                destData.metallicMap = dest;
                VistaDebugger.Capture("Final", dest);
            }
            VistaDebugger.CloseScope();
        }

        /// <summary>
        /// Blends generic textures from all source biomes into <paramref name="destData"/>.
        /// </summary>
        /// <param name="destData">Destination biome data receiving blended generic textures.</param>
        /// <param name="srcBiomes">Ordered biome list that provides texture blend options.</param>
        /// <param name="srcDatas">Biome output data parallel to <paramref name="srcBiomes"/>.</param>
        /// <param name="ctx">Texture blend context created by <see cref="Begin"/>.</param>
        /// <remarks>
        /// Each generic texture label represents an independent texture channel. Textures with the same label are
        /// blended together into one output texture. Biomes that do not carry a given label contribute black in their
        /// effective coverage region, allowing later biomes to clear lower values for that label.
        /// </remarks>
        /// <remarks>
        /// A missing biome mask is treated as full coverage. Height Win filters the biome mask with the height-win mask
        /// generated by <see cref="BlendHeightMap"/>. If a Height Win biome has no height-win mask, that biome contributes
        /// nothing for the label and a configuration message is logged.
        /// </remarks>
        internal static void BlendGenericTextures(
            BiomeData destData, List<IBiome> srcBiomes, List<BiomeData> srcDatas, BlendContext ctx)
        {
            VistaDebugger.OpenScope("Generic Textures", DebugScopeType.BlendPass);

            // Gather all unique labels across all biomes, preserving encounter order.
            List<string> uniqueLabels = new();
            for (int i = 0; i < srcDatas.Count; ++i)
            {
                foreach (string label in srcDatas[i].m_genericTextureLabels)
                {
                    if (!uniqueLabels.Contains(label))
                        uniqueLabels.Add(label);
                }
            }

            if (uniqueLabels.Count == 0)
            {
                VistaDebugger.CloseScope();
                return;
            }
            int linearKernel = ctx.kernels[0];

            for (int iLabel = 0; iLabel < uniqueLabels.Count; ++iLabel)
            {
                string label = uniqueLabels[iLabel];
                string labelDisplay = string.IsNullOrEmpty(label) ? "Generic texture" : $"{label} (Generic)";
                int res = 0;
                RenderTextureFormat format = RenderTextureFormat.RFloat;

                for (int i = 0; i < srcDatas.Count; ++i)
                {
                    int index = srcDatas[i].m_genericTextureLabels.IndexOf(label);
                    if (index >= 0)
                    {
                        RenderTexture rt = srcDatas[i].m_genericTextures.At(index);
                        if (rt != null)
                        {
                            res = Mathf.Max(res, rt.width);
                            format = rt.format;
                        }
                    }
                }

                if (res <= 0)
                {
                    VistaDebugger.CaptureString(labelDisplay, "No biome produced a generic texture for this channel, leaving channel unset");
                    continue;
                }

                RenderTexture dest = GraphicsUtils.CreateBlankRT(res, format);

                for (int i = 0; i < srcDatas.Count; ++i)
                {
                    IBiome biome = srcBiomes[i];
                    BiomeData srcData = srcDatas[i];
                    int index = srcData.m_genericTextureLabels.IndexOf(label);
                    // Biomes without this label contribute black, fading the texture to zero in their region.
                    Texture src = index >= 0 ? srcData.m_genericTextures.At(index) : Texture2D.blackTexture;
                    Texture biomeMask = srcData.biomeMaskMap != null ? srcData.biomeMaskMap : Texture2D.whiteTexture;

                    if (biome.blendOptions.textureBlendMode == BiomeBlendOptions.TextureBlendMode.Replace)
                    {
                        DispatchBlend(ctx.shader, src, dest, biomeMask, linearKernel);
                    }
                    else if (biome.blendOptions.textureBlendMode == BiomeBlendOptions.TextureBlendMode.HeightWin)
                    {
                        Texture heightWinMask = ctx.GetHeightWinMask(i);
                        if (heightWinMask == null)
                        {
                            Debug.Log(
                                $"[Biome Blend/Generic Texture] Biome '{biome.gameObject.name}' uses Height Win texture blending, but it has no height output. " +
                                $"Enable Height in the Data Mask and add a Height Output node.",
                                biome.gameObject);
                            continue;
                        }

                        RenderTexture filteredBiomeMask = GraphicsUtils.GetBlankTempRT(res, RenderTextureFormat.RFloat);
                        FilterBiomeMaskWithHeightWinMask(ctx, biomeMask, heightWinMask, filteredBiomeMask);
                        DispatchBlend(ctx.shader, src, dest, filteredBiomeMask, linearKernel);

                        GraphicsUtils.ReleaseTempRT(filteredBiomeMask);
                    }
                    else
                    {
                        throw new System.NotImplementedException(
                            $"No generic texture blend implementation for TextureBlendMode.{biome.blendOptions.textureBlendMode}. Add a case here.");
                    }
                }

                VistaDebugger.Capture(labelDisplay, dest);
                destData.AddGenericTexture(label, dest);
            }

            VistaDebugger.CloseScope();
        }

        /// <summary>
        /// Blends terrain texture layer weights from all source biomes into <paramref name="destData"/>.
        /// </summary>
        /// <param name="destData">Destination biome data receiving blended terrain layers and weight maps.</param>
        /// <param name="srcBiomes">Ordered biome list that provides texture blend options.</param>
        /// <param name="srcDatas">Biome output data parallel to <paramref name="srcBiomes"/>.</param>
        /// <param name="ctx">Texture blend context created by <see cref="Begin"/>.</param>
        /// <remarks>
        /// Texture layers are accumulated as non-distinct layer/weight pairs and then normalized by
        /// <see cref="WeightsBlend.Blend"/>. Biomes with no layers or no biome mask contribute black coverage.
        /// </remarks>
        /// <remarks>
        /// In Height Win mode, each source weight map is multiplied by the biome's height-win mask before the final weight
        /// blend. If a Height Win biome has no height output, its filtered weight is black and a configuration message is
        /// logged.
        /// </remarks>
        internal static void BlendTextureWeights(
            BiomeData destData, List<IBiome> srcBiomes, List<BiomeData> srcDatas, BlendContext ctx)
        {
            VistaDebugger.OpenScope("Texture Weights", DebugScopeType.BlendPass);

            List<Texture> srcBiomeMasks = new List<Texture>();
            List<TerrainLayer> srcLayers = new List<TerrainLayer>();
            List<RenderTexture> srcWeights = new List<RenderTexture>();
            List<int> srcLayerIndexToBiomeIndex = new List<int>();
            List<int> layerCountsByBiome = new List<int>();

            int resolution = 0;
            bool hasContribution = false;
            for (int iSrcData = 0; iSrcData < srcDatas.Count; ++iSrcData)
            {
                BiomeData srcData = srcDatas[iSrcData];
                int layerCount = srcData.GetLayerCount();
                srcData.GetLayerWeightsAppended(srcLayers, srcWeights);
                layerCountsByBiome.Add(layerCount);
                srcLayerIndexToBiomeIndex.AddRepeated(iSrcData, layerCount);

                if (layerCount > 0 && srcData.biomeMaskMap != null)
                {
                    hasContribution = true;
                    resolution = Mathf.Max(resolution, GetMaxLayerWeightResolution(srcData));
                    srcBiomeMasks.Add(srcData.biomeMaskMap);
                }
                else
                {
                    srcBiomeMasks.Add(Texture2D.blackTexture);
                }
            }

            if (hasContribution)
            {
                RenderTextureFormat format = RenderTextureFormat.RFloat;

                VistaDebugger.CaptureTexture("Source weights", srcWeights);
                List<RenderTexture> filteredWeights = new List<RenderTexture>();
                for (int iWeight = 0; iWeight < srcWeights.Count; ++iWeight)
                {
                    RenderTexture sourceWeight = srcWeights[iWeight];
                    int biomeIndex = srcLayerIndexToBiomeIndex[iWeight];
                    IBiome biome = srcBiomes[biomeIndex];

                    if (biome.blendOptions.textureBlendMode == BiomeBlendOptions.TextureBlendMode.HeightWin)
                    {
                        Texture heightWinMask = ctx.GetHeightWinMask(biomeIndex);
                        if (heightWinMask == null)
                        {
                            Debug.Log(
                                $"[Biome Blend/Texture Weights] Biome '{biome.gameObject.name}' uses Height Win texture blending, but it has no height output. " +
                                $"Enable Height in the Data Mask and add a Height Output node.",
                                biome.gameObject);
                            heightWinMask = Texture2D.blackTexture;
                        }
                        RenderTexture filteredWeight = GraphicsUtils.GetBlankTempRT(resolution, format);
                        DispatchFilterByHeight(ctx.shader, sourceWeight, filteredWeight, heightWinMask, ctx.filterByHeightKernel);
                        filteredWeights.Add(filteredWeight);
                    }
                    else
                    {
                        filteredWeights.Add(sourceWeight);
                    }
                }

                VistaDebugger.CaptureTexture("Height filtered weights", filteredWeights);

                List<Texture> biomeMasksPerLayer = new List<Texture>();
                for (int iBiome = 0; iBiome < srcBiomes.Count; ++iBiome)
                {
                    biomeMasksPerLayer.AddRepeated(srcBiomeMasks[iBiome], layerCountsByBiome[iBiome]);
                }

                List<RenderTexture> destWeights = GraphicsUtils.CreateBlankRTCollection(srcWeights.Count, resolution, format);
                WeightsBlend.Blend(destWeights.ToArray(), filteredWeights.ToArray(), biomeMasksPerLayer.ToArray());
                VistaDebugger.CaptureTexture("Dest weights", destWeights);

                for (int i = 0; i < filteredWeights.Count; ++i)
                {
                    if (filteredWeights[i] != null && filteredWeights[i] != srcWeights[i])
                    {
                        GraphicsUtils.ReleaseTempRT(filteredWeights[i]);
                    }
                }

                destData.AddTextureLayers(srcLayers, destWeights);
            }

            VistaDebugger.CloseScope();
        }

        /// <summary>
        /// Blends detail density maps from all source biomes into <paramref name="destData"/>.
        /// </summary>
        /// <param name="destData">Destination biome data receiving blended detail templates and density maps.</param>
        /// <param name="srcBiomes">Ordered biome list that provides population blend options.</param>
        /// <param name="srcDatas">Biome output data parallel to <paramref name="srcBiomes"/>.</param>
        /// <param name="ctx">Texture blend context created by <see cref="Begin"/>.</param>
        /// <remarks>
        /// Detail density maps are population outputs stored as textures. Replace mode lets each biome claim its mask
        /// region by fading lower density maps to black before adding its own maps. Coexist mode appends this biome's
        /// density maps without eroding lower maps. Height Win uses the biome mask filtered by the height-win mask before
        /// eroding lower maps and appending this biome's maps.
        /// </remarks>
        /// <remarks>
        /// Output entries are intentionally non-distinct. Duplicate detail templates are preserved so terrain backends can
        /// merge or interpret them according to their own detail density path. Biomes without biome masks are skipped.
        /// </remarks>
        internal static void BlendDensityMaps(
            BiomeData destData, List<IBiome> srcBiomes, List<BiomeData> srcDatas, BlendContext ctx)
        {
            VistaDebugger.OpenScope("Density Maps", DebugScopeType.BlendPass);

            // Find the working resolution for this channel across all source biomes.
            int maxResolution = 0;
            for (int iBiome = 0; iBiome < srcDatas.Count; ++iBiome)
            {
                maxResolution = Mathf.Max(maxResolution, srcDatas[iBiome].GetMaxDensityMapResolution());
            }
            if (maxResolution <= 0)
            {
                VistaDebugger.CaptureString("Missing output", "No biome produced any density map, leaving channel unset");
                VistaDebugger.CloseScope();
                return;
            }

            int linearKernel = ctx.kernels[0];
            // Accumulate non-distinct template/map pairs for the tile backend to merge later.
            List<DetailTemplate> destDetailTemplates = new List<DetailTemplate>();
            List<RenderTexture> destDensityMaps = new List<RenderTexture>();
            for (int iBiome = 0; iBiome < srcDatas.Count; ++iBiome)
            {
                IBiome biome = srcBiomes[iBiome];
                RenderTexture biomeMask = srcDatas[iBiome].biomeMaskMap;
                if (biomeMask == null)
                {
                    continue;
                }

                // Gather the current biome's source density outputs.
                List<DetailTemplate> srcDetailTemplates = new List<DetailTemplate>();
                List<RenderTexture> srcDensityMaps = new List<RenderTexture>();
                srcDatas[iBiome].GetDensityMaps(srcDetailTemplates, srcDensityMaps);

                VistaDebugger.CaptureString("Biome", $"{biome.gameObject.name} | {biome.blendOptions.populationBlendMode}");
                VistaDebugger.CaptureTexture("Biome Mask", biomeMask);
                VistaDebugger.CaptureTexture("Src Collection", srcDensityMaps);

                if (biome.blendOptions.populationBlendMode == BiomeBlendOptions.PopulationBlendMode.Replace)
                {
                    // Replace still claims the region even if this biome outputs no density maps.
                    // Fade everything underneath within the current biome region.
                    for (int iDensityMap = 0; iDensityMap < destDensityMaps.Count; ++iDensityMap)
                    {
                        DispatchBlend(ctx.shader, Texture2D.blackTexture, destDensityMaps[iDensityMap], biomeMask, linearKernel);
                    }

                    // Add this biome's own masked density maps on top.
                    AppendMaskedDensityMaps(
                        ctx, srcDetailTemplates, srcDensityMaps, biomeMask, destDetailTemplates, destDensityMaps);
                }
                else if (biome.blendOptions.populationBlendMode == BiomeBlendOptions.PopulationBlendMode.Coexist)
                {
                    // Coexist only adds this biome's masked density maps without cleaning underneath.
                    AppendMaskedDensityMaps(
                        ctx, srcDetailTemplates, srcDensityMaps, biomeMask, destDetailTemplates, destDensityMaps);
                }
                else if (biome.blendOptions.populationBlendMode == BiomeBlendOptions.PopulationBlendMode.HeightWin)
                {
                    RenderTexture heightWinMask = ctx.GetHeightWinMask(iBiome);
                    if (heightWinMask != null)
                    {
                        // HeightWin only adds this biome's density maps where it wins the height blend.
                        RenderTexture filteredBiomeMask = GraphicsUtils.GetBlankTempRT(biomeMask.width, biomeMask.format);
                        FilterBiomeMaskWithHeightWinMask(ctx, biomeMask, heightWinMask, filteredBiomeMask);

                        // Remove underlying density where this biome wins before adding its own maps.
                        for (int iDensityMap = 0; iDensityMap < destDensityMaps.Count; ++iDensityMap)
                        {
                            DispatchBlend(ctx.shader, Texture2D.blackTexture, destDensityMaps[iDensityMap], filteredBiomeMask, linearKernel);
                        }

                        AppendMaskedDensityMaps(
                            ctx, srcDetailTemplates, srcDensityMaps, filteredBiomeMask, destDetailTemplates, destDensityMaps);

                        GraphicsUtils.ReleaseTempRT(filteredBiomeMask);
                    }
                    else
                    {
                        Debug.Log(
                            $"[Biome Blend/Detail Density] Biome '{biome.gameObject.name}' uses Height Win population blending, but it has no height output. Enable Height in the Data Mask and add a Height Output node.");
                    }
                }
                else
                {
                    throw new System.NotImplementedException(
                        $"No density map gather implementation for PopulationBlendMode.{biome.blendOptions.populationBlendMode}. Add a case here.");
                }

                VistaDebugger.CaptureTexture("Dest Collection", destDensityMaps);
                VistaDebugger.CaptureSeparator();
            }

            // Return the non-distinct density pairs for later merge.
            destData.AddDetailDensities(destDetailTemplates, destDensityMaps);
            VistaDebugger.CaptureTexture("Final Dest Collection", destDensityMaps);
            VistaDebugger.CloseScope();
        }

        /// <summary>
        /// Creates masked copies of source density maps and appends them to destination density collections.
        /// </summary>
        /// <param name="ctx">Texture blend context used for shader dispatch.</param>
        /// <param name="srcDetailTemplates">Source detail templates paired with <paramref name="srcDensityMaps"/>.</param>
        /// <param name="srcDensityMaps">Source density maps to copy and mask.</param>
        /// <param name="biomeMask">Coverage mask applied while copying each source density map.</param>
        /// <param name="destDetailTemplates">Destination template collection receiving appended templates.</param>
        /// <param name="destDensityMaps">Destination density collection receiving newly allocated masked maps.</param>
        /// <remarks>
        /// Null source density maps are ignored. Newly created render textures are owned by the destination
        /// <see cref="BiomeData"/> after they are added through <see cref="BiomeData.AddDetailDensities"/>.
        /// </remarks>
        private static void AppendMaskedDensityMaps(
            BlendContext ctx,
            List<DetailTemplate> srcDetailTemplates,
            List<RenderTexture> srcDensityMaps,
            Texture biomeMask,
            List<DetailTemplate> destDetailTemplates,
            List<RenderTexture> destDensityMaps)
        {
            int linearKernel = ctx.kernels[0];
            for (int iDensityMap = 0; iDensityMap < srcDensityMaps.Count; ++iDensityMap)
            {
                DetailTemplate srcDetailTemplate = srcDetailTemplates[iDensityMap];
                RenderTexture srcDensityMap = srcDensityMaps[iDensityMap];
                if (srcDensityMap != null)
                {
                    RenderTexture destDensityMap =
                        GraphicsUtils.CreateBlankRT(srcDensityMap.width, srcDensityMap.format);
                    DispatchBlend(ctx.shader, srcDensityMap, destDensityMap, biomeMask, linearKernel);

                    destDetailTemplates.Add(srcDetailTemplate);
                    destDensityMaps.Add(destDensityMap);
                }
            }
        }

        /// <summary>
        /// Multiplies a biome mask by a height-win mask.
        /// </summary>
        /// <param name="ctx">Texture blend context used for shader dispatch.</param>
        /// <param name="sourceBiomeMask">Original biome coverage mask.</param>
        /// <param name="heightWinMask">Mask generated by the height blend pass.</param>
        /// <param name="filteredBiomeMask">Destination mask receiving the filtered result.</param>
        /// <remarks>
        /// The output mask represents the part of the biome that is both inside the authored biome mask and winning the
        /// height blend. Texture and buffer population channels use this mask to make Height Win affect only winning
        /// pixels.
        /// </remarks>
        internal static void FilterBiomeMaskWithHeightWinMask(
            BlendContext ctx,
            Texture sourceBiomeMask,
            Texture heightWinMask,
            RenderTexture filteredBiomeMask)
        {
            int linearKernel = ctx.kernels[0];
            DispatchBlend(ctx.shader, heightWinMask, filteredBiomeMask, sourceBiomeMask, linearKernel);
        }

        /// <summary>
        /// Fades an accumulated coverage mask to black inside another mask.
        /// </summary>
        /// <param name="ctx">Texture blend context used for shader dispatch.</param>
        /// <param name="destCoverageMask">Coverage mask to modify in-place.</param>
        /// <param name="erodeMask">Region where <paramref name="destCoverageMask"/> should be reduced.</param>
        /// <remarks>
        /// This helper is shared with buffer blending. It implements the "claim region" part of Replace and Height Win
        /// population blending by blending a black source into an existing coverage mask.
        /// </remarks>
        internal static void ErodeCoverageMask(BlendContext ctx, RenderTexture destCoverageMask, Texture erodeMask)
        {
            int linearKernel = ctx.kernels[0];
            DispatchBlend(ctx.shader, Texture2D.blackTexture, destCoverageMask, erodeMask, linearKernel);
        }

        /// <summary>
        /// Blends one simple texture channel with linear Replace semantics.
        /// </summary>
        /// <param name="srcDatas">Source biome data in blend order.</param>
        /// <param name="ctx">Texture blend context used for shader dispatch.</param>
        /// <param name="getMap">Callback that retrieves the source texture for the channel.</param>
        /// <param name="dest">Output render texture, or null when no source biome provides the channel.</param>
        /// <remarks>
        /// This helper is intended for channels with no per-biome mode override. It allocates the destination at the
        /// largest source resolution and skips biomes that do not provide a source texture. Missing biome masks are
        /// treated as full coverage.
        /// </remarks>
        private static void BlendLinearFixed(
            List<BiomeData> srcDatas, BlendContext ctx,
            System.Func<BiomeData, RenderTexture> getMap,
            out RenderTexture dest)
        {
            int res = 0;
            RenderTextureFormat format = RenderTextureFormat.ARGB32;
            for (int i = 0; i < srcDatas.Count; ++i)
            {
                RenderTexture rt = getMap(srcDatas[i]);
                if (rt != null)
                {
                    res = Mathf.Max(res, rt.width);
                    format = rt.format;
                }
            }

            if (res <= 0)
            {
                dest = null;
                return;
            }

            dest = GraphicsUtils.CreateBlankRT(res, format);
            int linearKernel = ctx.kernels[0];

            for (int i = 0; i < srcDatas.Count; ++i)
            {
                BiomeData srcData = srcDatas[i];
                RenderTexture srcMap = getMap(srcData);
                Texture mask = srcData.biomeMaskMap != null ? (Texture)srcData.biomeMaskMap : Texture2D.whiteTexture;
                DebuggerCapturePass(i, dest, srcMap, mask, "Linear");

                if (srcMap == null)
                    continue;

                DispatchBlend(ctx.shader, srcMap, dest, mask, linearKernel);
            }
        }

        /// <summary>
        /// Clamps a render texture to the [0, 1] range in-place.
        /// </summary>
        /// <param name="shader">Texture blend compute shader.</param>
        /// <param name="dest">Render texture to clamp.</param>
        /// <param name="kernel">Clamp kernel index.</param>
        private static void DispatchClamp(ComputeShader shader, RenderTexture dest, int kernel)
        {
            shader.SetTexture(kernel, DEST_TEXTURE, dest);
            shader.SetVector(DEST_RESOLUTION, new Vector2(dest.width, dest.height));
            shader.Dispatch(kernel, (dest.width + 7) / 8, (dest.height + 7) / 8, 1);
        }

        /// <summary>
        /// Dispatches one texture blend operation.
        /// </summary>
        /// <param name="shader">Texture blend compute shader.</param>
        /// <param name="src">Source texture. Null is treated as zero by the shader.</param>
        /// <param name="dest">Destination render texture modified in-place.</param>
        /// <param name="mask">Biome mask controlling where the source affects the destination.</param>
        /// <param name="kernel">Blend kernel index.</param>
        /// <param name="srcRemapOffset">Optional source value offset applied before blending.</param>
        /// <param name="srcRemapScale">Optional source value scale applied before blending.</param>
        /// <remarks>
        /// The remap parameters are used by height blending to apply biome transform Y position and scale in normalized
        /// terrain-height space. Other texture channels leave them at the identity values.
        /// </remarks>
        private static void DispatchBlend(ComputeShader shader, Texture src, RenderTexture dest, Texture mask, int kernel, float srcRemapOffset = 0f, float srcRemapScale = 1f)
        {
            if (src != null)
            {
                shader.SetTexture(kernel, SRC_TEXTURE, src);
                shader.DisableKeyword(KW_SRC_IS_NULL);
            }
            else
            {
                shader.EnableKeyword(KW_SRC_IS_NULL);
            }

            shader.SetTexture(kernel, DEST_TEXTURE, dest);
            shader.SetTexture(kernel, BIOME_MASK_TEXTURE, mask);
            shader.SetVector(DEST_RESOLUTION, new Vector2(dest.width, dest.height));
            shader.SetFloat(SRC_REMAP_OFFSET, srcRemapOffset);
            shader.SetFloat(SRC_REMAP_SCALE, srcRemapScale);
            shader.Dispatch(kernel, (dest.width + 7) / 8, (dest.height + 7) / 8, 1);
        }

        /// <summary>
        /// Generates a height-win mask for one biome before that biome is blended into the height destination.
        /// </summary>
        /// <param name="shader">Texture blend compute shader.</param>
        /// <param name="srcHeight">Biome source height map.</param>
        /// <param name="dest">Destination height-win mask to write.</param>
        /// <param name="currentDestHeight">Current accumulated height map before the biome is applied.</param>
        /// <param name="biomeMask">Biome coverage mask.</param>
        /// <param name="kernel">Win-mask kernel for Keep Higher or Keep Lower.</param>
        /// <param name="srcRemapOffset">Source height offset in normalized terrain-height space.</param>
        /// <param name="srcRemapScale">Source height scale in normalized terrain-height space.</param>
        /// <remarks>
        /// The generated mask is positive where the source biome wins the min/max height comparison. The caller smooths
        /// the mask before storing it for later texture and population filtering.
        /// </remarks>
        private static void DispatchHeightWinMask(
            ComputeShader shader,
            Texture srcHeight,
            RenderTexture dest,
            Texture currentDestHeight,
            Texture biomeMask,
            int kernel,
            float srcRemapOffset,
            float srcRemapScale)
        {
            shader.SetTexture(kernel, SRC_TEXTURE, srcHeight);
            shader.SetTexture(kernel, DEST_TEXTURE, dest);
            shader.SetTexture(kernel, WIN_TEXTURE, currentDestHeight);
            shader.SetTexture(kernel, BIOME_MASK_TEXTURE, biomeMask);
            shader.SetVector(DEST_RESOLUTION, new Vector2(dest.width, dest.height));
            shader.SetFloat(SRC_REMAP_OFFSET, srcRemapOffset);
            shader.SetFloat(SRC_REMAP_SCALE, srcRemapScale);
            shader.Dispatch(kernel, (dest.width + 7) / 8, (dest.height + 7) / 8, 1);
        }

        /// <summary>
        /// Multiplies a texture weight map by a height-win mask.
        /// </summary>
        /// <param name="shader">Texture blend compute shader.</param>
        /// <param name="srcWeight">Source weight map.</param>
        /// <param name="dest">Destination filtered weight map.</param>
        /// <param name="winMask">Height-win mask. Black means the biome contributes no weight.</param>
        /// <param name="kernel">Filter kernel index.</param>
        private static void DispatchFilterByHeight(
            ComputeShader shader,
            Texture srcWeight,
            RenderTexture dest,
            Texture winMask,
            int kernel)
        {
            shader.SetTexture(kernel, SRC_TEXTURE, srcWeight);
            shader.SetTexture(kernel, DEST_TEXTURE, dest);
            shader.SetTexture(kernel, WIN_TEXTURE, winMask);
            shader.SetVector(DEST_RESOLUTION, new Vector2(dest.width, dest.height));
            shader.Dispatch(kernel, (dest.width + 7) / 8, (dest.height + 7) / 8, 1);
        }

        /// <summary>
        /// Computes normalized height offset and scale from a biome transform.
        /// </summary>
        /// <param name="biome">Biome whose transform should be converted to normalized terrain-height space.</param>
        /// <param name="normalizedOffset">Output Y-position offset divided by terrain maximum height.</param>
        /// <param name="normalizedScale">Output Y scale from the biome transform.</param>
        /// <returns>True when a valid Vista Manager and terrain height were available; otherwise false.</returns>
        private static bool GetHeightRemap(IBiome biome, out float normalizedOffset, out float normalizedScale)
        {
            normalizedOffset = 0f;
            normalizedScale = 1f;

            VistaManager manager = biome.GetVistaManagerInstance();
            if (manager != null && manager.terrainMaxHeight > 0)
            {
                Transform biomeTransform = biome.gameObject.transform;
                normalizedOffset = biomeTransform.position.y / manager.terrainMaxHeight;
                normalizedScale = biomeTransform.lossyScale.y;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the highest layer weight resolution in one biome data object.
        /// </summary>
        /// <param name="data">Biome data containing terrain layer weight maps.</param>
        /// <returns>The highest render texture width, or <see cref="int.MinValue"/> when no layer weights exist.</returns>
        private static int GetMaxLayerWeightResolution(BiomeData data)
        {
            int res = int.MinValue;
            if (data.m_layerWeights == null)
                return res;
            foreach (RenderTexture rt in data.m_layerWeights)
            {
                res = Mathf.Max(res, rt.width);
            }
            return res;
        }

        /// <summary>
        /// Gets the compute kernel for a height blend mode.
        /// </summary>
        /// <param name="mode">Height blend mode to execute.</param>
        /// <param name="ctx">Texture blend context containing pre-resolved kernels.</param>
        /// <returns>Kernel index matching <paramref name="mode"/>.</returns>
        /// <exception cref="System.NotImplementedException">Thrown when the mode has no kernel mapping.</exception>
        private static int GetKernel(BiomeBlendOptions.HeightBlendMode mode, BlendContext ctx)
        {
            switch (mode)
            {
                case BiomeBlendOptions.HeightBlendMode.Replace: return ctx.kernels[0];
                case BiomeBlendOptions.HeightBlendMode.Raise: return ctx.kernels[1];
                case BiomeBlendOptions.HeightBlendMode.Lower: return ctx.kernels[2];
                case BiomeBlendOptions.HeightBlendMode.KeepHigher: return ctx.kernels[3];
                case BiomeBlendOptions.HeightBlendMode.KeepLower: return ctx.kernels[4];
                default: throw new System.NotImplementedException($"No kernel mapping for HeightBlendMode.{mode}. Add a case here.");
            }
        }

        /// <summary>
        /// Gets the compute kernel for a mesh density blend mode.
        /// </summary>
        /// <param name="mode">Mesh density blend mode to execute.</param>
        /// <param name="ctx">Texture blend context containing pre-resolved kernels.</param>
        /// <returns>Kernel index matching <paramref name="mode"/>.</returns>
        /// <exception cref="System.NotImplementedException">Thrown when the mode has no kernel mapping.</exception>
        private static int GetKernel(BiomeBlendOptions.MeshDensityBlendMode mode, BlendContext ctx)
        {
            switch (mode)
            {
                case BiomeBlendOptions.MeshDensityBlendMode.Replace: return ctx.kernels[0];
                case BiomeBlendOptions.MeshDensityBlendMode.Add: return ctx.kernels[1];
                case BiomeBlendOptions.MeshDensityBlendMode.Subtract: return ctx.kernels[2];
                case BiomeBlendOptions.MeshDensityBlendMode.Max: return ctx.kernels[3];
                case BiomeBlendOptions.MeshDensityBlendMode.Min: return ctx.kernels[4];
                default: throw new System.NotImplementedException($"No kernel mapping for MeshDensityBlendMode.{mode}. Add a case here.");
            }
        }

        /// <summary>
        /// Gets the compute kernel for a hole map blend mode.
        /// </summary>
        /// <param name="mode">Hole blend mode to execute.</param>
        /// <param name="ctx">Texture blend context containing pre-resolved kernels.</param>
        /// <returns>Kernel index matching <paramref name="mode"/>.</returns>
        /// <exception cref="System.NotImplementedException">Thrown when the mode has no kernel mapping.</exception>
        private static int GetKernel(BiomeBlendOptions.HoleBlendMode mode, BlendContext ctx)
        {
            switch (mode)
            {
                case BiomeBlendOptions.HoleBlendMode.Replace: return ctx.kernels[0];
                case BiomeBlendOptions.HoleBlendMode.Max: return ctx.kernels[3];
                default: throw new System.NotImplementedException($"No kernel mapping for HoleBlendMode.{mode}. Add a case here.");
            }
        }

        /// <summary>
        /// Gets the compute kernel for population density blend modes.
        /// </summary>
        /// <param name="mode">Population blend mode to execute for density textures.</param>
        /// <param name="ctx">Texture blend context containing pre-resolved kernels.</param>
        /// <returns>Kernel index matching <paramref name="mode"/>.</returns>
        /// <exception cref="System.NotImplementedException">Thrown when the mode has no kernel mapping.</exception>
        private static int GetKernel(BiomeBlendOptions.PopulationBlendMode mode, BlendContext ctx)
        {
            switch (mode)
            {
                case BiomeBlendOptions.PopulationBlendMode.Replace: return ctx.kernels[0];
                case BiomeBlendOptions.PopulationBlendMode.Coexist: return ctx.kernels[1];
                default: throw new System.NotImplementedException($"No kernel mapping for PopulationBlendMode.{mode}. Add a case here.");
            }
        }

    }
}
#endif
