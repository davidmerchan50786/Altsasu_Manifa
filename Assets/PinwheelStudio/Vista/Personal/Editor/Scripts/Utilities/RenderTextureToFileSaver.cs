#if VISTA
using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;
using System.IO;

namespace Pinwheel.VistaEditor
{
    [InitializeOnLoad]
    public static class RenderTextureToFileSaver
    {
        [InitializeOnLoadMethod]
        public static void OnInitialize()
        {
            RenderTextureToFileUtils.saveRenderTextureCallback += OnSaveRenderTexture;
        }

        private static void OnSaveRenderTexture(RenderTexture rt, string fileNameNoExtension)
        {
            if (rt.format == RenderTextureFormat.RFloat)
            {
                SaveRT_RFloat(rt, fileNameNoExtension);
            }
            else if (rt.format == RenderTextureFormat.ARGB32)
            {
                SaveRT_ARGB32(rt, fileNameNoExtension);
            }
            else
            {
                throw new System.ArgumentException($"Save RT to file: Unsupported RT format {rt.format}");
            }
        }

        private static void SaveRT_RFloat(RenderTexture rt, string fileNameNoExtension)
        {
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RFloat, false, true);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            string directory = EditorSettings.Get().graphEditorSettings.fileExportDirectory;
            string path = Path.Combine(directory, fileNameNoExtension + ".r32");
            byte[] rawData = tex.GetRawTextureData();
            File.WriteAllBytes(path, rawData);

            Object.DestroyImmediate(tex);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            RawImporter importer = AssetImporter.GetAtPath(path) as RawImporter;
            if (importer != null)
            {
                importer.width = rt.width;
                importer.height = rt.height;
                importer.bitDepth = RawImporter.BitDepth.Bit32;

                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();

                AssetDatabase.Refresh();
            }
        }

        private static void SaveRT_ARGB32(RenderTexture rt, string fileNameNoExtension)
        {
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false, true);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            string directory = EditorSettings.Get().graphEditorSettings.fileExportDirectory;
            string path = Path.Combine(directory, fileNameNoExtension + ".png");
            byte[] rawData = tex.EncodeToPNG();
            File.WriteAllBytes(path, rawData);

            Object.DestroyImmediate(tex);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
#endif
