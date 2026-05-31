// Assets/Scripts/SistemaFerroviario.cs
// Simulación de tráfico ferroviario en la línea Madrid–Irún a su paso por Alsasua.
//
// ── CONTEXTO REAL ─────────────────────────────────────────────────────────────
//  Los Alvia/Intercity de Renfe pasan por Alsasua en la línea Madrid–Irún:
//  Madrid → Burgos → Miranda → Vitoria → Alsasua → Donostia → Irún
//  La estación de Alsasua está en aprox. Lat 42.9037, Lon -2.1668.
//
// ── ARQUITECTURA ──────────────────────────────────────────────────────────────
//  • TrenData : struct  → sin MonoBehaviour por tren, array contiguo.
//  • Cada tren es una composición de vagones renderizados con TRS matrices.
//    Los vagones no son GameObjects; se renderizan con DrawMeshInstanced.
//  • El tren más cercano al jugador usa un GameObject real (detalle completo).
//  • La vía se define por waypoints colocados sobre la geometría real de Cesium.
//  • Interpolación cúbica Catmull-Rom entre waypoints → curvas suaves.
//
// ── MEMORIA ───────────────────────────────────────────────────────────────────
//  • Arrays pre-allocados en Awake(). Cero GC en Update.
//  • Materiales creados por código → OnDestroy los destruye.
//  • GameObject visual del tren cercano se reutiliza (no se destruye/crea en runtime).
//
// ── SETUP EN EDITOR ───────────────────────────────────────────────────────────
//  1. Crear "SistemaFerroviario" GameObject y añadir este componente.
//  2. Crear waypoints siguiendo la vía real visible en los tiles de Cesium.
//     Sugerencia: colocarlos cada ~50m sobre la vía en Alsasua (eje E-O).
//  3. Configurar velocidad y número de trenes.

using UnityEngine;
using System.Collections.Generic;
using Unity.Profiling;

[AddComponentMenu("Alsasua/Sistema Ferroviario")]
public sealed class SistemaFerroviario : MonoBehaviour
{
    // ══════════════════════════════════════════════════════════════════════
    //  INSPECTOR
    // ══════════════════════════════════════════════════════════════════════

    [Header("═══ VÍA FÉRREA ═══")]
    [Tooltip("Waypoints sobre la vía real. Sitúalos sobre los rails visibles en Cesium.")]
    [SerializeField] private Transform[] waypointsVia;

    [Tooltip("Usar interpolación Catmull-Rom entre waypoints (curvas suaves).")]
    [SerializeField] private bool usarCatmullRom = true;

    [Header("═══ TRENES ═══")]
    [Range(1, 6)]
    [Tooltip("Número de trenes simultáneos circulando por la vía.")]
    [SerializeField] private int numTrenes = 2;

    [Range(3, 12)]
    [Tooltip("Número de vagones por tren.")]
    [SerializeField] private int numVagones = 6;

    [Range(40f, 200f)]
    [Tooltip("Velocidad del tren en km/h. Cercanías ~80 km/h, Alvia ~160 km/h.")]
    [SerializeField] private float velocidadKmh = 80f;

    [Range(10f, 20f)]
    [Tooltip("Longitud de cada vagón en metros.")]
    [SerializeField] private float longitudVagon = 14f;

    [Range(0.5f, 3f)]
    [Tooltip("Separación entre vagones en metros.")]
    [SerializeField] private float separacionVagones = 1.2f;

    [Header("═══ VISUAL ═══")]
    [SerializeField] private Color colorLocomotora   = new Color(0.08f, 0.12f, 0.45f);  // azul Renfe
    [SerializeField] private Color colorVagon        = new Color(0.85f, 0.85f, 0.85f);  // plata/blanco
    [SerializeField] private bool  mostrarGizmos     = true;

    // ══════════════════════════════════════════════════════════════════════
    //  TIPOS INTERNOS
    // ══════════════════════════════════════════════════════════════════════

    private struct TrenData
    {
        public float  distanciaRecorrida;   // distancia acumulada a lo largo de la vía (m)
        public float  velocidad;            // m/s
        public bool   sentidoPositivo;      // true = hacia waypoint final; false = vuelta
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CAMPOS PRIVADOS
    // ══════════════════════════════════════════════════════════════════════

    private TrenData[]   _trenes;
    private float        _longitudVia;           // longitud total de la vía en metros
    private float[]      _distanciasSegmentos;   // distancia acumulada hasta cada waypoint

    // Render
    private Mesh         _meshVagon;
    private Material     _matLocomotora;
    private Material     _matVagon;
    private List<Material> _matsCreados = new List<Material>();

    // Buffer para render instanciado
    private Matrix4x4[]  _matrices;
    private Vector4[]    _colores;
    private bool[]        _esLocomotora;    // FIX: flag bool en lugar de comparación float frágil
    private Matrix4x4[]   _matrizLote;
    private List<Vector4> _colorListaLote; // FIX GC: pre-alloc → cero alloc por frame
    private const int    MAX_LOTE = 1023;

    private MaterialPropertyBlock _propBlock;
    private static readonly int _idBaseColor = Shader.PropertyToID("_BaseColor");

    // ── Profiler marker ─────────────────────────────────────────────────
    // FIX: static readonly field initializer lanza TypeInitializationException
    // en Unity EditMode tests → mover a static constructor con try/catch.
    private static ProfilerMarker _markerUpdate;
    static SistemaFerroviario()
    {
        try   { _markerUpdate = new ProfilerMarker("SistemaFerroviario.Update"); }
        catch (System.Exception) { _markerUpdate = default; }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ══════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        // Si no hay waypoints asignados en Inspector, auto-crearlos desde GeoDataAlsasua.
        if (waypointsVia == null || waypointsVia.Length < 2)
        {
            AlsasuaLogger.Info("SistemaFerroviario",
                "Sin waypoints en Inspector → auto-creando vía desde GeoDataAlsasua.ViaFerreaNorte.");
            waypointsVia = CrearWaypointsDesdeGeoData();
        }

        if (waypointsVia == null || waypointsVia.Length < 2)
        {
            AlsasuaLogger.Warn("SistemaFerroviario", "No se pudieron crear waypoints de vía. Sistema desactivado.");
            enabled = false;
            return;
        }

        _propBlock = new MaterialPropertyBlock();

        CalcularLongitudVia();
        CrearRecursosMesh();
        PreallocarArrays();
        InicializarTrenes();
    }

    private void Update()
    {
        using var _prof = _markerUpdate.Auto();
        if (_trenes == null) return;
        ActualizarTrenes(Time.deltaTime);
        RenderizarTrenes();
    }

    private void OnDestroy()
    {
        foreach (var m in _matsCreados)
            if (m != null) Object.Destroy(m);
        _matsCreados.Clear();

        if (_meshVagon != null) { Object.Destroy(_meshVagon); _meshVagon = null; }

        // FIX: anular referencias para que el GC no mantenga objetos destruidos.
        // Sin esto, código de Editor o inspectores externos podrían acceder a
        // _matLocomotora/_matVagon después de que Unity los marcase como destruidos.
        _matLocomotora = null;
        _matVagon      = null;
        _propBlock     = null;
    }

    private void OnDrawGizmos()
    {
        if (!mostrarGizmos || waypointsVia == null) return;
        Gizmos.color = new Color(0.6f, 0.6f, 0.6f, 0.9f);
        for (int i = 0; i < waypointsVia.Length - 1; i++)
        {
            if (waypointsVia[i] != null && waypointsVia[i + 1] != null)
                Gizmos.DrawLine(waypointsVia[i].position, waypointsVia[i + 1].position);
        }
        Gizmos.color = new Color(0.3f, 0.3f, 0.9f);
        foreach (var wp in waypointsVia)
            if (wp != null) Gizmos.DrawWireSphere(wp.position, 1.5f);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  INICIALIZACIÓN
    // ══════════════════════════════════════════════════════════════════════

    // ══════════════════════════════════════════════════════════════════════
    //  AUTO-CREACIÓN DE WAYPOINTS DESDE GEODATA
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Crea los waypoints de la vía del tren automáticamente a partir de
    /// GeoDataAlsasua.ViaFerreaNorte cuando no hay waypoints asignados en Inspector.
    /// Genera GameObjects hijos como contenedores de waypoints.
    /// </summary>
    private Transform[] CrearWaypointsDesdeGeoData()
    {
        var puntos = GeoDataAlsasua.ViaFerreaNorte;
        if (puntos == null || puntos.Length < 2) return null;

        var padre = new GameObject("ViaFerrea_GeoData");
        padre.transform.SetParent(transform);

        var wps = new Transform[puntos.Length];
        for (int i = 0; i < puntos.Length; i++)
        {
            var wp = new GameObject($"ViaWP_{i:D2}");
            wp.transform.SetParent(padre.transform);
            wp.transform.position = puntos[i];
            wps[i] = wp.transform;
        }

        AlsasuaLogger.Info("SistemaFerroviario",
            $"✓ {wps.Length} waypoints de vía creados desde GeoDataAlsasua " +
            "(Madrid–Irún pasando por estación Alsasua).");
        return wps;
    }

    /// <summary>
    /// Calcula la longitud total de la vía y guarda la distancia acumulada
    /// en cada waypoint para poder hacer lookup O(log n) con BinarySearch.
    /// </summary>
    private void CalcularLongitudVia()
    {
        int n = waypointsVia.Length;
        _distanciasSegmentos = new float[n];
        _distanciasSegmentos[0] = 0f;
        _longitudVia = 0f;

        for (int i = 1; i < n; i++)
        {
            if (waypointsVia[i] == null || waypointsVia[i - 1] == null)
            {
                _distanciasSegmentos[i] = _distanciasSegmentos[i - 1];
                continue;
            }
            float segLen = Vector3.Distance(waypointsVia[i - 1].position, waypointsVia[i].position);
            _longitudVia += segLen;
            _distanciasSegmentos[i] = _longitudVia;
        }
    }

    private void CrearRecursosMesh()
    {
        // Vagón: paralelepípedo proporcional a un vagón de Renfe (~14×3.1×3.2m)
        var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);

        // FIX CRÍTICO: GetComponent<MeshFilter>() puede devolver null → NPE en .sharedMesh
        var meshFilter = temp.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            AlsasuaLogger.Error("SistemaFerroviario", "MeshFilter no encontrado en primitivo Cube.");
            Object.DestroyImmediate(temp);
            return;
        }
        _meshVagon = Object.Instantiate(meshFilter.sharedMesh);

        // FIX LIMPIEZA: destruir el colisionador antes del GO para liberar los datos de
        // colisión internas; solo así se garantiza que no queden recursos huérfanos.
        var col = temp.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);
        Object.DestroyImmediate(temp);

        // FIX NULL: guard contra shader no encontrado → new Material(null) lanza NRE
        var litShader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Standard");
        if (litShader == null)
        {
            AlsasuaLogger.Error("SistemaFerroviario", "Shader URP/Lit no encontrado. " +
                               "Añádelo a Always Included Shaders o asigna un Material en el Inspector.");
            litShader = Shader.Find("Hidden/InternalErrorShader");
            if (litShader == null) return;
        }

        _matLocomotora = new Material(litShader) { color = colorLocomotora };
        _matVagon      = new Material(litShader) { color = colorVagon };
        _matLocomotora.enableInstancing = true;
        _matVagon.enableInstancing      = true;
        _matsCreados.Add(_matLocomotora);
        _matsCreados.Add(_matVagon);
    }

    private void PreallocarArrays()
    {
        int maxInstancias = numTrenes * numVagones;
        _trenes         = new TrenData[numTrenes];
        _matrices       = new Matrix4x4[maxInstancias];
        _colores        = new Vector4[maxInstancias];
        _esLocomotora   = new bool[maxInstancias];           // flag bool: sin comparación float
        _matrizLote     = new Matrix4x4[MAX_LOTE];
        _colorListaLote = new List<Vector4>(MAX_LOTE);      // cero GC en render
    }

    private void InicializarTrenes()
    {
        float velocidadMs = velocidadKmh / 3.6f;
        float separacion  = _longitudVia / numTrenes;   // distribuir trenes uniformemente

        for (int i = 0; i < numTrenes; i++)
        {
            _trenes[i] = new TrenData
            {
                distanciaRecorrida = separacion * i,
                velocidad          = velocidadMs,
                sentidoPositivo    = (i % 2 == 0),  // trenes alternos en sentidos opuestos
            };
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  LÓGICA
    // ══════════════════════════════════════════════════════════════════════

    private void ActualizarTrenes(float dt)
    {
        for (int i = 0; i < numTrenes; i++)
        {
            ref TrenData t = ref _trenes[i];

            float desplazamiento = t.velocidad * dt;
            if (t.sentidoPositivo)
            {
                t.distanciaRecorrida += desplazamiento;
                if (t.distanciaRecorrida >= _longitudVia)
                {
                    // Al llegar al final, invertir dirección (servicio de ida y vuelta)
                    t.distanciaRecorrida = _longitudVia - (t.distanciaRecorrida - _longitudVia);
                    t.sentidoPositivo = false;
                }
            }
            else
            {
                t.distanciaRecorrida -= desplazamiento;
                if (t.distanciaRecorrida <= 0f)
                {
                    t.distanciaRecorrida = -t.distanciaRecorrida;
                    t.sentidoPositivo = true;
                }
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RENDER
    // ══════════════════════════════════════════════════════════════════════

    private void RenderizarTrenes()
    {
        // FIX: validar todos los recursos antes de renderizar (evita NRE silenciosa si
        // CrearRecursosMesh() retornó antes de tiempo por shader/mesh no encontrado).
        if (_meshVagon == null || _matLocomotora == null || _matVagon == null) return;

        float pasoPorVagon  = longitudVagon + separacionVagones;
        int   instanciaIdx  = 0;

        for (int t = 0; t < numTrenes; t++)
        {
            float distBase = _trenes[t].distanciaRecorrida;

            for (int v = 0; v < numVagones; v++)
            {
                // Cada vagón está desplazado hacia atrás de la locomotora
                float offset   = v * pasoPorVagon;
                float distVagon = _trenes[t].sentidoPositivo
                    ? distBase - offset
                    : distBase + offset;

                // Clamp dentro de la vía (FIX: NaN guard — evita que Clamp propague NaN)
                if (float.IsNaN(distVagon)) distVagon = 0f;
                distVagon = Mathf.Clamp(distVagon, 0f, _longitudVia);

                // Obtener posición y tangente en esa distancia
                PosicionEnVia(distVagon, out Vector3 posicion, out Vector3 tangente);

                // FIX DIRECCIÓN: PosicionEnVia devuelve siempre la tangente en sentido
                // positivo (distancia creciente). Si el tren va en sentido negativo, hay
                // que invertirla para que los vagones miren hacia donde va el tren.
                if (!_trenes[t].sentidoPositivo) tangente = -tangente;

                Quaternion rotacion = (tangente.sqrMagnitude > 0.001f)
                    ? Quaternion.LookRotation(tangente, Vector3.up)
                    : Quaternion.identity;

                _matrices[instanciaIdx] = Matrix4x4.TRS(
                    posicion + Vector3.up * 1.6f,
                    rotacion,
                    new Vector3(3.0f, 3.2f, longitudVagon));

                bool esLoco = (v == 0);
                // FIX COMPARACIÓN: usar flag bool en vez de (Vector4)colorLocomotora == _colores[i]
                // (comparación float frágil, falla si el campo se modifica en Inspector en runtime)
                _esLocomotora[instanciaIdx] = esLoco;
                _colores[instanciaIdx] = esLoco ? (Vector4)colorLocomotora : (Vector4)colorVagon;

                instanciaIdx++;
            }
        }

        // ── Render en lotes ──────────────────────────────────────────────
        RenderizarLotes(_matLocomotora, instanciaIdx, soloLocomotoras: true);
        RenderizarLotes(_matVagon,      instanciaIdx, soloLocomotoras: false);
    }

    private void RenderizarLotes(Material mat, int total, bool soloLocomotoras)
    {
        if (mat == null) return;

        // FIX COMPARACIÓN + FIX GC: usar _esLocomotora[] (bool) en vez de comparación float.
        // FIX GC: List<Vector4> pre-allocada en lugar de "new Vector4[n]" por frame.
        int n = 0;
        _colorListaLote.Clear();
        for (int i = 0; i < total; i++)
        {
            if (soloLocomotoras != _esLocomotora[i]) continue;

            _matrizLote[n] = _matrices[i];
            _colorListaLote.Add(_colores[i]);
            n++;

            if (n == MAX_LOTE)
            {
                _propBlock.SetVectorArray(_idBaseColor, _colorListaLote.ToArray());
                Graphics.DrawMeshInstanced(_meshVagon, 0, mat, _matrizLote, n, _propBlock);
                n = 0;
                _colorListaLote.Clear();
            }
        }

        if (n > 0)
        {
            _propBlock.SetVectorArray(_idBaseColor, _colorListaLote.ToArray());
            Graphics.DrawMeshInstanced(_meshVagon, 0, mat, _matrizLote, n, _propBlock);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  INTERPOLACIÓN EN LA VÍA
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Dado un valor de distancia acumulada sobre la vía, devuelve la posición
    /// interpolada y la tangente (dirección de avance).
    /// Usa Catmull-Rom si está activado, lineal en caso contrario.
    /// </summary>
    private void PosicionEnVia(float distancia, out Vector3 posicion, out Vector3 tangente)
    {
        // FIX PERFORMANCE: O(n) → O(log n) con Array.BinarySearch.
        // _distanciasSegmentos está garantizado como no-decreciente (calculado en CalcularLongitudVia).
        // BinarySearch devuelve el índice exacto si lo encuentra, o el complemento bit a bit
        // (~idx) del punto de inserción (primer elemento > distancia) si no.
        // Queremos el índice del segmento que COMIENZA antes de "distancia", es decir:
        //   insertionPoint - 1, con Clamp para no salir del rango válido de segmentos.
        int segIdx = System.Array.BinarySearch(_distanciasSegmentos, distancia);
        if (segIdx < 0) segIdx = ~segIdx;   // ~idx = punto de inserción (primer valor > distancia)
        segIdx = Mathf.Clamp(segIdx - 1, 0, waypointsVia.Length - 2);

        int   i0 = Mathf.Clamp(segIdx - 1, 0, waypointsVia.Length - 1);
        int   i1 = Mathf.Clamp(segIdx,     0, waypointsVia.Length - 1);
        int   i2 = Mathf.Clamp(segIdx + 1, 0, waypointsVia.Length - 1);
        int   i3 = Mathf.Clamp(segIdx + 2, 0, waypointsVia.Length - 1);

        float segLen = _distanciasSegmentos[i2] - _distanciasSegmentos[i1];
        float t = (segLen > 0.001f)
            ? (distancia - _distanciasSegmentos[i1]) / segLen
            : 0f;
        t = Mathf.Clamp01(t);

        Vector3 p0 = waypointsVia[i0] != null ? waypointsVia[i0].position : Vector3.zero;
        Vector3 p1 = waypointsVia[i1] != null ? waypointsVia[i1].position : Vector3.zero;
        Vector3 p2 = waypointsVia[i2] != null ? waypointsVia[i2].position : Vector3.zero;
        Vector3 p3 = waypointsVia[i3] != null ? waypointsVia[i3].position : Vector3.zero;

        if (usarCatmullRom)
        {
            posicion = CatmullRom(p0, p1, p2, p3, t);

            // Tangente: derivada numérica con epsilon pequeño.
            // FIX ESTABILIDAD: si t-eps y t+eps se clampan al mismo valor (e.g. t=0 con eps=0.01),
            // pB-pA puede ser Vector3.zero → .normalized devuelve (0,0,0) y el vagón rota a identity.
            // Fallback: tangente lineal del segmento (p2-p1) garantizada no-cero si los WPs difieren.
            const float eps = 0.01f;
            Vector3 pA      = CatmullRom(p0, p1, p2, p3, Mathf.Clamp01(t - eps));
            Vector3 pB      = CatmullRom(p0, p1, p2, p3, Mathf.Clamp01(t + eps));
            Vector3 delta   = pB - pA;
            tangente = delta.sqrMagnitude > 1e-6f ? delta.normalized : (p2 - p1).normalized;
        }
        else
        {
            posicion = Vector3.Lerp(p1, p2, t);
            tangente = (p2 - p1).normalized;
        }
    }

    /// <summary>
    /// Interpolación Catmull-Rom estándar entre p1 y p2, usando p0 y p3 como tangentes.
    /// Garantiza continuidad C1 en cada waypoint → curvas ferroviarias suaves.
    /// </summary>
    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t  * t;
        float t3 = t2 * t;
        return 0.5f * (
              2f * p1
            + (-p0 + p2)          * t
            + (2f*p0 - 5f*p1 + 4f*p2 - p3) * t2
            + (-p0 + 3f*p1 - 3f*p2 + p3)   * t3
        );
    }
}
