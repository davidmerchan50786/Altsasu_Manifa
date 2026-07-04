// Assets/Scripts/Editor/ActivadorClipmapV3.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ACTIVADOR CLIPMAP V3 — crea el material y conecta el componente en escena
//
//  Menú: Tools/Alsasua/Mundo/🌍 Activar Clipmap V3 (terreno GPU)
//
//  Pasos que hace:
//  1. Comprueba que el ShaderGraph ClipmapV3_Terrain existe (si no, avisa)
//  2. Crea el material Assets/Materials/Terrain/M_ClipmapV3_Terrain.mat
//  3. Busca o crea un GameObject "ClipmapV3_Terreno" en escena
//  4. Añade ClipmapTerrenoV3 + CargadorTexturaHeightmapV3 si no están
//  5. Asigna el material al componente
//  6. Asigna el jugador (tag "Player") si hay uno en escena
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

public static class ActivadorClipmapV3
{
    const string SHADERGRAPH = "Assets/Shaders/ClipmapV3_Terrain.shadergraph";
    const string MATERIAL    = "Assets/Materials/Terrain/M_ClipmapV3_Terrain.mat";
    const string NOMBRE_GO   = "ClipmapV3_Terreno";

    [MenuItem("Tools/Alsasua/Mundo/🌍 Activar Clipmap V3 (terreno GPU)", priority = 5)]
    static void Activar()
    {
        // 1. ShaderGraph
        var shaderAsset = AssetDatabase.LoadAssetAtPath<Shader>(SHADERGRAPH);
        if (shaderAsset == null)
        {
            // ShaderGraph importado como Shader generado → buscar por nombre
            shaderAsset = Shader.Find("Alsasua/Terrain/ClipmapV3_Terrain");
        }
        if (shaderAsset == null)
        {
            EditorUtility.DisplayDialog("Clipmap V3",
                "No se encontró el ShaderGraph compilado.\n\n" +
                "Comprueba que Assets/Shaders/ClipmapV3_Terrain.shadergraph " +
                "se importó correctamente (mira la Console de Unity para errores).\n\n" +
                "Si hay errores de importación, revisa que el paquete Shader Graph está instalado.",
                "OK");
            return;
        }

        // 2. Material
        Directory.CreateDirectory(Path.GetDirectoryName(
            Path.Combine(Application.dataPath, "..", MATERIAL)));
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MATERIAL);
        if (mat == null)
        {
            mat = new Material(shaderAsset) { name = "M_ClipmapV3_Terrain" };
            AssetDatabase.CreateAsset(mat, MATERIAL);
            Debug.Log($"[ClipmapV3] Material creado: {MATERIAL}");
        }
        else
        {
            // Actualizar shader en caso de que haya cambiado
            mat.shader = shaderAsset;
            EditorUtility.SetDirty(mat);
        }

        // 3. GameObject en escena
        var go = GameObject.Find(NOMBRE_GO);
        if (go == null)
        {
            go = new GameObject(NOMBRE_GO);
            Undo.RegisterCreatedObjectUndo(go, "Crear ClipmapV3");
            Debug.Log($"[ClipmapV3] GameObject '{NOMBRE_GO}' creado en escena.");
        }

        // 4. Componentes
        bool cambios = false;
        var terreno = go.GetComponent<ClipmapTerrenoV3>();
        if (terreno == null)
        {
            Undo.AddComponent<MeshFilter>(go);
            Undo.AddComponent<MeshRenderer>(go);
            terreno = Undo.AddComponent<ClipmapTerrenoV3>(go);
            cambios = true;
        }

        var cargador = go.GetComponent<CargadorTexturaHeightmapV3>();
        if (cargador == null)
        {
            cargador = Undo.AddComponent<CargadorTexturaHeightmapV3>(go);
            cambios = true;
        }

        // 5. Asignar material al componente
        if (terreno.material != mat)
        {
            Undo.RecordObject(terreno, "Asignar material clipmap");
            terreno.material = mat;
            cambios = true;
        }

        // 6. Jugador
        var jugadorGO = GameObject.FindGameObjectWithTag("Player");
        if (jugadorGO != null && terreno.jugador != jugadorGO.transform)
        {
            Undo.RecordObject(terreno, "Asignar jugador clipmap");
            terreno.jugador = jugadorGO.transform;
            cambios = true;
        }

        if (cambios)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("Clipmap V3 ✅",
            $"Clipmap V3 activado.\n\n" +
            $"• Material: {MATERIAL}\n" +
            $"• GameObject: '{NOMBRE_GO}'\n" +
            $"• Jugador: {(jugadorGO != null ? jugadorGO.name : "no encontrado — asigna manualmente")}\n\n" +
            "Pulsa Play para ver el terreno desplazado por la GPU.\n" +
            "El clipmap sigue al jugador con snap de rejilla (sin swimming).",
            "Genial");

        // Seleccionar el GO para que el usuario lo vea en Inspector
        Selection.activeGameObject = go;
    }

    [MenuItem("Tools/Alsasua/Mundo/🌍 Activar Clipmap V3 (terreno GPU)", validate = true)]
    static bool ActivarValidar() => !Application.isPlaying;
}
#endif
