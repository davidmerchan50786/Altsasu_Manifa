#if VISTA
using Pinwheel.Vista.Graph;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Pinwheel.VistaEditor.Graph
{
    [NodeEditor(typeof(CopyToRTNode))]
    public class CopyToRTNodeEditor : ExecutableNodeEditorBase, INeedUpdateNodeVisual
    {
        private static readonly GUIContent SLOT_TYPE = new GUIContent("Slot Type", "Data type of the slot");
        private static readonly GUIContent RT_ASSET = new GUIContent("Render Texture Asset", "The render texture in your Assets folder");

        public override void OnGUI(INode node)
        {
            CopyToRTNode n = node as CopyToRTNode;
            EditorGUI.BeginChangeCheck();
            List<Type> slotTypes = SlotProvider.GetTextureSlotTypes();
            int selectedTypeIndex = slotTypes.IndexOf(n.slotType);
            string[] slotTypeLabels = new string[slotTypes.Count];
            for (int i = 0; i < slotTypes.Count; ++i)
            {
                slotTypeLabels[i] = ObjectNames.NicifyVariableName(slotTypes[i].Name);
            }
            selectedTypeIndex = EditorGUILayout.Popup(SLOT_TYPE, selectedTypeIndex, slotTypeLabels);

            RenderTexture rt = n.rtAsset;
            rt = EditorGUILayout.ObjectField(RT_ASSET, rt, typeof(RenderTexture), false) as RenderTexture;

            if (EditorGUI.EndChangeCheck())
            {
                m_graphEditor.RegisterUndo(n);
                if (selectedTypeIndex >= 0 && selectedTypeIndex < slotTypes.Count)
                {
                    n.SetSlotType(slotTypes[selectedTypeIndex]);
                }
                else
                {
                    n.SetSlotType(slotTypes[0]);
                }
                n.rtAsset = rt;
            }
        }

        public void UpdateVisual(INode node, NodeView nv)
        {
            CopyToRTNode n = node as CopyToRTNode;
            PortView pv = nv.Q<PortView>();
            if (pv != null)
            {
                pv.portName = "Input";
            }
        }
    }
}
#endif
