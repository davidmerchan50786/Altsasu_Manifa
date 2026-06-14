// Assets/Scripts/Systems/CargadorMosaicoTerreno.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CARGADOR MOSAICO TERRENO — instancia los 48 tiles del mosaico V2
//
//  Fuente: Assets/AlsasuaData/terrain_tiles_v2/ (RAW uint16 LE, fila 0 = sur,
//  manifest_v2.json). Codificación lattice 1/64: size.y = 1023.984375 para
//  TODOS los tiles, posY = y64/64 ⇒ alturaMundo = (y64 + q)/64 con numerador
//  entero < 2²⁴ ⇒ las costuras dependen solo de la IGUALDAD ENTERA de q,
//  garantizada por el gate Python (ValidarMosaicoV2.py).
//
//  Reparto de trabajo:
//    · Parseo RAW uint16→float01 en Tasks de fondo (por anillo, limita memoria)
//    · SetHeightsDelayLOD por bandas con presupuesto de ~8 ms/frame
//    · Anillo 0 primero (jugable) → callback → anillos 1-2 → callback completo
//    · SetNeighbors SOLO intra-anillo (Unity no soporta vecinos de distinta
//      resolución/tamaño); los datos cross-ring ya son continuos
//    · Colliders: anillo 0 ON; anillos 1-2 OFF (StreamerColliderTerreno los
//      activa por distancia)
//
//  Es un componente auxiliar de ServicioTerreno; no publica eventos por sí
//  mismo (eso es del servicio).
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class CargadorMosaicoTerreno : MonoBehaviour
{
    public const string DIR_TILES_REL = "AlsasuaData/terrain_tiles_v2";
    public const string MANIFEST = "manifest_v2.json";
    const int CAPA_TERRENO = 8;
    const int GROUPING_ID = 8570;
    const float PRESUPUESTO_MS = 8f;
    // Tiles parseados en paralelo por lote. El anillo 1 tiene 32 tiles; lanzarlos
    // todos a la vez reservaba ~134 MB de float[,] simultáneos → bajo presión de
    // memoria una Task se quedaba sin completar y el mosaico se clavaba en 5 tiles.
    const int LOTE_PARSEO = 4;
    // Un tile que no parsea en este tiempo se salta (no cuelga el mosaico entero).
    const float TIMEOUT_TILE_S = 20f;

    static readonly Dictionary<int, float> PIXEL_ERROR = new() { { 0, 3f }, { 1, 6f }, { 2, 12f } };
    const float PIXEL_ERROR_FRONTERA = 4f; // tiles que tocan una frontera cross-ring

    public MosaicoManifest Manifest { get; private set; }
    public IReadOnlyList<Terrain> Tiles => _tiles;
    public Terrain TileAncla { get; private set; }
    public bool Anillo0Listo { get; private set; }
    public bool Completo { get; private set; }

    readonly List<Terrain> _tiles = new();
    // lookup O(1): (anillo, fila, col) → Terrain
    readonly Dictionary<(int, int, int), Terrain> _porIndice = new();
    TerrainLayer[] _capasCompartidas;

    /// <summary>Ruta absoluta del manifest v2, o null si no existe.</summary>
    public static string RutaManifest()
    {
        string p = Path.Combine(Application.dataPath, DIR_TILES_REL, MANIFEST);
        return File.Exists(p) ? p : null;
    }

    /// <summary>Tile que contiene la posición (anillo más fino), O(1). Null si
    /// está fuera del mosaico o el tile aún no se ha instanciado.</summary>
    public Terrain TerrainEn(Vector3 pos)
    {
        if (Manifest == null) return null;
        var ch = Manifest.convencionHorizontal;
        float dx = pos.x - (float)ch.OX, dz = pos.z - (float)ch.OZ;
        float adx = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));

        foreach (var a in Manifest.anillos) // 3 anillos, orden fino→grueso
        {
            if (adx > a.halfExtent) continue;
            int n = Mathf.RoundToInt(2f * a.halfExtent / a.tileM);
            int col = Mathf.Clamp(Mathf.FloorToInt((dx + a.halfExtent) / a.tileM), 0, n - 1);
            int fila = Mathf.Clamp(Mathf.FloorToInt((dz + a.halfExtent) / a.tileM), 0, n - 1);
            return _porIndice.TryGetValue((a.id, fila, col), out var t) ? t : null;
        }
        return null;
    }

    /// <summary>
    /// Carga completa del mosaico. onAnillo0 se invoca cuando los tiles del
    /// anillo 0 tienen alturas (mundo jugable); onCompleto cuando están los 48.
    /// </summary>
    public IEnumerator Cargar(System.Action onAnillo0, System.Action onCompleto)
    {
        string rutaManifest = RutaManifest();
        if (rutaManifest == null)
        {
            AlsasuaLogger.Warn("Mosaico", $"No existe {DIR_TILES_REL}/{MANIFEST}.");
            yield break;
        }

        try { Manifest = MosaicoManifest.Cargar(rutaManifest); }
        catch (System.Exception ex)
        {
            AlsasuaLogger.Warn("Mosaico", $"Manifest ilegible: {ex.Message}");
            yield break;
        }

        _capasCompartidas = CrearCapasCompartidas();
        string dirAbs = Path.Combine(Application.dataPath, DIR_TILES_REL);

        // por anillos: parsear cada anillo EN LOTES (no todos a la vez) e instanciar.
        // Antes lanzaba los 32 tiles del anillo 1 como Tasks simultáneas → pico de RAM
        // que congelaba una Task y dejaba el mundo en 5 tiles. Ahora ≤ LOTE_PARSEO en
        // vuelo a la vez, con timeout por tile y log de progreso para ver dónde falla.
        foreach (var grupo in Manifest.tiles.GroupBy(t => t.anillo).OrderBy(g => g.Key))
        {
            var defs   = grupo.ToList();
            int anillo = grupo.Key;
            AlsasuaLogger.Info("Mosaico",
                $"Anillo {anillo}: {defs.Count} tiles (lotes de {LOTE_PARSEO})…");

            for (int baseIdx = 0; baseIdx < defs.Count; baseIdx += LOTE_PARSEO)
            {
                int n = Mathf.Min(LOTE_PARSEO, defs.Count - baseIdx);
                var tareas = new System.Threading.Tasks.Task<float[,]>[n];
                for (int j = 0; j < n; j++)
                {
                    var d = defs[baseIdx + j];   // local fresco por iteración (closure correcta)
                    tareas[j] = System.Threading.Tasks.Task.Run(
                        () => ParsearRaw(Path.Combine(dirAbs, d.file), d.res));
                }

                for (int j = 0; j < n; j++)
                {
                    var d = defs[baseIdx + j];

                    // Espera con timeout: un tile que nunca completa ya NO cuelga el mosaico.
                    float tEspera = Time.realtimeSinceStartup;
                    while (!tareas[j].IsCompleted)
                    {
                        if (Time.realtimeSinceStartup - tEspera > TIMEOUT_TILE_S)
                        {
                            AlsasuaLogger.Error("Mosaico",
                                $"Tile {d.file} (anillo {anillo}) no parseó en {TIMEOUT_TILE_S:F0}s — SE SALTA. " +
                                "Probable RAW corrupto/bloqueado o falta de memoria.");
                            break;
                        }
                        yield return null;
                    }
                    if (!tareas[j].IsCompleted) continue;              // saltado por timeout
                    if (!tareas[j].IsCompletedSuccessfully)
                    {
                        AlsasuaLogger.Warn("Mosaico",
                            $"Tile {d.file} ilegible: {tareas[j].Exception?.GetBaseException().Message}");
                        continue;
                    }
                    yield return InstanciarTile(d, tareas[j].Result);
                    tareas[j] = null; // liberar el float[,] cuanto antes
                }

                AlsasuaLogger.Info("Mosaico",
                    $"Anillo {anillo}: {Mathf.Min(baseIdx + n, defs.Count)}/{defs.Count} parseados · {_tiles.Count} instanciados.");
            }

            if (anillo == 0)
            {
                ConectarVecinosIntraAnillo(0);
                Anillo0Listo = true;
                onAnillo0?.Invoke();
            }
        }

        for (int a = 1; a <= 2; a++) ConectarVecinosIntraAnillo(a);
        Completo = _tiles.Count == Manifest.tiles.Count;
        if (!Completo)
            AlsasuaLogger.Warn("Mosaico",
                $"Mosaico incompleto: {_tiles.Count}/{Manifest.tiles.Count} tiles.");
        onCompleto?.Invoke();
    }

    // ── Parseo RAW (hilo de fondo) ───────────────────────────────────────────
    static float[,] ParsearRaw(string ruta, int res)
    {
        byte[] raw = File.ReadAllBytes(ruta);
        int esperado = res * res * 2;
        if (raw.Length != esperado)
            throw new IOException($"{Path.GetFileName(ruta)}: {raw.Length} bytes (esperados {esperado})");
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
        return h;
    }

    // ── Instanciación + SetHeights amortizado ────────────────────────────────
    IEnumerator InstanciarTile(MosaicoManifest.TileDef def, float[,] alturas)
    {
        var td = new TerrainData { heightmapResolution = def.res };
        td.size = new Vector3(def.ancho, Manifest.altoGlobal, def.ancho);

        // SetHeightsDelayLOD por bandas con presupuesto por frame.
        // Medimos SOLO el tiempo SÍNCRONO de SetHeights+Sync (sin contar los yields)
        // y lo empujamos a Telemetria → VisualizadorHeatmap (editor) pinta las costuras
        // más caras del lattice. El Stopwatch acumula únicamente el coste real de coser.
        int res = def.res;
        int filasPorLote = Mathf.Max(32, res / 16);
        float msCosido = 0f;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        float t0 = Time.realtimeSinceStartup;
        for (int j0 = 0; j0 < res; j0 += filasPorLote)
        {
            int h = Mathf.Min(filasPorLote, res - j0);
            var banda = new float[h, res];
            for (int j = 0; j < h; j++)
                for (int i = 0; i < res; i++)
                    banda[j, i] = alturas[j0 + j, i];
            sw.Restart();
            td.SetHeightsDelayLOD(0, j0, banda);
            msCosido += (float)sw.Elapsed.TotalMilliseconds;
            if ((Time.realtimeSinceStartup - t0) * 1000f > PRESUPUESTO_MS)
            {
                yield return null;
                t0 = Time.realtimeSinceStartup;
            }
        }
        sw.Restart();
        td.SyncHeightmap();
        msCosido += (float)sw.Elapsed.TotalMilliseconds;
        td.terrainLayers = _capasCompartidas;

        var go = Terrain.CreateTerrainGameObject(td);
        go.name = $"Tile_{Path.GetFileNameWithoutExtension(def.file)}";
        go.transform.position = new Vector3(def.x, def.y, def.z);
        go.layer = CAPA_TERRENO;
        go.transform.SetParent(transform, true);

        var terr = go.GetComponent<Terrain>();
        terr.groupingID = GROUPING_ID;
        terr.allowAutoConnect = false; // vecinos explícitos, solo intra-anillo
        terr.drawInstanced = true;
        terr.heightmapPixelError = EsFronteraCrossRing(def) ? PIXEL_ERROR_FRONTERA
                                                            : PIXEL_ERROR[def.anillo];
        terr.basemapDistance = def.anillo == 0 ? 1000f : 4000f;

        var col = go.GetComponent<TerrainCollider>();
        if (col != null) col.enabled = def.anillo == 0; // 1-2: StreamerColliderTerreno

        var marca = go.AddComponent<MarcadorTerrenoAltsasua>();
        marca.fuente = FuenteTerreno.Mosaico;
        marca.anillo = def.anillo;
        DescomponerIndice(def, out int fila, out int colIdx);
        marca.fila = fila; marca.columna = colIdx;

        _tiles.Add(terr);
        _porIndice[(def.anillo, fila, colIdx)] = terr;

        // Telemetría de cosido (centro mundo del tile, lado, ms síncronos).
        Telemetria.RegistrarCostura(
            new Vector3(def.x + def.ancho * 0.5f, def.y, def.z + def.ancho * 0.5f),
            def.ancho, msCosido);

        var ch = Manifest.convencionHorizontal;
        if (def.x <= ch.OX && ch.OX <= def.x + def.ancho &&
            def.z <= ch.OZ && ch.OZ <= def.z + def.ancho && TileAncla == null)
            TileAncla = terr;
    }

    void DescomponerIndice(MosaicoManifest.TileDef def, out int fila, out int col)
    {
        var a = Manifest.anillos.First(x => x.id == def.anillo);
        var ch = Manifest.convencionHorizontal;
        col = Mathf.RoundToInt((def.x - ((float)ch.OX - a.halfExtent)) / a.tileM);
        fila = Mathf.RoundToInt((def.z - ((float)ch.OZ - a.halfExtent)) / a.tileM);
    }

    bool EsFronteraCrossRing(MosaicoManifest.TileDef def)
    {
        // toca el perímetro de su bloque (anillos 0-1) o el agujero interior (1-2)
        var ch = Manifest.convencionHorizontal;
        foreach (var a in Manifest.anillos)
        {
            if (a.id != def.anillo) continue;
            float lo_x = (float)ch.OX - a.halfExtent, hi_x = (float)ch.OX + a.halfExtent;
            float lo_z = (float)ch.OZ - a.halfExtent, hi_z = (float)ch.OZ + a.halfExtent;
            bool perimetro = Mathf.Approximately(def.x, lo_x) || Mathf.Approximately(def.x + def.ancho, hi_x)
                          || Mathf.Approximately(def.z, lo_z) || Mathf.Approximately(def.z + def.ancho, hi_z);
            if (def.anillo < 2 && perimetro) return true;
            if (def.anillo > 0)
            {
                var interior = Manifest.anillos.First(x => x.id == def.anillo - 1);
                float ix0 = (float)ch.OX - interior.halfExtent, ix1 = (float)ch.OX + interior.halfExtent;
                float iz0 = (float)ch.OZ - interior.halfExtent, iz1 = (float)ch.OZ + interior.halfExtent;
                bool tocaAgujero = (Mathf.Approximately(def.x + def.ancho, ix0) || Mathf.Approximately(def.x, ix1) ||
                                    Mathf.Approximately(def.z + def.ancho, iz0) || Mathf.Approximately(def.z, iz1)) &&
                                   def.x + def.ancho >= ix0 && def.x <= ix1 &&
                                   def.z + def.ancho >= iz0 && def.z <= iz1;
                if (tocaAgujero) return true;
            }
        }
        return false;
    }

    void ConectarVecinosIntraAnillo(int anillo)
    {
        foreach (var kv in _porIndice)
        {
            if (kv.Key.Item1 != anillo) continue;
            int fila = kv.Key.Item2, col = kv.Key.Item3;
            _porIndice.TryGetValue((anillo, fila, col - 1), out var izq);
            _porIndice.TryGetValue((anillo, fila, col + 1), out var der);
            _porIndice.TryGetValue((anillo, fila + 1, col), out var arriba); // +z
            _porIndice.TryGetValue((anillo, fila - 1, col), out var abajo);  // -z
            kv.Value.SetNeighbors(izq, arriba, der, abajo);
        }
    }

    static TerrainLayer[] CrearCapasCompartidas()
    {
        // capa base única compartida por TODOS los tiles; SistemaTerreno pinta
        // biomas encima (alphamaps) reutilizando este mismo array de capas
        var tex = new Texture2D(4, 4, TextureFormat.RGB24, false);
        var c = new Color(0.36f, 0.45f, 0.25f);
        var px = new Color[16];
        for (int i = 0; i < 16; i++) px[i] = c;
        tex.SetPixels(px); tex.Apply(false, true);
        var capa = new TerrainLayer
        {
            diffuseTexture = tex,
            tileSize = new Vector2(8f, 8f),
            name = "CapaBase_MosaicoV2"
        };
        return new[] { capa };
    }
}
