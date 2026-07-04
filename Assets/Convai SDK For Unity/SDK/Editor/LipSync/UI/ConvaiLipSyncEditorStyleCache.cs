#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.LipSync.Editor.UI
{
    internal static class ConvaiLipSyncEditorStyleCache
    {
        private static bool s_isInitialized;
        private static bool s_lastKnownProSkin;

        public static GUIStyle SectionHeaderLabelStyle { get; private set; }
        public static GUIStyle SectionIconStyle { get; private set; }
        public static GUIStyle SectionChevronStyle { get; private set; }

        private static GUIStyle CreateSafeBaseLabelStyle()
        {
            try
            {
                if (EditorStyles.boldLabel != null)
                    return EditorStyles.boldLabel;
            }
            catch
            {
                // Unity can throw while resolving editor styles in headless batch mode.
            }

            try
            {
                if (GUI.skin?.label != null)
                    return GUI.skin.label;
            }
            catch
            {
                // GUI skin can also be unavailable depending on editor initialization timing.
            }

            return new GUIStyle();
        }

        public static void EnsureInitialized()
        {
            bool isProSkin = EditorGUIUtility.isProSkin;
            if (s_isInitialized && s_lastKnownProSkin == isProSkin) return;

            s_lastKnownProSkin = isProSkin;
            s_isInitialized = true;

            GUIStyle baseLabelStyle = CreateSafeBaseLabelStyle();

            SectionHeaderLabelStyle = new GUIStyle(baseLabelStyle)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 11,
                fixedHeight = ConvaiLipSyncEditorThemeTokens.SectionHeaderRowHeight,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 0, 0)
            };

            SectionIconStyle = new GUIStyle(SectionHeaderLabelStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                fixedWidth = ConvaiLipSyncEditorThemeTokens.SectionIconCellWidth,
                fontSize = ConvaiLipSyncEditorThemeTokens.SectionIconFontSize,
                contentOffset = Vector2.zero
            };

            SectionChevronStyle = new GUIStyle(SectionHeaderLabelStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                fixedWidth = ConvaiLipSyncEditorThemeTokens.SectionChevronCellWidth,
                fontSize = 10,
                contentOffset = Vector2.zero
            };
        }
    }
}
#endif
