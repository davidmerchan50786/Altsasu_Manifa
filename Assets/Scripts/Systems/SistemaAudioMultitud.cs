// Assets/Scripts/Systems/SistemaAudioMultitud.cs
// ═══════════════════════════════════════════════════════════════════════════
//  AUDIO CLUSTERS — sonido de multitud por ZONA (capa SYSTEMS)
//
//  En vez de miles de AudioSources (uno por NPC), un puñado de "clusters": una
//  zona = 2 fuentes 3D (Consignas + Disturbios) cuyo volumen se modula por:
//   · DENSIDAD de NPCs en la zona  → ICrowdDensity (SistemaMultitudBRG, vía SL)
//   · OCLUSIÓN por edificios        → RaycastCommand batched (Unity Jobs)
//   · NIVEL DE TENSIÓN              → IWantedSystem.NivelBusqueda (0..5 → 0..1):
//        tensión baja  → predomina "Consignas" (cánticos)
//        tensión alta  → predomina "Disturbios" + sirenas
//
//  Coste de audio CONSTANTE e independiente del nº de NPCs. Reusa los clips ya
//  registrados en AudioManager (Clip.Consignas/Disturbios) y el patrón de
//  RaycastCommand+QueryParameters de SistemaDeteccionIA.
//
//  Se auto-arranca. Capa Systems (ve Runtime: AudioManager/GeoDataAlsasua; y
//  Core: ICrowdDensity/IWantedSystem/ServiceLocator).
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Audio;

[DefaultExecutionOrder(60)]   // tras la simulación de multitud → snapshot ya válido
public sealed class SistemaAudioMultitud : MonoBehaviour
{
    [Header("Mezcla")]
    [SerializeField] float volumenMaestro = 1f;
    [Tooltip("Velocidad de suavizado de la tensión (mayor = reacciona antes).")]
    [SerializeField] float velTension = 0.5f;

    [Header("Rendimiento")]
    [Tooltip("Cada cuántos frames se relanza el batch de oclusión.")]
    [SerializeField] int framesPorOclusion = 6;
    [Tooltip("Clusters cuya densidad se recalcula por frame (round-robin).")]
    [SerializeField] int clustersDensidadPorFrame = 2;

    [Header("AudioMixer (OPCIONAL — crea el asset; ver cabecera de pasos)")]
    [Tooltip("Ruta en Resources del AudioMixer. Si no existe, se usa control directo de volumen/low-pass.")]
    [SerializeField] string rutaMixer = "Audio/MixerManifa";
    [SerializeField] string grupoCrowd = "Crowd";
    [SerializeField] string snapshotCalma = "Calma";
    [SerializeField] string snapshotDisturbio = "Disturbio";

    sealed class Cluster
    {
        public string nombre;
        public Vector3 centro;
        public float radio;
        public int aforo;
        public float densidadBase;          // fallback si no hay sistema de multitud
        public AudioSource srcCons, srcDist;
        public AudioLowPassFilter lpfCons, lpfDist;
        public float densidad01;
        public float oclusion, oclusionObjetivo;
    }

    readonly List<Cluster> _cl = new();
    NativeArray<RaycastCommand> _cmd;
    NativeArray<RaycastHit>     _hit;
    JobHandle _job;
    bool _jobVivo;

    int _maskEdificios;
    int _frame, _cursorDens;
    float _tension;
    bool _clipsOk;
    Transform _listener;
    AudioClip _clipCons, _clipDist;

    // Mixer opcional: snapshots Calma↔Disturbio mezclados por tensión.
    bool _mixerProbado;
    AudioMixer _mixer;
    AudioMixerSnapshot _snapCalma, _snapDist;
    readonly AudioMixerSnapshot[] _snaps = new AudioMixerSnapshot[2];
    readonly float[] _pesos = new float[2];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<SistemaAudioMultitud>() != null) return;
        var go = new GameObject("SistemaAudioMultitud");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaAudioMultitud>();
    }

    void Awake()
    {
        // Edificios = todo menos jugador/terreno/agua/UI (sus colliders ocluyen el sonido).
        _maskEdificios = ~LayerMask.GetMask("Player", "Ignore Raycast", "Terrain", "Water", "UI");

        // Clusters anclados a landmarks reales (GeoDataAlsasua). Y al suelo del valle.
        float y = GeoDataAlsasua.COTA_PLAZA - GeoDataAlsasua.Z_MIN;
        void Add(string n, Vector3 c, float r, int aforo, float baseDens)
            => _cl.Add(new Cluster { nombre = n, centro = new Vector3(c.x, y, c.z), radio = r, aforo = aforo, densidadBase = baseDens });

        Add("Zona Plaza",    GeoDataAlsasua.HerrikoPlaza,     60f, 600, 0.7f);
        Add("Estación",      GeoDataAlsasua.EstacionTren,     50f, 200, 0.35f);
        Add("Cuartel",       GeoDataAlsasua.CuartelGC,        45f, 150, 0.3f);
        Add("Barrio Norte",  GeoDataAlsasua.BarrioNorte,      55f, 250, 0.4f);
        Add("Carretera N-1", GeoDataAlsasua.CarreteraN1Norte, 50f, 150, 0.3f);

        foreach (var cl in _cl)
        {
            cl.srcCons = CrearFuente(cl, "Consignas",  out cl.lpfCons);
            cl.srcDist = CrearFuente(cl, "Disturbios", out cl.lpfDist);
        }

        _cmd = new NativeArray<RaycastCommand>(_cl.Count, Allocator.Persistent);
        _hit = new NativeArray<RaycastHit>(_cl.Count, Allocator.Persistent);
    }

    AudioSource CrearFuente(Cluster cl, string sufijo, out AudioLowPassFilter lpf)
    {
        var go = new GameObject($"Cluster_{cl.nombre}_{sufijo}");
        go.transform.SetParent(transform);
        go.transform.position = cl.centro;

        var s = go.AddComponent<AudioSource>();
        s.loop         = true;
        s.playOnAwake  = false;
        s.spatialBlend = 1f;                              // 3D
        s.rolloffMode  = AudioRolloffMode.Logarithmic;
        s.minDistance  = cl.radio * 0.25f;
        s.maxDistance  = cl.radio * 3f;
        s.dopplerLevel = 0f;
        s.volume       = 0f;

        lpf = go.AddComponent<AudioLowPassFilter>();
        lpf.cutoffFrequency = 22000f;
        return s;
    }

    void Update()
    {
        if (_listener == null) { var c = Camera.main; if (c != null) _listener = c.transform; }

        AsignarClipsSiHaceFalta();
        ConfigurarMixerSiHaceFalta();
        ActualizarDensidad();
        ActualizarTension();
        Mezclar();
        ProgramarOclusion();
        _frame++;
    }

    void LateUpdate()
    {
        if (!_jobVivo) return;
        _job.Complete();
        _jobVivo = false;
        for (int c = 0; c < _cl.Count; c++)
            _cl[c].oclusionObjetivo = _hit[c].collider != null ? 1f : 0f;   // edificio entre zona y oído
    }

    // ── Asignación perezosa de clips (AudioManager lo crea el SceneBootstrapper) ──
    void AsignarClipsSiHaceFalta()
    {
        if (_clipsOk || AudioManager.I == null) return;
        _clipCons = AudioManager.I.GetClip(AudioManager.Clip.Consignas);
        _clipDist = AudioManager.I.GetClip(AudioManager.Clip.Disturbios);
        foreach (var cl in _cl)
        {
            if (_clipCons != null) { cl.srcCons.clip = _clipCons; cl.srcCons.Play(); }
            if (_clipDist != null) { cl.srcDist.clip = _clipDist; cl.srcDist.Play(); }
        }
        _clipsOk = true;
        if (_clipCons == null && _clipDist == null)
            AlsasuaLogger.Warn("AudioMultitud", "Sin clips Consignas/Disturbios en Resources/Audio — los clusters quedan mudos.");
    }

    void ActualizarDensidad()
    {
        if (_cl.Count == 0) return;
        var dens = ServiceLocator.Get<ICrowdDensity>();
        for (int k = 0; k < clustersDensidadPorFrame; k++)
        {
            _cursorDens = (_cursorDens + 1) % _cl.Count;
            var cl = _cl[_cursorDens];
            float objetivo = dens != null
                ? Mathf.Clamp01((float)dens.ContarEnRadio(cl.centro, cl.radio) / Mathf.Max(1, cl.aforo))
                : cl.densidadBase;
            cl.densidad01 = Mathf.MoveTowards(cl.densidad01, objetivo, Time.deltaTime * 1.5f);
        }
    }

    void ActualizarTension()
    {
        int wanted = ServiceLocator.Get<IWantedSystem>()?.NivelBusqueda ?? 0;
        _tension = Mathf.MoveTowards(_tension, Mathf.Clamp01(wanted / 5f), Time.deltaTime * velTension);
        if (AudioManager.I != null) AudioManager.SetSirena(_tension > 0.6f);   // sirenas con tensión alta

        // Mezcla por snapshot (EQ/reverb/ducking de música): Calma ↔ Disturbio por tensión.
        if (_mixer != null && _snapCalma != null && _snapDist != null)
        {
            _pesos[0] = 1f - _tension;
            _pesos[1] = _tension;
            _mixer.TransitionToSnapshots(_snaps, _pesos, 0.3f);
        }
    }

    // El asset de AudioMixer es manual (ver pasos abajo); aquí solo lo CONSUMIMOS.
    // Si no existe, el sistema sigue con control directo de volumen/low-pass (igual de válido).
    void ConfigurarMixerSiHaceFalta()
    {
        if (_mixerProbado) return;
        _mixerProbado = true;

        _mixer = Resources.Load<AudioMixer>(rutaMixer);
        if (_mixer == null)
        {
            AlsasuaLogger.Info("AudioMultitud", $"Sin AudioMixer en Resources/{rutaMixer} — mezcla directa (volumen/low-pass).");
            return;
        }

        var grupos = _mixer.FindMatchingGroups(grupoCrowd);
        if (grupos != null && grupos.Length > 0)
            foreach (var cl in _cl)
            {
                cl.srcCons.outputAudioMixerGroup = grupos[0];
                cl.srcDist.outputAudioMixerGroup = grupos[0];
            }

        _snapCalma = _mixer.FindSnapshot(snapshotCalma);
        _snapDist  = _mixer.FindSnapshot(snapshotDisturbio);
        _snaps[0] = _snapCalma;
        _snaps[1] = _snapDist;

        AlsasuaLogger.Info("AudioMultitud",
            $"AudioMixer '{_mixer.name}' conectado (grupo '{grupoCrowd}', snapshots '{snapshotCalma}'/'{snapshotDisturbio}').");
    }

    void Mezclar()
    {
        float dt = Time.deltaTime;
        for (int i = 0; i < _cl.Count; i++)
        {
            var cl = _cl[i];
            cl.oclusion = Mathf.MoveTowards(cl.oclusion, cl.oclusionObjetivo, dt * 4f); // suave → sin clicks
            float cut   = Mathf.Lerp(22000f, 900f, cl.oclusion);                        // ocluido → grave
            cl.lpfCons.cutoffFrequency = cut;
            cl.lpfDist.cutoffFrequency = cut;

            float baseGain = cl.densidad01 * Mathf.Lerp(1f, 0.4f, cl.oclusion) * volumenMaestro;
            cl.srcCons.volume = baseGain * (1f - _tension);   // calma = cánticos
            cl.srcDist.volume = baseGain * _tension;          // tensión = disturbios
        }
    }

    void ProgramarOclusion()
    {
        if (_jobVivo || _listener == null || _cl.Count == 0) return;
        if (_frame % Mathf.Max(1, framesPorOclusion) != 0) return;

        var qp = new QueryParameters(layerMask: _maskEdificios, hitTriggers: QueryTriggerInteraction.Ignore);
        Vector3 oido = _listener.position;
        for (int c = 0; c < _cl.Count; c++)
        {
            Vector3 o = _cl[c].centro + Vector3.up * 1.5f;   // por encima del suelo/mobiliario bajo
            Vector3 d = oido - o;
            float dist = d.magnitude;
            _cmd[c] = new RaycastCommand(o, dist > 0.01f ? d / dist : Vector3.up, qp, Mathf.Max(dist, 0.01f));
        }
        _job = RaycastCommand.ScheduleBatch(_cmd, _hit, minCommandsPerJob: 1, maxHits: 1, dependsOn: default);
        _jobVivo = true;
    }

    void OnDestroy()
    {
        if (_jobVivo) { _job.Complete(); _jobVivo = false; }
        if (_cmd.IsCreated) _cmd.Dispose();
        if (_hit.IsCreated) _hit.Dispose();
    }
}
