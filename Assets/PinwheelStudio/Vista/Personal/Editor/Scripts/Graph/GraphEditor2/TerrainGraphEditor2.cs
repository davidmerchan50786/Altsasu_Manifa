#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;
using Pinwheel.VistaEditor.Graph;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using System;
using Pinwheel.VistaEditor.UIElements;
using Pinwheel.Vista.Graph;
using UnityEditor;
using static Pinwheel.VistaEditor.EditorSettings.MarketingSettings;

namespace Pinwheel.VistaEditor.Graph
{
    public class TerrainGraphEditor2 : TerrainGraphEditor
    {
        protected Viewport2DSubWindow m_viewport2DSubWindow;
        protected Viewport3DSubWindow m_viewport3DSubWindow;
        protected NodePropertiesSubWindow m_nodePropertiesSubWindow;
        protected EditorSettingsSubWindow m_editorSettingsSubWindow;
        protected NotificationSubWindow m_notificationSubWindow;

        protected UtilityButton m_splitExecButton;
        protected UtilityButton m_editorSettingsButton;
        protected UtilityButton m_emailButton;
        protected UtilityButton m_documentationButton;
        protected UtilityButton m_notificationButton;

        protected Ribbon m_ribbon;

        protected override void SetupAdapter()
        {
            TerrainGraphAdapter2 adapter = new TerrainGraphAdapter2();
            adapter.Init(this);
            m_adapter = adapter;
        }

        protected override void OnSetupGUI()
        {
            base.OnSetupGUI();
            m_bodyContainer.style.paddingLeft = new StyleLength(0f);
            m_bodyContainer.style.paddingTop = new StyleLength(0f);
            m_bodyContainer.style.paddingRight = new StyleLength(0f);
            m_bodyContainer.style.paddingBottom = new StyleLength(0f);

            m_graphView.RemoveFromHierarchy();
            m_graphView.style.marginLeft = new StyleLength(0f);
            m_graphView.style.marginTop = new StyleLength(0f);
            m_graphView.style.marginRight = new StyleLength(0f);
            m_graphView.style.marginBottom = new StyleLength(0f);
            m_graphView.style.borderLeftWidth = new StyleFloat(0f);
            m_graphView.style.borderTopWidth = new StyleFloat(0f);
            m_graphView.style.borderRightWidth = new StyleFloat(0f);
            m_graphView.style.borderBottomWidth = new StyleFloat(0f);
            m_graphView.style.backgroundColor = new StyleColor(new Color32(36, 36, 36, 255));
            m_graphView.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.Flex);
            m_bodyContainer.Add(m_graphView);

            m_splitExecButton = new UtilityButton(OnSplitExecButtonClicked);
            m_splitExecButton.image = Resources.Load<Texture2D>("Vista/Textures/SplitExecution");
            m_splitExecButton.tooltip = "Toggling Split Execution mode for this graph, you also need to setup children nodes to decide which nodes to split the execution";
            VisualElement graphViewUtilityButtons = m_graphView.Q<VisualElement>("utility-button-container");
            if (graphViewUtilityButtons != null)
            {
                graphViewUtilityButtons.Add(m_splitExecButton);

                graphViewUtilityButtons.RemoveFromHierarchy();
                m_toolbar.leftContainer.Add(new VerticalSeparator());
                m_toolbar.leftContainer.Add(graphViewUtilityButtons);
                graphViewUtilityButtons.AddToClassList("v2");

                graphViewUtilityButtons.Query<UtilityButton>().ForEach(b => b.AddToClassList("v2"));
            }

            m_viewport2dButton.RemoveFromHierarchy();
            m_viewport2dButton = new ToolbarButton() { text = "2D" };
            m_viewport2dButton.AddToClassList("active");
            m_viewport2dButton.clicked += On2DButtonClicked;
            m_toolbar.rightContainer.Add(m_viewport2dButton);

            m_viewport3dButton.RemoveFromHierarchy();
            m_viewport3dButton = new ToolbarButton() { text = "3D" };
            m_viewport3dButton.AddToClassList("active");
            m_viewport3dButton.clicked += On3DButtonClicked;
            m_toolbar.rightContainer.Add(m_viewport3dButton);

            GraphAsset targetGraph = this.sourceGraph != null ? this.sourceGraph : currentlyOpeningGraph;

            m_viewport2DSubWindow = new Viewport2DSubWindow(this, targetGraph?.name) { name = "2D-viewport-sub-window" };
            m_viewport2DSubWindow.OnEnable();
            m_viewport2DSubWindow.SetTitle("2D Viewport");
            m_viewport2d.RemoveFromHierarchy();
            m_viewport2DSubWindow.bodyContainer.Add(m_viewport2d);
            m_viewport2d.StretchToParentSize();
            m_viewport2d.Q<Resizer>()?.RemoveFromHierarchy();
            m_viewport2d.style.height = new StyleLength(StyleKeyword.Auto);
            m_viewport2d.style.width = new StyleLength(StyleKeyword.Auto);
            m_viewport2d.style.marginLeft = new StyleLength(0f);
            m_viewport2d.style.marginTop = new StyleLength(0f);
            m_viewport2d.style.marginRight = new StyleLength(0f);
            m_viewport2d.style.marginBottom = new StyleLength(0f);
            m_viewport2d.style.borderLeftWidth = new StyleFloat(0f);
            m_viewport2d.style.borderTopWidth = new StyleFloat(0f);
            m_viewport2d.style.borderRightWidth = new StyleFloat(0f);
            m_viewport2d.style.borderBottomWidth = new StyleFloat(0f);
            m_viewport2d.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.Flex);

            VisualElement viewport2DUtilityButtons = m_viewport2d.Q<VisualElement>("utility-button-container");
            if (viewport2DUtilityButtons != null)
            {
                viewport2DUtilityButtons.RemoveFromHierarchy();
                m_viewport2DSubWindow.topContainer.Add(viewport2DUtilityButtons);
                viewport2DUtilityButtons.AddToClassList("v2");

                viewport2DUtilityButtons.Query<UtilityButton>().ForEach(b => b.AddToClassList("v2"));
            }
            m_bodyContainer.Add(m_viewport2DSubWindow);
            if (SubWindowManager.IsVisibleBySettings(m_viewport2DSubWindow.viewDataKey))
            {
                SubWindowManager.Show(m_viewport2DSubWindow);
            }
            else
            {
                SubWindowManager.Hide(m_viewport2DSubWindow);
            }

            m_viewport3DSubWindow = new Viewport3DSubWindow(this, targetGraph?.name) { name = "3D-viewport-sub-window" };
            m_viewport3DSubWindow.OnEnable();
            m_viewport3DSubWindow.SetTitle("3D Viewport");
            m_viewport3d.RemoveFromHierarchy();
            m_viewport3DSubWindow.bodyContainer.Add(m_viewport3d);
            m_viewport3d.StretchToParentSize();
            m_viewport3d.Q<Resizer>()?.RemoveFromHierarchy();
            m_viewport3d.style.height = new StyleLength(StyleKeyword.Auto);
            m_viewport3d.style.width = new StyleLength(StyleKeyword.Auto);
            m_viewport3d.style.marginLeft = new StyleLength(0f);
            m_viewport3d.style.marginTop = new StyleLength(0f);
            m_viewport3d.style.marginRight = new StyleLength(0f);
            m_viewport3d.style.marginBottom = new StyleLength(0f);
            m_viewport3d.style.borderLeftWidth = new StyleFloat(0f);
            m_viewport3d.style.borderTopWidth = new StyleFloat(0f);
            m_viewport3d.style.borderRightWidth = new StyleFloat(0f);
            m_viewport3d.style.borderBottomWidth = new StyleFloat(0f);
            m_viewport3d.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.Flex);

            VisualElement viewport3DUtilityButtons = m_viewport3d.Q<VisualElement>("utility-button-container");
            if (viewport3DUtilityButtons != null)
            {
                viewport3DUtilityButtons.RemoveFromHierarchy();
                m_viewport3DSubWindow.topContainer.Add(viewport3DUtilityButtons);
                viewport3DUtilityButtons.AddToClassList("v2");

                viewport3DUtilityButtons.Query<UtilityButton>().ForEach(b => b.AddToClassList("v2"));
            }
            m_bodyContainer.Add(m_viewport3DSubWindow);
            if (SubWindowManager.IsVisibleBySettings(m_viewport3DSubWindow.viewDataKey))
            {
                SubWindowManager.Show(m_viewport3DSubWindow);
            }
            else
            {
                SubWindowManager.Hide(m_viewport3DSubWindow);
            }

            m_rightPanelButton.RemoveFromHierarchy();
            m_nodePropertiesSubWindow = new NodePropertiesSubWindow(this, targetGraph?.name) { name = "node-properties-sub-window" };
            m_nodePropertiesSubWindow.OnEnable();
            m_nodePropertiesSubWindow.SetImguiCallback(OnDrawNodeProperties);
            m_bodyContainer.Add(m_nodePropertiesSubWindow);
            if (SubWindowManager.IsVisibleBySettings(m_nodePropertiesSubWindow.viewDataKey))
            {
                SubWindowManager.Show(m_nodePropertiesSubWindow);
            }
            else
            {
                SubWindowManager.Hide(m_nodePropertiesSubWindow);
            }

            m_editorSettingsSubWindow = new EditorSettingsSubWindow(this, targetGraph?.name) { name = "graph-properties-sub-window" };
            m_editorSettingsSubWindow.OnEnable();
            m_editorSettingsSubWindow.SetImguiCallback(OnDrawEditorSettings);
            m_editorSettingsSubWindow.SetTitle("Editor settings");
            m_bodyContainer.Add(m_editorSettingsSubWindow);
            if (SubWindowManager.IsVisibleBySettings(m_editorSettingsSubWindow.viewDataKey))
            {
                SubWindowManager.Show(m_editorSettingsSubWindow);
            }
            else
            {
                SubWindowManager.Hide(m_editorSettingsSubWindow);
            }

            m_notificationSubWindow = new NotificationSubWindow(this, targetGraph?.name) { name = "graph-properties-notifications" };
            m_notificationSubWindow.OnEnable();
            m_notificationSubWindow.SetImguiCallback(OnDrawNotifications);
            m_notificationSubWindow.SetTitle("Notifications");
            m_bodyContainer.Add(m_notificationSubWindow);
            if (SubWindowManager.IsVisibleBySettings(m_notificationSubWindow.viewDataKey))
            {
                SubWindowManager.Show(m_notificationSubWindow);
            }
            else
            {
                SubWindowManager.Hide(m_notificationSubWindow);
            }

            m_editorSettingsButton = new UtilityButton();
            m_editorSettingsButton.clicked += OnEditorSettingsButtonClicked;
            m_editorSettingsButton.image = Resources.Load<Texture2D>("Vista/Textures/Settings");
            m_editorSettingsButton.tooltip = "Edit settings for this graph editor.";
            m_toolbar.rightContainer.Insert(0, m_editorSettingsButton);

            m_toolbar.rightContainer.Insert(0, new VerticalSeparator());
            m_emailButton = new UtilityButton();
            m_emailButton.image = Resources.Load<Texture2D>("Vista/Textures/EmailIcon");
            m_emailButton.tooltip = "Get support via email or chat.";
            m_emailButton.clicked += () => { Application.OpenURL(Links.CONTACT_PAGE); };
            m_toolbar.rightContainer.Insert(0, m_emailButton);

            m_documentationButton = new UtilityButton();
            m_documentationButton.image = Resources.Load<Texture2D>("Vista/Textures/DocumentationIcon");
            m_documentationButton.tooltip = "Open online documentation.";
            m_documentationButton.clicked += () => { Application.OpenURL(Links.DOC); };
            m_toolbar.rightContainer.Insert(0, m_documentationButton);

            m_notificationButton = new UtilityButton();
            m_notificationButton.image = Resources.Load<Texture2D>("Vista/Textures/NotificationIcon");
            m_notificationButton.tooltip = "";
            m_notificationButton.clicked += OnNotificationButtonClicked;
            m_toolbar.rightContainer.Insert(0, m_notificationButton);

            m_toolbar.rightContainer.AddToClassList("utility-button-container");
            m_toolbar.rightContainer.AddToClassList("v2");
            m_toolbar.Query<ToolbarButton>().ForEach(b =>
            {
                b.AddToClassList("v2");
            });
            m_toolbar.Query<UtilityButton>().ForEach(b =>
            {
                b.AddToClassList("v2");
            });

            m_leftContainer.RemoveFromHierarchy();
            m_rightContainer.RemoveFromHierarchy();

            SetupRibbon();
        }

        private void SetupRibbon()
        {
            if (!Ribbon.CanShowToday())
                return; 
            List<NewsEntry> news = EditorSettings.Get().marketingSettings.GetSpecialNews();
            if (news == null || news.Count == 0)
                return;
            string url = news[0].link;
            m_ribbon = new Ribbon() { name = "ribbon" };
            m_bodyContainer.Add(m_ribbon);
            m_ribbon.text = news[0].title;
            m_ribbon.tooltip = url;
            m_ribbon.RegisterCallback<MouseUpEvent>((mue) => { NetUtils.TrackClick("ribbon", UILocation.GraphEditor_Ribbon); Application.OpenURL(url); });

            m_ribbon.Show();
        }

        protected override void OnTearDownGUI()
        {
            base.OnTearDownGUI();
            m_viewport2DSubWindow.OnDisable();
            m_viewport3DSubWindow.OnDisable();
            m_nodePropertiesSubWindow.OnDisable();
            m_editorSettingsSubWindow.OnDisable();
            m_notificationSubWindow.OnDisable();
        }

        protected override void OnViewportKeyDown(KeyDownEvent evt)
        {
            if (!evt.ctrlKey && !evt.altKey && !evt.shiftKey)
            {
                if (evt.keyCode == KeyCode.Alpha1)
                {
                    SubWindowManager.ToggleViewVisibility(m_nodePropertiesSubWindow);
                    evt.StopPropagation();
                    return;
                }
                else if (evt.keyCode == KeyCode.Alpha4)
                {
                    OnEditorSettingsButtonClicked();
                    evt.StopPropagation();
                    return;
                }
            }
            base.OnViewportKeyDown(evt);
        }

        protected override void Toggle2DViewport()
        {
            SubWindowManager.ToggleViewVisibility(m_viewport2DSubWindow);
        }

        protected override void Toggle3DViewport()
        {
            SubWindowManager.ToggleViewVisibility(m_viewport3DSubWindow);
        }

        private void On2DButtonClicked()
        {
            Toggle2DViewport();
        }

        private void On3DButtonClicked()
        {
            Toggle3DViewport();
        }

        private void OnEditorSettingsButtonClicked()
        {
            SubWindowManager.ToggleViewVisibility(m_editorSettingsSubWindow);
            if (SubWindowManager.IsVisibleBySettings(m_editorSettingsSubWindow.viewDataKey))
            {
                Vector2 windowParentSize = m_editorSettingsSubWindow.parent.layout.size;
                Vector2 windowSize = new Vector2(windowParentSize.y * 0.5f, windowParentSize.y);
                float left = windowParentSize.x - windowSize.x;
                float top = 0;
                m_editorSettingsSubWindow.SetPosition(new Vector2(left, top));
                m_editorSettingsSubWindow.SetSize(windowSize);
            }
        }

        protected override void OnActiveNodeChanged(NodeView nv)
        {
            base.OnActiveNodeChanged(nv);
            CoroutineUtility.StartCoroutine(IOnActiveNodeChangedDelayed(nv));
        }

        protected IEnumerator IOnActiveNodeChangedDelayed(NodeView nv)
        {
            yield return null;
            yield return null;
            if (nv == null)
            {
                SubWindowManager.Hide(m_nodePropertiesSubWindow);
            }
            else
            {
                Vector2 PROPERTIES_WINDOW_SIZE = new Vector2(360, 300);

                SubWindowManager.Hide(m_nodePropertiesSubWindow);
                SubWindowManager.Show(m_nodePropertiesSubWindow);
                Rect nodeRect = nv.contentRect;
                Rect newWindowRect = nv.ChangeCoordinatesTo(m_nodePropertiesSubWindow.parent, nodeRect);
                newWindowRect.position += new Vector2(0, newWindowRect.height + 8);
                newWindowRect.size = PROPERTIES_WINDOW_SIZE;
                //m_nodePropertiesSubWindow.SetPosition(newWindowRect.position);
                //m_nodePropertiesSubWindow.SetSize(newWindowRect.size);
            }
        }

        internal override void OnDrawNodeProperties()
        {
            if (m_clonedGraph == null)
                return;
            INode activeNode = clonedGraph.GetNode(m_activeNodeId);
            if (activeNode != null)
            {
                Type nodeType = activeNode.GetType();
                string displayName = ObjectNames.NicifyVariableName(nodeType.Name);
                NodeMetadataAttribute meta = NodeMetadata.Get(nodeType);
                if (meta != null && !string.IsNullOrEmpty(meta.title))
                {
                    displayName = meta.title;
                }
                m_nodePropertiesSubWindow.SetTitle(displayName);
                NodeEditor nodeEditor = NodeEditor.Get(nodeType);
                if (nodeEditor == null)
                {
                    nodeEditor = new GenericNodeEditor();
                }

                nodeEditor.m_graphEditor = this;
                EditorGUI.BeginChangeCheck();
                const string BASE_PROPERTIES_FOLDOUT_KEY = "vista-graph-editor-base-properties";
                bool basePropertiesExpanded = SessionState.GetBool(BASE_PROPERTIES_FOLDOUT_KEY, false);
                basePropertiesExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(basePropertiesExpanded, "Base Properties");
                if (basePropertiesExpanded)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
                    nodeEditor.OnBaseGUI(activeNode);
                    EditorGUILayout.Space();
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
                SessionState.SetBool(BASE_PROPERTIES_FOLDOUT_KEY, basePropertiesExpanded);

                if (nodeEditor.hasSpecificProperties)
                {
                    const string SPECIFIC_PROPERTIES_FOLDOUT_KEY = "vista-graph-editor-specific-properties";
                    bool specificPropertiesExpanded = SessionState.GetBool(SPECIFIC_PROPERTIES_FOLDOUT_KEY, true);
                    specificPropertiesExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(specificPropertiesExpanded, "Specific Properties");
                    if (specificPropertiesExpanded)
                    {
                        EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
                        nodeEditor.OnGUI(activeNode);
                        EditorGUILayout.Space();
                        EditorGUILayout.EndVertical();
                    }
                    EditorGUILayout.EndFoldoutHeaderGroup();
                    SessionState.SetBool(SPECIFIC_PROPERTIES_FOLDOUT_KEY, specificPropertiesExpanded);
                }
                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(clonedGraph);
                    ExecuteGraph();
                    UpdateNodesVisual();
                }

                EditorGUI.BeginChangeCheck();
                nodeEditor.OnExposedPropertiesGUI(activeNode);
                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(clonedGraph);
                    UpdateNodesVisual();
                }
            }
            else
            {
                m_nodePropertiesSubWindow.SetTitle("Properties");
            }
        }

        internal override void OnDrawEditorSettings()
        {
            if (m_clonedGraph == null)
                return;
            int lastDirtyCount = EditorUtility.GetDirtyCount(m_clonedGraph);
            m_adapter.propertiesDrawer.OnDrawProperties(m_clonedGraph);
            if (EditorUtility.GetDirtyCount(m_clonedGraph) > lastDirtyCount)
            {
                UpdateNodesVisual();
                if (m_clonedGraph.HasNode(m_activeNodeId))
                {
                    ExecuteGraph();
                }
            }
        }

        protected override bool IsViewport2DVisible()
        {
            return SubWindowManager.IsVisibleBySettings(m_viewport2DSubWindow.viewDataKey);
        }

        protected override bool IsViewport3DVisible()
        {
            return SubWindowManager.IsVisibleBySettings(m_viewport3DSubWindow.viewDataKey);
        }

        protected override void UpdateToolbarButtonsStatus()
        {
            base.UpdateToolbarButtonsStatus();
            m_splitExecButton.SetToggled((clonedGraph as TerrainGraph).allowSplitExecution);
            m_editorSettingsButton.SetToggled(SubWindowManager.IsVisibleBySettings(m_editorSettingsSubWindow.viewDataKey));
            m_notificationButton.SetToggled(SubWindowManager.IsVisibleBySettings(m_notificationSubWindow.viewDataKey));
        }

        private void OnSplitExecButtonClicked()
        {
            TerrainGraph tg = clonedGraph as TerrainGraph;
            tg.allowSplitExecution = !tg.allowSplitExecution;
        }

        private void OnNotificationButtonClicked()
        {
            SubWindowManager.ToggleViewVisibility(m_notificationSubWindow);
            if (SubWindowManager.IsVisibleBySettings(m_notificationSubWindow.viewDataKey))
            {
                Vector2 windowParentSize = m_notificationSubWindow.parent.layout.size;
                Vector2 windowSize = new Vector2(windowParentSize.y * 0.5f, windowParentSize.y);
                float left = windowParentSize.x - windowSize.x;
                float top = 0;
                m_notificationSubWindow.SetPosition(new Vector2(left, top));
                m_notificationSubWindow.SetSize(windowSize);
            }
        }

        private void OnDrawNotifications()
        {
            EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
            PropertiesView.OnExploreGUI();
            EditorGUILayout.EndVertical();
        }
    }
}
#endif
