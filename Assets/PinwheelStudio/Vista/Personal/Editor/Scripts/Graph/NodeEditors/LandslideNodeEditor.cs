#if VISTA
using Pinwheel.Vista.Graph;
using UnityEditor;
using UnityEngine;

namespace Pinwheel.VistaEditor.Graph
{
    [NodeEditor(typeof(LandslideNode))]
    public class LandslideNodeEditor : ImageNodeEditorBase
    {
        private static readonly GUIContent GENERAL_HEADER = new GUIContent("General");
        private static readonly GUIContent HIGH_QUALITY = new GUIContent("High Quality", "If true, it will simulate in 8 directions, otherwise 4");
        private static readonly GUIContent DETAIL_LEVEL = new GUIContent("Detail Level", "Smaller value runs faster and produces larger features, while larger value is more expensive but produces more micro details");
        private static readonly GUIContent ITERATION_COUNT = new GUIContent("Iteration", "The number of simulation steps to perform");
        private static readonly GUIContent ITERATION_PER_FRAME = new GUIContent("Iteration Per Frame", "The number of steps to perform in a single frame");

        private static readonly GUIContent SIMULATION_HEADER = new GUIContent("Simulation");
        private static readonly GUIContent INTENSITY = new GUIContent("Intensity", "The strength of the landslide effect");
        private static readonly GUIContent INTENSITY_MULTIPLIER = new GUIContent(" ", "Overall multiplier. Change the intensity without modifying its base value");
        private static readonly GUIContent FLOW_RATE = new GUIContent("Flow Rate", "Speed of the landslide flow");
        private static readonly GUIContent FLOW_MULTIPLIER = new GUIContent(" ", "Overall multiplier. Change the flow speed without modifying its base value");
        private static readonly GUIContent RESTING_ANGLE = new GUIContent("Resting Angle", "The angle at which soil comes to rest");
        private static readonly GUIContent RESTING_ANGLE_MULTIPLIER = new GUIContent(" ", "Overall multiplier. Change the resting angle without modifying its base value");

        public override void OnGUI(INode node)
        {
            LandslideNode n = node as LandslideNode;
            EditorGUI.BeginChangeCheck();

            EditorCommon.Header(GENERAL_HEADER);
            bool highQualityMode = EditorGUILayout.Toggle(HIGH_QUALITY, n.highQualityMode);
            float detailLevel = EditorGUILayout.Slider(DETAIL_LEVEL, n.detailLevel, 0.2f, 1f);
            int iterationCount = EditorGUILayout.DelayedIntField(ITERATION_COUNT, n.iterationCount);
            int iterationPerFrame = EditorGUILayout.DelayedIntField(ITERATION_PER_FRAME, n.iterationPerFrame);

            EditorCommon.Header(SIMULATION_HEADER);
            float intensity = EditorGUILayout.DelayedFloatField(INTENSITY, n.intensity);
            float intensityMultiplier = EditorGUILayout.Slider(INTENSITY_MULTIPLIER, n.intensityMultiplier, 0f, 2f);
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

                n.intensity = intensity;
                n.intensityMultiplier = intensityMultiplier;
                n.flowRate = flowRate;
                n.flowMultiplier = flowMultiplier;
                n.restingAngle = restingAngle;
                n.restingAngleMultiplier = restingAngleMultiplier;
            }
        }
    }
}
#endif
