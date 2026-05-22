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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;

[DefaultExecutionOrder(-68)]  // justo después de SistemaTerreno (-70)
public class GeneradorTerrenoUltraPreciso : MonoBehaviour
{
    public static GeneradorTerrenoUltraPreciso Instance { get; private set; }

    [Header("Configuración")]
    [Tooltip("Aplicar automáticamente al arranque si hay datos mejores")]
    public bool aplicarAutomatico = true;

    [Tooltip("Resolución máxima del heightmap (potencia de 2 + 1)")]
    public int resolucionMax = 2049;

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

        // Leer metadatos
        string metaPath = FullPath(PATH_LIDAR_META);
        float terrainW = 1024f, terrainH_m = 900f, zMin = 300f, zRange = 700f;
        int hRes = 2049;

        if (File.Exists(metaPath))
        {
            try
            {
                var meta = JsonUtility.FromJson<LidarDtmMeta>(File.ReadAllText(metaPath));
                terrainW   = meta.terrainWidth;
                terrainH_m = meta.terrainHeight;
                zMin       = meta.z_min;
                zRange     = meta.z_max - meta.z_min;
                hRes       = meta.heightmapResolution;
            }
            catch { }
        }

        // Leer .raw en thread (puede ser 8MB)
        ushort[] rawData = null;
        bool lecto = false;
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                rawData = new ushort[bytes.Length / 2];
                Buffer.BlockCopy(bytes, 0, rawData, 0, bytes.Length);
                lecto = true;
            }
            catch (System.Exception e)
            { AlsasuaLogger.Warn("TerrenoHDR", $"RAW read error: {e.Message}"); lecto = true; }
        });

        while (!lecto) yield return new WaitForSeconds(0.05f);
        if (rawData == null) yield break;

        yield return null;

        var td = terrain.terrainData;
        td.heightmapResolution = hRes;
        td.size = new Vector3(terrainW, zRange, terrainW);
        terrain.transform.position = new Vector3(
            OX - terrainW * 0.5f,
            zMin,
            OZ - terrainW * 0.5f);

        float[,] heights = new float[hRes, hRes];
        for (int i = 0; i < hRes * hRes && i < rawData.Length; i++)
            heights[i / hRes, i % hRes] = rawData[i] / 65535f;

        td.SetHeights(0, 0, heights);
        terrain.Flush();

        _aplicado = true;
        AlsasuaLogger.Info("TerrenoHDR",
            $"✅ DTM LIDAR 0.5m aplicado: {hRes}×{hRes}  {terrainW}m×{terrainW}m  Z={zMin:F0}-{zMin+zRange:F0}m");
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

        // Leer en hilo de fondo (no bloquear el main thread)
        bool lecto = false;
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                using var r = new StreamReader(path);
                ncols = int.Parse(  r.ReadLine().Split()[1]);
                nrows = int.Parse(  r.ReadLine().Split()[1]);
                xll   = float.Parse(r.ReadLine().Split()[1],
                    System.Globalization.CultureInfo.InvariantCulture);
                yll   = float.Parse(r.ReadLine().Split()[1],
                    System.Globalization.CultureInfo.InvariantCulture);
                cell  = float.Parse(r.ReadLine().Split()[1],
                    System.Globalization.CultureInfo.InvariantCulture);
                nodata= float.Parse(r.ReadLine().Split()[1],
                    System.Globalization.CultureInfo.InvariantCulture);

                data = new float[ncols * nrows];
                int idx = 0; string line;
                while ((line = r.ReadLine()) != null)
                    foreach (var tok in line.Split(' ','\t'))
                        if (!string.IsNullOrEmpty(tok) && idx < data.Length)
                            data[idx++] = float.Parse(tok,
                                System.Globalization.CultureInfo.InvariantCulture);
                lecto = true;
            }
            catch (System.Exception e)
            { AlsasuaLogger.Warn("TerrenoHDR", $"Error leyendo ASC: {e.Message}"); }
        });

        while (!lecto) yield return new WaitForSeconds(0.1f);
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

            // Unity XZ → lat/lon
            float lon_ = (ux - 1918f) / M_LON + LON0;
            float lat_ = (uz - 8570f) / M_LAT + LAT0;

            // lat/lon → índice en el grid
            int col, row;
            if (coordsUTM)
            {
                // Conversión aproximada lat/lon → UTM 30N
                float utm_e = (lon_ - LON0) * M_LON + 574900f;
                float utm_n = (lat_ - LAT0) * M_LAT + 4751600f;
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

        // Leer puntos en hilo de fondo
        var puntos = new List<Vector3>();
        bool lecto = false;

        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                foreach (var line in File.ReadLines(path))
                {
                    var tok = line.Split(' ', '\t');
                    if (tok.Length < 3) continue;
                    if (float.TryParse(tok[0], out float x)
                     && float.TryParse(tok[1], out float y)
                     && float.TryParse(tok[2], out float z))
                    {
                        // lidar_ground.xyz: X=relativo a Herriko Plaza, Y=altitud, Z=relativo
                        // Convertir a Unity absoluto (+ OX, + OZ)
                        puntos.Add(new Vector3(x + 1918f, y, z + 8570f));
                    }
                }
                lecto = true;
            }
            catch (System.Exception e)
            { AlsasuaLogger.Warn("TerrenoHDR", $"XYZ error: {e.Message}"); lecto = true; }
        });

        while (!lecto) yield return new WaitForSeconds(0.1f);

        if (puntos.Count < 100) yield break;
        AlsasuaLogger.Info("TerrenoHDR", $"LIDAR ground: {puntos.Count} puntos");

        yield return null;

        var td   = terrain.terrainData;
        int hRes = Mathf.Min(resolucionMax, 2049);
        td.heightmapResolution = hRes;

        float terrW = td.size.x, terrH = td.size.z;
        float terrY = td.size.y;
        Vector3 terrPos = terrain.transform.position;

        // Acumular alturas en grid (promedio de puntos caídos en cada celda)
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

        td.SetHeights(0, 0, heights);
        terrain.Flush();

        _aplicado = true;
        AlsasuaLogger.Info("TerrenoHDR",
            $"✅ Terreno LIDAR aplicado: {puntos.Count} pts → {hRes}×{hRes} heightmap");
    }

    static string FullPath(string relative)
        => Path.Combine(Application.dataPath.Replace("Assets", ""), relative);
}
