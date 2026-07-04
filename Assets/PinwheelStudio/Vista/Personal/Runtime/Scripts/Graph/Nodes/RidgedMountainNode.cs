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
        title = "Ridged Mountain",
        path = "Generators/Ridged Mountain",
        description = "Generate base shape for mountainous scene with sharp ridges")]
    public class RidgedMountainNode : ImageNodeBase
    {
        public readonly MaskSlot outputSlot = new MaskSlot("Output", SlotDirection.Output, 100);

        [SerializeField]
        private float m_scale;
        public float scale
        {
            get
            {
                return m_scale;
            }
            set
            {
                m_scale = value;
            }
        }

        [SerializeField]
        private int m_seed;
        public int seed
        {
            get
            {
                return m_seed;
            }
            set
            {
                m_seed = value;
            }
        }

        [SerializeField]
        private float m_ridgeIntensity;
        public float ridgeIntensity
        {
            get
            {
                return m_ridgeIntensity;
            }
            set
            {
                m_ridgeIntensity = Mathf.Clamp(value, 0, 20);
            }
        }

        private static readonly string TEMP_RT0_NAME = "~RidgedMountain_TempRT0";
        private static readonly string TEMP_RT1_NAME = "~RidgedMountain_TempRT1";
        private static readonly string TEMP_RT2_NAME = "~RidgedMountain_TempRT2";
        private static readonly string TEMP_RT3_NAME = "~RidgedMountain_TempRT3";
        private static readonly string TEMP_RT4_NAME = "~RidgedMountain_TempRT4";
        public RidgedMountainNode() : base()
        {
            m_scale = 500;
            m_ridgeIntensity = 10;
            m_seed = 0;
        }
        public override void ExecuteImmediate(GraphContext context)
        {
            int baseResolution = context.GetArg(Args.RESOLUTION).intValue;
            int resolution = this.CalculateResolution(baseResolution, baseResolution);
            DataPool.RtDescriptor desc = DataPool.RtDescriptor.Create(resolution, resolution);

            int baseSeed = context.GetArg(Args.SEED).intValue;
            Vector4 worldBounds = context.GetArg(Args.WORLD_BOUNDS).vectorValue;

            NodeLibraryUtilities.NoiseNode.Params p = new NodeLibraryUtilities.NoiseNode.Params();
            p.offset = Vector2.zero;
            p.scale = m_scale;
            p.lacunarity = 2;
            p.persistence = 0.3f;
            p.layerCount = 4;
            p.mode = NoiseMode.Perlin01;
            p.warpMode = NodeLibraryUtilities.NoiseNode.WARP_DIRECTIONAL;
            p.warpAngleMin = 0;
            p.warpAngleMax = 0;
            p.warpIntensity = m_ridgeIntensity;
            p.remapCurve = AnimationCurve.Linear(0, 0, 1, 0.9f);
            p.seed = baseSeed ^ m_seed;
            p.worldBounds = worldBounds;

            RenderTexture tempRt0 = context.CreateTemporaryRT(desc, TEMP_RT0_NAME);
            NodeLibraryUtilities.NoiseNode.Execute(tempRt0, p);

            int resolutionQuarter = Utilities.MultipleOf8(resolution / 4);
            DataPool.RtDescriptor desc1 = DataPool.RtDescriptor.Create(resolutionQuarter, resolutionQuarter);
            RenderTexture tempRt1 = context.CreateTemporaryRT(desc1, TEMP_RT1_NAME);
            NodeLibraryUtilities.ClampNode.Execute(tempRt0, tempRt1, 0f, 1f);

            RenderTexture tempRt2 = context.CreateTemporaryRT(desc1, TEMP_RT2_NAME);
            NodeLibraryUtilities.ConvexNode.Execute(tempRt1, tempRt2, 0, 3);

            int dfIteration = Mathf.RoundToInt(desc1.width / 10f);
            NodeLibraryUtilities.DistanceFieldNode.Execute(tempRt2, tempRt1, dfIteration);

            RenderTexture tempRt3 = context.CreateTemporaryRT(desc, TEMP_RT3_NAME);
            int combineMode = NodeLibraryUtilities.CombineNode.MODE_MUL;
            NodeLibraryUtilities.CombineNode.Execute(tempRt0, tempRt1, Texture2D.whiteTexture, combineMode, tempRt3);

            RenderTexture tempRt4 = context.CreateTemporaryRT(desc, TEMP_RT4_NAME);
            combineMode = NodeLibraryUtilities.CombineNode.MODE_ADD;
            NodeLibraryUtilities.CombineNode.Execute(tempRt0, tempRt3, Texture2D.whiteTexture, combineMode, tempRt4, 1.0f, 0.1f, 1.0f);

            NodeLibraryUtilities.BlurNode.Execute(tempRt4, tempRt0, 5);

            SlotRef outputRef = new SlotRef(m_id, outputSlot.id);
            RenderTexture targetRt = context.CreateRenderTarget(desc, outputRef);
            UnityEngine.Graphics.CopyTexture(tempRt0, targetRt);

            context.ReleaseTemporary(TEMP_RT0_NAME);
            context.ReleaseTemporary(TEMP_RT1_NAME);
            context.ReleaseTemporary(TEMP_RT2_NAME);
            context.ReleaseTemporary(TEMP_RT3_NAME);
            context.ReleaseTemporary(TEMP_RT4_NAME);
        }
    }
}
#endif


