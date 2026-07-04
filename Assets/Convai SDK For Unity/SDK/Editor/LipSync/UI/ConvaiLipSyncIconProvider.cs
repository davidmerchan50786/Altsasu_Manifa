#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.LipSync.Editor.UI
{
    internal static class ConvaiLipSyncIconProvider
    {
        private static readonly string[] CandidateIconPaths =
        {
            "Packages/com.convai.convai-sdk-for-unity/SDK/Editor/Art/UI/Branding/Convai Icon.png"
        };

        private static Texture2D s_convaiIcon;

        public static Texture2D GetConvaiIcon()
        {
            if (s_convaiIcon != null) return s_convaiIcon;

            for (int i = 0; i < CandidateIconPaths.Length; i++)
            {
                string path = CandidateIconPaths[i];
                s_convaiIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (s_convaiIcon != null) return s_convaiIcon;
            }

            s_convaiIcon = EditorGUIUtility.IconContent("d_UnityEditor.InspectorWindow").image as Texture2D;
            if (s_convaiIcon == null) s_convaiIcon = CreateFallbackTexture();
            return s_convaiIcon;
        }

        private static Texture2D CreateFallbackTexture()
        {
            Texture2D texture = new(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return texture;
        }
    }
}
#endif
