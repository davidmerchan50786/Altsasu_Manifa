// Assets/Scripts/AudioManager.cs
// ═══════════════════════════════════════════════════════════════════════════
//  AUDIO MANAGER — pool, categorías, volumen, generación procedural
//
//  • Pool de 32 AudioSources reciclables (zero GC en runtime)
//  • 4 categorías: Master, Musica, SFX, Ambiente
//  • Clips registrados por enum Clip (sin strings en runtime)
//  • Audio procedural sintético para los clips que falten:
//      Disparo → burst de ruido blanco filtrado
//      Explosion → burst grave + decaimiento
//      Pasos → clicks rítmicos
//      Sirena → oscilador FM
//  • 3D espacial configurable por clip
//  • Fade in/out con corrutinas
//  • Respeta SistemaOpciones.VolSFX/VolAmbiente
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class AudioManager : MonoBehaviour
{
    public static AudioManager I { get; private set; }

    // ── Categorías de volumen ─────────────────────────────────────────────
    public enum Categoria { SFX, Musica, Ambiente, UI }

    // ── Clips registrados ─────────────────────────────────────────────────
    public enum Clip
    {
        // Armas
        Disparo, DisparoEscopeta, DisparoFusil, Recarga, SecoCasquillo,
        // Explosiones / impactos
        Explosion, ExplosionPequena, ImpactoMetal, ImpactoHormigon,
        ImpactoMadera, ImpactoSuelo, ImpactoSangre,
        // Vehículo
        MotorArranque, MotorAceleracion, MotorFrenada, ChirridoNeumatico, Bocina,
        // Jugador
        PasoTierra, PasoAsfalto, PasoGrava, Salto, Aterrizaje, DanoRecibido, Muerte,
        // Ambiente
        VientoMontana, LluviaLigera, LluviaTormenta, Trueno, Pajaros, Grillo,
        // Manifestación
        Consignas, Disturbios, Aplausos,
        // UI / sistema
        UIClick, UIError, UINotificacion, MisionIniciada, MisionCompletada,
        // Policia
        Sirena, RadioPolicia, PasoPolicia,
    }

    // ── Pool ──────────────────────────────────────────────────────────────
    const int POOL_SIZE = 32;
    AudioSource[] _pool;
    int           _poolIdx;

    // ── Registro de clips ─────────────────────────────────────────────────
    readonly Dictionary<Clip, AudioClip>     _clips      = new();
    readonly Dictionary<Clip, Categoria>     _categorias = new();
    readonly Dictionary<Clip, float>         _volumenes  = new();
    readonly Dictionary<Clip, bool>          _is3D       = new();

    // ── AudioSources persistentes (loop) ──────────────────────────────────
    AudioSource _srcAmbiente;
    AudioSource _srcSirena;

    // ════════════════════════════════════════════════════════════════════════
    //  BOOT
    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        CrearPool();
        RegistrarClips();
        GenerarClipsSinteticos();
        CrearFuentesPersistentes();
    }

    void CrearPool()
    {
        _pool = new AudioSource[POOL_SIZE];
        for (int i = 0; i < POOL_SIZE; i++)
        {
            var go = new GameObject($"AudioPool_{i:00}");
            go.transform.SetParent(transform);
            _pool[i] = go.AddComponent<AudioSource>();
            _pool[i].playOnAwake = false;
            // BUG FIX (auditoría): atenuación 3D a escala de mundo abierto.
            // Antes usaban el rolloff logarítmico por defecto (maxDistance 500) → mal escalado.
            _pool[i].rolloffMode = AudioRolloffMode.Linear;
            _pool[i].minDistance = 4f;
            _pool[i].maxDistance = 90f;
            _pool[i].dopplerLevel = 0.25f;
        }
    }

    void RegistrarClips()
    {
        // Categorías y volúmenes base
        Reg(Clip.Disparo,           Categoria.SFX,      0.7f, true);
        Reg(Clip.DisparoEscopeta,   Categoria.SFX,      0.8f, true);
        Reg(Clip.DisparoFusil,      Categoria.SFX,      0.75f,true);
        Reg(Clip.Recarga,           Categoria.SFX,      0.5f, true);
        Reg(Clip.SecoCasquillo,     Categoria.SFX,      0.3f, true);
        Reg(Clip.Explosion,         Categoria.SFX,      1.0f, true);
        Reg(Clip.ExplosionPequena,  Categoria.SFX,      0.7f, true);
        Reg(Clip.ImpactoMetal,      Categoria.SFX,      0.6f, true);
        Reg(Clip.ImpactoHormigon,   Categoria.SFX,      0.5f, true);
        Reg(Clip.ImpactoMadera,     Categoria.SFX,      0.45f,true);
        Reg(Clip.ImpactoSuelo,      Categoria.SFX,      0.4f, true);
        Reg(Clip.ImpactoSangre,     Categoria.SFX,      0.4f, true);
        Reg(Clip.MotorArranque,     Categoria.SFX,      0.6f, true);
        Reg(Clip.MotorAceleracion,  Categoria.SFX,      0.5f, true);
        Reg(Clip.MotorFrenada,      Categoria.SFX,      0.45f,true);
        Reg(Clip.ChirridoNeumatico, Categoria.SFX,      0.7f, true);
        Reg(Clip.Bocina,            Categoria.SFX,      0.8f, true);
        Reg(Clip.PasoTierra,        Categoria.SFX,      0.35f,true);
        Reg(Clip.PasoAsfalto,       Categoria.SFX,      0.3f, true);
        Reg(Clip.PasoGrava,         Categoria.SFX,      0.4f, true);
        Reg(Clip.Salto,             Categoria.SFX,      0.5f, true);
        Reg(Clip.Aterrizaje,        Categoria.SFX,      0.6f, true);
        Reg(Clip.DanoRecibido,      Categoria.SFX,      0.7f, false);
        Reg(Clip.Muerte,            Categoria.SFX,      0.8f, false);
        Reg(Clip.VientoMontana,     Categoria.Ambiente, 0.4f, false);
        Reg(Clip.LluviaLigera,      Categoria.Ambiente, 0.5f, false);
        Reg(Clip.LluviaTormenta,    Categoria.Ambiente, 0.7f, false);
        Reg(Clip.Trueno,            Categoria.Ambiente, 0.9f, true);
        Reg(Clip.Pajaros,           Categoria.Ambiente, 0.3f, true);
        Reg(Clip.Grillo,            Categoria.Ambiente, 0.25f,true);
        Reg(Clip.Consignas,         Categoria.Ambiente, 0.8f, true);
        Reg(Clip.Disturbios,        Categoria.Ambiente, 0.7f, true);
        Reg(Clip.Aplausos,          Categoria.Ambiente, 0.6f, true);
        Reg(Clip.UIClick,           Categoria.UI,       0.4f, false);
        Reg(Clip.UIError,           Categoria.UI,       0.5f, false);
        Reg(Clip.UINotificacion,    Categoria.UI,       0.5f, false);
        Reg(Clip.MisionIniciada,    Categoria.UI,       0.7f, false);
        Reg(Clip.MisionCompletada,  Categoria.UI,       0.8f, false);
        Reg(Clip.Sirena,            Categoria.SFX,      0.9f, true);
        Reg(Clip.RadioPolicia,      Categoria.SFX,      0.5f, true);
        Reg(Clip.PasoPolicia,       Categoria.SFX,      0.35f,true);
    }

    void Reg(Clip c, Categoria cat, float vol, bool es3D)
    {
        _categorias[c] = cat;
        _volumenes[c]  = vol;
        _is3D[c]       = es3D;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  GENERACIÓN PROCEDURAL DE CLIPS FALTANTES
    // ════════════════════════════════════════════════════════════════════════

    void GenerarClipsSinteticos()
    {
        // Intentar cargar desde Assets/Audio primero
        CargarClipsReales();

        // Generar sintéticos para los que faltan
        if (!_clips.ContainsKey(Clip.Disparo))          _clips[Clip.Disparo]         = GenDisparo(0.18f);
        if (!_clips.ContainsKey(Clip.DisparoEscopeta))  _clips[Clip.DisparoEscopeta] = GenDisparo(0.35f);
        if (!_clips.ContainsKey(Clip.DisparoFusil))     _clips[Clip.DisparoFusil]    = GenDisparo(0.25f);
        if (!_clips.ContainsKey(Clip.Explosion))        _clips[Clip.Explosion]        = GenExplosion(1.2f, 80f);
        if (!_clips.ContainsKey(Clip.ExplosionPequena)) _clips[Clip.ExplosionPequena] = GenExplosion(0.5f, 120f);
        if (!_clips.ContainsKey(Clip.ImpactoMetal))     _clips[Clip.ImpactoMetal]    = GenImpactoMetal();
        if (!_clips.ContainsKey(Clip.ImpactoHormigon))  _clips[Clip.ImpactoHormigon] = GenImpactoSuelo(150f);
        if (!_clips.ContainsKey(Clip.ImpactoMadera))    _clips[Clip.ImpactoMadera]   = GenImpactoSuelo(300f);
        if (!_clips.ContainsKey(Clip.ImpactoSuelo))     _clips[Clip.ImpactoSuelo]    = GenImpactoSuelo(200f);
        if (!_clips.ContainsKey(Clip.PasoAsfalto))      _clips[Clip.PasoAsfalto]     = GenPaso(800f, 0.06f);
        if (!_clips.ContainsKey(Clip.PasoTierra))       _clips[Clip.PasoTierra]      = GenPaso(400f, 0.08f);
        if (!_clips.ContainsKey(Clip.PasoGrava))        _clips[Clip.PasoGrava]       = GenPasoGrava();
        if (!_clips.ContainsKey(Clip.Sirena))           _clips[Clip.Sirena]           = GenSirena();
        if (!_clips.ContainsKey(Clip.UIClick))          _clips[Clip.UIClick]          = GenClick();
        if (!_clips.ContainsKey(Clip.MisionIniciada))   _clips[Clip.MisionIniciada]  = GenNotificacion(440f);
        if (!_clips.ContainsKey(Clip.MisionCompletada)) _clips[Clip.MisionCompletada]= GenNotificacion(660f);
        if (!_clips.ContainsKey(Clip.DanoRecibido))     _clips[Clip.DanoRecibido]    = GenDisparo(0.05f);
        if (!_clips.ContainsKey(Clip.ChirridoNeumatico))_clips[Clip.ChirridoNeumatico]=GenChirrido();

        AlsasuaLogger.Info("AudioManager", $"Clips totales: {_clips.Count} ({_clips.Count} sintéticos incluidos)");
    }

    void CargarClipsReales()
    {
        // Mapeo nombre-archivo → enum
        var mapa = new Dictionary<string, Clip>
        {
            ["disparo"] = Clip.Disparo, ["shot"] = Clip.Disparo, ["gunshot"] = Clip.Disparo,
            ["explosion"] = Clip.Explosion, ["blast"] = Clip.Explosion,
            ["footstep"] = Clip.PasoAsfalto, ["step"] = Clip.PasoAsfalto,
            ["siren"] = Clip.Sirena, ["sirena"] = Clip.Sirena,
            ["rain"] = Clip.LluviaLigera, ["lluvia"] = Clip.LluviaLigera,
            ["thunder"] = Clip.Trueno, ["trueno"] = Clip.Trueno,
            ["wind"] = Clip.VientoMontana, ["viento"] = Clip.VientoMontana,
            ["click"] = Clip.UIClick, ["button"] = Clip.UIClick,
            ["reload"] = Clip.Recarga, ["recarga"] = Clip.Recarga,
            ["motor"] = Clip.MotorAceleracion, ["engine"] = Clip.MotorAceleracion,
        };

        // Cargar desde Resources/Audio (no requiere Addressables)
        var clips = Resources.LoadAll<AudioClip>("Audio");
        foreach (var c in clips)
        {
            string n = c.name.ToLower();
            foreach (var kv in mapa)
                if (n.Contains(kv.Key) && !_clips.ContainsKey(kv.Value))
                {
                    _clips[kv.Value] = c;
                    break;
                }
        }
    }

    // ── Generadores síntéticos ────────────────────────────────────────────

    static AudioClip GenDisparo(float duracion)
    {
        int sr = 44100; int len = (int)(sr * duracion);
        float[] data = new float[len];
        var rng = new System.Random();
        for (int i = 0; i < len; i++)
        {
            float t    = (float)i / len;
            float env  = Mathf.Exp(-t * 18f);                        // decay rápido
            float noise= (float)(rng.NextDouble() * 2 - 1);
            float tone = Mathf.Sin(2 * Mathf.PI * 120f * i / sr);    // fundamental grave
            data[i] = (noise * 0.7f + tone * 0.3f) * env * 0.8f;
        }
        var clip = AudioClip.Create("Disparo_Synth", len, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip GenExplosion(float duracion, float freqBase)
    {
        int sr = 44100; int len = (int)(sr * duracion);
        float[] data = new float[len];
        var rng = new System.Random(42);
        for (int i = 0; i < len; i++)
        {
            float t   = (float)i / len;
            float env = Mathf.Exp(-t * 4f) * (1f - Mathf.Exp(-t * 80f)); // attack + decay
            float noise = (float)(rng.NextDouble() * 2 - 1);
            float sub   = Mathf.Sin(2 * Mathf.PI * freqBase * Mathf.Exp(-t * 3f) * i / sr);
            data[i] = (noise * 0.6f + sub * 0.4f) * env;
        }
        var clip = AudioClip.Create("Explosion_Synth", len, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip GenImpactoMetal()
    {
        int sr = 44100; int len = (int)(sr * 0.25f);
        float[] data = new float[len];
        var rng = new System.Random(7);
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / len;
            float env = Mathf.Exp(-t * 22f);
            float ring = Mathf.Sin(2 * Mathf.PI * 1200f * i / sr) * 0.4f
                       + Mathf.Sin(2 * Mathf.PI * 2400f * i / sr) * 0.2f;
            float noise = (float)(rng.NextDouble() * 2 - 1) * 0.4f;
            data[i] = (ring + noise) * env;
        }
        var clip = AudioClip.Create("Metal_Synth", len, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip GenImpactoSuelo(float freq)
    {
        int sr = 44100; int len = (int)(sr * 0.1f);
        float[] data = new float[len];
        var rng = new System.Random(13);
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / len;
            float env = Mathf.Exp(-t * 30f);
            data[i] = ((float)(rng.NextDouble()*2-1)*0.7f
                      + Mathf.Sin(2*Mathf.PI*freq*i/sr)*0.3f) * env;
        }
        var clip = AudioClip.Create("Suelo_Synth", len, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip GenPaso(float freq, float duracion)
    {
        int sr = 44100; int len = (int)(sr * duracion);
        float[] data = new float[len];
        var rng = new System.Random(3);
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / len;
            float env = Mathf.Exp(-t * 40f);
            data[i] = ((float)(rng.NextDouble()*2-1)*0.8f
                      + Mathf.Sin(2*Mathf.PI*freq*i/sr)*0.2f) * env * 0.6f;
        }
        var clip = AudioClip.Create("Paso_Synth", len, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip GenPasoGrava()
    {
        int sr = 44100; int len = (int)(sr * 0.12f);
        float[] data = new float[len];
        var rng = new System.Random(17);
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / len;
            float env = Mathf.Exp(-t * 25f) * (1f - Mathf.Exp(-t * 100f));
            // Múltiples clicks aleatorios = grava
            float crunch = 0f;
            for (int k = 0; k < 6; k++)
            {
                float offset = k * len / 6f;
                float d = Mathf.Abs(i - offset) / (len / 12f);
                crunch += (float)(rng.NextDouble()*2-1) * Mathf.Exp(-d*d * 8f);
            }
            data[i] = crunch * env * 0.5f;
        }
        var clip = AudioClip.Create("Grava_Synth", len, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip GenSirena()
    {
        // Sirena oscilante 700-1200 Hz a 1 Hz de modulación
        float duracion = 2f;
        int sr = 44100; int len = (int)(sr * duracion);
        float[] data = new float[len];
        float phase = 0f;
        for (int i = 0; i < len; i++)
        {
            float t    = (float)i / sr;
            float freq = 700f + 500f * (0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * 1f * t));
            phase += 2 * Mathf.PI * freq / sr;
            data[i] = Mathf.Sin(phase) * 0.7f;
        }
        var clip = AudioClip.Create("Sirena_Synth", len, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip GenClick()
    {
        int sr = 44100; int len = (int)(sr * 0.04f);
        float[] data = new float[len];
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / len;
            data[i] = Mathf.Sin(2*Mathf.PI*800f*i/sr) * Mathf.Exp(-t * 60f) * 0.5f;
        }
        var clip = AudioClip.Create("Click_Synth", len, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip GenNotificacion(float freq)
    {
        float dur = 0.35f;
        int sr = 44100; int len = (int)(sr * dur);
        float[] data = new float[len];
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / len;
            float env = Mathf.Exp(-t * 5f) * (1f - Mathf.Exp(-t * 80f));
            data[i] = Mathf.Sin(2*Mathf.PI*freq*i/sr) * env * 0.6f
                    + Mathf.Sin(2*Mathf.PI*freq*1.5f*i/sr) * env * 0.2f;
        }
        var clip = AudioClip.Create("Notif_Synth", len, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip GenChirrido()
    {
        float dur = 0.8f;
        int sr = 44100; int len = (int)(sr * dur);
        float[] data = new float[len];
        var rng = new System.Random(5);
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / len;
            float freq = 3000f - 2000f * t;
            float env = Mathf.Exp(-t * 4f) * (1f - Mathf.Exp(-t * 50f));
            float noise = (float)(rng.NextDouble()*2-1) * 0.3f;
            data[i] = (Mathf.Sin(2*Mathf.PI*freq*i/sr)*0.7f + noise) * env;
        }
        var clip = AudioClip.Create("Chirrido_Synth", len, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    void CrearFuentesPersistentes()
    {
        var goAmb = new GameObject("SrcAmbiente"); goAmb.transform.SetParent(transform);
        _srcAmbiente = goAmb.AddComponent<AudioSource>();
        _srcAmbiente.loop = true; _srcAmbiente.spatialBlend = 0f; _srcAmbiente.volume = 0f;

        var goSir = new GameObject("SrcSirena"); goSir.transform.SetParent(transform);
        _srcSirena = goSir.AddComponent<AudioSource>();
        _srcSirena.loop = true; _srcSirena.spatialBlend = 0.3f; _srcSirena.volume = 0f;
        if (_clips.TryGetValue(Clip.Sirena, out var sClip)) { _srcSirena.clip = sClip; }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  API PÚBLICA
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Reproduce un clip en 3D o 2D según configuración.</summary>
    public static void Play(Clip c, Vector3 pos = default)
    {
        if (I == null) return;
        if (!I._clips.TryGetValue(c, out var clip)) return;

        var src = I.ObtenerFuente();
        if (src == null) return;

        src.clip         = clip;
        src.spatialBlend = I._is3D.GetValueOrDefault(c, false) ? 1f : 0f;
        src.volume       = I.VolumenEfectivo(c);
        src.transform.position = pos == default ? (Camera.main?.transform.position ?? Vector3.zero) : pos;
        src.pitch = Random.Range(0.93f, 1.07f); // variación leve
        src.Play();
    }

    /// <summary>Reproduce en loop (ambiente). Devuelve la fuente para control.</summary>
    public static AudioSource PlayLoop(Clip c, float fadeIn = 1f)
    {
        if (I == null) return null;
        if (!I._clips.TryGetValue(c, out var clip)) return null;
        var src = I.ObtenerFuente();
        if (src == null) return null;
        src.clip = clip; src.loop = true; src.volume = 0f;
        src.spatialBlend = 0f; src.Play();
        I.StartCoroutine(I.FadeVolumen(src, I.VolumenEfectivo(c), fadeIn));
        return src;
    }

    public static void Stop(AudioSource src, float fadeOut = 0.5f)
    {
        if (I == null || src == null) return;
        I.StartCoroutine(I.FadeYDetener(src, fadeOut));
    }

    /// <summary>Sirena de policía on/off.</summary>
    public static void SetSirena(bool activo)
    {
        if (I == null || I._srcSirena == null) return;
        if (activo && !I._srcSirena.isPlaying) I._srcSirena.Play();
        float targetVol = activo ? I.VolumenEfectivo(Clip.Sirena) : 0f;
        I.StartCoroutine(I.FadeVolumen(I._srcSirena, targetVol, 0.5f));
        if (!activo) I.StartCoroutine(I.DetenerTras(I._srcSirena, 0.5f));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    AudioSource ObtenerFuente()
    {
        for (int i = 0; i < POOL_SIZE; i++)
        {
            int idx = (_poolIdx + i) % POOL_SIZE;
            if (!_pool[idx].isPlaying) { _poolIdx = (idx + 1) % POOL_SIZE; return _pool[idx]; }
        }
        // Todas ocupadas: reciclar la más antigua
        var src = _pool[_poolIdx];
        src.Stop();
        _poolIdx = (_poolIdx + 1) % POOL_SIZE;
        return src;
    }

    float VolumenEfectivo(Clip c)
    {
        float base_ = _volumenes.GetValueOrDefault(c, 0.7f);
        float cat   = _categorias.GetValueOrDefault(c, Categoria.SFX) switch
        {
            Categoria.SFX      => SistemaOpciones.VolSFX,
            Categoria.Musica   => SistemaOpciones.VolMusica,
            Categoria.Ambiente => SistemaOpciones.VolAmbiente,
            Categoria.UI       => SistemaOpciones.VolSFX,
            _ => 1f
        };
        return base_ * cat * SistemaOpciones.VolMaster;
    }

    IEnumerator FadeVolumen(AudioSource src, float target, float dur)
    {
        float start = src.volume, t = 0f;
        while (t < dur && src != null) { t += Time.unscaledDeltaTime; src.volume = Mathf.Lerp(start, target, t/dur); yield return null; }
        if (src != null) src.volume = target;
    }

    IEnumerator FadeYDetener(AudioSource src, float dur)
    {
        yield return FadeVolumen(src, 0f, dur);
        if (src != null) { src.Stop(); src.loop = false; }
    }

    IEnumerator DetenerTras(AudioSource src, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        if (src != null) src.Stop();
    }

    // ── Suscripción a eventos del juego ───────────────────────────────────
    // Handlers con nombre para poder desuscribir correctamente
    void OnDano(int _)      => Play(Clip.DanoRecibido);
    void OnMisionStart(string _) => Play(Clip.MisionIniciada);
    void OnMisionEnd(string _)   => Play(Clip.MisionCompletada);
    void OnWanted(int nivel)     => SetSirena(nivel >= 2);

    void OnEnable()
    {
        ControladorJugador.OnDanoRecibido     += OnDano;
        GameManagerAltsasua.OnEstrellasCambia += OnWanted;
        SistemaMisiones.OnMisionIniciada      += OnMisionStart;
        SistemaMisiones.OnMisionCompletada    += OnMisionEnd;
    }

    void OnDisable()
    {
        ControladorJugador.OnDanoRecibido     -= OnDano;
        GameManagerAltsasua.OnEstrellasCambia -= OnWanted;
        SistemaMisiones.OnMisionIniciada      -= OnMisionStart;
        SistemaMisiones.OnMisionCompletada    -= OnMisionEnd;
    }
}
