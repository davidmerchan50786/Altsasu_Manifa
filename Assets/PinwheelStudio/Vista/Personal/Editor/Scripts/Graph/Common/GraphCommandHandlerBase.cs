#if VISTA
using Pinwheel.Vista.Graph;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Pinwheel.VistaEditor.Graph
{
    public abstract class GraphCommandHandlerBase<T> : ICommandHandler where T : GraphAsset
    {
        public GraphEditorBase editor { get; set; }

        public void Save()
        {
            bool isSourceGraphPersistent = editor.sourceGraph != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(editor.sourceGraph));

            if (isSourceGraphPersistent)
            {
                bool hasModification = EditorUtility.IsDirty(editor.clonedGraph);

                CopyGraphData(editor.clonedGraph as T, editor.sourceGraph as T);
                EditorUtility.SetDirty(editor.sourceGraph);
                AssetDatabase.SaveAssets();
                EditorUtility.ClearDirty(editor.clonedGraph);

                if (hasModification)
                {
                    editor.sourceGraph.InvokeChangeEvent();
                }
            }
            else
            {
                SaveAs();
            }
        }

        public void SaveAs()
        {
            string path = EditorUtility.SaveFilePanelInProject("Save As...", "", "asset", "");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            GraphAsset newAsset = ScriptableObject.CreateInstance<GraphAsset>();
            newAsset.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(newAsset, path);

            editor.sourceGraph = newAsset;
            EditorUtility.SetDirty(editor.clonedGraph);
            Save();
        }

        protected abstract void CopyGraphData(T from, T to);
    }
}
#endif
