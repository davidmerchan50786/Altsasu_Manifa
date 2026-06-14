// Assets/Scripts/Systems/GestorStreamingPredictivo.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GESTOR DE STREAMING PREDICTIVO — Mosaico V2 + Addressables (Unity 6)
//
//  Lee posición + vector de velocidad del jugador (ControladorJugador) y PREDICE
//  en qué tile del Mosaico V2 estará dentro de 'horizonteSegundos' (10 s). Carga
//  por adelantado el CONTENIDO de ese tile (edificios/props como prefab Addressable)
//  y LIBERA con regla estricta los tiles que quedan atrás → la memoria sigue al
//  jugador, no crece sin techo.
//
//  ── COORDENADAS ───────────────────────────────────────────────────────────────
//  Tiles tomados tal cual del manifest_v2.json (esquina SO en Unity + 'ancho'),
//  ya en el espacio canónico ETRS89→Unity (escalaX 0.93687). No reconvierto nada:
//  el manifest YA está en coordenadas Unity. La predicción es lineal en ese espacio.
//
//  ── PRESUPUESTO DE GC (objetivo < 2 ms) ───────────────────────────────────────
//  · Estado estable (sin cruzar tile): CERO asignaciones — keep-set en HashSet
//    reusado, sin LINQ, sin strings (direcciones precalculadas en init), checks a
//    2 Hz (no por frame).
//  · Al cruzar un tile: una carga/descarga asíncrona; los Destroy (ReleaseInstance)
//    se ESCALONAN a 'maxOperacionesPorCiclo' por ciclo para no encadenar varios
//    Destroy en el mismo frame. InstantiateAsync reparte el coste de instanciación.
//  No es una garantía de compilación: es un diseño construido para ese objetivo;
//  ajusta intervaloCheck / maxOperacionesPorCiclo según el Profiler.
//
//  ── ADDRESSABLES (OPT-IN) ─────────────────────────────────────────────────────
//  El paquete com.unity.addressables NO está en el proyecto. Para evitar romper la
//  compilación, la carga/liberación real vive tras el define ALSASUA_ADDRESSABLES.
//  El predictor (matemática) compila siempre; sin el define, "carga"/"libera" son
//  no-ops registradas que puedes ver en el GUI de debug.
//  Activación: instala com.unity.addressables, marca el prefab de contenido de cada
//  tile como Addressable con dirección = nombre del .raw sin extensión
//  (p. ej. "tile_a0_z0_x0"), y añade ALSASUA_ADDRESSABLES a Scripting Define Symbols.
//
//  Capa SYSTEMS (puede referenciar Runtime/Core).
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if ALSASUA_ADDRESSABLES
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
#endif

public sealed class GestorStreamingPredictivo : MonoBehaviour
{
    // ── Predicción ─────────────────────────────────────────────────────────────
    [Header("═══ PREDICCIÓN ═══")]
    [Tooltip("Horizonte de predicción (s). Se carga el tile donde estará el jugador en este tiempo.")]
    [SerializeField] private float horizonteSegundos = 10f;
    [Tooltip("Por debajo de esta velocidad (m/s) no se mira hacia delante; solo el tile actual + vecinos.")]
    [SerializeField] private float velocidadMinima   = 1.5f;
    [Tooltip("Paso de muestreo a lo largo de la trayectoria (m). Debe ser ≤ lado de tile para no saltarse tiles.")]
    [SerializeField] private float pasoMuestreoPath  = 300f;
    [Tooltip("Mantener también los tiles vecinos del actual (colchón ante giros laterales).")]
    [SerializeField] private bool  mantenerVecinos   = true;

    // ── Filtro / presupuesto ────────────────────────────────────────────────────
    [Header("═══ FILTRO / PRESUPUESTO ═══")]
    [Tooltip("Solo se hace streaming de tiles cuyo anillo sea ≤ este valor (0=urbano, 1=valle, 2=sierras). " +
             "Las sierras (anillo 2) no tienen edificios → no se transmiten.")]
    [SerializeField] private int   anilloMaximoStreaming = 1;
    [Tooltip("Operaciones (cargas + liberaciones) máximas por ciclo. Escalona el coste para no encadenar Destroys.")]
    [SerializeField] private int   maxOperacionesPorCiclo = 2;

    // ── Direcciones Addressable ──────────────────────────────────────────────────
    [Header("═══ DIRECCIONES ═══")]
    [Tooltip("Prefijo opcional de la dirección Addressable (p. ej. 'contenido_').")]
    [SerializeField] private string prefijoDireccion = "";
    [Tooltip("Sufijo opcional de la dirección Addressable.")]
    [SerializeField] private string sufijoDireccion  = "";

    // ── Rendimiento / debug ──────────────────────────────────────────────────────
    [Header("═══ RENDIMIENTO / DEBUG ═══")]
    [Tooltip("Intervalo (s) entre recomprobaciones. 0.5 s sobra; nunca por frame.")]
    [SerializeField] private float intervaloCheck = 0.5f;
    [Tooltip("Suavizado EMA de la velocidad (0=congelado, 1=instantáneo). Evita que el jitter cambie de tile.")]
    [SerializeField, Range(0.05f, 1f)] private float suavizadoVel = 0.4f;
    [SerializeField] private bool mostrarGUI = true;

    // ── Tabla de tiles (precalculada en init) ────────────────────────────────────
    private struct TileStream
    {
        public float  x0, z0, x1, z1;   // AABB en Unity (esquina SO → NE)
        public float  cx, cz;           // centro
        public float  ancho;
        public int    anillo;
        public string direccion;        // dirección Addressable precalculada
        public bool   streamable;       // anillo ≤ max  (y dirección no marcada como ausente)
    }
    private TileStream[] _tiles;

    // ── Estado ───────────────────────────────────────────────────────────────────
    private Transform          _objetivo;
    private ControladorJugador _control;
    private Vector3            _velSuavizada;
    private Vector3            _ultimaPos;
    private bool               _tienePos;
    private float              _timer;
    private int                _tilePredicho = -1;

    // Buffers reutilizados (cero alloc en estado estable)
    private readonly HashSet<int> _keep        = new HashSet<int>();
    private readonly List<int>    _tmpLiberar  = new List<int>();

    // Fuente de verdad de "qué tiles están vivos" — agnóstica al paquete, para que
    // toda la lógica de reconciliación compile con y sin Addressables.
    private readonly HashSet<int> _cargadosKeys = new HashSet<int>();
#if ALSASUA_ADDRESSABLES
    // Handles solo cuando el paquete está presente.
    private readonly Dictionary<int, AsyncOperationHandle<GameObject>> _handles =
        new Dictionary<int, AsyncOperationHandle<GameObject>>();
#endif

    public int  TilesCargados        => _cargadosKeys.Count;
    private bool EstaCargado(int idx) => _cargadosKeys.Contains(idx);

    // ── Zona de arranque (boot dinámico) ─────────────────────────────────────────
    [Header("═══ ARRANQUE ═══")]
    [Tooltip("Punto cuya zona se PRECARGA al arrancar, antes de que el jugador se mueva. " +
             "Vector3.zero = Herriko Plaza (spawn por defecto).")]
    [SerializeField] private Vector3 puntoArranque = Vector3.zero;

    /// <summary>Instancia (la pantalla de carga puede consultar ZonaInicialLista vía evento/servicio).</summary>
    public static GestorStreamingPredictivo Instance { get; private set; }
    /// <summary>True cuando el contenido de la zona de spawn ya está instanciado (o no hay
    /// nada que transmitir): la pantalla de carga puede levantarse sin pop-in en el spawn.</summary>
    public bool ZonaInicialLista { get; private set; }

    // Tiles que la precarga de arranque está esperando (se vacía al resolverse cada uno).
    private readonly HashSet<int> _keepInicial = new HashSet<int>();

    // ── Auto-pausa por sobrecarga de frame (Director de Simulación) ──────────
    // Productor OPCIONAL: cuando el FactorCarga baja del umbral de pausa, dejamos de
    // CARGAR contenido nuevo de tiles (lo que cuesta: InstantiateAsync); las
    // liberaciones SIGUEN porque alivian. Histéresis para no parpadear. Si no hay
    // orquestador (null), _degradado nunca se activa → comportamiento normal.
    private IGlobalSimulationOrchestrator _orquestador;
    private System.Action<float>          _onFactorCarga;
    private bool                          _degradado;

    // ═════════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ═════════════════════════════════════════════════════════════════════════

    private void Start()
    {
        if (!ConstruirTablaDesdeManifest())
        {
            AlsasuaLogger.Warn("Streaming", "Sin manifest del Mosaico V2 → gestor desactivado.");
            enabled = false;
            return;
        }

        Instance = this;
        SuscribirDegrade();
        ArranqueMundo.RegistrarGestor();   // la pantalla de carga esperará a nuestra zona inicial
        AltsasuCore.OnJugadorSpawned += OnJugadorSpawned;
        BuscarJugador();
        PrecargarZonaInicial();   // carga la zona de spawn YA, sin esperar a que el jugador se mueva
        AlsasuaLogger.Info("Streaming",
            $"Streaming predictivo listo: {_tiles.Length} tiles, " +
            $"{ContarStreamables()} transmitibles (anillo ≤ {anilloMaximoStreaming}).");
    }

    private void OnDestroy()
    {
        AltsasuCore.OnJugadorSpawned -= OnJugadorSpawned;
        if (_orquestador != null && _onFactorCarga != null)
            _orquestador.OnFactorCargaCambia -= _onFactorCarga;
        _orquestador   = null;
        _onFactorCarga = null;
        LiberarTodo();   // limpieza estricta: ningún handle queda colgando
        if (Instance == this) Instance = null;
    }

    // ── Hookup con el Director de Simulación ────────────────────────────────
    private void SuscribirDegrade()
    {
        _orquestador = ServiceLocator.Get<IGlobalSimulationOrchestrator>();
        if (_orquestador == null) return;   // sin director → ritmo normal

        var cfg = GlobalSimulationOrchestrator.Instancia?.Config;
        float pausa   = cfg?.productoresPausaFactor   ?? 0.85f;
        float reanuda = cfg?.productoresReanudaFactor ?? 0.95f;

        _onFactorCarga = factor =>
        {
            if (!_degradado && factor < pausa)        _degradado = true;
            else if (_degradado && factor > reanuda)  _degradado = false;
        };
        _orquestador.OnFactorCargaCambia += _onFactorCarga;
        _onFactorCarga(_orquestador.FactorCarga);   // estado inicial coherente
    }

    // Precarga la zona de spawn al arrancar → el jugador tiene contenido bajo los
    // pies desde el primer frame y la pantalla de carga puede levantarse en cuanto
    // ZonaInicialLista pase a true (boot dinámico, no espera al mundo entero).
    private void PrecargarZonaInicial()
    {
        Vector3 punto = puntoArranque == Vector3.zero ? GeoDataAlsasua.HerrikoPlaza : puntoArranque;

        _keep.Clear();
        int actual = TileEn(punto.x, punto.z);
        AddSiStreamable(actual);
        if (mantenerVecinos && actual >= 0) AddVecinos(actual);

        // Lanzar TODAS las cargas de la zona inicial (sin tope: queremos el spawn ya).
        _keepInicial.Clear();
        foreach (int idx in _keep)
            if (_tiles[idx].streamable && !EstaCargado(idx))
            {
                _keepInicial.Add(idx);   // tiles que estamos esperando para declarar la zona lista
                CargarTile(idx);
            }

#if ALSASUA_ADDRESSABLES
        if (_keepInicial.Count == 0) MarcarZonaListaInterno();   // nada que cargar
#else
        MarcarZonaListaInterno();   // sin Addressables las cargas son no-op → lista al instante
#endif
    }

    // Marca un tile de la zona inicial como resuelto (cargado/fallido). Idempotente:
    // para tiles normales (no iniciales) Remove devuelve false → no hace nada.
    private void ResolverInicial(int idx)
    {
        if (_keepInicial.Remove(idx) && _keepInicial.Count == 0)
            MarcarZonaListaInterno();
    }

    // Marca la zona de spawn como lista (local + señal de Core para la pantalla de carga).
    private void MarcarZonaListaInterno()
    {
        if (ZonaInicialLista) return;
        ZonaInicialLista = true;
        ArranqueMundo.MarcarZonaInicialLista();
    }

    private void OnJugadorSpawned(Transform t)
    {
        _objetivo = t;
        _control  = t != null ? t.GetComponent<ControladorJugador>() : null;
        _tienePos = false;
    }

    private void Update()
    {
        _timer -= Time.deltaTime;
        if (_timer > 0f) return;
        float dt = intervaloCheck - _timer;   // tiempo real transcurrido desde el último ciclo
        _timer = intervaloCheck;

        if (_objetivo == null) { BuscarJugador(); return; }

        Vector3 pos = PosicionJugador();
        ActualizarVelocidad(pos, dt);
        ConstruirKeepSet(pos);
        Reconciliar();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  INIT — tabla de tiles desde el manifest (en coordenadas Unity ya)
    // ═════════════════════════════════════════════════════════════════════════

    private bool ConstruirTablaDesdeManifest()
    {
        string ruta = CargadorMosaicoTerreno.RutaManifest();
        if (string.IsNullOrEmpty(ruta) || !File.Exists(ruta)) return false;

        MosaicoManifest man;
        try { man = MosaicoManifest.Cargar(ruta); }
        catch (System.Exception ex)
        {
            AlsasuaLogger.Warn("Streaming", $"Manifest ilegible: {ex.Message}");
            return false;
        }
        if (man?.tiles == null || man.tiles.Count == 0) return false;

        _tiles = new TileStream[man.tiles.Count];
        for (int i = 0; i < man.tiles.Count; i++)
        {
            var d = man.tiles[i];
            _tiles[i] = new TileStream
            {
                x0 = d.x, z0 = d.z, x1 = d.x + d.ancho, z1 = d.z + d.ancho,
                cx = d.x + d.ancho * 0.5f, cz = d.z + d.ancho * 0.5f,
                ancho = d.ancho, anillo = d.anillo,
                direccion = prefijoDireccion + Path.GetFileNameWithoutExtension(d.file) + sufijoDireccion,
                streamable = d.anillo <= anilloMaximoStreaming,
            };
        }
        return true;
    }

    private int ContarStreamables()
    {
        int n = 0;
        for (int i = 0; i < _tiles.Length; i++) if (_tiles[i].streamable) n++;
        return n;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  PREDICCIÓN — keep-set de tiles a mantener vivos este ciclo
    // ═════════════════════════════════════════════════════════════════════════

    private void ConstruirKeepSet(Vector3 pos)
    {
        _keep.Clear();

        // 1. Tile actual.
        int actual = TileEn(pos.x, pos.z);
        AddSiStreamable(actual);

        // 2. Trayectoria: muestreo desde la posición hasta la posición prevista
        //    en 'horizonteSegundos'. Así no nos saltamos tiles intermedios a alta
        //    velocidad y precargamos por donde realmente vamos.
        float vel = _velSuavizada.magnitude;
        _tilePredicho = actual;
        if (vel >= velocidadMinima)
        {
            Vector3 futuro = pos + _velSuavizada * horizonteSegundos;
            _tilePredicho = TileEn(futuro.x, futuro.z);

            float dist  = Vector3.Distance(pos, futuro);
            int   pasos = Mathf.Clamp(Mathf.CeilToInt(dist / Mathf.Max(1f, pasoMuestreoPath)), 1, 32);
            for (int k = 1; k <= pasos; k++)
            {
                Vector3 p = Vector3.Lerp(pos, futuro, (float)k / pasos);
                AddSiStreamable(TileEn(p.x, p.z));
            }
        }

        // 3. Colchón lateral: vecinos del tile actual (giros imprevistos).
        if (mantenerVecinos && actual >= 0)
            AddVecinos(actual);
    }

    private void AddSiStreamable(int idx)
    {
        if (idx >= 0 && _tiles[idx].streamable) _keep.Add(idx);
    }

    // Vecinos = tiles del mismo anillo cuyas AABB tocan la del tile dado (8-vecindad).
    private void AddVecinos(int idx)
    {
        ref readonly TileStream t = ref _tiles[idx];
        float margen = t.ancho * 0.5f;
        for (int i = 0; i < _tiles.Length; i++)
        {
            if (i == idx || !_tiles[i].streamable || _tiles[i].anillo != t.anillo) continue;
            if (Mathf.Abs(_tiles[i].cx - t.cx) <= t.ancho + margen &&
                Mathf.Abs(_tiles[i].cz - t.cz) <= t.ancho + margen)
                _keep.Add(i);
        }
    }

    // Tile MÁS FINO (menor 'ancho') que contiene la posición; -1 si ninguno.
    private int TileEn(float x, float z)
    {
        int mejor = -1;
        float mejorAncho = float.MaxValue;
        for (int i = 0; i < _tiles.Length; i++)
        {
            ref readonly TileStream t = ref _tiles[i];
            if (x >= t.x0 && x < t.x1 && z >= t.z0 && z < t.z1 && t.ancho < mejorAncho)
            {
                mejor = i; mejorAncho = t.ancho;
            }
        }
        return mejor;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  RECONCILIAR — cargar lo que falta, liberar lo que sobra (escalonado)
    // ═════════════════════════════════════════════════════════════════════════

    private void Reconciliar()
    {
        int ops = 0;

        // Frame sobrecargado: no se ARRANCAN cargas nuevas (es lo que cuesta:
        // InstantiateAsync). Excepción: tiles aún pendientes de la zona inicial,
        // que se cargan igual para no bloquear la pantalla de carga. Las
        // LIBERACIONES (paso 2) siguen siempre porque alivian la memoria.
        bool permitirCargas = !_degradado;

        // 1. Cargar tiles en el keep-set que aún no están cargados.
        foreach (int idx in _keep)
        {
            if (ops >= maxOperacionesPorCiclo) break;
            if (!EstaCargado(idx) && _tiles[idx].streamable)
            {
                if (!permitirCargas && !_keepInicial.Contains(idx)) continue;
                CargarTile(idx);
                ops++;
            }
        }

        // 2. Liberar tiles cargados que ya no están en el keep-set.
        _tmpLiberar.Clear();
        foreach (int idx in _cargadosKeys)
            if (!_keep.Contains(idx)) _tmpLiberar.Add(idx);

        for (int i = 0; i < _tmpLiberar.Count && ops < maxOperacionesPorCiclo; i++, ops++)
            LiberarTile(_tmpLiberar[i]);
    }

#if ALSASUA_ADDRESSABLES
    private void CargarTile(int idx)
    {
        _cargadosKeys.Add(idx);                         // marcar YA: permite liberar a media carga
        var handle = Addressables.InstantiateAsync(_tiles[idx].direccion, transform);
        _handles[idx] = handle;

        int captura = idx;
        handle.Completed += op =>
        {
            if (op.Status != AsyncOperationStatus.Succeeded)
            {
                // Dirección inexistente → marca el tile como no-transmitible y no reintentar.
                _tiles[captura].streamable = false;
                _cargadosKeys.Remove(captura);
                _handles.Remove(captura);
                AlsasuaLogger.Warn("Streaming", $"Sin Addressable '{_tiles[captura].direccion}' — tile omitido.");
                ResolverInicial(captura);   // no bloquear el boot por un tile que no existe
                return;
            }
            // ¿Se salió del keep-set mientras cargaba? → soltar inmediatamente (evita fantasma).
            if (!_keep.Contains(captura))
            {
                Addressables.ReleaseInstance(op.Result);
                _cargadosKeys.Remove(captura);
                _handles.Remove(captura);
            }
            ResolverInicial(captura);       // éxito (o evicción) → zona inicial un paso más cerca
        };
    }

    private void LiberarTile(int idx)
    {
        _cargadosKeys.Remove(idx);
        if (!_handles.TryGetValue(idx, out var handle)) return;
        _handles.Remove(idx);
        if (handle.IsValid())
            Addressables.ReleaseInstance(handle);       // Destroy del contenido + libera el bundle si nadie más lo usa
    }

    private void LiberarTodo()
    {
        foreach (var kv in _handles)
            if (kv.Value.IsValid()) Addressables.ReleaseInstance(kv.Value);
        _handles.Clear();
        _cargadosKeys.Clear();
    }
#else
    private void CargarTile(int idx)  => _cargadosKeys.Add(idx);     // no-op simulado (sin paquete)
    private void LiberarTile(int idx) => _cargadosKeys.Remove(idx);
    private void LiberarTodo()        => _cargadosKeys.Clear();
#endif

    // ═════════════════════════════════════════════════════════════════════════
    //  VELOCIDAD / POSICIÓN (compatible con jugador a pie y en vehículo)
    // ═════════════════════════════════════════════════════════════════════════

    private void ActualizarVelocidad(Vector3 pos, float dt)
    {
        Vector3 v;
        // Fuente primaria: el vector ya suavizado del ControladorJugador (a pie).
        if (_control != null && _control.isActiveAndEnabled && _control.VelocidadHoriz > 0.05f)
            v = _control.VelocidadHorizontal;
        else if (_tienePos && dt > 1e-3f)               // en vehículo / sin control → delta de posición
            v = (pos - _ultimaPos) / dt;
        else
            v = Vector3.zero;

        v.y = 0f;
        _velSuavizada = Vector3.Lerp(_velSuavizada, v, suavizadoVel);
        _ultimaPos = pos;
        _tienePos  = true;
    }

    private Vector3 PosicionJugador()
    {
        // En vehículo el Transform del jugador cuelga del coche → usar la raíz.
        bool enVehiculo = ServiceLocator.Get<ISpawnService>()?.JugadorEnVehiculo ?? false;
        return (enVehiculo && _objetivo.parent != null) ? _objetivo.parent.position : _objetivo.position;
    }

    private void BuscarJugador()
    {
        var core = AltsasuCore.Jugador;
        if (core != null) { OnJugadorSpawned(core); return; }
        var jGO = GameObject.FindGameObjectWithTag("Player");
        if (jGO != null) OnJugadorSpawned(jGO.transform);
    }

    /// <summary>Recalcular ya (tras teletransporte del jugador).</summary>
    public void ForzarActualizacion()
    {
        _timer = 0f;
        _tienePos = false;   // descarta la velocidad previa (evita predecir hacia el punto antiguo)
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  GUI DEBUG (cacheado — sin allocs por frame)
    // ═════════════════════════════════════════════════════════════════════════

    private GUIStyle _guiStyle;
    private int      _guiCacheClave = int.MinValue;
    private string   _guiCacheTexto = "";

    private void OnGUI()
    {
        if (!mostrarGUI || (!Application.isEditor && !Debug.isDebugBuild)) return;

        _guiStyle ??= new GUIStyle(GUI.skin.box)
        {
            fontSize = 12, alignment = TextAnchor.UpperLeft, padding = new RectOffset(6, 6, 4, 4)
        };

        int clave = _cargadosKeys.Count * 1000 + (_tilePredicho + 1);
        if (clave != _guiCacheClave)
        {
            _guiCacheClave = clave;
            string dirPred = (_tilePredicho >= 0) ? _tiles[_tilePredicho].direccion : "—";
#if ALSASUA_ADDRESSABLES
            const string modo = "Addressables ON";
#else
            const string modo = "Addressables OFF (simulado)";
#endif
            _guiCacheTexto =
                $"STREAMING [{modo}]\n" +
                $"Tiles cargados: {_cargadosKeys.Count}\n" +
                $"Tile previsto (+{horizonteSegundos:0}s): {dirPred}\n" +
                $"Vel: {_velSuavizada.magnitude:0.0} m/s";
        }
        GUI.Box(new Rect(10, 80, 320, 78), _guiCacheTexto, _guiStyle);
    }
}
