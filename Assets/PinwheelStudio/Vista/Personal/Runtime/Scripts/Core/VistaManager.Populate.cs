#if VISTA
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista.Diagnostics;
using UnityEngine;

namespace Pinwheel.Vista
{
    public partial class VistaManager
    {
        private void HandlePopulateGeometry(ITile tile, BiomeData data)
        {
            if (!(tile is IGeometryPopulator gp))
            {
                return;
            }

            bool geometryChanged = false;
            if (data.heightMap != null)
            {
                gp.PopulateHeightMap(data.heightMap);
                heightMapPopulated?.Invoke(this, tile, data.heightMap);
                VistaDebugger.Capture("Height Map", data.heightMap);
                geometryChanged = true;
            }
            else if (missingGeometryAction == MissingOutputAction.Clear)
            {
                gp.ClearHeightMap();
                geometryChanged = true;
            }

            if (data.holeMap != null)
            {
                gp.PopulateHoleMap(data.holeMap);
                holeMapPopulated?.Invoke(this, tile, data.holeMap);
                VistaDebugger.Capture("Hole Map", data.holeMap);
                geometryChanged = true;
            }
            else if (missingGeometryAction == MissingOutputAction.Clear)
            {
                gp.ClearHoleMap();
                geometryChanged = true;
            }

            if (data.meshDensityMap != null)
            {
                gp.PopulateMeshDensityMap(data.meshDensityMap);
                meshDensityMapPopulated?.Invoke(this, tile, data.meshDensityMap);
                VistaDebugger.Capture("Mesh Density Map", data.meshDensityMap);
                geometryChanged = true;
            }
            else if (missingGeometryAction == MissingOutputAction.Clear)
            {
                gp.ClearMeshDensityMap();
                geometryChanged = true;
            }

            if (geometryChanged)
            {
                gp.UpdateGeometry();
            }
        }

        private void HandlePopulateTextures(ITile tile, BiomeData data)
        {
            if (tile is IAlbedoMapPopulator amp)
            {
                if (data.albedoMap != null)
                {
                    amp.PopulateAlbedoMap(data.albedoMap);
                    albedoMapPopulated?.Invoke(this, tile, data.albedoMap);
                    VistaDebugger.Capture("Albedo Map", data.albedoMap);
                }
                else if (missingTextureAction == MissingOutputAction.Clear)
                {
                    amp.ClearAlbedoMap();
                }
            }

            if (tile is IMetallicMapPopulator mmp)
            {
                if (data.metallicMap != null)
                {
                    mmp.PopulateMetallicMap(data.metallicMap);
                    metallicMapPopulated?.Invoke(this, tile, data.metallicMap);
                    VistaDebugger.Capture("Metallic Map", data.metallicMap);
                }
                else if (missingTextureAction == MissingOutputAction.Clear)
                {
                    mmp.ClearMetallicMap();
                }
            }

            if (tile is ILayerWeightsPopulator lwp)
            {
                List<TerrainLayer> layers = new List<TerrainLayer>();
                List<RenderTexture> layerWeights = new List<RenderTexture>();
                data.GetLayerWeights(layers, layerWeights);
                if (layers.Count > 0)
                {
                    lwp.PopulateLayerWeights(layers, layerWeights);
                    layerWeightPopulated?.Invoke(this, tile, layers, layerWeights);
                    for (int i = 0; i < layerWeights.Count; ++i)
                    {
                        string layerName = layers[i] != null ? layers[i].name : "Unnamed Layer";
                        VistaDebugger.Capture($"Layer Weight: {layerName}", layerWeights[i]);
                    }
                }
                else if (missingTextureAction == MissingOutputAction.Clear)
                {
                    lwp.ClearLayerWeights();
                }
            }
        }

        private void HandlePopulateTrees(ITile tile, BiomeData data)
        {
            if (!(tile is ITreePopulator tp))
            {
                return;
            }

            List<TreeTemplate> treeTemplates = new List<TreeTemplate>();
            List<ComputeBuffer> treeBuffers = new List<ComputeBuffer>();
            data.GetTrees(treeTemplates, treeBuffers);
            if (treeTemplates.Count > 0)
            {
                tp.PopulateTrees(treeTemplates, treeBuffers);
                treePopulated?.Invoke(this, tile, treeTemplates, treeBuffers);
                for (int i = 0; i < treeBuffers.Count; ++i)
                {
                    string templateName = treeTemplates[i] != null ? treeTemplates[i].name : "Unnamed Template";
                    VistaDebugger.Capture($"Tree: {templateName}", treeBuffers[i], DebugBufferInterpretation.InstanceSample);
                }
            }
            else if (missingPopulationAction == MissingOutputAction.Clear)
            {
                tp.ClearTrees();
            }
        }

        private IEnumerator HandlePopulateDetailDensity(ITile tile, BiomeData data)
        {
            if (!(tile is IDetailDensityPopulator ddp))
            {
                yield break;
            }

            List<DetailTemplate> detailTemplates = new List<DetailTemplate>();
            List<RenderTexture> densityMaps = new List<RenderTexture>();
            data.GetDensityMaps(detailTemplates, densityMaps);
            if (detailTemplates.Count > 0)
            {
                yield return ddp.PopulateDetailDensity(detailTemplates, densityMaps);
                detailDensityPopulated?.Invoke(this, tile, detailTemplates, densityMaps);
                for (int i = 0; i < densityMaps.Count; ++i)
                {
                    string templateName = detailTemplates[i] != null ? detailTemplates[i].name : "Unnamed Template";
                    VistaDebugger.Capture($"Detail Density: {templateName}", densityMaps[i]);
                }
            }
            else if (missingPopulationAction == MissingOutputAction.Clear)
            {
                yield return ddp.ClearDetailDensity();
            }
        }

        private void HandlePopulateDetailInstances(ITile tile, BiomeData data)
        {
            if (!(tile is IDetailInstancePopulator dip))
            {
                return;
            }

            List<DetailTemplate> detailTemplates = new List<DetailTemplate>();
            List<ComputeBuffer> detailBuffers = new List<ComputeBuffer>();
            data.GetDetailInstances(detailTemplates, detailBuffers);
            if (detailTemplates.Count > 0)
            {
                dip.PopulateDetailInstance(detailTemplates, detailBuffers);
                detailInstancePopulated?.Invoke(this, tile, detailTemplates, detailBuffers);
                for (int i = 0; i < detailBuffers.Count; ++i)
                {
                    string templateName = detailTemplates[i] != null ? detailTemplates[i].name : "Unnamed Template";
                    VistaDebugger.Capture($"Detail Instance: {templateName}", detailBuffers[i], DebugBufferInterpretation.InstanceSample);
                }
            }
            else if (missingPopulationAction == MissingOutputAction.Clear)
            {
                dip.ClearDetailInstance();
            }
        }

        private IEnumerator HandlePopulateObjects(ITile tile, BiomeData data, ObjectPopulateArgs objectPopulateArgs)
        {
            if (!(tile is IObjectPopulator op))
            {
                yield break;
            }

            List<ObjectTemplate> objectTemplates = new List<ObjectTemplate>();
            List<ComputeBuffer> sampleBuffers = new List<ComputeBuffer>();
            data.GetObjects(objectTemplates, sampleBuffers);
            if (objectTemplates.Count > 0)
            {
                yield return op.PopulateObject(objectTemplates, sampleBuffers, objectPopulateArgs);
                objectPopulated?.Invoke(this, tile, objectTemplates, sampleBuffers);
                for (int i = 0; i < sampleBuffers.Count; ++i)
                {
                    string templateName = objectTemplates[i] != null ? objectTemplates[i].name : "Unnamed Template";
                    VistaDebugger.Capture($"Object: {templateName}", sampleBuffers[i], DebugBufferInterpretation.InstanceSample);
                }
            }
            else if (missingPopulationAction == MissingOutputAction.Clear)
            {
                yield return op.ClearObject();
            }
        }

        private void HandlePopulateGenericTextures(ITile tile, BiomeData data)
        {
            if (!(tile is IGenericTexturePopulator gtp))
            {
                return;
            }

            List<string> genericTextureLabels = new List<string>();
            List<RenderTexture> genericTextures = new List<RenderTexture>();
            data.GetGenericTextures(genericTextureLabels, genericTextures);
            gtp.PopulateGenericTextures(genericTextureLabels, genericTextures);
            genericTexturesPopulated?.Invoke(this, tile, genericTextureLabels, genericTextures);
            for (int i = 0; i < genericTextures.Count; ++i)
            {
                string label = string.IsNullOrEmpty(genericTextureLabels[i]) ? "Generic Texture" : genericTextureLabels[i];
                VistaDebugger.Capture(label, genericTextures[i]);
            }
        }

        private void HandlePopulateGenericBuffers(ITile tile, BiomeData data)
        {
            if (!(tile is IGenericBufferPopulator gbp))
            {
                return;
            }

            List<string> genericBufferLabels = new List<string>();
            List<ComputeBuffer> genericBuffers = new List<ComputeBuffer>();
            data.GetGenericBuffers(genericBufferLabels, genericBuffers);
            gbp.PopulateGenericBuffers(genericBufferLabels, genericBuffers);
            genericBuffersPopulated?.Invoke(this, tile, genericBufferLabels, genericBuffers);
            for (int i = 0; i < genericBuffers.Count; ++i)
            {
                string label = string.IsNullOrEmpty(genericBufferLabels[i]) ? "Generic Buffer" : $"{genericBufferLabels[i]} (Generic)";
                VistaDebugger.Capture(label, genericBuffers[i], DebugBufferInterpretation.PositionSample);
            }
        }
    }
}
#endif
