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

    [Tooltip("Número de puntos para validación RMSE (0 = desactivar)")]
    public int puntosValidacion = 2000;

    [Tooltip("Ancho de blend gaussiano en metros entre LIDAR y DTM5m en los bordes")]
    public float blendGaussianoBordes = 200f;

    [Header("Debug")]
    public bool logVerboso = true;

    // ── Rutas de datos ────────────────────────────────────────────────────
    const string PATH_LIDAR_RAW  = "Assets/AlsasuaData/lidar_dtm_05m.raw";
    const string PATH_LIDAR_META = "Assets/AlsasuaData/lidar_dtm_meta.json";
    const string PATH_LIDAR_GND  = "Assets/AlsasuaData/lidar_ground.xyz";
    const string PATH_DTM_5M     = "Assets/AlsasuaData/dtm_alsasua_5m.asc";
    const string PATH_DEM_FB     = "Assets/AlsasuaData/dem_unity_1025.raw";

    // ── Constantes geográficas ────────────────────────────────────────────
    const float E_ORIG    = 567951f;
    const float N_ORIG    = 4749902f;
    const float UNITY_OX  = 1918f;
    const float UNITY_OZ  = 8570f;

    // ── NativeArray persistente del RAW LIDAR (nunca re-leído) ───────────
    NativeArray<ushort> _lidarRaw;
    LidarDtmMeta        _lidarMeta;
    bool                _rawCargado;

    // ── Estado público ────────────────────────────────────────────────────
    bool _aplicado;
    public bool Aplicado => _aplicado;

    float _ultimoRMSE = -1f;
    public float UltimoRMSE => _ultimoRMSE;

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
        while (Terrain.activeTerrain == null) yield return new WaitForSeconds(0.3f);
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
    public IEnumerator AplicarMejorDTM()
    {
        string rutaRaw = FullPath(PATH_LIDAR_RAW);
        string rutaGnd = FullPath(PATH_LIDAR_GND);
        string ruta5m  = FullPath(PATH_DTM_5M);
        string rutaFb  = FullPath(PATH_DEM_FB);

        bool hayRaw  = File.Exists(rutaRaw) && new FileInfo(rutaRaw).Length > 1_000_000;
        bool hayGnd  = File.Exists(rutaGnd) && new FileInfo(rutaGnd).Length > 50_000;
        bool hay5m   = File.Exists(ruta5m)  && new FileInfo(ruta5m).Length  > 100_000;
        bool hayFb   = File.Exists(rutaFb)  && new FileInfo(rutaFb).Length  > 100_000;

        if (hayRaw)
        {
            AlsasuaLogger.Info("TerrenoAAA",
                $"LIDAR 0.5m encontrado ({new FileInfo(rutaRaw).Length/1024/1024}MB). " +
                "Cargando heightmap 2049×2049...");
            yield return StartCoroutine(AplicarDesdeRAW_Bicubico(rutaRaw, hay5m ? ruta5m : null));

            // Validación post-aplicación
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
            return Mathf.Clamp01((altM - 0f) / terY);  // normalizado [0,1] respecto TerrainData.size.y
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
                    lista.Add(new Vector3(x + UNITY_OX, y, z + UNITY_OZ));
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
        var errores = new List<(Vector3 pos, float delta)>();
        double sumSq = 0.0;
        int count    = 0;

        for (int i = 0; i < puntos.Count; i += paso)
        {
            var p  = puntos[i];
            float yTerrain = terrain.SampleHeight(new Vector3(p.x, 0, p.z))
                           + terrain.transform.position.y;
            float delta = p.y - yTerrain;
            sumSq += delta * delta;
            count++;
            errores.Add((p, delta));
        }

        if (count == 0) yield break;
        float rmse = Mathf.Sqrt((float)(sumSq / count));
        _ultimoRMSE = rmse;

        AlsasuaLogger.Info("TerrenoAAA",
            $"Validación RMSE: {rmse:F3}m ({count} pts) — umbral={rmseMaxMetros:F2}m");

        if (rmse <= rmseMaxMetros)
        {
            AlsasuaLogger.Info("TerrenoAAA", "✅ RMSE dentro del umbral. Sin corrección necesaria.");
            yield break;
        }

        // ── Corrección local Gaussian splat r=2m para puntos con gran error ──
        AlsasuaLogger.Info("TerrenoAAA",
            $"RMSE > umbral. Aplicando corrección Gaussian splat r=2m...");

        int outRes  = td.heightmapResolution;
        float terW  = td.size.x;
        float terH  = td.size.z;
        float terY  = td.size.y;
        Vector3 terPos = terrain.transform.position;

        float[,] heights = td.GetHeights(0, 0, outRes, outRes);

        const float SPLAT_R  = 2f;   // radio en metros
        const float SPLAT_R2 = SPLAT_R * SPLAT_R;
        float sigSq = SPLAT_R2 * 0.5f;

        // Solo puntos con error > rmse*0.5
        int corregidos = 0;
        float umbralError = rmse * 0.5f;

        foreach (var (pos, delta) in errores)
        {
            if (Mathf.Abs(delta) < umbralError) continue;

            float nx = Mathf.Clamp01((pos.x - terPos.x) / terW);
            float nz = Mathf.Clamp01((pos.z - terPos.z) / terH);
            int cx   = Mathf.RoundToInt(nx * (outRes - 1));
            int cz   = Mathf.RoundToInt(nz * (outRes - 1));

            int radio_px = Mathf.CeilToInt(SPLAT_R / terW * (outRes - 1)) + 2;
            float normDelta = delta / terY;

            for (int dy = -radio_px; dy <= radio_px; dy++)
            for (int dx = -radio_px; dx <= radio_px; dx++)
            {
                int hx = cx + dx, hz = cz + dy;
                if (hx < 0 || hz < 0 || hx >= outRes || hz >= outRes) continue;

                float wx = dx / (float)(outRes - 1) * terW;
                float wz = dy / (float)(outRes - 1) * terH;
                float d2 = wx * wx + wz * wz;
                if (d2 > SPLAT_R2) continue;

                float peso = Mathf.Exp(-d2 / (2f * sigSq));
                heights[hz, hx] = Mathf.Clamp01(heights[hz, hx] + normDelta * peso);
            }
            corregidos++;
        }

        if (corregidos > 0)
        {
            td.SetHeights(0, 0, heights);
            terrain.Flush();
            AlsasuaLogger.Info("TerrenoAAA",
                $"✅ Corrección Gaussian aplicada a {corregidos} puntos. " +
                $"RMSE inicial: {rmse:F3}m");
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

        // ── 1. RMSE contra lidar_ground.xyz ──────────────────────────────
        string pathGnd = FullPath(PATH_LIDAR_GND);
        if (File.Exists(pathGnd))
        {
            var taskXyz = Task.Run(() =>
            {
                var lista = new List<Vector3>();
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                int n = 0;
                foreach (var line in File.ReadLines(pathGnd))
                {
                    if (n++ > 10000) break; // limitar para velocidad en Editor
                    var tok = line.Split(' ', '\t');
                    if (tok.Length < 3) continue;
                    if (float.TryParse(tok[0], System.Globalization.NumberStyles.Float, ci, out float x)
                     && float.TryParse(tok[1], System.Globalization.NumberStyles.Float, ci, out float y)
                     && float.TryParse(tok[2], System.Globalization.NumberStyles.Float, ci, out float z))
                        lista.Add(new Vector3(x + UNITY_OX, y, z + UNITY_OZ));
                }
                return lista;
            });
            yield return EsperarTask(taskXyz, "XYZ validacion");

            if (taskXyz.IsCompletedSuccessfully && taskXyz.Result.Count > 0)
            {
                var pts = taskXyz.Result;
                double sumSq = 0; int cnt = 0;
                float errMax = 0f; Vector3 posErrMax = default;
                int paso = Mathf.Max(1, pts.Count / 2000);
                for (int i = 0; i < pts.Count; i += paso)
                {
                    float yT = terrain.SampleHeight(new Vector3(pts[i].x, 0, pts[i].z))
                             + terrain.transform.position.y;
                    float d = pts[i].y - yT;
                    sumSq += d * d; cnt++;
                    if (Mathf.Abs(d) > Mathf.Abs(errMax)) { errMax = d; posErrMax = pts[i]; }
                }
                float rmse = Mathf.Sqrt((float)(sumSq / Mathf.Max(1, cnt)));
                _ultimoRMSE = rmse;
                AlsasuaLogger.Info("Validacion",
                    $"RMSE: {rmse:F3}m ({cnt} pts) — ErrMax: {errMax:+F2;-F2}m @ {posErrMax}");
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
            ("Herriko Plaza",     new(1918f, 0, 8570f), 547f),
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

        // Normalizar: altitudUnity = altitudReal - 0  (TerrainData.size.y es el rango total)
        // Terrain.position.y = 0, terrainHeight = 900 en SceneBootstrapper
        return Mathf.Clamp01(z / terY);
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

        float zMin = float.MaxValue, zMax = float.MinValue;
        foreach (var p in puntos) { if (p.y < zMin) zMin = p.y; if (p.y > zMax) zMax = p.y; }
        float zRange = zMax - zMin;
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
                ? Mathf.Clamp01((sumZ[hy, hx] / countZ[hy, hx] - zMin) / zRange * (zRange / terY))
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
