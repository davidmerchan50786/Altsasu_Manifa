// Assets/Scripts/SistemaTrafico.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE TRÁFICO — vehículos NPC + semáforos sobre roads_unity.json
//
//  Flujo de arranque:
//    1. Carga roads_unity.json → filtra vías vehiculares (tertiary + residential)
//    2. Construye waypoints Unity para cada vía
//    3. Detecta intersecciones (endpoints a <8 m entre vías distintas)
//    4. Coloca SemaforoNodo en cada intersección y los une en grupos ortogonales
//    5. Spawna el pool de VehiculoNPC y asigna rutas aleatorias
//
//  Densidad por hora del día:
//    • Hora punta (7-9h, 13-14h, 17-19h) → vehiculosMax
//    • Horas medias                       → vehiculosMax × 0.5
//    • Noche (22-6h)                      → vehiculosMax × 0.15
//
//  Semáforos:
//    • En cada intersección de ≥2 vías vehiculares se crea un SemaforoNodo
//    • Los nodos de vías cruzadas ciclan en antifase (cuando uno está verde
//      el otro está rojo) → cesión de paso automática
//    • El collider del nodo en rojo es sólido → el raycast de VehiculoNPC
//      lo detecta como obstáculo y frena sin modificar el código del vehículo
//
//  Reacción a DirectorMundo:
//    • ControlPolicial / Redada → pausa spawns y retira vehículos de la vía
//    • Calma / MercadoDia       → reanuda tráfico normal
//
//  Performance:
//    • Pool fijo de vehiculosMax vehículos (sin alloc en runtime)
//    • Waypoints creados en Start(); no se regeneran
//    • Semáforos actualizan su propio Update(); SistemaTrafico solo supervisa
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

[DefaultExecutionOrder(150)]
public class SistemaTrafico : MonoBehaviour
{
    public static SistemaTrafico Instance { get; private set; }

    // ── Config ────────────────────────────────────────────────────────────
    [Header("Pool de vehículos")]
    [SerializeField] GameObject prefabVehiculo;          // VehiculoNPC prefab; si null se genera procedural
    [Range(4, 40)]
    [SerializeField] int vehiculosMax  = 16;
    [SerializeField] float radioSpawn  = 600f;           // solo rutas dentro de este radio del jugador

    [Header("Semáforos")]
    [SerializeField] float distanciaInterseccion = 8f;   // m para considerar dos endpoints como intersección

    [Header("Datos")]
    [SerializeField] string archivoRoads = "AlsasuaData/roads_unity";   // Resources path

    // ── Estado interno ────────────────────────────────────────────────────
    struct ViaVehicular
    {
        public List<Transform> waypoints;
        public bool            esSentidoUnico;
        public string          nombre;
    }

    readonly List<ViaVehicular>  _vias            = new();
    readonly List<VehiculoNPC>   _pool            = new();
    readonly List<SemaforoNodo>  _semaforos        = new();

    Transform     _waypointsRoot;
    bool          _traficoActivo = true;
    float         _timerDensidad;
    int           _vehiculosActivos;

    // ── Franja horaria simulada (0-24, avanza con _GlobalNightLevel) ──────
    float _horaSimulada = 8f;

    static readonly int ID_NightLevel = Shader.PropertyToID("_GlobalNightLevel");

    // ════════════════════════════════════════════════════════════════════════
    //  BOOT
    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _waypointsRoot = new GameObject("Trafico_Waypoints").transform;
        _waypointsRoot.SetParent(transform);
    }

    IEnumerator Start()
    {
        yield return null;   // esperar un frame a que el terreno esté listo

        CargarVias();
        if (_vias.Count == 0) { AlsasuaLogger.Warn("Trafico", "Sin vías vehiculares"); yield break; }

        ConstruirInterseccionesYSemaforos();
        InicializarPool();
        StartCoroutine(BucleSpawn());

        DirectorMundo.OnEvento += ReaccionarDirector;
        AlsasuaLogger.Info("Trafico", $"{_vias.Count} vías | {_semaforos.Count} semáforos | pool={vehiculosMax}");
    }

    void OnDestroy() => DirectorMundo.OnEvento -= ReaccionarDirector;

    // ════════════════════════════════════════════════════════════════════════
    //  CARGA DE VÍAS
    // ════════════════════════════════════════════════════════════════════════

    void CargarVias()
    {
        var asset = Resources.Load<TextAsset>(archivoRoads);
        if (asset == null)
        {
            // Intentar carga directa desde Application.dataPath
            string ruta = System.IO.Path.Combine(Application.dataPath, "AlsasuaData/roads_unity.json");
            if (!System.IO.File.Exists(ruta)) { AlsasuaLogger.Error("Trafico", "roads_unity.json no encontrado"); return; }
            asset = new TextAsset(System.IO.File.ReadAllText(ruta));
        }

        var raiz = JsonUtility.FromJson<RoadsWrapper>("{\"roads\":" + asset.text + "}");
        if (raiz?.roads == null) return;

        foreach (var road in raiz.roads)
        {
            // Solo vías vehiculares
            if (road.type != "tertiary" && road.type != "residential") continue;
            if (road.points == null || road.points.Length < 2)         continue;

            var via = new ViaVehicular
            {
                esSentidoUnico = road.oneway,
                nombre         = road.name ?? road.type,
                waypoints      = new List<Transform>()
            };

            for (int i = 0; i < road.points.Length; i++)
            {
                var go = new GameObject($"WP_{road.id}_{i}");
                go.transform.SetParent(_waypointsRoot);
                float ux = road.points[i].x + GeoDataAlsasua.OX;
                float uz = road.points[i].z + GeoDataAlsasua.OZ;
                // Altura: terreno real si disponible
                float uy = Terrain.activeTerrain != null
                    ? Terrain.activeTerrain.SampleHeight(new Vector3(ux, 0, uz))
                    : 0f;
                go.transform.position = new Vector3(ux, uy + 0.3f, uz);
                via.waypoints.Add(go.transform);
            }
            _vias.Add(via);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  INTERSECCIONES Y SEMÁFOROS
    // ════════════════════════════════════════════════════════════════════════

    void ConstruirInterseccionesYSemaforos()
    {
        // Recopilar todos los endpoints (primer y último wp de cada vía)
        var endpoints = new List<(Vector3 pos, int viaIdx, bool esInicio)>();
        for (int i = 0; i < _vias.Count; i++)
        {
            var wps = _vias[i].waypoints;
            endpoints.Add((wps[0].position,            i, true));
            endpoints.Add((wps[wps.Count - 1].position, i, false));
        }

        var interseccionesVistas = new HashSet<long>();

        for (int a = 0; a < endpoints.Count; a++)
        for (int b = a + 1; b < endpoints.Count; b++)
        {
            if (endpoints[a].viaIdx == endpoints[b].viaIdx) continue;
            if (Vector3.Distance(endpoints[a].pos, endpoints[b].pos) > distanciaInterseccion) continue;

            long key = (long)Mathf.Min(a, b) * 10000 + Mathf.Max(a, b);
            if (!interseccionesVistas.Add(key)) continue;

            // Crear semáforo en el punto medio
            Vector3 centro = (endpoints[a].pos + endpoints[b].pos) * 0.5f;
            CrearSemaforoPar(centro, endpoints[a].viaIdx, endpoints[b].viaIdx);
        }
    }

    void CrearSemaforoPar(Vector3 pos, int viaA, int viaB)
    {
        // Dirección de la vía A (para orientar el collider)
        Vector3 dirA = DireccionVia(_vias[viaA]);

        var goA = new GameObject($"Semaforo_V{viaA}");
        goA.transform.SetParent(transform);
        goA.transform.position = pos + dirA * 1.5f;
        goA.transform.forward  = dirA;
        var nodoA = goA.AddComponent<SemaforoNodo>();

        var goB = new GameObject($"Semaforo_V{viaB}");
        goB.transform.SetParent(transform);
        goB.transform.position = pos - dirA * 1.5f;
        goB.transform.forward  = -dirA;
        var nodoB = goB.AddComponent<SemaforoNodo>();

        // Antifase: A empieza en verde, B empieza en rojo
        nodoA.IniciarCiclo(0f);
        nodoB.IniciarCiclo(12f);   // offset = duracionVerde → empieza en rojo

        _semaforos.Add(nodoA);
        _semaforos.Add(nodoB);
    }

    static Vector3 DireccionVia(ViaVehicular via)
    {
        var wps = via.waypoints;
        if (wps.Count < 2) return Vector3.forward;
        return (wps[1].position - wps[0].position).normalized;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  POOL DE VEHÍCULOS
    // ════════════════════════════════════════════════════════════════════════

    void InicializarPool()
    {
        for (int i = 0; i < vehiculosMax; i++)
        {
            // Prioridad: 1) SerializeField, 2) SistemaAssets (Resources), 3) procedural
            GameObject prefab = prefabVehiculo;
            if (prefab == null)
            {
                var sa = SistemaAssets.Instance;
                if (sa != null && sa.ContarCoches() > 0) prefab = sa.CocheAleatorio();
            }
            var go = prefab != null
                ? Instantiate(prefab, Vector3.down * 200f, Quaternion.identity)
                : CrearVehiculoProcedural(i);
            go.transform.SetParent(transform);
            go.name = $"VehiculoTrafico_{i:00}";
            go.SetActive(false);

            var npc = go.GetComponent<VehiculoNPC>();
            if (npc == null) npc = go.AddComponent<VehiculoNPC>();
            _pool.Add(npc);
        }
    }

    static GameObject CrearVehiculoProcedural(int idx)
    {
        var go  = new GameObject("VNPCTrafico");
        var rb  = go.AddComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        // Cuerpo del coche: cápsula horizontal
        var cuerpo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cuerpo.transform.SetParent(go.transform);
        cuerpo.transform.localPosition = new Vector3(0, 0.45f, 0);
        cuerpo.transform.localScale    = new Vector3(1.8f, 0.9f, 4f);
        cuerpo.GetComponent<Collider>().enabled = false;
        go.AddComponent<BoxCollider>().size = new Vector3(1.8f, 1.2f, 4f);

        // Color aleatorio
        Color[] colores = { Color.red, Color.blue, Color.white, Color.gray,
                            new Color(0.1f,0.5f,0.1f), new Color(0.8f,0.6f,0f) };
        cuerpo.GetComponent<MeshRenderer>().material.color = colores[idx % colores.Length];
        return go;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  BUCLE DE SPAWN
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator BucleSpawn()
    {
        while (true)
        {
            yield return new WaitForSeconds(5f);
            if (!_traficoActivo) continue;

            ActualizarHoraSimulada();
            int objetivo = CalcularObjetivoDensidad();
            int activos  = _pool.Count(v => v.gameObject.activeSelf);

            if (activos < objetivo)
                SpawnVehiculo();
            else if (activos > objetivo)
                RetirarVehiculo();
        }
    }

    void ActualizarHoraSimulada()
    {
        float night = Shader.GetGlobalFloat(ID_NightLevel);
        // _GlobalNightLevel: 0 = mediodía, 1 = medianoche (aproximado)
        // Mapear a 0-24h: noche alta → hora ~0, día → hora ~12
        _horaSimulada = Mathf.Lerp(12f, 0f, night);
    }

    int CalcularObjetivoDensidad()
    {
        float h = _horaSimulada;
        // Hora punta
        bool horaPunta = (h >= 7 && h <= 9) || (h >= 13 && h <= 14) || (h >= 17 && h <= 19);
        bool noche     = h < 6 || h > 22;

        float factor = horaPunta ? 1f : noche ? 0.15f : 0.5f;
        return Mathf.RoundToInt(vehiculosMax * factor);
    }

    void SpawnVehiculo()
    {
        var libre = _pool.FirstOrDefault(v => !v.gameObject.activeSelf);
        if (libre == null) return;

        // Elegir vía aleatoria cercana al jugador
        var via = ElegirViaAleatoria();
        if (via.waypoints == null || via.waypoints.Count < 2) return;

        // Spawn en el primer waypoint
        Vector3 spawnPos = via.waypoints[0].position;
        libre.transform.position = spawnPos;
        libre.transform.forward  = DireccionVia(via);
        libre.gameObject.SetActive(true);

        // Ruta: si sentido único siempre hacia adelante, si no elegir al azar
        var ruta = via.esSentidoUnico ? via.waypoints
                   : (Random.value > 0.5f ? via.waypoints
                      : Enumerable.Reverse(via.waypoints).ToList());

        libre.AsignarRuta(new List<Transform>(ruta), bucle: true);
    }

    void RetirarVehiculo()
    {
        var activo = _pool.FirstOrDefault(v => v.gameObject.activeSelf);
        if (activo != null) activo.gameObject.SetActive(false);
    }

    ViaVehicular ElegirViaAleatoria()
    {
        if (_vias.Count == 0) return default;
        var jugador = AltsasuCore.Jugador;
        if (jugador == null) return _vias[Random.Range(0, _vias.Count)];

        // Filtrar vías dentro del radio
        var candidatas = _vias.Where(v =>
            v.waypoints.Count > 0 &&
            Vector3.Distance(v.waypoints[0].position, jugador.position) < radioSpawn).ToList();

        if (candidatas.Count == 0) return _vias[Random.Range(0, _vias.Count)];
        return candidatas[Random.Range(0, candidatas.Count)];
    }

    // ════════════════════════════════════════════════════════════════════════
    //  REACCIÓN A DIRECTORMUNDO
    // ════════════════════════════════════════════════════════════════════════

    void ReaccionarDirector(DirectorMundo.EventoMundo ev)
    {
        switch (ev)
        {
            case DirectorMundo.EventoMundo.ControlPolicial:
            case DirectorMundo.EventoMundo.Redada:
                // Retirar todos los vehículos de las vías principales
                _traficoActivo = false;
                foreach (var v in _pool)
                    if (v.gameObject.activeSelf) v.gameObject.SetActive(false);
                StartCoroutine(ReanudarTrasDelay(45f));
                break;

            case DirectorMundo.EventoMundo.Calma:
            case DirectorMundo.EventoMundo.MercadoDia:
                _traficoActivo = true;
                break;
        }
    }

    IEnumerator ReanudarTrasDelay(float segundos)
    {
        yield return new WaitForSeconds(segundos);
        _traficoActivo = true;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  JSON HELPERS
    // ════════════════════════════════════════════════════════════════════════

    [System.Serializable] class RoadsWrapper { public RoadData[] roads; }

    [System.Serializable]
    class RoadData
    {
        public long      id;
        public string    type;
        public string    name;
        public bool      oneway;
        public PointData[] points;
    }

    [System.Serializable]
    class PointData { public float x; public float z; }
}
