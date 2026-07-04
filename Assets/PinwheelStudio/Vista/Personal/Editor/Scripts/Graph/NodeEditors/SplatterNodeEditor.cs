#if VISTA
using Pinwheel.Vista.Graph;
using UnityEditor;
using UnityEngine;

namespace Pinwheel.VistaEditor.Graph
{
    [NodeEditor(typeof(SplatterNode))]
    public class SplatterNodeEditor : ImageNodeEditorBase
    {
        private static readonly GUIContent SHAPE_HEADER = new GUIContent("Shape");
        private static readonly GUIContent SIZE = new GUIContent("Size", "Size of the splatter element");
        private static readonly GUIContent SIZE_IN_WORLD_SPACE = new GUIContent("Size In World Space", "If true, the size is measured in world space units");

        private static readonly GUIContent ROTATION_HEADER = new GUIContent("Rotation");
        private static readonly GUIContent MIN_ROTATION = new GUIContent("Min Rotation", "Minimum rotation angle in degrees");
        private static readonly GUIContent MAX_ROTATION = new GUIContent("Max Rotation", "Maximum rotation angle in degrees");
        private static readonly GUIContent ROTATION_MULTIPLIER = new GUIContent("Rotation Multiplier", "Overall multiplier for the rotation effect");

        private static readonly GUIContent SCALE_HEADER = new GUIContent("Scale");
        private static readonly GUIContent SCALE_MULTIPLIER = new GUIContent("Scale Multiplier", "Overall multiplier for the scale effect");

        private static readonly GUIContent INTENSITY_HEADER = new GUIContent("Intensity");
        private static readonly GUIContent INTENSITY_MULTIPLIER = new GUIContent("Intensity Multiplier", "Overall multiplier for the intensity effect");

        public override void OnGUI(INode node)
        {
            SplatterNode n = node as SplatterNode;
            EditorGUI.BeginChangeCheck();

            EditorCommon.Header(SHAPE_HEADER);
            float size = EditorGUILayout.FloatField(SIZE, n.size);
            bool sizeInWorldSpace = EditorGUILayout.Toggle(SIZE_IN_WORLD_SPACE, n.sizeInWorldSpace);

            EditorCommon.Header(ROTATION_HEADER);
            float minRotation = EditorGUILayout.FloatField(MIN_ROTATION, n.minRotation);
            float maxRotation = EditorGUILayout.FloatField(MAX_ROTATION, n.maxRotation);
            float rotationMultiplier = EditorGUILayout.Slider(ROTATION_MULTIPLIER, n.rotationMultiplier, 0f, 1f);

            EditorCommon.Header(SCALE_HEADER);
            float scaleMultiplier = EditorGUILayout.Slider(SCALE_MULTIPLIER, n.scaleMultiplier, 0f, 1f);

            EditorCommon.Header(INTENSITY_HEADER);
            float intensityMultiplier = EditorGUILayout.Slider(INTENSITY_MULTIPLIER, n.intensityMultiplier, 0f, 1f);

            if (EditorGUI.EndChangeCheck())
            {
                m_graphEditor.RegisterUndo(n);
                n.size = size;
                n.sizeInWorldSpace = sizeInWorldSpace;
                n.minRotation = minRotation;
                n.maxRotation = maxRotation;
                n.rotationMultiplier = rotationMultiplier;
                n.scaleMultiplier = scaleMultiplier;
                n.intensityMultiplier = intensityMultiplier;
            }
        }
    }
}
#endif
