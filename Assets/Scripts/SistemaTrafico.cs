// Assets/Scripts/SistemaTrafico.cs
// Simulación de tráfico rodado en autovías y calles de Alsasua.
//
// ── ARQUITECTURA ──────────────────────────────────────────────────────────────
//  • VehiculoData : struct  → array contiguo, sin MonoBehaviour por vehículo.
//  • Pool de GameObjects visuales: solo se crean objetos para los N vehículos
//    más cercanos al jugador. Los lejanos se renderizan con DrawMeshInstanced.
//  • Estado por vehículo (enum EstadoVehiculo): CIRCULANDO / FRENANDO / PARADO.
//    Máquina de estados simple sin herencia; cada tick es O(1) por vehículo.
//  • Múltiples carriles definidos por arrays de waypoints (una ruta = un carril).
//  • Raycast de suelo escalonado (1 vehículo/frame): O(1) por frame.
//
// ── MEMORIA ───────────────────────────────────────────────────────────────────
//  • Pool pre-allocado en Awake(); cero Instantiate en runtime.
//  • Materiales creados por código → OnDestroy los destruye.
//  • Arrays de matrices y colores pre-allocados; cero GC en Update.
//
// ── SETUP EN EDITOR ───────────────────────────────────────────────────────────
//  1. Crear un GameObject "SistemaTrafico" y añadir este componente.
//  2. Crear subarrays de waypoints para cada carril (autovía A-10, N-1, casco).
//  3. Asignar en el Inspector las rutas mediante el array "carriles".
//  4. Configurar el número de vehículos y la velocidad.

using UnityEngine;
using System.Collections.Generic;

[AddComponentMenu("Alsasua/Sistema Trafico")]
public sealed class SistemaTrafico : MonoBehaviour
{
    // ══════════════════════════════════════════════════════════════════════
    //  TIPOS INTERNOS
    // ══════════════════════════════════════════════════════════════════════

    private enum EstadoVehiculo : byte
    {
        Circulando,   // velocidad normal
        Frenando,     // hay vehículo delante → reducir velocidad
        Parado,       // atasco o semáforo (no implementado aún, reservado)
    }

    /// <summary>Definición de un carril de tráfico. Asignar en Inspector.</summary>
    [System.Serializable]
    public sealed class Carril
    {
        [Tooltip("Waypoints del carril, en orden de circulación.")]
        public Transform[] waypoints;
        [Range(20f, 140f)]
        [Tooltip("Velocidad máxima del carril en km/h.")]
        public float velocidadMaxKmh = 80f;
        [Tooltip("Es autovía (true) → vehículos más rápidos y sin paradas.")]
        public bool esAutovia = false;
    }

    private struct VehiculoData
    {
        public Vector3        posicion;
        public Vector3        velocidad;
        public int            carrilIdx;
        public int            waypointActual;
        public EstadoVehiculo estado;
        public float          alturaY;
        public float          velocidadMax;       // m/s, fijada al spawnar
        public Color          colorCarroceria;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  INSPECTOR
    // ══════════════════════════════════════════════════════════════════════

    [Header("═══ CARRILES ═══")]
    [Tooltip("Cada Carril define una ruta de waypoints y su velocidad máxima.")]
    [SerializeField] private Carril[] carriles;

    [Header("═══ VEHÍCULOS ═══")]
    [Range(5, 80)]
    [Tooltip("Total de vehículos repartidos entre todos los carriles.")]
    [SerializeField] private int totalVehiculos = 40;

    [Range(20f, 50f)]
    [Tooltip("Distancia de seguridad mínima entre vehículos del mismo carril (m).")]
    [SerializeField] private float distanciaSeguridad = 12f;

    [Range(0.3f, 1.0f)]
    [Tooltip("Factor de variación de velocidad entre vehículos (0.3 = ±30%).")]
    [SerializeField] private float variacionVelocidad = 0.35f;

    [Header("═══ VISUAL (pool) ═══")]
    [Range(5, 30)]
    [Tooltip("Máximo de GameObjects visuales activos simultáneamente (vehículos cercanos).")]
    [SerializeField] private int maxObjetosVisuales = 18;

    [Range(30f, 200f)]
    [Tooltip("Distancia al jugador por encima de la cual se usa DrawMeshInstanced en lugar de GO.")]
    [SerializeField] private float radioPoolVisual = 80f;

    [SerializeField] private bool mostrarGizmos = true;

    // ══════════════════════════════════════════════════════════════════════
    //  CAMPOS PRIVADOS
    // ══════════════════════════════════════════════════════════════════════

    // Datos de vehículo (struct array)
    private VehiculoData[] _vehiculos;
    private int            _numVehiculos;

    // Pool visual de GameObjects
    private GameObject[]   _poolGO;
    private Renderer[]     _poolRenderers;       // FIX: caché de Renderer por slot → evita GetComponent cada frame
    private bool[]         _poolEnUso;
    private int[]          _vehiculoAGO;         // _vehiculoAGO[i] = índice del GO en pool (-1 si sin GO)
    private int[]          _goAVehiculo;         // _goAVehiculo[i] = índice de vehículo (-1 si libre)

    // Render instanciado para vehículos lejanos
    private Mesh           _meshCoche;
    private Material       _matVehiculo;
    private Matrix4x4[]    _matrices;
    private Vector4[]      _colores;
    private Matrix4x4[]    _matrizLote;
    private List<Vector4>  _colorListaLote;    // FIX GC: pre-alloc → cero GC en render
    private const int      MAX_LOTE = 1023;
    private MaterialPropertyBlock _propBlock;
    private static readonly int _idBaseColor = Shader.PropertyToID("_BaseColor");

    // Materiales para pool visual
    private List<Material> _matsCreados = new List<Material>();

    // Referencia al jugador para culling
    private Transform _jugadorTr;

    // Raycast escalonado
    private int   _indiceRaycast;

    // FIX O(n²) → O(1): índices de vehículos agrupados y ordenados por progreso en ruta,
    // una lista por carril. PrepararOrdenCarriles() los rellena al inicio de cada tick;
    // ActualizarVehiculos() consulta solo el vehículo inmediatamente delante → cero búsqueda.
    private List<int>[] _vehiculosPorCarril;  // índices de vehículos activos, por carril
    private float[]     _progresoEnCarril;    // score = waypointActual*10000 − distAlWP
    private int[]       _posEnCarril;         // posición del vehículo i en su lista de carril

    // ══════════════════════════════════════════════════════════════════════
    //  PALETA DE COLORES DE COCHES
    // ══════════════════════════════════════════════════════════════════════

    private static readonly Color[] PALETA_COCHES = new Color[]
    {
        new Color(0.90f, 0.90f, 0.90f),  // blanco perla
        new Color(0.15f, 0.15f, 0.15f),  // negro
        new Color(0.55f, 0.55f, 0.60f),  // plata
        new Color(0.08f, 0.18f, 0.45f),  // azul marino
        new Color(0.65f, 0.08f, 0.08f),  // rojo burdeos
        new Color(0.42f, 0.35f, 0.28f),  // beige
        new Color(0.08f, 0.28f, 0.10f),  // verde oscuro
    };

    // ══════════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ══════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        // Si no hay carriles asignados en el Inspector, auto-crearlos desde GeoDataAlsasua.
        if (carriles == null || carriles.Length == 0)
        {
            AlsasuaLogger.Info("SistemaTrafico",
                "Sin carriles en Inspector → auto-creando desde GeoDataAlsasua (N1, NA120, casco).");
            carriles = CrearCarrilesDesdeGeoData();
        }

        if (carriles == null || carriles.Length == 0)
        {
            AlsasuaLogger.Warn("SistemaTrafico", "No se pudieron crear carriles. Sistema desactivado.");
            enabled = false;
            return;
        }

        _numVehiculos = Mathf.Clamp(totalVehiculos, 1, 200);
        _propBlock    = new MaterialPropertyBlock();

        _meshCoche  = CrearMeshCoche();
        _matVehiculo = CrearMaterialVehiculo();

        PreallocarArrays();
        InicializarVehiculos();
        CrearPoolVisual();
        CachearJugador();
    }

    private void Update()
    {
        MuestrearSuelo();
        ActualizarVehiculos(Time.deltaTime);
        SincronizarPoolVisual();
        RenderizarVehiculosLejanos();
    }

    private void OnDestroy()
    {
        foreach (var m in _matsCreados)
            if (m != null) Object.Destroy(m);
        _matsCreados.Clear();

        if (_meshCoche != null) { Object.Destroy(_meshCoche); _meshCoche = null; }
    }

    private void OnDrawGizmos()
    {
        if (!mostrarGizmos || carriles == null) return;
        Color[] gizmoColores = { Color.green, Color.cyan, Color.yellow, Color.magenta };
        for (int c = 0; c < carriles.Length; c++)
        {
            Gizmos.color = gizmoColores[c % gizmoColores.Length];
            var wps = carriles[c]?.waypoints;
            if (wps == null) continue;
            for (int i = 0; i < wps.Length - 1; i++)
                if (wps[i] != null && wps[i + 1] != null)
                    Gizmos.DrawLine(wps[i].position, wps[i + 1].position);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  INICIALIZACIÓN
    // ══════════════════════════════════════════════════════════════════════

    private Mesh CrearMeshCoche()
    {
        // Cubo achatado que simula la silueta de un turismo (1.8 × 0.75 × 4.2 m)
        var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var mesh = Object.Instantiate(temp.GetComponent<MeshFilter>().sharedMesh);
        Object.DestroyImmediate(temp);
        return mesh;
    }

    private Material CrearMaterialVehiculo()
    {
        var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard")
                  ?? Shader.Find("Standard");
        if (shader == null) return null;
        var mat = new Material(shader);
        mat.enableInstancing = true;
        _matsCreados.Add(mat);
        return mat;
    }

    private void PreallocarArrays()
    {
        _vehiculos    = new VehiculoData[_numVehiculos];
        _matrices     = new Matrix4x4[_numVehiculos];
        _colores      = new Vector4[_numVehiculos];
        _matrizLote     = new Matrix4x4[MAX_LOTE];
        _colorListaLote = new List<Vector4>(MAX_LOTE);  // capacidad fija → cero GC
        _vehiculoAGO    = new int[_numVehiculos];
        for (int i = 0; i < _numVehiculos; i++) _vehiculoAGO[i] = -1;

        // FIX O(n²): pre-allocar listas por carril (capacidad = nVehículos → sin resize en runtime)
        _vehiculosPorCarril = new List<int>[carriles.Length];
        for (int c = 0; c < carriles.Length; c++)
            _vehiculosPorCarril[c] = new List<int>(_numVehiculos);
        _progresoEnCarril = new float[_numVehiculos];
        _posEnCarril      = new int[_numVehiculos];
    }

    private void InicializarVehiculos()
    {
        int numCarriles = carriles.Length;

        for (int i = 0; i < _numVehiculos; i++)
        {
            int    carrilIdx  = i % numCarriles;
            Carril carril     = carriles[carrilIdx];
            var    wps        = carril?.waypoints;

            if (wps == null || wps.Length < 2)
            {
                // Carril vacío: aparcar en origen
                _vehiculos[i] = new VehiculoData { estado = EstadoVehiculo.Parado };
                continue;
            }

            // Distribuir vehículos a lo largo de la ruta evitando amontonamiento inicial
            int wpIdx = (i / numCarriles) % (wps.Length - 1);
            float t   = (float)((i / numCarriles) % 4) / 4f;

            // FIX: al llegar al último waypoint, interpolar hacia sí mismo en lugar de hacia
            // wps[0], que puede ser null o estar en el extremo opuesto de la ruta.
            Vector3 posBase = wps[wpIdx] != null
                ? Vector3.Lerp(wps[wpIdx].position, wps[wpIdx + 1 < wps.Length ? wpIdx + 1 : wpIdx].position, t)
                : Vector3.zero;

            // Velocidad con variación aleatoria (±variacionVelocidad %)
            float velMaxMs = (carril.velocidadMaxKmh / 3.6f)
                           * (1f + Random.Range(-variacionVelocidad, variacionVelocidad));

            Vector3 dirInicial = wps.Length > wpIdx + 1 && wps[wpIdx + 1] != null
                ? (wps[wpIdx + 1].position - posBase).normalized
                : Vector3.forward;

            _vehiculos[i] = new VehiculoData
            {
                posicion       = posBase,
                velocidad      = dirInicial * velMaxMs,
                carrilIdx      = carrilIdx,
                waypointActual = wpIdx,
                estado         = EstadoVehiculo.Circulando,
                alturaY        = posBase.y,
                velocidadMax   = velMaxMs,
                colorCarroceria = PALETA_COCHES[Random.Range(0, PALETA_COCHES.Length)],
            };
            _colores[i] = (Vector4)_vehiculos[i].colorCarroceria;
        }
    }

    private void CrearPoolVisual()
    {
        _poolGO        = new GameObject[maxObjetosVisuales];
        _poolRenderers = new Renderer[maxObjetosVisuales];   // FIX: array de caché de Renderer
        _poolEnUso     = new bool[maxObjetosVisuales];
        _goAVehiculo   = new int[maxObjetosVisuales];
        for (int i = 0; i < maxObjetosVisuales; i++) _goAVehiculo[i] = -1;

        for (int i = 0; i < maxObjetosVisuales; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Vehiculo_Pool_{i:D2}";
            go.transform.SetParent(transform);
            go.transform.localScale = new Vector3(1.8f, 0.75f, 4.2f);
            // Desactivar hasta que sea asignado a un vehículo
            go.SetActive(false);
            // Eliminar colisores del pool visual (solo decorativo)
            Object.Destroy(go.GetComponent<Collider>());

            // FIX NULL: guard contra shader no encontrado → new Material(null) lanza NRE
            var poolShader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Standard");
            if (poolShader == null)
            {
                AlsasuaLogger.Error("SistemaTrafico", "Shader URP/Lit no encontrado para pool visual. " +
                                   "Añádelo a Always Included Shaders.");
                continue;
            }
            var mat = new Material(poolShader);
            _matsCreados.Add(mat);

            // FIX: cachear Renderer aquí (único GetComponent por slot, en init)
            var rend = go.GetComponent<Renderer>();
            rend.sharedMaterial = mat;
            _poolRenderers[i] = rend;

            _poolGO[i] = go;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  AUTO-CREACIÓN DE CARRILES DESDE GEODATA
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Crea carriles de tráfico automáticamente a partir de GeoDataAlsasua
    /// cuando no se han asignado waypoints en el Inspector.
    /// Genera GameObjects hijos como contenedores de waypoints.
    /// </summary>
    private Carril[] CrearCarrilesDesdeGeoData()
    {
        // Rutas + config (velocidad km/h, es autovía)
        var rutas = new (string nombre, Vector3[] puntos, float velKmh, bool autovia)[]
        {
            ("N1_Norte",     GeoDataAlsasua.N1_Norte,            90f,  true),
            ("N1_Sur",       GeoDataAlsasua.N1_Sur,              90f,  true),
            ("NA120_Este",   GeoDataAlsasua.NA120_Este,          60f,  false),
            ("NA120_Oeste",  GeoDataAlsasua.NA120_Oeste,         60f,  false),
            ("CascoUrbano",  GeoDataAlsasua.CalleInteriorCasco,  30f,  false),
        };

        var resultado = new Carril[rutas.Length];
        for (int r = 0; r < rutas.Length; r++)
        {
            var (nombre, puntos, vel, autovia) = rutas[r];
            var padre = new GameObject($"Carril_{nombre}");
            padre.transform.SetParent(transform);

            var wps = new Transform[puntos.Length];
            for (int i = 0; i < puntos.Length; i++)
            {
                var wp = new GameObject($"WP_{i:D2}");
                wp.transform.SetParent(padre.transform);
                wp.transform.position = puntos[i];
                wps[i] = wp.transform;
            }

            resultado[r] = new Carril
            {
                waypoints       = wps,
                velocidadMaxKmh = vel,
                esAutovia       = autovia,
            };
        }

        AlsasuaLogger.Info("SistemaTrafico",
            $"✓ {resultado.Length} carriles creados desde GeoDataAlsasua " +
            "(N1×2, NA120×2, casco).");
        return resultado;
    }

    private void CachearJugador()
    {
        var ctrl = Object.FindFirstObjectByType<ControladorJugador>();
        if (ctrl != null) _jugadorTr = ctrl.transform;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  LÓGICA DE TRÁFICO
    // ══════════════════════════════════════════════════════════════════════

    private void ActualizarVehiculos(float dt)
    {
        // FIX O(n²) → O(1): clasificar y ordenar vehículos por carril UNA vez antes del bucle.
        // Cada vehículo consulta después su vecino inmediato en O(1), sin iterar sobre todos.
        PrepararOrdenCarriles();

        for (int i = 0; i < _numVehiculos; i++)
        {
            ref VehiculoData v = ref _vehiculos[i];
            if (v.estado == EstadoVehiculo.Parado) continue;

            var carril = carriles[v.carrilIdx];
            var wps    = carril?.waypoints;
            if (wps == null || wps.Length < 2) continue;

            // ── 1. Waypoint siguiente ──────────────────────────────────────
            Vector3 target = wps[v.waypointActual] != null
                ? wps[v.waypointActual].position
                : v.posicion;
            target.y = v.posicion.y;

            Vector3 dirTarget = target - v.posicion;
            float   distTarget = dirTarget.magnitude;

            if (distTarget < 5f)
            {
                // Avanzar al siguiente waypoint (en bucle)
                v.waypointActual = (v.waypointActual + 1) % wps.Length;
            }

            // ── 2. Detección de vehículo delante (mismo carril) — O(1) ────
            // FIX O(n²) → O(1): PrepararOrdenCarriles() clasificó y ordenó los vehículos
            // por progreso en ruta (waypointActual*10000 − distAlWP) antes de este bucle.
            // Solo consultamos el vehículo inmediatamente siguiente en la lista del carril;
            // cero iteración sobre todos los vehículos → complejidad total O(n) por tick.
            float distanciaAlFrente = float.MaxValue;
            {
                var lista     = _vehiculosPorCarril[v.carrilIdx];
                int posActual = _posEnCarril[i];
                if (posActual + 1 < lista.Count)
                {
                    int j = lista[posActual + 1];
                    Vector3 diff = _vehiculos[j].posicion - v.posicion;
                    diff.y = 0f;
                    distanciaAlFrente = diff.magnitude;
                }
            }

            // ── 3. Máquina de estados ──────────────────────────────────────
            if (distanciaAlFrente < distanciaSeguridad * 0.6f)
                v.estado = EstadoVehiculo.Frenando;
            else if (distanciaAlFrente > distanciaSeguridad)
                v.estado = EstadoVehiculo.Circulando;

            // ── 4. Control de velocidad ────────────────────────────────────
            float velObjetivo = (v.estado == EstadoVehiculo.Frenando)
                ? Mathf.Max(0f, v.velocidadMax * (distanciaAlFrente / distanciaSeguridad - 0.4f))
                : v.velocidadMax;

            Vector3 dirMovimiento = (distTarget > 0.1f) ? (dirTarget / distTarget) : v.velocidad.normalized;
            Vector3 velObjetivoVec = dirMovimiento * velObjetivo;

            // Suavizar cambio de velocidad.
            // MEJORA: aceleración proporcional a la velocidad actual (ease-in desde parado).
            // Un coche real tarda ~4-5s en alcanzar velocidad de crucero desde parado.
            // Antes: factorAcel constante = 2.5 m/s² → coche alcanzaba 22 m/s en 9s (rápido).
            // Ahora: rampa 0.5→2.5 m/s² según velocidad → transición suave desde parado.
            float velActual  = v.velocidad.magnitude;
            float factorAcel;
            if (velObjetivo > velActual)
            {
                // Aceleración: empieza lenta (0.5 m/s²) y sube al máximo (2.5 m/s²)
                // cuando el coche supera el 40% de su velocidad máxima.
                float t = Mathf.Clamp01(velActual / Mathf.Max(v.velocidadMax * 0.4f, 0.1f));
                factorAcel = Mathf.Lerp(0.5f, 2.5f, t);
            }
            else
            {
                factorAcel = 6.0f;  // frenada sigue siendo rápida
            }
            v.velocidad = Vector3.MoveTowards(v.velocidad, velObjetivoVec, factorAcel * dt);

            // ── 5. Integración ─────────────────────────────────────────────
            v.posicion   += v.velocidad * dt;
            v.posicion.y  = v.alturaY;
        }
    }

    private void MuestrearSuelo()
    {
        if (_numVehiculos == 0) return;
        _indiceRaycast = (_indiceRaycast + 1) % _numVehiculos;
        ref VehiculoData v = ref _vehiculos[_indiceRaycast];
        if (Physics.Raycast(v.posicion + Vector3.up * 8f, Vector3.down, out RaycastHit hit, 30f))
            v.alturaY = hit.point.y;
    }

    /// <summary>
    /// Agrupa los índices de vehículos activos por carril y los ordena por progreso en ruta
    /// (score = waypointActual * 10000 − distanciaAlWaypoint).
    /// Llamar una vez al inicio de ActualizarVehiculos(); InsertionSort cero-alloc es óptimo
    /// para N ≤ 20 vehículos por carril (típico en este simulador).
    /// </summary>
    private void PrepararOrdenCarriles()
    {
        // Vaciar listas sin allocar (Clear no libera la capacidad reservada)
        for (int c = 0; c < _vehiculosPorCarril.Length; c++)
            _vehiculosPorCarril[c].Clear();

        // Calcular score de progreso y clasificar cada vehículo activo en su carril
        for (int i = 0; i < _numVehiculos; i++)
        {
            ref VehiculoData v = ref _vehiculos[i];
            if (v.estado == EstadoVehiculo.Parado) continue;

            var wps = carriles[v.carrilIdx]?.waypoints;
            if (wps == null || wps.Length == 0) continue;

            // Score mayor = más avanzado en la ruta.
            // Restar la distancia al waypoint actual desambigua vehículos en el mismo tramo.
            float dist = (v.waypointActual < wps.Length && wps[v.waypointActual] != null)
                ? Vector3.Distance(v.posicion, wps[v.waypointActual].position)
                : 0f;
            _progresoEnCarril[i] = v.waypointActual * 10000f - dist;

            _vehiculosPorCarril[v.carrilIdx].Add(i);
        }

        // Ordenar cada carril por score ascendente y registrar la posición de cada vehículo
        for (int c = 0; c < _vehiculosPorCarril.Length; c++)
        {
            var lista = _vehiculosPorCarril[c];
            InsertionSortLista(lista);
            for (int k = 0; k < lista.Count; k++)
                _posEnCarril[lista[k]] = k;
        }
    }

    /// <summary>
    /// InsertionSort in-place sobre una List&lt;int&gt; usando _progresoEnCarril como clave.
    /// Cero allocaciones. Optimal para N ≤ ~20 (típico por carril).
    /// </summary>
    private void InsertionSortLista(List<int> lista)
    {
        int n = lista.Count;
        for (int i = 1; i < n; i++)
        {
            int   key      = lista[i];
            float keyScore = _progresoEnCarril[key];
            int   j        = i - 1;
            while (j >= 0 && _progresoEnCarril[lista[j]] > keyScore)
            {
                lista[j + 1] = lista[j];
                j--;
            }
            lista[j + 1] = key;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  POOL VISUAL (GameObjects cercanos)
    // ══════════════════════════════════════════════════════════════════════

    private void SincronizarPoolVisual()
    {
        Vector3 posJugador = _jugadorTr != null ? _jugadorTr.position : Vector3.zero;
        float   radio2     = radioPoolVisual * radioPoolVisual;

        // Liberar GOs de vehículos que ya están lejos
        for (int p = 0; p < maxObjetosVisuales; p++)
        {
            if (!_poolEnUso[p]) continue;
            int vi = _goAVehiculo[p];
            if (vi < 0 || vi >= _numVehiculos) { LiberarSlotPool(p); continue; }

            float d2 = (_vehiculos[vi].posicion - posJugador).sqrMagnitude;
            if (d2 > radio2 * 1.2f) LiberarSlotPool(p);
        }

        // Asignar GOs a vehículos cercanos sin GO
        for (int i = 0; i < _numVehiculos; i++)
        {
            if (_vehiculoAGO[i] >= 0) continue;  // ya tiene GO
            float d2 = (_vehiculos[i].posicion - posJugador).sqrMagnitude;
            if (d2 > radio2) continue;

            int slotLibre = EncontrarSlotLibre();
            if (slotLibre < 0) break;  // pool lleno

            AsignarSlotPool(slotLibre, i);
        }

        // Actualizar posición y rotación de GOs activos
        for (int p = 0; p < maxObjetosVisuales; p++)
        {
            if (!_poolEnUso[p]) continue;
            int vi = _goAVehiculo[p];
            if (vi < 0) continue;

            ref VehiculoData v = ref _vehiculos[vi];
            var go = _poolGO[p];
            go.transform.position = v.posicion + Vector3.up * 0.375f;
            if (v.velocidad.sqrMagnitude > 0.01f)
            {
                // BUG FIX: usar Slerp para evitar rotaciones instantáneas en giros bruscos.
                // LookRotation directo generaba "snap" de 90° en cruces → coches girando al instante.
                Quaternion rotDestino = Quaternion.LookRotation(v.velocidad.normalized, Vector3.up);
                go.transform.rotation = Quaternion.Slerp(go.transform.rotation, rotDestino, Time.deltaTime * 10f);
            }

            // FIX CRÍTICO: usar Renderer cacheado + MaterialPropertyBlock.
            // Antes: GetComponent<Renderer>() cada frame (lento) y
            //        sharedMaterial.color = … (modifica el material compartido → cambia el color
            //        de TODOS los vehículos que usen ese mismo material).
            // Ahora: referencia cacheada + SetPropertyBlock (per-instance, sin GC, sin leak).
            var rend = _poolRenderers[p];
            if (rend != null)
            {
                _propBlock.SetColor(_idBaseColor, v.colorCarroceria);
                rend.SetPropertyBlock(_propBlock);
            }
        }
    }

    private int EncontrarSlotLibre()
    {
        for (int p = 0; p < maxObjetosVisuales; p++)
            if (!_poolEnUso[p]) return p;
        return -1;
    }

    private void AsignarSlotPool(int slot, int vehiculoIdx)
    {
        _poolEnUso[slot]           = true;
        _goAVehiculo[slot]         = vehiculoIdx;
        _vehiculoAGO[vehiculoIdx]  = slot;
        _poolGO[slot].SetActive(true);
    }

    private void LiberarSlotPool(int slot)
    {
        int vi = _goAVehiculo[slot];
        if (vi >= 0 && vi < _numVehiculos) _vehiculoAGO[vi] = -1;
        _goAVehiculo[slot] = -1;
        _poolEnUso[slot]   = false;
        _poolGO[slot].SetActive(false);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RENDER INSTANCIADO (vehículos lejanos)
    // ══════════════════════════════════════════════════════════════════════

    private void RenderizarVehiculosLejanos()
    {
        if (_meshCoche == null || _matVehiculo == null) return;

        int n = 0;
        for (int i = 0; i < _numVehiculos; i++)
        {
            if (_vehiculoAGO[i] >= 0) continue;  // ya tiene GO, skip
            Vector3 vel = _vehiculos[i].velocidad;
            Quaternion rot = vel.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(vel.normalized, Vector3.up)
                : Quaternion.identity;

            _matrices[n] = Matrix4x4.TRS(
                _vehiculos[i].posicion + Vector3.up * 0.375f,
                rot,
                new Vector3(1.8f, 0.75f, 4.2f));
            _colores[n] = (Vector4)_vehiculos[i].colorCarroceria;
            n++;
        }

        if (n == 0) return;

        int enviados = 0;
        while (enviados < n)
        {
            int lote = Mathf.Min(MAX_LOTE, n - enviados);
            System.Array.Copy(_matrices, enviados, _matrizLote, 0, lote);

            // FIX GC: List<Vector4> pre-allocada → cero alloc por frame
            _colorListaLote.Clear();
            for (int k = enviados; k < enviados + lote; k++)
                _colorListaLote.Add(_colores[k]);

            _propBlock.SetVectorArray(_idBaseColor, _colorListaLote);
            Graphics.DrawMeshInstanced(_meshCoche, 0, _matVehiculo, _matrizLote, lote, _propBlock);
            enviados += lote;
        }
    }
}
