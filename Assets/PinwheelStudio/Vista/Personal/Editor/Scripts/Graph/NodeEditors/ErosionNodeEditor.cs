#if VISTA
using Pinwheel.Vista;
using Pinwheel.Vista.Graph;
using Pinwheel.Vista.Graphics;
using UnityEditor;
using UnityEngine;

namespace Pinwheel.VistaEditor.Graph
{
    [NodeEditor(typeof(ErosionNode))]
    public class ErosionNodeEditor : ImageNodeEditorBase
    {
        private static readonly GUIContent GENERAL_HEADER = new GUIContent("General");
        private static readonly GUIContent ITERATION_COUNT = new GUIContent("Iteration", "The number of simulation steps to perform");
        private static readonly GUIContent ITERATION_PER_FRAME = new GUIContent("Iteration Per Frame", "The number of steps to perform in a single frame");
        private static readonly GUIContent USE_MULTI_RESOLUTION = new GUIContent("Multi Resolution", "Run the simulation at progressively higher resolutions for better performance");

        private static readonly GUIContent HYDRAULIC_HEADER = new GUIContent("Hydraulic Erosion");
        private static readonly GUIContent RAIN_RATE = new GUIContent("Rain Rate", "The amount of water pour into the system in each iteration");
        private static readonly GUIContent RAIN_OVER_TIME = new GUIContent("Rain Over Time", "A curve that controls the rain intensity over the simulation lifetime");
        private static readonly GUIContent SEDIMENT_CAPACITY = new GUIContent("Sediment Capacity", "The amount of sediment that water can carry");
        private static readonly GUIContent EROSION_RATE = new GUIContent("Erosion Rate", "Strength of the erosion, higher value will pick up more soil");
        private static readonly GUIContent DEPOSITION_RATE = new GUIContent("Deposition Rate", "Strength of the deposition, higher value will add more soil back to the terrain");
        private static readonly GUIContent EVAPORATION_RATE = new GUIContent("Evaporation Rate", "Strength of the evaporation that removes water from the system");

        private static readonly GUIContent THERMAL_HEADER = new GUIContent("Thermal Erosion");
        private static readonly GUIContent TALUS_ANGLE = new GUIContent("Talus Angle", "The angle of repose, material above this angle will slide down");
        private static readonly GUIContent THERMAL_EROSION_PROPORTION = new GUIContent("Proportion", "Apply thermal erosion every N hydraulic iterations");

        private static readonly GUIContent OUTPUT_ARTISTIC_CONTROL_HEADER = new GUIContent("Output Artistic Control");
        private static readonly GUIContent SEDIMENT_RANGE = new GUIContent("Sediment Range", "The output range for the remapped sediment map");
        private static readonly GUIContent SEDIMENT_MIN = new GUIContent("Min");
        private static readonly GUIContent SEDIMENT_MAX = new GUIContent("Max");

        public override void OnGUI(INode node)
        {
            ErosionNode n = node as ErosionNode;
            EditorGUI.BeginChangeCheck();

            EditorCommon.Header(GENERAL_HEADER);
            int iterationCount = EditorGUILayout.DelayedIntField(ITERATION_COUNT, n.iterationCount);
            int iterationPerFrame = EditorGUILayout.DelayedIntField(ITERATION_PER_FRAME, n.iterationPerFrame);
            bool useMultiResolution = EditorGUILayout.Toggle(USE_MULTI_RESOLUTION, n.useMultiResolution);

            EditorCommon.Header(HYDRAULIC_HEADER);
            float rainRate = EditorGUILayout.DelayedFloatField(RAIN_RATE, n.rainRate);
            AnimationCurve rainOverTime = EditorGUILayout.CurveField(RAIN_OVER_TIME, n.rainOverTime, Color.cyan, new Rect(0, 0, 1, 1));
            float sedimentCapacity = EditorGUILayout.DelayedFloatField(SEDIMENT_CAPACITY, n.sedimentCapacity);
            float erosionRate = EditorGUILayout.DelayedFloatField(EROSION_RATE, n.erosionRate);
            float depositionRate = EditorGUILayout.DelayedFloatField(DEPOSITION_RATE, n.depositionRate);
            float evaporationRate = EditorGUILayout.DelayedFloatField(EVAPORATION_RATE, n.evaporationRate);

            EditorCommon.Header(THERMAL_HEADER);
            float talusAngle = EditorGUILayout.DelayedFloatField(TALUS_ANGLE, n.talusAngle);
            int thermalErosionProportion = EditorGUILayout.DelayedIntField(THERMAL_EROSION_PROPORTION, n.thermalErosionProportion);

            EditorCommon.Header(OUTPUT_ARTISTIC_CONTROL_HEADER);
            float sedimentOutputMin = n.sedimentOutputMin;
            float sedimentOutputMax = n.sedimentOutputMax;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(SEDIMENT_RANGE);
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 30;
            sedimentOutputMin = EditorGUILayout.DelayedFloatField(SEDIMENT_MIN, sedimentOutputMin);
            sedimentOutputMax = EditorGUILayout.DelayedFloatField(SEDIMENT_MAX, sedimentOutputMax);
            EditorGUIUtility.labelWidth = oldLabelWidth;
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                m_graphEditor.RegisterUndo(n);
                n.iterationCount = iterationCount;
                n.iterationPerFrame = iterationPerFrame;
                n.useMultiResolution = useMultiResolution;

                n.rainRate = rainRate;
                n.rainOverTime = rainOverTime;
                n.sedimentCapacity = sedimentCapacity;
                n.erosionRate = erosionRate;
                n.depositionRate = depositionRate;
                n.evaporationRate = evaporationRate;

                n.talusAngle = talusAngle;
                n.thermalErosionProportion = thermalErosionProportion;

                n.sedimentOutputMin = sedimentOutputMin;
                n.sedimentOutputMax = sedimentOutputMax;
            }
        }
    }
}
#endif
