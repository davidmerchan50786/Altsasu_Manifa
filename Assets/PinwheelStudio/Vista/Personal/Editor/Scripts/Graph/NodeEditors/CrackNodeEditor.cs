#if VISTA
using Pinwheel.Vista.Graph;
using UnityEditor;
using UnityEngine;

namespace Pinwheel.VistaEditor.Graph
{
    [NodeEditor(typeof(CrackNode))]
    public class CrackNodeEditor : ImageNodeEditorBase
    {
        private static readonly GUIContent SHAPE_HEADER = new GUIContent("Shape");
        private static readonly GUIContent SMOOTHNESS = new GUIContent("Smoothness", "Smoothness of the crack edges");
        private static readonly GUIContent WIDTH = new GUIContent("Width", "Width of the cracks");
        private static readonly GUIContent LENGTH = new GUIContent("Length", "Length of the cracks");
        private static readonly GUIContent DEPTH = new GUIContent("Depth", "Depth of the cracks");
        private static readonly GUIContent ANGLE_LIMIT = new GUIContent("Angle Limit", "Maximum angle deviation for crack branching");

        private static readonly GUIContent SIMULATION_HEADER = new GUIContent("Simulation");
        private static readonly GUIContent ITERATION_COUNT = new GUIContent("Iteration", "The number of simulation steps to perform");

        public override void OnGUI(INode node)
        {
            CrackNode n = node as CrackNode;
            EditorGUI.BeginChangeCheck();

            EditorCommon.Header(SHAPE_HEADER);
            float smoothness = EditorGUILayout.Slider(SMOOTHNESS, n.smoothness, 0.01f, 1f);
            float width = EditorGUILayout.FloatField(WIDTH, n.width);
            float length = EditorGUILayout.FloatField(LENGTH, n.length);
            float depth = EditorGUILayout.Slider(DEPTH, n.depth, 0f, 1f);
            float angleLimit = EditorGUILayout.Slider(ANGLE_LIMIT, n.angleLimit, 0f, 90f);

            EditorCommon.Header(SIMULATION_HEADER);
            int iterationCount = EditorGUILayout.IntField(ITERATION_COUNT, n.iterationCount);

            if (EditorGUI.EndChangeCheck())
            {
                m_graphEditor.RegisterUndo(n);
                n.smoothness = smoothness;
                n.width = width;
                n.length = length;
                n.depth = depth;
                n.angleLimit = angleLimit;
                n.iterationCount = iterationCount;
            }
        }
    }
}
#endif
