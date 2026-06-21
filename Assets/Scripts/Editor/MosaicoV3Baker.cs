// Assets/Scripts/Editor/MosaicoV3Baker.cs
// ═══════════════════════════════════════════════════════════════════════════
//  MOSAICO V3 BAKER — Fase 4 del plan AAA (Docs/plan_render_aaa.md)
//
//  Genera 3 mallas de terreno estáticas (una por anillo) desde los RAW del
//  Mosaico V2. Cada malla es un "donut" que cubre sólo su anillo:
//    · Anillo 0 (urbano):    full 2400×2400m (sin agujero)
//    · Anillo 1 (valle):     7200×7200m con agujero 2400×2400m
//    · Anillo 2 (sierras): 14400×14400m con agujero 7200×7200m
//
//  Resultado: 3 draw calls de terreno vs 48+ Unity Terrain draw calls.
//  La collision sigue en los TerrainColliders (MosaicoV3Sistema los preserva).
//
//  IMPORTANTE: las mallas NO se actualizan en runtime con la posición del
//  jugador (eso sería el V3 completo con clipmap dinámico). Son ESTÁTICAS:
//  cubren el mosaico completo con la densidad adecuada a cada anillo.
//
//  DENSIDAD POR ANILLO:
//    · A0: grid 513×513 → vértice cada ~4.7m (área 2400m)  ~ 260k verts
//    · A1: grid 513×513 → vértice cada ~14m  (área 7200m)  ~ 260k verts
//    · A2: grid 257×257 → vértice cada ~56m  (área 14400m) ~  66k verts
//  Total: ~590k vértices, ~1.17M triángulos — subido a GPU una vez al bakear.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class MosaicoV3Baker
{
    const string DIR_OUT   = "Assets/MosaicoV3";
    const string DIR_RES   = "Assets/Resources/MosaicoV3";
    const string SO_PATH   = DIR_RES + "/MosaicoV3SO.asset";

    // Resolución de la rejilla por anillo (número de quads por eje = res-1)
    static readonly int[] GRID_RES = { 512, 512, 256 };

    [MenuItem("Tools/Alsasua/Mundo/🏔️ Hornear Mosaico V3 (terreno GPU)", priority = 34)]
    static void Hornear()
    {
        string manifestPath = CargadorMosaicoTerreno.RutaManifest();
        if (manifestPath == null)
        {
            EditorUtility.DisplayDialog("Hornear Terreno V3",
                "manifest_v2.json no encontrado. Genera el mosaico V2 primero.", "Vale");
            return;
        }

        MosaicoManifest man;
        try { man = MosaicoManifest.Cargar(manifestPath); }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("Hornear Terreno V3", $"Manifest ilegible: {ex.Message}", "Vale");
            return;
        }

        int nAnillos = man.anillos.Count;
        if (nAnillos > GRID_RES.Length)
        {
            Debug.LogError($"[V3Baker] El manifest tiene {nAnillos} anillos pero GRID_RES solo define {GRID_RES.Length}. Añade entradas a GRID_RES.");
            return;
        }
        if (!EditorUtility.DisplayDialog("Hornear Mosaico V3",
            $"Genera {nAnillos} mallas de terreno desde {man.tiles.Count} tiles RAW.\n" +
            $"Grid por anillo: {GRID_RES[0]}², {GRID_RES[1]}², {GRID_RES[2]}²\n" +
            "Puede tardar 1-3 minutos. ¿Continuar?",
            "Hornear", "Cancelar")) return;

        Directory.CreateDirectory(DIR_OUT);
        Directory.CreateDirectory(DIR_RES);
        AssetDatabase.Refresh();

        string dirTiles = Path.Combine(Application.dataPath, CargadorMosaicoTerreno.DIR_TILES_REL);
        float cx = (float)man.convencionHorizontal.OX;
        float cz = (float)man.convencionHorizontal.OZ;

        // ── Precargar todos los RAW en memoria ──────────────────────────
        var raws = new Dictionary<string, ushort[]>(man.tiles.Count);
        for (int ti = 0; ti < man.tiles.Count; ti++)
        {
            var td = man.tiles[ti];
            EditorUtility.DisplayProgressBar("Hornear V3", $"Cargando tile {ti + 1}/{man.tiles.Count}…",
                0.1f * (ti + 1) / man.tiles.Count);
            string ruta = Path.Combine(dirTiles, td.file);
            if (!File.Exists(ruta)) { Debug.LogWarning($"[V3Baker] Tile no encontrado: {ruta}"); continue; }
            byte[] raw = File.ReadAllBytes(ruta);
            int esperado = td.res * td.res * 2;
            if (raw.Length != esperado) { Debug.LogWarning($"[V3Baker] Tamaño inesperado: {td.file}"); continue; }
            var u16 = new ushort[td.res * td.res];
            for (int i = 0, j = 0; i < raw.Length; i += 2, j++)
                u16[j] = (ushort)(raw[i] | (raw[i + 1] << 8));
            raws[td.file] = u16;
        }

        // ── Generar malla por anillo ─────────────────────────────────────
        var mallas = new Mesh[nAnillos];
        float innerHalf = 0f;

        // Construir índice O(1) UNA vez para todos los anillos
        var tileIdx = BuildTileIndex(man, raws, cx, cz);

        AssetDatabase.StartAssetEditing();
        try
        {
            for (int ai = 0; ai < nAnillos; ai++)
            {
                var aDef = man.anillos[ai];
                int gridN = GRID_RES[Mathf.Min(ai, GRID_RES.Length - 1)];

                EditorUtility.DisplayProgressBar("Hornear V3",
                    $"Anillo {ai} ({aDef.halfExtent * 2:F0}m × {aDef.halfExtent * 2:F0}m, grid {gridN}²)…",
                    0.1f + 0.85f * ai / nAnillos);

                mallas[ai] = GenerarMallaAnillo(man, tileIdx, cx, cz, aDef, innerHalf, gridN, ai);
                string meshPath = $"{DIR_OUT}/terreno_anillo_{ai}.asset";
                AssetDatabase.CreateAsset(mallas[ai], AssetDatabase.GenerateUniqueAssetPath(meshPath));

                innerHalf = aDef.halfExtent;   // el siguiente anillo tiene este como agujero
            }
        }
        finally { AssetDatabase.StopAssetEditing(); EditorUtility.ClearProgressBar(); }

        // ── Crear / actualizar material HDRP/Lit ─────────────────────────
        var mat = CrearOActualizarMaterial();

        // ── Crear / actualizar MosaicoV3SO en Resources ─────────────────
        var so = AssetDatabase.LoadAssetAtPath<MosaicoV3SO>(SO_PATH);
        bool soNuevo = (so == null);
        if (soNuevo) so = ScriptableObject.CreateInstance<MosaicoV3SO>();

        so.mallasPorAnillo = mallas;
        so.material        = mat;
        so.halfExtents     = new float[nAnillos];
        for (int ai = 0; ai < nAnillos; ai++) so.halfExtents[ai] = man.anillos[ai].halfExtent;
        so.centroX = cx;
        so.centroZ = cz;

        if (soNuevo) AssetDatabase.CreateAsset(so, SO_PATH);
        else         EditorUtility.SetDirty(so);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int totalVerts = 0;
        foreach (var m in mallas) if (m != null) totalVerts += m.vertexCount;
        Debug.Log($"[V3Baker] ✅ Mosaico V3 horneado: {nAnillos} anillos · {totalVerts:N0} vértices totales · " +
            $"SO en {SO_PATH}. Play → MosaicoV3Sistema reemplaza 48 Terrain draw calls por {nAnillos}.");
        EditorUtility.DisplayDialog("Hornear Mosaico V3",
            $"✅ {nAnillos} mallas de terreno generadas.\n{totalVerts:N0} vértices totales.\n\n" +
            $"En Play, MosaicoV3Sistema oculta las 48 Unity Terrains y usa estas {nAnillos} mallas → " +
            $"~{nAnillos} draw calls de terreno.", "Genial");
    }

    // ── Índice O(1) por (anilloId, col, fila) → TileDef ──────────────────
    // Construido una vez, compartido para todos los vértices de todos los anillos.
    struct TileEntry { public MosaicoManifest.TileDef def; public ushort[] raw; }

    static Dictionary<(int anillo, int col, int fil), TileEntry> BuildTileIndex(
        MosaicoManifest man, Dictionary<string, ushort[]> raws, float cx, float cz)
    {
        var idx = new Dictionary<(int, int, int), TileEntry>(man.tiles.Count);
        foreach (var td in man.tiles)
        {
            if (!raws.TryGetValue(td.file, out var raw)) continue;
            MosaicoManifest.AnilloDef a = null;
            foreach (var aDef in man.anillos) if (aDef.id == td.anillo) { a = aDef; break; }
            if (a == null) continue;
            int col = Mathf.RoundToInt((td.x - (cx - a.halfExtent)) / a.tileM);
            int fil = Mathf.RoundToInt((td.z - (cz - a.halfExtent)) / a.tileM);
            idx[(td.anillo, col, fil)] = new TileEntry { def = td, raw = raw };
        }
        return idx;
    }

    // ── Generación de malla de anillo ─────────────────────────────────────
    static Mesh GenerarMallaAnillo(MosaicoManifest man,
        Dictionary<(int, int, int), TileEntry> tileIdx,
        float cx, float cz, MosaicoManifest.AnilloDef aDef,
        float innerHalf, int gridN, int anilloIdx)
    {
        float outerHalf = aDef.halfExtent;
        float size      = outerHalf * 2f;
        float spacing   = size / gridN;
        int gridVerts   = gridN + 1;

        var verts = new Vector3[gridVerts * gridVerts];
        var uvs   = new Vector2[gridVerts * gridVerts];

        for (int zi = 0; zi <= gridN; zi++)
        {
            for (int xi = 0; xi <= gridN; xi++)
            {
                float wx = cx - outerHalf + xi * spacing;
                float wz = cz - outerHalf + zi * spacing;
                float wy = SampleHeight(man, tileIdx, wx, wz, cx, cz);
                int vidx = zi * gridVerts + xi;
                verts[vidx] = new Vector3(wx, wy, wz);
                uvs[vidx]   = new Vector2(wx * 0.05f, wz * 0.05f);
            }
        }

        var tris = new List<int>(gridN * gridN * 6);
        for (int zi = 0; zi < gridN; zi++)
        {
            for (int xi = 0; xi < gridN; xi++)
            {
                float qcx = cx - outerHalf + (xi + 0.5f) * spacing;
                float qcz = cz - outerHalf + (zi + 0.5f) * spacing;
                if (innerHalf > 0f &&
                    Mathf.Abs(qcx - cx) < innerHalf &&
                    Mathf.Abs(qcz - cz) < innerHalf) continue;

                int i00 = zi * gridVerts + xi;
                int i10 = zi * gridVerts + xi + 1;
                int i01 = (zi + 1) * gridVerts + xi;
                int i11 = (zi + 1) * gridVerts + xi + 1;
                tris.Add(i00); tris.Add(i01); tris.Add(i11);
                tris.Add(i00); tris.Add(i11); tris.Add(i10);
            }
        }

        var mesh = new Mesh { name = $"Terreno_Anillo{anilloIdx}", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.Optimize();
        return mesh;
    }

    // ── Muestreo bilineal O(1) usando el índice preconstruido ─────────────
    static float SampleHeight(MosaicoManifest man,
        Dictionary<(int, int, int), TileEntry> tileIdx,
        float wx, float wz, float cx, float cz)
    {
        float dx = wx - cx, dz = wz - cz;
        float adx = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));

        foreach (var a in man.anillos)
        {
            if (adx > a.halfExtent) continue;
            int n   = Mathf.RoundToInt(2f * a.halfExtent / a.tileM);
            int col = Mathf.Clamp(Mathf.FloorToInt((dx + a.halfExtent) / a.tileM), 0, n - 1);
            int fil = Mathf.Clamp(Mathf.FloorToInt((dz + a.halfExtent) / a.tileM), 0, n - 1);

            if (!tileIdx.TryGetValue((a.id, col, fil), out var entry)) continue;

            var td  = entry.def;
            var raw = entry.raw;
            int res = td.res;

            float u = (wx - td.x) / td.ancho * (res - 1);
            float v = (wz - td.z) / td.ancho * (res - 1);
            u = Mathf.Clamp(u, 0f, res - 1.001f);
            v = Mathf.Clamp(v, 0f, res - 1.001f);

            int u0 = (int)u, v0 = (int)v;
            float tu = u - u0, tv = v - v0;
            int u1 = Mathf.Min(u0 + 1, res - 1), v1 = Mathf.Min(v0 + 1, res - 1);

            float a0 = raw[v0 * res + u0] * (1 - tu) + raw[v0 * res + u1] * tu;
            float b0 = raw[v1 * res + u0] * (1 - tu) + raw[v1 * res + u1] * tu;
            float q  = a0 * (1 - tv) + b0 * tv;
            return td.y + q * (1f / 64f);
        }
        return 0f;
    }

    // ── Crear material HDRP/Lit de terreno ────────────────────────────────
    // Prioridad de textura:
    //   1. Primera TerrainLayer del Terrain del Anillo 0 en escena (fuente canónica)
    //   2. Primera textura .png/.jpg en Assets/Textures_AAA/TerrainLayers/ (si descargada)
    //   3. Fallback: color verde terreno procedural
    static Material CrearOActualizarMaterial()
    {
        string matPath = $"{DIR_OUT}/terreno_mat.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat != null) return mat;

        var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
        mat = new Material(shader) { name = "Terreno_V3" };
        mat.enableInstancing = true;

        // ── 1. Buscar textura en TerrainLayers de los Terrain existentes ──
        Texture2D texBase = null;
        foreach (var ter in Terrain.activeTerrains)
        {
            if (ter?.terrainData == null) continue;
            var layers = ter.terrainData.terrainLayers;
            if (layers != null && layers.Length > 0 && layers[0]?.diffuseTexture != null)
            {
                texBase = layers[0].diffuseTexture;
                Debug.Log($"[V3Baker] Textura de terreno desde TerrainLayer '{layers[0].name}'.");
                break;
            }
        }

        // ── 2. Buscar en carpeta de texturas si no hay Terrain ──
        if (texBase == null)
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:Texture2D", new[] { "Assets/Textures_AAA/TerrainLayers" });
            if (guids.Length > 0)
            {
                texBase = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    AssetDatabase.GUIDToAssetPath(guids[0]));
                if (texBase != null)
                    Debug.Log($"[V3Baker] Textura de terreno desde {AssetDatabase.GUIDToAssetPath(guids[0])}.");
            }
        }

        if (texBase != null)
        {
            mat.SetTexture("_BaseColorMap", texBase);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTextureScale("_BaseColorMap", new Vector2(0.1f, 0.1f));  // ~10m por tile
        }
        else
        {
            // ── 3. Fallback procedural ────────────────────────────────────
            mat.SetColor("_BaseColor", new Color(0.28f, 0.35f, 0.18f));
            Debug.Log("[V3Baker] Sin texturas disponibles — usando color verde base. " +
                "Ejecuta '🎨 Texturas AAA PBR' o configura TerrainLayers.");
        }

        mat.SetFloat("_Smoothness", 0.08f);
        mat.SetFloat("_Metallic",   0f);

        AssetDatabase.CreateAsset(mat, matPath);
        return mat;
    }

    [MenuItem("Tools/Alsasua/Mundo/↩️ Deshacer Mosaico V3", priority = 35)]
    static void Deshacer()
    {
        if (AssetDatabase.IsValidFolder(DIR_OUT))
            AssetDatabase.DeleteAsset(DIR_OUT);
        if (AssetDatabase.IsValidFolder(DIR_RES))
            AssetDatabase.DeleteAsset(DIR_RES);
        AssetDatabase.Refresh();
        Debug.Log("[V3Baker] Mosaico V3 deshecho: Assets/MosaicoV3/ y Resources/MosaicoV3/ borrados.");
    }
}
