// Assets/Scripts/SistemaZonas.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ZONE STREAMING — carga/descarga edificios por cuadrícula alrededor del jugador
//
//  Arquitectura:
//    · El mundo OSM (1030 edificios) está pre-indexado en celdas de ZONE_SIZE×ZONE_SIZE.
//    · Al arrancar, GeneradorMundoOSM parsea los JSONs y llama a RegistrarEdificio()
//      para que SistemaZonas sepa qué edificios pertenecen a cada celda.
//    · Cada frame se comprueba si la celda del jugador ha cambiado.
//    · Al cambiar: se activa el anillo VIEW_RADIUS y se destruyen celdas lejanas.
//    · La construcción de meshes es asíncrona (un lote por frame) para no congelar.
//
//  Celdas:  200 m × 200 m  (Alsasua cabe en ~6×5 celdas)
//  Activas: radio 1 → 3×3 = 9 celdas   (600 m vista)
//           radio 2 → 5×5 = 25 celdas  (1000 m vista, pesado)
//  Histéresis: cargar a 1 celda, descargar a 2 celdas de distancia
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DefaultExecutionOrder(-82)]  // antes de IntegradorAssets(-80)
public class SistemaZonas : MonoBehaviour
{
    // ── Singleton ──────────────────────────────────────────────────────────
    public static SistemaZonas Instance { get; private set; }
    public static event Action<Vector2Int> OnZonaCargada;
    public static event Action<Vector2Int> OnZonaDescargada;

    // ── Configuración ──────────────────────────────────────────────────────
    [Header("Grid")]
    [Tooltip("Tamaño de cada celda en metros (200 m cubre ~2 manzanas de Alsasua).")]
    public float zonSize  = 200f;
    [Tooltip("Radio de celdas visibles. 1 = 3×3 activas (recomendado). 2 = 5×5 (potente).")]
    [Range(1, 3)]
    public int   viewRadius = 1;

    [Header("Rendimiento")]
    [Tooltip("Edificios que se construyen por frame al cargar una zona nueva.")]
    [Range(5, 50)]
    public int   buildingsPerFrame = 20;
    [Tooltip("Segundos que una zona inactiva espera antes de destruirse (histéresis).")]
    public float unloadDelay = 4f;

    // ── Datos OSM pre-indexados ────────────────────────────────────────────
    // Clave: celda (x, z) en espacio de cuadrícula.
    // Valor: lista de datos de edificios que hay en esa celda.
    readonly Dictionary<Vector2Int, List<EdificioData>> _datosPorZona  = new();
    readonly Dictionary<Vector2Int, List<RoadData>> _callesPorZona = new();

    // ── Estado de zonas activas ────────────────────────────────────────────
    readonly Dictionary<Vector2Int, ZonaInfo>           _zonas         = new();
    readonly Queue<Vector2Int>                          _colaCarga     = new();
    readonly HashSet<Vector2Int>                        _enCola        = new(); // shadow set para Contains O(1)

    // Buffers reutilizables — evitan allocations en ActualizarZonasActivas y ProcesarDesactivaciones
    readonly HashSet<Vector2Int>  _deseadasBuffer  = new(25);
    readonly List<Vector2Int>     _aEliminarBuffer = new(16);

    Vector2Int _zonaJugadorAnterior = new(int.MinValue, int.MinValue);
    Transform  _jugador;
    bool       _indexadoListo;

    // ── Pool de GameObjects ────────────────────────────────────────────────
    Transform _parentEdificios;
    Transform _parentCalles;

    // ──────────────────────────────────────────────────────────────────────

    class ZonaInfo
    {
        public Vector2Int key;
        public GameObject root;           // GameObject padre de la zona
        public bool       cargada;
        public bool       cargando;
        public float      tiempoDesactivacion; // para histéresis
    }

    // ══════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ══════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        _parentEdificios = new GameObject("_Zonas_Edificios").transform;
        _parentEdificios.SetParent(transform);
        _parentCalles    = new GameObject("_Zonas_Calles").transform;
        _parentCalles.SetParent(transform);

        // Suscribirse al evento de jugador para evitar FindGameObjectWithTag en Update
        AltsasuCore.OnJugadorSpawned += OnJugadorSpawned;
    }

    void OnDestroy()
    {
        AltsasuCore.OnJugadorSpawned -= OnJugadorSpawned;
    }

    void OnJugadorSpawned(Transform t) { _jugador = t; }

    IEnumerator Start()
    {
        // Esperar a que GeneradorMundoOSM indexe los datos
        float t = 0f;
        while (!_indexadoListo && t < 10f) { t += 0.5f; yield return new WaitForSeconds(0.5f); }

        if (!_indexadoListo)
        {
            AlsasuaLogger.Warn("Zonas", "Timeout esperando índice OSM — sin edificios");
            yield break;
        }

        AlsasuaLogger.Info("Zonas", $"Índice listo: {_datosPorZona.Count} zonas con edificios");
        StartCoroutine(BucleCarga());
    }

    void Update()
    {
        if (!_indexadoListo) return;
        if (_jugador == null) BuscarJugador();
        if (_jugador == null) return;

        Vector2Int zonaActual = MundoAZona(_jugador.position);
        if (zonaActual == _zonaJugadorAnterior) return;

        _zonaJugadorAnterior = zonaActual;
        ActualizarZonasActivas(zonaActual);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  API PÚBLICA — llamada desde GeneradorMundoOSM
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Registra un edificio OSM en su celda de cuadrícula.</summary>
    public void RegistrarEdificio(EdificioData e, Vector3 centroMundo)
    {
        var key = MundoAZona(centroMundo);
        if (!_datosPorZona.TryGetValue(key, out var lista))
        {
            lista = new List<EdificioData>();
            _datosPorZona[key] = lista;
        }
        lista.Add(e);
    }

    /// <summary>Registra un tramo de calle en su celda.</summary>
    public void RegistrarCalle(RoadData r, Vector3 centroMundo)
    {
        var key = MundoAZona(centroMundo);
        if (!_callesPorZona.TryGetValue(key, out var lista))
        {
            lista = new List<RoadData>();
            _callesPorZona[key] = lista;
        }
        lista.Add(r);
    }

    /// <summary>Llamar cuando todos los datos OSM han sido registrados.</summary>
    public void MarcarIndexadoListo()
    {
        _indexadoListo = true;
        AlsasuaLogger.Info("Zonas", "Indexado OSM completado. Zona streaming activo.");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ACTIVACIÓN / DESACTIVACIÓN DE ZONAS
    // ══════════════════════════════════════════════════════════════════════

    void ActualizarZonasActivas(Vector2Int centro)
    {
        // Reutilizar el HashSet en lugar de allocar uno nuevo cada vez
        _deseadasBuffer.Clear();

        for (int dx = -viewRadius; dx <= viewRadius; dx++)
        for (int dz = -viewRadius; dz <= viewRadius; dz++)
            _deseadasBuffer.Add(new Vector2Int(centro.x + dx, centro.y + dz));

        var deseadas = _deseadasBuffer;

        // Encolar zonas nuevas
        foreach (var key in deseadas)
        {
            if (!_zonas.TryGetValue(key, out var info) || !info.cargada)
            {
                if (_enCola.Add(key)) _colaCarga.Enqueue(key); // Add returns false if already in set
            }
            else
            {
                // Resetear temporizador de desactivación
                info.tiempoDesactivacion = float.MaxValue;
            }
        }

        // Programar desactivación de zonas que ya no son necesarias
        int desactivadas = 0;
        foreach (var kv in _zonas)
        {
            if (deseadas.Contains(kv.Key)) continue;
            if (kv.Value.cargada && kv.Value.tiempoDesactivacion == float.MaxValue)
            {
                kv.Value.tiempoDesactivacion = Time.time + unloadDelay;
                desactivadas++;
            }
        }

        if (desactivadas > 0)
            StartCoroutine(ProcesarDesactivaciones());

        AlsasuaLogger.Info("Zonas",
            $"Jugador en zona ({centro.x},{centro.y}) — activas: {deseadas.Count}, cola: {_colaCarga.Count}");
    }

    IEnumerator BucleCarga()
    {
        while (true)
        {
            if (_colaCarga.Count > 0)
            {
                var key = _colaCarga.Dequeue();
                _enCola.Remove(key);  // ya sale de la cola
                if (!_zonas.TryGetValue(key, out var info) || (!info.cargada && !info.cargando))
                    yield return StartCoroutine(CargarZona(key));
            }
            yield return null;
        }
    }

    IEnumerator CargarZona(Vector2Int key)
    {
        if (!_zonas.TryGetValue(key, out var info))
        {
            info = new ZonaInfo { key = key, tiempoDesactivacion = float.MaxValue };
            _zonas[key] = info;
        }

        if (info.cargada || info.cargando) yield break;
        info.cargando = true;

        // Crear root de zona
        info.root = new GameObject($"Zona_{key.x}_{key.y}");
        info.root.transform.SetParent(_parentEdificios);

        // ── Edificios ────────────────────────────────────────────────────
        // Prefiere SistemaEdificiosAAA (calidad AAA) sobre GeneradorMundoOSM (básico)
        if (_datosPorZona.TryGetValue(key, out var edificios))
        {
            int lote = 0;
            var edificiosAAA = SistemaEdificiosAAA.Instance;
            var generador    = GeneradorMundoOSM.Instance;

            foreach (var e in edificios)
            {
                if (edificiosAAA != null)
                    edificiosAAA.ConstruirEdificio(e, info.root.transform);
                else
                    generador?.ConstruirEdificio(e, info.root.transform);

                if (++lote >= buildingsPerFrame) { lote = 0; yield return null; }
            }
        }

        // ── Calles ──────────────────────────────────────────────────────
        if (_callesPorZona.TryGetValue(key, out var calles))
        {
            int lote = 0;
            var rootCalles = new GameObject($"Calles_{key.x}_{key.y}");
            rootCalles.transform.SetParent(_parentCalles);
            foreach (var c in calles)
            {
                GeneradorMundoOSM.Instance?.ConstruirCalle(c, rootCalles.transform);
                if (++lote >= buildingsPerFrame) { lote = 0; yield return null; }
            }
        }

        // Pintar asfalto en el terreno bajo las calles de esta zona
        if (_callesPorZona.TryGetValue(key, out var callesTerreno) && SistemaTerreno.Instance != null)
        {
            foreach (var c in callesTerreno)
            {
                if (c.points == null || c.points.Length < 2) continue;
                var pts = System.Array.ConvertAll(c.points,
                    p => new Vector3(p.x + GeoDataAlsasua.OX, 0, p.z + GeoDataAlsasua.OZ));
                SistemaTerreno.Instance?.PintarCarretera(pts, Mathf.Max(3f, c.width));
            }
        }

        info.cargada  = true;
        info.cargando = false;
        OnZonaCargada?.Invoke(key);

        // Avisar al NavMesh para hornear esta zona
        Vector3 centro = ZonaCentroMundo(key);
        SistemaNavMesh.Instance?.ForzarRehornear(centro);

        AlsasuaLogger.Info("Zonas", $"✅ Zona ({key.x},{key.y}) cargada");
    }

    IEnumerator ProcesarDesactivaciones()
    {
        yield return new WaitForSeconds(unloadDelay * 0.5f);

        _aEliminarBuffer.Clear();
        var aEliminar = _aEliminarBuffer;
        foreach (var kv in _zonas)
        {
            if (!kv.Value.cargada) continue;
            if (Time.time < kv.Value.tiempoDesactivacion) continue;

            if (kv.Value.root != null)
                Destroy(kv.Value.root);
            aEliminar.Add(kv.Key);
            OnZonaDescargada?.Invoke(kv.Key);
        }

        foreach (var k in aEliminar) _zonas.Remove(k);

        // También eliminar raíces de calles
        foreach (var k in aEliminar)
        {
            var nombre = $"Calles_{k.x}_{k.y}";
            var go = _parentCalles.Find(nombre);
            if (go != null) Destroy(go.gameObject);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  UTILIDADES
    // ══════════════════════════════════════════════════════════════════════

    void BuscarJugador()
    {
        var go = GameObject.FindGameObjectWithTag("Player");
        if (go != null) _jugador = go.transform;
        else _jugador = AltsasuCore.Jugador;
    }

    /// <summary>Convierte una posición mundo a coordenadas de celda.</summary>
    public Vector2Int MundoAZona(Vector3 pos)
        => new(Mathf.FloorToInt(pos.x / zonSize),
               Mathf.FloorToInt(pos.z / zonSize));

    /// <summary>Centro mundial de una celda.</summary>
    public Vector3 ZonaCentroMundo(Vector2Int key)
    {
        float cx = (key.x + 0.5f) * zonSize;
        float cz = (key.y + 0.5f) * zonSize;
        float y  = Terrain.activeTerrain != null
            ? Terrain.activeTerrain.SampleHeight(new Vector3(cx, 0, cz))
            : 240f;
        return new Vector3(cx, y, cz);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  GIZMOS
    // ══════════════════════════════════════════════════════════════════════

    void OnDrawGizmosSelected()
    {
        if (_jugador == null) return;
        var centro = MundoAZona(_jugador.position);
        float h = 10f;

        for (int dx = -viewRadius - 1; dx <= viewRadius + 1; dx++)
        for (int dz = -viewRadius - 1; dz <= viewRadius + 1; dz++)
        {
            var key = new Vector2Int(centro.x + dx, centro.y + dz);
            bool activa = _zonas.TryGetValue(key, out var info) && info.cargada;

            int dist = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
            Gizmos.color = activa
                ? new Color(0, 1, 0, 0.15f)
                : dist <= viewRadius
                    ? new Color(1, 1, 0, 0.1f)
                    : new Color(1, 0, 0, 0.05f);

            Vector3 c = ZonaCentroMundo(key);
            Gizmos.DrawCube(new Vector3(c.x, h, c.z),
                            new Vector3(zonSize * 0.95f, 2f, zonSize * 0.95f));

            Gizmos.color = activa ? Color.green : Color.grey;
            Gizmos.DrawWireCube(new Vector3(c.x, h, c.z),
                                new Vector3(zonSize, 2f, zonSize));
        }
    }
}
