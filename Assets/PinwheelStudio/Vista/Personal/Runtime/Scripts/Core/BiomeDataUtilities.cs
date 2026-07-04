#if VISTA
using Pinwheel.Vista.Graphics;
using System.Collections.Generic;
using UnityEngine;
using Pinwheel.Vista.Diagnostics;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Utilities for remapping one <see cref="BiomeData"/> set into another bounds and resolution space.
    /// </summary>
    /// <remarks>
    /// This helper is primarily used when cached biome data generated in biome-local bounds must be copied into
    /// tile-specific request bounds. Texture outputs are remapped through a bounds-aware blit shader, while
    /// buffer outputs are remapped through <see cref="BiomeBufferCopy"/>.
    /// </remarks>
    public static class BiomeDataUtilities
    {
        private static readonly string BLIT_SHADER_NAME = "Hidden/Vista/BiomeDataBlit";
        private static readonly int MAIN_TEX = Shader.PropertyToID("_MainTex");
        private static readonly int RENDER_TARGET_SIZE = Shader.PropertyToID("_RenderTargetSize");
        private static readonly int SRC_BOUNDS = Shader.PropertyToID("_SrcBounds");
        private static readonly int DEST_BOUNDS = Shader.PropertyToID("_DestBounds");
        private static readonly int PASS = 0;

        /// <summary>
        /// Copies all supported biome outputs from one bounds space into another and writes them into a destination data object.
        /// </summary>
        /// <param name="srcData">Source biome data to read from.</param>
        /// <param name="srcBounds">World-space bounds represented by <paramref name="srcData"/>.</param>
        /// <param name="destData">Destination biome data object to overwrite.</param>
        /// <param name="destBounds">World-space bounds that the copied outputs should represent.</param>
        /// <param name="heightMapResolution">Target resolution for height-related outputs such as height, holes, and mesh density.</param>
        /// <param name="textureResolution">Target resolution for texture-like outputs such as albedo, metallic, layer weights, density maps, generic textures, and biome mask.</param>
        /// <remarks>
        /// This method starts by disposing any resources already owned by <paramref name="destData"/>.
        /// It then recreates destination outputs at the requested resolutions and remaps them from <paramref name="srcBounds"/> into <paramref name="destBounds"/>.
        /// Buffers are copied with bounds-aware sample remapping, not by raw byte cloning.
        /// </remarks>
        public static void Copy(BiomeData srcData, Bounds srcBounds, BiomeData destData, Bounds destBounds, int heightMapResolution, int textureResolution)
        {
            VistaDebugger.OpenScope($"Copy biome data slice", DebugScopeType.Custom);
            destData.Dispose();
            Material mat = new Material(ShaderUtilities.Find(BLIT_SHADER_NAME));
            mat.SetVector(SRC_BOUNDS, new Vector4(srcBounds.min.x, srcBounds.min.z, srcBounds.size.x, srcBounds.size.z));
            mat.SetVector(DEST_BOUNDS, new Vector4(destBounds.min.x, destBounds.min.z, destBounds.size.x, destBounds.size.z));

            RenderTexture srcHeightMap = srcData.heightMap;
            if (srcHeightMap != null)
            {
                RenderTextureDescriptor desc = srcHeightMap.descriptor;
                desc.width = heightMapResolution;
                desc.height = heightMapResolution;
                RenderTexture destHeightMap = CreateClearedRenderTexture(desc);
                CopyTexture(mat, srcHeightMap, destHeightMap);
                destData.heightMap = destHeightMap;
            }

            RenderTexture srcHoleMap = srcData.holeMap;
            if (srcHoleMap != null)
            {
                RenderTextureDescriptor desc = srcHoleMap.descriptor;
                desc.width = heightMapResolution;
                desc.height = heightMapResolution;
                RenderTexture destHoleMap = CreateClearedRenderTexture(desc);
                CopyTexture(mat, srcHoleMap, destHoleMap);
                destData.holeMap = destHoleMap;
            }

            RenderTexture srcMeshDensityMap = srcData.meshDensityMap;
            if (srcMeshDensityMap != null)
            {
                RenderTextureDescriptor desc = srcMeshDensityMap.descriptor;
                desc.width = heightMapResolution;
                desc.height = heightMapResolution;
                RenderTexture destMeshDensityMap = CreateClearedRenderTexture(desc);
                CopyTexture(mat, srcMeshDensityMap, destMeshDensityMap);
                destData.meshDensityMap = destMeshDensityMap;
            }

            RenderTexture srcAlbedoMap = srcData.albedoMap;
            if (srcAlbedoMap != null)
            {
                RenderTextureDescriptor desc = srcAlbedoMap.descriptor;
                desc.width = textureResolution;
                desc.height = textureResolution;
                RenderTexture destAlbedoMap = CreateClearedRenderTexture(desc);
                CopyTexture(mat, srcAlbedoMap, destAlbedoMap);
                destData.albedoMap = destAlbedoMap;
            }

            RenderTexture srcMetallicMap = srcData.metallicMap;
            if (srcMetallicMap != null)
            {
                RenderTextureDescriptor desc = srcMetallicMap.descriptor;
                desc.width = textureResolution;
                desc.height = textureResolution;
                RenderTexture destMetallicMap = CreateClearedRenderTexture(desc);
                CopyTexture(mat, srcMetallicMap, destMetallicMap);
                destData.metallicMap = destMetallicMap;
            }

            List<TerrainLayer> terrainLayers = new List<TerrainLayer>();
            List<RenderTexture> srcWeights = new List<RenderTexture>();
            srcData.GetLayerWeights(terrainLayers, srcWeights);
            for (int i = 0; i < terrainLayers.Count; ++i)
            {
                RenderTexture srcWeight = srcWeights[i];
                RenderTextureDescriptor desc = srcWeight.descriptor;
                desc.width = textureResolution;
                desc.height = textureResolution;
                RenderTexture destWeight = CreateClearedRenderTexture(desc);
                CopyTexture(mat, srcWeight, destWeight);
                destData.AddTextureLayer(terrainLayers[i], destWeight);
            }

            List<DetailTemplate> detailTemplates_Density = new List<DetailTemplate>();
            List<RenderTexture> srcDensityMaps = new List<RenderTexture>();
            srcData.GetDensityMaps(detailTemplates_Density, srcDensityMaps);
            for (int i = 0; i < detailTemplates_Density.Count; ++i)
            {
                RenderTexture srcDensity = srcDensityMaps[i];
                RenderTextureDescriptor desc = srcDensity.descriptor;
                desc.width = textureResolution;
                desc.height = textureResolution;
                RenderTexture destDensity = CreateClearedRenderTexture(desc);
                CopyTexture(mat, srcDensity, destDensity);
                destData.AddDetailDensity(detailTemplates_Density[i], destDensity);
            }

            List<string> genericTextureLabels = new List<string>();
            List<RenderTexture> genericTextures = new List<RenderTexture>();
            srcData.GetGenericTextures(genericTextureLabels, genericTextures);
            for (int i = 0; i < genericTextureLabels.Count; ++i)
            {
                RenderTexture srcTexture = genericTextures[i];
                RenderTextureDescriptor desc = srcTexture.descriptor;
                desc.width = textureResolution;
                desc.height = textureResolution;
                RenderTexture destTexture = CreateClearedRenderTexture(desc);
                CopyTexture(mat, srcTexture, destTexture);
                destData.AddGenericTexture(genericTextureLabels[i], destTexture);
            }

            RenderTexture srcBiomeMaskMap = srcData.biomeMaskMap;
            if (srcBiomeMaskMap != null)
            {
                RenderTextureDescriptor desc = srcBiomeMaskMap.descriptor;
                desc.width = textureResolution;
                desc.height = textureResolution;
                RenderTexture destBiomeMaskMap = CreateClearedRenderTexture(desc);
                CopyTexture(mat, srcBiomeMaskMap, destBiomeMaskMap);
                destData.biomeMaskMap = destBiomeMaskMap;
            }

            Rect inBounds = new Rect(srcBounds.min.x, srcBounds.min.z, srcBounds.size.x, srcBounds.size.z);
            Rect outBounds = new Rect(destBounds.min.x, destBounds.min.z, destBounds.size.x, destBounds.size.z);

            List<TreeTemplate> treeTemplates = new List<TreeTemplate>();
            List<ComputeBuffer> srcTreeBuffers = new List<ComputeBuffer>();
            srcData.GetTrees(treeTemplates, srcTreeBuffers);
            for (int i = 0; i < treeTemplates.Count; ++i)
            {
                ComputeBuffer srcBuffer = srcTreeBuffers[i];
                ComputeBuffer destBuffer = BiomeBufferCopy.CopyFrom<InstanceSample>(srcBuffer, inBounds, outBounds);
                if (destBuffer != null)
                {
                    destData.AddTree(treeTemplates[i], destBuffer);
                }
            }

            List<DetailTemplate> detailTemplates_Instance = new List<DetailTemplate>();
            List<ComputeBuffer> detailInstanceBuffers = new List<ComputeBuffer>();
            srcData.GetDetailInstances(detailTemplates_Instance, detailInstanceBuffers);
            for (int i = 0; i < detailTemplates_Instance.Count; ++i)
            {
                ComputeBuffer srcBuffer = detailInstanceBuffers[i];
                ComputeBuffer destBuffer = BiomeBufferCopy.CopyFrom<InstanceSample>(srcBuffer, inBounds, outBounds);
                if (destBuffer != null)
                {
                    destData.AddDetailInstance(detailTemplates_Instance[i], destBuffer);
                }
            }

            List<ObjectTemplate> objectTemplates = new List<ObjectTemplate>();
            List<ComputeBuffer> srcObjectBuffers = new List<ComputeBuffer>();
            srcData.GetObjects(objectTemplates, srcObjectBuffers);
            for (int i = 0; i < objectTemplates.Count; ++i)
            {
                ComputeBuffer srcBuffer = srcObjectBuffers[i];
                ComputeBuffer destBuffer = BiomeBufferCopy.CopyFrom<InstanceSample>(srcBuffer, inBounds, outBounds);
                if (destBuffer != null)
                {
                    destData.AddObject(objectTemplates[i], destBuffer);
                }
            }

            List<string> genericBufferLabels = new List<string>();
            List<ComputeBuffer> srcGenericBuffers = new List<ComputeBuffer>();
            srcData.GetGenericBuffers(genericBufferLabels, srcGenericBuffers);
            for (int i = 0; i < genericBufferLabels.Count; ++i)
            {
                ComputeBuffer srcBuffer = srcGenericBuffers[i];
                ComputeBuffer destBuffer = BiomeBufferCopy.CopyFrom<PositionSample>(srcBuffer, inBounds, outBounds);
                if (destBuffer != null)
                {
                    destData.AddGenericBuffer(genericBufferLabels[i], destBuffer);
                }
            }

            if (VistaDebugger.isRecording)
            {
                CaptureCopiedResults(destData);
            }

            Object.DestroyImmediate(mat);
            VistaDebugger.CloseScope();
        }

        private static void CaptureCopiedResults(BiomeData destData)
        {
            if (destData.heightMap != null)
            {
                VistaDebugger.Capture("Height Map", destData.heightMap);
            }

            if (destData.holeMap != null)
            {
                VistaDebugger.Capture("Hole Map", destData.holeMap);
            }

            if (destData.meshDensityMap != null)
            {
                VistaDebugger.Capture("Mesh Density Map", destData.meshDensityMap);
            }

            if (destData.albedoMap != null)
            {
                VistaDebugger.Capture("Albedo Map", destData.albedoMap);
            }

            if (destData.metallicMap != null)
            {
                VistaDebugger.Capture("Metallic Map", destData.metallicMap);
            }

            List<TerrainLayer> terrainLayers = new List<TerrainLayer>();
            List<RenderTexture> layerWeights = new List<RenderTexture>();
            destData.GetLayerWeights(terrainLayers, layerWeights);
            if (layerWeights.Count > 0)
            {
                VistaDebugger.CaptureString("Texture Weight Order\n", string.Join("\n", terrainLayers.ConvertAll(layer => layer != null ? layer.name : "Unnamed Layer")));
                VistaDebugger.Capture("Texture Weights", layerWeights);
            }

            List<DetailTemplate> detailTemplates_Density = new List<DetailTemplate>();
            List<RenderTexture> densityMaps = new List<RenderTexture>();
            destData.GetDensityMaps(detailTemplates_Density, densityMaps);
            for (int i = 0; i < densityMaps.Count; ++i)
            {
                string templateName = detailTemplates_Density[i] != null ? detailTemplates_Density[i].name : "Unnamed Template";
                VistaDebugger.Capture($"Density Map: {templateName}", densityMaps[i]);
            }

            List<string> genericTextureLabels = new List<string>();
            List<RenderTexture> genericTextures = new List<RenderTexture>();
            destData.GetGenericTextures(genericTextureLabels, genericTextures);
            for (int i = 0; i < genericTextures.Count; ++i)
            {
                string label = string.IsNullOrEmpty(genericTextureLabels[i]) ? "Generic Texture" : $"{genericTextureLabels[i]} (Generic)";
                VistaDebugger.Capture(label, genericTextures[i]);
            }

            if (destData.biomeMaskMap != null)
            {
                VistaDebugger.Capture("Biome Mask", destData.biomeMaskMap);
            }

            List<TreeTemplate> treeTemplates = new List<TreeTemplate>();
            List<ComputeBuffer> treeBuffers = new List<ComputeBuffer>();
            destData.GetTrees(treeTemplates, treeBuffers);
            for (int i = 0; i < treeBuffers.Count; ++i)
            {
                string templateName = treeTemplates[i] != null ? treeTemplates[i].name : "Unnamed Template";
                VistaDebugger.Capture($"Tree: {templateName}", treeBuffers[i], DebugBufferInterpretation.InstanceSample);
            }

            List<DetailTemplate> detailTemplates_Instance = new List<DetailTemplate>();
            List<ComputeBuffer> detailInstanceBuffers = new List<ComputeBuffer>();
            destData.GetDetailInstances(detailTemplates_Instance, detailInstanceBuffers);
            for (int i = 0; i < detailInstanceBuffers.Count; ++i)
            {
                string templateName = detailTemplates_Instance[i] != null ? detailTemplates_Instance[i].name : "Unnamed Template";
                VistaDebugger.Capture($"Detail Instance: {templateName}", detailInstanceBuffers[i], DebugBufferInterpretation.InstanceSample);
            }

            List<ObjectTemplate> objectTemplates = new List<ObjectTemplate>();
            List<ComputeBuffer> objectBuffers = new List<ComputeBuffer>();
            destData.GetObjects(objectTemplates, objectBuffers);
            for (int i = 0; i < objectBuffers.Count; ++i)
            {
                string templateName = objectTemplates[i] != null ? objectTemplates[i].name : "Unnamed Template";
                VistaDebugger.Capture($"Object: {templateName}", objectBuffers[i], DebugBufferInterpretation.InstanceSample);
            }

            List<string> genericBufferLabels = new List<string>();
            List<ComputeBuffer> genericBuffers = new List<ComputeBuffer>();
            destData.GetGenericBuffers(genericBufferLabels, genericBuffers);
            for (int i = 0; i < genericBuffers.Count; ++i)
            {
                string label = string.IsNullOrEmpty(genericBufferLabels[i]) ? "Generic Buffer" : $"{genericBufferLabels[i]} (Generic)";
                VistaDebugger.Capture(label, genericBuffers[i], DebugBufferInterpretation.PositionSample);
            }
        }

        /// <summary>
        /// Copies one texture output from source to destination using the currently configured bounds remap material.
        /// </summary>
        /// <param name="mat">Material configured with source and destination bounds.</param>
        /// <param name="src">Source texture to sample.</param>
        /// <param name="dest">Destination render texture to write into.</param>
        private static void CopyTexture(Material mat, RenderTexture src, RenderTexture dest)
        {
            mat.SetTexture(MAIN_TEX, src);
            mat.SetVector(RENDER_TARGET_SIZE, new Vector4(dest.width, dest.height, 0, 0));
            Drawing.DrawQuad(dest, mat, PASS);
        }

        private static RenderTexture CreateClearedRenderTexture(RenderTextureDescriptor desc)
        {
            RenderTexture renderTexture = new RenderTexture(desc);
            renderTexture.enableRandomWrite = desc.enableRandomWrite;
            renderTexture.Create();
            GraphicsUtils.ClearWithZeros(renderTexture);

            return renderTexture;
        }
    }
}
#endif


