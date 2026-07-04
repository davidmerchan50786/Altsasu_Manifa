#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;
using UnityEditor;

namespace Pinwheel.VistaEditor
{
    internal class HowSmartSearchWorksWindow : EditorWindow
    {
        //[MenuItem("Window/Vista/How Smart Search Works")]
        public static void ShowWindow()
        {
            HowSmartSearchWorksWindow w = CreateInstance<HowSmartSearchWorksWindow>();
            w.Show();
        }

        private void OnEnable()
        {
            content = new HowSmartSearchWorksPopupContent();
            content.OnOpen();

            position = new Rect(position.position, content.GetWindowSize());
        }

        private void OnDisable()
        {
            content.OnClose();
        }

        HowSmartSearchWorksPopupContent content;
        private void OnGUI()
        {
            if (content != null)
            {
                content.OnGUI(new Rect(Vector2.zero, content.GetWindowSize()));
            }

            titleContent = new GUIContent(position.size.ToString());
        }
    }

    public class HowSmartSearchWorksPopupContent : PopupWindowContent
    {
        Texture2D imgSmartSearchExample;
        GUIContent ctaText = new GUIContent("Explore more productivity features");
        string ctaLink = "https://www.pinwheelstud.io/post/vista-productivity-features";

        public override void OnOpen()
        {
            base.OnOpen();
            imgSmartSearchExample = Resources.Load<Texture2D>("Vista/Textures/Guides/guide-smartsearch-example");
        }

        public override void OnClose()
        {
            base.OnClose();
            if (imgSmartSearchExample != null)
            {
                Resources.UnloadAsset(imgSmartSearchExample);
            }
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(350, 380);
        }

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("How Smart Search works?", EditorCommon.Styles.h1);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Smart Search is a productivity feature in Indie and Pro edition that allows you to quickly find the node you need not just from its name, but also from related keywords and ideas.", EditorCommon.Styles.p1);
            EditorGUILayout.LabelField("This greatly reduce the time spend on search list just to find the node.", EditorCommon.Styles.p1);
            if (imgSmartSearchExample != null)
            {
                Rect imgRect = EditorGUILayout.GetControlRect(GUILayout.Width(imgSmartSearchExample.width), GUILayout.Height(imgSmartSearchExample.height));
                GUI.DrawTexture(imgRect, imgSmartSearchExample);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("It also perform quick node setup on creation based on search query, for example: you search for 'add', the editor is able to create a Combine node in Add mode.", EditorCommon.Styles.p1);

            EditorGUILayout.Space();
            Rect ctaButtonRect = EditorGUILayout.GetControlRect(GUILayout.Width(250), GUILayout.Height(24));
            if (EditorCommon.ButtonCTA(ctaButtonRect, ctaText))
            {
                NetUtils.TrackClick("explore-features", UILocation.Popup_SmartSearch);
                Application.OpenURL(ctaLink);
            }

            EditorGUILayout.EndVertical();
        }
    }
}
#endif
