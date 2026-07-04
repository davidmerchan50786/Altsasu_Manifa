#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;
using UnityEditor;

namespace Pinwheel.VistaEditor
{
    internal class HowRwdWorksWindow : EditorWindow
    {
        //[MenuItem("Window/Vista/How Rwd Works")]
        public static void ShowWindow()
        {
            HowRwdWorksWindow w = CreateInstance<HowRwdWorksWindow>();
            w.Show();
        }

        private void OnEnable()
        {
            content = new HowRwdWorksPopupContent();
            content.OnOpen();
        }

        private void OnDisable()
        {
            content.OnClose();
        }

        HowRwdWorksPopupContent content;
        private void OnGUI()
        {
            if (content != null)
            {
                content.OnGUI(new Rect(Vector2.zero, content.GetWindowSize()));
            }

            titleContent = new GUIContent(position.size.ToString());
        }
    }

    public class HowRwdWorksPopupContent : PopupWindowContent
    {
        Texture2D imgWorldMapExample;
        Texture2D imgRwdNodesExample;
        GUIContent ctaText = new GUIContent("Explore more Pro features");
        string ctaLink = "https://www.pinwheelstud.io/post/vista-pro-features";

        public override void OnOpen()
        {
            base.OnOpen();
            imgWorldMapExample = Resources.Load<Texture2D>("Vista/Textures/Guides/guide-world-map-example");
            imgRwdNodesExample = Resources.Load<Texture2D>("Vista/Textures/Guides/guide-rwd-nodes-example");
        }

        public override void OnClose()
        {
            base.OnClose();
            if (imgWorldMapExample != null)
            {
                Resources.UnloadAsset(imgWorldMapExample);
            }
            if (imgRwdNodesExample != null)
            {
                Resources.UnloadAsset(imgRwdNodesExample);
            }
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(650, 675);
        }

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Build From A Real Location", EditorCommon.Styles.h1);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Choose a region on the world map, download elevation and satellite imagery, and bring that data straight into your graph in Vista Pro. Instead of rebuilding a real landscape by hand, you start from real geography and keep working inside the same terrain workflow.", EditorCommon.Styles.p1);

            if (imgWorldMapExample != null)
            {
                Rect imgRect = EditorGUILayout.GetControlRect(GUILayout.Width(imgWorldMapExample.width), GUILayout.Height(imgWorldMapExample.height));
                GUI.DrawTexture(imgRect, imgWorldMapExample);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Use the map view to pick your area, then load the downloaded data into the graph with Real World Data nodes. If your project is based on a real place, this gives you a faster and more grounded starting point.", EditorCommon.Styles.p1);
            if (imgRwdNodesExample != null)
            {
                Rect imgRect = EditorGUILayout.GetControlRect(GUILayout.Width(imgRwdNodesExample.width), GUILayout.Height(imgRwdNodesExample.height));
                GUI.DrawTexture(imgRect, imgRwdNodesExample);
            }

            EditorGUILayout.Space();
            Rect ctaButtonRect = EditorGUILayout.GetControlRect(GUILayout.Width(250), GUILayout.Height(24));
            if (EditorCommon.ButtonCTA(ctaButtonRect, ctaText))
            {
                NetUtils.TrackClick("explore-features", UILocation.Popup_Rwd);
                Application.OpenURL(ctaLink);
            }

            EditorGUILayout.EndVertical();
        }
    }
}
#endif
