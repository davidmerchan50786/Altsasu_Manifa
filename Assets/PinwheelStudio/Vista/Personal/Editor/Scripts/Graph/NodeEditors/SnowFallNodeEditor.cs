#if VISTA
using Pinwheel.Vista.Graph;
using UnityEditor;
using UnityEngine;

namespace Pinwheel.VistaEditor.Graph
{
    [NodeEditor(typeof(SnowFallNode))]
    public class SnowFallNodeEditor : ImageNodeEditorBase
    {
        private static readonly GUIContent GENERAL_HEADER = new GUIContent("General");
        private static readonly GUIContent HIGH_QUALITY = new GUIContent("High Quality", "If true, it will simulate in 8 directions, otherwise 4");
        private static readonly GUIContent DETAIL_LEVEL = new GUIContent("Detail Level", "Smaller value runs faster and produces larger features, while larger value is more expensive but produces more micro details");
        private static readonly GUIContent ITERATION_COUNT = new GUIContent("Iteration", "The number of simulation steps to perform");
        private static readonly GUIContent ITERATION_PER_FRAME = new GUIContent("Iteration Per Frame", "The number of steps to perform in a single frame");

        private static readonly GUIContent SIMULATION_HEADER = new GUIContent("Simulation");
        private static readonly GUIContent SNOW_AMOUNT = new GUIContent("Snow Amount", "The amount of snow added to the system in each iteration");
        private static readonly GUIContent SNOW_MULTIPLIER = new GUIContent(" ", "Overall multiplier. Change the snow amount without modifying its base value");
        private static readonly GUIContent FLOW_RATE = new GUIContent("Flow Rate", "Speed of the snow flow");
        private static readonly GUIContent FLOW_MULTIPLIER = new GUIContent(" ", "Overall multiplier. Change the flow speed without modifying its base value");
        private static readonly GUIContent RESTING_ANGLE = new GUIContent("Resting Angle", "The angle at which snow comes to rest");
        private static readonly GUIContent RESTING_ANGLE_MULTIPLIER = new GUIContent(" ", "Overall multiplier. Change the resting angle without modifying its base value");

        public override void OnGUI(INode node)
        {
            SnowFallNode n = node as SnowFallNode;
            EditorGUI.BeginChangeCheck();

            EditorCommon.Header(GENERAL_HEADER);
            bool highQualityMode = EditorGUILayout.Toggle(HIGH_QUALITY, n.highQualityMode);
            float detailLevel = EditorGUILayout.Slider(DETAIL_LEVEL, n.detailLevel, 0.2f, 1f);
            int iterationCount = EditorGUILayout.DelayedIntField(ITERATION_COUNT, n.iterationCount);
            int iterationPerFrame = EditorGUILayout.DelayedIntField(ITERATION_PER_FRAME, n.iterationPerFrame);

            EditorCommon.Header(SIMULATION_HEADER);
            float snowAmount = EditorGUILayout.DelayedFloatField(SNOW_AMOUNT, n.snowAmount);
            float snowMultiplier = EditorGUILayout.Slider(SNOW_MULTIPLIER, n.snowMultiplier, 0f, 2f);
            float flowRate = EditorGUILayout.DelayedFloatField(FLOW_RATE, n.flowRate);
            float flowMultiplier = EditorGUILayout.Slider(FLOW_MULTIPLIER, n.flowMultiplier, 0f, 2f);
            float restingAngle = EditorGUILayout.Slider(RESTING_ANGLE, n.restingAngle, 0f, 90f);
            float restingAngleMultiplier = EditorGUILayout.Slider(RESTING_ANGLE_MULTIPLIER, n.restingAngleMultiplier, 0f, 2f);

            if (EditorGUI.EndChangeCheck())
            {
                m_graphEditor.RegisterUndo(n);
                n.highQualityMode = highQualityMode;
                n.detailLevel = detailLevel;
                n.iterationCount = iterationCount;
                n.iterationPerFrame = iterationPerFrame;

                n.snowAmount = snowAmount;
                n.snowMultiplier = snowMultiplier;
                n.flowRate = flowRate;
                n.flowMultiplier = flowMultiplier;
                n.restingAngle = restingAngle;
                n.restingAngleMultiplier = restingAngleMultiplier;
            }
        }
    }
}
#endif
