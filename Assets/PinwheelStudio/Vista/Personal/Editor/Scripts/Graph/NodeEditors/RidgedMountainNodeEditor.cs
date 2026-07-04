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
    [NodeEditor(typeof(RidgedMountainNode))]
    public class RidgedMountainNodeEditor : ImageNodeEditorBase
    {
        private static readonly GUIContent SCALE = new GUIContent("Scale", "Size of the mountains");
        private static readonly GUIContent RIDGE_INTENSITY = new GUIContent("Ridge Intensity", "Defines ridges sharpness");
        private static readonly GUIContent SEED = new GUIContent("Seed", "Randomize the result with an integer");

        public override void OnGUI(INode node)
        {
            RidgedMountainNode n = node as RidgedMountainNode;
            EditorGUI.BeginChangeCheck();
            float scale = EditorGUILayout.FloatField(SCALE, n.scale);
            float ridgeIntensity = EditorGUILayout.Slider(RIDGE_INTENSITY, n.ridgeIntensity, 0f, 20f);
            int seed = EditorGUILayout.IntField(SEED, n.seed);

            if (EditorGUI.EndChangeCheck())
            {
                m_graphEditor.RegisterUndo(n);
                n.scale = scale;
                n.ridgeIntensity = ridgeIntensity;
                n.seed = seed;
            }
        }
    }
}
#endif
