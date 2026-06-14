// Assets/Scripts/Crowd/SistemaMultitudBRG.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA MULTITUD BRG — orquestador (MonoBehaviour)
//
//  Sustituye el cuello de botella de CPU de SistemaMultitud para la MULTITUD DE
//  FONDO (5.000+ manifestantes): la lógica de flocking corre en jobs Burst sobre
//  todos los núcleos y el render va por BatchRendererGroup. Cero GameObjects por
//  agente, cero GC por frame.
//
//  ── AISLAMIENTO DE CAPAS ─────────────────────────────────────────────────────
//  Vive en el ensamblado leaf Alsasua.Crowd, que sólo referencia Alsasua.Core
//  (+ Burst/Collections/Mathematics). El suelo se consulta vía la interfaz
//  Core ITerrainService (ServiceLocator) / TerrenoGlobal; el objetivo (la plaza)
//  entra por el Inspector → ninguna dependencia hacia Runtime/Systems.
//
//  ── FRAME ────────────────────────────────────────────────────────────────────
//  Update    : raycasts de suelo escalonados (hilo ppal) + programar jobs @30 Hz
//  LateUpdate : completar jobs → subir matrices al BRG → swap de doble buffer
//
//  SETUP: añadir a un GameObject vacío, situarlo en el arranque de la marcha,
//  asignar 'objetivo' (la plaza) y un material HDRP/Lit (DOTS instancing).
// ═══════════════════════════════════════════════════════════════════════════

using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Alsasua.Crowd
{
    [AddComponentMenu("Alsasua/Sistema Multitud BRG (5000)")]
    public sealed class SistemaMultitudBRG : MonoBehaviour, ICrowdDensity, IInfluenciaSocial
    {
        [Header("═══ MULTITUD ═══")]
        [SerializeField, Range(100, 20000)] int cantidadAgentes = 5000;
        [Tooltip("Mesh de cada agente. Si es null se usa una cápsula.")]
        [SerializeField] Mesh meshAgente;
        [Tooltip("Material HDRP/Lit con soporte DOTS Instancing. Si es null se crea uno de fallback.")]
        [SerializeField] Material materialAgente;
        [Tooltip("Objetivo de la marcha (la plaza). Si es null se usa 'objetivoFallback'.")]
        [SerializeField] Transform objetivo;
        [Tooltip("Objetivo por defecto si no se asigna Transform. Por defecto, Herriko Plaza (Unity).")]
        [SerializeField] Vector3 objetivoFallback = new Vector3(1918f, 20.6f, 8570f);
        [Tooltip("Caja (m) alrededor de este transform donde se reparten los agentes al inicio.")]
        [SerializeField] Vector3 zonaSpawn = new Vector3(60f, 0f, 60f);
        [SerializeField] Vector3 escalaAgente = new Vector3(0.35f, 0.875f, 0.35f);

        [Header("═══ MARCHA / FLOCKING ═══")]
        [SerializeField] float velocidadCrucero = 1.3f;
        [SerializeField, Range(0.4f, 1.2f)] float radioSeparacion = 0.65f;
        [SerializeField, Range(1.0f, 5.0f)] float radioCohesion   = 2.2f;
        [SerializeField, Range(1.0f, 4.0f)] float radioAlineacion = 1.8f;
        [SerializeField] float pesoSeparacion = 2.2f;
        [SerializeField] float pesoCohesion   = 0.9f;
        [SerializeField] float pesoAlineacion = 1.0f;
        [SerializeField] float pesoObjetivo   = 1.5f;

        [Header("═══ SUELO ═══")]
        [Tooltip("Raycasts de corrección de altura por frame (se reparten en anillo entre agentes).")]
        [SerializeField] int raycastsPorFrame = 256;

        [Header("═══ INFLUENCIA SOCIAL ═══")]
        [Tooltip("Opinión inicial de cada agente [-1,1] (0=indeciso, +=pro-movimiento).")]
        [SerializeField, Range(-1f, 1f)] float opinionInicial = 0.1f;
        [Tooltip("Velocidad de difusión: cuánto arrastra el humor local a cada individuo.")]
        [SerializeField, Range(0f, 2f)] float difusionOpinion = 0.6f;
        [Tooltip("Fuerza de los pozos de evento (jugador pega / carga policial).")]
        [SerializeField, Range(0f, 4f)] float fuerzaEvento = 1.5f;
        [Tooltip("Relajación hacia el apoyo global (baseline). Bajo = los eventos mandan.")]
        [SerializeField, Range(0f, 1f)] float relajacionBaseline = 0.15f;
        [Tooltip("Radio (m) de difusión de opinión entre vecinos.")]
        [SerializeField] float radioDifusionSocial = 3f;
        [Tooltip("Opinión a partir de la cual un agente se interpone ante la policía.")]
        [SerializeField, Range(0f, 1f)] float umbralBloqueo = 0.4f;
        [Tooltip("Radio (m) en el que un radical reacciona al policía más cercano.")]
        [SerializeField] float radioReaccionPolicia = 8f;
        [Tooltip("Peso de la fuerza de interposición (muro humano).")]
        [SerializeField] float pesoBloqueo = 2.5f;
        [Tooltip("Peso de la fuerza de huida (opinión negativa abre paso).")]
        [SerializeField] float pesoHuida = 2.0f;
        [Tooltip("DEPURACIÓN: tiñe la multitud por opinión (azul hostil → rojo radical) " +
                 "y muestra un panel con métricas en vivo. Solo para tunear; apágalo en release.")]
        [SerializeField] bool depurarOpinion = false;

        [Header("═══ OMNI-GRID (experimental) ═══")]
        [Tooltip("EXPERIMENTAL: sacar los policías del Omni-Grid (conciencia cruzada) en vez " +
                 "del push manual ReportarAntagonista. Default OFF = comportamiento de siempre.")]
        [SerializeField] bool usarGridParaPolicias = false;
        [Tooltip("Radio (m) alrededor de este transform para buscar policías en el grid.")]
        [SerializeField] float radioGridPolicias = 150f;

        // ── Datos nativos (Persistent; alloc en OnEnable, free en OnDisable) ──
        NativeArray<Boid>        _boidsA, _boidsB;   // doble buffer ping-pong
        NativeArray<float>       _alturaSuelo;
        NativeArray<PackedMatrix> _o2w, _w2o;
        NativeParallelMultiHashMap<int, int> _grid;
        bool _usaA = true;                            // buffer "actual" = _usaA ? A : B

        // Snapshot de posiciones para ICrowdDensity: lo refresca LateUpdate tras
        // completar los jobs; ningún Job lo toca → ContarEnRadio() es siempre seguro.
        NativeArray<float3> _posSnapshot;
        int _nSnapshot;

        // ── Capa social (IInfluenciaSocial) ──
        NativeArray<float> _opinionA, _opinionB;     // doble buffer ping-pong (sigue a _usaA)
        NativeArray<float> _opinionSnapshot;         // lectura segura para ApoyoLocal01
        NativeArray<EventoBurst> _eventos;           // capacidad fija; _nEventos en uso
        NativeArray<float3>      _antagonistas;      // policías; _nAntagonistas en uso
        int _nEventos, _nAntagonistas;
        float _apoyoMedio01 = 0.5f;
        float _acumReporte;

        // ── Depuración visual ──
        NativeArray<float4> _coloresBase;    // paleta original (para restaurar)
        NativeArray<float4> _coloresDebug;   // teñido por opinión
        bool _debugTenido;
        int  _nRadicales, _nHostiles;
        GUIStyle _estiloGUI;

        const int MAX_EVENTOS    = 64;
        const int MAX_ANTAGONIST = 32;
        const float DUR_EVENTO       = 0.6f;   // s que vive un pozo de evento
        const float TTL_ANTAGONISTA  = 0.5f;   // s sin reportarse → el policía caduca

        // Buffers managed que alimentan el gameplay (hilo ppal). Se vuelcan a los
        // NativeArray en RefrescarEntradasSociales(), en la ventana sin jobs en vuelo.
        struct EventoVivo { public EventoInfluencia ev; public float expira; }
        readonly System.Collections.Generic.List<EventoVivo> _eventosVivos = new();
        readonly System.Collections.Generic.Dictionary<int, (Vector3 pos, float t)> _antagReg = new();
        readonly object _lockSocial = new();

        JobHandle _handle;
        bool _jobsEnVuelo;

        RenderizadorMultitudBRG _render;
        ITerrainService _terreno;
        IUnifiedSpatialGrid _grid;          // conciencia cruzada (policías) si usarGridParaPolicias
        NativeList<float3> _antagGridBuf;   // buffer de posiciones de policías leídas del grid

        Mesh     _meshPropio;     // creado por código → destruir
        Material _matPropio;      // creado por código → destruir

        int   _n;
        float _invCell;
        int   _idxSuelo;
        float _acum;
        bool  _listo;

        const float TICK = 1f / 30f;

        // ════════════════════════════════════════════════════════════════════
        //  LIFECYCLE
        // ════════════════════════════════════════════════════════════════════

        void OnEnable()  => Inicializar();
        void OnDisable() => Liberar();

        void Inicializar()
        {
            _n = Mathf.Clamp(cantidadAgentes, 1, 20000);
            _terreno = ServiceLocator.Get<ITerrainService>();
            _grid    = ServiceLocator.Get<IUnifiedSpatialGrid>();

            float cellSize = Mathf.Max(radioSeparacion, radioCohesion, radioAlineacion);
            _invCell = 1f / cellSize;

            _boidsA      = new NativeArray<Boid>(_n, Allocator.Persistent);
            _boidsB      = new NativeArray<Boid>(_n, Allocator.Persistent);
            _alturaSuelo = new NativeArray<float>(_n, Allocator.Persistent);
            _o2w         = new NativeArray<PackedMatrix>(_n, Allocator.Persistent);
            _w2o         = new NativeArray<PackedMatrix>(_n, Allocator.Persistent);
            _grid        = new NativeParallelMultiHashMap<int, int>(_n, Allocator.Persistent);
            _posSnapshot = new NativeArray<float3>(_n, Allocator.Persistent);

            _opinionA        = new NativeArray<float>(_n, Allocator.Persistent);
            _opinionB        = new NativeArray<float>(_n, Allocator.Persistent);
            _opinionSnapshot = new NativeArray<float>(_n, Allocator.Persistent);
            _eventos         = new NativeArray<EventoBurst>(MAX_EVENTOS, Allocator.Persistent);
            _antagonistas    = new NativeArray<float3>(MAX_ANTAGONIST, Allocator.Persistent);
            _antagGridBuf    = new NativeList<float3>(MAX_ANTAGONIST, Allocator.Persistent);

            _coloresBase  = new NativeArray<float4>(_n, Allocator.Persistent);
            _coloresDebug = new NativeArray<float4>(_n, Allocator.Persistent);
            SembrarAgentes(_coloresBase);
            var rndOp = new Unity.Mathematics.Random(0x5EEDu);
            for (int i = 0; i < _n; i++)
            {
                _posSnapshot[i] = _boidsA[i].pos;   // semilla antes del 1er tick
                float op = math.clamp(opinionInicial + rndOp.NextFloat(-0.2f, 0.2f), -1f, 1f);
                _opinionA[i] = op; _opinionB[i] = op; _opinionSnapshot[i] = op;
            }
            _nSnapshot = _n;

            Mesh mesh = meshAgente != null ? meshAgente : (_meshPropio = CrearMeshCapsula());
            Material mat = ResolverMaterial();
            if (mat == null)
            {
                AlsasuaLogger.Error("MultitudBRG", "Sin material válido (HDRP/Lit). Sistema desactivado.");
                return;   // Liberar() (OnDisable) libera los NativeArray ya reservados
            }

            // Radio de la esfera envolvente para el frustum culling (envuelve la cápsula escalada).
            float radio = escalaAgente.magnitude;
            // _coloresBase NO se libera aquí: lo conservamos para restaurar la paleta
            // tras el modo depuración (el BRG ya copió los colores a su GraphicsBuffer).
            _render = new RenderizadorMultitudBRG(mesh, mat, _n, _coloresBase, radio);

            _listo = true;
            ServiceLocator.Registrar<ICrowdDensity>(this);     // el audio lee densidad por zona
            ServiceLocator.Registrar<IInfluenciaSocial>(this); // el gameplay emite eventos sociales
            AlsasuaLogger.Info("MultitudBRG", $"Multitud BRG lista: {_n} agentes, 1 draw command.");
        }

        void Update()
        {
            if (!_listo) return;

            MuestrearSueloEscalonado();   // hilo principal: jobs NUNCA en vuelo aquí

            _acum += Time.deltaTime;
            if (_acum >= TICK)
            {
                RefrescarEntradasSociales();   // volcar eventos/antagonistas (jobs no en vuelo)
                ProgramarJobs(_acum);
                _acum = 0f;
            }
        }

        void LateUpdate()
        {
            if (!_listo || !_jobsEnVuelo) return;

            _handle.Complete();
            _render.SubirMatrices(_o2w, _w2o);
            _usaA = !_usaA;               // el output pasa a ser el input del próximo tick
            _jobsEnVuelo = false;

            // Refrescar snapshots (jobs ya completados → lectura segura) y media de opinión.
            var actual   = _usaA ? _boidsA : _boidsB;
            var actualOp = _usaA ? _opinionA : _opinionB;
            float suma = 0f;
            int nRad = 0, nHos = 0;
            for (int i = 0; i < _n; i++)
            {
                _posSnapshot[i]     = actual[i].pos;
                float op = actualOp[i];
                _opinionSnapshot[i] = op;
                suma += op;
                if (op > umbralBloqueo) nRad++;
                else if (op < -0.1f)    nHos++;
            }
            _nSnapshot = _n;
            _nRadicales = nRad; _nHostiles = nHos;
            _apoyoMedio01 = _n > 0 ? (suma / _n) * 0.5f + 0.5f : 0.5f;   // [-1,1] → [0,1]

            // Depuración visual: teñir por opinión (azul hostil → rojo radical) o restaurar paleta.
            if (depurarOpinion)
            {
                for (int i = 0; i < _n; i++) _coloresDebug[i] = ColorOpinion(_opinionSnapshot[i]);
                _render.SubirColores(_coloresDebug);
                _debugTenido = true;
            }
            else if (_debugTenido)
            {
                _render.SubirColores(_coloresBase);
                _debugTenido = false;
            }

            // Cerrar el lazo macro↔micro a ~1 Hz (no spamear la macro ni el HUD).
            _acumReporte += Time.deltaTime;
            if (_acumReporte >= 1f)
            {
                _acumReporte = 0f;
                SistemaApoyoPopular.Instance?.ReportarApoyoMultitud(_apoyoMedio01);
            }
        }

        // ── ICrowdDensity (lo consume SistemaAudioMultitud vía ServiceLocator) ──
        public int TotalAgentes => _nSnapshot;

        public int ContarEnRadio(Vector3 centro, float radio)
        {
            if (!_posSnapshot.IsCreated || _nSnapshot == 0) return 0;
            float r2 = radio * radio, cx = centro.x, cz = centro.z;
            int n = 0;
            for (int i = 0; i < _nSnapshot; i++)
            {
                float3 p = _posSnapshot[i];
                float dx = p.x - cx, dz = p.z - cz;
                if (dx * dx + dz * dz < r2) n++;
            }
            return n;
        }

        // ════════════════════════════════════════════════════════════════════
        //  IInfluenciaSocial (lo consume el gameplay vía ServiceLocator)
        // ════════════════════════════════════════════════════════════════════

        // Emitir/Reportar sólo tocan buffers managed (hilo ppal del gameplay); el
        // volcado a NativeArray ocurre en RefrescarEntradasSociales(), en la ventana
        // de Update donde los jobs NO están en vuelo → cero carrera con Burst.

        public void Emitir(EventoInfluencia ev)
        {
            lock (_lockSocial)
            {
                _eventosVivos.Add(new EventoVivo { ev = ev, expira = Time.time + DUR_EVENTO });
                if (_eventosVivos.Count > MAX_EVENTOS * 4) _eventosVivos.RemoveAt(0);
            }
        }

        public void ReportarAntagonista(int id, Vector3 pos)
        {
            lock (_lockSocial) { _antagReg[id] = (pos, Time.time); }
        }

        public float ApoyoMedio01 => _apoyoMedio01;

        public float ApoyoLocal01(Vector3 centro, float radio)
        {
            if (!_opinionSnapshot.IsCreated || _nSnapshot == 0) return _apoyoMedio01;
            float r2 = radio * radio, cx = centro.x, cz = centro.z, suma = 0f;
            int n = 0;
            for (int i = 0; i < _nSnapshot; i++)
            {
                float3 p = _posSnapshot[i];
                float dx = p.x - cx, dz = p.z - cz;
                if (dx * dx + dz * dz < r2) { suma += _opinionSnapshot[i]; n++; }
            }
            return n > 0 ? (suma / n) * 0.5f + 0.5f : _apoyoMedio01;
        }

        // Vuelca eventos vivos y antagonistas frescos a los NativeArray (hilo ppal,
        // jobs no en vuelo). Caduca lo expirado por tiempo.
        void RefrescarEntradasSociales()
        {
            float ahora = Time.time;
            lock (_lockSocial)
            {
                for (int k = _eventosVivos.Count - 1; k >= 0; k--)
                    if (_eventosVivos[k].expira < ahora) _eventosVivos.RemoveAt(k);

                int ne = Mathf.Min(_eventosVivos.Count, MAX_EVENTOS);
                for (int k = 0; k < ne; k++)
                {
                    var ev = _eventosVivos[k].ev;
                    _eventos[k] = new EventoBurst { pos = (float3)ev.pos, carga = ev.carga, radio = ev.radio };
                }
                _nEventos = ne;

                if (usarGridParaPolicias && _grid != null && _grid.Listo)
                {
                    // Conciencia cruzada vía Omni-Grid: los policías están en el grid
                    // (NPCBase se publica solo) → no hace falta el push manual.
                    _grid.QueryRadioPos((float3)transform.position, radioGridPolicias,
                                        TipoEspacial.Policia, _antagGridBuf);
                    int m = math.min(_antagGridBuf.Length, MAX_ANTAGONIST);
                    for (int k = 0; k < m; k++) _antagonistas[k] = _antagGridBuf[k];
                    _nAntagonistas = m;
                }
                else
                {
                    _nAntagonistas = 0;
                    foreach (var kv in _antagReg)
                    {
                        if (ahora - kv.Value.t > TTL_ANTAGONISTA) continue;
                        if (_nAntagonistas >= MAX_ANTAGONIST) break;
                        _antagonistas[_nAntagonistas++] = (float3)kv.Value.pos;
                    }
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  JOBS
        // ════════════════════════════════════════════════════════════════════

        void ProgramarJobs(float dt)
        {
            var inArr  = _usaA ? _boidsA : _boidsB;   // posiciones actuales
            var outArr = _usaA ? _boidsB : _boidsA;
            var opIn   = _usaA ? _opinionA : _opinionB;   // opinión sigue el mismo ping-pong
            var opOut  = _usaA ? _opinionB : _opinionA;

            float base01 = SistemaApoyoPopular.Instance != null ? SistemaApoyoPopular.Instance.Apoyo01 : 0.5f;

            _grid.Clear();   // conserva la capacidad (N); sólo resetea el contador

            var hash = new HashBoidsJob
            {
                boids = inArr,
                invCellSize = _invCell,
                grid = _grid.AsParallelWriter()
            }.Schedule(_n, 64);

            var flock = new FlockingJob
            {
                boidsIn = inArr,
                grid = _grid,
                alturaSuelo = _alturaSuelo,
                boidsOut = outArr,
                objetivo = (float3)Objetivo(),
                dt = dt,
                invCellSize = _invCell,
                r2Sep = radioSeparacion * radioSeparacion,
                r2Coh = radioCohesion   * radioCohesion,
                r2Ali = radioAlineacion * radioAlineacion,
                wSep = pesoSeparacion,
                wCoh = pesoCohesion,
                wAli = pesoAlineacion,
                wObj = pesoObjetivo,
                velCrucero = velocidadCrucero,
                velMax = velocidadCrucero * 1.8f,

                // ── Capa social ──
                opinionIn = opIn,
                opinionOut = opOut,
                eventos = _eventos,
                nEventos = _nEventos,
                antagonistas = _antagonistas,
                nAntagonistas = _nAntagonistas,
                r2Social = radioDifusionSocial * radioDifusionSocial,
                baseOpinion = base01 * 2f - 1f,
                kDif = difusionOpinion,
                kEvt = fuerzaEvento,
                kDecay = relajacionBaseline,
                umbralBloqueo = umbralBloqueo,
                r2Reaccion = radioReaccionPolicia * radioReaccionPolicia,
                wBloqueo = pesoBloqueo,
                wHuida = pesoHuida
            }.Schedule(_n, 64, hash);

            _handle = new BuildMatricesJob
            {
                boids = outArr,
                escala = (float3)escalaAgente,
                alturaPivote = escalaAgente.y,   // base de la cápsula al ras del suelo
                objectToWorld = _o2w,
                worldToObject = _w2o
            }.Schedule(_n, 64, flock);

            _jobsEnVuelo = true;
        }

        // ════════════════════════════════════════════════════════════════════
        //  SUELO (hilo principal — Physics/Terrain no son Burst-safe)
        // ════════════════════════════════════════════════════════════════════

        void MuestrearSueloEscalonado()
        {
            if (_terreno == null) _terreno = ServiceLocator.Get<ITerrainService>();
            var arr = _usaA ? _boidsA : _boidsB;

            int pasos = Mathf.Min(raycastsPorFrame, _n);
            for (int k = 0; k < pasos; k++)
            {
                _idxSuelo = (_idxSuelo + 1) % _n;
                float3 p = arr[_idxSuelo].pos;
                _alturaSuelo[_idxSuelo] = _terreno != null
                    ? _terreno.AlturaMundo(new Vector3(p.x, 0f, p.z))
                    : TerrenoGlobal.AlturaMundo(p.x, p.z);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  INICIALIZACIÓN
        // ════════════════════════════════════════════════════════════════════

        void SembrarAgentes(NativeArray<float4> colores)
        {
            // Paleta de ropa de manifestación (rojo/negro/verde/amarillo/azul/blanco…)
            var paleta = new float4[]
            {
                new float4(0.80f, 0.08f, 0.08f, 1f), new float4(0.06f, 0.06f, 0.06f, 1f),
                new float4(0.12f, 0.42f, 0.18f, 1f), new float4(0.88f, 0.80f, 0.08f, 1f),
                new float4(0.10f, 0.18f, 0.52f, 1f), new float4(0.88f, 0.88f, 0.88f, 1f),
                new float4(0.42f, 0.26f, 0.12f, 1f), new float4(0.35f, 0.18f, 0.45f, 1f),
            };

            Vector3 origen = transform.position;
            Vector3 haciaObjetivo = (Objetivo() - origen);
            haciaObjetivo.y = 0f;
            float3 vel0 = haciaObjetivo.sqrMagnitude > 0.01f
                ? (float3)(haciaObjetivo.normalized * velocidadCrucero)
                : new float3(0f, 0f, velocidadCrucero);

            var rnd = new Unity.Mathematics.Random(0xC0FFEEu);

            for (int i = 0; i < _n; i++)
            {
                float ox = rnd.NextFloat(-zonaSpawn.x * 0.5f, zonaSpawn.x * 0.5f);
                float oz = rnd.NextFloat(-zonaSpawn.z * 0.5f, zonaSpawn.z * 0.5f);
                float3 pos = new float3(origen.x + ox, origen.y, origen.z + oz);

                var boid = new Boid { pos = pos, vel = vel0 };
                _boidsA[i] = boid;
                _boidsB[i] = boid;
                _alturaSuelo[i] = origen.y;   // hasta que el raycast escalonado lo corrija
                colores[i] = paleta[rnd.NextInt(0, paleta.Length)];
            }
        }

        Vector3 Objetivo() => objetivo != null ? objetivo.position : objetivoFallback;

        // ── Color por opinión: azul (hostil) → amarillo (neutro) → rojo (radical) ──
        static float4 ColorOpinion(float op)
        {
            float t = op * 0.5f + 0.5f;   // [-1,1] → [0,1]
            float4 azul = new float4(0.10f, 0.30f, 0.95f, 1f);
            float4 amar = new float4(0.95f, 0.85f, 0.10f, 1f);
            float4 rojo = new float4(0.95f, 0.10f, 0.10f, 1f);
            return t < 0.5f ? math.lerp(azul, amar, t / 0.5f)
                            : math.lerp(amar, rojo, (t - 0.5f) / 0.5f);
        }

        void OnGUI()
        {
            if (!depurarOpinion || !_listo) return;
            _estiloGUI ??= new GUIStyle(GUI.skin.box)
            {
                fontSize = 12, alignment = TextAnchor.UpperLeft, padding = new RectOffset(8, 8, 6, 6)
            };
            float pctRad = _n > 0 ? 100f * _nRadicales / _n : 0f;
            float pctHos = _n > 0 ? 100f * _nHostiles  / _n : 0f;
            string txt =
                $"INFLUENCIA SOCIAL · {_n} agentes\n" +
                $"Apoyo medio: {_apoyoMedio01 * 100f:F0} %\n" +
                $"Radicales (≥{umbralBloqueo:F2}): {_nRadicales}  ({pctRad:F0} %)\n" +
                $"Hostiles  (<−0.1): {_nHostiles}  ({pctHos:F0} %)\n" +
                $"Eventos vivos: {_nEventos}   ·   Policías: {_nAntagonistas}";
            GUI.Box(new Rect(10, Screen.height - 104, 330, 94), txt, _estiloGUI);
        }

        Mesh CrearMeshCapsula()
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var mesh = Instantiate(temp.GetComponent<MeshFilter>().sharedMesh);
            DestroyImmediate(temp);
            return mesh;
        }

        Material ResolverMaterial()
        {
            if (materialAgente != null) return materialAgente;
            var sh = Shader.Find("HDRP/Lit");
            if (sh == null) return null;
            _matPropio = new Material(sh);
            return _matPropio;
        }

        // ════════════════════════════════════════════════════════════════════
        //  LIBERACIÓN
        // ════════════════════════════════════════════════════════════════════

        void Liberar()
        {
            _listo = false;
            if (_jobsEnVuelo) { _handle.Complete(); _jobsEnVuelo = false; }

            if (ReferenceEquals(ServiceLocator.Get<ICrowdDensity>(), this))
                ServiceLocator.Desregistrar<ICrowdDensity>();
            if (ReferenceEquals(ServiceLocator.Get<IInfluenciaSocial>(), this))
                ServiceLocator.Desregistrar<IInfluenciaSocial>();

            _render?.Dispose(); _render = null;

            if (_opinionA.IsCreated)        _opinionA.Dispose();
            if (_opinionB.IsCreated)        _opinionB.Dispose();
            if (_opinionSnapshot.IsCreated) _opinionSnapshot.Dispose();
            if (_eventos.IsCreated)         _eventos.Dispose();
            if (_antagonistas.IsCreated)    _antagonistas.Dispose();
            if (_antagGridBuf.IsCreated)    _antagGridBuf.Dispose();
            if (_coloresBase.IsCreated)     _coloresBase.Dispose();
            if (_coloresDebug.IsCreated)    _coloresDebug.Dispose();

            if (_posSnapshot.IsCreated) _posSnapshot.Dispose();
            if (_boidsA.IsCreated)      _boidsA.Dispose();
            if (_boidsB.IsCreated)      _boidsB.Dispose();
            if (_alturaSuelo.IsCreated) _alturaSuelo.Dispose();
            if (_o2w.IsCreated)         _o2w.Dispose();
            if (_w2o.IsCreated)         _w2o.Dispose();
            if (_grid.IsCreated)        _grid.Dispose();

            if (_meshPropio != null) { Destroy(_meshPropio); _meshPropio = null; }
            if (_matPropio  != null) { Destroy(_matPropio);  _matPropio  = null; }
        }
    }
}
