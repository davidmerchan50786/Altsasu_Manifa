#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public static class CrearTerrainLayers
{
    const string RUTA = "Assets/AlsasuaData";

    [MenuItem("Altsasu GTA/Utilidades/Crear Terrain Layers (texturas base)", false, 320)]
    public static void CrearTodo()
    {
        // Cargar la ortofoto PNOA real (cubre todo el terrain 5x18km)
        var ortofoto = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/AlsasuaData/ortofoto_alsasua_REAL.png");

        // Texturas de roca y nieve para las montañas
        var texRoca  = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/AlsasuaData/Textures/Tex_Rock.png")
                    ?? AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/AlsasuaData/Textures/Tex_Alpine.png");
        var texHierba= AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/AlsasuaData/Textures/Tex_Grass.png");

        // Capa 0 — Ortofoto PNOA: cubre el terrain exactamente 1 vez (5000x18000m)
        CrearLayerConTextura("Layer_Ortofoto",  ortofoto,  5000f, 18000f, new Color(0.42f, 0.52f, 0.32f));

        // Capa 1 — Hierba para prados
        CrearLayerConTextura("Layer_Grass",     texHierba, 6f,   6f,     new Color(0.35f, 0.52f, 0.22f));

        // Capa 2 — Roca para pendientes y cumbres
        CrearLayerConTextura("Layer_Rock",      texRoca,   4f,   4f,     new Color(0.55f, 0.52f, 0.46f));

        // Asignar al terrain activo y pintar el splatmap
        var terrain = Object.FindFirstObjectByType<Terrain>();
        if (terrain != null) AsignarAlTerrain(terrain);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("✓ Terrain Layers con ortofoto PNOA real asignadas.");
        EditorUtility.DisplayDialog("✅ Terrain Layers",
            ortofoto != null
                ? "✓ Ortofoto PNOA real aplicada\n✓ Capas de roca e hierba añadidas"
                : "⚠ Ortofoto no encontrada — se usaron colores base\nComprueba Assets/AlsasuaData/ortofoto_alsasua_REAL.png",
            "OK");
    }

    static void CrearLayerConTextura(string nombre, Texture2D tex, float tileSizeX, float tileSizeZ, Color colorFallback)
    {
        string path = $"{RUTA}/{nombre}.terrainlayer";
        if (AssetDatabase.LoadAssetAtPath<TerrainLayer>(path) != null) return;

        Texture2D texFinal = tex;
        if (texFinal == null)
        {
            // Fallback: textura sólida del color
            texFinal = new Texture2D(64, 64, TextureFormat.RGB24, false);
            var pixels = new Color[64 * 64];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = colorFallback;
            texFinal.SetPixels(pixels);
            texFinal.Apply();
            string texPath = $"{RUTA}/{nombre}_tex.png";
            System.IO.File.WriteAllBytes(
                System.IO.Path.Combine(Application.dataPath, "..", texPath),
                texFinal.EncodeToPNG());
            AssetDatabase.ImportAsset(texPath);
            texFinal = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        }

        var layer = new TerrainLayer();
        layer.diffuseTexture = texFinal;
        layer.tileSize       = new Vector2(tileSizeX, tileSizeZ);
        layer.tileOffset     = Vector2.zero;
        AssetDatabase.CreateAsset(layer, path);
    }

    static void CrearLayer(string nombre, Color color, float tileSizeX, float tileSizeZ)
    {
        string path = $"{RUTA}/{nombre}.terrainlayer";
        if (AssetDatabase.LoadAssetAtPath<TerrainLayer>(path) != null) return;

        // Crear textura sólida 64x64
        var tex = new Texture2D(64, 64, TextureFormat.RGB24, false);
        var pixels = new Color[64 * 64];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        tex.SetPixels(pixels);
        tex.Apply();

        string texPath = $"{RUTA}/{nombre}_tex.png";
        System.IO.File.WriteAllBytes(
            System.IO.Path.Combine(Application.dataPath, "..", texPath),
            tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(texPath);

        var importedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

        var layer = new TerrainLayer();
        layer.diffuseTexture = importedTex;
        layer.tileSize       = new Vector2(tileSizeX, tileSizeZ);
        layer.tileOffset     = Vector2.zero;

        AssetDatabase.CreateAsset(layer, path);
    }

    static void AsignarAlTerrain(Terrain terrain)
    {
        // Cargar solo las 3 capas que creamos
        string[] nombres = { "Layer_Ortofoto", "Layer_Grass", "Layer_Rock" };
        var capas = new System.Collections.Generic.List<TerrainLayer>();
        foreach (var n in nombres)
        {
            var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{RUTA}/{n}.terrainlayer");
            if (l != null) capas.Add(l);
        }
        if (capas.Count == 0) return;
        terrain.terrainData.terrainLayers = capas.ToArray();

        // Pintar splatmap: ortofoto base + roca en pendientes pronunciadas
        int res = terrain.terrainData.alphamapResolution;
        int num = capas.Count;
        float[,,] mapa = new float[res, res, num];

        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                float nx = (float)x / res;
                float nz = (float)z / res;
                float slope = terrain.terrainData.GetSteepness(nx, nz);
                float alt   = terrain.terrainData.GetHeight(x, z);

                if (num == 1)
                {
                    mapa[z, x, 0] = 1f;
                }
                else if (num == 2)
                {
                    // Ortofoto base, hierba encima en zonas planas
                    float wOrto  = Mathf.Clamp01(1f - slope / 20f);
                    float wHierba= 1f - wOrto;
                    float t = wOrto + wHierba; if (t == 0) t = 1;
                    mapa[z, x, 0] = wOrto  / t;
                    mapa[z, x, 1] = wHierba / t;
                }
                else // 3 capas: Ortofoto, Hierba, Roca
                {
                    float wRoca  = Mathf.Clamp01((slope - 30f) / 20f);
                    float wOrto  = (1f - wRoca) * 0.7f;
                    float wHierba= (1f - wRoca) * 0.3f;
                    float t = wOrto + wHierba + wRoca; if (t == 0) t = 1;
                    mapa[z, x, 0] = wOrto   / t;
                    mapa[z, x, 1] = wHierba / t;
                    mapa[z, x, 2] = wRoca   / t;
                }
            }
            if (z % 64 == 0) EditorUtility.DisplayProgressBar("Pintando splatmap...", $"{z}/{res}", (float)z/res);
        }
        EditorUtility.ClearProgressBar();
        terrain.terrainData.SetAlphamaps(0, 0, mapa);
        EditorUtility.SetDirty(terrain.terrainData);
    }
}
#endif
