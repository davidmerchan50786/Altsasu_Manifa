#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;
using Pinwheel.VistaEditor.Graph;
using UnityEngine.UIElements;
using UnityEditor;

namespace Pinwheel.VistaEditor.Graph
{
    public class MovableSubWindow : SubWindow
    {
        [System.Serializable]
        public class ViewData
        {
            [SerializeField]
            public Vector2 position;
            [SerializeField]
            public Vector2 size;

            public static readonly Vector2 MIN_SIZE = new Vector2(250, 250);
            public static readonly Vector2 MAX_SIZE = new Vector2(3000, 3000);

            public void Save(string key)
            {
                Validate();
                string json = JsonUtility.ToJson(this);
                EditorPrefs.SetString(key, json);
            }

            public void Load(string key)
            {
                string json = EditorPrefs.GetString(key);
                JsonUtility.FromJsonOverwrite(json, this);
                Validate();
            }

            public void Validate()
            {
                size.x = Mathf.Clamp(size.x, MIN_SIZE.x, MAX_SIZE.x);
                size.y = Mathf.Clamp(size.y, MIN_SIZE.y, MAX_SIZE.y);
            }
        }

        private ViewData m_viewData = new ViewData();

        public MovableSubWindow(GraphEditorBase parentEditor, string viewKey) : base(parentEditor, viewKey)
        {
            viewDataKey = this.GetType().Name + viewKey;
            m_viewData.Load(viewDataKey);
        }

        public override void Show()
        {
            style.display = new StyleEnum<DisplayStyle>(DisplayStyle.Flex);
            m_viewData.Load(viewDataKey);
            SetPosition(m_viewData.position);
            SetSize(m_viewData.size);
            EnableWindowDragging(OnGeometryManipulated);
            EnableWindowResizing(Vector2.one * 100, Vector2.one * 1000, OnGeometryManipulated);
            BringToFront();
            MarkDirtyRepaint();
        }

        public override void Hide()
        {
            base.Hide();
            DisableWindowDragging();
            DisableWindowResizing();
            m_viewData.Save(viewDataKey);
        }

        public override void SetPosition(Vector2 pos)
        {
            base.SetPosition(pos);
            m_viewData.position = pos;
        }

        public override void SetSize(Vector2 size)
        {
            base.SetSize(size);
            m_viewData.size = size;
        }

        private void OnGeometryManipulated(Rect r)
        {
            m_viewData.position = r.position;
            m_viewData.size = r.size;
        }

        private void OnResize(Vector2 s)
        {
            m_viewData.size = s;
        }

        public override void OnEnable()
        {
            base.OnEnable();
            m_viewData.Load(viewDataKey);
        }

        public override void OnDisable()
        {
            base.OnDisable();
            m_viewData.Save(viewDataKey);
        }
    }

    public class Viewport3DSubWindow : MovableSubWindow
    {
        public Viewport3DSubWindow(GraphEditorBase parentEditor, string viewKey) : base(parentEditor, viewKey) { }
    }

    public class Viewport2DSubWindow : MovableSubWindow
    {
        public Viewport2DSubWindow(GraphEditorBase parentEditor, string viewKey) : base(parentEditor, viewKey) { }
    }

    public class IMGUISubWindow : MovableSubWindow
    {
        protected IMGUIContainer m_imguiContainer;

        public IMGUISubWindow(GraphEditorBase parentEditor, string viewKey) : base(parentEditor, viewKey)
        {
            ScrollView scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.name = "scroll-view";
            bodyContainer.Add(scrollView);

            m_imguiContainer = new IMGUIContainer();
            scrollView.Add(m_imguiContainer);
        }

        public void SetImguiCallback(System.Action imguiCallback)
        {
            m_imguiContainer.onGUIHandler = imguiCallback;
        }
    }

    public class NodePropertiesSubWindow : IMGUISubWindow
    {
        public NodePropertiesSubWindow(GraphEditorBase parentEditor, string viewKey) : base(parentEditor, viewKey) { }
    }

    public class EditorSettingsSubWindow : IMGUISubWindow
    {
        public EditorSettingsSubWindow(GraphEditorBase parentEditor, string viewKey) : base(parentEditor, viewKey) { }
    }

    public class NotificationSubWindow : IMGUISubWindow
    {
        public NotificationSubWindow(GraphEditorBase parentEditor, string viewKey) : base(parentEditor, viewKey) { }
    }
}
#endif
