// Assets/Scripts/Runtime/StreamerMundoEstatico.cs
// ═══════════════════════════════════════════════════════════════════════════
//  STREAMER DEL MUNDO ESTÁTICO — activación por distancia de TODO el mundo estático
//
//  EL HUECO QUE CIERRA: el jugador entraba a un mundo 100% activo. El único cull era
//  el de SistemaOptimizacion: todo-o-nada a 600 m fijos, reactivo solo al FPS. No había
//  anillo intermedio ni radio gobernado por la GPU.
//
//  POR QUÉ ESTA REESCRITURA (2026-06-15): la versión anterior descubría el mundo por una
//  WHITELIST de 6 nombres de raíz (Edificios_OSM…). En el mundo real la inmensa mayoría
//  de los renderers (medidos: ~77.000 a 0.6 FPS) viven en OTROS contenedores —calles,
//  suelo AAA, malla OBJ por chunks, detalle OSM, hijos modulares de edificios— que la
//  whitelist no miraba. El gobernador de GPU encogía el radio a 90 m pero el streamer
//  solo gestionaba 41 objetos → no rescataba los FPS. Ahora descubre por ESCANEO DE
//  ESCENA con DENYLIST: gestiona TODA raíz salvo lo que llevan otros (terreno, árboles,
//  multitud BRG) o lo que nunca debe ocultarse (jugador, cámara, Cesium, luces, UI).
//
//  CÓMO OCULTA — a nivel de RENDERER, nunca SetActive del GameObject:
//    · Banda lejana: Renderer.forceRenderingOff = true  → mata el draw call (el cuello
//      real de GPU) pero DEJA VIVOS colliders, NavMesh y lógica → imposible "caerse del
//      mundo" si por error gestiona una calle o la malla del suelo.
//  Tres bandas (job Burst con histéresis, sobre el centro de bounds cacheado):
//    · Activo   (< RadioActivacion)             → render ON,  sombras originales, LOD auto
//    · Impostor (RadioActivacion..RadioImpostor) → render ON,  sombras OFF,  LOD mínimo
//    · Oculto   (> RadioImpostor)               → render OFF (forceRenderingOff)
//
//  Qué gestiona y qué NO:
//    · SÍ: cualquier raíz de escena con renderers que no esté en la denylist.
//    · NO árboles  → ya los streamea AlsasuaTreeStreamer (radio + LOD0-5 propios).
//    · NO multitud → es BRG (1 draw call) y la CPU-LOD la lleva el orquestador.
//    · NO terreno  → lo gobierna CargadorMosaicoTerreno; ocultarlo = caída libre.
//
//  Coste acotado: descubrimiento REPARTIDO por presupuesto (el GetComponentsInChildren
//  caro se drena de una cola con tope de ms); clasificación en Burst sobre centros
//  estáticos; aplicación REPARTIDA por frames. Re-escanea raíces cada pocos segundos
//  para recoger lo que el mundo va poblando tras el baseline.
//
//  Capa RUNTIME → consume Core (IRenderBudgetGovernor, AltsasuCore, jobs). No referencia
//  tipos de Systems: descubre el mundo por escena + componentes/nombres genéricos.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[DefaultExecutionOrder(-90)]   // tras SistemaOptimizacion (-95): leemos su mismo mundo
public sealed class StreamerMundoEstatico : MonoBehaviour
{
    // ── DENYLIST por nombre (substring, case-insensitive): raíces que NUNCA gestionamos ──
    //    Lo llevan otros sistemas o no debe ocultarse jamás.
    static readonly string[] DENY_NOMBRE = {
        "terrain", "terreno",                         // CargadorMosaicoTerreno
        "arbol", "arboles", "vegetacion", "tree",     // AlsasuaTreeStreamer
        "multitud", "crowd", "manifest",              // BRG (1 draw call)
        "cesium", "georeference",                      // fondo lejano propio
        "player", "jugador", "camera", "cámara", "camara",
        "light", "luz", "sun", "sol",                 // iluminación
        "canvas", "hud", "eventsystem", "audio",      // UI/Audio
        "streamer", "sistema", "manager", "director", // managers (sin renderers de mundo)
        "diagnostic", "diagnostico", "navmesh",
        "pantallacarga", "loading", "bootstrap",
    };

    const float INTERVALO_CLASIF  = 0.33f;  // s entre clasificaciones (no cada frame)
    const float INTERVALO_RESCAN  = 3f;     // s entre re-escaneos de raíces (mundo poblándose)
    const float MS_PRESUPUESTO    = 2f;     // ms/frame para aplicar cambios de estado
    const float MS_REGISTRO       = 1.5f;   // ms/frame para registrar unidades nuevas
    const float RADIO_MIN_SEGURO  = 60f;    // nunca ocultamos más cerca que esto (anti-pop brusco)

    // ── Una entrada por unidad estática gestionada ──
    sealed class Entrada
    {
        public GameObject go;
        public Renderer[] renderers;             // para alternar render/sombras
        public ShadowCastingMode[] sombraOrig;   // sombra original (restaurar en Activo)
        public LODGroup   lod;                    // si lo tiene: forzamos el LOD más bajo en impostor
        public float3     centro;                 // centro de bounds (estático) para banding
        public byte       estado;                 // 0=Activo 1=Impostor 2=Oculto
        public long       osmId;                 // sufijo numérico del nombre → clave en ImpostorAtlasSO; 0 si no aplica
        public ImpostorBillboard impostor;       // billboard activo (solo en banda 1 con atlas disponible)
    }

    readonly List<Entrada> _ent = new(4096);
    readonly HashSet<int>  _vistos     = new(8192);  // instanceIDs de unidades ya registradas
    readonly HashSet<int>  _raizDeny   = new(256);   // raíces descartadas (no reevaluar)
    readonly Dictionary<int, int> _conteoRaiz = new(256); // childCount visto por raíz gestionada
    readonly Queue<Transform> _pendientes = new(4096);    // unidades por registrar (drenaje budget)

    Transform _jugador;
    IRenderBudgetGovernor _gob;
    float _tClasif, _tRescan;
    bool  _aplicando, _registrando;

    // Buffers nativos reutilizados (se reasignan solo si crece el nº de entradas).
    NativeArray<float3> _pos;
    NativeArray<byte>   _estadoIn, _estadoOut;
    int _cap;

    // ── Auto-arranque: no hace falta ponerlo en escena ──
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<StreamerMundoEstatico>() != null) return;
        var go = new GameObject("StreamerMundoEstatico");
        DontDestroyOnLoad(go);
        go.AddComponent<StreamerMundoEstatico>();
    }

    void Start()
    {
        AltsasuCore.OnJugadorSpawned += OnJugador;
        if (AltsasuCore.Jugador != null) _jugador = AltsasuCore.Jugador;
        _gob = ServiceLocator.Get<IRenderBudgetGovernor>();
    }

    void OnDestroy()
    {
        AltsasuCore.OnJugadorSpawned -= OnJugador;
        if (_pos.IsCreated)       _pos.Dispose();
        if (_estadoIn.IsCreated)  _estadoIn.Dispose();
        if (_estadoOut.IsCreated) _estadoOut.Dispose();
    }

    void OnJugador(Transform t) => _jugador = t;

    void Update()
    {
        // El gobernador puede registrarse después que nosotros (orden de boot): reintenta.
        if (_gob == null) _gob = ServiceLocator.Get<IRenderBudgetGovernor>();

        _tRescan -= Time.unscaledDeltaTime;
        if (_tRescan <= 0f) { _tRescan = INTERVALO_RESCAN; Rescanear(); }

        // Drenar la cola de registro con presupuesto (descubrimiento caro repartido).
        if (!_registrando && _pendientes.Count > 0) StartCoroutine(DrenarRegistro());

        if (_jugador == null) { _jugador = AltsasuCore.Jugador; return; }

        if (_aplicando) return;   // no solapar pases de aplicación
        _tClasif -= Time.unscaledDeltaTime;
        if (_tClasif <= 0f) { _tClasif = INTERVALO_CLASIF; StartCoroutine(ClasificarYAplicar()); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  DESCUBRIMIENTO — escaneo de escena + denylist; encola unidades nuevas
    // ─────────────────────────────────────────────────────────────────────────
    void Rescanear()
    {
        var raices = SceneManager.GetActiveScene().GetRootGameObjects();
        for (int r = 0; r < raices.Length; r++)
        {
            var raiz = raices[r];
            if (raiz == null) continue;
            int id = raiz.GetInstanceID();
            if (_raizDeny.Contains(id)) continue;

            // ¿Raíz nueva? Decidir si la gestionamos.
            if (!_conteoRaiz.ContainsKey(id))
            {
                if (RaizDenegada(raiz)) { _raizDeny.Add(id); continue; }
                _conteoRaiz[id] = -1;   // fuerza primer barrido
                // Si la raíz tiene renderers propios (objeto único sin hijos), es una unidad.
                if (TieneRendererPropio(raiz)) EncolarUnidad(raiz.transform);
            }

            // Solo recorremos los hijos si el conteo cambió (barato en estado estable).
            var tr = raiz.transform;
            int prev = _conteoRaiz[id];
            if (tr.childCount == prev) continue;
            _conteoRaiz[id] = tr.childCount;

            foreach (Transform hijo in tr)
                if (hijo != null) EncolarUnidad(hijo);
        }
    }

    void EncolarUnidad(Transform t)
    {
        if (t == null) return;
        if (!_vistos.Add(t.GetInstanceID())) return;   // ya registrada o en cola
        _pendientes.Enqueue(t);
    }

    // ── ¿Esta raíz NO debe gestionarse? (denylist por nombre + componentes) ──
    bool RaizDenegada(GameObject raiz)
    {
        string n = raiz.name.ToLowerInvariant();
        for (int i = 0; i < DENY_NOMBRE.Length; i++)
            if (n.Contains(DENY_NOMBRE[i])) return true;

        // Componentes que marcan subárboles intocables (red de seguridad ante renombrados).
        // GetComponentInChildren (singular) corta en el primero → barato aunque la raíz sea enorme.
        if (raiz.GetComponentInChildren<Terrain>(true) != null) return true;
        if (raiz.GetComponentInChildren<Camera>(true)  != null) return true;
        if (raiz.GetComponentInChildren<Canvas>(true)  != null) return true;
        // Cesium: solo en la propia raíz (CesiumGeoreference vive ahí) → evita recorrer un
        // subárbol de decenas de miles de hijos. El nombre ya cubre el caso habitual.
        foreach (var mb in raiz.GetComponents<MonoBehaviour>())
            if (mb != null && mb.GetType().Name.StartsWith("Cesium")) return true;

        // Sin renderers = manager/lógica → nada que gestionar (singular: corta pronto).
        if (raiz.GetComponentInChildren<Renderer>(true) == null) return true;

        return false;
    }

    static bool TieneRendererPropio(GameObject go)
    {
        var rs = go.GetComponents<Renderer>();
        return rs != null && rs.Length > 0;
    }

    // ── Drenaje de la cola de registro con presupuesto de ms (GetComponents es caro) ──
    IEnumerator DrenarRegistro()
    {
        _registrando = true;
        float t0 = Time.realtimeSinceStartup;
        bool huboAltas = false;

        while (_pendientes.Count > 0)
        {
            var tr = _pendientes.Dequeue();
            if (tr == null) continue;

            var renderers = tr.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) continue;   // unidad sin geometría → ignorar

            var sombraOrig = new ShadowCastingMode[renderers.Length];
            var b = new Bounds(tr.position, Vector3.zero);
            bool primero = true;
            for (int i = 0; i < renderers.Length; i++)
            {
                var rd = renderers[i];
                sombraOrig[i] = rd != null ? rd.shadowCastingMode : ShadowCastingMode.On;
                // bounds de un renderer inactivo es basura (origen) → solo cuenta los activos.
                if (rd == null || !rd.gameObject.activeInHierarchy) continue;
                if (primero) { b = rd.bounds; primero = false; }
                else b.Encapsulate(rd.bounds);
            }

            _ent.Add(new Entrada
            {
                go         = tr.gameObject,
                renderers  = renderers,
                sombraOrig = sombraOrig,
                lod        = tr.GetComponentInChildren<LODGroup>(true),
                centro     = new float3(b.center.x, b.center.y, b.center.z),
                estado     = 0,   // empieza Activo; la 1ª clasificación lo corrige
                osmId      = ExtraerOsmId(tr.name),
            });
            huboAltas = true;

            if ((Time.realtimeSinceStartup - t0) * 1000f >= MS_REGISTRO)
            {
                yield return null;
                t0 = Time.realtimeSinceStartup;
            }
        }

        _registrando = false;

        // Recién registrado: clasifica ya para no dejar el mundo entero encendido.
        if (huboAltas && _jugador != null && !_aplicando)
            StartCoroutine(ClasificarYAplicar());
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CLASIFICACIÓN (Burst) + APLICACIÓN (repartida por presupuesto)
    // ─────────────────────────────────────────────────────────────────────────
    IEnumerator ClasificarYAplicar()
    {
        if (_aplicando) yield break;
        int n = _ent.Count;
        if (n == 0 || _jugador == null) yield break;

        _aplicando = true;

        // Radios del gobernador (con suelo de seguridad). Si no hay gobernador aún,
        // alcance amplio fijo para no ocultar el mundo por error.
        float rAct = _gob != null ? Mathf.Max(RADIO_MIN_SEGURO, _gob.RadioActivacion) : 400f;
        float rImp = _gob != null ? Mathf.Max(rAct + 50f, _gob.RadioImpostor)        : 700f;

        AsegurarBuffers(n);

        Vector3 pj = _jugador.position;
        for (int i = 0; i < n; i++)
        {
            _pos[i]      = _ent[i].centro;   // centro estático cacheado
            _estadoIn[i] = _ent[i].estado;
        }

        var job = new JobBandasMundo
        {
            posiciones    = _pos,
            estadoActual  = _estadoIn,
            posJugador    = new float3(pj.x, pj.y, pj.z),
            radioActivar  = rAct,
            radioImpostor = rImp,
            histeresis    = 12f,
            estadoNuevo   = _estadoOut,
        };
        job.Schedule(n, 64).Complete();

        // Aplicar solo los cambios, repartido por presupuesto de ms.
        float t0 = Time.realtimeSinceStartup;
        for (int i = 0; i < n; i++)
        {
            var e = _ent[i];
            if (e.go == null) continue;
            byte nuevo = _estadoOut[i];
            if (nuevo != e.estado)
            {
                AplicarEstado(e, nuevo);
                e.estado = nuevo;
            }
            if ((Time.realtimeSinceStartup - t0) * 1000f >= MS_PRESUPUESTO)
            {
                yield return null;
                t0 = Time.realtimeSinceStartup;
            }
        }

        _aplicando = false;
    }

    void AplicarEstado(Entrada e, byte estado)
    {
        switch (estado)
        {
            case 0: // Activo — detalle completo, render ON, sombras originales
                LiberarImpostor(e);
                FijarRender(e, false);
                RestaurarSombras(e);
                if (e.lod != null) e.lod.ForceLOD(-1);   // automático por distancia
                break;

            case 1: // Impostor-lite — visible pero barato: render ON, sin sombras, LOD mínimo
                if (e.impostor == null) AdquirirImpostor(e);
                // Si el impostor se adquirió: malla oculta, billboard la reemplaza.
                // Si no hay atlas/entrada: malla en LOD mínimo sin sombras (fallback).
                FijarRender(e, e.impostor != null);
                FijarSombras(e, ShadowCastingMode.Off);
                if (e.lod != null && e.lod.lodCount > 0) e.lod.ForceLOD(e.lod.lodCount - 1);
                break;

            default: // Oculto — mata el draw call, NO toca el GameObject (colliders/NavMesh vivos)
                LiberarImpostor(e);
                FijarRender(e, true);
                break;
        }
    }

    static void AdquirirImpostor(Entrada e)
    {
        if (e.osmId == 0 || GestorImpostores.Instance == null) return;
        e.impostor = GestorImpostores.Instance.Adquirir(e.osmId, e.go.transform);
    }

    static void LiberarImpostor(Entrada e)
    {
        if (e.impostor == null) return;
        GestorImpostores.Instance?.Liberar(e.impostor);
        e.impostor = null;
    }

    // Extrae el ID OSM del sufijo numérico del nombre (ej: "E_resid_12345678" → 12345678).
    static long ExtraerOsmId(string nombre)
    {
        int i = nombre.LastIndexOf('_');
        if (i >= 0 && long.TryParse(nombre.Substring(i + 1), out long id)) return id;
        return 0;
    }

    // forceRenderingOff: deja de dibujar sin desactivar el componente ni el GameObject.
    void FijarRender(Entrada e, bool off)
    {
        var rs = e.renderers;
        if (rs == null) return;
        for (int i = 0; i < rs.Length; i++)
            if (rs[i] != null && rs[i].forceRenderingOff != off)
                rs[i].forceRenderingOff = off;
    }

    void FijarSombras(Entrada e, ShadowCastingMode modo)
    {
        var rs = e.renderers;
        if (rs == null) return;
        for (int i = 0; i < rs.Length; i++)
            if (rs[i] != null && rs[i].shadowCastingMode != modo)
                rs[i].shadowCastingMode = modo;
    }

    void RestaurarSombras(Entrada e)
    {
        var rs = e.renderers; var so = e.sombraOrig;
        if (rs == null || so == null) return;
        int m = Mathf.Min(rs.Length, so.Length);
        for (int i = 0; i < m; i++)
            if (rs[i] != null && rs[i].shadowCastingMode != so[i])
                rs[i].shadowCastingMode = so[i];
    }

    void AsegurarBuffers(int n)
    {
        if (_cap >= n && _pos.IsCreated) return;
        if (_pos.IsCreated)       _pos.Dispose();
        if (_estadoIn.IsCreated)  _estadoIn.Dispose();
        if (_estadoOut.IsCreated) _estadoOut.Dispose();
        _cap = Mathf.NextPowerOfTwo(Mathf.Max(256, n));
        _pos       = new NativeArray<float3>(_cap, Allocator.Persistent);
        _estadoIn  = new NativeArray<byte>(_cap,   Allocator.Persistent);
        _estadoOut = new NativeArray<byte>(_cap,   Allocator.Persistent);
    }

    // ── Lectura para overlays de debug ──
    public int Gestionados => _ent.Count;
    public int Pendientes  => _pendientes.Count;
    public int CuentaEstado(byte estado)
    {
        int c = 0;
        for (int i = 0; i < _ent.Count; i++) if (_ent[i].estado == estado) c++;
        return c;
    }
}
