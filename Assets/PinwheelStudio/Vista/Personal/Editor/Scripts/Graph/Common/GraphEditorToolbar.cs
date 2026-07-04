#if VISTA
using Pinwheel.VistaEditor.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Pinwheel.VistaEditor.Graph
{
    public class GraphEditorToolbar : Toolbar
    {
        public delegate void LeftImguiHandler(GraphEditorToolbar sender);
        public static event LeftImguiHandler leftImguiCallback;

        private GraphEditorBase m_editor;
        public VisualElement leftContainer { get; set; }
        public VisualElement rightContainer { get; set; }

        private ToolbarButton m_autoSaveButton;
        private ToolbarButton m_saveButton;
        private ToolbarButton m_saveAsButton;
        private ToolbarButton m_showInProjectButton;
        private IMGUIContainer m_leftImgui;

        private static readonly Texture2D autosaveIcon = Resources.Load<Texture2D>("Vista/Textures/AutoSave");
        private static readonly Texture2D autosaveOnIcon = Resources.Load<Texture2D>("Vista/Textures/AutoSaveOn");

        private int m_autosaveCounter = 0;

        public GraphEditorToolbar(GraphEditorBase editor)
        {
            this.m_editor = editor;
            StyleSheet uss = Resources.Load<StyleSheet>("Vista/USS/Graph/Toolbar");
            styleSheets.Add(uss);

            leftContainer = new VisualElement() { name = "left-container" };
            rightContainer = new VisualElement() { name = "right-container" };
            this.Add(leftContainer);
            this.Add(rightContainer);
            
            m_autoSaveButton = new ToolbarButton() { text = "", tooltip = "Toggle auto save" };
            m_autoSaveButton.clicked += OnAutoSaveButtonClicked;
            leftContainer.Add(m_autoSaveButton);

            Image autosaveImg = new Image() { image = autosaveIcon };
            m_autoSaveButton.Add(autosaveImg);

            m_autoSaveButton.style.paddingLeft = new StyleLength(2f);
            m_autoSaveButton.style.paddingTop = new StyleLength(0f);
            m_autoSaveButton.style.paddingRight = new StyleLength(2f);
            m_autoSaveButton.style.paddingBottom = new StyleLength(0f);
            m_autoSaveButton.style.width = new StyleLength(20);

            m_saveButton = new ToolbarButton() { text = "Save Asset" };
            m_saveButton.clicked += OnSaveButtonClicked;
            leftContainer.Add(m_saveButton);

            m_saveAsButton = new ToolbarButton() { text = "Save As..." };
            m_saveAsButton.clicked += OnSaveAsButtonClicked;
            leftContainer.Add(m_saveAsButton);

            m_showInProjectButton = new ToolbarButton() { text = "Show In Project" };
            m_showInProjectButton.clicked += OnShowInProjectButtonClicked;
            leftContainer.Add(m_showInProjectButton);

            m_leftImgui = new IMGUIContainer(OnLeftIMGUI);
            leftContainer.Add(m_leftImgui);
        }

        private void OnAutoSaveButtonClicked()
        {
            EditorSettings s = EditorSettings.Get();
            s.graphEditorSettings.enableAutosave = !s.graphEditorSettings.enableAutosave;
        }

        private void OnSaveButtonClicked()
        {
            m_editor.OnSaveRequest();
        }

        private void OnSaveAsButtonClicked()
        {
            m_editor.OnSaveAsRequest();
        }

        private void OnShowInProjectButtonClicked()
        {
            EditorGUIUtility.PingObject(m_editor.sourceGraph);
        }

        internal void OnGUI()
        {
            EditorSettings s = EditorSettings.Get();
            Image img = m_autoSaveButton.Q<Image>();
            if (img != null)
            {
                if (s.graphEditorSettings.enableAutosave)
                {
                    img.image = autosaveOnIcon;
                    //m_editor.wantsMouseMove = true;
                }
                else
                {
                    img.image = autosaveIcon;
                    //m_editor.wantsMouseMove = false;
                }
            }

            m_autosaveCounter += 1;
            if (m_autosaveCounter >= 100 &&
                s.graphEditorSettings.enableAutosave &&
                EditorUtility.IsDirty(m_editor.clonedGraph))
            {
                OnSaveButtonClicked();
                m_autosaveCounter = 0;
            }
        }

        private void OnLeftIMGUI()
        {
            leftImguiCallback?.Invoke(this);
        }
    }
}
#endif
