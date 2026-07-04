#if VISTA
using Pinwheel.Vista.Graph;
using UnityEditor;
using UnityEngine;

namespace Pinwheel.VistaEditor.Graph
{
    [NodeEditor(typeof(GeometryMaskNode))]
    public class GeometryMaskNodeEditor : ImageNodeEditorBase
    {
        private static readonly GUIContent GENERAL_HEADER = new GUIContent("General");
        private static readonly GUIContent BLEND_MODE = new GUIContent("Blend Mode", "How to blend the individual masks together");

        private static readonly GUIContent HEIGHT_MASK_HEADER = new GUIContent("Height Mask");
        private static readonly GUIContent ENABLE_HEIGHT_MASK = new GUIContent("Enable Height Mask", "Enable masking based on terrain height");
        private static readonly GUIContent MIN_HEIGHT = new GUIContent("Min Height", "Minimum height level");
        private static readonly GUIContent MAX_HEIGHT = new GUIContent("Max Height", "Maximum height level");
        private static readonly GUIContent HEIGHT_TRANSITION = new GUIContent("Height Transition", "A curve to remap the height mask");

        private static readonly GUIContent SLOPE_MASK_HEADER = new GUIContent("Slope Mask");
        private static readonly GUIContent ENABLE_SLOPE_MASK = new GUIContent("Enable Slope Mask", "Enable masking based on terrain slope");
        private static readonly GUIContent MIN_ANGLE = new GUIContent("Min Angle", "Minimum slope angle in degrees");
        private static readonly GUIContent MAX_ANGLE = new GUIContent("Max Angle", "Maximum slope angle in degrees");
        private static readonly GUIContent SLOPE_TRANSITION = new GUIContent("Slope Transition", "A curve to remap the slope mask");

        private static readonly GUIContent DIRECTION_MASK_HEADER = new GUIContent("Direction Mask");
        private static readonly GUIContent ENABLE_DIRECTION_MASK = new GUIContent("Enable Direction Mask", "Enable masking based on terrain facing direction");
        private static readonly GUIContent DIRECTION = new GUIContent("Direction", "The target direction angle in degrees");
        private static readonly GUIContent DIRECTION_TOLERANCE = new GUIContent("Direction Tolerance", "The tolerance angle in degrees");
        private static readonly GUIContent DIRECTION_FALLOFF = new GUIContent("Direction Falloff", "A curve to control the falloff of the direction mask");

        public override void OnGUI(INode node)
        {
            GeometryMaskNode n = node as GeometryMaskNode;
            EditorGUI.BeginChangeCheck();

            EditorCommon.Header(GENERAL_HEADER);
            GeometryMaskNode.BlendMode blendMode = (GeometryMaskNode.BlendMode)EditorGUILayout.EnumPopup(BLEND_MODE, n.blendMode);

            EditorCommon.Header(HEIGHT_MASK_HEADER);
            bool enableHeightMask = EditorGUILayout.Toggle(ENABLE_HEIGHT_MASK, n.enableHeightMask);
            EditorGUI.BeginDisabledGroup(!enableHeightMask);
            float minHeight = EditorGUILayout.FloatField(MIN_HEIGHT, n.minHeight);
            float maxHeight = EditorGUILayout.FloatField(MAX_HEIGHT, n.maxHeight);
            AnimationCurve heightTransition = EditorGUILayout.CurveField(HEIGHT_TRANSITION, n.heightTransition, Color.red, new Rect(0,0,1,1));
            EditorGUI.EndDisabledGroup();

            EditorCommon.Header(SLOPE_MASK_HEADER);
            bool enableSlopeMask = EditorGUILayout.Toggle(ENABLE_SLOPE_MASK, n.enableSlopeMask);
            EditorGUI.BeginDisabledGroup(!enableSlopeMask);
            float minAngle = EditorGUILayout.FloatField(MIN_ANGLE, n.minAngle);
            float maxAngle = EditorGUILayout.FloatField(MAX_ANGLE, n.maxAngle);
            AnimationCurve slopeTransition = EditorGUILayout.CurveField(SLOPE_TRANSITION, n.slopeTransition, Color.red, new Rect(0,0,1,1));
            EditorGUI.EndDisabledGroup();

            EditorCommon.Header(DIRECTION_MASK_HEADER);
            bool enableDirectionMask = EditorGUILayout.Toggle(ENABLE_DIRECTION_MASK, n.enableDirectionMask);
            EditorGUI.BeginDisabledGroup(!enableDirectionMask);
            float direction = EditorGUILayout.Slider(DIRECTION, n.direction, 0f, 360f);
            float directionTolerance = EditorGUILayout.Slider(DIRECTION_TOLERANCE, n.directionTolerance, 0f, 180f);
            AnimationCurve directionFalloff = EditorGUILayout.CurveField(DIRECTION_FALLOFF, n.directionFalloff, Color.red, new Rect(0,0,1,1));
            EditorGUI.EndDisabledGroup();

            if (EditorGUI.EndChangeCheck())
            {
                m_graphEditor.RegisterUndo(n);
                n.blendMode = blendMode;
                n.enableHeightMask = enableHeightMask;
                n.minHeight = minHeight;
                n.maxHeight = maxHeight;
                n.heightTransition = heightTransition;
                n.enableSlopeMask = enableSlopeMask;
                n.minAngle = minAngle;
                n.maxAngle = maxAngle;
                n.slopeTransition = slopeTransition;
                n.enableDirectionMask = enableDirectionMask;
                n.direction = direction;
                n.directionTolerance = directionTolerance;
                n.directionFalloff = directionFalloff;
            }
        }
    }
}
#endif
