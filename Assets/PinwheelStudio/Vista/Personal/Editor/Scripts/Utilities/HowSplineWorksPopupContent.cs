#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;
using UnityEditor;

namespace Pinwheel.VistaEditor
{
    internal class HowSplineWorksWindow : EditorWindow
    {
        //[MenuItem("Window/Vista/How Spline Works")]
        public static void ShowWindow()
        {
            HowSplineWorksWindow w = CreateInstance<HowSplineWorksWindow>();
            w.Show();
        }

        private void OnEnable()
        {
            content = new HowSplineWorksPopupContent();
            content.OnOpen();
        }

        private void OnDisable()
        {
            content.OnClose();
        }

        HowSplineWorksPopupContent content;
        private void OnGUI()
        {
            if (content != null)
            {
                content.OnGUI(new Rect(Vector2.zero, content.GetWindowSize()));
            }

            titleContent = new GUIContent(position.size.ToString());
        }
    }

    public class HowSplineWorksPopupContent : PopupWindowContent
    {
        Texture2D imgSplineExample;
        Texture2D imgSplineNodesExample;
        GUIContent ctaText = new GUIContent("Explore more Pro features");
        string ctaLink = "https://www.pinwheelstud.io/post/vista-pro-features";

        public override void OnOpen()
        {
            base.OnOpen();
            imgSplineExample = Resources.Load<Texture2D>("Vista/Textures/Guides/guide-spline-example");
            imgSplineNodesExample = Resources.Load<Texture2D>("Vista/Textures/Guides/guide-spline-node-example");
        }

        public override void OnClose()
        {
            base.OnClose();
            if (imgSplineExample != null)
            {
                Resources.UnloadAsset(imgSplineExample);
            }
            if (imgSplineNodesExample != null)
            {
                Resources.UnloadAsset(imgSplineNodesExample);
            }
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(650, 850);
        }

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Splines for path based terrain generation", EditorCommon.Styles.h1);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Create mountain passes, paths, rivers, terrain conforming, and cleared corridors that follow a route you place by hand in the scene. Vista Pro can use splines from different spline providers for this workflow.", EditorCommon.Styles.p1);

            if (imgSplineExample != null)
            {
                Rect imgRect = EditorGUILayout.GetControlRect(GUILayout.Width(imgSplineExample.width), GUILayout.Height(imgSplineExample.height));
                GUI.DrawTexture(imgRect, imgSplineExample);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Vista Pro includes spline nodes for common tasks, and it can also expose the spline itself as graph data. You can use the built in nodes when they fit, or read spline masks, height, anchors, and points to build your own graph logic.", EditorCommon.Styles.p1);
            if (imgSplineNodesExample != null)
            {
                Rect imgRect = EditorGUILayout.GetControlRect(GUILayout.Width(imgSplineNodesExample.width), GUILayout.Height(imgSplineNodesExample.height));
                GUI.DrawTexture(imgRect, imgSplineNodesExample);
            }

            EditorGUILayout.Space();
            Rect ctaButtonRect = EditorGUILayout.GetControlRect(GUILayout.Width(250), GUILayout.Height(24));
            if (EditorCommon.ButtonCTA(ctaButtonRect, ctaText))
            {
                NetUtils.TrackClick("explore-features", UILocation.Popup_Spline);
                Application.OpenURL(ctaLink);
            }

            EditorGUILayout.EndVertical();
        }
    }
}
#endif
