#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEditor;

namespace Pinwheel.VistaEditor.Graph
{
    public static class SubWindowManager
    {
        public const string KEY_VISIBLE = "visible";

        public static bool IsVisibleBySettings(string viewDataKey)
        {
            return EditorPrefs.GetBool(viewDataKey + KEY_VISIBLE, false);
        }

        public static void ToggleViewVisibility(SubWindow wd)
        {
            if (IsVisibleBySettings(wd.viewDataKey))
            {
                Hide(wd);
            }
            else
            {
                Show(wd);
            }
        }

        public static void Show(SubWindow wd)
        {
            wd.Show();
            EditorPrefs.SetBool(wd.viewDataKey + KEY_VISIBLE, true);
        }

        public static void Hide(SubWindow wd)
        {
            wd.Hide();
            EditorPrefs.SetBool(wd.viewDataKey + KEY_VISIBLE, false);
        }
    }
}
#endif
