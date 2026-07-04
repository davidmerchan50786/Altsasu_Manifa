#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;
using Pinwheel.Vista.Graph;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "Thermal Erosion 2 (exp)",
        path = "Nature/Thermal Erosion 2 (exp)",
        description = "Simulate the effect of soil breaking out from the surface due to high temperature, slide and rest in lower places",
        keywords = "talus, heat, temperature")]
    public class ThermalErosionNode2 : ImageNodeBase
    {
        public readonly MaskSlot inputHeightSlot = new MaskSlot("Height", SlotDirection.Input, 0);
        public readonly MaskSlot hardnessSlot = new MaskSlot("Hardness", SlotDirection.Input, 1);

        public readonly MaskSlot outputHeightSlot = new MaskSlot("Height", SlotDirection.Output, 100);
        public readonly MaskSlot outputSedimentSlot = new MaskSlot("Sediment", SlotDirection.Output, 101);

        public override bool shouldSplitExecution
        {
            get => true;
            set => base.shouldSplitExecution = value;
        }

        [SerializeField]
        private int m_iterationCount;
        public int iterationCount
        {
            get
            {
                return m_iterationCount;
            }
            set
            {
                m_iterationCount = Mathf.Max(1, value);
            }
        }

        [SerializeField]
        private int m_iterationPerFrame;
        public int iterationPerFrame
        {
            get
            {
                return Mathf.Max(1, m_iterationPerFrame);
            }
            set
            {
                m_iterationPerFrame = Mathf.Max(1, value);
            }
        }
        

        [SerializeField]
        private float m_erosionRate;
        public float erosionRate
        {
            get
            {
                return m_erosionRate;
            }
            set
            {
                m_erosionRate = Mathf.Max(0, value);
            }
        }        

        [SerializeField]
        private float m_talusAngle;
        public float talusAngle
        {
            get
            {
                return m_talusAngle;
            }
            set
            {
                m_talusAngle = Mathf.Clamp(value, 0, 89);
            }
        }

        [SerializeField]
        private bool m_useMultiResolution;
        public bool useMultiResolution
        {
            get
            {
                return m_useMultiResolution;
            }
            set
            {
                m_useMultiResolution = value;
            }
        }

        [SerializeField]
        private float m_sedimentOutputMin;
        public float sedimentOutputMin
        {
            get
            {
                return m_sedimentOutputMin;
            }
            set
            {
                m_sedimentOutputMin = Mathf.Min(value, m_sedimentOutputMax);
            }
        }

        [SerializeField]
        private float m_sedimentOutputMax;
        public float sedimentOutputMax
        {
            get
            {
                return m_sedimentOutputMax;
            }
            set
            {
                m_sedimentOutputMax = Mathf.Max(value, m_sedimentOutputMin);
            }
        }

        private static ComputeShader s_sourceShader;
        private static ComputeShader sourceShader
        {
            get
            {
                if (s_sourceShader == null)
                {
                    s_sourceShader = Resources.Load<ComputeShader>(COMPUTE_SHADER_NAME);
                }
                return s_sourceShader;
            }
        }

        private static readonly string COMPUTE_SHADER_NAME = "Vista/Shaders/Graph/ThermalErosion2";
        private static readonly string TEMP_WORLD_DATA = "~Erosion_WorldData";
        private static readonly string TEMP_SIM_DATA_0 = "~Erosion_SimData0";
        private static readonly string TEMP_SIM_DATA_1 = "~Erosion_SimData1";
        private static readonly string TEMP_REMAP_TEXTURE = "~Erosion_TempRemap";

        private static readonly int INPUT_HEIGHT_01 = Shader.PropertyToID("_InputHeight01");
        private static readonly int INPUT_HARDNESS_01 = Shader.PropertyToID("_InputHardness01");
        private static readonly int WORLD_DATA_CM = Shader.PropertyToID("_WorldDataCM");
        private static readonly int SIM_DATA_0 = Shader.PropertyToID("_SimData0");
        private static readonly int SIM_DATA_1 = Shader.PropertyToID("_SimData1");
        private static readonly int OUTPUT_RT_01 = Shader.PropertyToID("_OutputRT01");

        private static readonly int WORLD_DATA_RESOLUTION = Shader.PropertyToID("_WorldDataResolution");
        private static readonly int UPSAMPLE_RESOLUTION = Shader.PropertyToID("_UpsampleResolution");
        private static readonly int WORLD_SIZE_CM = Shader.PropertyToID("_WorldSizeCM");
        private static readonly int SEDIMENT_TRANSPORT_CONSTANT = Shader.PropertyToID("_SedimentTransportConstant");
        private static readonly int THERMAL_EROSION_DELTA_HEIGHT_THRESHOLD = Shader.PropertyToID("_ThermalErosionDeltaHeightThreshold");

        private static readonly int EROSION_RATE_CM = Shader.PropertyToID("_ErosionRate");
        private static readonly int SINE_TALUS = Shader.PropertyToID("_SineTalus");

        private static readonly int KERNEL_INIT_WORLD_DATA = 0;
        private static readonly int KERNEL_OUTPUT_HEIGHT = 1;
        private static readonly int KERNEL_THERMAL_EROSION_PHASE_0 = 2;
        private static readonly int KERNEL_THERMAL_EROSION_PHASE_1 = 3;
        private static readonly int KERNEL_THERMAL_EROSION_PHASE_2 = 4;
        private static readonly int KERNEL_UPSAMPLING = 5;
        private static readonly int KERNEL_OUTPUT_DEPOSIT = 6;

        private static readonly int SUBSTEP_COUNT = 6;

        public ThermalErosionNode2() : base()
        {
            m_iterationCount = 500;
            m_iterationPerFrame = 5;
            
            m_erosionRate = 20f;
            m_talusAngle = 15f;

            m_useMultiResolution = true;

            m_sedimentOutputMin = 0f;
            m_sedimentOutputMax = 1f;
        }

        public override void ExecuteImmediate(GraphContext context)
        {
            Utilities.DrainCoroutine(Execute(context));
        }

        class SimTextures
        {
            public RenderTexture worldDataTexture;
            public RenderTexture simDataTexture0;
            public RenderTexture simDataTexture1;
#if VISTA_DEBUG
            public RenderTexture tempTexture;
#endif
        }

        private struct RequiredOutputs
        {
            public bool height;
            public bool sediment;
        }

        [System.Serializable]
        private struct SettingsSnapshot
        {
            public int iterationCount;
            public float erosionRate;
            public float talusAngle;
            public bool useMultiResolution;
        }

        [System.Serializable]
        private struct ArgsSnapshot
        {
            public int graphResolution;
            public int inputResolution;
            public int outputResolution;
            public float boundsX;
            public float boundsY;
            public float boundsZ;
            public float boundsW;
            public float terrainHeight;
            public int seed;
        }

        public override IEnumerator Execute(GraphContext context)
        {
            int graphResolution = context.GetArg(Args.RESOLUTION).intValue;
            SlotRef inputHeightRefLink = context.GetInputLink(m_id, inputHeightSlot.id);
            Texture inputHeightTexture = context.GetTexture(inputHeightRefLink);
            int inputResolution;
            if (inputHeightTexture != null)
            {
                inputResolution = inputHeightTexture.width;
            }
            else
            {
                inputHeightTexture = Texture2D.blackTexture;
                inputResolution = graphResolution;
            }

            SlotRef hardnessRefLink = context.GetInputLink(m_id, hardnessSlot.id);
            Texture hardnessTexture = context.GetTexture(hardnessRefLink);
            if (hardnessTexture == null)
            {
                hardnessTexture = Texture2D.blackTexture;
            }

            int outputResolution = this.CalculateResolution(graphResolution, inputResolution);
            Vector4 bounds = context.GetArg(Args.WORLD_BOUNDS).vectorValue;
            float maxHeight = context.GetArg(Args.TERRAIN_HEIGHT).floatValue;
            SlotRef outputHeightRef = new SlotRef(m_id, outputHeightSlot.id);
            SlotRef outputSedimentRef = new SlotRef(m_id, outputSedimentSlot.id);
            RequiredOutputs requiredOutputs = GetRequiredOutputs(context, outputHeightRef, outputSedimentRef);
            string settingsJson = null;
            string argsJson = null;

            if (inputHeightTexture == Texture2D.blackTexture)
            {
                if (requiredOutputs.height)
                {
                    DataPool.RtDescriptor outputHeightDesc = DataPool.RtDescriptor.Create(outputResolution, outputResolution, RenderTextureFormat.RFloat);
                    RenderTexture outputHeightTexture = context.CreateRenderTarget(outputHeightDesc, outputHeightRef);
                    GraphicsUtils.ClearWithZeros(outputHeightTexture);
                }
                if (requiredOutputs.sediment)
                {
                    DataPool.RtDescriptor outputSedimentDesc = DataPool.RtDescriptor.Create(outputResolution, outputResolution, RenderTextureFormat.RFloat);
                    RenderTexture outputSedimentTexture = context.CreateRenderTarget(outputSedimentDesc, outputSedimentRef);
                    GraphicsUtils.ClearWithZeros(outputSedimentTexture);
                }
                context.ReleaseReference(inputHeightRefLink);
                context.ReleaseReference(hardnessRefLink);
                yield break;
            }

            if (context.hasCache)
            {
                settingsJson = CreateSettingsJson();
                argsJson = CreateArgsJson(context, graphResolution, inputResolution, outputResolution, bounds, maxHeight);
                if (TryLoadFromCache(context, settingsJson, argsJson, inputHeightTexture, hardnessTexture, requiredOutputs, outputResolution, outputHeightRef, outputSedimentRef))
                {
                    context.SetCurrentProgress(1f);
                    context.ReleaseReference(inputHeightRefLink);
                    context.ReleaseReference(hardnessRefLink);
                    yield break;
                }
            }

            DataPool.RtDescriptor worldDataDesc = DataPool.RtDescriptor.Create(outputResolution + 8, outputResolution + 8, RenderTextureFormat.ARGBFloat);
            RenderTexture worldDataTexture = context.CreateTemporaryRT(worldDataDesc, TEMP_WORLD_DATA);
            GraphicsUtils.ClearWithZeros(worldDataTexture);

            Vector3 worldSizeCM = new Vector3(bounds.z, maxHeight, bounds.w) * 100f;

#if VISTA_DEBUG
            ErosionVisualizer.worldSizeCM = worldSizeCM;
            RenderTexture tempHeightTexture = RenderTexture.GetTemporary(outputResolution, outputResolution, 0, RenderTextureFormat.RFloat);
            tempHeightTexture.enableRandomWrite = true;
            GraphicsUtils.ClearWithZeros(tempHeightTexture);
#endif

            DataPool.RtDescriptor simDataDesc = DataPool.RtDescriptor.Create(worldDataTexture.width, worldDataTexture.height, RenderTextureFormat.ARGBFloat);
            RenderTexture simDataTexture0 = context.CreateTemporaryRT(simDataDesc, TEMP_SIM_DATA_0); //contains outflowVH
            RenderTexture simDataTexture1 = context.CreateTemporaryRT(simDataDesc, TEMP_SIM_DATA_1); //contains outflowDiag

            GraphicsUtils.ClearWithZeros(simDataTexture0);
            GraphicsUtils.ClearWithZeros(simDataTexture1);

            ComputeShader shader = Object.Instantiate(sourceShader);
            shader.SetFloat(EROSION_RATE_CM, m_erosionRate);
            shader.SetFloat(SINE_TALUS, Mathf.Sin(m_talusAngle));

            shader.SetTexture(KERNEL_INIT_WORLD_DATA, INPUT_HEIGHT_01, inputHeightTexture);
            shader.SetTexture(KERNEL_INIT_WORLD_DATA, WORLD_DATA_CM, worldDataTexture);
            shader.SetTexture(KERNEL_INIT_WORLD_DATA, SIM_DATA_0, simDataTexture0); //upsampled data from last canvas
            
            shader.SetTexture(KERNEL_THERMAL_EROSION_PHASE_0, WORLD_DATA_CM, worldDataTexture);
            shader.SetTexture(KERNEL_THERMAL_EROSION_PHASE_0, INPUT_HARDNESS_01, hardnessTexture);

            shader.SetTexture(KERNEL_THERMAL_EROSION_PHASE_1, WORLD_DATA_CM, worldDataTexture);
            shader.SetTexture(KERNEL_THERMAL_EROSION_PHASE_1, INPUT_HARDNESS_01, hardnessTexture);
            shader.SetTexture(KERNEL_THERMAL_EROSION_PHASE_1, SIM_DATA_0, simDataTexture0);
            shader.SetTexture(KERNEL_THERMAL_EROSION_PHASE_1, SIM_DATA_1, simDataTexture1);

            shader.SetTexture(KERNEL_THERMAL_EROSION_PHASE_2, WORLD_DATA_CM, worldDataTexture);
            shader.SetTexture(KERNEL_THERMAL_EROSION_PHASE_2, SIM_DATA_0, simDataTexture0);
            shader.SetTexture(KERNEL_THERMAL_EROSION_PHASE_2, SIM_DATA_1, simDataTexture1);

            shader.SetFloat(SEDIMENT_TRANSPORT_CONSTANT, 1.0f / SUBSTEP_COUNT);

            shader.SetTexture(KERNEL_UPSAMPLING, WORLD_DATA_CM, worldDataTexture);
            shader.SetTexture(KERNEL_UPSAMPLING, INPUT_HEIGHT_01, inputHeightTexture);
            shader.SetTexture(KERNEL_UPSAMPLING, SIM_DATA_0, simDataTexture0); //storing interpolated data

            SimTextures simTextures = new SimTextures();
            simTextures.worldDataTexture = worldDataTexture;
            simTextures.simDataTexture0 = simDataTexture0;
            simTextures.simDataTexture1 = simDataTexture1;
#if VISTA_DEBUG
            simTextures.tempTexture = tempHeightTexture;
#endif

            if (m_useMultiResolution)
            {
                List<int> simRes = new List<int>();
                List<int> numIteration = new List<int>();
                int res = 256;
                while (res < outputResolution)
                {
                    simRes.Add(res);
                    res *= 2;
                }
                simRes.Add(outputResolution);

                int remainingIteration = m_iterationCount;
                int n = 0;
                for (int i = 0; i < simRes.Count - 1; ++i)
                {
                    n = Mathf.FloorToInt(m_iterationCount * 0.5f * (simRes[i] * 1.0f / outputResolution));
                    numIteration.Add(n);
                    remainingIteration -= n;
                }
                numIteration.Add(remainingIteration);

                for (int i = 0; i < simRes.Count - 1; ++i)
                {
                    yield return Simulate(context, shader, numIteration[i], simRes[i], simRes[i], worldSizeCM, simTextures);
                    Upsample(shader, simRes[i + 1], simRes[i + 1]);
                }

                yield return Simulate(context, shader, numIteration[numIteration.Count - 1], simRes[simRes.Count - 1], simRes[simRes.Count - 1], worldSizeCM, simTextures);
            }
            else
            {
                yield return Simulate(context, shader, m_iterationCount, outputResolution, outputResolution, worldSizeCM, simTextures);
            }

            if (requiredOutputs.height)
            {
                DataPool.RtDescriptor outputHeightDesc = DataPool.RtDescriptor.Create(outputResolution, outputResolution, RenderTextureFormat.RFloat);
                RenderTexture outputHeightTexture = context.CreateRenderTarget(outputHeightDesc, outputHeightRef);

                shader.SetTexture(KERNEL_OUTPUT_HEIGHT, WORLD_DATA_CM, worldDataTexture);
                shader.SetTexture(KERNEL_OUTPUT_HEIGHT, OUTPUT_RT_01, outputHeightTexture);
                shader.Dispatch(KERNEL_OUTPUT_HEIGHT, (outputResolution + 7) / 8, 1, (outputResolution + 7) / 8);
            }

            if (requiredOutputs.sediment)
            {
                DataPool.RtDescriptor outputSedimentDesc = DataPool.RtDescriptor.Create(outputResolution, outputResolution, RenderTextureFormat.RFloat);
                RenderTexture outputSedimentTexture = context.CreateRenderTarget(outputSedimentDesc, outputSedimentRef);
                RenderTexture tempRemapTexture = context.CreateTemporaryRT(outputSedimentDesc, TEMP_REMAP_TEXTURE);

                shader.SetTexture(KERNEL_OUTPUT_DEPOSIT, WORLD_DATA_CM, worldDataTexture);
                shader.SetTexture(KERNEL_OUTPUT_DEPOSIT, INPUT_HEIGHT_01, inputHeightTexture);
                shader.SetTexture(KERNEL_OUTPUT_DEPOSIT, OUTPUT_RT_01, tempRemapTexture);
                shader.Dispatch(KERNEL_OUTPUT_DEPOSIT, (outputResolution + 7) / 8, 1, (outputResolution + 7) / 8);

                VistaLib.Remap(outputSedimentTexture, tempRemapTexture, m_sedimentOutputMin, m_sedimentOutputMax);
                context.ReleaseTemporary(TEMP_REMAP_TEXTURE);
            }

            if (context.hasCache)
            {
                StoreToCache(context, settingsJson, argsJson, inputHeightTexture, hardnessTexture, outputHeightRef, outputSedimentRef);
            }

            Object.DestroyImmediate(shader);
            context.ReleaseReference(inputHeightRefLink);
            context.ReleaseReference(hardnessRefLink);
            context.ReleaseTemporary(TEMP_WORLD_DATA);
            context.ReleaseTemporary(TEMP_SIM_DATA_0);
            context.ReleaseTemporary(TEMP_SIM_DATA_1);
#if VISTA_DEBUG
            RenderTexture.ReleaseTemporary(tempHeightTexture);
#endif
            yield return null;
        }

        private RequiredOutputs GetRequiredOutputs(GraphContext context, SlotRef outputHeightRef, SlotRef outputSedimentRef)
        {
            RequiredOutputs outputs = new RequiredOutputs();
            outputs.height = context.GetReferenceCount(outputHeightRef) > 0 || context.IsTargetNode(m_id);
            outputs.sediment = context.GetReferenceCount(outputSedimentRef) > 0;
            return outputs;
        }

        private string CreateSettingsJson()
        {
            SettingsSnapshot snapshot = new SettingsSnapshot();
            snapshot.iterationCount = m_iterationCount;
            snapshot.erosionRate = m_erosionRate;
            snapshot.talusAngle = m_talusAngle;
            snapshot.useMultiResolution = m_useMultiResolution;
            return JsonUtility.ToJson(snapshot);
        }

        private string CreateArgsJson(GraphContext context, int graphResolution, int inputResolution, int outputResolution, Vector4 bounds, float terrainHeight)
        {
            ArgsSnapshot snapshot = new ArgsSnapshot();
            snapshot.graphResolution = graphResolution;
            snapshot.inputResolution = inputResolution;
            snapshot.outputResolution = outputResolution;
            snapshot.boundsX = bounds.x;
            snapshot.boundsY = bounds.y;
            snapshot.boundsZ = bounds.z;
            snapshot.boundsW = bounds.w;
            snapshot.terrainHeight = terrainHeight;
            snapshot.seed = context.GetArg(Args.SEED).intValue;
            return JsonUtility.ToJson(snapshot);
        }

        private bool TryLoadFromCache(
            GraphContext context,
            string settingsJson,
            string argsJson,
            Texture inputHeightTexture,
            Texture hardnessTexture,
            RequiredOutputs requiredOutputs,
            int outputResolution,
            SlotRef outputHeightRef,
            SlotRef outputSedimentRef)
        {
            GraphExecutionCache.Entry entry;
            if (!context.TryGetCacheEntry(m_id, out entry) || entry == null)
            {
                return false;
            }

            if (!string.Equals(entry.settingsJson, settingsJson, System.StringComparison.Ordinal) ||
                !string.Equals(entry.argsJson, argsJson, System.StringComparison.Ordinal))
            {
                return false;
            }

            if (!HasRequiredCachedOutputs(entry, requiredOutputs, outputResolution))
            {
                return false;
            }

            if (!InputMatches(entry, inputHeightSlot.id, inputHeightTexture))
            {
                return false;
            }

            if (!InputMatches(entry, hardnessSlot.id, hardnessTexture))
            {
                return false;
            }

            CopyRequiredOutputsFromCache(context, entry, requiredOutputs, outputResolution, outputHeightRef, outputSedimentRef);
            return true;
        }

        private bool InputMatches(GraphExecutionCache.Entry entry, int slotId, Texture currentTexture)
        {
            RenderTexture cachedTexture;
            if (entry.inputTextures == null || !entry.inputTextures.TryGetValue(slotId, out cachedTexture))
            {
                return false;
            }
            return TextureComparator.AreEqual(currentTexture, cachedTexture);
        }

        private bool HasRequiredCachedOutputs(GraphExecutionCache.Entry entry, RequiredOutputs requiredOutputs, int outputResolution)
        {
            if (requiredOutputs.height && !HasCachedOutput(entry, outputHeightSlot.id, outputResolution))
            {
                return false;
            }
            if (requiredOutputs.sediment && !HasCachedOutput(entry, outputSedimentSlot.id, outputResolution))
            {
                return false;
            }
            return true;
        }

        private static bool HasCachedOutput(GraphExecutionCache.Entry entry, int slotId, int outputResolution)
        {
            RenderTexture cachedTexture;
            if (entry.outputTextures == null || !entry.outputTextures.TryGetValue(slotId, out cachedTexture))
            {
                return false;
            }
            return cachedTexture != null &&
                cachedTexture.width == outputResolution &&
                cachedTexture.height == outputResolution &&
                cachedTexture.format == RenderTextureFormat.RFloat;
        }

        private void CopyRequiredOutputsFromCache(
            GraphContext context,
            GraphExecutionCache.Entry entry,
            RequiredOutputs requiredOutputs,
            int outputResolution,
            SlotRef outputHeightRef,
            SlotRef outputSedimentRef)
        {
            if (requiredOutputs.height)
            {
                CopyOutputFromCache(context, entry.outputTextures[outputHeightSlot.id], outputResolution, outputHeightRef);
            }
            if (requiredOutputs.sediment)
            {
                CopyOutputFromCache(context, entry.outputTextures[outputSedimentSlot.id], outputResolution, outputSedimentRef);
            }
        }

        private static void CopyOutputFromCache(GraphContext context, RenderTexture cachedTexture, int outputResolution, SlotRef outputRef)
        {
            DataPool.RtDescriptor outputDesc = DataPool.RtDescriptor.Create(outputResolution, outputResolution, RenderTextureFormat.RFloat);
            RenderTexture outputTexture = context.CreateRenderTarget(outputDesc, outputRef);
            UnityEngine.Graphics.Blit(cachedTexture, outputTexture);
        }

        private void StoreToCache(
            GraphContext context,
            string settingsJson,
            string argsJson,
            Texture inputHeightTexture,
            Texture inputHardnessTexture,
            SlotRef outputHeightRef,
            SlotRef outputSedimentRef)
        {
            GraphExecutionCache.Entry entry = new GraphExecutionCache.Entry();
            entry.settingsJson = settingsJson;
            entry.argsJson = argsJson;

            entry.inputTextures[inputHeightSlot.id] = GraphicsUtils.CloneToRenderTexture(inputHeightTexture);
            entry.inputTextures[hardnessSlot.id] = GraphicsUtils.CloneToRenderTexture(inputHardnessTexture);

            RenderTexture outputHeightTexture = context.GetTexture(outputHeightRef);
            if (outputHeightTexture != null)
            {
                entry.outputTextures[outputHeightSlot.id] = GraphicsUtils.CloneToRenderTexture(outputHeightTexture);
            }

            RenderTexture outputSedimentTexture = context.GetTexture(outputSedimentRef);
            if (outputSedimentTexture != null)
            {
                entry.outputTextures[outputSedimentSlot.id] = GraphicsUtils.CloneToRenderTexture(outputSedimentTexture);
            }

            if (!context.SetCacheEntry(m_id, entry))
            {
                entry.Dispose();
            }
        }

        private IEnumerator Simulate(GraphContext context, ComputeShader shader, int numIteration, int outputWidth, int outputHeight, Vector3 worldSizeCM, SimTextures textures)
        {
            int threadGroupX = (outputWidth + 7) / 8;
            int threadGroupY = 1;
            int threadGroupZ = (outputHeight + 7) / 8;

            shader.SetVector(WORLD_SIZE_CM, worldSizeCM);
            shader.SetVector(WORLD_DATA_RESOLUTION, new Vector4(outputWidth, outputHeight));
            shader.Dispatch(KERNEL_INIT_WORLD_DATA, threadGroupX, threadGroupY, threadGroupZ);

#if VISTA_DEBUG
            OutputWorldDataToTemp(textures.worldDataTexture, textures.tempTexture, shader, KERNEL_OUTPUT_HEIGHT);
            ErosionVisualizer.CopyHeightFrom(textures.tempTexture);
#endif

            float cellDistance = worldSizeCM.x / outputWidth;
            float thermalErosionDeltaHeightThreshold = Mathf.Tan(m_talusAngle * Mathf.Deg2Rad) * cellDistance;
            shader.SetFloat(THERMAL_EROSION_DELTA_HEIGHT_THRESHOLD, thermalErosionDeltaHeightThreshold);

            shader.Dispatch(KERNEL_THERMAL_EROSION_PHASE_0, threadGroupX, threadGroupY, threadGroupZ);
            int currentIteration = 0;
            for (int i = 0; i < numIteration; ++i)
            {
#if VISTA_DEBUG
                OutputWorldDataToTemp(textures.worldDataTexture, textures.tempTexture, shader, KERNEL_OUTPUT_HEIGHT);
                ErosionVisualizer.CopyHeightFrom(textures.tempTexture);

                OutputWorldDataToTemp(textures.worldDataTexture, textures.tempTexture, shader, KERNEL_OUTPUT_DEPOSIT);
                ErosionVisualizer.CopySedimentFrom(textures.tempTexture);
#endif

                for (int iThermalErosion = 0; iThermalErosion < SUBSTEP_COUNT; ++iThermalErosion)
                {
                    shader.Dispatch(KERNEL_THERMAL_EROSION_PHASE_1, threadGroupX, threadGroupY, threadGroupZ);
                    shader.Dispatch(KERNEL_THERMAL_EROSION_PHASE_2, threadGroupX, threadGroupY, threadGroupZ);
                }

                if (i % iterationPerFrame == 0 && shouldSplitExecution)
                {
                    context.SetCurrentProgress(currentIteration * 1.0f / numIteration);
                    yield return null;
                }
                currentIteration += 1;
            }

#if VISTA_DEBUG
            OutputWorldDataToTemp(textures.worldDataTexture, textures.tempTexture, shader, KERNEL_OUTPUT_DEPOSIT);
            ErosionVisualizer.CopySedimentFrom(textures.tempTexture);

            OutputWorldDataToTemp(textures.worldDataTexture, textures.tempTexture, shader, KERNEL_OUTPUT_HEIGHT);
            ErosionVisualizer.CopyHeightFrom(textures.tempTexture);
#endif
        }

        private void Upsample(ComputeShader shader, int newWidth, int newHeight)
        {
            shader.SetVector(UPSAMPLE_RESOLUTION, new Vector4(newWidth, newHeight, 0, 0));
            int threadGroupX = (newWidth + 7) / 8;
            int threadGroupY = 1;
            int threadGroupZ = (newHeight + 7) / 8;
            shader.Dispatch(KERNEL_UPSAMPLING, threadGroupX, threadGroupY, threadGroupZ);
        }

#if VISTA_DEBUG
        private void OutputWorldDataToTemp(RenderTexture worldDataTexture, RenderTexture tempTexture, ComputeShader shader, int kernel)
        {
            shader.SetTexture(kernel, WORLD_DATA_CM, worldDataTexture);
            shader.SetTexture(kernel, OUTPUT_RT_01, tempTexture);
            shader.Dispatch(kernel, (tempTexture.width + 7) / 8, 1, (tempTexture.height + 7) / 8);
        }
#endif
    }
}
#endif
