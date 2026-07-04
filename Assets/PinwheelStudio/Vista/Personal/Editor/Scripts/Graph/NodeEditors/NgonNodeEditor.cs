#if VISTA
using Pinwheel.Vista.Graph;
using UnityEditor;
using UnityEngine;

namespace Pinwheel.VistaEditor.Graph
{
    [NodeEditor(typeof(NgonNode))]
    public class NgonNodeEditor : ImageNodeEditorBase
    {
        private static GUIContent EDGE_COUNT = new GUIContent("Edge Count", "Number of polygon sides");
        private static GUIContent HEIGHT = new GUIContent("Height", "Max height at center or plateau");
        private static GUIContent INNER_RADIUS = new GUIContent("Inner Radius", "Flat top size relative to outer radius. 0 = sharp peak, 1 = flat polygon");
        private static GUIContent OUTER_RADIUS = new GUIContent("Outer Radius", "Base polygon radius relative to image canvas");
        private static GUIContent POINT_UP = new GUIContent("Point Up", "Rotate the shape 90 degrees so a vertex points upward");

        public override void OnGUI(INode node)
        {
            NgonNode n = node as NgonNode;
            int edgeCount;
            float height;
            float innerRadius;
            float outerRadius;
            bool pointUp;

            EditorGUI.BeginChangeCheck();
            edgeCount = EditorGUILayout.IntField(EDGE_COUNT, n.edgeCount);
            height = EditorGUILayout.Slider(HEIGHT, n.height, 0f, 1f);
            innerRadius = EditorGUILayout.Slider(INNER_RADIUS, n.innerRadius, 0f, 1f);
            outerRadius = EditorGUILayout.Slider(OUTER_RADIUS, n.outerRadius, 0.001f, 1f);
            pointUp = EditorGUILayout.Toggle(POINT_UP, n.pointUp);
            if (EditorGUI.EndChangeCheck())
            {
                m_graphEditor.RegisterUndo(n);
                n.edgeCount = edgeCount;
                n.height = height;
                n.innerRadius = innerRadius;
                n.outerRadius = outerRadius;
                n.pointUp = pointUp;
            }
        }
    }
}
#endif
