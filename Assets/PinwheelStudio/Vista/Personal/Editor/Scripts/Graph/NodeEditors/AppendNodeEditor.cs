#if VISTA
using Pinwheel.Vista.Graph;
using UnityEditor;
using UnityEngine;

namespace Pinwheel.VistaEditor.Graph
{
    [NodeEditor(typeof(AppendNode))]
    public class AppendNodeEditor : ExecutableNodeEditorBase
    {
        private static readonly GUIContent INPUT_COUNT = new GUIContent("Input Count", "Number of input buffers to append together.");

        public override void OnGUI(INode node)
        {
            AppendNode n = node as AppendNode;
            EditorGUI.BeginChangeCheck();
            int inputCount = EditorGUILayout.DelayedIntField(INPUT_COUNT, n.inputCount);
            if (EditorGUI.EndChangeCheck())
            {
                m_graphEditor.RegisterUndo(n);
                n.inputCount = inputCount;
            }
        }
    }
}
#endif
