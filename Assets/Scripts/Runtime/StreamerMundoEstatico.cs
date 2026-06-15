// Assets/Scripts/Runtime/StreamerMundoEstatico.cs
// ═══════════════════════════════════════════════════════════════════════════
//  STREAMER DEL MUNDO ESTÁTICO — activación por distancia de edificios y props
//
//  EL HUECO QUE CIERRA: el jugador entraba a un mundo 100% activo. 1030 edificios +
//  props se renderizaban desde el frame 1 estuviera donde estuviera el jugador. El
//  único cull era el de SistemaOptimizacion: todo-o-nada a 600 m fijos, reactivo solo
//  al FPS. No había anillo intermedio ni radio gobernado por la GPU.
//
//  Este sistema da streaming por distancia REAL del mundo estático, con el radio que
//  publica el GOBERNADOR DE RENDER (IRenderBudgetGovernor) — que lo encoge cuando la
//  GPU se satura. Tres bandas (job Burst con histéresis):
//    · Activo   (< RadioActivacion)             → detalle completo
//    · Impostor (RadioActivacion..RadioImpostor) → "impostor-lite": fuerza el LOD más
//                                                   bajo y APAGA las sombras (el coste
//                                                   de sombra es lo más caro de la GPU)
//    · Oculto   (> RadioImpostor)               → SetActive(false)
//
//  Qué gestiona y qué NO:
//    · SÍ edificios (Edificios_OSM/Precisos) y props (Props_*, MobiliarioUrbano).
//    · NO árboles  → ya los streamea AlsasuaTreeStreamer (radio + LOD0-5 propios).
//    · NO multitud → es BRG (1 draw call) y la CPU-LOD la lleva el orquestador.
//
//  Coste acotado: clasificación en Burst sobre posiciones estáticas (capturadas una
//  vez); aplicación REPARTIDA por frames (presupuesto). Re-escanea las raíces cada
//  pocos segundos para recoger lo que el mundo va poblando tras el baseline.
//
//  Capa RUNTIME → consume Core (IRenderBudgetGovernor, AltsasuCore, jobs). No referencia
//  tipos de Systems: descubre el mundo por NOMBRE de raíz + componentes genéricos.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[DefaultExecutionOrder(-90)]   // tras SistemaOptimizacion (-95): leemos su mismo mundo
public sealed class StreamerMundoEstatico : MonoBehaviour
{
    // ── Raíces del mundo estático a vigilar (por nombre; tolerante a ausencias) ──
    static readonly string[] RAICES = {
        "Edificios_OSM", "Edificios_Precisos", "Edificios_AAA",
        "Props_Urbanos", "Props_Mapillary", "MobiliarioUrbano",
    };

    const float INTERVALO_CLASIF  = 0.33f;  // s entre clasificaciones (no cada frame)
    const float INTERVALO_RESCAN  = 3f;     // s entre re-escaneos de raíces (mundo poblándose)
    const float MS_PRESUPUESTO    = 2f;     // ms/frame para aplicar cambios de estado
    const float RADIO_MIN_SEGURO  = 60f;    // nunca ocultamos más cerca que esto (anti-pop brusco)

    // ── Una entrada por objeto estático gestionado ──
    sealed class Entrada
    {
        public Transform tr;
        public GameObject go;
        public Renderer[] renderers;   // para alternar sombras (impostor-lite)
        public LODGroup   lod;         // si lo tiene: forzamos el LOD más bajo en impostor
        public byte       estado;      // 0=Activo 1=Impostor 2=Oculto
    }

    readonly List<Entrada> _ent = new(1200);
    readonly HashSet<int>  _vistos = new(1200);   // instanceIDs ya registrados
    readonly Dictionary<string, Transform> _raices = new();
    readonly Dictionary<string, int> _conteoRaiz = new();   // childCount visto por raíz

    Transform _jugador;
    IRenderBudgetGovernor _gob;
    float _tClasif, _tRescan;
    bool  _aplicando;

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
        if (_aplicando) return;   // no solapar pases de aplicación

        // El gobernador puede registrarse después que nosotros (orden de boot): reintenta.
        if (_gob == null) { _gob = ServiceLocator.Get<IRenderBudgetGovernor>(); }

        _tRescan -= Time.unscaledDeltaTime;
        if (_tRescan <= 0f) { _tRescan = INTERVALO_RESCAN; Rescanear(); }

        if (_jugador == null) { _jugador = AltsasuCore.Jugador; return; }

        _tClasif -= Time.unscaledDeltaTime;
        if (_tClasif <= 0f) { _tClasif = INTERVALO_CLASIF; StartCoroutine(ClasificarYAplicar()); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  DESCUBRIMIENTO — recoge hijos nuevos de las raíces conforme el mundo puebla
    // ─────────────────────────────────────────────────────────────────────────
    void Rescanear()
    {
        bool huboAltas = false;
        for (int r = 0; r < RAICES.Length; r++)
        {
            string nombre = RAICES[r];
            if (!_raices.TryGetValue(nombre, out var raiz) || raiz == null)
            {
                var go = GameObject.Find(nombre);
                if (go == null) continue;
                raiz = go.transform;
                _raices[nombre] = raiz;
                _conteoRaiz[nombre] = -1;   // fuerza primer barrido
            }

            // Solo recorremos los hijos si el conteo cambió (barato en estado estable).
            int prev = _conteoRaiz.TryGetValue(nombre, out var c) ? c : -1;
            if (raiz.childCount == prev) continue;
            _conteoRaiz[nombre] = raiz.childCount;

            foreach (Transform hijo in raiz)
            {
                if (hijo == null) continue;
                int id = hijo.GetInstanceID();
                if (!_vistos.Add(id)) continue;   // ya registrado
                _ent.Add(new Entrada
                {
                    tr = hijo,
                    go = hijo.gameObject,
                    renderers = hijo.GetComponentsInChildren<Renderer>(true),
                    lod = hijo.GetComponentInChildren<LODGroup>(true),
                    estado = hijo.gameObject.activeSelf ? (byte)0 : (byte)2,
                });
                huboAltas = true;
            }
        }

        // Recién poblado: clasifica ya para no dejar el mundo entero encendido un frame.
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
            var e = _ent[i];
            // posición y estado actual al buffer (los destruidos quedan lejísimos → Oculto)
            if (e.tr != null) { var p = e.tr.position; _pos[i] = new float3(p.x, p.y, p.z); }
            else                _pos[i] = new float3(1e9f, 1e9f, 1e9f);
            _estadoIn[i] = e.estado;
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
            case 0: // Activo — detalle completo
                if (!e.go.activeSelf) e.go.SetActive(true);
                FijarSombras(e, ShadowCastingMode.On);
                if (e.lod != null) e.lod.ForceLOD(-1);   // automático por distancia
                break;

            case 1: // Impostor-lite — visible pero barato: sin sombras + LOD mínimo
                if (!e.go.activeSelf) e.go.SetActive(true);
                FijarSombras(e, ShadowCastingMode.Off);
                if (e.lod != null && e.lod.lodCount > 0) e.lod.ForceLOD(e.lod.lodCount - 1);
                break;

            default: // Oculto
                if (e.go.activeSelf) e.go.SetActive(false);
                break;
        }
    }

    void FijarSombras(Entrada e, ShadowCastingMode modo)
    {
        var rs = e.renderers;
        if (rs == null) return;
        for (int i = 0; i < rs.Length; i++)
            if (rs[i] != null && rs[i].shadowCastingMode != modo)
                rs[i].shadowCastingMode = modo;
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
    public int CuentaEstado(byte estado)
    {
        int c = 0;
        for (int i = 0; i < _ent.Count; i++) if (_ent[i].estado == estado) c++;
        return c;
    }
}
