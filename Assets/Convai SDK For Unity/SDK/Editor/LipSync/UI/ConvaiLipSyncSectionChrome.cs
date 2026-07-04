#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.LipSync.Editor.UI
{
    internal readonly struct ConvaiSectionHeaderSpec
    {
        public ConvaiSectionHeaderSpec(
            string editorTypeId,
            string sectionId,
            string title,
            string icon,
            Color? headerColor = null,
            int iconFontSize = ConvaiLipSyncEditorThemeTokens.SectionIconFontSize)
        {
            EditorTypeId = editorTypeId;
            SectionId = sectionId;
            Title = title;
            Icon = icon;
            HeaderColor = headerColor ?? ConvaiLipSyncEditorThemeTokens.Accent;
            IconFontSize = iconFontSize;
        }

        public string EditorTypeId { get; }
        public string SectionId { get; }
        public string Title { get; }
        public string Icon { get; }
        public Color HeaderColor { get; }
        public int IconFontSize { get; }
    }

    internal static class ConvaiLipSyncSectionChrome
    {
        public static bool DrawHeader(in ConvaiSectionHeaderSpec spec, bool expanded)
        {
            ConvaiLipSyncEditorStyleCache.EnsureInitialized();

            GUIStyle headerLabelStyle = ConvaiLipSyncEditorStyleCache.SectionHeaderLabelStyle;
            GUIStyle iconStyle = ConvaiLipSyncEditorStyleCache.SectionIconStyle;
            GUIStyle chevronStyle = ConvaiLipSyncEditorStyleCache.SectionChevronStyle;

            SetTextColor(headerLabelStyle, spec.HeaderColor);
            SetTextColor(iconStyle, spec.HeaderColor);
            SetTextColor(chevronStyle, spec.HeaderColor);
            iconStyle.fontSize = spec.IconFontSize;

            EditorGUILayout.BeginHorizontal(GUILayout.Height(ConvaiLipSyncEditorThemeTokens.SectionHeaderRowHeight));

            GUI.color = spec.HeaderColor;
            GUILayout.Label(spec.Icon, iconStyle);
            GUI.color = Color.white;

            GUILayout.Space(ConvaiLipSyncEditorThemeTokens.SectionIconSpacing);
            GUILayout.Label(expanded ? "\u25BC" : "\u25B6", chevronStyle);
            GUILayout.Space(ConvaiLipSyncEditorThemeTokens.SectionChevronTextSpacing);

            Rect titleRect = GUILayoutUtility.GetRect(
                GUIContent.none,
                headerLabelStyle,
                GUILayout.Height(ConvaiLipSyncEditorThemeTokens.SectionHeaderRowHeight),
                GUILayout.ExpandWidth(true));
            EditorGUI.LabelField(titleRect, spec.Title, headerLabelStyle);

            EditorGUILayout.EndHorizontal();

            Rect headerRect = GUILayoutUtility.GetLastRect();
            EditorGUIUtility.AddCursorRect(headerRect, MouseCursor.Link);

            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.MouseDown && headerRect.Contains(currentEvent.mousePosition))
            {
                expanded = !expanded;
                currentEvent.Use();
            }

            if (expanded)
            {
                Rect lineRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(1));
                lineRect.x += 4;
                lineRect.width -= 8;
                EditorGUI.DrawRect(lineRect, ConvaiLipSyncEditorThemeTokens.DividerColor(spec.HeaderColor));
            }

            return expanded;
        }

        public static void BeginBody(Color? backgroundOverride = null)
        {
            Color background = backgroundOverride ?? ConvaiLipSyncEditorThemeTokens.SectionBackground;

            Rect bodyRect = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(
                new Rect(
                    bodyRect.x,
                    bodyRect.y,
                    bodyRect.width,
                    bodyRect.height + ConvaiLipSyncEditorThemeTokens.SectionBodyBottomFill),
                background);

            GUILayout.Space(ConvaiLipSyncEditorThemeTokens.SectionBodyTopPadding);
            EditorGUI.indentLevel++;
        }

        public static void EndBody()
        {
            EditorGUI.indentLevel--;
            GUILayout.Space(ConvaiLipSyncEditorThemeTokens.SectionBodyBottomPadding);
            EditorGUILayout.EndVertical();
            GUILayout.Space(ConvaiLipSyncEditorThemeTokens.SectionOuterSpacing);
        }

        private static void SetTextColor(GUIStyle style, Color color)
        {
            style.normal.textColor = color;
            style.onNormal.textColor = color;
            style.focused.textColor = color;
            style.onFocused.textColor = color;
            style.hover.textColor = color;
            style.onHover.textColor = color;
            style.active.textColor = color;
            style.onActive.textColor = color;
        }
    }
}
#endif
