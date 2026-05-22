// Assets/Scripts/AlsasuaTreeStreamer.cs  (also houses GeoDataAlsasua constants)
// ═══════════════════════════════════════════════════════════════════════════
//  ÁRBOL STREAMER — coloca prefabs de árbol en radio cercano al jugador
//  usando los datos OSM de trees_unity.json.
//
//  Capa de árboles de rango medio (100-400m):
//    - SistemaVegetacion gestiona hierba/arbustos GPU instanced (< 100m)
//    - AlsasuaTreeStreamer gestiona árboles individuales prefab (100-400m)
//    - Cesium Google Tiles gestiona bosques lejanos (> 400m)
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

public class AlsasuaTreeStreamer : MonoBehaviour
{
    [Header("Prefabs de árbol")]
    public GameObject[] treePrefabs;

    [Header("Radio de streaming")]
    [Tooltip("Distancia a la que se instancian árboles (m). Alias: radioStreaming.")]
    public float radioVisible  = 250f;   // CesiumCapasAlsasua lo amplía a 400m si no hay Cesium

    [Tooltip("Distancia a la que se destruyen árboles fuera de rango (m). Debe ser > radioVisible.")]
    public float radioDestroir = 350f;   // alias radioDestruir (nombre histórico del campo)

    [Tooltip("Distancia mínima — árboles más cercanos los gestiona SistemaVegetacion.")]
    public float radioMinimo   = 30f;

    [Tooltip("Máximo de árboles instanciados simultáneamente.")]
    public int maxArboles = 300;

    // Alias de compatibilidad con código antiguo
    public float radioStreaming { get => radioVisible; set => radioVisible = value; }

    [Header("Datos OSM")]
    [Tooltip("Ruta al JSON con posiciones de árboles OSM.\n" +
             "Generado por DescargarDatosAlsasua.ps1 → Assets/AlsasuaData/trees_unity.json")]
    public string rutaJSON = "Assets/AlsasuaData/trees_unity.json";

    // ── Estado interno ─────────────────────────────────────────────────────
    readonly List<GameObject> _instancias = new();
    readonly List<Vector3>    _posiciones  = new();
    Transform                 _jugador;
    bool                      _cargado;

    // ──────────────────────────────────────────────────────────────────────

    void Start()
    {
        // Si existen árboles LIDAR exactos, PosicionadorPrecisionUrbana los gestiona.
        // El streamer OSM sirve de fallback cuando no hay datos LIDAR.
        bool hayLIDAR = TieneArbolesLIDAR();

        if (hayLIDAR)
        {
            AlsasuaLogger.Info("TreeStreamer",
                "Árboles LIDAR detectados (lidar_trees.json) — " +
                "AlsasuaTreeStreamer suspende colocación OSM para no duplicar.");
            // Seguimos activos para streaming (ocultamos árboles lejanos),
            // pero cargamos las posiciones LIDAR en lugar de las OSM.
            CargarPosicionesLIDAR();
        }
        else
        {
            CargarPosicionesOSM();
        }

        StartCoroutine(BucleSteaming());
    }

    static bool TieneArbolesLIDAR()
    {
        string p = System.IO.Path.Combine(
            Application.dataPath.Replace("Assets", ""),
            "Assets/AlsasuaData/lidar_trees.json");
        return System.IO.File.Exists(p)
            && new System.IO.FileInfo(p).Length > 500; // al menos algunos árboles
    }

    void CargarPosicionesLIDAR()
    {
        string p = System.IO.Path.Combine(
            Application.dataPath.Replace("Assets", ""),
            "Assets/AlsasuaData/lidar_trees.json");
        try
        {
            string json = System.IO.File.ReadAllText(p);
            // Formato: [{"x":..., "z":..., "altura":..., "radio":...}, ...]
            var arr = JsonHelper.ParseArray<LIDARTreeData>(json);
            if (arr != null)
            {
                // lidar_trees.json usa coordenadas Unity ABSOLUTAS (generadas por PipelineLIDAR)
                // NO añadir offset — ya incluyen OX=1918, OZ=8570
                foreach (var t in arr)
                    _posiciones.Add(new Vector3(t.x, 0f, t.z));
                _cargado = true;
                AlsasuaLogger.Info("TreeStreamer",
                    $"{_posiciones.Count} árboles LIDAR exactos cargados para streaming.");
            }
        }
        catch (System.Exception e)
        { AlsasuaLogger.Warn("TreeStreamer", $"LIDAR trees parse: {e.Message}"); }
    }

    [System.Serializable] class LIDARTreeData { public float x, z, altura, radio; }

    void CargarPosicionesOSM()
    {
        var ta = Resources.Load<TextAsset>(
            System.IO.Path.ChangeExtension(rutaJSON, null)
                .Replace("Assets/Resources/", "")
                .Replace("Assets/", ""));

        if (ta == null)
        {
            // Intentar carga directa via File.ReadAllText en editor
#if UNITY_EDITOR
            if (System.IO.File.Exists(rutaJSON))
            {
                ParseJSON(System.IO.File.ReadAllText(rutaJSON));
                return;
            }
#endif
            AlsasuaLogger.Warn("TreeStreamer",
                $"No se pudo cargar {rutaJSON}. Streaming desactivado.");
            return;
        }
        ParseJSON(ta.text);
    }

    void ParseJSON(string json)
    {
        try
        {
            var wrapper = JsonUtility.FromJson<TreesWrapper>(
                "{\"items\":" + json + "}");
            foreach (var t in wrapper.items)
                _posiciones.Add(new Vector3(t.x, t.y, t.z));
            _cargado = true;
            AlsasuaLogger.Info("TreeStreamer",
                $"{_posiciones.Count} árboles OSM cargados.");
        }
        catch (System.Exception e)
        {
            AlsasuaLogger.Error("TreeStreamer", $"Error parsing trees JSON: {e.Message}");
        }
    }

    // ── Cache NativeArrays (reutilizados entre frames) ─────────────────────
    NativeArray<float3> _posicionesNative;
    bool                _nativeInit;

    void OnDestroy()
    {
        if (_nativeInit && _posicionesNative.IsCreated)
            _posicionesNative.Dispose();
    }

    void InicializarNative()
    {
        if (_nativeInit || _posiciones.Count == 0) return;
        _posicionesNative = new NativeArray<float3>(_posiciones.Count, Allocator.Persistent);
        for (int i = 0; i < _posiciones.Count; i++)
            _posicionesNative[i] = new float3(_posiciones[i].x, 0f, _posiciones[i].z);
        _nativeInit = true;
    }

    IEnumerator BucleSteaming()
    {
        while (true)
        {
            yield return new WaitForSeconds(2f);

            if (!_cargado || treePrefabs == null || treePrefabs.Length == 0) continue;

            if (_jugador == null)
            {
                var jGO = GameObject.FindGameObjectWithTag("Player");
                if (jGO != null) _jugador = jGO.transform;
                else continue;
            }

            // Inicializar NativeArray la primera vez
            if (!_nativeInit) InicializarNative();

            Vector3 posJ3 = _jugador.position;
            float3  posJ  = new float3(posJ3.x, 0f, posJ3.z);

            // ── FASE 1: Burst — marcar instancias a destruir ──────────────
            var instanciasValidas = new List<GameObject>();
            foreach (var inst in _instancias)
                if (inst != null) instanciasValidas.Add(inst);
            _instancias = instanciasValidas;

            if (_instancias.Count > 0)
            {
                var posInst = new NativeArray<float3>(_instancias.Count, Allocator.TempJob);
                for (int i = 0; i < _instancias.Count; i++)
                {
                    var p = _instancias[i].transform.position;
                    posInst[i] = new float3(p.x, 0f, p.z);
                }
                var aDestruir = new NativeArray<byte>(_instancias.Count, Allocator.TempJob);

                var jobDestruir = new JobMarcarArbolesADestruir
                {
                    posicionesInstancias = posInst,
                    posJugador           = posJ,
                    radioDestruirSq      = radioDestroir * radioDestroir,
                    aDestruir            = aDestruir,
                };
                jobDestruir.Schedule(_instancias.Count, 64).Complete();

                for (int i = _instancias.Count - 1; i >= 0; i--)
                    if (aDestruir[i] == 1) { Destroy(_instancias[i]); _instancias.RemoveAt(i); }

                posInst.Dispose();
                aDestruir.Dispose();
            }

            if (_instancias.Count >= maxArboles) continue;

            // ── FASE 2: Burst — filtrar posiciones en rango ───────────────
            var resultadoRango = new NativeArray<int>(_posicionesNative.Length, Allocator.TempJob);
            var jobRango = new JobFiltrarArbolesEnRango
            {
                posiciones  = _posicionesNative,
                posJugador  = posJ,
                radioMin    = radioMinimo,
                radioMax    = radioVisible,
                radioMaxSq  = radioVisible * radioVisible,
                radioMinSq  = radioMinimo  * radioMinimo,
                resultado   = resultadoRango,
            };
            jobRango.Schedule(_posicionesNative.Length, 128).Complete();

            // ── FASE 3: Burst — comprobar ocupación (anti-duplicado) ──────
            var candidatos = new List<int>();
            for (int i = 0; i < resultadoRango.Length && candidatos.Count < 200; i++)
                if (resultadoRango[i] >= 0) candidatos.Add(resultadoRango[i]);
            resultadoRango.Dispose();

            if (candidatos.Count == 0) continue;

            // Posiciones de instancias existentes para ocupación
            var posExist = new NativeArray<float3>(_instancias.Count + 1, Allocator.TempJob);
            for (int i = 0; i < _instancias.Count; i++)
            {
                var p = _instancias[i].transform.position;
                posExist[i] = new float3(p.x, 0f, p.z);
            }
            var posCand = new NativeArray<float3>(candidatos.Count, Allocator.TempJob);
            for (int i = 0; i < candidatos.Count; i++)
                posCand[i] = _posicionesNative[candidatos[i]];

            var ocupado = new NativeArray<byte>(candidatos.Count, Allocator.TempJob);
            var jobOcup = new JobComprobarOcupacion
            {
                candidatos           = posCand,
                posicionesExistentes = posExist,
                radioOcupacionSq     = 9f, // 3m²
                ocupado              = ocupado,
            };
            jobOcup.Schedule(candidatos.Count, 32).Complete();

            // ── FASE 4: Instanciar en hilo principal (required by Unity) ──
            var terrain = Terrain.activeTerrain;
            for (int i = 0; i < candidatos.Count; i++)
            {
                if (_instancias.Count >= maxArboles) break;
                if (ocupado[i] == 1) continue;

                var pos = _posicionesNative[candidatos[i]];
                float y = terrain != null
                    ? terrain.SampleHeight(new Vector3(pos.x, 0, pos.z))
                    : 240f;

                var prefab = treePrefabs[Random.Range(0, treePrefabs.Length)];
                if (prefab == null) continue;

                var go = Instantiate(prefab,
                    new Vector3(pos.x, y, pos.z),
                    Quaternion.Euler(0, Random.Range(0f, 360f), 0),
                    transform);
                go.isStatic = false;
                _instancias.Add(go);

                yield return null; // distribuir en frames
            }

            posExist.Dispose();
            posCand.Dispose();
            ocupado.Dispose();
        }
    }

    // ── Clases de deserialización ──────────────────────────────────────────

    [System.Serializable] class TreeEntry  { public float x, y, z; }
    [System.Serializable] class TreesWrapper { public List<TreeEntry> items; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  CONSTANTES GEOGRÁFICAS — Alsasua / Altsasu (lat 42.9°N)
// ─────────────────────────────────────────────────────────────────────────────
public static class GeoDataAlsasua
{
    /// <summary>Latitud del centro de Alsasua (Herriko Plaza).</summary>
    public const double LATITUD_CENTRO  = 42.9003;
    /// <summary>Longitud del centro de Alsasua (Herriko Plaza).</summary>
    public const double LONGITUD_CENTRO = -2.1665;

    /// <summary>Metros por grado de latitud (constante global ~111 320 m/°).</summary>
    public const double M_POR_GRADO_LAT = 111_320.0;

    /// <summary>
    /// Metros por grado de longitud a lat 42.9°N.
    /// = 111 320 × cos(42.9°) ≈ 81 560 m/°
    /// </summary>
    public const double M_POR_GRADO_LON = 81_560.0;
}
