#if VISTA
using Pinwheel.Vista.Graph;
using UnityEditor;
using UnityEngine;

namespace Pinwheel.VistaEditor.Graph
{
    [NodeEditor(typeof(ThermalErosionNode))]
    public class ThermalErosionNodeEditor : ImageNodeEditorBase
    {
        private static readonly GUIContent GENERAL_HEADER = new GUIContent("General");
        private static readonly GUIContent HIGH_QUALITY = new GUIContent("High Quality", "If true, it will simulate in 8 directions, otherwise 4");
        private static readonly GUIContent DETAIL_LEVEL = new GUIContent("Detail Level", "Smaller value runs faster and produces larger features, while larger value is more expensive but produces more micro details");
        private static readonly GUIContent ITERATION_COUNT = new GUIContent("Iteration", "The number of simulation steps to perform");
        private static readonly GUIContent ITERATION_PER_FRAME = new GUIContent("Iteration Per Frame", "The number of steps to perform in a single frame");

        private static readonly GUIContent SIMULATION_HEADER = new GUIContent("Simulation");
        private static readonly GUIContent EROSION_RATE = new GUIContent("Erosion Rate", "Strength of the erosion, higher value will pick up more soil");
        private static readonly GUIContent EROSION_MULTIPLIER = new GUIContent(" ", "Overall multiplier. Change the erosion strength without modifying its base value");
        private static readonly GUIContent RESTING_ANGLE = new GUIContent("Resting Angle", "The angle at which soil comes to rest");
        private static readonly GUIContent RESTING_ANGLE_MULTIPLIER = new GUIContent(" ", "Overall multiplier. Change the resting angle without modifying its base value");

        private static readonly GUIContent ARTISTIC_HEADER = new GUIContent("Artistic Controls");
        private static readonly GUIContent HEIGHT_SCALE = new GUIContent("Height Scale", "A multiplier to terrain height to further enhance the erosion effect");
        private static readonly GUIContent EROSION_BOOST = new GUIContent("Erosion Boost", "A multiplier to enhance the erosion effect");
        private static readonly GUIContent DEPOSITION_BOOST = new GUIContent("Deposition Boost", "A multiplier to enhance the deposition effect");

        public override void OnGUI(INode node)
        {
            ThermalErosionNode n = node as ThermalErosionNode;
            EditorGUI.BeginChangeCheck();

            EditorCommon.Header(GENERAL_HEADER);
            bool highQualityMode = EditorGUILayout.Toggle(HIGH_QUALITY, n.highQualityMode);
            float detailLevel = EditorGUILayout.Slider(DETAIL_LEVEL, n.detailLevel, 0.1f, 1f);
            int iterationCount = EditorGUILayout.DelayedIntField(ITERATION_COUNT, n.iterationCount);
            int iterationPerFrame = EditorGUILayout.DelayedIntField(ITERATION_PER_FRAME, n.iterationPerFrame);

            EditorCommon.Header(SIMULATION_HEADER);
            float erosionRate = EditorGUILayout.DelayedFloatField(EROSION_RATE, n.erosionRate);
            float erosionMultiplier = EditorGUILayout.Slider(EROSION_MULTIPLIER, n.erosionMultiplier, 0f, 2f);
            float restingAngle = EditorGUILayout.Slider(RESTING_ANGLE, n.restingAngle, 0f, 90f);
            float restingAngleMultiplier = EditorGUILayout.Slider(RESTING_ANGLE_MULTIPLIER, n.restingAngleMultiplier, 0f, 2f);

            EditorCommon.Header(ARTISTIC_HEADER);
            float heightScale = EditorGUILayout.DelayedFloatField(HEIGHT_SCALE, n.heightScale);
            float erosionBoost = EditorGUILayout.DelayedFloatField(EROSION_BOOST, n.erosionBoost);
            float depositionBoost = EditorGUILayout.DelayedFloatField(DEPOSITION_BOOST, n.depositionBoost);

            if (EditorGUI.EndChangeCheck())
            {
                m_graphEditor.RegisterUndo(n);
                n.highQualityMode = highQualityMode;
                n.detailLevel = detailLevel;
                n.iterationCount = iterationCount;
                n.iterationPerFrame = iterationPerFrame;

                n.erosionRate = erosionRate;
                n.erosionMultiplier = erosionMultiplier;
                n.restingAngle = restingAngle;
                n.restingAngleMultiplier = restingAngleMultiplier;

                n.heightScale = heightScale;
                n.erosionBoost = erosionBoost;
                n.depositionBoost = depositionBoost;
            }
        }
    }
}
#endif
