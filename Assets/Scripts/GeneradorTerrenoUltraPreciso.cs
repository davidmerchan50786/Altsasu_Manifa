// Assets/Scripts/GeneradorTerrenoUltraPreciso.cs
// ═══════════════════════════════════════════════════════════════════════════
//  Reemplaza el DTM IGN 5m por el DTM Navarra 2m (IDENA) o la nube de
//  puntos LIDAR suelo (0.5m) cuando están disponibles.
//
//  Jerarquía de precisión:
//    LIDAR ground.xyz  → 0.5m  (mejor, si LIDAR descargado)
//    terreno_2m.asc    → 2m    (IDENA Navarra, mejor que IGN)
//    dtm_alsasua_5m.asc→ 5m    (IGN, fallback actual)
//
//  El terreno de Unity se actualiza directamente con SetHeights().
//  También genera un MeshCollider de alta resolución en zonas urbanas.
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using System.IO;

[DefaultExecutionOrder(-68)]  // justo después de SistemaTerreno (-70)
public class GeneradorTerrenoUltraPreciso : MonoBehaviour
{
    public static GeneradorTerrenoUltraPreciso Instance { get; private set; }

    [Header("Configuración")]
    [Tooltip("Aplicar automáticamente al arranque si hay datos mejores")]
    public bool aplicarAutomatico = true;

    [Tooltip("Resolución máxima del heightmap (potencia de 2 + 1). 1025=~1m/px, 2049=~0.5m/px")]
    public int resolucionMax = 1025;  // 2049 requiere ~50MB spike — usar 1025 por defecto

    // ── Constantes ────────────────────────────────────────────────────────
    const string PATH_LIDAR_RAW = "Assets/AlsasuaData/lidar_dtm_05m.raw";   // 0.5m, generado por PipelineLIDAR_Completo.py
    const string PATH_LIDAR_META= "Assets/AlsasuaData/lidar_dtm_meta.json"; // metadatos del RAW
    const string PATH_DTM_2M    = "Assets/AlsasuaData/terreno_2m.asc";
    const string PATH_DTM_5M    = "Assets/AlsasuaData/dtm_alsasua_5m.asc";
    const string PATH_LIDAR_G   = "Assets/AlsasuaData/lidar_ground.xyz";

    // Coordenadas — UTM 30N ETRS89 (fórmula rigurosa)
    const float E_ORIG     = 567951f;   // UTM E de Herriko Plaza
    const float N_ORIG     = 4749902f;  // UTM N de Herriko Plaza
    const float M_LON_REAL = 81548f;    // cos(42.8987°) × 111320 m/grado
    const float M_LON_PROJ = 76400f;    // escala del proyecto
    const float M_LAT      = 111320f;

    bool _aplicado;
    public bool Aplicado => _aplicado;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        if (!aplicarAutomatico) yield break;
        while (Terrain.activeTerrain == null) yield return new WaitForSeconds(0.3f);
        yield return null; // frame tras SistemaTerreno

        yield return StartCoroutine(AplicarMejorDTM());
    }

    // ── API pública ────────────────────────────────────────────────────────

    public IEnumerator AplicarMejorDTM()
    {
        string rutaRaw = FullPath(PATH_LIDAR_RAW);
        string ruta2m  = FullPath(PATH_DTM_2M);
        string ruta5m  = FullPath(PATH_DTM_5M);
        string rutaXYZ = FullPath(PATH_LIDAR_G);

        // Prioridad: RAW 0.5m LIDAR > XYZ LIDAR > ASC 2m > ASC 5m
        if (File.Exists(rutaRaw) && new FileInfo(rutaRaw).Length > 1_000_000)
        {
            AlsasuaLogger.Info("TerrenoHDR", "Aplicando DTM LIDAR 0.5m (lidar_dtm_05m.raw)...");
            yield return StartCoroutine(AplicarDesdeRAW(rutaRaw));
        }
        else if (File.Exists(rutaXYZ) && new FileInfo(rutaXYZ).Length > 50_000)
        {
            AlsasuaLogger.Info("TerrenoHDR", "Aplicando DTM desde LIDAR ground.xyz (0.5m)...");
            yield return StartCoroutine(AplicarDesdeXYZ(rutaXYZ));
        }
        else if (File.Exists(ruta2m) && new FileInfo(ruta2m).Length > 100_000)
        {
            AlsasuaLogger.Info("TerrenoHDR", "Aplicando DTM 2m (IDENA Navarra)...");
            yield return StartCoroutine(AplicarDesdeASC(ruta2m, 2f));
        }
        else if (File.Exists(ruta5m) && new FileInfo(ruta5m).Length > 100_000)
        {
            AlsasuaLogger.Info("TerrenoHDR", "Usando DTM 5m (IGN) ya aplicado por SistemaTerreno");
            // Ya aplicado por SistemaTerreno — no hacer nada
        }
        else
        {
            AlsasuaLogger.Warn("TerrenoHDR",
                "Sin DTM de alta resolución. Ejecuta PipelineDatosUltraprecisos.py");
        }
    }

    // ── RAW 16-bit (LIDAR 0.5m, generado por PipelineLIDAR_Completo.py) ─────

    IEnumerator AplicarDesdeRAW(string path)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) yield break;

        // Leer metadatos del fichero RAW
        string metaPath = FullPath(PATH_LIDAR_META);
        float lidarW = 1024f, lidarL = 1024f, zMin = 480f, zRange = 420f;
        int srcRes = 2049;

        if (File.Exists(metaPath))
        {
            try
            {
                var meta = JsonUtility.FromJson<LidarDtmMeta>(File.ReadAllText(metaPath));
                lidarW  = meta.terrainWidth;
                lidarL  = meta.terrainLength > 1f ? meta.terrainLength : meta.terrainWidth;
                zMin    = meta.z_min;
                zRange  = meta.z_max - meta.z_min;
                srcRes  = meta.heightmapResolution;
            }
            catch { }
        }

        // Leer .raw en hilo de fondo con Task.Run (puede ser 8–33 MB)
        ushort[] rawData = null;
        var taskRaw = Task.Run(() =>
        {
            var bytes = File.ReadAllBytes(path);
            var buf   = new ushort[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, buf, 0, bytes.Length);
            return buf;
        });

        yield return EsperarTask(taskRaw, "RAW read");
        if (taskRaw.IsFaulted || !taskRaw.IsCompletedSuccessfully) yield break;
        rawData = taskRaw.Result;
        if (rawData == null) yield break;
        yield return null;

        var td = terrain.terrainData;

        // ── CRÍTICO: NO cambiar td.size ni terrain.transform.position ──────
        // SceneBootstrapper ya fijó el terreno a 5000×18000m en (0,0,0).
        // Modificarlo rompería la alineación de edificios, árboles y ortofoto.

        int outRes = Mathf.Min(resolucionMax, srcRes);
        td.heightmapResolution = outRes;

        Vector3 terSize = td.size;           // 5000, 900, 18000 (de SceneBootstrapper)
        Vector3 terPos  = terrain.transform.position;  // 0, 0, 0

        // Leer el heightmap DEM existente ANTES de sobrescribir.
        // Fuera del área LIDAR conservamos el DEM (valles de Urbasa, monte Aizkorri, etc.)
        float[,] demHeights = td.GetHeights(0, 0, outRes, outRes);

        // Límites del área cubierta por el LIDAR (centrada en Herriko Plaza)
        float lidarMinX = 1918f - lidarW * 0.5f;
        float lidarMinZ = 8570f - lidarL * 0.5f;

        // Zona de transición suave: blend LIDAR→DEM en el borde (evita corte brusco)
        const float BLEND_M = 80f;  // metros de transición

        var heights = new float[outRes, outRes];

        for (int oy = 0; oy < outRes; oy++)
        for (int ox = 0; ox < outRes; ox++)
        {
            // Posición Unity de este píxel del heightmap
            float ux = terPos.x + (float)ox / (outRes - 1) * terSize.x;
            float uz = terPos.z + (float)oy / (outRes - 1) * terSize.z;

            // Fracción dentro del rectángulo LIDAR
            float lx = (ux - lidarMinX) / lidarW;
            float lz = (uz - lidarMinZ) / lidarL;

            float demH  = demHeights[oy, ox];  // altura DEM normalizada [0,1]

            if (lx >= 0f && lx <= 1f && lz >= 0f && lz <= 1f)
            {
                // Dentro del LIDAR
                int sx  = Mathf.Clamp(Mathf.RoundToInt(lx * (srcRes - 1)), 0, srcRes - 1);
                int sy  = Mathf.Clamp(Mathf.RoundToInt(lz * (srcRes - 1)), 0, srcRes - 1);
                int idx = sy * srcRes + sx;

                float altitudM = idx < rawData.Length
                    ? rawData[idx] / 65535f * zRange + zMin
                    : zMin;
                float lidarH = Mathf.Clamp01((altitudM - terPos.y) / terSize.y);

                // Blend suave en los bordes del parche LIDAR
                float distBorde = Mathf.Min(
                    Mathf.Min(lx, 1f - lx) * lidarW,
                    Mathf.Min(lz, 1f - lz) * lidarL);
                float t = Mathf.Clamp01(distBorde / BLEND_M);

                heights[oy, ox] = Mathf.Lerp(demH, lidarH, t);
            }
            else
            {
                // Fuera del LIDAR: conservar DEM original (valle del Arakil, Urbasa, etc.)
                heights[oy, ox] = demH;
            }
        }

        td.SetHeights(0, 0, heights);
        terrain.Flush();

        _aplicado = true;
        AlsasuaLogger.Info("TerrenoHDR",
            $"✅ DTM LIDAR 0.5m: {outRes}²px  terreno {terSize.x}×{terSize.z}m  " +
            $"LIDAR {lidarW}×{lidarL}m  Z {zMin:F0}–{zMin+zRange:F0}m");
    }

    [System.Serializable]
    class LidarDtmMeta
    {
        public int   heightmapResolution;
        public float terrainWidth, terrainLength, terrainHeight;
        public float z_min, z_max, res_m;
    }

    // ── ASC Grid (IDENA 2m) ───────────────────────────────────────────────

    IEnumerator AplicarDesdeASC(string path, float resolucion)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) yield break;

        // Leer cabecera del ASC
        int ncols = 0, nrows = 0;
        float xll = 0, yll = 0, cell = 0, nodata = -9999f;
        float[] data = null;

        // Leer en hilo de fondo con Task.Run
        // Variables locales capturadas por la Task — se actualizan mediante tupla de retorno
        var taskAsc = Task.Run(() =>
        {
            using var r   = new StreamReader(path);
            int   _ncols  = int.Parse(  r.ReadLine().Split()[1]);
            int   _nrows  = int.Parse(  r.ReadLine().Split()[1]);
            float _xll    = float.Parse(r.ReadLine().Split()[1], System.Globalization.CultureInfo.InvariantCulture);
            float _yll    = float.Parse(r.ReadLine().Split()[1], System.Globalization.CultureInfo.InvariantCulture);
            float _cell   = float.Parse(r.ReadLine().Split()[1], System.Globalization.CultureInfo.InvariantCulture);
            float _nodata = float.Parse(r.ReadLine().Split()[1], System.Globalization.CultureInfo.InvariantCulture);

            float[] _data = new float[_ncols * _nrows];
            int idx = 0; string line;
            while ((line = r.ReadLine()) != null)
                foreach (var tok in line.Split(' ', '\t'))
                    if (!string.IsNullOrEmpty(tok) && idx < _data.Length)
                        _data[idx++] = float.Parse(tok, System.Globalization.CultureInfo.InvariantCulture);

            return (_ncols, _nrows, _xll, _yll, _cell, _nodata, _data);
        });

        yield return EsperarTask(taskAsc, "ASC read");
        if (taskAsc.IsFaulted || !taskAsc.IsCompletedSuccessfully) yield break;

        (ncols, nrows, xll, yll, cell, nodata, data) = taskAsc.Result;
        if (data == null) yield break;

        yield return null;

        AlsasuaLogger.Info("TerrenoHDR",
            $"ASC leído: {ncols}×{nrows}, cell={cell}m, xll={xll:F4}");

        // El ASC puede estar en UTM 30N (si viene de IDENA) o en grados (IGN)
        bool esUTM = (xll > 100_000);  // UTM tiene coordenadas > 100km
        AplicarHeightmap(terrain, data, ncols, nrows, xll, yll, cell, nodata, esUTM);

        _aplicado = true;
        AlsasuaLogger.Info("TerrenoHDR", $"✅ DTM {resolucion}m aplicado al terreno");
    }

    void AplicarHeightmap(Terrain terrain, float[] data,
        int ncols, int nrows, float xll, float yll, float cell, float nodata,
        bool coordsUTM)
    {
        var td   = terrain.terrainData;
        int hRes = Mathf.Min(resolucionMax, Mathf.NextPowerOfTwo(Mathf.Max(ncols, nrows)) + 1);
        td.heightmapResolution = hRes;

        float[,] heights = new float[hRes, hRes];
        float terrW = td.size.x, terrH = td.size.z;
        float terrY = td.size.y;
        Vector3 terrPos = terrain.transform.position;

        // Rango Z del DTM para normalizar
        float zMin = float.MaxValue, zMax = float.MinValue;
        foreach (float v in data)
            if (!Mathf.Approximately(v, nodata)) { zMin = Mathf.Min(zMin, v); zMax = Mathf.Max(zMax, v); }
        float zRange = zMax - zMin;
        if (zRange < 1f) return;

        for (int hy = 0; hy < hRes; hy++)
        for (int hx = 0; hx < hRes; hx++)
        {
            // Posición Unity → coordenadas del ASC
            float ux = terrPos.x + (float)hx / (hRes - 1) * terrW;
            float uz = terrPos.z + (float)hy / (hRes - 1) * terrH;

            // Unity XZ → lat/lon  (LON0=-2.1677, LAT0=42.8987, M_LON_PROJ=76400, M_LAT=111320)
            const float LON0_ = -2.1677f, LAT0_ = 42.8987f;
            const float M_LON_ = 76400f;
            float lon_ = (ux - 1918f) / M_LON_ + LON0_;
            float lat_ = (uz - 8570f) / M_LAT  + LAT0_;

            // lat/lon → índice en el grid
            int col, row;
            if (coordsUTM)
            {
                // Conversión aproximada lat/lon → UTM 30N (E_ORIG=567951, N_ORIG=4749902)
                float utm_e = (lon_ - LON0_) * M_LON_REAL + E_ORIG;
                float utm_n = (lat_ - LAT0_) * M_LAT      + N_ORIG;
                col = Mathf.FloorToInt((utm_e - xll) / cell);
                row = nrows - 1 - Mathf.FloorToInt((utm_n - yll) / cell);
            }
            else
            {
                col = Mathf.FloorToInt((lon_ - xll) / cell);
                row = nrows - 1 - Mathf.FloorToInt((lat_ - yll) / cell);
            }

            col = Mathf.Clamp(col, 0, ncols - 1);
            row = Mathf.Clamp(row, 0, nrows - 1);

            float z = data[row * ncols + col];
            if (Mathf.Approximately(z, nodata)) z = zMin;

            heights[hy, hx] = Mathf.Clamp01((z - zMin) / zRange);
        }

        td.SetHeights(0, 0, heights);
        terrain.Flush();
    }

    // ── XYZ (LIDAR ground) ────────────────────────────────────────────────

    IEnumerator AplicarDesdeXYZ(string path)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) yield break;

        // Leer puntos en hilo de fondo con Task.Run
        var taskXyz = Task.Run(() =>
        {
            var lista = new List<Vector3>();
            foreach (var line in File.ReadLines(path))
            {
                var tok = line.Split(' ', '\t');
                if (tok.Length < 3) continue;
                if (float.TryParse(tok[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float x)
                 && float.TryParse(tok[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float y)
                 && float.TryParse(tok[2], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float z))
                {
                    // lidar_ground.xyz: X/Z relativo a Herriko Plaza → convertir a Unity absoluto
                    lista.Add(new Vector3(x + GeoDataAlsasua.OX, y, z + GeoDataAlsasua.OZ));
                }
            }
            return lista;
        });

        yield return EsperarTask(taskXyz, "XYZ read");
        if (taskXyz.IsFaulted || !taskXyz.IsCompletedSuccessfully) yield break;
        List<Vector3> puntos = taskXyz.Result;

        if (puntos.Count < 100) yield break;
        AlsasuaLogger.Info("TerrenoHDR", $"LIDAR ground: {puntos.Count} puntos");

        yield return null;   // GC opportunity before large allocations

        var td   = terrain.terrainData;
        // Limitar a 1025 para XYZ (3 arrays de hRes² serían ~50MB con 2049)
        int hRes = Mathf.Min(resolucionMax, 1025);
        td.heightmapResolution = hRes;

        float terrW = td.size.x, terrH = td.size.z;
        Vector3 terrPos = terrain.transform.position;

        // Acumular alturas (hRes=1025 → 3×1025²×4 = 12.6MB, manejable)
        var sumZ  = new float[hRes, hRes];
        var countZ= new int  [hRes, hRes];

        float zMin = float.MaxValue, zMax = float.MinValue;
        foreach (var p in puntos) { zMin = Mathf.Min(zMin, p.y); zMax = Mathf.Max(zMax, p.y); }
        float zRange = zMax - zMin;
        if (zRange < 1f) yield break;

        foreach (var p in puntos)
        {
            // p.x = Unity X (relativo a origen), p.y = altitud, p.z = Unity Z
            int hx = Mathf.Clamp(Mathf.RoundToInt((p.x - terrPos.x) / terrW * (hRes - 1)), 0, hRes - 1);
            int hy = Mathf.Clamp(Mathf.RoundToInt((p.z - terrPos.z) / terrH * (hRes - 1)), 0, hRes - 1);
            sumZ[hy, hx]  += p.y;
            countZ[hy, hx]++;
        }

        // Interpolar celdas vacías con valor del vecino más cercano
        var heights = new float[hRes, hRes];
        for (int hy = 0; hy < hRes; hy++)
        for (int hx = 0; hx < hRes; hx++)
            heights[hy, hx] = countZ[hy, hx] > 0
                ? Mathf.Clamp01((sumZ[hy, hx] / countZ[hy, hx] - zMin) / zRange)
                : 0f;

        // Rellenar huecos con media de vecinos (iteración sencilla)
        for (int iter = 0; iter < 3; iter++)
        for (int hy = 1; hy < hRes - 1; hy++)
        for (int hx = 1; hx < hRes - 1; hx++)
            if (countZ[hy, hx] == 0)
            {
                int n = 0; float s = 0;
                if (countZ[hy-1,hx]>0){s+=heights[hy-1,hx];n++;}
                if (countZ[hy+1,hx]>0){s+=heights[hy+1,hx];n++;}
                if (countZ[hy,hx-1]>0){s+=heights[hy,hx-1];n++;}
                if (countZ[hy,hx+1]>0){s+=heights[hy,hx+1];n++;}
                if (n > 0) heights[hy, hx] = s / n;
            }

        // Liberar la lista de puntos antes de SetHeights (libera ~29MB)
        int nPuntos = puntos.Count;
        puntos.Clear();
        puntos = null;
        System.GC.Collect();

        yield return null;   // frame para que GC actúe

        td.SetHeights(0, 0, heights);
        terrain.Flush();

        _aplicado = true;
        AlsasuaLogger.Info("TerrenoHDR",
            $"✅ Terreno LIDAR aplicado: {nPuntos} pts → {hRes}×{hRes} heightmap");
    }

    static string FullPath(string relative)
        => Path.Combine(Application.dataPath.Replace("Assets", ""), relative);

    /// <summary>
    /// Cede el control cada frame hasta que la Task finaliza.
    /// Si falla, loga el error. Sustituye el patrón ThreadPool + bool + WaitForSeconds.
    /// </summary>
    static IEnumerator EsperarTask(Task task, string nombreTarea)
    {
        while (!task.IsCompleted)
            yield return null; // cede cada frame sin sleep fijo

        if (task.IsFaulted)
        {
            var ex = task.Exception?.InnerException ?? task.Exception;
            AlsasuaLogger.Error("TerrenoHDR",
                $"Error en tarea '{nombreTarea}': {ex?.GetType().Name} — {ex?.Message}");
        }
    }
}
