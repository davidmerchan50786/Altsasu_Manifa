// Assets/Scripts/GeneradorTerrenoUltraPreciso.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GENERADOR DE TERRENO ULTRA PRECISO — v3.0  (AAA+++)
//
//  Jerarquía de precisión:
//    1. lidar_dtm_05m.raw   → 0.5m/px  2049×2049 uint16 LE  (PRIMARIO)
//    2. lidar_ground.xyz    → 587k pts XYZ reales            (VALIDACIÓN/CORRECCIÓN)
//    3. dtm_alsasua_5m.asc  → 5m/px IGN                     (BORDES/MONTAÑAS)
//    4. dem_unity_1025.raw  → 1025×1025                      (FALLBACK)
//
//  Pipeline:
//    A. Carga lidar_dtm_05m.raw en NativeArray<ushort> persistente (nunca re-leído)
//    B. Heightmap 2049×2049 con resample bicúbico (Catmull-Rom)
//    C. Blend gaussiano 200m contra DTM 5m en bordes
//    D. Validación RMSE contra lidar_ground.xyz (2000 pts aleatorios)
//    E. Corrección local Gaussian splat r=2m si RMSE > 0.3m
//    F. Terrain resolution = 2049
//
//  Sistema de coordenadas (CRÍTICO — NO modificar):
//    Origen = Herriko Plaza: E=567951, N=4749902 UTM 30N ETRS89
//    Unity: OX=1918, OZ=8570  →  UnityX = (E - E_origin) + OX
//    Escala 1:1 (1 unit = 1m real). AltitudUnity = altitud_real - Z_min
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[DefaultExecutionOrder(-68)]
public class GeneradorTerrenoUltraPreciso : MonoBehaviour
{
    public static GeneradorTerrenoUltraPreciso Instance { get; private set; }

    // ── Configuración Inspector ───────────────────────────────────────────
    [Header("Configuración Principal")]
    [Tooltip("Aplicar automáticamente al arranque")]
    public bool aplicarAutomatico = true;

    [Tooltip("Resolución del heightmap. 2049 = máxima (0.5m/px). Requiere ~33MB RAM.")]
    public int resolucionTarget = 2049;

    [Tooltip("RMSE máximo aceptable en metros antes de aplicar corrección local")]
    public float rmseMaxMetros = 0.3f;

    [Tooltip("Número de puntos para validación RMSE (0 = desactivar). 0 en ValidarTerrenoCoroutine = todos los puntos.")]
    public int puntosValidacion = 10000;

    [Tooltip("Ancho de blend gaussiano en metros entre LIDAR y DTM5m en los bordes")]
    public float blendGaussianoBordes = 200f;

    [Header("Debug")]
    public bool logVerboso = true;

    // ── Rutas de datos ────────────────────────────────────────────────────
    const string PATH_LIDAR_RAW    = "Assets/AlsasuaData/lidar_dtm_05m.raw";
    const string PATH_LIDAR_META   = "Assets/AlsasuaData/lidar_dtm_meta.json";
    const string PATH_LIDAR_GND    = "Assets/AlsasuaData/lidar_ground.xyz";
    const string PATH_DTM_5M       = "Assets/AlsasuaData/dtm_alsasua_5m.asc";
    const string PATH_DEM_FB       = "Assets/AlsasuaData/dem_unity_1025.raw";
    const string PATH_VAL_REPORT   = "Assets/AlsasuaData/terrain_validation_report.json";

    // ── Constantes geográficas — fuente única: GeoDataAlsasua ────────────
    const float E_ORIG    = (float)GeoDataAlsasua.UTM_E_ORIGIN;
    const float N_ORIG    = (float)GeoDataAlsasua.UTM_N_ORIGIN;
    const float UNITY_OX  = GeoDataAlsasua.UNITY_OX;
    const float UNITY_OZ  = GeoDataAlsasua.UNITY_OZ;
    // Z_MIN CRÍTICO: altitud mínima real del área (511.33m, de lidar_dtm_meta.json).
    // AltitudUnity = altitudReal - Z_MIN. Sin este offset todos los picos quedan ~9× fuera de rango.
    const float Z_MIN     = GeoDataAlsasua.Z_MIN;

    // ── NativeArray persistente del RAW LIDAR (nunca re-leído) ───────────
    NativeArray<ushort> _lidarRaw;
    LidarDtmMeta        _lidarMeta;
    bool                _rawCargado;

    // ── Estado público ────────────────────────────────────────────────────
    bool _aplicado;
    public bool Aplicado => _aplicado;

    float _ultimoRMSE = -1f;
    public float UltimoRMSE => _ultimoRMSE;

    bool metricasValidas = false;
    public bool MetricasValidas => metricasValidas;

    // ════════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        if (!aplicarAutomatico) yield break;
        // BUG FIX: bucle infinito sin timeout → si el terreno nunca aparece la corrutina
        // vive para siempre consumiendo un slot de corrutina por frame. Límite de 30s.
        float tw = 0f;
        while (Terrain.activeTerrain == null && tw < 30f) { tw += 0.3f; yield return new WaitForSeconds(0.3f); }
        if (Terrain.activeTerrain == null)
        {
            AlsasuaLogger.Warn("TerrenoAAA", "Terrain no disponible tras 30s — GeneradorTerreno inactivo");
            yield break;
        }
        yield return null;
        yield return StartCoroutine(AplicarMejorDTM());
    }

    void OnDestroy()
    {
        if (_lidarRaw.IsCreated) _lidarRaw.Dispose();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  API PÚBLICA
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Aplica el mejor DTM disponible. Puede llamarse desde Editor o runtime.
    /// </summary>
    // Snapshot heightmap para rollback si SetHeights falla por OOM
    float[,] _heightmapSnapshot;
    int      _snapshotRes;

    /// <summary>Restaura el heightmap al estado anterior a AplicarMejorDTM si algo falló.</summary>
    public void RollbackTerrain()
    {
        if (_heightmapSnapshot == null) return;
        var td = Terrain.activeTerrain?.terrainData;
        if (td == null) return;
        td.heightmapResolution = _snapshotRes;
        td.SetHeights(0, 0, _heightmapSnapshot);
        Terrain.activeTerrain.Flush();
        _heightmapSnapshot = null;
        AlsasuaLogger.Warn("TerrenoAAA", "Rollback heightmap aplicado — terrain restaurado al estado previo.");
    }

    public IEnumerator AplicarMejorDTM()
    {
        // Guardar snapshot antes de tocar heightmapResolution/SetHeights
        var tdSnap = Terrain.activeTerrain?.terrainData;
        if (tdSnap != null)
        {
            _snapshotRes       = tdSnap.heightmapResolution;
            _heightmapSnapshot = tdSnap.GetHeights(0, 0, _snapshotRes, _snapshotRes);
        }
        yield return null;

        string rutaRaw = FullPath(PATH_LIDAR_RAW);
        string rutaGnd = FullPath(PATH_LIDAR_GND);
        string ruta5m  = FullPath(PATH_DTM_5M);
        string rutaFb  = FullPath(PATH_DEM_FB);

        bool hayRaw  = File.Exists(rutaRaw) && new FileInfo(rutaRaw).Length > 1_000_000;
        bool hayGnd  = File.Exists(rutaGnd) && new FileInfo(rutaGnd).Length > 50_000;
        bool hay5m   = File.Exists(ruta5m)  && new FileInfo(ruta5m).Length  > 100_000;
        bool hayFb   = File.Exists(rutaFb)  && new FileInfo(rutaFb).Length  > 100_000;

        bool antesAplicado = _aplicado;
        _aplicado = false;

        if (hayRaw)
        {
            AlsasuaLogger.Info("TerrenoAAA",
                $"LIDAR 0.5m encontrado ({new FileInfo(rutaRaw).Length/1024/1024}MB). " +
                "Cargando heightmap 2049×2049...");
            yield return StartCoroutine(AplicarDesdeRAW_Bicubico(rutaRaw, hay5m ? ruta5m : null));

            if (hayGnd && puntosValidacion > 0)
                yield return StartCoroutine(ValidarYCorregir(rutaGnd));
        }
        else if (hayGnd)
        {
            AlsasuaLogger.Info("TerrenoAAA", "Usando lidar_ground.xyz como fuente primaria...");
            yield return StartCoroutine(AplicarDesdeXYZ(rutaGnd));
        }
        else if (hay5m)
        {
            AlsasuaLogger.Info("TerrenoAAA", "Usando DTM 5m IGN...");
            yield return StartCoroutine(AplicarDesdeASC(ruta5m));
        }
        else if (hayFb)
        {
            AlsasuaLogger.Info("TerrenoAAA", "Fallback a dem_unity_1025.raw...");
            yield return StartCoroutine(AplicarDesdeFallbackRAW(rutaFb));
        }
        else
        {
            AlsasuaLogger.Warn("TerrenoAAA",
                "Sin datos de elevación. Ejecuta PipelineLIDAR_Completo.py");
        }

        // Si _aplicado sigue false después de los intentos → rollback automático
        if (!_aplicado)
        {
            AlsasuaLogger.Error("TerrenoAAA",
                "AplicarMejorDTM no completó (_aplicado=false). Posible OOM. Ejecutando rollback.");
            RollbackTerrain();
            _aplicado = antesAplicado; // restaurar estado previo
        }
        else
        {
            // Éxito — liberar snapshot para no mantener un array enorme en RAM
            _heightmapSnapshot = null;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  FASE A+B — RAW 0.5m + RESAMPLE BICÚBICO + BLEND DTM5m BORDES
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator AplicarDesdeRAW_Bicubico(string pathRaw, string path5m)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) yield break;

        // ── Cargar metadatos ──────────────────────────────────────────────
        _lidarMeta = CargarMeta(FullPath(PATH_LIDAR_META));
        if (_lidarMeta == null) { AlsasuaLogger.Error("TerrenoAAA","Meta nula"); yield break; }

        int   srcRes  = _lidarMeta.heightmapResolution;  // 2049
        float lidarW  = _lidarMeta.terrainWidth;          // 1024m
        float lidarL  = _lidarMeta.terrainLength > 1f ? _lidarMeta.terrainLength : lidarW;
        float zMin    = _lidarMeta.z_min;
        float zRange  = _lidarMeta.z_max - zMin;

        AlsasuaLogger.Info("TerrenoAAA",
            $"Meta: {srcRes}px {lidarW}×{lidarL}m Z={zMin:F1}–{_lidarMeta.z_max:F1}m");

        // ── A. Leer RAW en NativeArray persistente (solo una vez) ─────────
        if (!_rawCargado)
        {
            yield return StartCoroutine(CargarRAWPersistente(pathRaw, srcRes));
            if (!_rawCargado) yield break;
        }

        // ── B. Cargar DTM 5m para montañas fuera del área LIDAR ──────────
        AscGrid dtm5m = null;
        if (path5m != null)
        {
            var taskDtm = Task.Run(() => LeerASCGrid(path5m));
            yield return EsperarTask(taskDtm, "DTM 5m");
            if (taskDtm.IsCompletedSuccessfully) dtm5m = taskDtm.Result;
        }
        yield return null;

        // ── C. Construir heightmap 2049×2049 con resample bicúbico ────────
        var td      = terrain.terrainData;
        int outRes  = resolucionTarget;
        td.heightmapResolution = outRes;

        Vector3 terSize = td.size;
        Vector3 terPos  = terrain.transform.position;

        AlsasuaLogger.Info("TerrenoAAA",
            $"Terreno Unity: {terSize.x:F0}×{terSize.z:F0}m, Y={terSize.y:F1}m pos={terPos}");

        // Precargar DEM existente (para blending fuera del LIDAR)
        float[,] demExist = null;
        if (dtm5m == null)
        {
            demExist = td.GetHeights(0, 0, outRes, outRes);
        }
        yield return null;

        // Límites del área LIDAR en coordenadas Unity
        float lidarMinX = UNITY_OX - lidarW * 0.5f;
        float lidarMinZ = UNITY_OZ - lidarL * 0.5f;
        float lidarMaxX = lidarMinX + lidarW;
        float lidarMaxZ = lidarMinZ + lidarL;

        float blend = blendGaussianoBordes;

        // Construir heightmap en trozos para no bloquear el frame
        var heights = new float[outRes, outRes];
        int lote = 0;

        for (int oy = 0; oy < outRes; oy++)
        {
            for (int ox = 0; ox < outRes; ox++)
            {
                // Posición Unity de este texel
                float ux = terPos.x + (float)ox / (outRes - 1) * terSize.x;
                float uz = terPos.z + (float)oy / (outRes - 1) * terSize.z;

                // Fracción dentro del LIDAR [0,1]
                float lx = (ux - lidarMinX) / lidarW;
                float lz = (uz - lidarMinZ) / lidarL;

                bool dentroLidar = lx >= 0f && lx <= 1f && lz >= 0f && lz <= 1f;

                float lidarH = 0f;
                if (dentroLidar)
                {
                    // Resample bicúbico Catmull-Rom del RAW LIDAR
                    lidarH = SampleBicubicRAW(_lidarRaw, srcRes, lx, lz, zMin, zRange, terSize.y);
                }

                // Fuente de referencia para zonas fuera o en blend
                float refH = 0f;
                if (dtm5m != null)
                {
                    // Usar DTM 5m para toda la zona fuera del LIDAR
                    // Convertir ux/uz → UTM → índice ASC
                    float utm_e = (ux - UNITY_OX) + E_ORIG;
                    float utm_n = (uz - UNITY_OZ) + N_ORIG;
                    refH = SampleASCUTM(dtm5m, utm_e, utm_n, terSize.y);
                }
                else if (demExist != null)
                {
                    int dox = Mathf.Clamp(ox, 0, outRes - 1);
                    int doy = Mathf.Clamp(oy, 0, outRes - 1);
                    refH = demExist[doy, dox];
                }

                // Blend gaussiano en bordes del LIDAR
                if (dentroLidar)
                {
                    float distBorde = Mathf.Min(
                        Mathf.Min(lx, 1f - lx) * lidarW,
                        Mathf.Min(lz, 1f - lz) * lidarL);
                    float t = GaussianBlend(distBorde, blend);
                    heights[oy, ox] = Mathf.Lerp(refH, lidarH, t);
                }
                else
                {
                    heights[oy, ox] = refH;
                }
            }

            if (++lote >= 16) { lote = 0; yield return null; }
        }

        // Clamp final: garantizar [0,1] en todos los píxeles por si alguna fuente da valores fuera de rango.
        for (int oy = 0; oy < outRes; oy++)
        for (int ox = 0; ox < outRes; ox++)
            heights[oy, ox] = Mathf.Clamp01(heights[oy, ox]);

        td.SetHeights(0, 0, heights);
        terrain.Flush();
        _aplicado = true;

        AlsasuaLogger.Info("TerrenoAAA",
            $"✅ DTM LIDAR 0.5m bicúbico aplicado: {outRes}² — " +
            $"terreno {terSize.x:F0}×{terSize.z:F0}m — " +
            $"LIDAR {lidarW:F0}×{lidarL:F0}m — Z {zMin:F0}–{_lidarMeta.z_max:F0}m");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CARGA RAW PERSISTENTE en NativeArray<ushort>
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator CargarRAWPersistente(string path, int expectedRes)
    {
        int expectedPx = expectedRes * expectedRes;

        var taskRaw = Task.Run(() =>
        {
            var bytes = File.ReadAllBytes(path);
            var buf   = new ushort[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, buf, 0, bytes.Length);
            return buf;
        });

        yield return EsperarTask(taskRaw, "RAW read");
        if (!taskRaw.IsCompletedSuccessfully) yield break;

        ushort[] raw = taskRaw.Result;
        if (raw == null || raw.Length < 1000) { AlsasuaLogger.Error("TerrenoAAA","RAW vacío"); yield break; }

        int actualRes = (int)Mathf.Sqrt(raw.Length);
        if (Mathf.Abs(actualRes * actualRes - raw.Length) > actualRes)
        {
            // Intentar con la resolución especificada en meta
            actualRes = expectedRes;
        }

        if (_lidarRaw.IsCreated) _lidarRaw.Dispose();
        _lidarRaw = new NativeArray<ushort>(raw.Length, Allocator.Persistent);
        _lidarRaw.CopyFrom(raw);
        _rawCargado = true;

        AlsasuaLogger.Info("TerrenoAAA",
            $"RAW cargado: {raw.Length} ushorts (~{raw.Length*2/1024/1024}MB) res≈{actualRes}");

        // Liberar array managed
        raw = null;
        GC.Collect();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  RESAMPLE BICÚBICO CATMULL-ROM del NativeArray RAW
    // ════════════════════════════════════════════════════════════════════════

    static float SampleBicubicRAW(NativeArray<ushort> raw, int res,
                                   float fx, float fz,
                                   float zMin, float zRange, float terY)
    {
        // Coordenadas en píxeles del srcRes
        float px = Mathf.Clamp(fx * (res - 1), 0.5f, res - 1.5f);
        float pz = Mathf.Clamp(fz * (res - 1), 0.5f, res - 1.5f);

        int ix = (int)px, iz = (int)pz;
        float tx = px - ix, tz = pz - iz;

        // Kernel Catmull-Rom 1D
        float CatmullRom(float t, float p0, float p1, float p2, float p3)
        {
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t
            );
        }

        float GetH(int r, int c)
        {
            r = Mathf.Clamp(r, 0, res - 1);
            c = Mathf.Clamp(c, 0, res - 1);
            int idx = r * res + c;
            float rawVal = idx < raw.Length ? raw[idx] : 0;
            float altM = rawVal / 65535f * zRange + zMin;
            // CORRECCIÓN Z_MIN: restar Z_MIN (511.33m) antes de normalizar.
            // Sin este offset el heightmap quedaba desplazado ~8.93 unidades normalizadas (completamente incorrecto).
            return Mathf.Clamp01((altM - Z_MIN) / terY);
        }

        // 4 filas de interpolación horizontal
        float r0 = CatmullRom(tx, GetH(iz-1,ix-1), GetH(iz-1,ix), GetH(iz-1,ix+1), GetH(iz-1,ix+2));
        float r1 = CatmullRom(tx, GetH(iz,  ix-1), GetH(iz,  ix), GetH(iz,  ix+1), GetH(iz,  ix+2));
        float r2 = CatmullRom(tx, GetH(iz+1,ix-1), GetH(iz+1,ix), GetH(iz+1,ix+1), GetH(iz+1,ix+2));
        float r3 = CatmullRom(tx, GetH(iz+2,ix-1), GetH(iz+2,ix), GetH(iz+2,ix+1), GetH(iz+2,ix+2));

        return Mathf.Clamp01(CatmullRom(tz, r0, r1, r2, r3));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  VALIDACIÓN RMSE + CORRECCIÓN LOCAL GAUSSIAN SPLAT
    // ════════════════════════════════════════════════════════════════════════

    // ── Struct para métricas de validación ───────────────────────────────────
    struct ValidationMetrics
    {
        public float rmse, mae, maxError, minError, p95;
        public int   count;
        // Zonas: 0=urbana(<540m Unity=<28.67m), 1=ladera(540-560m Unity=28.67-48.67m), 2=montaña(>560m Unity>48.67m)
        public int[]   zoneCount;
        public double[] zoneSumSq;
        // Histograma: [0-0.1, 0.1-0.3, 0.3-0.5, 0.5-1, >1]
        public int[] hist;
    }

    // Umbrales de altitud Unity para zonas (altitudReal - Z_MIN)
    // urbana: altReal < 540m  → unityY < 540 - 511.33 = 28.67m
    // ladera: 540-560m        → unityY 28.67-48.67m
    // montaña: >560m          → unityY > 48.67m
    const float ZONA_URBANA_MAX  = 540f - GeoDataAlsasua.Z_MIN;   // ~28.67m
    const float ZONA_LADERA_MAX  = 560f - GeoDataAlsasua.Z_MIN;   // ~48.67m

    static ValidationMetrics CalcularMetricas(List<(Vector3 pos, float delta)> errores)
    {
        var m = new ValidationMetrics
        {
            zoneCount = new int[3],
            zoneSumSq = new double[3],
            hist      = new int[5],
            minError  = float.MaxValue,
            maxError  = float.MinValue
        };

        double sumSq = 0.0, sumAbs = 0.0;
        var absErrs = new List<float>(errores.Count);

        foreach (var (pos, delta) in errores)
        {
            float ab = Mathf.Abs(delta);
            sumSq  += delta * delta;
            sumAbs += ab;
            if (ab > m.maxError) m.maxError = ab;
            if (ab < m.minError) m.minError = ab;
            absErrs.Add(ab);
            m.count++;

            // Zona por altura Unity del punto (pos.y = altReal - Z_MIN)
            int zona = pos.y < ZONA_URBANA_MAX ? 0 : (pos.y < ZONA_LADERA_MAX ? 1 : 2);
            m.zoneCount[zona]++;
            m.zoneSumSq[zona] += delta * delta;

            // Histograma
            if      (ab < 0.1f) m.hist[0]++;
            else if (ab < 0.3f) m.hist[1]++;
            else if (ab < 0.5f) m.hist[2]++;
            else if (ab < 1.0f) m.hist[3]++;
            else                m.hist[4]++;
        }

        if (m.count == 0) return m;
        m.rmse = Mathf.Sqrt((float)(sumSq  / m.count));
        m.mae  = (float)(sumAbs / m.count);

        // Percentil 95
        absErrs.Sort();
        int p95idx = Mathf.Clamp(Mathf.RoundToInt(absErrs.Count * 0.95f), 0, absErrs.Count - 1);
        m.p95 = absErrs[p95idx];

        return m;
    }

    static void LogMetricas(ValidationMetrics m)
    {
        // Formato parseable
        Debug.Log($"[TerrainRMSE] RMSE={m.rmse:F3}m MAE={m.mae:F3}m MAX={m.maxError:F3}m P95={m.p95:F3}m");

        float[] zRmse = new float[3];
        for (int z = 0; z < 3; z++)
            zRmse[z] = m.zoneCount[z] > 0
                ? Mathf.Sqrt((float)(m.zoneSumSq[z] / m.zoneCount[z]))
                : 0f;
        Debug.Log($"[TerrainRMSE] Zonas: urbana={m.zoneCount[0]} pts rmse={zRmse[0]:F3}m | " +
                  $"ladera={m.zoneCount[1]} pts rmse={zRmse[1]:F3}m | " +
                  $"montaña={m.zoneCount[2]} pts rmse={zRmse[2]:F3}m");

        float total = Mathf.Max(1, m.count);
        Debug.Log($"[TerrainRMSE] Histograma: " +
                  $"<0.1m={m.hist[0]*100f/total:F1}% | " +
                  $"0.1-0.3m={m.hist[1]*100f/total:F1}% | " +
                  $"0.3-0.5m={m.hist[2]*100f/total:F1}% | " +
                  $"0.5-1m={m.hist[3]*100f/total:F1}% | " +
                  $">1m={m.hist[4]*100f/total:F1}%");

        string estado = m.rmse < 0.3f ? "OK" : (m.rmse < 0.6f ? "ADVERTENCIA" : "CRITICO");
        Debug.Log($"[TerrainRMSE] Estado: {estado} (threshold 0.3m)");
    }

    [Serializable]
    class TerrainValidationReport
    {
        public string timestamp;
        public float  rmse, mae, max_error, p95;
        public int    puntos_validados;
        public int[]  histograma;
        public ZonaReport[] zonas;

        [Serializable]
        public class ZonaReport { public string nombre; public int puntos; public float rmse; }
    }

    static void GuardarReporte(ValidationMetrics m, string path)
    {
        float[] zRmse = new float[3];
        for (int z = 0; z < 3; z++)
            zRmse[z] = m.zoneCount[z] > 0
                ? Mathf.Sqrt((float)(m.zoneSumSq[z] / m.zoneCount[z]))
                : 0f;

        var report = new TerrainValidationReport
        {
            timestamp        = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            rmse             = m.rmse,
            mae              = m.mae,
            max_error        = m.maxError,
            p95              = m.p95,
            puntos_validados = m.count,
            histograma       = m.hist,
            zonas            = new[]
            {
                new TerrainValidationReport.ZonaReport { nombre="urbana",  puntos=m.zoneCount[0], rmse=zRmse[0] },
                new TerrainValidationReport.ZonaReport { nombre="ladera",  puntos=m.zoneCount[1], rmse=zRmse[1] },
                new TerrainValidationReport.ZonaReport { nombre="montana", puntos=m.zoneCount[2], rmse=zRmse[2] },
            }
        };

        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            AlsasuaLogger.Info("TerrenoAAA", $"Reporte guardado: {path}");
        }
        catch (Exception ex)
        {
            AlsasuaLogger.Warn("TerrenoAAA", $"No se pudo guardar reporte: {ex.Message}");
        }
    }

    IEnumerator ValidarYCorregir(string pathXYZ)
    {
        AlsasuaLogger.Info("TerrenoAAA",
            $"Iniciando validación RMSE con {puntosValidacion} puntos...");

        // Leer XYZ en background
        var taskXyz = Task.Run(() =>
        {
            var lista = new List<Vector3>();
            var ci    = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var line in File.ReadLines(pathXYZ))
            {
                var tok = line.Split(' ', '\t');
                if (tok.Length < 3) continue;
                if (float.TryParse(tok[0], System.Globalization.NumberStyles.Float, ci, out float x)
                 && float.TryParse(tok[1], System.Globalization.NumberStyles.Float, ci, out float y)
                 && float.TryParse(tok[2], System.Globalization.NumberStyles.Float, ci, out float z))
                {
                    // Y almacenada ya en espacio Unity (altitudReal - Z_MIN) para que la comparación RMSE sea coherente.
                    lista.Add(new Vector3(x + UNITY_OX, y - Z_MIN, z + UNITY_OZ));
                }
            }
            return lista;
        });

        yield return EsperarTask(taskXyz, "XYZ validacion");
        if (!taskXyz.IsCompletedSuccessfully) yield break;

        var puntos = taskXyz.Result;
        if (puntos == null || puntos.Count < 50)
        {
            AlsasuaLogger.Warn("TerrenoAAA", "Muy pocos puntos XYZ para validar");
            yield break;
        }
        yield return null;

        var terrain = Terrain.activeTerrain;
        if (terrain == null) yield break;
        var td      = terrain.terrainData;

        // Subsamplear para N puntos de validación
        int paso  = Mathf.Max(1, puntos.Count / puntosValidacion);
        var errores = new List<(Vector3 pos, float delta)>(puntos.Count / paso + 1);

        for (int i = 0; i < puntos.Count; i += paso)
        {
            var p  = puntos[i];
            // terrain.SampleHeight devuelve altura Unity (altReal - Z_MIN); p.y ya tiene el mismo offset.
            float yTerrain = terrain.SampleHeight(new Vector3(p.x, 0, p.z))
                           + terrain.transform.position.y;
            float delta = p.y - yTerrain;
            errores.Add((p, delta));
        }

        if (errores.Count == 0) yield break;

        var m = CalcularMetricas(errores);
        _ultimoRMSE = m.rmse;
        LogMetricas(m);

        // ── Calcular RMSE por zona para decidir radio ─────────────────────
        float rmseUrbana  = m.zoneCount[0] > 0 ? Mathf.Sqrt((float)(m.zoneSumSq[0] / m.zoneCount[0])) : 0f;
        float rmseMontana = m.zoneCount[2] > 0 ? Mathf.Sqrt((float)(m.zoneSumSq[2] / m.zoneCount[2])) : 0f;

        if (m.rmse < rmseMaxMetros)
        {
            AlsasuaLogger.Info("TerrenoAAA", "RMSE < umbral. Corrección omitida.");
            yield break;
        }

        // ── Radio dinámico según RMSE por zona ───────────────────────────
        float splatR = 2f;
        if (rmseMontana > 1f)        splatR = 5f;
        else if (rmseUrbana > 0.5f)  splatR = 3f;

        float splatR2 = splatR * splatR;
        float sigSq   = splatR2 * 0.5f;

        int outRes  = td.heightmapResolution;
        float terW  = td.size.x;
        float terH  = td.size.z;
        float terY  = td.size.y;
        Vector3 terPos = terrain.transform.position;

        float[,] heights = td.GetHeights(0, 0, outRes, outRes);

        float umbralError = m.rmse * 0.5f;
        int corregidos = 0;

        foreach (var (pos, delta) in errores)
        {
            if (Mathf.Abs(delta) < umbralError) continue;

            float nx = Mathf.Clamp01((pos.x - terPos.x) / terW);
            float nz = Mathf.Clamp01((pos.z - terPos.z) / terH);
            int cx   = Mathf.RoundToInt(nx * (outRes - 1));
            int cz   = Mathf.RoundToInt(nz * (outRes - 1));

            int radio_px = Mathf.CeilToInt(splatR / terW * (outRes - 1)) + 2;
            float normDelta = delta / terY;

            for (int dy = -radio_px; dy <= radio_px; dy++)
            for (int dx = -radio_px; dx <= radio_px; dx++)
            {
                int hx = cx + dx, hz = cz + dy;
                if (hx < 0 || hz < 0 || hx >= outRes || hz >= outRes) continue;

                float wx = dx / (float)(outRes - 1) * terW;
                float wz = dy / (float)(outRes - 1) * terH;
                float d2 = wx * wx + wz * wz;
                if (d2 > splatR2) continue;

                float peso = Mathf.Exp(-d2 / (2f * sigSq));
                heights[hz, hx] = Mathf.Clamp01(heights[hz, hx] + normDelta * peso);
            }
            corregidos++;
        }

        Debug.Log($"[TerrainCorrection] Aplicando {corregidos} splats gaussianos r={splatR:F0}m");

        if (corregidos > 0)
        {
            td.SetHeights(0, 0, heights);
            terrain.Flush();
            AlsasuaLogger.Info("TerrenoAAA",
                $"Corrección Gaussian aplicada a {corregidos} puntos. RMSE inicial: {m.rmse:F3}m");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  VALIDACIÓN EDITOR — ValidarTerreno()
    //  Llamable desde [ContextMenu] o desde TestsAltsasua
    // ════════════════════════════════════════════════════════════════════════

    [ContextMenu("Validar Terreno (RMSE + Flujo Arakil)")]
    public void ValidarTerreno()
    {
        StartCoroutine(ValidarTerrenoCoroutine());
    }

    IEnumerator ValidarTerrenoCoroutine()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) { AlsasuaLogger.Error("Validacion","Sin terreno activo"); yield break; }

        AlsasuaLogger.Info("Validacion","═══ VALIDACIÓN GEOGRÁFICA ALSASUA ═══");

        // ── 1. RMSE completo contra TODOS los puntos de lidar_ground.xyz ──
        string pathGnd = FullPath(PATH_LIDAR_GND);
        ValidationMetrics metricas = default;

        if (File.Exists(pathGnd))
        {
            AlsasuaLogger.Info("Validacion","Leyendo TODOS los puntos lidar_ground.xyz...");

            // Leer todos los puntos sin límite
            var taskXyz = Task.Run(() =>
            {
                var lista = new List<Vector3>();
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                foreach (var line in File.ReadLines(pathGnd))
                {
                    var tok = line.Split(' ', '\t');
                    if (tok.Length < 3) continue;
                    if (float.TryParse(tok[0], System.Globalization.NumberStyles.Float, ci, out float x)
                     && float.TryParse(tok[1], System.Globalization.NumberStyles.Float, ci, out float y)
                     && float.TryParse(tok[2], System.Globalization.NumberStyles.Float, ci, out float z))
                        // Y con offset Z_MIN para comparar con terrain.SampleHeight (espacio Unity).
                        lista.Add(new Vector3(x + UNITY_OX, y - Z_MIN, z + UNITY_OZ));
                }
                return lista;
            });
            yield return EsperarTask(taskXyz, "XYZ validacion completa");

            if (taskXyz.IsCompletedSuccessfully && taskXyz.Result.Count > 0)
            {
                var pts = taskXyz.Result;
                AlsasuaLogger.Info("Validacion", $"Puntos cargados: {pts.Count}. Calculando errores...");
                yield return null;

                // Calcular errores para todos los puntos
                var errores = new List<(Vector3 pos, float delta)>(pts.Count);
                int lote = 0;
                for (int i = 0; i < pts.Count; i++)
                {
                    float yT = terrain.SampleHeight(new Vector3(pts[i].x, 0, pts[i].z))
                             + terrain.transform.position.y;
                    errores.Add((pts[i], pts[i].y - yT));
                    if (++lote >= 5000) { lote = 0; yield return null; }
                }

                metricas      = CalcularMetricas(errores);
                _ultimoRMSE   = metricas.rmse;
                metricasValidas = true;
                LogMetricas(metricas);

                // Guardar reporte JSON
                string reportPath = FullPath(PATH_VAL_REPORT);
                GuardarReporte(metricas, reportPath);
            }
        }
        else AlsasuaLogger.Warn("Validacion","lidar_ground.xyz no encontrado");

        // ── 2. Verificar flujo del Arakil hacia ONO ───────────────────────
        // El Arakil fluye aproximadamente de E a O pasando por Alsasua
        // Muestreamos 5 puntos a lo largo del cauce y verificamos descenso
        var arakilPts = new Vector3[]
        {
            new(2400f, 0, 8400f),  // aguas arriba (E)
            new(2000f, 0, 8350f),
            new(1700f, 0, 8300f),
            new(1300f, 0, 8250f),
            new( 900f, 0, 8200f),  // aguas abajo (O)
        };

        AlsasuaLogger.Info("Validacion","Flujo Arakil (debe descender E→O):");
        float altAnterior = float.MaxValue;
        bool flujoOK = true;
        foreach (var p in arakilPts)
        {
            float alt = terrain.SampleHeight(new Vector3(p.x, 0, p.z))
                      + terrain.transform.position.y;
            bool baja = alt <= altAnterior + 0.5f; // tolerancia 0.5m
            if (!baja && altAnterior < float.MaxValue) flujoOK = false;
            AlsasuaLogger.Info("Validacion",
                $"  X={p.x:F0} Z={p.z:F0} → alt={alt:F1}m {(baja ? "↓✓" : "↑✗")}");
            altAnterior = alt;
        }

        AlsasuaLogger.Info("Validacion",
            flujoOK ? "✅ Flujo Arakil correcto (descenso E→O)" :
                     "⚠ Flujo Arakil incorrecto — revisar heightmap en cauce");

        // ── 3. Altitudes de referencia ────────────────────────────────────
        AlsasuaLogger.Info("Validacion","Altitudes de referencia Alsasua:");
        var refsAlt = new (string nombre, Vector3 pos, float altEsperada)[]
        {
            ("Herriko Plaza",     new(GeoDataAlsasua.OX, 0, GeoDataAlsasua.OZ), 547f),
            ("Estacion de tren",  new(1650f, 0, 8200f), 540f),
            ("N. Carretera N-1",  new(1900f, 0, 8000f), 525f),
        };
        foreach (var (nombre, pos, esp) in refsAlt)
        {
            float alt = terrain.SampleHeight(new Vector3(pos.x, 0, pos.z))
                      + terrain.transform.position.y;
            float err = alt - esp;
            AlsasuaLogger.Info("Validacion",
                $"  {nombre}: {alt:F1}m (esperado~{esp:F0}m, err={err:+F1;-F1}m)");
        }

        AlsasuaLogger.Info("Validacion","═══ FIN VALIDACIÓN ═══");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ASC READER + SAMPLER (DTM 5m IGN — UTM 30N ETRS89)
    // ════════════════════════════════════════════════════════════════════════

    class AscGrid
    {
        public int   ncols, nrows;
        public float xll, yll, cell, nodata;
        public bool  isUTM;
        public float[] data;
        public float zMin, zMax;
    }

    static AscGrid LeerASCGrid(string path)
    {
        var g = new AscGrid();
        using var r = new StreamReader(path);
        var ci = System.Globalization.CultureInfo.InvariantCulture;

        g.ncols  = int.Parse(  r.ReadLine().Split()[1]);
        g.nrows  = int.Parse(  r.ReadLine().Split()[1]);
        g.xll    = float.Parse(r.ReadLine().Split()[1], ci);
        g.yll    = float.Parse(r.ReadLine().Split()[1], ci);
        g.cell   = float.Parse(r.ReadLine().Split()[1], ci);
        g.nodata = float.Parse(r.ReadLine().Split()[1], ci);
        g.isUTM  = g.xll > 100_000;

        g.data   = new float[g.ncols * g.nrows];
        int idx = 0; string line;
        while ((line = r.ReadLine()) != null)
            foreach (var tok in line.Split(' ', '\t'))
                if (!string.IsNullOrEmpty(tok) && idx < g.data.Length)
                    g.data[idx++] = float.Parse(tok, ci);

        g.zMin = float.MaxValue; g.zMax = float.MinValue;
        foreach (var v in g.data)
            if (!Mathf.Approximately(v, g.nodata))
            { if (v < g.zMin) g.zMin = v; if (v > g.zMax) g.zMax = v; }

        return g;
    }

    /// Sample del DTM 5m dado coordenadas UTM, devuelve valor normalizado [0,1] para terY
    float SampleASCUTM(AscGrid g, float utm_e, float utm_n, float terY)
    {
        if (g == null || g.data == null) return 0f;

        int col, row;
        if (g.isUTM)
        {
            col = Mathf.FloorToInt((utm_e - g.xll) / g.cell);
            row = g.nrows - 1 - Mathf.FloorToInt((utm_n - g.yll) / g.cell);
        }
        else
        {
            // Coordenadas geográficas — convertir UTM a lat/lon aproximado
            float lon_ = (utm_e - E_ORIG) / 76400f + (-2.1677f);
            float lat_ = (utm_n - N_ORIG) / 111320f + 42.8987f;
            col = Mathf.FloorToInt((lon_ - g.xll) / g.cell);
            row = g.nrows - 1 - Mathf.FloorToInt((lat_ - g.yll) / g.cell);
        }

        col = Mathf.Clamp(col, 0, g.ncols - 1);
        row = Mathf.Clamp(row, 0, g.nrows - 1);

        float z = g.data[row * g.ncols + col];
        if (Mathf.Approximately(z, g.nodata)) z = g.zMin;

        // CORRECCIÓN Z_MIN: restar Z_MIN (511.33m) para que altitudUnity = altitudReal - Z_MIN.
        // Mismo offset que GetH() en SampleBicubicRAW — imprescindible para coherencia entre fuentes.
        return Mathf.Clamp01((z - Z_MIN) / terY);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ASC COROUTINE WRAPPER
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator AplicarDesdeASC(string path)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) yield break;

        var taskAsc = Task.Run(() => LeerASCGrid(path));
        yield return EsperarTask(taskAsc, "ASC 5m");
        if (!taskAsc.IsCompletedSuccessfully) yield break;

        var g   = taskAsc.Result;
        var td  = terrain.terrainData;
        int res = resolucionTarget;
        td.heightmapResolution = res;

        float terW = td.size.x, terH = td.size.z, terY = td.size.y;
        Vector3 terPos = terrain.transform.position;

        var heights = new float[res, res];
        for (int oy = 0; oy < res; oy++)
        for (int ox = 0; ox < res; ox++)
        {
            float ux = terPos.x + (float)ox / (res - 1) * terW;
            float uz = terPos.z + (float)oy / (res - 1) * terH;
            float utm_e = (ux - UNITY_OX) + E_ORIG;
            float utm_n = (uz - UNITY_OZ) + N_ORIG;
            heights[oy, ox] = SampleASCUTM(g, utm_e, utm_n, terY);
        }

        td.SetHeights(0, 0, heights);
        terrain.Flush();
        _aplicado = true;
        AlsasuaLogger.Info("TerrenoAAA", $"✅ DTM 5m aplicado: {res}²");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  XYZ COROUTINE (587k puntos → heightmap)
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator AplicarDesdeXYZ(string path)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) yield break;

        var taskXyz = Task.Run(() =>
        {
            var lista = new List<Vector3>();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var line in File.ReadLines(path))
            {
                var tok = line.Split(' ', '\t');
                if (tok.Length < 3) continue;
                if (float.TryParse(tok[0], System.Globalization.NumberStyles.Float, ci, out float x)
                 && float.TryParse(tok[1], System.Globalization.NumberStyles.Float, ci, out float y)
                 && float.TryParse(tok[2], System.Globalization.NumberStyles.Float, ci, out float z))
                    lista.Add(new Vector3(x + UNITY_OX, y, z + UNITY_OZ));
            }
            return lista;
        });
        yield return EsperarTask(taskXyz, "XYZ read");
        if (!taskXyz.IsCompletedSuccessfully) yield break;

        var puntos = taskXyz.Result;
        AlsasuaLogger.Info("TerrenoAAA", $"XYZ: {puntos.Count} puntos");
        yield return null;

        var td  = terrain.terrainData;
        int res = Mathf.Min(resolucionTarget, 2049);
        td.heightmapResolution = res;

        float terW = td.size.x, terH = td.size.z, terY = td.size.y;
        Vector3 terPos = terrain.transform.position;

        // Use absolute altitude datum so fallback path matches primary RAW path
        float zMin = Z_MIN;
        float zRange = terY;
        if (zRange < 1f) yield break;

        var sumZ   = new float[res, res];
        var countZ = new int  [res, res];

        foreach (var p in puntos)
        {
            int hx = Mathf.Clamp(Mathf.RoundToInt((p.x - terPos.x) / terW * (res - 1)), 0, res - 1);
            int hz = Mathf.Clamp(Mathf.RoundToInt((p.z - terPos.z) / terH * (res - 1)), 0, res - 1);
            sumZ[hz, hx]   += p.y;
            countZ[hz, hx] ++;
        }
        yield return null;

        var heights = new float[res, res];
        for (int hy = 0; hy < res; hy++)
        for (int hx = 0; hx < res; hx++)
            heights[hy, hx] = countZ[hy, hx] > 0
                ? Mathf.Clamp01((sumZ[hy, hx] / countZ[hy, hx] - zMin) / terY)
                : 0f;

        // Fill holes
        for (int iter = 0; iter < 4; iter++)
        for (int hy = 1; hy < res - 1; hy++)
        for (int hx = 1; hx < res - 1; hx++)
            if (countZ[hy, hx] == 0)
            {
                int n = 0; float s = 0;
                if (countZ[hy-1,hx]>0){s+=heights[hy-1,hx];n++;}
                if (countZ[hy+1,hx]>0){s+=heights[hy+1,hx];n++;}
                if (countZ[hy,hx-1]>0){s+=heights[hy,hx-1];n++;}
                if (countZ[hy,hx+1]>0){s+=heights[hy,hx+1];n++;}
                if (n > 0) heights[hy, hx] = s / n;
            }

        int nPuntos = puntos.Count;
        puntos.Clear(); puntos = null; GC.Collect();
        yield return null;

        td.SetHeights(0, 0, heights);
        terrain.Flush();
        _aplicado = true;
        AlsasuaLogger.Info("TerrenoAAA", $"✅ XYZ → heightmap {res}² ({nPuntos} puntos)");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  FALLBACK RAW 1025
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator AplicarDesdeFallbackRAW(string path)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) yield break;

        var taskRaw = Task.Run(() =>
        {
            var bytes = File.ReadAllBytes(path);
            var buf   = new ushort[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, buf, 0, bytes.Length);
            return buf;
        });
        yield return EsperarTask(taskRaw, "FallbackRAW");
        if (!taskRaw.IsCompletedSuccessfully) yield break;

        var raw    = taskRaw.Result;
        int srcRes = (int)Mathf.Sqrt(raw.Length);

        var td  = terrain.terrainData;
        td.heightmapResolution = srcRes;

        var heights = new float[srcRes, srcRes];
        for (int hy = 0; hy < srcRes; hy++)
        for (int hx = 0; hx < srcRes; hx++)
        {
            int idx = hy * srcRes + hx;
            heights[hy, hx] = idx < raw.Length ? raw[idx] / 65535f : 0f;
        }

        td.SetHeights(0, 0, heights);
        terrain.Flush();
        _aplicado = true;
        AlsasuaLogger.Info("TerrenoAAA", $"✅ Fallback RAW {srcRes}² aplicado");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════════════════

    /// Blend gaussiano suave: 0 en borde, 1 en interior (a partir de distBlend metros)
    static float GaussianBlend(float dist, float sigma)
    {
        if (sigma <= 0f) return dist > 0f ? 1f : 0f;
        float t = Mathf.Clamp01(dist / sigma);
        // S-curve suave (smoothstep sobre exponencial)
        return 1f - Mathf.Exp(-t * t * 3f);
    }

    static LidarDtmMeta CargarMeta(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var meta = JsonUtility.FromJson<LidarDtmMeta>(File.ReadAllText(path));
            if (meta.terrainLength < 1f) meta.terrainLength = meta.terrainWidth;
            return meta;
        }
        catch { return null; }
    }

    static string FullPath(string rel)
        => Path.Combine(Application.dataPath.Replace("Assets", ""), rel);

    static IEnumerator EsperarTask(Task task, string nombre)
    {
        while (!task.IsCompleted) yield return null;
        if (task.IsFaulted)
        {
            var ex = task.Exception?.InnerException ?? task.Exception;
            AlsasuaLogger.Error("TerrenoAAA", $"Task '{nombre}': {ex?.GetType().Name} — {ex?.Message}");
        }
    }

    static IEnumerator EsperarTask<T>(Task<T> task, string nombre)
    {
        while (!task.IsCompleted) yield return null;
        if (task.IsFaulted)
        {
            var ex = task.Exception?.InnerException ?? task.Exception;
            AlsasuaLogger.Error("TerrenoAAA", $"Task '{nombre}': {ex?.GetType().Name} — {ex?.Message}");
        }
    }

    [Serializable]
    class LidarDtmMeta
    {
        public int   heightmapResolution;
        public float terrainWidth, terrainLength, terrainHeight;
        public float z_min, z_max, res_m;
        public float E_origin, N_origin, unity_ox, unity_oz;
    }
}
