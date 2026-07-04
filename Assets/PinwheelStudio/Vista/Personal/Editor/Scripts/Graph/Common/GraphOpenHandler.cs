#if VISTA
using Pinwheel.Vista.Graph;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using GraphEditorUIVersion = Pinwheel.VistaEditor.EditorSettings.GraphEditorSettings.UIVersion;

namespace Pinwheel.VistaEditor.Graph
{
    public static class GraphOpenHandler
    {
        [OnOpenAsset(0)]
        public static bool HandleOpenGraphAsset(int instanceId, int line)
        {
            Object asset = EditorUtility.InstanceIDToObject(instanceId); 
            if (asset is GraphAsset)
            {
                return GraphEditorBase.OpenGraph(asset as GraphAsset);
            }
            else
            {
                return false;
            }
        }
    }
}
#endif
