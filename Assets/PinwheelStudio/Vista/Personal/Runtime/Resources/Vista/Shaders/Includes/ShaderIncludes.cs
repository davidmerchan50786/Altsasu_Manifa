#if VISTA
#if UNITY_EDITOR
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;
using UnityEditor;
using System.IO;
using System.Text;

namespace Pinwheel.Vista.BigWorld
{
    public static class ShaderIncludes
    {
        [InitializeOnLoadMethod]
        private static void OnInitialize()
        {
            EditorApplication.projectChanged += GenerateIncludesFile;
            GenerateIncludesFile();
        }

        private static void GenerateIncludesFile()
        {
            string directory = GetDirectory();
            string filePath = Path.Combine(directory, $"{typeof(ShaderIncludes).Name}.hlsl");
            string[] includeFilePaths = GetAllIncludeFilePaths();
            StringBuilder text = new StringBuilder();
            text.AppendLine("//This file was generated, don't edit by hand")
                .AppendLine("#ifndef VISTA_BIG_WORLD_SHADER_INCLUDES")
                .AppendLine("#define VISTA_BIG_WORLD_SHADER_INCLUDES")
                .AppendLine();
            
            foreach (string p in includeFilePaths)
            {
                string fileName = Path.GetFileName(p);
                string macro = ObjectNames.NicifyVariableName(fileName).Replace('.', ' ').Replace(' ', '_').ToUpper();
                text.AppendLine($"#define {macro} \"{p}\"");
            }
            text.AppendLine();
            text.Append("#endif");

            string newContent = text.ToString();
            string oldContent = File.ReadAllText(filePath);
            if (!string.Equals(oldContent, newContent))
            {
                try
                {
                    AssetDatabase.StartAssetEditing();
                    File.WriteAllText(filePath, newContent);
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
            }
        }

        private static string GetDirectory()
        {
            string[] guids = AssetDatabase.FindAssets($"t:Script {typeof(ShaderIncludes).Name}");
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return Path.GetDirectoryName(path);
        }

        private static string[] GetAllIncludeFilePaths()
        {
            string[] guids = AssetDatabase.FindAssets("l:VistaCoreIncludeFile");
            string[] paths = new string[guids.Length];
            for (int i = 0; i < guids.Length; ++i)
            {
                paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
            }
            return paths;
        }
    }
}
#endif
#endif