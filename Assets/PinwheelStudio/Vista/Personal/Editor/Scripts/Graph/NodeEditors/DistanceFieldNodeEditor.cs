#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using Pinwheel.VistaEditor;
using Pinwheel.Vista.Graph;
using Pinwheel.VistaEditor.Graph;

namespace Pinwheel.VistaEditor.Graph
{
    [NodeEditor(typeof(DistanceFieldNode))]
    public class DistanceFieldNodeEditor : ImageNodeEditorBase
    {
        public static readonly GUIContent ITERATION_COUNT = new GUIContent("Iteration Count", "");

        public override void OnGUI(INode node)
        {
            DistanceFieldNode n = node as DistanceFieldNode;
            EditorGUI.BeginChangeCheck();
            int iterationCount = EditorGUILayout.IntField(ITERATION_COUNT, n.iterationCount);

            if (EditorGUI.EndChangeCheck())
            {
                m_graphEditor.RegisterUndo(n);
                n.iterationCount = iterationCount;
            }
        }
    }
}
#endif
