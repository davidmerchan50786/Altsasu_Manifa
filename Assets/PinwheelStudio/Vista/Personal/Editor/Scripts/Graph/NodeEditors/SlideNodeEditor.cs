#if VISTA
using Pinwheel.Vista.Graph;
using UnityEditor;
using UnityEngine;

namespace Pinwheel.VistaEditor.Graph
{
    [NodeEditor(typeof(SlideNode))]
    public class SlideNodeEditor : ExecutableNodeEditorBase
    {
        private static readonly GUIContent SIMULATION_HEADER = new GUIContent("Simulation");
        private static readonly GUIContent ITERATION_COUNT = new GUIContent("Iteration", "The number of simulation steps to perform");
        private static readonly GUIContent TRAIL_CURVATURE = new GUIContent("Trail Curvature", "Curvature of the slide trail");

        public override void OnGUI(INode node)
        {
            SlideNode n = node as SlideNode;
            EditorGUI.BeginChangeCheck();

            EditorCommon.Header(SIMULATION_HEADER);
            int iterationCount = EditorGUILayout.IntField(ITERATION_COUNT, n.iterationCount);
            float trailCurvature = EditorGUILayout.Slider(TRAIL_CURVATURE, n.trailCurvature, 0f, 1f);

            if (EditorGUI.EndChangeCheck())
            {
                m_graphEditor.RegisterUndo(n);
                n.iterationCount = iterationCount;
                n.trailCurvature = trailCurvature;
            }
        }
    }
}
#endif
