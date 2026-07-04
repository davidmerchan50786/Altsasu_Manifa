#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;
using UnityEditor;

namespace Pinwheel.VistaEditor
{
    internal class HowSubgraphWorksWindow : EditorWindow
    {
        //[MenuItem("Window/Vista/How Subgraph Works")]
        public static void ShowWindow()
        {
            HowSubgraphWorksWindow w = CreateInstance<HowSubgraphWorksWindow>();
            w.Show();
        }

        private void OnEnable()
        {
            content = new HowSubgraphWorksPopupContent();
            content.OnOpen();
        }

        private void OnDisable()
        {
            content.OnClose();
        }

        HowSubgraphWorksPopupContent content;
        private void OnGUI()
        {
            if (content != null)
            {
                content.OnGUI(new Rect(Vector2.zero, content.GetWindowSize()));
            }

            titleContent = new GUIContent(position.size.ToString());
        }
    }

    public class HowSubgraphWorksPopupContent : PopupWindowContent
    {
        Texture2D imgSubgraphExample;
        Texture2D imgSubgraphSearcher;
        GUIContent ctaText = new GUIContent("Explore more productivity features");
        string ctaLink = "https://www.pinwheelstud.io/post/vista-productivity-features";

        public override void OnOpen()
        {
            base.OnOpen();
            imgSubgraphExample = Resources.Load<Texture2D>("Vista/Textures/Guides/guide-subgraph-example");
            imgSubgraphSearcher = Resources.Load<Texture2D>("Vista/Textures/Guides/guide-subgraph-searcher");
        }

        public override void OnClose()
        {
            base.OnClose();
            if (imgSubgraphExample != null)
            {
                Resources.UnloadAsset(imgSubgraphExample);
            }
            if (imgSubgraphSearcher != null)
            {
                Resources.UnloadAsset(imgSubgraphSearcher);
            }
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(550, 575);
        }

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("How Subgraph works?", EditorCommon.Styles.h1);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Subgraph is a productivity feature in Indie and Pro edition that allows you to nest a terrain graph inside another one as a reuseable block, with customizable inputs and outputs.", EditorCommon.Styles.p1);
            EditorGUILayout.LabelField("It keeps your graph clean and well structured as it grows bigger.", EditorCommon.Styles.p1);
            if (imgSubgraphExample != null)
            {
                Rect imgRect = EditorGUILayout.GetControlRect(GUILayout.Width(imgSubgraphExample.width), GUILayout.Height(imgSubgraphExample.height));
                GUI.DrawTexture(imgRect, imgSubgraphExample);
            }

            EditorGUILayout.LabelField("Available subgraphs will be listed in the searcher.", EditorCommon.Styles.p1);
            if (imgSubgraphSearcher != null)
            {
                Rect imgRect = EditorGUILayout.GetControlRect(GUILayout.Width(imgSubgraphSearcher.width), GUILayout.Height(imgSubgraphSearcher.height));
                GUI.DrawTexture(imgRect, imgSubgraphSearcher);
            }

            EditorGUILayout.LabelField("Or you can drag and drop a terrain graph asset directly to the graph view.", EditorCommon.Styles.p1);

            EditorGUILayout.Space();
            Rect ctaButtonRect = EditorGUILayout.GetControlRect(GUILayout.Width(250), GUILayout.Height(24));
            if (EditorCommon.ButtonCTA(ctaButtonRect, ctaText))
            {
                NetUtils.TrackClick("explore-features", UILocation.Popup_Subgraph);
                Application.OpenURL(ctaLink);
            }

            EditorGUILayout.EndVertical();
        }
    }
}
#endif
