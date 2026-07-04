#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UIElements;
using System;
using Pinwheel.Vista;

namespace Pinwheel.VistaEditor.Graph
{
    public class SubWindowDragZone : MouseManipulator
    {
        public VisualElement window { get; set; }
        public VisualElement windowParent { get; set; }

        private bool isDragging { get; set; }

        public Action<Rect> onGeometryChanged;

        private const float SNAP_THRESHOLD = 100f;
        private VisualElement m_snapHighlighter;

        private const float SNAP_TRANSITION_TIME = 0.25f;
        private static readonly StyleList<TimeValue> windowTransitionZero = new StyleList<TimeValue>(new List<TimeValue>() { new TimeValue(0f) });
        private static readonly StyleList<TimeValue> windowTransitionStyle = new StyleList<TimeValue>(new List<TimeValue>() { new TimeValue(SNAP_TRANSITION_TIME) });

        public SubWindowDragZone(VisualElement window, VisualElement windowParent)
        {
            this.window = window;
            this.windowParent = windowParent;

            m_snapHighlighter = new VisualElement();
            m_snapHighlighter.style.position = new StyleEnum<Position>(Position.Absolute);
            m_snapHighlighter.style.backgroundColor = new StyleColor(new Color(1, 1, 1, 0.25f));
            m_snapHighlighter.style.borderLeftColor = new StyleColor(new Color(1, 1, 1, 0.5f));
            m_snapHighlighter.style.borderTopColor = new StyleColor(new Color(1, 1, 1, 0.5f));
            m_snapHighlighter.style.borderRightColor = new StyleColor(new Color(1, 1, 1, 0.5f));
            m_snapHighlighter.style.borderBottomColor = new StyleColor(new Color(1, 1, 1, 0.5f));
            m_snapHighlighter.style.borderLeftWidth = new StyleFloat(1f);
            m_snapHighlighter.style.borderTopWidth = new StyleFloat(1f);
            m_snapHighlighter.style.borderRightWidth = new StyleFloat(1f);
            m_snapHighlighter.style.borderBottomWidth = new StyleFloat(1f);
            m_snapHighlighter.style.borderTopLeftRadius = new StyleLength(8f);
            m_snapHighlighter.style.borderTopRightRadius = new StyleLength(8f);
            m_snapHighlighter.style.borderBottomRightRadius = new StyleLength(8f);
            m_snapHighlighter.style.borderBottomLeftRadius = new StyleLength(8f);
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<MouseDownEvent>(OnMouseDown, TrickleDown.NoTrickleDown);
            target.RegisterCallback<MouseMoveEvent>(OnMouseMove, TrickleDown.NoTrickleDown);
            target.RegisterCallback<MouseUpEvent>(OnMouseUp, TrickleDown.NoTrickleDown);
            windowParent.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged, TrickleDown.NoTrickleDown);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<MouseDownEvent>(OnMouseDown, TrickleDown.NoTrickleDown);
            target.UnregisterCallback<MouseMoveEvent>(OnMouseMove, TrickleDown.NoTrickleDown);
            target.UnregisterCallback<MouseUpEvent>(OnMouseUp, TrickleDown.NoTrickleDown);
            windowParent.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged, TrickleDown.NoTrickleDown);
        }

        private void OnMouseDown(MouseDownEvent e)
        {
            target.CaptureMouse();
            if (e.button == 0)
            {
                isDragging = true;
                window.BringToFront();
            }
            e.StopPropagation();
        }

        private void OnMouseMove(MouseMoveEvent e)
        {
            if (!isDragging)
                return;
            Vector2 delta = e.mouseDelta;
            MoveAndClampPosition(delta);
            HandleSnapping(e);
            window.BringToFront();
        }

        private void OnMouseUp(MouseUpEvent e)
        {
            if (target.HasMouseCapture())
            {
                target.ReleaseMouse();
            }
            if (isDragging)
            {
                isDragging = false;
                HandleSnapping(e);
                e.StopPropagation();
            }
        }

        internal void MoveAndClampPosition(Vector2 delta)
        {
            if (window.resolvedStyle == null)
                return;
            if (window.resolvedStyle.position != Position.Absolute)
            {
                Debug.LogWarning("Window position must be Absolute to be dragged");
                return;
            }

            float left = window.resolvedStyle.left + delta.x;
            float top = window.resolvedStyle.top + delta.y;
            float right = float.NaN;
            float bottom = float.NaN;

            float minLeft = 0;
            float maxLeft = window.parent.layout.width - window.layout.width;
            float minTop = 0;
            float maxTop = window.parent.layout.height - window.layout.height;

            left = Mathf.Clamp(left, minLeft, maxLeft);
            top = Mathf.Clamp(top, minTop, maxTop);

            window.style.left = new StyleLength(left);
            window.style.top = new StyleLength(top);
            window.style.right = new StyleLength(right);
            window.style.bottom = new StyleLength(bottom);

            if (onGeometryChanged != null)
            {
                float width = window.resolvedStyle.width;
                float height = window.resolvedStyle.height;
                onGeometryChanged.Invoke(new Rect(left,top, width, height));
            }
        }

        private void HandleSnapping(IMouseEvent e)
        {
            if (window.resolvedStyle == null)
                return;
            if (window.resolvedStyle.position != Position.Absolute)
            {
                Debug.LogWarning("Window position must be Absolute to be snapped");
                return;
            }

            Vector2 mousePos = this.target.ChangeCoordinatesTo(window.parent, e.localMousePosition);
            Vector2 windowParentSize = window.parent.layout.size;
            Rect windowRect = window.layout;
            float width = windowRect.width;
            float height = windowRect.height;

            float left = mousePos.x;
            float top = mousePos.y;
            float right = windowParentSize.x - mousePos.x;
            float bottom = windowParentSize.y - mousePos.y;

            bool willSnap = false;
            if (e.shiftKey)
            {
                willSnap = false;
            }
            else if (left <= SNAP_THRESHOLD &&
                top <= SNAP_THRESHOLD)
            {
                width = windowParentSize.y * 0.5f;
                height = windowParentSize.y * 0.5f;
                left = 0;
                top = 0;
                willSnap = true;
            }
            else if (left <= SNAP_THRESHOLD &&
                bottom <= SNAP_THRESHOLD)
            {
                width = windowParentSize.y * 0.5f;
                height = windowParentSize.y * 0.5f;
                left = 0;
                top = windowParentSize.y * 0.5f + 1;
                willSnap = true;
            }
            else if (right <= SNAP_THRESHOLD &&
                top <= SNAP_THRESHOLD)
            {
                width = windowParentSize.y * 0.5f;
                height = windowParentSize.y * 0.5f;
                left = windowParentSize.x - width;
                top = 0;
                willSnap = true;
            }
            else if (right <= SNAP_THRESHOLD &&
               bottom <= SNAP_THRESHOLD)
            {
                width = windowParentSize.y * 0.5f;
                height = windowParentSize.y * 0.5f;
                left = windowParentSize.x - width;
                top = windowParentSize.y * 0.5f + 1;
                willSnap = true;
            }
            else if (Vector2.Distance(mousePos, new Vector2(0, windowParentSize.y * 0.5f)) < SNAP_THRESHOLD)
            {
                width = windowParentSize.y * 0.5f;
                height = windowParentSize.y;
                left = 0;
                top = 0;
                willSnap = true;
            }
            else if (Vector2.Distance(mousePos, new Vector2(windowParentSize.x * 0.5f, 0)) < SNAP_THRESHOLD)
            {
                width = windowParentSize.x;
                height = windowParentSize.y * 0.3f;
                left = 0;
                top = 0;
                willSnap = true;
            }
            else if (Vector2.Distance(mousePos, new Vector2(windowParentSize.x, windowParentSize.y * 0.5f)) < SNAP_THRESHOLD)
            {
                width = windowParentSize.y * 0.5f;
                height = windowParentSize.y;
                left = windowParentSize.x - width;
                top = 0;
                willSnap = true;
            }
            else if (Vector2.Distance(mousePos, new Vector2(windowParentSize.x * 0.5f, windowParentSize.y)) < SNAP_THRESHOLD)
            {
                width = windowParentSize.x;
                height = windowParentSize.y * 0.3f;
                left = 0;
                top = windowParentSize.y - height;
                willSnap = true;
            }

            if (!willSnap)
            {
                m_snapHighlighter.RemoveFromHierarchy();
            }
            if (willSnap && e is MouseMoveEvent mme)
            {
                window.parent.Add(m_snapHighlighter);
                m_snapHighlighter.BringToFront();

                m_snapHighlighter.style.left = left;
                m_snapHighlighter.style.top = top;
                m_snapHighlighter.style.width = width;
                m_snapHighlighter.style.height = height;
            }
            else if (willSnap && e is MouseUpEvent mue)
            {
                m_snapHighlighter.RemoveFromHierarchy();

                window.style.transitionDuration = windowTransitionStyle;
                CoroutineUtility.StartCoroutine(IResetWindowTransitionTime());

                window.style.left = left;
                window.style.top = top;
                window.style.width = width;
                window.style.height = height;

                if (onGeometryChanged != null)
                {
                    onGeometryChanged.Invoke(new Rect(left, top, width, height));
                }
            }
        }

        private IEnumerator IResetWindowTransitionTime()
        {
            yield return new WaitForSecondsRealtime(SNAP_TRANSITION_TIME);
            window.style.transitionDuration = windowTransitionZero;
        }

        private void OnGeometryChanged(GeometryChangedEvent e)
        {
            MoveAndClampPosition(Vector2.zero);
        }
    }
}
#endif
