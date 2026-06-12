// Assets/Scripts/Editor/ConstructorMosaicoEditor.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONSTRUCTOR MOSAICO V2 (BAKE) — camino PRIMARIO del plan Terreno Mosaico V2
//
//  Genera TerrainData ASSETS persistentes (Assets/Terrenos_V2/) desde
//  terrain_tiles_v2/manifest_v2.json y los instancia en la escena bajo
//  "Mosaico_Alsasua_V2" con MarcadorTerrenoAltsasua. Al entrar en Play,
//  ServicioTerreno los adopta vía su proveedor 1 (existente validado) con
//  coste cero — sin 48 SetHeights por arranque.
//
//  El cargador runtime (CargadorMosaicoTerreno) queda como fallback para
//  escenas limpias sin bake.
//
//  Configuración idéntica al cargador runtime: layer 8, groupingID 8570,
//  allowAutoConnect=false (vecinos explícitos intra-anillo), drawInstanced,
//  pixelError por anillo (3/6/12; 4 en tiles frontera cross-ring), colliders
//  solo anillo 0.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ConstructorMosaicoEditor
{
    const string DIR_ASSETS = "Assets/Terrenos_V2";
    const string NOMBRE_RAIZ = "Mosaico_Alsasua_V2";
    const int CAPA_TERRENO = 8;
    const int GROUPING_ID = 8570;
    static readonly Dictionary<int, float> PIXEL_ERROR = new() { { 0, 3f }, { 1, 6f }, { 2, 12f } };
    const float PIXEL_ERROR_FRONTERA = 4f;

    [MenuItem("Tools/Alsasua/Mundo/🧩 Construir Mosaico V2 (bake)")]
    public static void Construir()
    {
        string rutaManifest = CargadorMosaicoTerreno.RutaManifest();
        if (rutaManifest == null)
        {
            EditorUtility.DisplayDialog("Mosaico V2",
                "No existe Assets/AlsasuaData/terrain_tiles_v2/manifest_v2.json.\n\n" +
                "Ejecuta primero:\n  python Tools/GenerarMosaicoTerrenoV2.py\n" +
                "  python Tools/ValidarMosaicoV2.py  (GATE verde)", "OK");
            return;
        }

        var manifest = MosaicoManifest.Cargar(rutaManifest);

        // GATE: exigir validation_report.json verde antes de hornear
        string rutaReporte = Path.Combine(Path.GetDirectoryName(rutaManifest), "validation_report.json");
        if (!File.Exists(rutaReporte) ||
            !File.ReadAllText(rutaReporte).Contains("\"verde\": true"))
        {
            if (!EditorUtility.DisplayDialog("Mosaico V2 — GATE",
                "validation_report.json no existe o no está VERDE.\n\n" +
                "El plan exige pasar el gate Python antes del bake. ¿Continuar igualmente?",
                "Continuar bajo mi responsabilidad", "Cancelar"))
                return;
        }

        var existente = GameObject.Find(NOMBRE_RAIZ);
        if (existente != null)
        {
            if (!EditorUtility.DisplayDialog("Mosaico V2",
                $"Ya hay un '{NOMBRE_RAIZ}' en escena. Se borrará y regenerará.", "Regenerar", "Cancelar"))
                return;
            Object.DestroyImmediate(existente);
        }

        if (!AssetDatabase.IsValidFolder(DIR_ASSETS))
            AssetDatabase.CreateFolder("Assets", "Terrenos_V2");

        var capas = CapaCompartida();
        string dirRaw = Path.GetDirectoryName(rutaManifest);
        var raiz = new GameObject(NOMBRE_RAIZ);
        Undo.RegisterCreatedObjectUndo(raiz, "Construir Mosaico V2");

        var porIndice = new Dictionary<(int, int, int), Terrain>();
        int hechos = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var def in manifest.tiles)
            {
                EditorUtility.DisplayProgressBar("Mosaico V2 (bake)",
                    $"{def.file} ({++hechos}/{manifest.tiles.Count})",
                    hechos / (float)manifest.tiles.Count);

                byte[] raw = File.ReadAllBytes(Path.Combine(dirRaw, def.file));
                if (!MosaicoManifest.VerificarSha256(def, raw))
                {
                    Debug.LogError($"[MosaicoBake] SHA256 NO coincide: {def.file} — tile omitido");
                    continue;
                }

                var td = CrearTerrainData(def, raw, manifest.altoGlobal, capas);
                string rutaAsset = $"{DIR_ASSETS}/{Path.GetFileNameWithoutExtension(def.file)}.asset";
                AssetDatabase.CreateAsset(td, rutaAsset);

                var go = Terrain.CreateTerrainGameObject(td);
                go.name = $"Tile_{Path.GetFileNameWithoutExtension(def.file)}";
                go.transform.position = new Vector3(def.x, def.y, def.z);
                go.layer = CAPA_TERRENO;
                go.transform.SetParent(raiz.transform, true);

                var terr = go.GetComponent<Terrain>();
                terr.groupingID = GROUPING_ID;
                terr.allowAutoConnect = false;
                terr.drawInstanced = true;
                terr.heightmapPixelError = EsFronteraCrossRing(manifest, def)
                    ? PIXEL_ERROR_FRONTERA : PIXEL_ERROR[def.anillo];
                terr.basemapDistance = def.anillo == 0 ? 1000f : 4000f;

                var col = go.GetComponent<TerrainCollider>();
                if (col != null) col.enabled = def.anillo == 0;

                var marca = go.AddComponent<MarcadorTerrenoAltsasua>();
                marca.fuente = FuenteTerreno.Mosaico;
                marca.anillo = def.anillo;
                Indices(manifest, def, out int fila, out int colIdx);
                marca.fila = fila; marca.columna = colIdx;
                porIndice[(def.anillo, fila, colIdx)] = terr;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
        }

        // vecinos explícitos intra-anillo (mismo criterio que el cargador runtime)
        foreach (var kv in porIndice)
        {
            (int anillo, int fila, int col) = kv.Key;
            porIndice.TryGetValue((anillo, fila, col - 1), out var izq);
            porIndice.TryGetValue((anillo, fila, col + 1), out var der);
            porIndice.TryGetValue((anillo, fila + 1, col), out var arriba);
            porIndice.TryGetValue((anillo, fila - 1, col), out var abajo);
            kv.Value.SetNeighbors(izq, arriba, der, abajo);
        }

        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(raiz.scene);

        Debug.Log($"[MosaicoBake] ✅ {porIndice.Count}/{manifest.tiles.Count} tiles horneados " +
                  $"en {DIR_ASSETS} e instanciados bajo '{NOMBRE_RAIZ}'. " +
                  "Guarda la escena y ejecuta el Auditor (Tools/Alsasua/Mundo).");
    }

    static TerrainData CrearTerrainData(MosaicoManifest.TileDef def, byte[] raw,
                                        float altoGlobal, TerrainLayer[] capas)
    {
        int res = def.res;
        var h = new float[res, res];
        for (int r = 0; r < res; r++)
        {
            int fila = r * res * 2;
            for (int c = 0; c < res; c++)
            {
                int i = fila + c * 2;
                h[r, c] = (ushort)(raw[i] | (raw[i + 1] << 8)) / 65535f;
            }
        }
        var td = new TerrainData { heightmapResolution = res };
        td.size = new Vector3(def.ancho, altoGlobal, def.ancho);
        td.SetHeights(0, 0, h);
        td.terrainLayers = capas;
        return td;
    }

    static void Indices(MosaicoManifest m, MosaicoManifest.TileDef def, out int fila, out int col)
    {
        var a = m.anillos.First(x => x.id == def.anillo);
        var ch = m.convencionHorizontal;
        col = Mathf.RoundToInt((def.x - ((float)ch.OX - a.halfExtent)) / a.tileM);
        fila = Mathf.RoundToInt((def.z - ((float)ch.OZ - a.halfExtent)) / a.tileM);
    }

    static bool EsFronteraCrossRing(MosaicoManifest m, MosaicoManifest.TileDef def)
    {
        var ch = m.convencionHorizontal;
        var a = m.anillos.First(x => x.id == def.anillo);
        float lo_x = (float)ch.OX - a.halfExtent, hi_x = (float)ch.OX + a.halfExtent;
        float lo_z = (float)ch.OZ - a.halfExtent, hi_z = (float)ch.OZ + a.halfExtent;
        if (def.anillo < 2 &&
            (Mathf.Approximately(def.x, lo_x) || Mathf.Approximately(def.x + def.ancho, hi_x) ||
             Mathf.Approximately(def.z, lo_z) || Mathf.Approximately(def.z + def.ancho, hi_z)))
            return true;
        if (def.anillo > 0)
        {
            var interior = m.anillos.First(x => x.id == def.anillo - 1);
            float ix0 = (float)ch.OX - interior.halfExtent, ix1 = (float)ch.OX + interior.halfExtent;
            float iz0 = (float)ch.OZ - interior.halfExtent, iz1 = (float)ch.OZ + interior.halfExtent;
            return (Mathf.Approximately(def.x + def.ancho, ix0) || Mathf.Approximately(def.x, ix1) ||
                    Mathf.Approximately(def.z + def.ancho, iz0) || Mathf.Approximately(def.z, iz1)) &&
                   def.x + def.ancho >= ix0 && def.x <= ix1 &&
                   def.z + def.ancho >= iz0 && def.z <= iz1;
        }
        return false;
    }

    static TerrainLayer[] CapaCompartida()
    {
        string ruta = $"{DIR_ASSETS}/CapaBase_MosaicoV2.terrainlayer";
        var capa = AssetDatabase.LoadAssetAtPath<TerrainLayer>(ruta);
        if (capa == null)
        {
            if (!AssetDatabase.IsValidFolder(DIR_ASSETS))
                AssetDatabase.CreateFolder("Assets", "Terrenos_V2");
            var tex = new Texture2D(4, 4, TextureFormat.RGB24, false);
            var px = new Color[16];
            var c = new Color(0.36f, 0.45f, 0.25f);
            for (int i = 0; i < 16; i++) px[i] = c;
            tex.SetPixels(px); tex.Apply(false, false);
            AssetDatabase.CreateAsset(tex, $"{DIR_ASSETS}/CapaBase_MosaicoV2_tex.asset");
            capa = new TerrainLayer
            {
                diffuseTexture = tex,
                tileSize = new Vector2(8f, 8f),
                name = "CapaBase_MosaicoV2"
            };
            AssetDatabase.CreateAsset(capa, ruta);
        }
        return new[] { capa };
    }
}
