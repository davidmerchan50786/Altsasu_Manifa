#if VISTA
using Pinwheel.Vista.Graph;
using UnityEditor;
using UnityEngine;
using Type = System.Type;

namespace Pinwheel.VistaEditor.Graph
{
    [NodeEditor(typeof(DefaultValueNode))]
    public class DefaultValueNodeEditor : ExecutableNodeEditorBase
    {
        private static readonly GUIContent SETTINGS_HEADER = new GUIContent("Settings");
        private static readonly GUIContent SLOT_TYPE = new GUIContent("Slot Type", "The data type for input and output slots");

        private static readonly string[] SLOT_TYPE_LABELS = new string[] { "Mask", "Color Texture", "Buffer" };

        private static Type[] GetSlotTypes()
        {
            return new Type[]
            {
                typeof(MaskSlot),
                typeof(ColorTextureSlot),
                typeof(BufferSlot)
            };
        }

        private static int GetCurrentIndex(Type slotType)
        {
            Type[] slotTypes = GetSlotTypes();
            for (int i = 0; i < slotTypes.Length; i++)
            {
                if (slotTypes[i] == slotType)
                {
                    return i;
                }
            }
            return 0;
        }

        public override void OnGUI(INode node)
        {
            DefaultValueNode n = node as DefaultValueNode;
            EditorGUI.BeginChangeCheck();

            EditorCommon.Header(SETTINGS_HEADER);
            int currentIndex = GetCurrentIndex(n.slotType);
            int selectedIndex = EditorGUILayout.Popup(SLOT_TYPE, currentIndex, SLOT_TYPE_LABELS);

            if (EditorGUI.EndChangeCheck())
            {
                m_graphEditor.RegisterUndo(n);
                Type[] slotTypes = GetSlotTypes();
                n.SetSlotType(slotTypes[selectedIndex]);
            }
        }
    }
}
#endif
