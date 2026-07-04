#pragma warning disable CS0414 // _inicializado: flag asignado, lectura pendiente
// ============================================================================
//  MobiliarioUrbano.cs — Mobiliario urbano UNIFICADO (fusión 2026-06 con
//  SistemaMobiliarioUrbano, ahora deprecado).
//
//  A) Street furniture procedural (roads_unity.json, 344 calles):
//     bordillos, farolas y señales con object pooling + LOD streaming
//     (instancia <200m, devuelve al pool >300m). GPU instancing.
//
//  B) Props por zonas (prefabs de Resources/Prefabs/Mobiliario/):
//     Herriko Plaza → bancos, hortensias, farolas PolyHaven
//     Nafarroa Kalea → toldos, bicicletas · Polígono Isasia → contenedores,
//     megáfonos · Ribera Arakil → mesas de picnic.
//
//  Quality tier (SistemaOptimizacion.TierCalidad) escala la densidad de A y B.
//  DirectorMundo.Disturbio → vuelca bicis/bancos cerca del centro.
//  Farolas con Light se registran en SistemaVidaNocturna.
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[DefaultExecutionOrder(-40)]
public class MobiliarioUrbano : MonoBehaviour
{
    // ── Singleton ──────────────────────────────────────────────────────
    public static MobiliarioUrbano Instance { get; private set; }

    // ── Prefabs (arrastrar en Inspector o cargar desde Resources) ─────
    [Header("Prefabs — Bordillos")]
    [SerializeField] GameObject[] prefabsBordillo;   // Stone_Curb_1..4

    [Header("Prefabs — Farolas")]
    [SerializeField] GameObject prefabFarola;        // Wall_Prop_Lamp o Lantern_01

    [Header("Prefabs — Señales")]
    [SerializeField] GameObject prefabSenal;         // Wall_Prop_Sign_1

    [Header("Prefabs — Suelo Casco Viejo")]
    [SerializeField] GameObject prefabAdoquin;       // Cobblestone_Floor_1

    [Header("Prefabs — Puente")]
    [SerializeField] GameObject prefabPuente;        // bridge_roads

    // ── Parámetros ────────────────────────────────────────────────────
    [Header("Colocación")]
    [SerializeField] float stepBordillo       = 4f;    // metros entre bordillos
    [SerializeField] float stepFarola         = 30f;   // metros entre farolas
    [SerializeField] float offsetBordillo     = 3f;    // distancia lateral al centro
    [SerializeField] float offsetFarola       = 3.5f;  // distancia lateral farola
    [SerializeField] int   minConexionesSenal = 3;     // nodos con >= N conexiones → señal

    [Header("LOD")]
    [SerializeField] float distanciaInstanciar = 200f;
    [SerializeField] float distanciaDestruir   = 300f;
    [SerializeField] float intervaloLOD        = 0.5f;  // segundos entre checks LOD

    [Header("Pooling")]
    [SerializeField] int poolBordilloSize  = 600;
    [SerializeField] int poolFarolaSize    = 200;
    [SerializeField] int poolSenalSize     = 80;

    [Header("Props por zonas (ex-SistemaMobiliarioUrbano)")]
    [Range(10, 120)]
    [SerializeField] int   maxPropsZonaUltra = 80;     // tope en tier 0
    [SerializeField] float alturaBaseProps   = 0.05f;  // offset sobre terreno

    // ── Coordenadas ───────────────────────────────────────────────────
    const float OFFSET_X = GeoDataAlsasua.OX;  // 1918
    const float OFFSET_Z = GeoDataAlsasua.OZ;  // 8570

    // ── Datos internos ────────────────────────────────────────────────

    // Descriptor ligero de un objeto a colocar (no instanciado aún)
    struct FurnitureSlot
    {
        public Vector3    posicion;
        public Quaternion rotacion;
        public TipoMobiliario tipo;
        public int        varianteIdx;  // para bordillos (0-3)
    }

    enum TipoMobiliario : byte { Bordillo, Farola, Senal }

    // Todos los slots precalculados
    readonly List<FurnitureSlot> _slots = new(8000);

    // Pool por tipo
    readonly Dictionary<TipoMobiliario, Queue<GameObject>> _pools = new();

    // Slots actualmente instanciados: slot index → GO
    readonly Dictionary<int, GameObject> _activos = new(2000);

    // Buffers reutilizables
    readonly List<int> _bufferActivar   = new(256);
    readonly List<int> _bufferDesactivar = new(256);

    // Contenedores en la jerarquía
    Transform _contenedorBordillos;
    Transform _contenedorFarolas;
    Transform _contenedorSenales;

    Transform _jugador;
    Terrain   _terrain;
    bool      _inicializado;

    // Props por zonas (estáticos, sin pooling: máx ~80 objetos)
    readonly List<GameObject> _propsZona = new();
    bool _volcadoRedada;

    // Zonas de colocación (coordenadas Unity, ancladas al origen real)
    static readonly Vector3 HERRIKO_PLAZA   = new(GeoDataAlsasua.OX,        0f, GeoDataAlsasua.OZ);
    static readonly Vector3 NAFARROA_KALEA  = new(GeoDataAlsasua.OX - 48f,  0f, GeoDataAlsasua.OZ + 20f);
    static readonly Vector3 POLIGONO_ISASIA = new(GeoDataAlsasua.OX + 432f, 0f, GeoDataAlsasua.OZ - 170f);
    static readonly Vector3 RIBERA_ARAKIL   = new(GeoDataAlsasua.OX,        0f, GeoDataAlsasua.OZ - 150f);

    // ── Ciclo de vida ─────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _contenedorBordillos = CrearContenedor("MobiliarioUrbano_Bordillos");
        _contenedorFarolas   = CrearContenedor("MobiliarioUrbano_Farolas");
        _contenedorSenales   = CrearContenedor("MobiliarioUrbano_Senales");
    }

    IEnumerator Start()
    {
        // Esperar terreno
        float t0 = Time.time;
        while (Terrain.activeTerrain == null && Time.time - t0 < 30f)
            yield return null;
        _terrain = Terrain.activeTerrain;

        // Esperar jugador
        t0 = Time.time;
        while (_jugador == null && Time.time - t0 < 15f)
        {
            var go = GameObject.FindWithTag("Player");
            if (go != null) _jugador = go.transform;
            else yield return new WaitForSeconds(0.5f);
        }

        // Quality tier → densidad del street furniture (steps más largos = menos objetos)
        float factorDensidad = FactorDensidadPorTier();
        stepBordillo /= factorDensidad;
        stepFarola   /= factorDensidad;

        // Cargar datos
        yield return StartCoroutine(CargarYProcesar());

        // Inicializar pools
        InicializarPools();

        _inicializado = true;
        AlsasuaLogger.Info("MobiliarioUrbano",
            $"Inicializado: {_slots.Count} slots ({CountTipo(TipoMobiliario.Bordillo)} bordillos, " +
            $"{CountTipo(TipoMobiliario.Farola)} farolas, {CountTipo(TipoMobiliario.Senal)} señales)");

        // Bucle LOD
        StartCoroutine(BucleLOD());

        // Props por zonas (espera a SistemaAssets en su propia corrutina)
        StartCoroutine(PoblarZonasCoroutine());
        DirectorMundo.OnEvento += ReaccionarDirector;
    }

    void OnDestroy()
    {
        DirectorMundo.OnEvento -= ReaccionarDirector;
        if (Instance == this) Instance = null;
        StopAllCoroutines();
    }

    // ── Carga de datos ────────────────────────────────────────────────

    IEnumerator CargarYProcesar()
    {
        string path = Path.Combine(Application.dataPath, "AlsasuaData", "roads_unity.json");
        if (!File.Exists(path))
        {
            AlsasuaLogger.Warn("MobiliarioUrbano", $"roads_unity.json no encontrado: {path}");
            yield break;
        }

        string json;
        try { json = File.ReadAllText(path); }
        catch (Exception ex)
        {
            AlsasuaLogger.Error("MobiliarioUrbano", $"Error leyendo roads_unity.json: {ex.Message}");
            yield break;
        }

        RoadData[] roads;
        try { roads = JsonHelper.ParseArray<RoadData>(json); }
        catch (Exception ex)
        {
            AlsasuaLogger.Error("MobiliarioUrbano", $"Error parseando roads: {ex.Message}");
            yield break;
        }

        if (roads == null || roads.Length == 0) yield break;

        // Contar conexiones por nodo para detectar intersecciones
        var conexiones = new Dictionary<Vector2Int, int>(roads.Length * 4);

        foreach (var road in roads)
        {
            if (road?.points == null || road.points.Length < 2) continue;
            foreach (var pt in road.points)
            {
                var key = new Vector2Int(Mathf.RoundToInt(pt.x), Mathf.RoundToInt(pt.z));
                conexiones.TryGetValue(key, out int c);
                conexiones[key] = c + 1;
            }
        }

        int roadIdx = 0;
        foreach (var road in roads)
        {
            if (road?.points == null || road.points.Length < 2) continue;

            // Convertir puntos a Unity world
            var pts = new Vector3[road.points.Length];
            for (int i = 0; i < road.points.Length; i++)
            {
                float wx = road.points[i].x + OFFSET_X;
                float wz = road.points[i].z + OFFSET_Z;
                float wy = AlturaTerreno(wx, wz);
                pts[i] = new Vector3(wx, wy, wz);
            }

            float halfWidth = (road.width > 0 ? road.width : 5f) * 0.5f;
            float offsetCurb = Mathf.Min(offsetBordillo, halfWidth + 1f);

            // ── Bordillos (ambos lados) ──
            GenerarBordillosEnSegmentos(pts, offsetCurb);

            // ── Farolas (alternando lados) ──
            GenerarFarolasEnSegmentos(pts, offsetFarola, roadIdx);

            // ── Señales en intersecciones ──
            foreach (var pt in road.points)
            {
                var key = new Vector2Int(Mathf.RoundToInt(pt.x), Mathf.RoundToInt(pt.z));
                if (conexiones.TryGetValue(key, out int c) && c >= minConexionesSenal)
                {
                    float wx = pt.x + OFFSET_X;
                    float wz = pt.z + OFFSET_Z;
                    float wy = AlturaTerreno(wx, wz);

                    // Evitar duplicados: marcar como procesado
                    conexiones[key] = -999;

                    _slots.Add(new FurnitureSlot
                    {
                        posicion = new Vector3(wx + 2f, wy, wz + 2f),
                        rotacion = Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0),
                        tipo = TipoMobiliario.Senal,
                        varianteIdx = 0
                    });
                }
            }

            roadIdx++;

            // Ceder cada 20 calles
            if (roadIdx % 20 == 0) yield return null;
        }
    }

    void GenerarBordillosEnSegmentos(Vector3[] pts, float offset)
    {
        float acum = 0f;
        int variante = 0;

        for (int i = 0; i < pts.Length - 1; i++)
        {
            Vector3 a = pts[i], b = pts[i + 1];
            Vector3 dir = b - a;
            float segLen = new Vector2(dir.x, dir.z).magnitude;
            if (segLen < 0.1f) continue;

            Vector3 fwd = dir / segLen;
            // Perpendicular (en plano XZ)
            Vector3 right = new Vector3(fwd.z, 0, -fwd.x);
            Quaternion rot = Quaternion.LookRotation(new Vector3(fwd.x, 0, fwd.z), Vector3.up);

            while (acum < segLen)
            {
                float t = acum / segLen;
                Vector3 centro = Vector3.Lerp(a, b, t);
                centro.y = AlturaTerreno(centro.x, centro.z);

                // Lado derecho
                Vector3 posDer = centro + right * offset;
                posDer.y = AlturaTerreno(posDer.x, posDer.z);
                _slots.Add(new FurnitureSlot
                {
                    posicion = posDer, rotacion = rot,
                    tipo = TipoMobiliario.Bordillo, varianteIdx = variante % 4
                });

                // Lado izquierdo
                Vector3 posIzq = centro - right * offset;
                posIzq.y = AlturaTerreno(posIzq.x, posIzq.z);
                _slots.Add(new FurnitureSlot
                {
                    posicion = posIzq, rotacion = rot * Quaternion.Euler(0, 180, 0),
                    tipo = TipoMobiliario.Bordillo, varianteIdx = variante % 4
                });

                variante++;
                acum += stepBordillo;
            }
            acum -= segLen;
        }
    }

    void GenerarFarolasEnSegmentos(Vector3[] pts, float offset, int roadIdx)
    {
        float acum = 0f;
        bool ladoDerecho = (roadIdx % 2 == 0); // alternar inicio por calle

        for (int i = 0; i < pts.Length - 1; i++)
        {
            Vector3 a = pts[i], b = pts[i + 1];
            Vector3 dir = b - a;
            float segLen = new Vector2(dir.x, dir.z).magnitude;
            if (segLen < 0.1f) continue;

            Vector3 fwd = dir / segLen;
            Vector3 right = new Vector3(fwd.z, 0, -fwd.x);

            while (acum < segLen)
            {
                float t = acum / segLen;
                Vector3 centro = Vector3.Lerp(a, b, t);

                Vector3 pos = centro + right * (ladoDerecho ? offset : -offset);
                pos.y = AlturaTerreno(pos.x, pos.z);

                Quaternion rot = Quaternion.LookRotation(
                    ladoDerecho ? -right : right, Vector3.up);

                _slots.Add(new FurnitureSlot
                {
                    posicion = pos, rotacion = rot,
                    tipo = TipoMobiliario.Farola, varianteIdx = 0
                });

                ladoDerecho = !ladoDerecho;
                acum += stepFarola;
            }
            acum -= segLen;
        }
    }

    // ── Object Pooling ────────────────────────────────────────────────

    void InicializarPools()
    {
        _pools[TipoMobiliario.Bordillo] = CrearPool(
            prefabsBordillo != null && prefabsBordillo.Length > 0 ? prefabsBordillo[0] : null,
            poolBordilloSize, _contenedorBordillos);

        _pools[TipoMobiliario.Farola] = CrearPool(
            prefabFarola, poolFarolaSize, _contenedorFarolas);

        _pools[TipoMobiliario.Senal] = CrearPool(
            prefabSenal, poolSenalSize, _contenedorSenales);
    }

    Queue<GameObject> CrearPool(GameObject prefab, int count, Transform parent)
    {
        var queue = new Queue<GameObject>(count);
        if (prefab == null) return queue;

        for (int i = 0; i < count; i++)
        {
            var go = Instantiate(prefab, parent);
            ConfigurarGPUInstancing(go);
            go.SetActive(false);
            queue.Enqueue(go);
        }
        return queue;
    }

    GameObject ObtenerDePool(TipoMobiliario tipo, int varianteIdx)
    {
        // Para bordillos con variantes
        if (tipo == TipoMobiliario.Bordillo && prefabsBordillo != null
            && prefabsBordillo.Length > varianteIdx && prefabsBordillo[varianteIdx] != null)
        {
            if (_pools[tipo].Count > 0)
            {
                var go = _pools[tipo].Dequeue();
                go.SetActive(true);
                return go;
            }
            // Pool agotado: crear nuevo
            var nuevo = Instantiate(prefabsBordillo[varianteIdx], _contenedorBordillos);
            ConfigurarGPUInstancing(nuevo);
            return nuevo;
        }

        GameObject prefab = tipo switch
        {
            TipoMobiliario.Farola => prefabFarola,
            TipoMobiliario.Senal  => prefabSenal,
            _                     => prefabsBordillo != null && prefabsBordillo.Length > 0
                                        ? prefabsBordillo[0] : null
        };

        Transform parent = tipo switch
        {
            TipoMobiliario.Bordillo => _contenedorBordillos,
            TipoMobiliario.Farola   => _contenedorFarolas,
            TipoMobiliario.Senal    => _contenedorSenales,
            _                       => transform
        };

        if (_pools[tipo].Count > 0)
        {
            var go = _pools[tipo].Dequeue();
            go.SetActive(true);
            return go;
        }

        if (prefab == null) return null;
        var inst = Instantiate(prefab, parent);
        ConfigurarGPUInstancing(inst);
        return inst;
    }

    void DevolverAPool(TipoMobiliario tipo, GameObject go)
    {
        if (go == null) return;
        go.SetActive(false);
        _pools[tipo].Enqueue(go);
    }

    // ── LOD streaming ─────────────────────────────────────────────────

    IEnumerator BucleLOD()
    {
        var wait = new WaitForSeconds(intervaloLOD);

        while (true)
        {
            yield return wait;
            if (_jugador == null)
            {
                var go = GameObject.FindWithTag("Player");
                if (go != null) _jugador = go.transform;
                continue;
            }

            ActualizarLOD();
        }
    }

    void ActualizarLOD()
    {
        Vector3 jugadorPos = _jugador.position;
        float distInst2 = distanciaInstanciar * distanciaInstanciar;
        float distDest2 = distanciaDestruir * distanciaDestruir;

        _bufferActivar.Clear();
        _bufferDesactivar.Clear();

        // Revisar activos → ¿demasiado lejos?
        // Iterar sobre copia de keys para evitar modificar durante iteración
        foreach (var kvp in _activos)
        {
            int idx = kvp.Key;
            float d2 = DistanciaSqXZ(jugadorPos, _slots[idx].posicion);
            if (d2 > distDest2)
                _bufferDesactivar.Add(idx);
        }

        // Desactivar
        for (int i = 0; i < _bufferDesactivar.Count; i++)
        {
            int idx = _bufferDesactivar[i];
            if (_activos.TryGetValue(idx, out var go))
            {
                DevolverAPool(_slots[idx].tipo, go);
                _activos.Remove(idx);
            }
        }

        // Buscar nuevos slots cercanos (muestrear un rango — no iterar todos cada frame)
        // Dividimos la búsqueda en chunks temporales
        int count = _slots.Count;
        if (count == 0) return;   // FIX: sin slots (json de calles ausente/vacío) → `% count` lanza DivideByZeroException entero cada tick del LOD
        int batchSize = Mathf.Min(count, 2000);
        int offset = (Time.frameCount * batchSize) % count;

        for (int b = 0; b < batchSize; b++)
        {
            int idx = (offset + b) % count;
            if (_activos.ContainsKey(idx)) continue;

            float d2 = DistanciaSqXZ(jugadorPos, _slots[idx].posicion);
            if (d2 < distInst2)
                _bufferActivar.Add(idx);
        }

        // Activar (limitar por frame para evitar hitches)
        int maxActivar = 50;
        for (int i = 0; i < _bufferActivar.Count && i < maxActivar; i++)
        {
            int idx = _bufferActivar[i];
            if (_activos.ContainsKey(idx)) continue;

            var slot = _slots[idx];
            var go = ObtenerDePool(slot.tipo, slot.varianteIdx);
            if (go == null) continue;

            go.transform.SetPositionAndRotation(slot.posicion, slot.rotacion);
            _activos[idx] = go;
        }
    }

    // ── Utilidades ────────────────────────────────────────────────────

    float AlturaTerreno(float x, float z)
    {
        if (_terrain != null)
            return _terrain.SampleHeight(new Vector3(x, 0, z));
        return 0f;
    }

    static float DistanciaSqXZ(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    Transform CrearContenedor(string nombre)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(transform);
        return go.transform;
    }

    static void ConfigurarGPUInstancing(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var mats = renderers[i].sharedMaterials;
            for (int m = 0; m < mats.Length; m++)
            {
                if (mats[m] != null)
                    mats[m].enableInstancing = true;
            }
        }

        // Farolas/lámparas: registrar luces para el ciclo día/noche
        var luces = go.GetComponentsInChildren<Light>(true);
        for (int i = 0; i < luces.Length; i++)
            SistemaVidaNocturna.Instance?.RegistrarFarola(luces[i]);
    }

    static float FactorDensidadPorTier() => SistemaOptimizacion.TierCalidad switch
    {
        0 or 1 => 1f,
        2      => 0.5f,
        _      => 0.25f,
    };

    int CountTipo(TipoMobiliario tipo)
    {
        int c = 0;
        for (int i = 0; i < _slots.Count; i++)
            if (_slots[i].tipo == tipo) c++;
        return c;
    }

    // ════════════════════════════════════════════════════════════════════
    //  PROPS POR ZONAS (ex-SistemaMobiliarioUrbano) — estáticos, sin pooling
    // ════════════════════════════════════════════════════════════════════

    IEnumerator PoblarZonasCoroutine()
    {
        // Esperar a SistemaAssets (antes era un WaitForSeconds(20) ciego)
        float t0 = Time.time;
        while (SistemaAssets.Instance == null && Time.time - t0 < 30f)
            yield return new WaitForSeconds(0.5f);

        var assets = SistemaAssets.Instance;
        if (assets == null) yield break;

        int max = MaxPropsPorTier();
        int colocados = 0;

        // ── Herriko Plaza ─────────────────────────────────────────────────
        int plazaMax = max / 4;
        for (int i = 0; i < plazaMax / 3; i++)                       // bancos (anillo 30-60m)
            ColocarProp("Bench", assets.PropUrbanoAleatorio() ?? PrefabMobiliario("Bench"),
                PuntoEnAnillo(HERRIKO_PLAZA, 30f, 60f));
        for (int i = 0; i < plazaMax / 4; i++)                       // hortensias
            ColocarProp("Hortensia", PrefabMobiliario("Hydrangea"),
                PuntoEnAnillo(HERRIKO_PLAZA, 15f, 45f));
        int nFarolasPlaza = Mathf.Max(1, plazaMax / 3);              // farolas en círculo
        for (int i = 0; i < nFarolasPlaza; i++)
        {
            float angulo = i * (360f / nFarolasPlaza) * Mathf.Deg2Rad;
            Vector3 pos = HERRIKO_PLAZA + new Vector3(Mathf.Sin(angulo) * 50f, 0f, Mathf.Cos(angulo) * 50f);
            ColocarProp("Farola_Plaza", PrefabMobiliario("Lantern"), PosEnTerreno(pos));
        }
        colocados += plazaMax;
        yield return null;

        // ── Nafarroa Kalea — toldos y bicicletas ─────────────────────────
        int kalMax = max / 5;
        for (int i = 0; i < kalMax; i++)
        {
            Vector3 pos = NAFARROA_KALEA + new Vector3(UnityEngine.Random.Range(-80f, 80f), 0f, UnityEngine.Random.Range(-20f, 20f));
            string tipo = i % 3 == 0 ? "Awning" : i % 3 == 1 ? "Post lamp" : "Bicicletas";
            ColocarProp(tipo, PrefabMobiliario(tipo), PosEnTerreno(pos));
        }
        colocados += kalMax;
        yield return null;

        // ── Polígono Isasia — contenedores e industrial ───────────────────
        int polMax = max / 6;
        for (int i = 0; i < polMax; i++)
        {
            Vector3 pos = POLIGONO_ISASIA + new Vector3(UnityEngine.Random.Range(-120f, 120f), 0f, UnityEngine.Random.Range(-60f, 60f));
            string tipo = i % 2 == 0 ? "Container" : "Megaphone";
            ColocarProp(tipo, PrefabMobiliario(tipo), PosEnTerreno(pos));
        }
        colocados += polMax;
        yield return null;

        // ── Ribera del Arakil — mesas de picnic ──────────────────────────
        int ribMax = max / 5;
        for (int i = 0; i < ribMax; i++)
        {
            Vector3 pos = RIBERA_ARAKIL + new Vector3(UnityEngine.Random.Range(-200f, 200f), 0f, UnityEngine.Random.Range(-20f, 20f));
            ColocarProp("PicnicTable", PrefabMobiliario("PicnicTable"), PosEnTerreno(pos));
        }
        colocados += ribMax;
        yield return null;

        // ── Farolas extra en el eje principal ─────────────────────────────
        int farolaMax = Mathf.Max(0, max - colocados);
        for (int i = 0; i < farolaMax; i++)
        {
            float t = (float)i / Mathf.Max(1, farolaMax);
            float x = Mathf.Lerp(HERRIKO_PLAZA.x - 300f, HERRIKO_PLAZA.x + 300f, t);
            Vector3 pos = PosEnTerreno(new Vector3(x, 0f, HERRIKO_PLAZA.z + UnityEngine.Random.Range(-5f, 5f)));
            ColocarProp("FarolaCalle", PrefabMobiliario("lamp"), pos);
        }

        AlsasuaLogger.Info("MobiliarioUrbano", $"{_propsZona.Count} props de zona colocados (tier {SistemaOptimizacion.TierCalidad})");
    }

    void ColocarProp(string tag, GameObject prefab, Vector3 pos)
    {
        if (prefab == null || pos == Vector3.zero) return;

        // Validar que la posición sea suelo transitable antes de instanciar.
        // Raycast desde arriba: si no hay suelo, o el ángulo de la superficie es
        // >40° (pared, rampa) → descartar. Evita bicis y farolas dentro de edificios.
        const float RAY_ORIGEN = 20f;
        const float RAY_DIST   = 40f;
        var maskSuelo = ~LayerMask.GetMask("Player", "NPC", "Manifestante",
                                            "Ignore Raycast", "Water");
        if (Physics.Raycast(new Vector3(pos.x, pos.y + RAY_ORIGEN, pos.z),
                            Vector3.down, out var hit, RAY_DIST, maskSuelo))
        {
            // Superficie muy inclinada (pared o tejado) → saltar
            if (Vector3.Angle(hit.normal, Vector3.up) > 40f) return;
            // Ajustar Y exactamente al suelo detectado
            pos.y = hit.point.y + alturaBaseProps;
        }
        else
        {
            return; // sin suelo detectado → no colocar
        }

        var go = Instantiate(prefab, pos,
            Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f), transform);
        go.name = $"Mobiliario_{tag}_{_propsZona.Count}";
        go.isStatic = true;
        ConfigurarGPUInstancing(go);
        _propsZona.Add(go);
    }

    Vector3 PosEnTerreno(Vector3 xz)
    {
        xz.y = AlturaTerreno(xz.x, xz.z) + alturaBaseProps;
        return xz;
    }

    Vector3 PuntoEnAnillo(Vector3 centro, float rMin, float rMax)
    {
        float angulo = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        float radio  = UnityEngine.Random.Range(rMin, rMax);
        var pos = centro + new Vector3(Mathf.Sin(angulo) * radio, 0f, Mathf.Cos(angulo) * radio);
        pos.y = AlturaTerreno(pos.x, pos.z) + alturaBaseProps;
        return pos;
    }

    static GameObject PrefabMobiliario(string keyword)
    {
        var todos = Resources.LoadAll<GameObject>("Prefabs/Mobiliario");
        keyword = keyword.ToLower();
        foreach (var p in todos)
            if (p.name.ToLower().Contains(keyword)) return p;
        return todos.Length > 0 ? todos[UnityEngine.Random.Range(0, todos.Length)] : null;
    }

    int MaxPropsPorTier() => SistemaOptimizacion.TierCalidad switch
    {
        0 => maxPropsZonaUltra,
        1 => Mathf.RoundToInt(maxPropsZonaUltra * 0.7f),
        2 => Mathf.RoundToInt(maxPropsZonaUltra * 0.4f),
        _ => Mathf.Max(5, Mathf.RoundToInt(maxPropsZonaUltra * 0.1f)),
    };

    // ── Reacción al DirectorMundo ─────────────────────────────────────────

    void ReaccionarDirector(DirectorMundo.EventoMundo ev)
    {
        if (ev == DirectorMundo.EventoMundo.Disturbio && !_volcadoRedada)
        {
            _volcadoRedada = true;
            VolcarPropsCercanos();
        }
        else if (ev == DirectorMundo.EventoMundo.Calma)
        {
            _volcadoRedada = false;
        }
    }

    void VolcarPropsCercanos()
    {
        // Bicicletas y bancos cercanos al centro se "vuelcan" (disturbio)
        foreach (var go in _propsZona)
        {
            if (go == null) continue;
            string n = go.name.ToLower();
            if (!n.Contains("bicicl") && !n.Contains("bench")) continue;
            if (Vector3.Distance(go.transform.position, HERRIKO_PLAZA) > 150f) continue;

            go.isStatic = false;
            go.transform.rotation = Quaternion.Euler(
                UnityEngine.Random.Range(60f, 120f),
                UnityEngine.Random.Range(0f, 360f),
                UnityEngine.Random.Range(-30f, 30f));
            go.isStatic = true;
        }
        AlsasuaLogger.Info("MobiliarioUrbano", "Props volcados por disturbio");
    }

    // ── API pública ───────────────────────────────────────────────────

    /// <summary>Número total de slots precalculados.</summary>
    public int TotalSlots => _slots.Count;

    /// <summary>Número de objetos actualmente instanciados.</summary>
    public int ObjetosActivos => _activos.Count;

    /// <summary>Props estáticos de zona colocados (plaza, kalea, polígono, ribera).</summary>
    public int PropsZona => _propsZona.Count;

    /// <summary>Fuerza la recarga de mobiliario.</summary>
    public void Recargar()
    {
        // Devolver todo al pool
        foreach (var kvp in _activos)
            DevolverAPool(_slots[kvp.Key].tipo, kvp.Value);
        _activos.Clear();
        _slots.Clear();
        _inicializado = false;
        StartCoroutine(RecargarCoroutine());
    }

    IEnumerator RecargarCoroutine()
    {
        yield return StartCoroutine(CargarYProcesar());
        _inicializado = true;
        AlsasuaLogger.Info("MobiliarioUrbano", $"Recargado: {_slots.Count} slots");
    }
}
