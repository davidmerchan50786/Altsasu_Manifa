#if VISTA
using System.Collections.Generic;
using UnityEditor.Searcher;
using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;
using System;
using Pinwheel.Vista.Graph;
using UnityEditor;

namespace Pinwheel.VistaEditor.Graph
{
    public class NodeSearcherAdapter : SearcherAdapter
    {
        private VisualElement m_nodePreviewContainer;
        private Label m_nodeAssemblyLabel;
        private Label m_helpLabel;
        private IMGUIContainer m_contextualTipIMGUI;

        public NodeSearcherAdapter(string title) : base(title)
        {
        }

        public override bool HasDetailsPanel
        {
            get
            {
                return true;
            }
        }

        public override void InitDetailsPanel(VisualElement detailsPanel)
        {
            base.InitDetailsPanel(detailsPanel);
            VisualElement helpTextContainer = detailsPanel.Q("windowDetailsVisualContainer");
            if (helpTextContainer != null)
            {
                m_helpLabel = helpTextContainer.Q<Label>();
                if (m_helpLabel != null)
                {
                    m_helpLabel.style.fontSize = new StyleLength(12);
                }
            }

            m_nodePreviewContainer = new VisualElement() { name = "nodePreviewContainer" };
            detailsPanel.Add(m_nodePreviewContainer);

            m_nodePreviewContainer.style.paddingLeft = new StyleLength(8);
            m_nodePreviewContainer.style.paddingTop = new StyleLength(8);
            m_nodePreviewContainer.style.paddingRight = new StyleLength(8);
            m_nodePreviewContainer.style.paddingBottom = new StyleLength(8);
            m_nodePreviewContainer.style.alignItems = new StyleEnum<Align>(Align.Center);
            m_nodePreviewContainer.style.flexGrow = new StyleFloat(1);


            m_nodeAssemblyLabel = new Label() { name = "nodeAssemblyLabel" };
            detailsPanel.Add(m_nodeAssemblyLabel);
            m_nodeAssemblyLabel.style.marginBottom = new StyleLength(4);
            m_nodeAssemblyLabel.style.fontSize = new StyleLength(10);
            m_nodeAssemblyLabel.style.color = new StyleColor(new Color32(100, 100, 100, 255));

            m_contextualTipIMGUI = new IMGUIContainer(OnContextualTipGUI);
            detailsPanel.Insert(0, m_contextualTipIMGUI);

            m_hasEncounterEmptySearchSinceOpened = false;
            contextualTipGuiDelegate = null;
        }

        public override void OnSelectionChanged(IEnumerable<SearcherItem> items)
        {
            base.OnSelectionChanged(items);
            if (m_nodePreviewContainer != null)
            {
                m_nodePreviewContainer.Clear();
            }
            if (m_nodeAssemblyLabel != null)
            {
                m_nodeAssemblyLabel.text = string.Empty;
            }

            SearcherItem item = items.First();
            NodeSearcherItem i = item as NodeSearcherItem;
            if (i == null)
                return;
            if (i.data == null || i.data.nodeType == null)
                return;

            if (string.Equals(i.data.hint, SearcherItemData.HINT_PLACEHOLDER))
            {
                //This entry doesn't server for node creation purpose, so no node preview created here
            }
            else
            {
                INode n = Activator.CreateInstance(i.data.nodeType) as INode;
                TerrainSubGraphNode subGraphNode = n as TerrainSubGraphNode;
                if (subGraphNode != null)
                {
                    string subgraphPath = i.data.hint;
                    TerrainGraph subGraph = AssetDatabase.LoadMainAssetAtPath(subgraphPath) as TerrainGraph;
                    subGraphNode.graph = subGraph;
                    m_nodeAssemblyLabel.text = $"Located at {subgraphPath}";
                }
                else
                {
                    m_nodeAssemblyLabel.text = $"Defined in {i.data.nodeType.Assembly.GetName().Name}";
                }

                NodeView nv = NodeView.Create(n, null);
                m_nodePreviewContainer.Add(nv);

                if (subGraphNode != null && subGraphNode.graph != null)
                {
                    nv.title = subGraphNode.graph.name;
                }
            }
        }

        public override SearcherItem OnSearchResultsFilter(IEnumerable<SearcherItem> searchResults, string searchQuery)
        {
            //hide the smart search tip when there are some matched results
            if (contextualTipGuiDelegate == DrawSmartSearchContextualTip)
                contextualTipGuiDelegate = null;

            return base.OnSearchResultsFilter(searchResults, searchQuery);
        }

        private bool m_hasEncounterEmptySearchSinceOpened;
        private int m_emptySearchCount;
        private const int EMPTY_SEARCH_THRESHOLD_TO_SHOW_HINT = 3;
        public virtual void OnEmptySearchResult(NodeSearcherDatabase database, string query)
        {
            if (EditorCommon.IsPersonalEdition())
            {
                if (!m_hasEncounterEmptySearchSinceOpened)
                {
                    m_emptySearchCount += 1;
                }
                m_hasEncounterEmptySearchSinceOpened = true;

                if (m_emptySearchCount >= EMPTY_SEARCH_THRESHOLD_TO_SHOW_HINT)
                {
                    contextualTipGuiDelegate = DrawSmartSearchContextualTip;
                    m_emptySearchCount = 0;
                }
            }
        }

        private Action contextualTipGuiDelegate;
        private void OnContextualTipGUI()
        {
            contextualTipGuiDelegate?.Invoke();
        }

        private void DrawSmartSearchContextualTip()
        {
            EditorGUILayout.LabelField("Find the right node faster with Smart Search in Indie and Pro, using keywords and related ideas instead of exact names alone.", EditorCommon.Styles.infoLabel);
            Rect buttonRect = EditorGUILayout.GetControlRect();
            if (GUI.Button(buttonRect, "See how it works"))
            {
                //Need to close the searcher first as it is also a popup                
                if (EditorWindow.focusedWindow is SearcherWindow)
                {
                    EditorWindow.focusedWindow.Close();
                }

                UnityEditor.PopupWindow.Show(buttonRect, new HowSmartSearchWorksPopupContent());
            }
        }
    }
}
#endif
