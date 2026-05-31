// Assets/Scripts/SistemaMusica.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE MÚSICA Y ATMÓSFERA SONORA — AAA
//
//  Sistema de audio dinámico por capas:
//   · Música ambiental que varía con el nivel de búsqueda (wanted)
//   · Stingers de eventos (pelea, explosión, persecución)
//   · Atmósfera sonora por zona (bosque, ciudad, río, montaña)
//   · Transiciones suaves con crossfade
//
//  Arquitectura:
//   · 3 canales de audio paralelos (base + capa + evento)
//   · Crossfade automático entre estados
//   · Mezcla de volúmenes por distancia de zona
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using UnityEngine;

public class SistemaMusica : MonoBehaviour
{
    public static SistemaMusica Instance { get; private set; }

    [Header("Fuentes de audio (se crean automáticamente)")]
    public AudioSource fuenteBase;
    public AudioSource fuenteCapa;
    public AudioSource fuenteEvento;
    public AudioSource fuenteAmbiente;

    [Header("Clips de música (asignar en Inspector o cargar desde Resources)")]
    [Tooltip("Música tranquila — sin búsqueda")]
    public AudioClip musicaPaz;
    [Tooltip("Música tensión — 1-2 estrellas")]
    public AudioClip musicaTension;
    [Tooltip("Música persecución — 3-5 estrellas")]
    public AudioClip musicaPersecucion;
    [Tooltip("Sting de evento (explosión, muerte)")]
    public AudioClip stingEvento;
    [Tooltip("Loop de manifestación")]
    public AudioClip musicaManifestacion;

    [Header("Ambiente sonoro")]
    public AudioClip ambiPajaro;
    public AudioClip ambiRio;
    public AudioClip ambiViento;
    public AudioClip ambiTrafico;
    public AudioClip ambiSirena;

    [Header("Volúmenes")]
    [Range(0f, 1f)] public float volumenMusica  = 0.45f;
    [Range(0f, 1f)] public float volumenAmbiente = 0.6f;
    [Range(0f, 1f)] public float volumenEvento   = 0.8f;

    [Header("Crossfade")]
    public float tiempoCrossfade = 2.5f;

    // ── Estado interno ─────────────────────────────────────────────────────
    int   _nivelAnterior = -1;
    bool  _manifestacionActiva;
    float _timerZona;
    AudioClip _clipActual;

    // Zona sonora actual
    enum ZonaSonora { Ciudad, Bosque, Rio, Montana, Carretera }
    ZonaSonora _zona = ZonaSonora.Ciudad;

    GameManagerAltsasua _gm;
    SistemaManifestacion _manifa;

    // =========================================================================
    //  LIFECYCLE
    // =========================================================================

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        CrearFuentes();
        _gm     = FindFirstObjectByType<GameManagerAltsasua>();
        _manifa = FindFirstObjectByType<SistemaManifestacion>();

        // Iniciar con música tranquila
        StartCoroutine(FadeIn(fuenteBase, musicaPaz, volumenMusica));
        StartCoroutine(IniciarAmbiente());

        AlsasuaLogger.Info("SistemaMusica", "✓ Música y atmósfera sonora iniciadas.");
    }

    void Update()
    {
        _gm ??= FindFirstObjectByType<GameManagerAltsasua>();

        ActualizarMusicaPorWanted();
        ActualizarZonaSonora();
    }

    // =========================================================================
    //  FUENTES DE AUDIO
    // =========================================================================

    void CrearFuentes()
    {
        fuenteBase     ??= CrearFuente("Music_Base",     true,  volumenMusica);
        fuenteCapa     ??= CrearFuente("Music_Capa",     true,  0f);
        fuenteEvento   ??= CrearFuente("Music_Evento",   false, volumenEvento);
        fuenteAmbiente ??= CrearFuente("Ambiente_Loop",  true,  volumenAmbiente);
    }

    AudioSource CrearFuente(string nombre, bool loop, float vol)
    {
        var go  = new GameObject(nombre);
        go.transform.SetParent(transform);
        var src = go.AddComponent<AudioSource>();
        src.loop              = loop;
        src.volume            = vol;
        src.spatialBlend      = 0f;  // 2D
        src.playOnAwake       = false;
        src.reverbZoneMix     = 0.3f;
        return src;
    }

    // =========================================================================
    //  MÚSICA POR NIVEL DE BÚSQUEDA
    // =========================================================================

    void ActualizarMusicaPorWanted()
    {
        int nivel = _gm != null ? _gm.nivelBusqueda : 0;

        // Manifestación activa → música especial
        bool manif = _manifa != null && _manifa.EnCurso;
        if (manif != _manifestacionActiva)
        {
            _manifestacionActiva = manif;
            if (manif && musicaManifestacion != null)
                StartCoroutine(Crossfade(fuenteBase, musicaManifestacion, tiempoCrossfade));
            else if (!manif)
                StartCoroutine(Crossfade(fuenteBase, musicaPaz, tiempoCrossfade));
            return;
        }

        if (nivel == _nivelAnterior) return;
        _nivelAnterior = nivel;

        AudioClip nuevo = nivel switch {
            0     => musicaPaz,
            1 or 2 => musicaTension,
            _     => musicaPersecucion
        };

        if (nuevo != null && nuevo != _clipActual)
        {
            _clipActual = nuevo;
            StartCoroutine(Crossfade(fuenteBase, nuevo, tiempoCrossfade));
        }
    }

    // =========================================================================
    //  ZONA SONORA (según posición del jugador)
    // =========================================================================

    void ActualizarZonaSonora()
    {
        _timerZona -= Time.deltaTime;
        if (_timerZona > 0f) return;
        _timerZona = 5f; // revisar cada 5 segundos

        var jugador = AltsasuCore.Jugador;
        if (jugador == null) return;

        Vector3 pos = jugador.position;
        ZonaSonora nuevaZona = DeterminarZona(pos);

        if (nuevaZona != _zona)
        {
            _zona = nuevaZona;
            StartCoroutine(TransicionAmbiente(nuevaZona));
        }
    }

    ZonaSonora DeterminarZona(Vector3 pos)
    {
        // Distancia al centro urbano (Herriko Plaza)
        float dx = pos.x - 1918f;
        float dz = pos.z - 8570f;
        float distCentro = Mathf.Sqrt(dx * dx + dz * dz);

        float altNeta = pos.y; // altura Unity (origen = 305m snm)

        if (distCentro < 500f)   return ZonaSonora.Ciudad;
        if (altNeta > 450f)      return ZonaSonora.Montana;
        if (GeoDataAlsasua.ZonasBosque != null)
        {
            foreach (var z in GeoDataAlsasua.ZonasBosque)
            {
                float cx = z.Centro.x + 1918f;
                float cz = z.Centro.z + 8570f;
                float bx = pos.x - cx;
                float bz = pos.z - cz;
                if (bx*bx + bz*bz < z.Radio * z.Radio) return ZonaSonora.Bosque;
            }
        }
        // Proximidad al río Arakil (≈ X:1963, Z:8215)
        float drx = pos.x - 1963f; float drz = pos.z - 8215f;
        if (drx*drx + drz*drz < 200f * 200f) return ZonaSonora.Rio;

        return ZonaSonora.Carretera;
    }

    IEnumerator TransicionAmbiente(ZonaSonora zona)
    {
        // Fade out ambiente actual
        yield return StartCoroutine(FadeOut(fuenteAmbiente, 1.5f));

        AudioClip clip = zona switch {
            ZonaSonora.Bosque    => ambiPajaro,
            ZonaSonora.Rio       => ambiRio,
            ZonaSonora.Montana   => ambiViento,
            ZonaSonora.Carretera => ambiTrafico,
            _                    => ambiTrafico, // Ciudad
        };

        if (clip != null)
            yield return StartCoroutine(FadeIn(fuenteAmbiente, clip, volumenAmbiente));
    }

    IEnumerator IniciarAmbiente()
    {
        yield return new WaitForSeconds(1f);
        if (ambiPajaro != null)
            yield return StartCoroutine(FadeIn(fuenteAmbiente, ambiPajaro, volumenAmbiente * 0.5f));
        else if (ambiTrafico != null)
            yield return StartCoroutine(FadeIn(fuenteAmbiente, ambiTrafico, volumenAmbiente));
    }

    // =========================================================================
    //  HELPERS FADE
    // =========================================================================

    IEnumerator FadeIn(AudioSource src, AudioClip clip, float targetVol)
    {
        if (src == null || clip == null) yield break;
        src.clip   = clip;
        src.volume = 0f;
        src.Play();
        float t = 0f;
        while (t < tiempoCrossfade)
        {
            t += Time.deltaTime;
            src.volume = Mathf.Lerp(0f, targetVol, t / tiempoCrossfade);
            yield return null;
        }
        src.volume = targetVol;
    }

    IEnumerator FadeOut(AudioSource src, float duracion)
    {
        if (src == null) yield break;
        float inicio = src.volume;
        float t = 0f;
        while (t < duracion)
        {
            t += Time.deltaTime;
            src.volume = Mathf.Lerp(inicio, 0f, t / duracion);
            yield return null;
        }
        src.Stop();
        src.volume = 0f;
    }

    IEnumerator Crossfade(AudioSource src, AudioClip nuevo, float dur)
    {
        yield return StartCoroutine(FadeOut(src, dur * 0.5f));
        yield return StartCoroutine(FadeIn(src,  nuevo, volumenMusica));
    }

    // =========================================================================
    //  API PÚBLICA
    // =========================================================================

    public static void TocarEvento(AudioClip clip)
    {
        if (Instance == null || Instance.fuenteEvento == null || clip == null) return;
        Instance.fuenteEvento.PlayOneShot(clip, Instance.volumenEvento);
    }

    public static void TocarSirena()
    {
        if (Instance == null || Instance.ambiSirena == null) return;
        Instance.fuenteEvento.PlayOneShot(Instance.ambiSirena, 0.7f);
    }

    static new T FindFirstObjectByType<T>() where T : UnityEngine.Object
        => UnityEngine.Object.FindFirstObjectByType<T>();
}
