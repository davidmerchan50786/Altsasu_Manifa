// Assets/Scripts/Editor/BakeadorOrtofotoEditor.cs
// Tools → Alsasua → Ortofoto → 🗺 Aplicar Ortofoto al Terreno (bake)
// Tools → Alsasua → Ortofoto → 📸 Ver Estado Ortofoto

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public static class BakeadorOrtofotoEditor
{
    const string META_PATH  = "Assets/AlsasuaData/orto_tiles_meta.json";
    const string TILES_DIR  = "Assets/AlsasuaData/tiles/orto/";
    const string OUT_BAKE   = "Assets/AlsasuaData/ortofoto_bake.png";
    const int    BAKE_SIZE  = 8192;   // px de la textura combinada

    // ── Coordenadas del bounding box completo de la ortofoto (Unity XZ) ──
    // lat 42.888–42.912°N, lon -2.185 to -2.152°W
    const float WORLD_X_MIN =  596.3f;
    const float WORLD_X_MAX = 3116.1f;
    const float WORLD_Z_MIN = 7378.9f;
    const float WORLD_Z_MAX = 10051.4f;

    [System.Serializable]
    class TiloMeta
    {
        public string file;
        public float  ux_min, uz_min, ux_max, uz_max;
        public int    width_px, height_px;
    }

    // ── Estado ────────────────────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/Ortofoto/📸 Ver Estado Ortofoto", priority = 25)]
    public static void EstadoOrtofoto()
    {
        string metaFull  = Path.Combine(Application.dataPath.Replace("Assets",""), META_PATH);
        string bakeFull  = Path.Combine(Application.dataPath.Replace("Assets",""), OUT_BAKE);
        string tilesDir  = Path.Combine(Application.dataPath.Replace("Assets",""), TILES_DIR);

        bool metaOk  = File.Exists(metaFull);
        bool bakeOk  = File.Exists(bakeFull);
        int  numTiles = Directory.Exists(tilesDir)
            ? Directory.GetFiles(tilesDir, "*.jpg").Length : 0;

        string msg =
            $"=== ORTOFOTO IGN PNOA — ESTADO ===\n\n" +
            $"Metadatos:  {(metaOk ? $"✅ {META_PATH}" : "❌ No encontrado")}\n" +
            $"Teselas JPG:{(numTiles > 0 ? $"✅ {numTiles}/72 archivos" : "❌ No encontradas")}\n" +
            $"Bake 8K:    {(bakeOk ? $"✅ {new FileInfo(bakeFull).Length/1024}KB — {OUT_BAKE}" : "❌ No generado aún")}\n\n" +
            $"Cobertura: 42.888–42.912°N, 2.185–2.152°W\n" +
            $"Resolución: 25cm/pixel por tesela\n" +
            $"Total: {numTiles} teselas × 1320×1320px = ~{numTiles * 22}MB JPG\n\n" +
            $"Acciones disponibles:\n" +
            $"• Tools → Ortofoto → 🗺 Aplicar: genera bake 8K + aplica al terreno\n" +
            $"• AplicadorOrtofoto en runtime: streaming dinámico de teselas\n\n" +
            $"Fuente: IGN PNOA 2024 CC BY 4.0";

        Debug.Log(msg);
        EditorUtility.DisplayDialog("📸 Estado Ortofoto", msg, "OK");
    }

    // ── Bake ──────────────────────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/Ortofoto/🗺 Aplicar Ortofoto al Terreno (bake 8K)", priority = 26)]
    public static void BakearOrtofoto()
    {
        string metaFull = Path.Combine(Application.dataPath.Replace("Assets",""), META_PATH);
        if (!File.Exists(metaFull))
        {
            EditorUtility.DisplayDialog("🗺 Ortofoto",
                "No se encontró orto_tiles_meta.json\n\n" +
                "Ejecuta primero:\n  DescargarDatosCompletos.py",
                "OK");
            return;
        }

        bool ok = EditorUtility.DisplayDialog("🗺 Bake Ortofoto",
            $"Combinará las 72 teselas IGN PNOA en una textura {BAKE_SIZE}×{BAKE_SIZE}px\n\n" +
            $"Cobertura: 23.8km² del casco urbano de Alsasua\n" +
            $"Resolución resultante: ~30cm/pixel\n\n" +
            $"Guardará en: {OUT_BAKE}\n" +
            $"Tiempo estimado: 10-30 segundos\n\n" +
            $"¿Continuar?",
            "🗺 Bake", "Cancelar");

        if (!ok) return;

        EditorUtility.DisplayProgressBar("🗺 Ortofoto Bake", "Cargando metadatos...", 0f);

        try
        {
            var metas = CargarMetas(metaFull);
            if (metas.Count == 0)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error", "No se pudieron cargar los metadatos.", "OK");
                return;
            }

            EditorUtility.DisplayProgressBar("🗺 Ortofoto Bake", "Creando textura de salida...", 0.05f);

            // Textura de destino: fondo gris (sin datos)
            var bake = new Texture2D(BAKE_SIZE, BAKE_SIZE, TextureFormat.RGB24, false);
            var pixeles = new Color32[BAKE_SIZE * BAKE_SIZE];
            for (int i = 0; i < pixeles.Length; i++)
                pixeles[i] = new Color32(120, 120, 120, 255);
            bake.SetPixels32(pixeles);

            float worldW = WORLD_X_MAX - WORLD_X_MIN;
            float worldH = WORLD_Z_MAX - WORLD_Z_MIN;
            int   procesadas = 0;

            string tilesDir = Path.Combine(Application.dataPath.Replace("Assets",""), TILES_DIR);

            foreach (var m in metas)
            {
                float prog = 0.05f + (float)procesadas / metas.Count * 0.85f;
                EditorUtility.DisplayProgressBar("🗺 Ortofoto Bake",
                    $"Procesando {m.file} ({procesadas+1}/{metas.Count})...", prog);

                string tilePath = Path.Combine(tilesDir, m.file);
                if (!File.Exists(tilePath)) { procesadas++; continue; }

                byte[] bytes = File.ReadAllBytes(tilePath);
                var tileTex  = new Texture2D(m.width_px, m.height_px, TextureFormat.RGB24, false);
                tileTex.filterMode = FilterMode.Bilinear;

                if (!tileTex.LoadImage(bytes))
                { Object.DestroyImmediate(tileTex); procesadas++; continue; }

                // Posición de esta tesela en el bake
                int dstX0 = Mathf.RoundToInt((m.ux_min - WORLD_X_MIN) / worldW * BAKE_SIZE);
                int dstZ0 = Mathf.RoundToInt((m.uz_min - WORLD_Z_MIN) / worldH * BAKE_SIZE);
                int dstW  = Mathf.RoundToInt((m.ux_max - m.ux_min)    / worldW * BAKE_SIZE);
                int dstH  = Mathf.RoundToInt((m.uz_max - m.uz_min)    / worldH * BAKE_SIZE);

                // Copiar tesela escalada al bake
                for (int py = 0; py < dstH; py++)
                for (int px = 0; px < dstW;  px++)
                {
                    float u = (float)px / dstW;
                    float v = (float)py / dstH;
                    Color c = tileTex.GetPixelBilinear(u, v);
                    int bx  = dstX0 + px;
                    int by  = dstZ0 + py;
                    if (bx >= 0 && bx < BAKE_SIZE && by >= 0 && by < BAKE_SIZE)
                        bake.SetPixel(bx, by, c);
                }

                Object.DestroyImmediate(tileTex);
                procesadas++;
            }

            EditorUtility.DisplayProgressBar("🗺 Ortofoto Bake", "Guardando PNG...", 0.92f);

            bake.Apply();
            byte[] pngBytes = bake.EncodeToPNG();
            Object.DestroyImmediate(bake);

            string outFull = Path.Combine(Application.dataPath.Replace("Assets",""), OUT_BAKE);
            File.WriteAllBytes(outFull, pngBytes);
            AssetDatabase.Refresh();

            EditorUtility.DisplayProgressBar("🗺 Ortofoto Bake", "Aplicando al terreno...", 0.96f);
            AplicarAlTerreno(OUT_BAKE);

            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("✅ Ortofoto Bake",
                $"Bake completado:\n\n" +
                $"• {procesadas}/{metas.Count} teselas procesadas\n" +
                $"• Textura: {BAKE_SIZE}×{BAKE_SIZE}px\n" +
                $"• Guardada en: {OUT_BAKE}\n" +
                $"• Aplicada como base del terreno\n\n" +
                $"El terreno ahora muestra la ortofoto real del IGN PNOA.",
                "OK");
        }
        catch (System.Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"[OrtofotoBake] Error: {e.Message}\n{e.StackTrace}");
            EditorUtility.DisplayDialog("Error Bake",
                $"Error al generar el bake:\n{e.Message}", "OK");
        }
    }

    // ── Aplicar al terreno ────────────────────────────────────────────────

    static void AplicarAlTerreno(string texturePath)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            AlsasuaLogger.Warn("OrtofotoBake",
                "No hay Terrain.activeTerrain — el bake se guardó pero no se aplicó automáticamente");
            return;
        }

        // Importar la textura con ajustes correctos
        TextureImporter importer = (TextureImporter)
            AssetImporter.GetAtPath(texturePath);
        if (importer != null)
        {
            importer.isReadable         = true;
            importer.textureType        = TextureImporterType.Default;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.maxTextureSize     = 8192;
            importer.sRGBTexture        = true;
            importer.SaveAndReimport();
        }

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (tex == null)
        {
            AlsasuaLogger.Warn("OrtofotoBake", "No se pudo cargar el bake tras reimportar");
            return;
        }

        // Crear TerrainLayer con la ortofoto
        var layerPath = "Assets/AlsasuaData/OrtofotoLayer.terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
        if (layer == null)
        {
            layer = new TerrainLayer();
            AssetDatabase.CreateAsset(layer, layerPath);
        }

        layer.diffuseTexture = tex;
        // El tile size cubre todo el terreno en una sola repetición
        float terrainW = terrain.terrainData.size.x;
        float terrainH = terrain.terrainData.size.z;
        layer.tileSize   = new Vector2(terrainW, terrainH);
        layer.tileOffset = Vector2.zero;
        EditorUtility.SetDirty(layer);

        // Asignar como primer layer del terreno
        var layers = terrain.terrainData.terrainLayers;
        if (layers == null || layers.Length == 0)
        {
            terrain.terrainData.terrainLayers = new[] { layer };
        }
        else
        {
            layers[0] = layer;
            terrain.terrainData.terrainLayers = layers;
        }

        EditorUtility.SetDirty(terrain.terrainData);
        EditorUtility.SetDirty(terrain);
        AssetDatabase.SaveAssets();

        AlsasuaLogger.Info("OrtofotoBake",
            $"Ortofoto aplicada al terreno como TerrainLayer ({terrainW}×{terrainH}m)");
    }

    // ── Carga de metadatos ────────────────────────────────────────────────

    static List<TiloMeta> CargarMetas(string fullPath)
    {
        try
        {
            string json = File.ReadAllText(fullPath);
            var arr     = JsonHelper.ParseArray<TiloMeta>(json);
            return new List<TiloMeta>(arr ?? System.Array.Empty<TiloMeta>());
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[OrtofotoBake] Parse error: {e.Message}");
            return new List<TiloMeta>();
        }
    }
}
#endif
