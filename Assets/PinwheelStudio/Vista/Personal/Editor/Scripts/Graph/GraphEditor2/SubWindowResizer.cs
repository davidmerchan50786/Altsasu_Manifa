#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UIElements;
using System;

namespace Pinwheel.VistaEditor.Graph
{
    public class SubWindowResizer : VisualElement
    {
        public enum Position
        {
            Left, LeftTop, Top, TopRight, Right, RightBottom, Bottom, BottomLeft
        }

        public VisualElement window { get; private set; }
        public Position position { get; private set; }
        public Vector2 minSize { get; set; }
        public Vector2 maxSize { get; set; }

        private bool isDragging { get; set; }

        public Action<Rect> onGeometryChanged;

        public SubWindowResizer(VisualElement window, Position pos)
        {
            this.window = window;
            minSize = Vector2.zero;
            maxSize = Vector2.one * 10000;
            this.position = pos;

            this.RegisterCallback<MouseDownEvent>(OnMouseDown, TrickleDown.NoTrickleDown);
            this.RegisterCallback<MouseMoveEvent>(OnMouseMove, TrickleDown.NoTrickleDown);
            this.RegisterCallback<MouseUpEvent>(OnMouseUp, TrickleDown.NoTrickleDown);

            AddToClassList("resizer");
            if (position == Position.Left)
                AddToClassList("left");
            else if (position == Position.LeftTop)
                AddToClassList("left-top");
            else if (position == Position.Top)
                AddToClassList("top");
            else if (position == Position.TopRight)
                AddToClassList("top-right");
            else if (position == Position.Right)
                AddToClassList("right");
            else if (position == Position.RightBottom)
                AddToClassList("right-bottom");
            else if (position == Position.Bottom)
                AddToClassList("bottom");
            else if (position == Position.BottomLeft)
                AddToClassList("bottom-left");
        }

        private void OnMouseDown(MouseDownEvent e)
        {
            this.CaptureMouse();
            if (e.button == 0)
            {
                isDragging = true;
            }
            e.StopPropagation();
        }

        private void OnMouseMove(MouseMoveEvent e)
        {
            if (!isDragging)
                return;
            Vector2 delta = e.mouseDelta;
            ResizeAndClamp(delta);
            e.StopPropagation();
        }

        private void OnMouseUp(MouseUpEvent e)
        {
            if (this.HasMouseCapture())
            {
                this.ReleaseMouse();
            }
            isDragging = false;
            e.StopPropagation();
        }

        private void ResizeAndClamp(Vector2 delta)
        {
            if (window.resolvedStyle == null)
                return;
            if (window.resolvedStyle.position != UnityEngine.UIElements.Position.Absolute)
            {
                Debug.Log("Window position must be Absolute to be able to resized");
                return;
            }

            float x = window.resolvedStyle.left;
            float y = window.resolvedStyle.top;
            float width = window.resolvedStyle.width;
            float height = window.resolvedStyle.height;

            if (position == Position.Left)
            {
                x += delta.x;
                width -= delta.x;
            }
            else if (position == Position.LeftTop)
            {
                x += delta.x;
                width -= delta.x;
                y += delta.y;
                height -= delta.y;
            }
            else if (position == Position.Top)
            {
                y += delta.y;
                height -= delta.y;
            }
            else if (position == Position.TopRight)
            {
                y += delta.y;
                height -= delta.y;

                width += delta.x;
            }
            else if (position == Position.Right)
            {
                width += delta.x;
            }
            else if (position == Position.RightBottom)
            {
                width += delta.x;
                height += delta.y;
            }
            else if (position == Position.Bottom)
            {
                height += delta.y;
            }
            else if (position == Position.BottomLeft)
            {
                x += delta.x;
                width -= delta.x;
                height += delta.y;
            }

            width = Mathf.Clamp(width, minSize.x, maxSize.x);
            height = Mathf.Clamp(height, minSize.y, maxSize.y);

            window.style.left = new StyleLength(x);
            window.style.top = new StyleLength(y);
            window.style.width = new StyleLength(width);
            window.style.height = new StyleLength(height);

            if (onGeometryChanged != null)
            {
                onGeometryChanged.Invoke(new Rect(x, y, width, height));
            }
        }
    }
}
#endif