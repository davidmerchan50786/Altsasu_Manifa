#if VISTA
#if UNITY_6000_0_OR_NEWER && UNITY_EDITOR_OSX
using Pinwheel.VistaEditor.Graph;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using UnityEditor.Searcher;
using UnityEngine;

namespace Pinwheel.VistaEditor.Graph
{
    public static class GraphViewNewNodeUtilityMenuPopulator
    {
        [InitializeOnLoadMethod]
        private static void OnInitialize()
        {
            GraphEditorGraphView.buildContextualCallback += Populate;
        }

        private static void Populate(GraphEditorGraphView graphView, ContextualMenuPopulateEvent evt)
        {
            if (evt.target is GraphView)
            {
                if (graphView.m_editor is TerrainGraphEditor graphEditor)
                {
                    TerrainGraphAdapter adapter = graphEditor.m_adapter as TerrainGraphAdapter;
                    SearcherFilter filter = new SearcherFilter();
                    filter.typeFilter = graphEditor.clonedGraph.AcceptNodeType;
                    filter.sourceGraphPath = AssetDatabase.GetAssetPath(graphEditor.sourceGraph);
                    Searcher searcher = adapter.searcherProvider.GetSearcher(filter);
                    var result = searcher.Search("");

                    foreach (var r in result)
                    {
                        if (!r.HasChildren)
                        {
                            string path = $"New Node/{r.Path.Replace(r.Name, $"/{r.Name}")}";
                            SearcherItemData data = default;
                            if (r is NodeSearcherItem i)
                            {
                                data = i.data;
                            }

                            Vector2 nodePos = graphView.ChangeCoordinatesTo(graphView.contentViewContainer, evt.localMousePosition);
                            Rect nodeRect = new Rect(nodePos, Vector2.one * 100);

                            evt.menu.AppendAction(path, (a) =>
                            {
                                GraphEditorGraphView.AddNodeResult addResult = graphView.AddNodeOfType(data.nodeType, nodeRect);
                            }, DropdownMenuAction.Status.Normal);
                        }
                    }
                }
            }
        }
    }
}
#endif
#endif
