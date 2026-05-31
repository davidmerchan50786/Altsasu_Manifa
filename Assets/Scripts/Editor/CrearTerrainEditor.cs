#if UNITY_EDITOR
// Assets/Scripts/Editor/CrearTerrainEditor.cs
// Crea el terrain de Alsasua EN EL EDITOR (persistente en escena).
// Menú: Altsasu GTA → Territorio Real → ★ Crear Terrain + Ortofoto (Editor)

using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class CrearTerrainEditor
{
    const string DEM_PATH    = "Assets/AlsasuaData/dem_unity_1025.raw";
    const string ORTO_PATH   = "Assets/AlsasuaData/ortofoto_alsasua_REAL.png";
    const float  TER_W       = 5000f;
    const float  TER_H       = 900f;
    const float  TER_L       = 18000f;
    const int    DEM_RES     = 1025;

    // MENÚ-LEGACY: [MenuItem("Altsasu GTA/Territorio Real/★ Crear Terrain + Ortofoto (Editor)", false, 1)]
    public static void CrearTodo()
    {
        try {
        // 1. Borrar terrain existente para evitar duplicados
        var existing = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        foreach (var t in existing)
        {
            string nombreAnterior = t.name; // guardar ANTES de destruir
            Undo.DestroyObjectImmediate(t.gameObject);
            Debug.Log($"[Terrain] Terrain anterior eliminado: {nombreAnterior}");
        }

        // 2. Crear TerrainData desde DEM
        EditorUtility.DisplayProgressBar("Creando Terrain", "Cargando DEM...", 0.1f);
        var td = CrearTerrainData();
        if (td == null) { EditorUtility.ClearProgressBar(); return; }

        // 3. Crear GameObject Terrain en escena
        EditorUtility.DisplayProgressBar("Creando Terrain", "Creando terrain en escena...", 0.4f);
        var go = Terrain.CreateTerrainGameObject(td);
        go.name = "Terrain_Alsasua";
        go.transform.position = Vector3.zero;
        go.isStatic = true;
        Undo.RegisterCreatedObjectUndo(go, "Crear Terrain Alsasua");

        var terrain = go.GetComponent<Terrain>();
        terrain.heightmapPixelError = 8f;
        terrain.drawInstanced       = true;

        // 4. Aplicar ortofoto como layer
        EditorUtility.DisplayProgressBar("Creando Terrain", "Aplicando ortofoto PNOA...", 0.6f);
        AplicarOrtofoto(terrain);

        // 5. Guardar TerrainData como asset (borrar si ya existe)
        string tdPath = "Assets/AlsasuaData/TerrainData_Alsasua.asset";
        if (AssetDatabase.LoadAssetAtPath<TerrainData>(tdPath) != null)
            AssetDatabase.DeleteAsset(tdPath);
        AssetDatabase.CreateAsset(td, tdPath);
        AssetDatabase.SaveAssets();

        // 6. Marcar escena como modificada y guardar
        EditorUtility.DisplayProgressBar("Creando Terrain", "Guardando escena...", 0.9f);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        EditorUtility.ClearProgressBar();

        EditorUtility.DisplayDialog("✅ Terrain creado",
            "Terrain de Alsasua creado en escena:\n\n" +
            "• DEM LiDAR 1025×1025\n" +
            "• 5km × 18km escala real\n" +
            "• Ortofoto PNOA aplicada\n\n" +
            "Ahora pulsa ▶ Play — el terreno persiste en escena.",
            "¡Listo!");

        Debug.Log("[Terrain] ✓ Terrain_Alsasua creado y guardado en escena.");
        } catch (System.Exception e) {
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("❌ Error", e.Message + "\n\n" + e.StackTrace.Split('\n')[0], "OK");
            Debug.LogError("[Terrain] Error: " + e);
        }
    }

    static TerrainData CrearTerrainData()
    {
        string demAbs = Path.Combine(
            Application.dataPath.Replace("Assets", ""), DEM_PATH);

        if (!File.Exists(demAbs))
        {
            EditorUtility.DisplayDialog("Error DEM",
                $"No se encontró el DEM en:\n{demAbs}\n\n" +
                "Comprueba Assets/AlsasuaData/dem_unity_1025.raw", "OK");
            return null;
        }

        var td = new TerrainData();
        td.heightmapResolution = DEM_RES;
        td.size = new Vector3(TER_W, TER_H, TER_L);

        byte[] raw    = File.ReadAllBytes(demAbs);
        var heights   = new float[DEM_RES, DEM_RES];
        for (int r = 0; r < DEM_RES; r++)
            for (int c = 0; c < DEM_RES; c++)
            {
                int idx = (r * DEM_RES + c) * 2;
                heights[r, c] = System.BitConverter.ToUInt16(raw, idx) / 65535f;
            }
        td.SetHeights(0, 0, heights);

        Debug.Log($"[Terrain] DEM cargado: {DEM_RES}×{DEM_RES}, tamaño {TER_W}×{TER_L}m");
        return td;
    }

    static void AplicarOrtofoto(Terrain terrain)
    {
        // Cargar ortofoto
        var ortofoto = AssetDatabase.LoadAssetAtPath<Texture2D>(ORTO_PATH);
        if (ortofoto == null)
        {
            Debug.LogWarning($"[Terrain] Ortofoto no encontrada en {ORTO_PATH} — usando textura de color");
            AplicarColorBase(terrain);
            return;
        }

        // Configurar importación — necesita Read/Write para usarla como textura de terreno
        string ortoAbs = ORTO_PATH;
        var importer = AssetImporter.GetAtPath(ortoAbs) as TextureImporter;
        if (importer != null && !importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        // Crear TerrainLayer con ortofoto cubriendo todo el terrain (1:1)
        var layer = new TerrainLayer();
        layer.diffuseTexture = ortofoto;
        layer.tileSize   = new Vector2(TER_W, TER_L); // cubre exactamente el terrain
        layer.tileOffset = Vector2.zero;
        layer.metallic   = 0f;
        layer.smoothness = 0.05f;

        string layerPath = "Assets/AlsasuaData/Layer_Ortofoto_Real.terrainlayer";
        if (AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath) != null)
            AssetDatabase.DeleteAsset(layerPath);
        AssetDatabase.CreateAsset(layer, layerPath);

        // Capa de roca para pendientes
        var layerRoca = new TerrainLayer();
        layerRoca.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
            "Assets/AlsasuaData/Textures/Tex_Rock.png");
        if (layerRoca.diffuseTexture == null)
        {
            // Fallback: color gris roca
            var texRoca = new Texture2D(4,4);
            var px = new Color[16];
            for(int i=0;i<16;i++) px[i] = new Color(0.5f,0.48f,0.44f);
            texRoca.SetPixels(px); texRoca.Apply();
            layerRoca.diffuseTexture = texRoca;
        }
        layerRoca.tileSize = new Vector2(5f, 5f);
        string rocaPath = "Assets/AlsasuaData/Layer_Roca_Runtime.terrainlayer";
        if (AssetDatabase.LoadAssetAtPath<TerrainLayer>(rocaPath) != null)
            AssetDatabase.DeleteAsset(rocaPath);
        AssetDatabase.CreateAsset(layerRoca, rocaPath);

        terrain.terrainData.terrainLayers = new TerrainLayer[] { layer, layerRoca };

        // Pintar splatmap: ortofoto base, roca en pendientes >35°
        PintarSplatmap(terrain);

        Debug.Log("[Terrain] ✓ Ortofoto PNOA aplicada como terrain layer.");
    }

    static void AplicarColorBase(Terrain terrain)
    {
        var layer = new TerrainLayer();
        var tex   = new Texture2D(4, 4);
        var px    = new Color[16];
        for (int i = 0; i < 16; i++) px[i] = new Color(0.38f, 0.55f, 0.22f);
        tex.SetPixels(px); tex.Apply();
        layer.diffuseTexture = tex;
        layer.tileSize       = new Vector2(8f, 8f);
        terrain.terrainData.terrainLayers = new TerrainLayer[] { layer };

        int res = terrain.terrainData.alphamapResolution;
        float[,,] mapa = new float[res, res, 1];
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
                mapa[z, x, 0] = 1f;
        terrain.terrainData.SetAlphamaps(0, 0, mapa);
    }

    static void PintarSplatmap(Terrain terrain)
    {
        var td  = terrain.terrainData;
        int res = td.alphamapResolution;
        int num = td.terrainLayers.Length;
        float[,,] mapa = new float[res, res, num];

        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                float slope = td.GetSteepness((float)x/res, (float)z/res);
                float wRoca = Mathf.Clamp01((slope - 30f) / 25f);
                float wOrto = 1f - wRoca;

                mapa[z, x, 0] = wOrto;
                if (num > 1) mapa[z, x, 1] = wRoca;
            }
            if (z % 100 == 0)
                EditorUtility.DisplayProgressBar("Pintando splatmap",
                    $"Fila {z}/{res}", 0.6f + 0.3f * (float)z/res);
        }
        td.SetAlphamaps(0, 0, mapa);
        EditorUtility.ClearProgressBar();
    }
}
#endif
