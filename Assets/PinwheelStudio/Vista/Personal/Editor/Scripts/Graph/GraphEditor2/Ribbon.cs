#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;
using UnityEngine.UIElements;
using UnityEditor;

namespace Pinwheel.VistaEditor.Graph
{
    public class Ribbon : TextElement
    {
        protected Button m_closeButton;

        protected static string CLOSED_PREF_KEY = "graph-editor-ribbon-closed-" + System.DateTime.Today.ToString();

        public Ribbon() : base()
        {
            StyleSheet uss = Resources.Load<StyleSheet>("Vista/USS/Graph/Ribbon");
            styleSheets.Add(uss);

            AddToClassList("ribbon");
            text = "This is the <b>ribbon</b>";
            enableRichText = true;

            m_closeButton = new Button() { text = "X" };
            m_closeButton.clicked += OnCloseButtonClicked;
            m_closeButton.AddToClassList("close-button");
            Add(m_closeButton);
        }

        private void OnCloseButtonClicked()
        {
            SetClosedToday();
            Hide();
        }

        public void Show()
        {
            style.display = new StyleEnum<DisplayStyle>(DisplayStyle.Flex);
        }

        public void Hide()
        {
            style.display = new StyleEnum<DisplayStyle>(DisplayStyle.None);
        }

        public static bool CanShowToday()
        {
            return !EditorPrefs.HasKey(CLOSED_PREF_KEY);
        }

        public static void SetClosedToday()
        {
            EditorPrefs.SetBool(CLOSED_PREF_KEY, true);
        }
    }
}
#endif
