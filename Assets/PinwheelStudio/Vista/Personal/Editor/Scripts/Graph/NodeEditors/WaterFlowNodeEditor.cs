#if VISTA
using Pinwheel.Vista.Graph;
using UnityEditor;
using UnityEngine;

namespace Pinwheel.VistaEditor.Graph
{
    [NodeEditor(typeof(WaterFlowNode))]
    public class WaterFlowNodeEditor : ImageNodeEditorBase
    {
        private static readonly GUIContent GENERAL_HEADER = new GUIContent("General");
        private static readonly GUIContent HIGH_QUALITY = new GUIContent("High Quality", "If true, it will simulate water flow in 8 directions, otherwise 4");
        private static readonly GUIContent DETAIL_LEVEL = new GUIContent("Detail Level", "Smaller value runs faster and produces larger features, while larger value is more expensive but produces more micro details");
        private static readonly GUIContent ITERATION_COUNT = new GUIContent("Iteration", "The number of simulation steps to perform");
        private static readonly GUIContent ITERATION_PER_FRAME = new GUIContent("Iteration Per Frame", "The number of steps to perform in a single frame");

        private static readonly GUIContent SIMULATION_HEADER = new GUIContent("Simulation");
        private static readonly GUIContent WATER_SOURCE_AMOUNT = new GUIContent("Water Source", "The amount of water poured into the system in each iteration");
        private static readonly GUIContent WATER_SOURCE_MULTIPLIER = new GUIContent(" ", "Overall multiplier. Change the water source amount without modifying its base value");
        private static readonly GUIContent RAIN_RATE = new GUIContent("Rain Rate", "The amount of rain added to the system");
        private static readonly GUIContent RAIN_MULTIPLIER = new GUIContent(" ", "Overall multiplier. Change the rain rate without modifying its base value");
        private static readonly GUIContent FLOW_RATE = new GUIContent("Flow Rate", "Water flow speed");
        private static readonly GUIContent FLOW_MULTIPLIER = new GUIContent(" ", "Overall multiplier. Change the flow speed without modifying its base value");
        private static readonly GUIContent EVAPORATION_RATE = new GUIContent("Evaporation Rate", "Strength of the evaporation that removes water from the system");
        private static readonly GUIContent EVAPORATION_MULTIPLIER = new GUIContent(" ", "Overall multiplier. Change the evaporation strength without modifying its base value");

        public override void OnGUI(INode node)
        {
            WaterFlowNode n = node as WaterFlowNode;
            EditorGUI.BeginChangeCheck();

            EditorCommon.Header(GENERAL_HEADER);
            bool highQualityMode = EditorGUILayout.Toggle(HIGH_QUALITY, n.highQualityMode);
            float detailLevel = EditorGUILayout.Slider(DETAIL_LEVEL, n.detailLevel, 0.2f, 1f);
            int iterationCount = EditorGUILayout.DelayedIntField(ITERATION_COUNT, n.iterationCount);
            int iterationPerFrame = EditorGUILayout.DelayedIntField(ITERATION_PER_FRAME, n.iterationPerFrame);

            EditorCommon.Header(SIMULATION_HEADER);
            float waterSourceAmount = EditorGUILayout.DelayedFloatField(WATER_SOURCE_AMOUNT, n.waterSourceAmount);
            float waterSourceMultiplier = EditorGUILayout.Slider(WATER_SOURCE_MULTIPLIER, n.waterSourceMultiplier, 0f, 2f);
            float rainRate = EditorGUILayout.DelayedFloatField(RAIN_RATE, n.rainRate);
            float rainMultiplier = EditorGUILayout.Slider(RAIN_MULTIPLIER, n.rainMultiplier, 0f, 2f);
            float flowRate = EditorGUILayout.DelayedFloatField(FLOW_RATE, n.flowRate);
            float flowMultiplier = EditorGUILayout.Slider(FLOW_MULTIPLIER, n.flowMultiplier, 0f, 2f);
            float evaporationRate = EditorGUILayout.DelayedFloatField(EVAPORATION_RATE, n.evaporationRate);
            float evaporationMultiplier = EditorGUILayout.Slider(EVAPORATION_MULTIPLIER, n.evaporationMultiplier, 0f, 2f);

            if (EditorGUI.EndChangeCheck())
            {
                m_graphEditor.RegisterUndo(n);
                n.highQualityMode = highQualityMode;
                n.detailLevel = detailLevel;
                n.iterationCount = iterationCount;
                n.iterationPerFrame = iterationPerFrame;

                n.waterSourceAmount = waterSourceAmount;
                n.waterSourceMultiplier = waterSourceMultiplier;
                n.rainRate = rainRate;
                n.rainMultiplier = rainMultiplier;
                n.flowRate = flowRate;
                n.flowMultiplier = flowMultiplier;
                n.evaporationRate = evaporationRate;
                n.evaporationMultiplier = evaporationMultiplier;
            }
        }
    }
}
#endif
