// Assets/Scripts/SistemaMultitud.cs
// Simulación de manifestación multitudinaria en Alsasua (500-1000 personas).
//
// ── ARQUITECTURA (Data-Oriented) ──────────────────────────────────────────────
//  • AgentData : struct  → array contiguo, cache-friendly; sin MonoBehaviour por agente.
//  • SpatialHashGrid     → consulta de vecinos en O(1) amortizado; evita bucle O(n²).
//  • Graphics.DrawMeshInstanced + MaterialPropertyBlock → 1-2 draw calls para toda
//    la multitud con color de ropa individualizado por agente (GPU instancing).
//  • Lógica de flocking a 30 FPS (acumulador de tiempo); render a FPS del juego.
//
// ── MEMORIA ───────────────────────────────────────────────────────────────────
//  • Todos los arrays se pre-alloc en Awake(); cero GC por frame.
//  • Buckets del grid se reciclan con Array.Clear(); expansión dinámica solo en
//    el primer frame si la densidad supera la capacidad inicial.
//  • OnDestroy() destruye los materiales y meshes creados por código.
//
// ── SETUP EN EDITOR ───────────────────────────────────────────────────────────
//  1. Añadir este script a un GameObject vacío llamado "SistemaMultitud".
//  2. Crear GameObjects vacíos como waypoints y asignarlos a "Puntos Ruta".
//     Situar los waypoints siguiendo la Kale Nagusia de Alsasua (o cualquier ruta).
//  3. Asignar un Material URP/Lit con "Enable GPU Instancing" marcado.
//     Si se deja vacío, el sistema crea un material de fallback automáticamente.

using UnityEngine;
using System.Collections.Generic;

[AddComponentMenu("Alsasua/Sistema Multitud")]
public sealed class SistemaMultitud : MonoBehaviour
{
    // ══════════════════════════════════════════════════════════════════════
    //  INSPECTOR
    // ══════════════════════════════════════════════════════════════════════

    [Header("═══ RUTA DE LA MARCHA ═══")]
    [Tooltip("Waypoints que definen el recorrido. Sitúalos sobre la calzada real de Cesium.")]
    [SerializeField] private Transform[] puntosRuta;

    [Header("═══ MULTITUD ═══")]
    [Range(100, 1000)]
    [Tooltip("Número de manifestantes. 700 es un buen equilibrio rendimiento/visual.")]
    [SerializeField] private int cantidadAgentes = 700;

    [Range(0.8f, 2.5f)]
    [Tooltip("Velocidad de la marcha en m/s. Una marcha lenta es ~1.2 m/s.")]
    [SerializeField] private float velocidadMarcha = 1.3f;

    [Range(6, 20)]
    [Tooltip("Agentes por fila en la formación inicial. 12-14 llena bien una calle urbana.")]
    [SerializeField] private int anchoFormacion = 13;

    [Range(5, 25)]
    [Tooltip("Agentes de la fila delantera que portan la pancarta 'ANFETA'.")]
    [SerializeField] private int portadoresPancarta = 10;

    [Header("═══ FLOCKING (densidad de apelotamiento) ═══")]
    [Range(0.4f, 1.2f)] [SerializeField] private float radioSeparacion = 0.65f;
    [Range(1.0f, 5.0f)] [SerializeField] private float radioCohesion   = 2.2f;
    [Range(1.0f, 4.0f)] [SerializeField] private float radioAlineacion = 1.8f;
    [Range(0.5f, 4.0f)] [SerializeField] private float pesoSeparacion  = 2.2f;
    [Range(0.1f, 2.0f)] [SerializeField] private float pesoCohesion    = 0.9f;
    [Range(0.1f, 2.0f)] [SerializeField] private float pesoAlineacion  = 1.0f;
    [Range(1.0f, 6.0f)] [SerializeField] private float pesoRuta        = 3.0f;

    [Header("═══ VISUAL ═══")]
    [Tooltip("Material URP/Lit con Enable GPU Instancing activado. Si es null se crea fallback.")]
    [SerializeField] private Material materialMultitud;
    [SerializeField] private bool     mostrarGizmosRuta = true;

    // ══════════════════════════════════════════════════════════════════════
    //  DATO DE AGENTE — struct de valor para cache-line locality
    // ══════════════════════════════════════════════════════════════════════

    private struct AgentData
    {
        public Vector3 posicion;
        public Vector3 velocidad;
        public int     waypointActual;
        public float   alturaY;          // altura de suelo muestreada
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CAMPOS PRIVADOS
    // ══════════════════════════════════════════════════════════════════════

    // Agentes
    private AgentData[] _agentes;
    private int         _numAgentes;

    // Render — pre-allocados para cero GC/frame
    private Matrix4x4[]        _matrices;
    private Vector4[]          _coloresInstancia;
    private Matrix4x4[]        _matrizLote;           // buffer de MAX_LOTE para DrawMeshInstanced
    private List<Vector4>      _colorListaLote;        // FIX GC: pre-alloc MAX_LOTE → cero GC en render
    private const int          MAX_LOTE = 1023;       // límite de DrawMeshInstanced

    private Mesh               _meshAgente;
    private Material           _matInstanciada;       // instancia propia → destruir en OnDestroy
    private MaterialPropertyBlock _propBlock;

    private static readonly int _idBaseColor = Shader.PropertyToID("_BaseColor");

    // Spatial Hash Grid
    private const int  GRID_DIM     = 96;             // 96×96 celdas
    private int[][]    _gridBuckets;
    private int[]      _gridCounts;
    private float      _celdaSize;
    private float      _invCelda;
    private Vector3    _gridOrigen;

    // Pancarta
    private GameObject     _goRacimo;                    // raíz de la pancarta y palos
    private List<Material> _matsPancarta = new List<Material>(); // tracked → OnDestroy
    private Texture2D      _texturaPancarta;              // FIX LEAK: tracked → OnDestroy
                                                          // Object.Destroy(material) NO destruye
                                                          // la textura asignada vía mainTexture.

    // Lógica
    private float      _acumLogica;
    private const float TICK_LOGICA         = 1f / 30f;  // actualizar flocking a 30 FPS
    private int         _indiceRaycast;                   // puntero circular para muestreo continuo
    private const int   RAYCASTS_POR_FRAME  = 50;         // raycasts de corrección inicial por frame

    // ══════════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ══════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        _numAgentes = Mathf.Clamp(cantidadAgentes, 1, 1000);

        _meshAgente   = ObtenerMeshCapsula();
        _propBlock    = new MaterialPropertyBlock();
        _matInstanciada = CrearMaterialInstanciado();

        _agentes          = new AgentData[_numAgentes];
        _matrices         = new Matrix4x4[_numAgentes];
        _coloresInstancia = new Vector4[_numAgentes];
        _matrizLote     = new Matrix4x4[MAX_LOTE];
        _colorListaLote = new List<Vector4>(MAX_LOTE);  // capacidad fija → cero GC

        InicializarGrid();
        InicializarAgentes();
        CrearPancarta();
    }

    private void Start()
    {
        // Lanzar coroutine de corrección de alturas DESPUÉS de Awake (primer frame ya renderizado).
        // MuestrearSueloPorTurno() tarda 1000 frames en corregir todos los agentes;
        // esta coroutine lo hace en ~20 frames haciendo RAYCASTS_POR_FRAME/frame.
        StartCoroutine(CorregirAlturasIniciales());
    }

    private void Update()
    {
        _acumLogica += Time.deltaTime;
        if (_acumLogica >= TICK_LOGICA)
        {
            ActualizarGridSpatial();
            ActualizarFlocking(_acumLogica);
            _acumLogica = 0f;
        }
        MuestrearSueloPorTurno();    // un raycast por frame, rota entre agentes
        ActualizarMatricesYPancarta();
        RenderizarGPUInstanced();
    }

    private void OnDestroy()
    {
        if (_matInstanciada != null)
        {
            Object.Destroy(_matInstanciada);
            _matInstanciada = null;
        }
        // Los materiales de pancarta se destruyen junto con el GameObject hijo,
        // pero destruimos explícitamente la instancia del material para el Editor.
        foreach (var m in _matsPancarta)
            if (m != null) Object.Destroy(m);
        _matsPancarta.Clear();

        // _meshAgente fue instanciada desde primitiva temporal → destruir
        if (_meshAgente != null)
        {
            Object.Destroy(_meshAgente);
            _meshAgente = null;
        }
        // FIX LEAK: textura procedural de pancarta — Object.Destroy(material) NO la destruye.
        if (_texturaPancarta != null)
        {
            Object.Destroy(_texturaPancarta);
            _texturaPancarta = null;
        }
    }

    private void OnDrawGizmos()
    {
        if (!mostrarGizmosRuta || puntosRuta == null) return;
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.9f);
        for (int i = 0; i < puntosRuta.Length - 1; i++)
        {
            if (puntosRuta[i] != null && puntosRuta[i + 1] != null)
                Gizmos.DrawLine(puntosRuta[i].position, puntosRuta[i + 1].position);
        }
        Gizmos.color = Color.yellow;
        foreach (var wp in puntosRuta)
            if (wp != null) Gizmos.DrawWireSphere(wp.position, 0.6f);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  INICIALIZACIÓN
    // ══════════════════════════════════════════════════════════════════════

    private Mesh ObtenerMeshCapsula()
    {
        // Crear un primitivo temporal solo para robar su mesh compartida
        var temp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        var mesh = Object.Instantiate(temp.GetComponent<MeshFilter>().sharedMesh);
        Object.DestroyImmediate(temp);
        return mesh;
    }

    private Material CrearMaterialInstanciado()
    {
        // FIX LEAK: la versión anterior creaba "src = new Material(shader)" como objeto
        // intermedio nunca destruido cuando materialMultitud == null → leak de material.
        // Ahora se crea directamente sin Material intermedio.
        if (materialMultitud != null)
        {
            var mat = new Material(materialMultitud);
            mat.enableInstancing = true;
            return mat;
        }
        var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard")
                  ?? Shader.Find("HDRP/Unlit") ?? Shader.Find("Standard")
                  ?? Shader.Find("Standard");
        if (shader == null)
        {
            AlsasuaLogger.Error("SistemaMultitud", "No se encontró shader URP/Lit. " +
                               "Asigna un material en el Inspector.");
            return null;
        }
        var matFallback = new Material(shader);  // sin Material intermedio → sin leak
        matFallback.enableInstancing = true;
        return matFallback;
    }

    private void InicializarGrid()
    {
        _celdaSize = radioSeparacion * 2.4f;           // celda = ~1.5m para peaton
        _invCelda  = 1f / _celdaSize;

        int total     = GRID_DIM * GRID_DIM;
        _gridBuckets  = new int[total][];
        _gridCounts   = new int[total];
        for (int k = 0; k < total; k++)
            _gridBuckets[k] = new int[16];             // capacidad inicial: 16 agentes/celda
    }

    private void InicializarAgentes()
    {
        // Paleta de ropa de manifestación vasca
        var paleta = new Color[]
        {
            new Color(0.80f, 0.08f, 0.08f),  // rojo
            new Color(0.06f, 0.06f, 0.06f),  // negro
            new Color(0.12f, 0.42f, 0.18f),  // verde
            new Color(0.88f, 0.80f, 0.08f),  // amarillo (ikurriña)
            new Color(0.10f, 0.18f, 0.52f),  // azul marino
            new Color(0.88f, 0.88f, 0.88f),  // blanco
            new Color(0.42f, 0.26f, 0.12f),  // marrón
            new Color(0.35f, 0.18f, 0.45f),  // morado
        };

        // FIX: guards adicionales para waypoints individuales nulos (se pueden borrar en Editor)
        Vector3 origen = (puntosRuta != null && puntosRuta.Length > 0 && puntosRuta[0] != null)
            ? puntosRuta[0].position
            : transform.position;

        Vector3 dir = (puntosRuta != null && puntosRuta.Length > 1
                       && puntosRuta[0] != null && puntosRuta[1] != null)
            ? (puntosRuta[1].position - puntosRuta[0].position).normalized
            : transform.forward;

        Vector3 derecha = Vector3.Cross(Vector3.up, dir).normalized;

        for (int i = 0; i < _numAgentes; i++)
        {
            int fila    = i / anchoFormacion;
            int columna = i % anchoFormacion;

            // Desplazamiento en la formación: filas hacia atrás, columnas laterales
            float offsetX = (columna - anchoFormacion * 0.5f) * 0.72f + Random.Range(-0.12f, 0.12f);
            float offsetZ = -fila * 0.80f + Random.Range(-0.10f, 0.10f);

            Vector3 posInicial = origen
                + derecha * offsetX
                + dir     * offsetZ;

            // FIX HITCH: NO hacer raycast aquí. 1000 raycasts síncronos en Awake() causaban
            // un bloqueo de 80-200ms en el primer frame. En su lugar, los agentes parten a
            // posInicial.y y CorregirAlturasIniciales() (coroutine desde Start()) los corrige
            // distribuyendo RAYCASTS_POR_FRAME raycasts por frame → 20 frames / ~333ms a 60fps.
            // MuestrearSueloPorTurno() sigue activo para corrección continua de topografía.
            float alturaY = posInicial.y;

            bool esPortador = (i < portadoresPancarta);

            _agentes[i] = new AgentData
            {
                posicion      = new Vector3(posInicial.x, alturaY, posInicial.z),
                velocidad     = dir * velocidadMarcha,
                waypointActual = 0,
                alturaY       = alturaY,
            };

            // Portadores de pancarta: camiseta blanca; resto: color aleatorio
            _coloresInstancia[i] = esPortador
                ? new Vector4(0.95f, 0.95f, 0.95f, 1f)
                : (Vector4)paleta[Random.Range(0, paleta.Length)];
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  SPATIAL HASH GRID
    // ══════════════════════════════════════════════════════════════════════

    private void ActualizarGridSpatial()
    {
        // Centroide de la multitud → origen del grid (sigue a la masa)
        Vector3 centroide = Vector3.zero;
        for (int i = 0; i < _numAgentes; i++) centroide += _agentes[i].posicion;
        centroide /= _numAgentes;

        float halfSize = GRID_DIM * _celdaSize * 0.5f;
        _gridOrigen = new Vector3(centroide.x - halfSize, 0f, centroide.z - halfSize);

        // Reset contadores sin alloc (reutilizar arrays)
        System.Array.Clear(_gridCounts, 0, _gridCounts.Length);

        // Insertar agentes en sus celdas
        for (int i = 0; i < _numAgentes; i++)
        {
            int cx  = Mathf.Clamp((int)((_agentes[i].posicion.x - _gridOrigen.x) * _invCelda), 0, GRID_DIM - 1);
            int cz  = Mathf.Clamp((int)((_agentes[i].posicion.z - _gridOrigen.z) * _invCelda), 0, GRID_DIM - 1);
            int key = cz * GRID_DIM + cx;

            int cnt = _gridCounts[key];
            if (cnt >= _gridBuckets[key].Length)
            {
                // Expansión dinámica: solo ocurre en primeros frames si la densidad
                // inicial supera la capacidad. Tras el primer frame estabiliza.
                var ampliado = new int[_gridBuckets[key].Length * 2];
                _gridBuckets[key].CopyTo(ampliado, 0);
                _gridBuckets[key] = ampliado;
            }

            _gridBuckets[key][cnt] = i;
            _gridCounts[key]++;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  FLOCKING + SEGUIMIENTO DE RUTA
    // ══════════════════════════════════════════════════════════════════════

    private void ActualizarFlocking(float dt)
    {
        if (puntosRuta == null || puntosRuta.Length == 0) return;

        float r2Sep = radioSeparacion * radioSeparacion;
        float r2Coh = radioCohesion   * radioCohesion;
        float r2Ali = radioAlineacion * radioAlineacion;
        float velMax = velocidadMarcha * 1.8f;

        for (int i = 0; i < _numAgentes; i++)
        {
            ref AgentData ag = ref _agentes[i];

            // ── 1. Fuerza de ruta ──────────────────────────────────────────
            // FIX LOOP: skip nulos con un contador de intentos para no buclear infinitamente
            // si todos los waypoints son nulos (ej. Editor borra los GOs en runtime).
            // FIX PILE-UP: antes usaba < Length-1 → agentes se detenían en el último punto.
            // Ahora usa módulo → vuelven al inicio y la ruta es circular permanente.
            int intentosSkip = 0;
            while (puntosRuta[ag.waypointActual] == null && intentosSkip < puntosRuta.Length)
            {
                ag.waypointActual = (ag.waypointActual + 1) % puntosRuta.Length;
                intentosSkip++;
            }

            if (puntosRuta[ag.waypointActual] == null) continue;   // todos nulos: skip agente

            Vector3 wpPos  = puntosRuta[ag.waypointActual].position;
            wpPos.y = ag.posicion.y;
            Vector3 dirWP  = wpPos - ag.posicion;
            float   distWP = dirWP.magnitude;

            // FIX PILE-UP: avanzar con módulo → ruta circular, sin detención en el último WP.
            if (distWP < 3f)
                ag.waypointActual = (ag.waypointActual + 1) % puntosRuta.Length;

            Vector3 fuerzaRuta = (distWP > 0.05f)
                ? (dirWP / distWP) * velocidadMarcha * pesoRuta
                : Vector3.zero;

            // ── 2. Flocking desde vecinos (solo celdas adyacentes) ────────
            int cx0 = Mathf.Max(0, (int)(((ag.posicion.x - _gridOrigen.x) * _invCelda) - 1));
            int cx1 = Mathf.Min(GRID_DIM - 1, cx0 + 2);
            int cz0 = Mathf.Max(0, (int)(((ag.posicion.z - _gridOrigen.z) * _invCelda) - 1));
            int cz1 = Mathf.Min(GRID_DIM - 1, cz0 + 2);

            Vector3 fSep = Vector3.zero, cohSum = Vector3.zero, aliSum = Vector3.zero;
            int     nCoh = 0, nAli = 0;

            for (int gz = cz0; gz <= cz1; gz++)
            {
                for (int gx = cx0; gx <= cx1; gx++)
                {
                    int key   = gz * GRID_DIM + gx;
                    int count = _gridCounts[key];

                    for (int k = 0; k < count; k++)
                    {
                        int j = _gridBuckets[key][k];
                        if (j == i) continue;

                        Vector3 delta = ag.posicion - _agentes[j].posicion;
                        delta.y = 0f;
                        float d2 = delta.sqrMagnitude;

                        // Separación: fuerza inversa a la distancia (más fuerte más cerca)
                        if (d2 < r2Sep && d2 > 0.0001f)
                            fSep += delta / Mathf.Sqrt(d2);

                        // Cohesión: tender al centro de masa local
                        if (d2 < r2Coh) { cohSum += _agentes[j].posicion; nCoh++; }

                        // Alineación: igualar velocidad con vecinos
                        if (d2 < r2Ali) { aliSum += _agentes[j].velocidad; nAli++; }
                    }
                }
            }

            Vector3 fCoh = (nCoh > 0)
                ? ((cohSum / nCoh) - ag.posicion).normalized * pesoCohesion
                : Vector3.zero;
            Vector3 fAli = (nAli > 0)
                ? (aliSum / nAli).normalized * pesoAlineacion
                : Vector3.zero;

            // ── 3. Integración ─────────────────────────────────────────────
            Vector3 aceleracion = fuerzaRuta
                                + fSep * pesoSeparacion
                                + fCoh
                                + fAli;
            aceleracion.y = 0f;

            ag.velocidad   += aceleracion * dt;
            ag.velocidad.y  = 0f;

            float spd = ag.velocidad.magnitude;
            if (spd > velMax) ag.velocidad = ag.velocidad * (velMax / spd);
            if (spd < 0.05f)  ag.velocidad = (wpPos - ag.posicion).normalized * velocidadMarcha * 0.5f;

            ag.posicion   += ag.velocidad * dt;
            ag.posicion.y  = ag.alturaY;   // suelo fijo (se refresca por raycast escalonado)
        }
    }

    /// <summary>
    /// Un raycast por frame, rotando entre agentes (O(1)/frame).
    /// Actualiza ag.alturaY para que el agente siga la topografía real de Cesium.
    /// </summary>
    private void MuestrearSueloPorTurno()
    {
        if (_numAgentes == 0) return;
        _indiceRaycast = (_indiceRaycast + 1) % _numAgentes;
        ref AgentData ag = ref _agentes[_indiceRaycast];
        if (Physics.Raycast(ag.posicion + Vector3.up * 8f, Vector3.down, out RaycastHit hit, 30f))
            ag.alturaY = hit.point.y;
    }

    /// <summary>
    /// Corrección inicial de alturas distribuida en frames.
    /// Hace RAYCASTS_POR_FRAME raycasts/frame → completa en ~20 frames (≈333ms a 60fps)
    /// en lugar del bloqueo de 80-200ms que causaban 1000 raycasts síncronos en Awake().
    /// MuestrearSueloPorTurno() continúa activo para corrección de topografía en runtime.
    /// </summary>
    private System.Collections.IEnumerator CorregirAlturasIniciales()
    {
        for (int i = 0; i < _numAgentes; i++)
        {
            if (Physics.Raycast(_agentes[i].posicion + Vector3.up * 10f, Vector3.down,
                                out RaycastHit hit, 60f))
            {
                _agentes[i].posicion.y = hit.point.y;
                _agentes[i].alturaY    = hit.point.y;
            }

            // Ceder el control al motor cada RAYCASTS_POR_FRAME raycasts.
            // Ej: 1000 agentes / 50 raycasts por frame = 20 yields = 20 frames ≈ 333ms @ 60fps.
            if ((i + 1) % RAYCASTS_POR_FRAME == 0)
                yield return null;
        }
        AlsasuaLogger.Info("SistemaMultitud", "Alturas iniciales corregidas sin hitch de carga.");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RENDER (GPU Instancing) + PANCARTA
    // ══════════════════════════════════════════════════════════════════════

    private void ActualizarMatricesYPancarta()
    {
        Vector3 posCentroidePancarta = Vector3.zero;
        Vector3 dirMediaPancarta     = Vector3.zero;
        int     nPortadores          = Mathf.Min(portadoresPancarta, _numAgentes);

        for (int i = 0; i < _numAgentes; i++)
        {
            Vector3 vel = _agentes[i].velocidad;
            Quaternion rot = (vel.sqrMagnitude > 0.01f)
                ? Quaternion.LookRotation(vel.normalized, Vector3.up)
                : Quaternion.identity;

            // Escala: cápsula de ~0.35m radio × ~1.75m alto
            _matrices[i] = Matrix4x4.TRS(
                _agentes[i].posicion + Vector3.up * 0.875f,
                rot,
                new Vector3(0.35f, 0.875f, 0.35f));

            if (i < nPortadores)
            {
                posCentroidePancarta += _agentes[i].posicion;
                dirMediaPancarta     += vel;
            }
        }

        // Actualizar posición de la pancarta
        if (_goRacimo != null && nPortadores > 0)
        {
            posCentroidePancarta /= nPortadores;
            dirMediaPancarta     /= nPortadores;

            _goRacimo.transform.position = posCentroidePancarta;
            if (dirMediaPancarta.sqrMagnitude > 0.01f)
                _goRacimo.transform.rotation = Quaternion.LookRotation(dirMediaPancarta.normalized, Vector3.up);
        }
    }

    private void RenderizarGPUInstanced()
    {
        if (_meshAgente == null || _matInstanciada == null) return;

        int enviados = 0;
        while (enviados < _numAgentes)
        {
            int lote = Mathf.Min(MAX_LOTE, _numAgentes - enviados);

            System.Array.Copy(_matrices, enviados, _matrizLote, 0, lote);

            // FIX GC: usar List<Vector4> pre-allocada en lugar de SubArray() que creaba
            // new Vector4[lote] cada frame (~11KB/frame para 700 agentes a 60FPS).
            // SetVectorArray(List<Vector4>) acepta cualquier tamaño. La List tiene capacidad
            // MAX_LOTE fijada en Awake() → Clear() + Add() es cero GC dentro de capacidad.
            _colorListaLote.Clear();
            for (int k = enviados; k < enviados + lote; k++)
                _colorListaLote.Add(_coloresInstancia[k]);

            _propBlock.SetVectorArray(_idBaseColor, _colorListaLote);
            Graphics.DrawMeshInstanced(_meshAgente, 0, _matInstanciada, _matrizLote, lote, _propBlock);

            enviados += lote;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  PANCARTA "ANFETA" + hacha y serpiente
    // ══════════════════════════════════════════════════════════════════════

    private void CrearPancarta()
    {
        _goRacimo = new GameObject("Pancarta_Anfeta");
        _goRacimo.transform.SetParent(transform);

        // ── Tela de la pancarta ───────────────────────────────────────────
        var tela = GameObject.CreatePrimitive(PrimitiveType.Quad);
        tela.name = "Tela";
        tela.transform.SetParent(_goRacimo.transform);
        tela.transform.localPosition = new Vector3(0f, 1.9f, 0.5f);
        tela.transform.localScale    = new Vector3(4.5f, 1.1f, 1f);
        tela.transform.localRotation = Quaternion.identity;
        Object.Destroy(tela.GetComponent<Collider>());

        var matTela = CrearMaterialPancarta();
        tela.GetComponent<Renderer>().sharedMaterial = matTela;
        _matsPancarta.Add(matTela);

        // ── Palos en los extremos ─────────────────────────────────────────
        AnadirPalo(_goRacimo.transform, new Vector3(-2.1f, 0.65f, 0.5f));
        AnadirPalo(_goRacimo.transform, new Vector3( 2.1f, 0.65f, 0.5f));
    }

    private Material CrearMaterialPancarta()
    {
        var shader = Shader.Find("HDRP/Unlit") ?? Shader.Find("Standard")
                  ?? Shader.Find("Unlit/Texture")
                  ?? Shader.Find("Standard");
        if (shader == null)
        {
            // Fallback de emergencia rastreado para limpieza
            var matError = new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Standard"));
            if (matError != null) _matsPancarta.Add(matError);
            return matError;
        }
        // FIX LEAK: guardar referencia a la textura procedural. Object.Destroy(material) NO
        // destruye automáticamente las texturas asignadas vía mainTexture en la mayoría de
        // versiones de Unity — hay que destruirlas explícitamente (ver OnDestroy).
        _texturaPancarta = GenerarTexturaPancarta(512, 128);
        var mat = new Material(shader);
        mat.mainTexture = _texturaPancarta;
        return mat;
    }

    /// <summary>
    /// Genera la textura de la pancarta procedrualmente:
    /// fondo rojo oscuro, borde blanco, texto "ANFETA" y símbolo hacha+serpiente
    /// dibujados en blanco con trazos pixel-art gruesos (sin dependencia de fuentes).
    /// </summary>
    private Texture2D GenerarTexturaPancarta(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp
        };

        var pixels = new Color[w * h];

        // ── Fondo rojo oscuro ─────────────────────────────────────────────
        Color fondoRojo   = new Color(0.72f, 0.05f, 0.05f);
        Color colBlanco   = Color.white;
        for (int i = 0; i < pixels.Length; i++) pixels[i] = fondoRojo;

        // ── Borde blanco de 3px ───────────────────────────────────────────
        for (int x = 0; x < w; x++)
        {
            for (int b = 0; b < 3; b++)
            {
                pixels[ b        * w + x] = colBlanco;
                pixels[(h-1-b)   * w + x] = colBlanco;
            }
        }
        for (int y = 0; y < h; y++)
        {
            for (int b = 0; b < 3; b++)
            {
                pixels[y * w + b]       = colBlanco;
                pixels[y * w + (w-1-b)] = colBlanco;
            }
        }

        // ── Letras "ANFETA" en pixel-art (4×7 px cada carácter, escala ×5) ──
        // Definición de bits: cada carácter = 7 filas de 4 bits (top→bottom)
        int[][] glifos = new int[][]
        {
            // A
            new int[]{0b0110, 0b1001, 0b1001, 0b1111, 0b1001, 0b1001, 0b1001},
            // N
            new int[]{0b1001, 0b1101, 0b1101, 0b1011, 0b1011, 0b1001, 0b1001},
            // F
            new int[]{0b1111, 0b1000, 0b1000, 0b1110, 0b1000, 0b1000, 0b1000},
            // E
            new int[]{0b1111, 0b1000, 0b1000, 0b1110, 0b1000, 0b1000, 0b1111},
            // T
            new int[]{0b1111, 0b0100, 0b0100, 0b0100, 0b0100, 0b0100, 0b0100},
            // A
            new int[]{0b0110, 0b1001, 0b1001, 0b1111, 0b1001, 0b1001, 0b1001},
        };

        int escalaGlifo = 5;
        int anchoGlifo  = 4 * escalaGlifo;
        int altoGlifo   = 7 * escalaGlifo;
        int margenIzq   = 90;   // símbolo a la izquierda, texto empieza más a la derecha
        int margenY     = (h - altoGlifo) / 2;

        for (int g = 0; g < glifos.Length; g++)
        {
            int baseX = margenIzq + g * (anchoGlifo + escalaGlifo);
            for (int row = 0; row < 7; row++)
            {
                int bits = glifos[g][row];
                for (int col = 0; col < 4; col++)
                {
                    bool encendido = ((bits >> (3 - col)) & 1) == 1;
                    if (!encendido) continue;
                    for (int sy = 0; sy < escalaGlifo; sy++)
                    {
                        for (int sx = 0; sx < escalaGlifo; sx++)
                        {
                            int px = baseX + col * escalaGlifo + sx;
                            int py = margenY + (6 - row) * escalaGlifo + sy;  // flip Y
                            if (px >= 0 && px < w && py >= 0 && py < h)
                                pixels[py * w + px] = colBlanco;
                        }
                    }
                }
            }
        }

        // ── Símbolo hacha + serpiente (pixel-art simplificado, lado izquierdo) ──
        DibujarSimboloHachaSerpiente(pixels, w, h, 12, h / 2 - 28, 56, colBlanco);

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// Dibuja un símbolo estilizado de hacha (rectángulo diagonal con filo)
    /// y serpiente (curva en S) en pixel-art dentro del área indicada.
    /// </summary>
    // FIX: eliminado parámetro "colorFondo" no usado → suprimía CS0168 warning del compilador.
    private void DibujarSimboloHachaSerpiente(
        Color[] pixels, int w, int h,
        int ox, int oy, int size,
        Color colorLinea)
    {
        // Mango del hacha: línea diagonal gruesa
        for (int t = 0; t < size; t++)
        {
            int px = ox + t;
            int py = oy + t;
            PintarBloque(pixels, w, h, px, py, 3, colorLinea);
        }

        // Hoja del hacha: triángulo en la parte superior
        for (int t = 0; t < size / 2; t++)
        {
            for (int k = 0; k <= t; k++)
            {
                PintarBloque(pixels, w, h, ox + t - k, oy + size - 5 + k, 2, colorLinea);
            }
        }

        // Serpiente: curva en S usando seno
        for (int t = 4; t < size - 4; t++)
        {
            float angulo  = (float)t / size * Mathf.PI * 3f;
            float offsetX = Mathf.Sin(angulo) * 6f;
            int   px      = ox + size / 2 + (int)offsetX + 6;
            int   py      = oy + t;
            PintarBloque(pixels, w, h, px, py, 2, colorLinea);
        }
    }

    private void PintarBloque(Color[] pixels, int w, int h, int cx, int cy, int radio, Color color)
    {
        for (int dy = -radio; dy <= radio; dy++)
            for (int dx = -radio; dx <= radio; dx++)
            {
                int px = cx + dx, py = cy + dy;
                if (px >= 0 && px < w && py >= 0 && py < h)
                    pixels[py * w + px] = color;
            }
    }

    private void AnadirPalo(Transform padre, Vector3 posLocal)
    {
        var palo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        palo.name = "Palo";
        palo.transform.SetParent(padre);
        palo.transform.localPosition = posLocal;
        palo.transform.localScale    = new Vector3(0.06f, 0.9f, 0.06f);
        Object.Destroy(palo.GetComponent<Collider>());

        var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Standard");
        if (shader == null) return;

        var mat = new Material(shader) { color = new Color(0.50f, 0.32f, 0.12f) };
        palo.GetComponent<Renderer>().sharedMaterial = mat;
        _matsPancarta.Add(mat);   // tracked → OnDestroy
    }
}
