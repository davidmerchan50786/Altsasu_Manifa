// Assets/Scripts/SistemaMusicaAdaptativa.cs
// ═══════════════════════════════════════════════════════════════════════════
//  MÚSICA ADAPTATIVA POR TENSIÓN (El Narrador / Pilar Audio — Blueprint AAA+++)
//
//  Cruza tres capas de música según la "tensión" del juego, derivada del nivel
//  de búsqueda (IWantedSystem). Sin acoplarse a nada: lee el servicio vía
//  ServiceLocator y crea sus propios AudioSources 2D en runtime.
//
//    tensión 0.0 ─ calma          (capaCalma)
//    tensión 0.4 ─ alerta/tensión (capaTension, en banda)
//    tensión 0.9 ─ persecución    (capaPersecucion)
//
//  Degradación elegante: si no hay clips asignados, registra un aviso y no hace
//  nada dañino. Respeta el volumen de música del usuario (SistemaOpciones.VolMusica).
//
//  Patrón AAA: este sistema es un *consumidor* de señal (NivelBusqueda); no
//  modifica el estado del juego. Otros sistemas pueden leer TensionActual.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(40)]
public class SistemaMusicaAdaptativa : MonoBehaviour
{
    public static SistemaMusicaAdaptativa Instance { get; private set; }

    [Header("Capas de música (loops). Null = capa desactivada).")]
    public AudioClip clipCalma;
    public AudioClip clipTension;
    public AudioClip clipPersecucion;

    [Header("Ajustes")]
    [Tooltip("Nivel de búsqueda que equivale a tensión máxima (1.0).")]
    [Range(1, 8)] public int nivelBusquedaMax = 5;
    [Tooltip("Volumen base de la música antes de aplicar el volumen del usuario.")]
    [Range(0f, 1f)] public float volumenBase = 0.7f;
    [Tooltip("Velocidad de crossfade entre capas (mayor = más rápido).")]
    public float velocidadCrossfade = 1.5f;
    [Tooltip("Segundos entre evaluaciones de tensión.")]
    public float intervaloEval = 0.5f;

    /// <summary>Tensión actual normalizada 0..1 (derivada del nivel de búsqueda). Lectura pública.</summary>
    public static float TensionActual => Instance != null ? Instance._tension : 0f;

    AudioSource _srcCalma, _srcTension, _srcPersecucion;
    float _tension;          // 0..1 suavizada
    float _tensionObjetivo;  // 0..1 cruda

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start() => StartCoroutine(InicializarTras(4f));

    IEnumerator InicializarTras(float d)
    {
        yield return new WaitForSeconds(d);

        if (clipCalma == null && clipTension == null && clipPersecucion == null)
        {
            AlsasuaLogger.Warn("MusicaAdapt",
                "Sin clips asignados — música adaptativa inactiva. Asigna clipCalma/Tension/Persecucion en el Inspector.");
            yield break;
        }

        _srcCalma       = CrearCapa("Mus_Calma",       clipCalma);
        _srcTension     = CrearCapa("Mus_Tension",     clipTension);
        _srcPersecucion = CrearCapa("Mus_Persecucion", clipPersecucion);

        StartCoroutine(BucleTension());
        AlsasuaLogger.Info("MusicaAdapt", "Música adaptativa lista (3 capas).");
    }

    AudioSource CrearCapa(string nombre, AudioClip clip)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.clip          = clip;
        src.loop          = true;
        src.playOnAwake   = false;
        src.spatialBlend  = 0f;     // 2D — la música no se atenúa con la distancia
        src.volume        = 0f;
        src.ignoreListenerPause = true;
        if (clip != null) src.Play();   // arranca en silencio; el crossfade lo sube
        return src;
    }

    IEnumerator BucleTension()
    {
        var espera = new WaitForSeconds(intervaloEval);
        while (true)
        {
            int wanted = ServiceLocator.Get<IWantedSystem>()?.NivelBusqueda ?? 0;
            _tensionObjetivo = Mathf.Clamp01(wanted / (float)Mathf.Max(1, nivelBusquedaMax));
            yield return espera;
        }
    }

    void Update()
    {
        if (_srcCalma == null) return; // aún no inicializado o sin clips

        // Suavizar la tensión hacia su objetivo
        _tension = Mathf.MoveTowards(_tension, _tensionObjetivo, velocidadCrossfade * Time.deltaTime * 0.6f);

        float volUsuario = volumenBase * SistemaOpciones.VolMusica;

        // Pesos por capa (crossfade en bandas suaves)
        float wCalma       = 1f - SS(0.0f, 0.40f, _tension);
        float wPersecucion = SS(0.50f, 0.90f, _tension);
        float wTension     = SS(0.05f, 0.40f, _tension) * (1f - SS(0.55f, 0.90f, _tension));

        float k = velocidadCrossfade * Time.deltaTime;
        AplicarVolumen(_srcCalma,       wCalma       * volUsuario, k);
        AplicarVolumen(_srcTension,     wTension     * volUsuario, k);
        AplicarVolumen(_srcPersecucion, wPersecucion * volUsuario, k);
    }

    static void AplicarVolumen(AudioSource src, float objetivo, float k)
    {
        if (src == null || src.clip == null) return;
        src.volume = Mathf.Lerp(src.volume, objetivo, k);
    }

    // Smoothstep estándar entre dos bordes (Mathf.SmoothStep de Unity interpola valores,
    // no es el smoothstep(edge0,edge1,x) clásico → se compone con InverseLerp).
    static float SS(float edge0, float edge1, float x)
        => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, x));

    void OnDestroy() { if (Instance == this) Instance = null; }
}
