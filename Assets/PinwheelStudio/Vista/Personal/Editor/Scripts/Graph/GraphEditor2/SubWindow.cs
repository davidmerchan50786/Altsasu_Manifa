#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEditor;
using System;

namespace Pinwheel.VistaEditor.Graph
{
    public class SubWindow : VisualElement
    {
        public GraphEditorBase graphEditor { get; }

        public VisualElement mainContainer;
        public VisualElement topContainer;
        public VisualElement bodyContainer;
        public VisualElement resizerContainer;
        public Button closeButton;
        public VisualElement border;

        protected SubWindowDragZone dragZone;
        protected SubWindowResizer[] resizers;

        protected TextElement titleLabel;

        public SubWindow(GraphEditorBase parentEditor, string viewKey)
        {
            graphEditor = parentEditor;
            viewDataKey = viewKey;
            AddToClassList("subWindow");
            styleSheets.Add(Resources.Load<StyleSheet>("Vista/USS/Graph/SubWindow"));

            resizerContainer = new VisualElement() { name = "resizerContainer" };
            this.Add(resizerContainer);

            mainContainer = new VisualElement() { name = "mainContainer" };
            this.Add(mainContainer);

            topContainer = new VisualElement() { name = "topContainer" };
            titleLabel = new TextElement() { name = "titleLabel" };
            topContainer.Add(titleLabel);
            mainContainer.Add(topContainer);

            closeButton = new Button() { name = "closeButton", text = "X" };
            closeButton.clicked += () => { SubWindowManager.Hide(this); };
            topContainer.Add(closeButton);

            bodyContainer = new VisualElement() { name = "bodyContainer" };
            mainContainer.Add(bodyContainer);

            border = new VisualElement() { name = "border", pickingMode = PickingMode.Ignore };
            this.Add(border);

            SetTitle("");

            RegisterCallback<MouseDownEvent>(OnMouseDown);
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            BringToFront();
        }

        public void SetTitle(string t)
        {
            TextElement titleLabel = topContainer.Q<TextElement>("titleLabel");
            titleLabel.text = t;

            if (string.IsNullOrEmpty(t))
            {
                titleLabel.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.None);
            }
            else
            {
                titleLabel.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.Flex);
            }
        }

        public virtual void SetPosition(Vector2 pos)
        {
            style.left = new StyleLength(pos.x);
            style.top = new StyleLength(pos.y);
            style.right = new StyleLength(float.NaN);
            style.bottom = new StyleLength(float.NaN);
        }

        public virtual void SetSize(Vector2 size)
        {
            style.width = new StyleLength(size.x);
            style.height = new StyleLength(size.y);
        }

        public void EnableWindowDragging(Action<Rect> onGeometryChanged = null)
        {
            if (dragZone == null)
            {
                dragZone = new SubWindowDragZone(this, this.parent);
                dragZone.onGeometryChanged = onGeometryChanged;
                topContainer.AddManipulator(dragZone);
            }
        }

        public void DisableWindowDragging()
        {
            topContainer.RemoveManipulator(dragZone);
            dragZone = null;
        }

        public void EnableWindowResizing(Vector2 minSize, Vector2 maxSize, Action<Rect> onGeometryChanged = null)
        {
            if (resizers == null)
            {
                resizers = new SubWindowResizer[8];
                resizers[0] = new SubWindowResizer(this, SubWindowResizer.Position.Left)
                {
                    minSize = minSize,
                    maxSize = maxSize,
                    onGeometryChanged = onGeometryChanged
                };
                resizers[1] = new SubWindowResizer(this, SubWindowResizer.Position.LeftTop)
                {
                    minSize = minSize,
                    maxSize = maxSize,
                    onGeometryChanged = onGeometryChanged
                };
                resizers[2] = new SubWindowResizer(this, SubWindowResizer.Position.Top)
                {
                    minSize = minSize,
                    maxSize = maxSize,
                    onGeometryChanged = onGeometryChanged
                };
                resizers[3] = new SubWindowResizer(this, SubWindowResizer.Position.TopRight)
                {
                    minSize = minSize,
                    maxSize = maxSize,
                    onGeometryChanged = onGeometryChanged
                };
                resizers[4] = new SubWindowResizer(this, SubWindowResizer.Position.Right)
                {
                    minSize = minSize,
                    maxSize = maxSize,
                    onGeometryChanged = onGeometryChanged
                };
                resizers[5] = new SubWindowResizer(this, SubWindowResizer.Position.RightBottom)
                {
                    minSize = minSize,
                    maxSize = maxSize,
                    onGeometryChanged = onGeometryChanged
                };
                resizers[6] = new SubWindowResizer(this, SubWindowResizer.Position.Bottom)
                {
                    minSize = minSize,
                    maxSize = maxSize,
                    onGeometryChanged = onGeometryChanged
                };
                resizers[7] = new SubWindowResizer(this, SubWindowResizer.Position.BottomLeft)
                {
                    minSize = minSize,
                    maxSize = maxSize,
                    onGeometryChanged = onGeometryChanged
                };

                this.Add(resizers[0]);
                this.Add(resizers[1]);
                this.Add(resizers[2]);
                this.Add(resizers[3]);
                this.Add(resizers[4]);
                this.Add(resizers[5]);
                this.Add(resizers[6]);
                this.Add(resizers[7]);
            }
        }

        public void DisableWindowResizing()
        {
            if (resizers != null)
            {
                foreach (SubWindowResizer r in resizers)
                {
                    r.RemoveFromHierarchy();
                }
                resizers = null;
            }
        }

        public virtual void Show()
        {
            style.display = new StyleEnum<DisplayStyle>(DisplayStyle.Flex);
        }

        public virtual void Hide()
        {
            if (graphEditor != null)
            {
                GraphEditorBase editor = graphEditor;
                EditorApplication.delayCall += () =>
                {
                    if (editor != null)
                    {
                        editor.FocusMainContent();
                    }
                };
            }
            style.display = new StyleEnum<DisplayStyle>(DisplayStyle.None);
        }

        public virtual void OnEnable()
        {

        }

        public virtual void OnDisable()
        {

        }

        public virtual void OnDestroy()
        {

        }

        public virtual void Dispose()
        {
        }
    }
}
#endif
