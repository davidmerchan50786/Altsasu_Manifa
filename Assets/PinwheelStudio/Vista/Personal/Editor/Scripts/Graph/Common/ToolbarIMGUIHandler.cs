#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.VistaEditor;
using Pinwheel.Vista.Graphics;
using UnityEditor;
using System;

namespace Pinwheel.VistaEditor.Graph
{
    public static class ToolbarIMGUIHandler
    {
        private static GUIStyle s_toolbarAdText;
        private static GUIStyle toolbarAdText
        {
            get
            {
                if (s_toolbarAdText == null)
                {
                    s_toolbarAdText = new GUIStyle(EditorStyles.label);
                    s_toolbarAdText.richText = true;
                    s_toolbarAdText.fontStyle = FontStyle.BoldAndItalic;
                }

                return s_toolbarAdText;
            }
        }

        [InitializeOnLoadMethod]
        private static void OnInit()
        {
            GraphEditorToolbar.leftImguiCallback += OnGraphEditorToolbarLeftIMGUI;
        }

        private static void OnGraphEditorToolbarLeftIMGUI(GraphEditorToolbar sender)
        {
            bool isPersonalEdition = !ProjectInitializer.isVistaIndieInstalled && !ProjectInitializer.isVistaProInstalled;
            if (!isPersonalEdition)
                return;

            Rect adTextRect = EditorGUILayout.BeginHorizontal();
            EditorGUI.DrawRect(new RectOffset(1, 1, -2, -2).Add(adTextRect), new Color32(50, 50, 50, 255));

            EditorGUILayout.GetControlRect(GUILayout.Width(2));
            EditorGUILayout.LabelField("Vista - Personal Edition", toolbarAdText, GUILayout.Width(145), GUILayout.Height(23));

            bool hasPromo = false;
            var featuredAssets = EditorSettings.Get().marketingSettings.GetFeaturedAssets();
            foreach (var a in featuredAssets)
            {
                if (a.name.StartsWith("Vista") && !string.IsNullOrEmpty(a.promotionText))
                {
                    hasPromo = true;
                    string text = $"<color=orange>{a.promotionText}</color>";
                    if (GUILayout.Button(text, toolbarAdText, GUILayout.Height(23)))
                    {
                        NetUtils.TrackClick("upgrade-promo-cta", UILocation.GraphEditor_Toolbar);
                        Application.OpenURL(a.link);
                    }
                    Rect r = GUILayoutUtility.GetLastRect();
                    EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
                    break;
                }
            }

            if (!hasPromo)
            {
                string text = $"<color=orange>Upgrade to Pro</color>";
                if (GUILayout.Button(text, toolbarAdText, GUILayout.Height(23)))
                {
                    NetUtils.TrackClick("upgrade-cta", UILocation.GraphEditor_Toolbar);
                    Application.OpenURL(Links.VISTA_PRO);
                }
                Rect r = GUILayoutUtility.GetLastRect();
                EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
            }

            EditorGUILayout.GetControlRect(GUILayout.Width(2));
            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
