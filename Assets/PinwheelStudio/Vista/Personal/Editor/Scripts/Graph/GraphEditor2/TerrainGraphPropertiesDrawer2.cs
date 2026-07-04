#if VISTA
using Pinwheel.Vista.Graph;
using Pinwheel.VistaEditor.UIElements;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using TextureDisplayMode = Pinwheel.VistaEditor.EditorSettings.GraphEditorSettings.TextureDisplayMode;
using ViewportGradientOptions = Pinwheel.VistaEditor.EditorSettings.GraphEditorSettings.ViewportGradientOptions;

namespace Pinwheel.VistaEditor.Graph
{
    public class TerrainGraphPropertiesDrawer2 : IGraphPropertiesDrawer
    {
        private static readonly string PREVIEW_CONFIGS_HEADER = "Preview Configs";
        private static readonly GUIContent PREVIEW_CONFIGS_INFO = new GUIContent("These values are kept in sync with the graph editor's host (e.g: Local Procedural Biome, Real World Biome, etc.)");

        private static readonly string VIEWPORTS_HEADER = "Viewports";
        private static readonly GUIContent GRADIENT_MODE = new GUIContent("Gradient Mode", "Generated textures/masks will be displayed using this gradient for better visualization");
        private static readonly GUIContent CUSTOM_GRADIENT_TEXTURE = new GUIContent("Custom Gradient Texture", "Provide your horizontal gradient texture here for 3D/2D map visualization");
        private static readonly string GRADIENT_WARNING = "Please provide a gradient texture";

        private static readonly string VIEWPORT_3D_HEADER = "3D Viewport";
        private static readonly GUIContent TERRAIN_VIS_QUALITY = new GUIContent("Terrain Quality", "The quality of terrain rendering in 3d viewport");
        private static readonly GUIContent SHOW_GRID = new GUIContent("Show Grid", "Render the grid in the 3d viewport");
        private static readonly GUIContent SHOW_WATER_LEVEL = new GUIContent("Show Water Level", "Render the water plane in the 3d viewport");
        private static readonly GUIContent WATER_LEVEL = new GUIContent("Water Level", "Altitude of the water line");
        private static readonly GUIContent TEXTURE_DISPLAY_MODE = new GUIContent("Default Texture Display", "How to visualize the generated texture when you select a node");

        private static readonly string FILE_EXPORT_HEADER = "File Exports";
        private static readonly GUIContent FILE_EXPORT_DIRECTORY = new GUIContent("Directory");

        private static readonly string TROUBLESHOOTING_HEADER = "Troubleshooting";
        private static readonly GUIContent ENABLE_TROUBLESHOOTING_MODE = new GUIContent("Enable", "Enable trouble shooting mode, this will perform logging on graph execution and other tasks to provide useful insight for debugging. No personal info recorded.");
        private static readonly GUIContent DONT_EXECUTE_ON_SELECTION = new GUIContent("Don't Execute Graph", "Turn this on will prevent it from running the graph when selecting a node. Only use when there is problem with a node and you need to delete it from the graph.");

        private static readonly string GRAPH_EDITOR_UI_HEADER = "Graph Editor UI";
        private static readonly GUIContent GRAPH_EDITOR_UI_VERSION = new GUIContent("Version", "UI version for the graph editor");
        private static readonly string UI_VERSION_WARNING = "Graph Editor UI V1 remains for compatibility purpose and will be removed soon, it's recommended to go with V2.";

        public GraphEditorBase editor { get; set; }

        public void OnDrawProperties(GraphAsset graph)
        {
            TerrainGraph instance = graph as TerrainGraph;
            const string PREVIEW_CONFIGS_FOLDOUT_KEY = "vista-graph-editor-preview-configs";
            bool previewConfigsExpanded = SessionState.GetBool(PREVIEW_CONFIGS_FOLDOUT_KEY, true);
            previewConfigsExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(previewConfigsExpanded, PREVIEW_CONFIGS_HEADER);
            if (previewConfigsExpanded)
            {
                EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
                EditorGUI.BeginChangeCheck();
                GUI.enabled = editor.externalInputProvider == null;
                TerrainGenerationConfigs debugConfigs = EditorCommon.TerrainGenConfigField(instance.debugConfigs);
                GUI.enabled = true;
                if (editor.externalInputProvider != null)
                {
                    EditorGUILayout.HelpBox(PREVIEW_CONFIGS_INFO, true);
                }
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(instance, "Modify debug configs");
                    EditorUtility.SetDirty(instance);
                    instance.debugConfigs = debugConfigs;
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            SessionState.SetBool(PREVIEW_CONFIGS_FOLDOUT_KEY, previewConfigsExpanded);

            EditorSettings editorSettings = EditorSettings.Get();
            EditorSettings.GraphEditorSettings graphEditorSettings = editorSettings.graphEditorSettings;
            const string VIEWPORTS_FOLDOUT_KEY = "vista-graph-editor-viewports";
            bool viewportsExpanded = SessionState.GetBool(VIEWPORTS_FOLDOUT_KEY, true);
            viewportsExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(viewportsExpanded, VIEWPORTS_HEADER);
            if (viewportsExpanded)
            {
                EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
                EditorGUI.BeginChangeCheck();
                ViewportGradientOptions viewportGradient = (ViewportGradientOptions)EditorGUILayout.EnumPopup(GRADIENT_MODE, graphEditorSettings.viewportGradient);
                Texture2D customGradient = graphEditorSettings.customViewportGradient;
                if (viewportGradient == ViewportGradientOptions.Custom)
                {
                    customGradient = EditorCommon.InlineTexture2DField(CUSTOM_GRADIENT_TEXTURE, graphEditorSettings.customViewportGradient);
                    if (customGradient == null)
                    {
                        EditorCommon.DrawWarning(GRADIENT_WARNING, true);
                    }
                }

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(editorSettings, "Modify Editor Settings");
                    graphEditorSettings.viewportGradient = viewportGradient;
                    graphEditorSettings.customViewportGradient = customGradient;
                    EditorUtility.SetDirty(editorSettings);
                    TerrainGraphViewport2d viewport2d = editor.rootVisualElement.Q<TerrainGraphViewport2d>();
                    if (viewport2d != null)
                    {
                        viewport2d.RenderViewport();
                    }
                    TerrainGraphViewport3d viewport3d = editor.rootVisualElement.Q<TerrainGraphViewport3d>();
                    if (viewport3d != null)
                    {
                        viewport3d.RenderViewport();
                    }
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            SessionState.SetBool(VIEWPORTS_FOLDOUT_KEY, viewportsExpanded);

            const string VIEWPORT_3D_FOLDOUT_KEY = "vista-graph-editor-viewport-3d";
            bool viewport3dExpanded = SessionState.GetBool(VIEWPORT_3D_FOLDOUT_KEY, true);
            viewport3dExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(viewport3dExpanded, VIEWPORT_3D_HEADER);
            if (viewport3dExpanded)
            {
                EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
                EditorGUI.BeginChangeCheck();
                bool showGrid = EditorGUILayout.Toggle(SHOW_GRID, graphEditorSettings.showGrid);
                bool showWaterLevel = EditorGUILayout.Toggle(SHOW_WATER_LEVEL, graphEditorSettings.showWaterLevel);
                float waterLevel = EditorGUILayout.FloatField(WATER_LEVEL, graphEditorSettings.waterLevel);
                int terrainVisQuality = EditorGUILayout.IntSlider(TERRAIN_VIS_QUALITY, graphEditorSettings.terrainVisualizationQuality, EditorSettings.GraphEditorSettings.MIN_VIS_QUALITY, EditorSettings.GraphEditorSettings.MAX_VIS_QUALITY);
                TextureDisplayMode textureDisplayMode = (TextureDisplayMode)EditorGUILayout.EnumPopup(TEXTURE_DISPLAY_MODE, graphEditorSettings.defaultTextureDisplayMode);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(editorSettings, "Modify Editor Settings");
                    graphEditorSettings.showGrid = showGrid;
                    graphEditorSettings.showWaterLevel = showWaterLevel;
                    graphEditorSettings.waterLevel = waterLevel;
                    graphEditorSettings.terrainVisualizationQuality = terrainVisQuality;
                    graphEditorSettings.defaultTextureDisplayMode = textureDisplayMode;
                    EditorUtility.SetDirty(editorSettings);

                    TerrainGraphViewport3d viewport3d = editor.rootVisualElement.Q<TerrainGraphViewport3d>();
                    if (viewport3d != null)
                    {
                        viewport3d.UpdateToggleButtons();
                        viewport3d.RenderViewport();
                    }
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            SessionState.SetBool(VIEWPORT_3D_FOLDOUT_KEY, viewport3dExpanded);

            const string FILE_EXPORT_FOLDOUT_KEY = "vista-graph-editor-file-export";
            bool fileExportExpanded = SessionState.GetBool(FILE_EXPORT_FOLDOUT_KEY, true);
            fileExportExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(fileExportExpanded, FILE_EXPORT_HEADER);
            if (fileExportExpanded)
            {
                EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
                EditorGUI.BeginChangeCheck();
                string directory = EditorGUILayout.DelayedTextField(FILE_EXPORT_DIRECTORY, graphEditorSettings.fileExportDirectory);
                if (EditorGUI.EndChangeCheck())
                {
                    string relativeDirectory = FileUtil.GetProjectRelativePath(directory);
                    Undo.RecordObject(editorSettings, "Modify Editor Settings");
                    graphEditorSettings.fileExportDirectory = relativeDirectory;
                    EditorUtility.SetDirty(editorSettings);
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            SessionState.SetBool(FILE_EXPORT_FOLDOUT_KEY, fileExportExpanded);

            const string TROUBLESHOOTING_FOLDOUT_KEY = "vista-graph-editor-troubleshooting";
            bool troubleShootingExpanded = SessionState.GetBool(TROUBLESHOOTING_FOLDOUT_KEY, true);
            troubleShootingExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(troubleShootingExpanded, TROUBLESHOOTING_HEADER);
            if (troubleShootingExpanded)
            {
                EditorSettings.TroubleshootingSettings troubleshootingSettings = editorSettings.troubleshootingSettings;
                EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
                EditorGUI.BeginChangeCheck();
                bool enableTroubleshooting = EditorGUILayout.Toggle(ENABLE_TROUBLESHOOTING_MODE, troubleshootingSettings.enableTroubleshootingMode);
                bool dontExecuteGraphOnSelection = EditorGUILayout.Toggle(DONT_EXECUTE_ON_SELECTION, troubleshootingSettings.dontExecuteGraphOnSelection);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(editorSettings, "Modify Editor Settings");
                    troubleshootingSettings.enableTroubleshootingMode = enableTroubleshooting;
                    troubleshootingSettings.dontExecuteGraphOnSelection = dontExecuteGraphOnSelection;
                    EditorUtility.SetDirty(editorSettings);
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            SessionState.SetBool(TROUBLESHOOTING_FOLDOUT_KEY, troubleShootingExpanded);

            const string GRAPH_EDITOR_UI_FOLDOUT_KEY = "vista-graph-editor-ui-version";
            bool graphEditorUIExpanded = SessionState.GetBool(GRAPH_EDITOR_UI_FOLDOUT_KEY, false);
            graphEditorUIExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(graphEditorUIExpanded, GRAPH_EDITOR_UI_HEADER);
            if (graphEditorUIExpanded)
            {
                EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
                EditorGUI.BeginChangeCheck();
                EditorSettings.GraphEditorSettings.UIVersion version = (EditorSettings.GraphEditorSettings.UIVersion)EditorGUILayout.EnumPopup(GRAPH_EDITOR_UI_VERSION, graphEditorSettings.uiVersion);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(editorSettings, "Modify Editor Settings");
                    graphEditorSettings.uiVersion = version;
                    EditorUtility.SetDirty(editorSettings);

                    EditorUtility.DisplayDialog("Switching UI", "Please close and reopen the graph editor to apply this change!", "OK");
                }
                if (version == EditorSettings.GraphEditorSettings.UIVersion.V1)
                {
                    EditorCommon.DrawWarning(UI_VERSION_WARNING, true);
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            SessionState.SetBool(GRAPH_EDITOR_UI_FOLDOUT_KEY, graphEditorUIExpanded);
        }
    }
}
#endif
