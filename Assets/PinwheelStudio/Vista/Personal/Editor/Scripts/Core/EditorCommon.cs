#if VISTA
using Pinwheel.Vista.Graph;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.Compilation;
using Object = UnityEngine.Object;
using Pinwheel.Vista;

namespace Pinwheel.VistaEditor
{
    public static class EditorCommon
    {
        public static class Styles
        {
            private static GUIStyle m_h1;
            public static GUIStyle h1
            {
                get
                {
                    if (m_h1 == null)
                    {
                        m_h1 = new GUIStyle(EditorStyles.label);
                        m_h1.fontStyle = FontStyle.Bold;
                        m_h1.fontSize = 18;
                        m_h1.richText = true;
                        m_h1.margin = new RectOffset(3, 0, 8, 8);
                    }
                    return m_h1;
                }
            }

            private static GUIStyle m_h2;
            public static GUIStyle h2
            {
                get
                {
                    if (m_h2 == null)
                    {
                        m_h2 = new GUIStyle(EditorStyles.label);
                        m_h2.fontStyle = FontStyle.Bold;
                        m_h2.fontSize = 15;
                        m_h2.richText = true;
                        m_h2.margin = new RectOffset(3, 0, 4, 4);
                    }
                    return m_h2;
                }
            }

            private static GUIStyle m_h3;
            public static GUIStyle h3
            {
                get
                {
                    if (m_h3 == null)
                    {
                        m_h3 = new GUIStyle(EditorStyles.label);
                        m_h3.fontStyle = FontStyle.Bold;
                        m_h3.fontSize = 12;
                        m_h3.richText = true;
                        m_h3.margin = new RectOffset(3, 0, 0, 4);
                    }
                    return m_h3;
                }
            }

            private static GUIStyle m_p1;
            public static GUIStyle p1
            {
                get
                {
                    if (m_p1 == null)
                    {
                        m_p1 = new GUIStyle(EditorStyles.label);
                        m_p1.wordWrap = true;
                        m_p1.richText = true;
                        m_p1.margin = new RectOffset(3, 0, 0, 4);
                    }
                    return m_p1;
                }
            }

            private static GUIStyle m_p2;
            public static GUIStyle p2
            {
                get
                {
                    if (m_p2 == null)
                    {
                        m_p2 = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
                        m_p2.alignment = TextAnchor.UpperLeft;
                        m_p2.wordWrap = true;
                        m_p2.richText = true;
                    }
                    return m_p2;
                }
            }

            private static GUIStyle s_infoLabel;
            public static GUIStyle infoLabel
            {
                get
                {
                    if (s_infoLabel == null)
                    {
                        s_infoLabel = new GUIStyle(EditorStyles.label);
                    }
                    s_infoLabel.fontStyle = FontStyle.Italic;
                    s_infoLabel.wordWrap = true;
                    return s_infoLabel;
                }
            }

            private static GUIStyle s_richTextLabel;
            public static GUIStyle richTextLabel
            {
                get
                {
                    if (s_richTextLabel == null)
                    {
                        s_richTextLabel = new GUIStyle(EditorStyles.label);
                    }
                    s_richTextLabel.wordWrap = true;
                    s_richTextLabel.richText = true;
                    return s_richTextLabel;
                }
            }

            private static GUIStyle s_italicLabel;
            public static GUIStyle italicLabel
            {
                get
                {
                    if (s_italicLabel == null)
                    {
                        s_italicLabel = new GUIStyle(EditorStyles.label);
                    }
                    s_italicLabel.fontStyle = FontStyle.Italic;
                    s_italicLabel.wordWrap = false;
                    return s_italicLabel;
                }
            }

            private static GUIStyle s_centeredLabel;
            public static GUIStyle centeredLabel
            {
                get
                {
                    if (s_centeredLabel == null)
                    {
                        s_centeredLabel = new GUIStyle(EditorStyles.label);
                    }
                    s_centeredLabel.alignment = TextAnchor.MiddleCenter;
                    s_centeredLabel.wordWrap = false;
                    return s_centeredLabel;
                }
            }

            private static GUIStyle s_centerGrayMiniLabel;
            public static GUIStyle centerGrayMiniLabel
            {
                get
                {
                    if (s_centerGrayMiniLabel == null)
                    {
                        s_centerGrayMiniLabel = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
                    }
                    return s_centerGrayMiniLabel;
                }
            }

            private static GUIStyle s_grayMiniLabel;
            public static GUIStyle grayMiniLabel
            {
                get
                {
                    if (s_grayMiniLabel == null)
                    {
                        s_grayMiniLabel = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
                    }
                    s_grayMiniLabel.alignment = TextAnchor.MiddleLeft;
                    return s_grayMiniLabel;
                }
            }

            private static GUIStyle s_iconButton;
            public static GUIStyle iconButton
            {
                get
                {
                    if (s_iconButton == null)
                    {
                        s_iconButton = new GUIStyle(EditorStyles.miniButton);
                        s_iconButton.normal.background = null;
                        s_iconButton.active.background = null;
                        s_iconButton.hover.background = null;
                        s_iconButton.padding = new RectOffset(0, 0, 0, 0);
                    }
                    return s_iconButton;
                }
            }

            private static GUIStyle s_foldoutBold;
            public static GUIStyle foldoutBold
            {
                get
                {
                    if (s_foldoutBold == null)
                    {
                        s_foldoutBold = new GUIStyle(EditorStyles.foldout);
                    }
                    s_foldoutBold.fontStyle = FontStyle.Bold;
                    return s_foldoutBold;
                }
            }

            private static GUIStyle s_confirmButton;
            public static GUIStyle confirmButton
            {
                get
                {
                    if (s_confirmButton == null)
                    {
                        s_confirmButton = new GUIStyle(EditorStyles.miniButton);
                        s_confirmButton.normal.textColor = Color.white;
                    }

                    return s_confirmButton;
                }
            }
        }

        public const float INDENT_WIDTH = 12;

        public static bool IsPersonalEdition()
        {
            return !ProjectInitializer.isVistaIndieInstalled && !ProjectInitializer.isVistaProInstalled;
        }

        public static bool IsIndieEdition()
        {
            return ProjectInitializer.isVistaIndieInstalled && !ProjectInitializer.isVistaProInstalled;
        }

        public static bool IsProEdition()
        {
            return ProjectInitializer.isVistaIndieInstalled && ProjectInitializer.isVistaProInstalled;
        }

        public static string GetEditionString()
        {
            if (ProjectInitializer.isVistaProInstalled)
            {
                return "Vista Pro";
            }
            else if (ProjectInitializer.isVistaIndieInstalled)
            {
                return "Vista Indie";
            }
            else
            {
                return "Vista Personal";
            }
        }

        public static string GetVersionString()
        {
            return VersionInfo.versionLabel;
        }

        public static Vector2 InlineVector2Field(string label, Vector2 value)
        {
            EditorGUIUtility.wideMode = true;
            value = EditorGUILayout.Vector2Field(label, value);
            EditorGUIUtility.wideMode = false;
            return value;
        }

        public static Vector2 InlineVector2Field(GUIContent label, Vector2 value)
        {
            EditorGUIUtility.wideMode = true;
            value = EditorGUILayout.Vector2Field(label, value);
            EditorGUIUtility.wideMode = false;
            return value;
        }

        public static Vector2Int InlineVector2IntField(GUIContent label, Vector2Int value)
        {
            EditorGUIUtility.wideMode = true;
            value = EditorGUILayout.Vector2IntField(label, value);
            EditorGUIUtility.wideMode = false;
            return value;
        }

        public static Vector3 InlineVector3Field(string label, Vector3 value)
        {
            EditorGUIUtility.wideMode = true;
            value = EditorGUILayout.Vector3Field(label, value);
            EditorGUIUtility.wideMode = false;
            return value;
        }

        public static Vector3 InlineVector3Field(GUIContent label, Vector3 value)
        {
            EditorGUIUtility.wideMode = true;
            value = EditorGUILayout.Vector3Field(label, value);
            EditorGUIUtility.wideMode = false;
            return value;
        }

        public static Vector4 InlineVector4Field(string label, Vector4 value)
        {
            EditorGUIUtility.wideMode = true;
            value = EditorGUILayout.Vector4Field(label, value);
            EditorGUIUtility.wideMode = false;
            return value;
        }

        public static Quaternion InlineEulerRotationField(string label, Quaternion rotation)
        {
            EditorGUIUtility.wideMode = true;
            rotation = Quaternion.Euler(EditorGUILayout.Vector3Field(label, rotation.eulerAngles));
            EditorGUIUtility.wideMode = false;
            return rotation;
        }

        public static Texture2D InlineTexture2DField(string label, Texture2D value, int indentScope = 0)
        {
            return InlineTexture2DField(new GUIContent(label), value, indentScope);
        }

        public static Texture2D InlineTexture2DField(GUIContent label, Texture2D value, int indentScope = 0)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            using (EditorGUI.IndentLevelScope level = new EditorGUI.IndentLevelScope(indentScope))
            {
                value = EditorGUILayout.ObjectField(value, typeof(Texture2D), false) as Texture2D;
            }
            EditorGUILayout.EndHorizontal();
            return value;
        }

        public static void MinMaxSlider(GUIContent label, ref float min, ref float max, float minLimit, float maxLimit)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            min = EditorGUILayout.FloatField(min, GUILayout.Width(50));
            EditorGUILayout.MinMaxSlider(ref min, ref max, minLimit, maxLimit, GUILayout.ExpandWidth(true));
            max = EditorGUILayout.FloatField(max, GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();
        }

        public static Vector2 Vector2Slider(GUIContent guiContent, Vector2 param, float min, float max)
        {
            EditorGUIUtility.wideMode = true;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(guiContent);
            EditorGUILayout.BeginVertical();
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 14;
            param.x = EditorGUILayout.Slider("X", param.x, min, max);
            param.y = EditorGUILayout.Slider("Y", param.y, min, max);
            EditorGUIUtility.labelWidth = labelWidth;
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUIUtility.wideMode = false;
            return param;
        }

        public static void Header(GUIContent guiContent)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(guiContent, EditorStyles.boldLabel);
        }

        private class TerrainGenConfigGUI
        {
            public static readonly GUIContent RESOLUTION = new GUIContent("Resolution", "Size of the generated textures");
            public static readonly GUIContent ORIGIN_HEADER = new GUIContent("Origin", "Origin of the region");
            public static readonly GUIContent X = new GUIContent("X");
            public static readonly GUIContent Z = new GUIContent("Z");
            public static readonly GUIContent WIDTH = new GUIContent("Width");
            public static readonly GUIContent LENGTH = new GUIContent("Length");
            public static readonly GUIContent WORLD_BOUNDS = new GUIContent("World Bounds", "The regions in world space for generation");
            public static readonly GUIContent TERRAIN_HEIGHT = new GUIContent("Height", "The maximum terrain height");
            public static readonly GUIContent SEED = new GUIContent("Seed", "An integer to randomize the generation");
        }

        public static TerrainGenerationConfigs TerrainGenConfigField(TerrainGenerationConfigs value)
        {
            value.resolution = EditorGUILayout.DelayedIntField(TerrainGenConfigGUI.RESOLUTION, value.resolution);
            EditorGUILayout.BeginHorizontal();
            float x = value.worldBounds.min.x;
            float z = value.worldBounds.min.y;
            float worldSizeX = value.worldBounds.width;
            float worldSizeZ = value.worldBounds.height;
            float height = value.terrainHeight;
            EditorGUILayout.PrefixLabel(TerrainGenConfigGUI.WORLD_BOUNDS);
            EditorGUILayout.BeginVertical();
            using (LabelWidthScope s = new LabelWidthScope(50))
            {
                x = EditorGUILayout.FloatField(TerrainGenConfigGUI.X, x);
                z = EditorGUILayout.FloatField(TerrainGenConfigGUI.Z, z);
                worldSizeX = EditorGUILayout.DelayedFloatField(TerrainGenConfigGUI.WIDTH, worldSizeX);
                height = EditorGUILayout.DelayedFloatField(TerrainGenConfigGUI.TERRAIN_HEIGHT, height);
                worldSizeZ = EditorGUILayout.DelayedFloatField(TerrainGenConfigGUI.LENGTH, worldSizeZ);
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            value.worldBounds = new Rect(x, z, worldSizeX, worldSizeZ);
            value.terrainHeight = height;
            value.seed = EditorGUILayout.DelayedIntField(TerrainGenConfigGUI.SEED, value.seed);
            return value;
        }

        public static bool BeginFoldout(string id, GUIContent label, GenericMenu menu = null, bool defaultExpanded = false)
        {
            bool expanded = SessionState.GetBool(id, defaultExpanded);
            expanded = EditorGUILayout.BeginFoldoutHeaderGroup(expanded, label, null, (r) =>
            {
                if (menu != null)
                {
                    menu.DropDown(r);
                }
            },
            menu != null ? null : GUIStyle.none);

            SessionState.SetBool(id, expanded);
            EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
            return expanded;
        }

        public static void EndFoldout()
        {
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.Space();
        }

        private static class DropZoneStyles
        {
            public static readonly Color32 borderColor = new Color32(88, 88, 88, 255);
            public static readonly Color32 backgroundColor = new Color32(52, 52, 52, 255);
            public static readonly Color32 highlightColor = new Color32(255, 255, 255, 20);
            public static readonly GUIStyle messageStyle = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Normal,
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter
            };
            public static float dashLineLength = 3;
        }

        public static List<T> DropZone<T>(string message, string filter = "", bool allowSceneObject = false) where T : Object
        {
            Rect r = EditorGUILayout.GetControlRect(GUILayout.Height(50));
            int controlId = EditorGUIUtility.GetControlID(FocusType.Passive, r);
            List<T> objectsToReturn = new List<T>();
            GUI.Box(r, "", EditorStyles.textArea);

            Rect messageRect = new Rect();
            messageRect.size = new Vector2(r.width, 12);
            messageRect.center = r.center - Vector2.up * (messageRect.size.y * 0.5f + 2);
            EditorGUI.LabelField(r, message, DropZoneStyles.messageStyle);
            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);

            Event e = Event.current;
            if (e != null &&
                r.Contains(e.mousePosition) &&
                (e.type == EventType.DragUpdated ||
                e.type == EventType.Repaint))
            {
                List<T> draggedObjects = DragAndDrop.objectReferences.OfType<T>().ToList();
                if (draggedObjects.Count > 0)
                {
                    DragAndDrop.AcceptDrag();
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    DragAndDrop.activeControlID = controlId;
                }
            }
            else if (e != null && e.type == EventType.DragPerform && r.Contains(e.mousePosition))
            {
                List<T> draggedObjects = DragAndDrop.objectReferences.OfType<T>().ToList();
                if (draggedObjects.Count > 0)
                {
                    objectsToReturn.AddRange(draggedObjects);
                }
            }
            else if (e != null && e.type == EventType.MouseUp && r.Contains(e.mousePosition))
            {
                EditorGUIUtility.ShowObjectPicker<T>(null, allowSceneObject, filter, controlId);
            }
            else if (e != null && e.type == EventType.ExecuteCommand && e.commandName.Equals("ObjectSelectorClosed"))
            {
                int id = EditorGUIUtility.GetObjectPickerControlID();
                Object o = EditorGUIUtility.GetObjectPickerObject();
                if (id == controlId && o != null)
                {
                    objectsToReturn.Add(o as T);
                }
            }

            return objectsToReturn;
        }

        public static bool Button(GUIContent content, params GUILayoutOption[] options)
        {
            Rect r = EditorGUILayout.GetControlRect(options);
            r = EditorGUI.IndentedRect(r);
            return GUI.Button(r, content);
        }

        public static bool Button(GUIContent content, GUIStyle style, params GUILayoutOption[] options)
        {
            Rect r = EditorGUILayout.GetControlRect(options);
            r = EditorGUI.IndentedRect(r);
            return GUI.Button(r, content, style);
        }

        public static void OpenEmailEditor(string receiver, string subject, string body)
        {
            string url = string.Format(
                "mailto:{0}" +
                "?subject={1}" +
                "&body={2}",
                receiver,
                subject.Replace(" ", "%20"),
                body.Replace(" ", "%20"));

            Application.OpenURL(url);
        }
                
        public static void IndentSpace()
        {
            EditorGUILayout.GetControlRect(GUILayout.Width(INDENT_WIDTH * EditorGUI.indentLevel));
        }

        private static class WarningStyles
        {
            public static Color themeColor = new Color(1, 0.5f, 0f, 1f);

            private static GUIStyle s_labelStyle;
            public static GUIStyle labelStyle
            {
                get
                {
                    if (s_labelStyle == null)
                    {
                        s_labelStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
                        s_labelStyle.normal.textColor = themeColor;
                        s_labelStyle.padding = new RectOffset(5, 5, 5, 5);
                    }

                    return s_labelStyle;
                }
            }
        }

        public static void DrawWarning(string text, bool rightSide = false)
        {
            if (rightSide)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(" ");
            }
            using (IndentScope s = new IndentScope(0))
            {
                Rect r = EditorGUILayout.BeginVertical();
                Handles.BeginGUI();
                Handles.DrawSolidRectangleWithOutline(r, Color.clear, WarningStyles.themeColor);
                Handles.EndGUI();
                EditorGUILayout.LabelField(text, WarningStyles.labelStyle);
                EditorGUILayout.EndVertical();
            }
            if (rightSide)
            {
                EditorGUILayout.EndHorizontal();
            }
        }

        private static class BoxStyles
        {
            public static Color32 outlineColor = new Color32(45, 45, 45, 255);
        }

        public static void Box(Rect r)
        {
            Handles.BeginGUI();
            Handles.DrawSolidRectangleWithOutline(r, Color.clear, BoxStyles.outlineColor);
            Handles.EndGUI();
        }

        private class ContextButtonStyle
        {
            public static readonly GUIContent CONTEXT_ICON = new GUIContent(contextTexture);

            private static Texture2D s_contextTexture;
            public static Texture2D contextTexture
            {
                get
                {
                    if (s_contextTexture == null)
                    {
                        s_contextTexture = Resources.Load<Texture2D>("Vista/Textures/Context");
                    }
                    return s_contextTexture;
                }
            }
        }

        public static bool ContextButton()
        {
            Rect r = EditorGUILayout.GetControlRect(GUILayout.Width(16), GUILayout.Height(16));
            bool clicked = GUI.Button(r, ContextButtonStyle.CONTEXT_ICON, EditorStyles.label);

            if (r.Contains(Event.current.mousePosition))
            {
                EditorGUI.DrawRect(r, new Color(1, 1, 1, 0.2f));
            }
            return clicked;
        }

        /// <summary>
        /// Slow!
        /// </summary>
        /// <param name="asmName"></param>
        /// <returns></returns>
        public static bool HasAssembly(string asmName)
        {
            Assembly[] assemblies = CompilationPipeline.GetAssemblies();
            foreach (Assembly asm in assemblies)
            {
                if (asm.name.Equals(asmName))
                    return true;
            }
            return false;
        }

        private class PositionEditorInternal
        {
            public static readonly Color32 backgroundColor = new Color32(0, 0, 0, 255);

            public static readonly Color pointHandleColor = new Color32(255, 255, 255, 255);
            public static readonly Vector2 pointHandleSize = new Vector2(10, 10);
            public static readonly Vector2 pointHandlePickingSize = new Vector2(25, 25);

            public static int selectedHandleIndex = -1;
            public static bool isDragging = false;

            public static Rect GetHandleRect(Rect bounds, Vector2 p)
            {
                Rect r = new Rect();
                r.size = pointHandlePickingSize;
                r.center = Utilities.NormalizedToPoint(bounds, new Vector2(p.x, 1 - p.y));
                return r;
            }

            public static Rect[] GetHandleRects(Rect bounds, List<Vector2> points)
            {
                Rect[] rects = new Rect[points.Count];
                for (int i = 0; i < points.Count; ++i)
                {
                    rects[i] = GetHandleRect(bounds, points[i]);
                }
                return rects;
            }

            public static void DrawHandles(Rect[] pRects)
            {
                for (int i = 0; i < pRects.Length; ++i)
                {
                    Color c = i == selectedHandleIndex ? Handles.selectedColor : pointHandleColor;
                    EditorGUI.DrawRect(new Rect() { size = pointHandleSize, center = pRects[i].center }, c);
                }
            }

            public static void AddPoint(List<Vector2> points, Vector2 p)
            {
                int insertIndex = GetInsertIndex(points, p);

                if (insertIndex >= 0 && insertIndex < points.Count)
                {
                    points.Insert(insertIndex, p);
                }
                else
                {
                    points.Add(p);
                }
            }

            public static void Drag(Rect bounds, List<Vector2> points, Vector2 mousePosition)
            {
                Vector2 p = Utilities.PointToNormalized(bounds, mousePosition);
                p.y = 1 - p.y;
                points[selectedHandleIndex] = p;
            }

            public static int GetInsertIndex(List<Vector2> points, Vector2 p)
            {
                int insertIndex = -1;
                if (points.Count < 2)
                {
                    return insertIndex;
                }

                float d = 0;
                float minDistance = float.MaxValue;
                Vector2 center;

                center = (points[0] + points[points.Count - 1]) * 0.5f;
                d = Vector2.Distance(p, center);
                if (d < minDistance)
                {
                    minDistance = d;
                    insertIndex = 0;
                }

                for (int i = 1; i < points.Count; ++i)
                {
                    center = (points[i] + points[i - 1]) * 0.5f;
                    d = Vector2.Distance(p, center);
                    if (d < minDistance)
                    {
                        minDistance = d;
                        insertIndex = i;
                    }
                }

                return insertIndex;
            }
        }

        public static Vector2[] PositionSelector(Rect outerRect, Rect innerRect, Vector2[] polygon)
        {
            List<Vector2> points = new List<Vector2>();
            points.AddRange(polygon);

            Rect r = innerRect;
            EditorGUI.DrawRect(r, PositionEditorInternal.backgroundColor);

            Rect[] pRects = PositionEditorInternal.GetHandleRects(r, points);
            PositionEditorInternal.DrawHandles(pRects);

            if (Event.current.type == EventType.MouseDown)
            {
                if (Event.current.button == 0)
                {
                    if (Event.current.shift)
                    {
                        Vector2 newPoint = Utilities.PointToNormalized(r, Event.current.mousePosition);
                        newPoint.y = 1 - newPoint.y;
                        PositionEditorInternal.AddPoint(points, newPoint);
                        GUI.changed = true;
                    }
                    else if (Event.current.control)
                    {
                        for (int i = 0; i < pRects.Length; ++i)
                        {
                            if (pRects[i].Contains(Event.current.mousePosition))
                            {
                                points.RemoveAt(i);
                                GUI.changed = true;
                            }
                        }
                    }
                    else
                    {
                        PositionEditorInternal.selectedHandleIndex = -1;
                        for (int i = 0; i < pRects.Length; ++i)
                        {
                            if (pRects[i].Contains(Event.current.mousePosition))
                            {
                                PositionEditorInternal.selectedHandleIndex = i;
                                PositionEditorInternal.isDragging = true;
                            }
                        }
                    }
                }
            }
            else if (Event.current.type == EventType.MouseDrag)
            {
                if (PositionEditorInternal.isDragging &&
                    PositionEditorInternal.selectedHandleIndex >= 0 &&
                    PositionEditorInternal.selectedHandleIndex < points.Count)
                {
                    PositionEditorInternal.Drag(r, points, Event.current.mousePosition);
                    GUI.changed = true;
                }
            }
            else if (Event.current.type == EventType.MouseDrag)
            {
                PositionEditorInternal.isDragging = false;
            }
            else if (Event.current.type == EventType.MouseUp)
            {
                bool willDeselect = true;
                for (int i = 0; i < pRects.Length; ++i)
                {
                    if (pRects[i].Contains(Event.current.mousePosition))
                    {
                        willDeselect = false;
                        break;
                    }
                }
                if (willDeselect)
                {
                    PositionEditorInternal.selectedHandleIndex = -1;
                    PositionEditorInternal.isDragging = false;
                }
            }
            return points.ToArray();
        }

        public static bool ConfirmButton(Rect position, GUIContent text)
        {
            GUI.color = Color.white;
            GUI.backgroundColor = new Color32(39, 118, 234, 255);
            bool clicked = GUI.Button(position, text, Styles.confirmButton);
            GUI.backgroundColor = Color.white;
            return clicked;
        }

        public static bool RejectButton(Rect position, GUIContent text)
        {
            GUI.color = Color.white;
            GUI.backgroundColor = new Color32(234, 58, 39, 255);
            bool clicked = GUI.Button(position, text, Styles.confirmButton);
            GUI.backgroundColor = Color.white;
            return clicked;
        }

        static readonly int[] HM_RES_VALUES = new int[6] { 129, 257, 513, 1025, 2049, 4097 };
        static readonly string[] HM_RES_LABELS = new string[6] { "129", "257", "513", "1K", "2K", "4K" };

        public static int HeightMapResolutionSelector(GUIContent label, int selectedValue)
        {
            return ResolutionSelector(label, selectedValue, HM_RES_VALUES, HM_RES_LABELS, Constants.HM_RES_MAX);
        }

        static readonly int[] TEX_RES_VALUES = new int[6] { 128, 256, 512, 1024, 2048, 4096 };
        static readonly string[] TEX_RES_LABELS = new string[6] { "128", "256", "512", "1K", "2K", "4K" };

        public static int TextureResolutionSelector(GUIContent label, int selectedValue)
        {
            return ResolutionSelector(label, selectedValue, TEX_RES_VALUES, TEX_RES_LABELS, Constants.RES_MAX);
        }

        private static int ResolutionSelector(GUIContent label, int selectedValue, int[] presetValues, string[] presetLabels, int limit)
        {
            bool lastGUIState = GUI.enabled;
            if (selectedValue > limit)
            {
                selectedValue = limit;
                GUI.changed = true;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            for (int i = 0; i < presetValues.Length; ++i)
            {
                int v = presetValues[i];
                string s = presetLabels[i];

                GUIStyle btnStyle = i == 0 ? EditorStyles.miniButtonLeft :
                    i == HM_RES_VALUES.Length - 1 ? EditorStyles.miniButtonRight :
                    EditorStyles.miniButtonMid;

                GUI.backgroundColor = v == selectedValue ? Color.gray : Color.white;
                if (v > limit)
                {
                    GUI.enabled = false;
                }

                if (GUILayout.Button(s, btnStyle, GUILayout.ExpandWidth(true)))
                {
                    selectedValue = v;
                    GUI.changed = true;
                }
            }
            EditorGUILayout.EndHorizontal();

            GUI.backgroundColor = Color.white;
            GUI.enabled = lastGUIState;

            return selectedValue;
        }

        public static bool ToggleButton(GUIContent content, bool value, params GUILayoutOption[] options)
        {
            Color oldBackgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = value ? Color.gray : Color.white;
            if (GUILayout.Button(content, EditorStyles.miniButton, options))
            {
                value = !value;
                GUI.changed = true;
            }
            GUI.backgroundColor = oldBackgroundColor;
            return value;
        }

        public static bool ToggleButton(string text, bool value, params GUILayoutOption[] options)
        {
            return ToggleButton(new GUIContent(text), value, options);
        }

        public static bool ButtonCTA(Rect position, GUIContent text)
        {
            GUI.color = Color.white;
            GUI.backgroundColor = GUI.enabled ? new Color(0.13f, 0.59f, 0.95f, 1) * 2.2f : Color.white;
            bool clicked = GUI.Button(position, text);
            GUI.backgroundColor = Color.white;
            return clicked;
        }
    }
}
#endif
