#if VISTA
using Pinwheel.Vista.Graph;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Pinwheel.VistaEditor.Graph
{
    [NodeEditor(typeof(LayeredNoiseNode))]
    public class LayeredNoiseNodeEditor : ImageNodeEditorBase
    {
        private static readonly GUIContent GENERAL_HEADER = new GUIContent("General");
        private static readonly GUIContent BASE_SCALE = new GUIContent("Base Scale", "The base scale of the noise pattern");
        private static readonly GUIContent SEED = new GUIContent("Seed", "Random seed for the noise generation");

        private static readonly GUIContent LAYERS_HEADER = new GUIContent("Layers");
        private static readonly GUIContent NOISE_MODE = new GUIContent("Mode", "The noise algorithm to use for this layer");
        private static readonly GUIContent STRENGTH = new GUIContent("Strength", "The strength of this noise layer");
        private static readonly GUIContent ADD_LAYER = new GUIContent("+", "Add a new noise layer");
        private static readonly GUIContent REMOVE_LAYER = new GUIContent("-", "Remove this noise layer");

        public override void OnGUI(INode node)
        {
            LayeredNoiseNode n = node as LayeredNoiseNode;

            EditorGUI.BeginChangeCheck();
            EditorCommon.Header(GENERAL_HEADER);
            float baseScale = EditorGUILayout.FloatField(BASE_SCALE, n.baseScale);
            int seed = EditorGUILayout.IntField(SEED, n.seed);
            if (EditorGUI.EndChangeCheck())
            {
                m_graphEditor.RegisterUndo(n);
                n.baseScale = baseScale;
                n.seed = seed;
            }

            EditorCommon.Header(LAYERS_HEADER);
            int indexToRemove = -1;
            List<LayeredNoiseNode.LayerConfig> currentLayers = n.layers;
            for (int i = 0; i < currentLayers.Count; ++i)
            {
                LayeredNoiseNode.LayerConfig layer = currentLayers[i];
                EditorGUILayout.BeginHorizontal();

                EditorGUI.BeginChangeCheck();
                NoiseMode mode = (NoiseMode)EditorGUILayout.EnumPopup(layer.mode);
                float strength = EditorGUILayout.Slider(layer.strength, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    m_graphEditor.RegisterUndo(n);
                    layer.mode = mode;
                    layer.strength = strength;
                }

                if (currentLayers.Count > 1)
                {
                    if (GUILayout.Button(REMOVE_LAYER, GUILayout.Width(20)))
                    {
                        indexToRemove = i;
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            if (indexToRemove >= 0)
            {
                m_graphEditor.RegisterUndo(n);
                List<LayeredNoiseNode.LayerConfig> layers = new List<LayeredNoiseNode.LayerConfig>(n.layers);
                layers.RemoveAt(indexToRemove);
                n.layers = layers;
            }

            Rect addRect = EditorGUILayout.GetControlRect();
            if (GUI.Button(addRect, ADD_LAYER))
            {
                m_graphEditor.RegisterUndo(n);
                List<LayeredNoiseNode.LayerConfig> layers = new List<LayeredNoiseNode.LayerConfig>(n.layers);
                layers.Add(new LayeredNoiseNode.LayerConfig());
                n.layers = layers;
            }
        }
    }
}
#endif
