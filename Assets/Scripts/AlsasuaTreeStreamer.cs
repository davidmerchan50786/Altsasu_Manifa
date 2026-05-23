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

    // ── Pool de árboles ────────────────────────────────────────────────────
    readonly List<GameObject> _pool = new();
    [Tooltip("Tamaño inicial del pool (pre-calentado en Start).")]
    public int tamañoPool = 150;

    // ──────────────────────────────────────────────────────────────────────

    void Start()
    {
        PreCalentarPool();

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

    // ── Pool ──────────────────────────────────────────────────────────────

    void PreCalentarPool()
    {
        if (treePrefabs == null || treePrefabs.Length == 0) return;
        for (int i = 0; i < tamañoPool; i++)
        {
            var prefab = treePrefabs[i % treePrefabs.Length];
            if (prefab == null) continue;
            var go = Instantiate(prefab, transform);
            go.SetActive(false);
            go.name = $"Arbol_Pool_{i}";
            _pool.Add(go);
        }
    }

    GameObject AlquilarArbol(GameObject prefab, Vector3 pos, Quaternion rot)
    {
        // Buscar uno inactivo del mismo prefab (por nombre de prefab) o cualquiera disponible
        GameObject go = null;
        string prefabNombre = prefab.name;
        for (int i = _pool.Count - 1; i >= 0; i--)
        {
            if (_pool[i] == null) { _pool.RemoveAt(i); continue; }
            if (!_pool[i].activeInHierarchy)
            {
                // Preferir mismo tipo de árbol para evitar pop visual
                if (_pool[i].name.Contains(prefabNombre) || go == null)
                    go = _pool[i];
            }
        }

        if (go == null)
        {
            // Pool exhausto — crear nuevo y añadir al pool para devoluciones futuras
            go = Instantiate(prefab, transform);
            _pool.Add(go);
        }

        go.transform.SetPositionAndRotation(pos, rot);
        go.SetActive(true);
        return go;
    }

    void DevolverArbol(GameObject go)
    {
        if (go == null) return;
        go.SetActive(false);
    }

    void OnDestroy()
    {
        if (_nativeInit)
        {
            if (_posicionesNative.IsCreated)  _posicionesNative.Dispose();
            if (_naPosInst.IsCreated)         _naPosInst.Dispose();
            if (_naDestruir.IsCreated)        _naDestruir.Dispose();
            if (_naPosExist.IsCreated)        _naPosExist.Dispose();
            if (_naResultadoRango.IsCreated)  _naResultadoRango.Dispose();
            if (_naPosCand.IsCreated)         _naPosCand.Dispose();
            if (_naOcupado.IsCreated)         _naOcupado.Dispose();
        }

        foreach (var go in _pool)
            if (go != null) Destroy(go);
        _pool.Clear();
    }

    // ── NativeArrays persistentes — se asignan una vez, se reutilizan cada ciclo ──
    NativeArray<float3> _posicionesNative;   // posiciones de árboles del JSON
    NativeArray<float3> _naPosInst;          // posiciones de instancias activas  (max = maxArboles)
    NativeArray<byte>   _naDestruir;         // máscara destrucción               (max = maxArboles)
    NativeArray<float3> _naPosExist;         // posiciones existentes para ocupación (max = maxArboles+1)
    NativeArray<int>    _naResultadoRango;   // resultado filtro de rango         (= posicionesNative.Length)
    NativeArray<float3> _naPosCand;          // posiciones de candidatos           (max = MAX_CANDIDATOS)
    NativeArray<byte>   _naOcupado;          // máscara ocupación                  (max = MAX_CANDIDATOS)
    bool                _nativeInit;
    const int           MAX_CANDIDATOS = 200;

    void InicializarNative()
    {
        if (_nativeInit || _posiciones.Count == 0) return;

        _posicionesNative  = new NativeArray<float3>(_posiciones.Count, Allocator.Persistent);
        for (int i = 0; i < _posiciones.Count; i++)
            _posicionesNative[i] = new float3(_posiciones[i].x, 0f, _posiciones[i].z);

        // Arrays de trabajo persistentes — tamaño máximo fijo, sin realloc en cada ciclo
        int capInst = Mathf.Max(maxArboles + 1, 1);
        _naPosInst        = new NativeArray<float3>(capInst,              Allocator.Persistent);
        _naDestruir       = new NativeArray<byte>  (capInst,              Allocator.Persistent);
        _naPosExist       = new NativeArray<float3>(capInst,              Allocator.Persistent);
        _naResultadoRango = new NativeArray<int>   (_posicionesNative.Length, Allocator.Persistent);
        _naPosCand        = new NativeArray<float3>(MAX_CANDIDATOS,        Allocator.Persistent);
        _naOcupado        = new NativeArray<byte>  (MAX_CANDIDATOS,        Allocator.Persistent);

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
            _instancias.RemoveAll(inst => inst == null);

            int nInst = _instancias.Count;
            if (nInst > 0)
            {
                // Rellenar solo los primeros nInst elementos del array persistente
                for (int i = 0; i < nInst; i++)
                {
                    var p = _instancias[i].transform.position;
                    _naPosInst[i] = new float3(p.x, 0f, p.z);
                }

                var jobDestruir = new JobMarcarArbolesADestruir
                {
                    posicionesInstancias = _naPosInst,
                    posJugador           = posJ,
                    radioDestruirSq      = radioDestroir * radioDestroir,
                    aDestruir            = _naDestruir,
                };
                jobDestruir.Schedule(nInst, 64).Complete();

                for (int i = nInst - 1; i >= 0; i--)
                    if (_naDestruir[i] == 1) { DevolverArbol(_instancias[i]); _instancias.RemoveAt(i); }
            }

            if (_instancias.Count >= maxArboles) continue;

            // ── FASE 2: Burst — filtrar posiciones en rango ───────────────
            var jobRango = new JobFiltrarArbolesEnRango
            {
                posiciones  = _posicionesNative,
                posJugador  = posJ,
                radioMin    = radioMinimo,
                radioMax    = radioVisible,
                radioMaxSq  = radioVisible * radioVisible,
                radioMinSq  = radioMinimo  * radioMinimo,
                resultado   = _naResultadoRango,
            };
            jobRango.Schedule(_posicionesNative.Length, 128).Complete();

            // ── FASE 3: Burst — comprobar ocupación (anti-duplicado) ──────
            // Recopilar candidatos en lista temporal (reutilizar cada ciclo)
            var candidatos = new List<int>(); // pequeña (max 200), aceptable aquí
            for (int i = 0; i < _naResultadoRango.Length && candidatos.Count < MAX_CANDIDATOS; i++)
                if (_naResultadoRango[i] >= 0) candidatos.Add(_naResultadoRango[i]);

            if (candidatos.Count == 0) continue;

            // Rellenar posiciones de instancias existentes en array persistente
            nInst = _instancias.Count;
            for (int i = 0; i < nInst; i++)
            {
                var p = _instancias[i].transform.position;
                _naPosExist[i] = new float3(p.x, 0f, p.z);
            }

            int nCand = candidatos.Count;
            for (int i = 0; i < nCand; i++)
                _naPosCand[i] = _posicionesNative[candidatos[i]];

            var jobOcup = new JobComprobarOcupacion
            {
                candidatos           = _naPosCand,
                posicionesExistentes = _naPosExist,
                radioOcupacionSq     = 9f,
                ocupado              = _naOcupado,
            };
            jobOcup.Schedule(nCand, 32).Complete();

            // ── FASE 4: Instanciar en hilo principal (required by Unity) ──
            var terrain = Terrain.activeTerrain;
            for (int i = 0; i < nCand; i++)
            {
                if (_instancias.Count >= maxArboles) break;
                if (_naOcupado[i] == 1) continue;

                var pos = _posicionesNative[candidatos[i]];
                float y = terrain != null
                    ? terrain.SampleHeight(new Vector3(pos.x, 0, pos.z))
                    : 240f;

                var prefab = treePrefabs[UnityEngine.Random.Range(0, treePrefabs.Length)];
                if (prefab == null) continue;

                var go = AlquilarArbol(
                    prefab,
                    new Vector3(pos.x, y, pos.z),
                    Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0));
                go.isStatic = false;
                _instancias.Add(go);

                yield return null;
            }
            // Sin Dispose — los arrays son Persistent y se reutilizan en el siguiente ciclo
        }
    }

    // ── Clases de deserialización ──────────────────────────────────────────

    [System.Serializable] class TreeEntry  { public float x, y, z; }
    [System.Serializable] class TreesWrapper { public List<TreeEntry> items; }
}

// GeoDataAlsasua movida a Assets/Scripts/GeoDataAlsasua.cs
