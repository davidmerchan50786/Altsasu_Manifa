#if VISTA
using Pinwheel.Vista.Graph;
using UnityEditor;
using UnityEngine;

namespace Pinwheel.VistaEditor.Graph
{
    [NodeEditor(typeof(NoiseNode))]
    public class NoiseNodeEditor : ImageNodeEditorBase, INeedUpdateNodeVisual
    {
        private static readonly GUIContent PATTERN_HEADER = new GUIContent("Pattern");
        private static readonly GUIContent OFFSET = new GUIContent("Offset", "Offset the noise origin");
        private static readonly GUIContent SCALE = new GUIContent("Scale", "Scale of the noise");
        private static readonly GUIContent LACUNARITY = new GUIContent("Lacunarity", "The change in scale of the noise at each layer");
        private static readonly GUIContent PERSISTENCE = new GUIContent("Persistence", "The change in amplitude of the noise at each layer");
        private static readonly GUIContent LAYER_COUNT = new GUIContent("Layer Count", "Number of noise layer to generate and stack up");
        private static readonly GUIContent MODE = new GUIContent("Mode", "The noise pattern to generate");
        private static readonly GUIContent SEED = new GUIContent("Seed", "An integer to randomize the result");
        private static readonly GUIContent LAYER_DERIVATIVE = new GUIContent("Layer Derivative", "How the noise layer change corresponding to Lacunarity & Persistance");
        private static readonly GUIContent FLIP_SIGN = new GUIContent("Flip Sign", "Invert the noise value at each layer");

        private static readonly GUIContent WARP_HEADER = new GUIContent("Warp");
        private static readonly GUIContent WARP_MODE = new GUIContent("Mode", "Add warp effect to the noise pattern");
        private static readonly GUIContent WARP_ANGLE_MIN = new GUIContent("Min Angle", "Min rotation of the warp vector");
        private static readonly GUIContent WARP_ANGLE_MAX = new GUIContent("Max Angle", "Max rotation of the warp vector");
        private static readonly GUIContent WARP_INTENSITY = new GUIContent("Intensity", "Strength of the warp effect");

        private static readonly GUIContent REMAP_HEADER = new GUIContent("Remap");
        private static readonly GUIContent REMAP_CURVE = new GUIContent("Remap", "Remap the final noise value");
        private static readonly GUIContent APPLY_REMAP_PER_LAYER = new GUIContent("Apply Per Layer", "Remap the noise value at each layer");

        public override void OnGUI(INode node)
        {
            NoiseNode n = node as NoiseNode;
            Vector2 offset;
            float scale;
            float lacunarity;
            float persistence;
            int layerCount;
            NoiseMode mode;
            int seed;
            NoiseNode.LayerDerivativeMode derivative;
            bool flipSign;

            NoiseNode.WarpMode warpMode = n.warpMode;
            float warpAngleMin = n.warpAngleMin;
            float warpAngleMax = n.warpAngleMax;
            float warpIntensity = n.warpIntensity;

            AnimationCurve remapCurve = new AnimationCurve(n.remapCurve.keys);
            bool applyRemapPerLayer = n.applyRemapPerLayer;

            EditorGUI.BeginChangeCheck();
            EditorCommon.Header(PATTERN_HEADER);
            mode = (NoiseMode)EditorGUILayout.EnumPopup(MODE, n.mode);
            offset = EditorCommon.InlineVector2Field(OFFSET, n.offset);
            scale = EditorGUILayout.FloatField(SCALE, n.scale);
            lacunarity = EditorGUILayout.FloatField(LACUNARITY, n.lacunarity);
            persistence = EditorGUILayout.Slider(PERSISTENCE, n.persistence, 0f, 1f);
            layerCount = EditorGUILayout.IntSlider(LAYER_COUNT, n.layerCount, 1, 10);
            seed = EditorGUILayout.IntField(SEED, n.seed);
            derivative = (NoiseNode.LayerDerivativeMode)EditorGUILayout.EnumPopup(LAYER_DERIVATIVE, n.layerDerivativeMode);
            flipSign = EditorGUILayout.Toggle(FLIP_SIGN, n.flipSign);

            EditorCommon.Header(WARP_HEADER);
            warpMode = (NoiseNode.WarpMode)EditorGUILayout.EnumPopup(WARP_MODE, n.warpMode);
            if (warpMode == NoiseNode.WarpMode.Angular)
            {
                warpAngleMin = EditorGUILayout.Slider(WARP_ANGLE_MIN, n.warpAngleMin, -360, 360);
                warpAngleMax = EditorGUILayout.Slider(WARP_ANGLE_MAX, n.warpAngleMax, -360, 360);
            }
            if (warpMode == NoiseNode.WarpMode.Angular ||
                warpMode == NoiseNode.WarpMode.Directional)
            {
                warpIntensity = EditorGUILayout.FloatField(WARP_INTENSITY, n.warpIntensity);
            }

            EditorCommon.Header(REMAP_HEADER);
            remapCurve = EditorGUILayout.CurveField(REMAP_CURVE, n.remapCurve, Color.red, new Rect(0, 0, 1, 1));
            applyRemapPerLayer = EditorGUILayout.Toggle(APPLY_REMAP_PER_LAYER, n.applyRemapPerLayer);

            if (EditorGUI.EndChangeCheck())
            {
                m_graphEditor.RegisterUndo(n);
                n.offset = offset;
                n.scale = scale;
                n.lacunarity = lacunarity;
                n.persistence = persistence;
                n.layerCount = layerCount;
                n.mode = mode;
                n.seed = seed;
                n.layerDerivativeMode = derivative;
                n.flipSign = flipSign;

                n.warpMode = warpMode;
                n.warpAngleMin = warpAngleMin;
                n.warpAngleMax = warpAngleMax;
                n.warpIntensity = warpIntensity;

                n.remapCurve = remapCurve;
                n.applyRemapPerLayer = applyRemapPerLayer;
            }
        }

        public void UpdateVisual(INode node, NodeView nv)
        {
            NoiseNode n = node as NoiseNode;
            nv.title = $"Noise ({n.mode.ToString()})";
        }
    }
}
#endif
