// Assets/Scripts/SistemaReverbZonas.cs
// ═══════════════════════════════════════════════════════════════════════════
//  REVERB POR ZONA — AudioMixerSnapshot + oclusión acústica AAA
//
//  Detecta en qué zona está el jugador y aplica el preset de reverb
//  correspondiente vía AudioReverbZone o AudioMixerSnapshot.
//
//  Zonas:
//    Plaza       → reverb medio (edificios alrededor)
//    Calle       → reverb bajo
//    Túnel       → reverb alto (eco fuerte)
//    Interior    → reverb medio-alto
//    Monte       → reverb mínimo (espacio abierto)
//
//  Oclusión AAA (fase 4):
//    • Raycast fuente→oyente; si hay pared → volumen ×0.4 + low-pass a 800 Hz
//    • AudioLowPassFilter se añade/elimina dinámicamente (zero GC steady-state)
//    • cutoffFree  = 22000 Hz  (sin filtro perceptible)
//    • cutoffOcluido = 800 Hz  (sonido apagado tras hormigón/piedra vasca)
//    • Suavizado con Lerp 4 Hz para evitar clicks
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections.Generic;

public class SistemaReverbZonas : MonoBehaviour
{
    public static SistemaReverbZonas Instance { get; private set; }

    // ── Zonas ─────────────────────────────────────────────────────────────
    enum ZonaAcustica { Monte, Calle, Plaza, Interior, Tunel }
    ZonaAcustica _zonaActual  = ZonaAcustica.Calle;
    ZonaAcustica _zonaAnterior;
    float        _timerDeteccion;

    // ── AudioReverbZone ───────────────────────────────────────────────────
    AudioReverbZone _reverbZone;

    // ── Presets por zona ──────────────────────────────────────────────────
    struct PresetReverb
    {
        public AudioReverbPreset preset;
        public float             room;       // -10000 a 0 (0=seco)
        public float             decayTime;  // segundos
        public float             roomHF;
    }

    static readonly Dictionary<ZonaAcustica, PresetReverb> PRESETS = new()
    {
        [ZonaAcustica.Monte]    = new PresetReverb { preset = AudioReverbPreset.User, room = -2000, decayTime = 1.0f, roomHF = -1000 },
        [ZonaAcustica.Calle]    = new PresetReverb { preset = AudioReverbPreset.City,   room = -1000, decayTime = 1.5f, roomHF = -800  },
        [ZonaAcustica.Plaza]    = new PresetReverb { preset = AudioReverbPreset.User,   room = -500,  decayTime = 2.0f, roomHF = -600  },
        [ZonaAcustica.Interior] = new PresetReverb { preset = AudioReverbPreset.Room,   room = -300,  decayTime = 0.8f, roomHF = -200  },
        [ZonaAcustica.Tunel]    = new PresetReverb { preset = AudioReverbPreset.Hallway,room = 0,     decayTime = 3.5f, roomHF = -100  },
    };

    // ── Oclusión ──────────────────────────────────────────────────────────
    readonly List<AudioSource>                        _fuentesRegistradas = new();
    readonly Dictionary<AudioSource, AudioLowPassFilter> _filtros         = new();
    readonly Dictionary<AudioSource, float>           _volBase            = new();

    const float CUTOFF_FREE    = 22000f;
    const float CUTOFF_OCLUIDO =   800f;
    const float VOL_OCLUSION   =   0.4f;   // factor de volumen tras pared
    const float LERP_VEL       =     4f;   // Hz de suavizado

    float  _timerOclusion;
    Camera _camCache;
    int    _maskOclusion;

    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _camCache    = Camera.main;
        _maskOclusion = LayerMask.GetMask("Default", "Building", "Wall");
        InicializarReverbZone();
    }

    void InicializarReverbZone()
    {
        _reverbZone = gameObject.AddComponent<AudioReverbZone>();
        _reverbZone.minDistance = 1f;
        _reverbZone.maxDistance = 500f;
        AplicarPreset(ZonaAcustica.Calle);
    }

    void Update()
    {
        _timerDeteccion += Time.deltaTime;
        if (_timerDeteccion >= 1f)
        {
            _timerDeteccion = 0f;
            DetectarZona();
        }

        // Seguir al jugador
        var j = AltsasuCore.Jugador;
        if (j != null) transform.position = j.position;

        // Oclusión cada 0.5s
        _timerOclusion += Time.deltaTime;
        if (_timerOclusion >= 0.5f) { _timerOclusion = 0f; ActualizarOclusion(); }
    }

    // ── Detección de zona ─────────────────────────────────────────────────

    void DetectarZona()
    {
        var j = AltsasuCore.Jugador;
        if (j == null) return;

        // Raycast hacia arriba — si hay techo = interior/túnel
        bool techo = Physics.Raycast(j.position, Vector3.up, 10f);
        // Raycast en 4 direcciones — si hay paredes a <8m en todas = plaza/calle
        int paredes = 0;
        foreach (var dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            if (Physics.Raycast(j.position + Vector3.up, dir, 8f)) paredes++;

        ZonaAcustica nueva;
        if (techo && paredes >= 3) nueva = ZonaAcustica.Tunel;
        else if (techo)            nueva = ZonaAcustica.Interior;
        else if (paredes >= 3)     nueva = ZonaAcustica.Plaza;
        else if (paredes >= 1)     nueva = ZonaAcustica.Calle;
        else                       nueva = ZonaAcustica.Monte;

        if (nueva != _zonaActual)
        {
            _zonaAnterior = _zonaActual;
            _zonaActual   = nueva;
            AplicarPreset(nueva);
            AlsasuaLogger.Info("Reverb", $"Zona acústica: {nueva}");
        }
    }

    void AplicarPreset(ZonaAcustica zona)
    {
        if (_reverbZone == null || !PRESETS.TryGetValue(zona, out var p)) return;
        _reverbZone.reverbPreset = p.preset;
        if (p.preset == AudioReverbPreset.User)
        {
            _reverbZone.room      = (int)p.room;
            _reverbZone.decayTime = p.decayTime;
            _reverbZone.roomHF    = (int)p.roomHF;
        }
    }

    // ── Oclusión acústica AAA ─────────────────────────────────────────────
    //  Por cada fuente registrada:
    //    · Raycast fuente→oyente; si choca → hay pared
    //    · Con pared:  baja volumen a VOL_OCLUSION × volBase, cutoff → CUTOFF_OCLUIDO
    //    · Sin pared:  restaura volBase, cutoff → CUTOFF_FREE
    //    · AudioLowPassFilter se añade la primera vez y se reutiliza (zero GC estable)

    void ActualizarOclusion()
    {
        if (_camCache == null) _camCache = Camera.main;
        if (_camCache == null) return;
        float dt     = Time.deltaTime;
        Vector3 oyente = _camCache.transform.position;

        for (int i = _fuentesRegistradas.Count - 1; i >= 0; i--)
        {
            var src = _fuentesRegistradas[i];
            if (src == null)
            {
                _fuentesRegistradas.RemoveAt(i);
                continue;
            }

            // Guardar volumen base la primera vez
            if (!_volBase.ContainsKey(src))
                _volBase[src] = src.volume > 0f ? src.volume : 1f;

            Vector3 dir  = oyente - src.transform.position;
            float   dist = dir.magnitude;

            // Demasiado cerca: sin filtro
            if (dist < 0.5f)
            {
                SetFiltro(src, CUTOFF_FREE, dt);
                src.volume = Mathf.Lerp(src.volume, _volBase[src], dt * LERP_VEL);
                continue;
            }

            bool pared = Physics.Raycast(src.transform.position, dir.normalized, dist, _maskOclusion);

            float cutoffObj = pared ? CUTOFF_OCLUIDO : CUTOFF_FREE;
            float volObj    = pared ? _volBase[src] * VOL_OCLUSION : _volBase[src];

            SetFiltro(src, cutoffObj, dt);
            src.volume = Mathf.Lerp(src.volume, volObj, dt * LERP_VEL);
        }
    }

    // Gestiona el AudioLowPassFilter de una fuente (añade si falta, suaviza cutoff)
    void SetFiltro(AudioSource src, float cutoffObj, float dt)
    {
        if (!_filtros.TryGetValue(src, out var lpf) || lpf == null)
        {
            lpf = src.gameObject.GetComponent<AudioLowPassFilter>();
            if (lpf == null) lpf = src.gameObject.AddComponent<AudioLowPassFilter>();
            lpf.cutoffFrequency = cutoffObj;   // sin lerp en el primer frame
            _filtros[src] = lpf;
            return;
        }
        lpf.cutoffFrequency = Mathf.Lerp(lpf.cutoffFrequency, cutoffObj, dt * LERP_VEL);
    }

    public static void RegistrarFuente(AudioSource src)
    {
        if (Instance != null && src != null && !Instance._fuentesRegistradas.Contains(src))
            Instance._fuentesRegistradas.Add(src);
    }

    public static void DesregistrarFuente(AudioSource src)
    {
        if (Instance == null || src == null) return;
        Instance._fuentesRegistradas.Remove(src);
        if (Instance._filtros.TryGetValue(src, out var lpf) && lpf != null)
            Destroy(lpf);
        Instance._filtros.Remove(src);
        Instance._volBase.Remove(src);
    }
}
