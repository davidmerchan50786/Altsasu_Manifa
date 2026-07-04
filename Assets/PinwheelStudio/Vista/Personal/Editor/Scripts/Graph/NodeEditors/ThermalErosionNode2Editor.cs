#if VISTA
using Pinwheel.Vista.Graph;
using UnityEditor;
using UnityEngine;

namespace Pinwheel.VistaEditor.Graph
{
    [NodeEditor(typeof(ThermalErosionNode2))]
    public class ThermalErosionNode2Editor : ImageNodeEditorBase
    {
        private static readonly GUIContent GENERAL_HEADER = new GUIContent("General");
        private static readonly GUIContent ITERATION_COUNT = new GUIContent("Iteration", "The number of simulation steps to perform");
        private static readonly GUIContent ITERATION_PER_FRAME = new GUIContent("Iteration Per Frame", "The number of steps to perform in a single frame");
        private static readonly GUIContent USE_MULTI_RESOLUTION = new GUIContent("Use Multi Resolution", "Run the simulation at multiple resolution levels for better performance");

        private static readonly GUIContent SIMULATION_HEADER = new GUIContent("Simulation");
        private static readonly GUIContent EROSION_RATE = new GUIContent("Erosion Rate", "Strength of the erosion, higher value will pick up more soil");
        private static readonly GUIContent TALUS_ANGLE = new GUIContent("Talus Angle", "The angle threshold at which soil begins to erode");

        private static readonly GUIContent OUTPUT_ARTISTIC_CONTROL_HEADER = new GUIContent("Output Artistic Control");
        private static readonly GUIContent SEDIMENT_RANGE = new GUIContent("Sediment Range", "The output range for the remapped sediment map");
        private static readonly GUIContent SEDIMENT_MIN = new GUIContent("Min");
        private static readonly GUIContent SEDIMENT_MAX = new GUIContent("Max");

        public override void OnGUI(INode node)
        {
            ThermalErosionNode2 n = node as ThermalErosionNode2;
            EditorGUI.BeginChangeCheck();

            EditorCommon.Header(GENERAL_HEADER);
            int iterationCount = EditorGUILayout.DelayedIntField(ITERATION_COUNT, n.iterationCount);
            int iterationPerFrame = EditorGUILayout.DelayedIntField(ITERATION_PER_FRAME, n.iterationPerFrame);
            bool useMultiResolution = EditorGUILayout.Toggle(USE_MULTI_RESOLUTION, n.useMultiResolution);

            EditorCommon.Header(SIMULATION_HEADER);
            float erosionRate = EditorGUILayout.DelayedFloatField(EROSION_RATE, n.erosionRate);
            float talusAngle = EditorGUILayout.DelayedFloatField(TALUS_ANGLE, n.talusAngle);

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

                n.erosionRate = erosionRate;
                n.talusAngle = talusAngle;

                n.sedimentOutputMin = sedimentOutputMin;
                n.sedimentOutputMax = sedimentOutputMax;
            }
        }
    }
}
#endif
