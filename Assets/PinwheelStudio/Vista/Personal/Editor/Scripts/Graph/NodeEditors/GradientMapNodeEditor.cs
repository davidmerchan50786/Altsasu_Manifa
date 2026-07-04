#if VISTA
using Pinwheel.Vista.Graph;
using UnityEditor;
using UnityEngine;

namespace Pinwheel.VistaEditor.Graph
{
    [NodeEditor(typeof(GradientMapNode))]
    public class GradientMapNodeEditor : ImageNodeEditorBase
    {
        private static readonly GUIContent GRADIENT = new GUIContent("Gradient", "The gradient to map input values to");
        private static readonly GUIContent WRAP_MODE = new GUIContent("Wrap Mode", "How the gradient wraps when the input value exceeds the range");
        private static readonly GUIContent LOOP = new GUIContent("Loop", "Number of times the gradient repeats");

        public override void OnGUI(INode node)
        {
            GradientMapNode n = node as GradientMapNode;
            EditorGUI.BeginChangeCheck();
            Gradient gradient = EditorGUILayout.GradientField(GRADIENT, n.gradient);
            TextureWrapMode wrapMode = (TextureWrapMode)EditorGUILayout.EnumPopup(WRAP_MODE, n.wrapMode);
            float loop = EditorGUILayout.FloatField(LOOP, n.loop);
            if (EditorGUI.EndChangeCheck())
            {
                m_graphEditor.RegisterUndo(n);
                n.gradient = gradient;
                n.wrapMode = wrapMode;
                n.loop = loop;
            }
        }
    }
}
#endif
