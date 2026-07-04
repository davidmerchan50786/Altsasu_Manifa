#if VISTA
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Pinwheel.VistaEditor
{
    public class TerrainLayerDiffuseColorWindow : EditorWindow
    {
        private const string MENU_PATH = "Window/Vista/Utilities/Terrain Layer Diffuse Color";
        private const string WINDOW_TITLE = "Layer Diffuse Color";
        private const string GENERATED_FILE_SUFFIX = "_VistaGeneratedTexture";
        private const string GENERATED_USER_DATA_MARKER = "Pinwheel.VistaEditor.GeneratedTerrainLayerDiffuseColorTexture";
        private const string GENERATED_LABEL = "VistaGeneratedDiffuseColor";
        private const string LAYER_GUID_PREFIX = "LayerGuid:";
        private const int TEXTURE_SIZE = 4;

        private static readonly GUIContent SELECTED_LAYER = new GUIContent("Selected Layer");
        private static readonly GUIContent DIFFUSE_TEXTURE = new GUIContent("Diffuse Texture");
        private static readonly GUIContent COLOR = new GUIContent("Color");
        private static readonly GUIContent TARGET_PATH = new GUIContent("Target Path");

        private UnityEngine.Object m_selectedObject;
        private TerrainLayer m_terrainLayer;
        private Color m_color = Color.white;
        private string m_status;

        [MenuItem(MENU_PATH)]
        public static void ShowWindow()
        {
            TerrainLayerDiffuseColorWindow window = GetWindow<TerrainLayerDiffuseColorWindow>();
            window.titleContent = new GUIContent(WINDOW_TITLE);
            window.minSize = new Vector2(360, 180);
            window.RefreshSelection();
            window.Show();
        }

        public static void ShowWindow(TerrainLayer terrainLayer)
        {
            if (terrainLayer != null)
            {
                Selection.activeObject = terrainLayer;
            }

            ShowWindow();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WINDOW_TITLE);
            minSize = new Vector2(360, 180);
            RefreshSelection();
        }

        private void OnSelectionChange()
        {
            RefreshSelection();
        }

        private void OnGUI()
        {
            if (m_selectedObject != Selection.activeObject)
            {
                RefreshSelection();
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(SELECTED_LAYER, m_terrainLayer, typeof(TerrainLayer), false);
                }

                if (m_terrainLayer == null)
                {
                    EditorGUILayout.HelpBox("Select a TerrainLayer asset in the Project window.", MessageType.Info);
                    return;
                }

                string terrainLayerPath = AssetDatabase.GetAssetPath(m_terrainLayer);
                if (string.IsNullOrEmpty(terrainLayerPath) || !AssetDatabase.Contains(m_terrainLayer))
                {
                    EditorGUILayout.HelpBox("The selected TerrainLayer is not an asset on disk.", MessageType.Warning);
                    return;
                }

                Texture2D diffuseTexture = m_terrainLayer.diffuseTexture;
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(DIFFUSE_TEXTURE, diffuseTexture, typeof(Texture2D), false);
                }

                DrawDiffuseTextureHint(diffuseTexture, m_terrainLayer);

                EditorGUILayout.Space();
                EditorGUI.BeginChangeCheck();
                Color color = EditorGUILayout.ColorField(COLOR, m_color, true, true, false);
                if (EditorGUI.EndChangeCheck())
                {
                    m_color = color;
                    string updatePath = GetTargetTexturePath(m_terrainLayer, terrainLayerPath);
                    GenerateAndAssign(m_terrainLayer, terrainLayerPath, updatePath);
                }
                else
                {
                    m_color = color;
                }

                string targetPath = GetTargetTexturePath(m_terrainLayer, terrainLayerPath);
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(TARGET_PATH, targetPath);
                }

                if (!string.IsNullOrEmpty(m_status))
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.HelpBox(m_status, MessageType.Info);
                }
            }
        }

        private void RefreshSelection()
        {
            m_selectedObject = Selection.activeObject;
            m_terrainLayer = m_selectedObject as TerrainLayer;
            m_status = null;

            Texture2D diffuseTexture = m_terrainLayer != null ? m_terrainLayer.diffuseTexture : null;
            if (diffuseTexture != null && IsGeneratedTextureForLayer(diffuseTexture, m_terrainLayer))
            {
                TryLoadGeneratedColor(diffuseTexture, ref m_color);
            }

            Repaint();
        }

        private static void DrawDiffuseTextureHint(Texture2D diffuseTexture, TerrainLayer terrainLayer)
        {
            if (diffuseTexture == null)
            {
                EditorGUILayout.HelpBox("No diffuse texture is assigned. A new generated texture will be created.", MessageType.Info);
                return;
            }

            if (IsGeneratedTextureForLayer(diffuseTexture, terrainLayer))
            {
                EditorGUILayout.HelpBox("The assigned diffuse texture was generated by this tool for this layer. It will be updated in place.", MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox("The assigned diffuse texture was not generated by this tool for this layer. A new generated texture will be created and assigned.", MessageType.Info);
        }

        private void GenerateAndAssign(TerrainLayer terrainLayer, string terrainLayerPath, string texturePath)
        {
            try
            {
                WriteSolidColorTexture(texturePath, m_color);
                AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate);
                ConfigureImporter(texturePath, terrainLayer);

                Texture2D generatedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (generatedTexture == null)
                {
                    m_status = $"Could not load generated texture at '{texturePath}'.";
                    return;
                }

                AddGeneratedLabel(generatedTexture);

                Undo.RecordObject(terrainLayer, "Assign Generated Diffuse Texture");
                terrainLayer.diffuseTexture = generatedTexture;
                EditorUtility.SetDirty(terrainLayer);
                AssetDatabase.SaveAssets();

                m_status = $"Assigned generated texture '{texturePath}' to '{Path.GetFileNameWithoutExtension(terrainLayerPath)}'.";
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                m_status = e.Message;
            }
        }

        private static string GetTargetTexturePath(TerrainLayer terrainLayer, string terrainLayerPath)
        {
            Texture2D diffuseTexture = terrainLayer.diffuseTexture;
            if (diffuseTexture != null && IsGeneratedTextureForLayer(diffuseTexture, terrainLayer))
            {
                string existingPath = AssetDatabase.GetAssetPath(diffuseTexture);
                if (!string.IsNullOrEmpty(existingPath))
                {
                    return existingPath;
                }
            }

            string directory = Path.GetDirectoryName(terrainLayerPath);
            string fileName = SanitizeFileName(terrainLayer.name + GENERATED_FILE_SUFFIX) + ".png";
            string defaultPath = ToAssetPath(Path.Combine(directory, fileName));

            if (!AssetExists(defaultPath) || IsGeneratedTextureForLayer(defaultPath, terrainLayer))
            {
                return defaultPath;
            }

            return GenerateUniqueTexturePath(defaultPath);
        }

        private static string GenerateUniqueTexturePath(string basePath)
        {
            string directory = ToAssetPath(Path.GetDirectoryName(basePath));
            string fileName = Path.GetFileNameWithoutExtension(basePath);
            string extension = Path.GetExtension(basePath);
            int index = 1;

            string path;
            do
            {
                path = ToAssetPath(Path.Combine(directory, $"{fileName} {index}{extension}"));
                index += 1;
            }
            while (AssetExists(path));

            return path;
        }

        private static bool AssetExists(string assetPath)
        {
            return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null || File.Exists(GetFullPath(assetPath));
        }

        private static void WriteSolidColorTexture(string assetPath, Color color)
        {
            Texture2D texture = new Texture2D(TEXTURE_SIZE, TEXTURE_SIZE, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[TEXTURE_SIZE * TEXTURE_SIZE];
            Color32 pixel = color;
            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = pixel;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            string fullPath = GetFullPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllBytes(fullPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }

        private static void ConfigureImporter(string texturePath, TerrainLayer terrainLayer)
        {
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.userData = CreateImporterUserData(terrainLayer);
            importer.SaveAndReimport();
        }

        private static void AddGeneratedLabel(Texture2D texture)
        {
            string[] labels = AssetDatabase.GetLabels(texture);
            for (int i = 0; i < labels.Length; ++i)
            {
                if (labels[i] == GENERATED_LABEL)
                {
                    return;
                }
            }

            Array.Resize(ref labels, labels.Length + 1);
            labels[labels.Length - 1] = GENERATED_LABEL;
            AssetDatabase.SetLabels(texture, labels);
        }

        private static bool IsGeneratedTextureForLayer(Texture2D texture, TerrainLayer terrainLayer)
        {
            if (texture == null || terrainLayer == null)
            {
                return false;
            }

            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return IsGeneratedTextureForLayer(path, terrainLayer);
        }

        private static bool IsGeneratedTextureForLayer(string texturePath, TerrainLayer terrainLayer)
        {
            if (terrainLayer == null || !string.Equals(Path.GetExtension(texturePath), ".png", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null)
            {
                return false;
            }

            string userData = importer.userData;
            string layerGuid = GetLayerGuid(terrainLayer);
            return !string.IsNullOrEmpty(userData) &&
                !string.IsNullOrEmpty(layerGuid) &&
                userData.Contains(GENERATED_USER_DATA_MARKER) &&
                userData.Contains(LAYER_GUID_PREFIX + layerGuid);
        }

        private static string CreateImporterUserData(TerrainLayer terrainLayer)
        {
            return $"{GENERATED_USER_DATA_MARKER}\n{LAYER_GUID_PREFIX}{GetLayerGuid(terrainLayer)}";
        }

        private static string GetLayerGuid(TerrainLayer terrainLayer)
        {
            string terrainLayerPath = AssetDatabase.GetAssetPath(terrainLayer);
            return AssetDatabase.AssetPathToGUID(terrainLayerPath);
        }

        private static string SanitizeFileName(string fileName)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(fileName.Length);
            for (int i = 0; i < fileName.Length; ++i)
            {
                char c = fileName[i];
                builder.Append(Array.IndexOf(invalidChars, c) >= 0 ? '_' : c);
            }

            string sanitized = builder.ToString().Trim();
            return string.IsNullOrEmpty(sanitized) ? "TerrainLayer" : sanitized;
        }

        private static string ToAssetPath(string path)
        {
            return path.Replace("\\", "/");
        }

        private static void TryLoadGeneratedColor(Texture2D texture, ref Color color)
        {
            try
            {
                color = texture.GetPixel(0, 0);
            }
            catch (UnityException)
            {
            }
        }

        private static string GetFullPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath);
        }
    }

    [InitializeOnLoad]
    internal static class TerrainLayerDiffuseColorHeaderButton
    {
        private static readonly GUIContent OPEN_WINDOW = new GUIContent("Open Prototype Color Tool");
        private static readonly GUIContent TITLE = new GUIContent("Prototype terrain color fast");
        private static readonly GUIContent DESCRIPTION = new GUIContent("Generate a solid diffuse swatch for this layer, tune the palette in seconds, then swap to full texture sets once the terrain reads well.");

        static TerrainLayerDiffuseColorHeaderButton()
        {
            Editor.finishedDefaultHeaderGUI -= OnFinishedDefaultHeaderGUI;
            Editor.finishedDefaultHeaderGUI += OnFinishedDefaultHeaderGUI;
        }

        private static void OnFinishedDefaultHeaderGUI(Editor editor)
        {
            if (editor == null || editor.targets == null || editor.targets.Length != 1)
            {
                return;
            }

            TerrainLayer terrainLayer = editor.target as TerrainLayer;
            if (terrainLayer == null || !AssetDatabase.Contains(terrainLayer))
            {
                return;
            }

            GUILayout.Space(2);

            using (new EditorGUILayout.VerticalScope(Styles.container))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    Texture icon = EditorGUIUtility.ObjectContent(terrainLayer, typeof(TerrainLayer)).image;
                    if (icon != null)
                    {
                        GUILayout.Label(icon, Styles.icon, GUILayout.Width(32), GUILayout.Height(32));
                    }

                    using (new EditorGUILayout.VerticalScope())
                    {
                        GUILayout.Label(TITLE, Styles.title);
                        GUILayout.Label(DESCRIPTION, Styles.description);
                    }
                }

                GUILayout.Space(4);
                if (GUILayout.Button(OPEN_WINDOW, GUILayout.Height(18)))
                {
                    TerrainLayerDiffuseColorWindow.ShowWindow(terrainLayer);
                }
            }
        }

        private static class Styles
        {
            private static GUIStyle s_container;
            private static GUIStyle s_title;
            private static GUIStyle s_description;
            private static GUIStyle s_icon;
            private static GUIStyle s_button;

            public static GUIStyle container
            {
                get
                {
                    if (s_container == null)
                    {
                        s_container = new GUIStyle(EditorStyles.helpBox);
                        s_container.margin = new RectOffset(4, 4, 4, 4);
                        s_container.padding = new RectOffset(8, 8, 8, 8);
                    }
                    return s_container;
                }
            }

            public static GUIStyle title
            {
                get
                {
                    if (s_title == null)
                    {
                        s_title = new GUIStyle(EditorStyles.boldLabel);
                        s_title.wordWrap = true;
                    }
                    return s_title;
                }
            }

            public static GUIStyle description
            {
                get
                {
                    if (s_description == null)
                    {
                        s_description = new GUIStyle(EditorStyles.miniLabel);
                        s_description.wordWrap = true;
                        s_description.richText = false;
                    }
                    s_description.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.75f, 0.75f, 0.75f) : new Color(0.25f, 0.25f, 0.25f);
                    return s_description;
                }
            }

            public static GUIStyle icon
            {
                get
                {
                    if (s_icon == null)
                    {
                        s_icon = new GUIStyle(GUIStyle.none);
                        s_icon.alignment = TextAnchor.UpperCenter;
                        s_icon.margin = new RectOffset(0, 6, 2, 0);
                    }
                    return s_icon;
                }
            }

            public static GUIStyle button
            {
                get
                {
                    if (s_button == null)
                    {
                        s_button = new GUIStyle(EditorStyles.iconButton);
                        //s_button.fontStyle = FontStyle.Bold;
                        //s_button.padding = new RectOffset(8, 8, 4, 4);
                    }
                    return s_button;
                }
            }
        }
    }
}
#endif
