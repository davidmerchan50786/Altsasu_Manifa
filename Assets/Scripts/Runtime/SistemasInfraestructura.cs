// Assets/Scripts/SistemasInfraestructura.cs
// ═══════════════════════════════════════════════════════════════════════════
//  Sistemas de infraestructura con código real:
//    SonidosAmbienteAltsasu · SistemaMusica · SistemaPostProcesoAAA
//    EnemigoPatrulla · SistemaBombas · SistemaBarricadas · BarricadaFuego
//
//  ELIMINADOS (eran stubs vacíos):
//    SistemaFarolas    → SistemaZonas los instancia al cargar cada zona
//    SistemaFerroviario, TrenEnMovimiento → no implementados, sin datos
//    SistemaEdificios  → sustituido por SistemaZonas + GeneradorMundoOSM
//    HUDJugador        → HUDCanvas
//    SistemaTerreno    → SceneBootstrapper ya crea el Terrain desde DEM
//    SistemaLuzHDRP    → SistemaAtmosfera controla la luz solar directamente
//    SistemaPostProcesoAAA.FlashExplosion → redirige a SistemaPolish.Shake
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.AI;
using System.Collections;

// ─────────────────────────────────────────────────────────────────────────────
//  SONIDOS AMBIENTE  (loop de fondo que varía según wanted)
// ─────────────────────────────────────────────────────────────────────────────
public class SonidosAmbienteAltsasu : MonoBehaviour
{
    [Header("Clips de ambiente")]
    public AudioClip clipTrafico;
    public AudioClip clipAves;
    public AudioClip clipViento;

    AudioSource _src;
    float       _volBase = 0.25f;

    void Start()
    {
        _src = gameObject.AddComponent<AudioSource>();
        _src.loop         = true;
        _src.spatialBlend = 0f;
        _src.volume       = _volBase;
        _src.priority     = 256; // baja prioridad — cede ante SFX

        // Intentar cargar clips reales de Resources/Audio
        if (clipTrafico == null) clipTrafico = Resources.Load<AudioClip>("Audio/viento_real");
        if (clipAves    == null) clipAves    = Resources.Load<AudioClip>("Audio/pajaros");
        if (clipViento  == null) clipViento  = Resources.Load<AudioClip>("Audio/viento");

        if (clipAves != null) { _src.clip = clipAves; _src.Play(); }

        AlsasuaLogger.Info("Ambiente", "Sonidos de ambiente iniciados");
    }

    /// <summary>Llamado por GameManagerAltsasua.OnEstrellasCambia.</summary>
    public void ActualizarSegunNivel(int nivelWanted)
    {
        if (_src == null) return;
        // Más tensión a mayor wanted: sube volumen y puede cambiar clip
        float targetVol = Mathf.Lerp(_volBase, 0.55f, nivelWanted / 5f);
        _src.volume = Mathf.MoveTowards(_src.volume, targetVol, Time.deltaTime * 0.5f);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  MÚSICA ADAPTATIVA
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaMusica : MonoBehaviour
{
    public static SistemaMusica Instance { get; private set; }
    [Tooltip("Clip corto que suena al iniciar eventos clave (misiones, explosiones).")]
    public AudioClip stingEvento;

    AudioSource _src;
    AudioSource _srcSting;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        _src      = gameObject.AddComponent<AudioSource>();
        _srcSting = gameObject.AddComponent<AudioSource>();
        _src.spatialBlend      = 0f;
        _srcSting.spatialBlend = 0f;
        _srcSting.playOnAwake  = false;
        AlsasuaLogger.Info("Musica", "Sistema de música adaptativa iniciado");
    }

    /// <summary>Reproduce el sting de evento (one-shot, no interrumpe la música de fondo).</summary>
    public static void TocarEvento(AudioClip clip)
    {
        if (Instance == null || clip == null) return;
        Instance._srcSting.PlayOneShot(clip, 0.7f);
    }

    /// <summary>Fade-in de una pista de fondo.</summary>
    public void TocarFondo(AudioClip clip, float volMax = 0.4f, float fadeTime = 2f)
    {
        if (clip == null) return;
        StartCoroutine(FadeFondo(clip, volMax, fadeTime));
    }

    IEnumerator FadeFondo(AudioClip clip, float volMax, float fadeTime)
    {
        // Fade out del clip actual
        float t = 0f;
        float volInicio = _src.volume;
        while (t < fadeTime * 0.5f)
        {
            t += Time.deltaTime;
            _src.volume = Mathf.Lerp(volInicio, 0f, t / (fadeTime * 0.5f));
            yield return null;
        }
        _src.clip   = clip;
        _src.loop   = true;
        _src.volume = 0f;
        _src.Play();
        // Fade in
        t = 0f;
        while (t < fadeTime * 0.5f)
        {
            t += Time.deltaTime;
            _src.volume = Mathf.Lerp(0f, volMax, t / (fadeTime * 0.5f));
            yield return null;
        }
        _src.volume = volMax;
    }
}

// NOTA: la clase stub 'SistemaPostProcesoAAA' que vivía aquí se ELIMINÓ —
// estaba duplicada con la versión completa de SistemaPostProcesoAAA.cs
// (mismo tipo top-level sin namespace → error de compilación CS0101).
// La versión completa ya expone FlashExplosion/FlashDisparo/SetClimaGrading/
// CambiarEstado, así que todos los llamadores siguen funcionando.

// ─────────────────────────────────────────────────────────────────────────────
//  ENEMIGO PATRULLA  (NPC genérico sin IA compleja — simplificado)
// ─────────────────────────────────────────────────────────────────────────────
public class EnemigoPatrulla : MonoBehaviour
{
    [Header("Config")]
    public int   vida           = 100;
    public float velocidad      = 1.4f;
    public Transform[] waypoints;

    NavMeshAgent _agente;
    int          _wpIdx;
    float        _espera;
    System.Action _onNavListo;     // FIX: guardar el handler para poder desuscribir en OnDestroy

    // Reacción a la manifestación: cuando hay una protesta activa dentro del radio,
    // la patrulla se dirige hacia el foco en lugar de seguir sus waypoints.
    bool    _manifestacionActiva;
    Vector3 _centroManifestacion;
    float   _radioManifestacion = 140f;

    void Start()
    {
        _agente = GetComponent<NavMeshAgent>() ?? gameObject.AddComponent<NavMeshAgent>();
        _agente.speed = velocidad;
        // Esperar a que el NavMesh esté listo
        _agente.enabled = SistemaNavMesh.EstaListo;
        // FIX: handler nombrado que se auto-desuscribe al activarse + OnDestroy de respaldo.
        // Antes: lambda anónima nunca desuscrita → tras morir el enemigo (Destroy a los 12 s)
        // tocaba un NavMeshAgent destruido en cada re-bake → MissingReferenceException acumulada.
        _onNavListo = () =>
        {
            if (_agente != null) _agente.enabled = true;
            SistemaNavMesh.OnNavMeshListo -= _onNavListo;   // ya activado: dejar de escuchar
        };
        SistemaNavMesh.OnNavMeshListo += _onNavListo;

        // Suscripción a eventos de manifestación (una sola vez, no en Update).
        EventBus.Subscribe<ManifestacionIniciadaEvent>(OnManifestacionIniciada);
        EventBus.Subscribe<ManifestacionTerminadaEvent>(OnManifestacionTerminada);
    }

    void OnDestroy()
    {
        if (_onNavListo != null) SistemaNavMesh.OnNavMeshListo -= _onNavListo;
        EventBus.Unsubscribe<ManifestacionIniciadaEvent>(OnManifestacionIniciada);
        EventBus.Unsubscribe<ManifestacionTerminadaEvent>(OnManifestacionTerminada);
    }

    void OnManifestacionIniciada(ManifestacionIniciadaEvent evt)
    {
        _manifestacionActiva  = true;
        _centroManifestacion  = evt.centro;
        if (evt.radio > 0f) _radioManifestacion = evt.radio;
    }

    void OnManifestacionTerminada(ManifestacionTerminadaEvent evt)
    {
        _manifestacionActiva = false;
    }

    void Update()
    {
        if (!_agente.enabled || waypoints == null || waypoints.Length == 0) return;
        if (_espera > 0f) { _espera -= Time.deltaTime; return; }

        // Con manifestación activa y dentro del radio de influencia, converger al foco.
        if (_manifestacionActiva &&
            (transform.position - _centroManifestacion).sqrMagnitude
                <= _radioManifestacion * _radioManifestacion)
        {
            if (!_agente.hasPath || _agente.remainingDistance < 1.5f)
            {
                _agente.SetDestination(_centroManifestacion);
                _espera = Random.Range(0.5f, 1.5f);
            }
            return;
        }

        if (!_agente.hasPath || _agente.remainingDistance < 0.8f)
        {
            _wpIdx = (_wpIdx + 1) % waypoints.Length;
            _agente.SetDestination(waypoints[_wpIdx].position);
            _espera = Random.Range(1f, 3f);
        }
    }

    public void RecibirDano(int cantidad)
    {
        vida -= cantidad;
        if (vida <= 0)
        {
            SistemaRagdoll.Activar(transform, 3f);
            Destroy(gameObject, 12f);
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  BOMBAS MOLOTOV / IED  (colocación con F, detonación con G)
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaBombas : MonoBehaviour
{
    [Header("Parámetros de explosión")]
    public float radioBomba   = 8f;
    public float fuerzaBomba  = 350f;
    public float danoBomba    = 90f;
    [Tooltip("Segundos hasta que la bomba explota sola (0 = solo manual).")]
    public float fuseTimer    = 0f;

    readonly System.Collections.Generic.List<(Vector3 pos, float timer)> _bombas = new();

    void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;
        if (kb.fKey.wasPressedThisFrame) ColocarBomba();
        if (kb.gKey.wasPressedThisFrame) DetonarUltima();

        // Fusible automático
        if (fuseTimer > 0f)
            for (int i = _bombas.Count - 1; i >= 0; i--)
            {
                var b = _bombas[i];
                b.timer += Time.deltaTime;
                _bombas[i] = b;
                if (b.timer >= fuseTimer)
                {
                    Explotar(b.pos);
                    _bombas.RemoveAt(i);
                }
            }
    }

    public void ColocarBomba()
    {
        var j = AltsasuCore.Jugador;
        if (j == null) return;
        _bombas.Add((j.position, 0f));
        AlsasuaLogger.Info("Bombas", $"💣 Bomba #{_bombas.Count} colocada en {j.position:F0}");
        AudioManager.Play(AudioManager.Clip.UIClick);
    }

    public void DetonarUltima()
    {
        if (_bombas.Count == 0) { AlsasuaLogger.Info("Bombas", "Sin bombas colocadas"); return; }
        Explotar(_bombas[^1].pos);
        _bombas.RemoveAt(_bombas.Count - 1);
    }

    public void DetonarTodas()
    {
        foreach (var b in _bombas) Explotar(b.pos);
        _bombas.Clear();
    }

    void Explotar(Vector3 pos)
        => SistemaExplosion.Explotar(pos, radioBomba, fuerzaBomba, danoBomba);
}

// ─────────────────────────────────────────────────────────────────────────────
//  BARRICADAS  (colocación con clic en modo barricada)
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaBarricadas : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject prefabBarricada;
    public GameObject prefabBarricadaMetal;
    public GameObject prefabVFXFuego;

    readonly System.Collections.Generic.List<GameObject> _barricadas = new();

    /// <summary>Coloca una barricada en <paramref name="posicion"/>.</summary>
    public void ColocarBarricada(Vector3 posicion, Quaternion rotacion)
    {
        var prefab = prefabBarricada ?? prefabBarricadaMetal;
        if (prefab == null)
        {
            // Fallback: cubo gris
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.position   = posicion;
            go.transform.rotation   = rotacion;
            go.transform.localScale = new Vector3(2f, 1f, 0.3f);
            go.AddComponent<Rigidbody>().mass = 80f;
            _barricadas.Add(go);
            AlsasuaLogger.Info("Barricadas", $"Barricada colocada (fallback cubo) en {posicion:F0}");
            return;
        }
        var inst = Instantiate(prefab, posicion, rotacion);
        _barricadas.Add(inst);
        AlsasuaLogger.Info("Barricadas", $"Barricada colocada en {posicion:F0}");
    }

    public void PrenderTodas()
    {
        foreach (var b in _barricadas)
            if (b != null) b.GetComponent<BarricadaFuego>()?.PrenderFuego();
    }

    public void DanoArea(Vector3 centro, float radio, int daño)
    {
        foreach (var b in _barricadas)
        {
            if (b == null) continue;
            if (Vector3.Distance(b.transform.position, centro) <= radio)
                b.GetComponent<BarricadaFuego>()?.RecibirDaño(daño);
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  BARRICADA DE FUEGO  (componente de cada barricada individual)
// ─────────────────────────────────────────────────────────────────────────────
public class BarricadaFuego : MonoBehaviour
{
    [SerializeField] float _vida = 200f;
    ParticleSystem _fuego;
    bool _ardiendo;

    public void PrenderFuego()
    {
        if (_ardiendo) return;
        _ardiendo = true;
        _fuego    = GetComponentInChildren<ParticleSystem>();
        if (_fuego == null)
        {
            var go = new GameObject("Fuego");
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.up * 0.5f;
            _fuego = go.AddComponent<ParticleSystem>();
            var main = _fuego.main;
            main.startColor    = new ParticleSystem.MinMaxGradient(new Color(1f, 0.4f, 0f), Color.red);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
            main.startSpeed    = new ParticleSystem.MinMaxCurve(1f, 3f);
            main.startSize     = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            main.loop          = true;
        }
        _fuego.Play();
        AlsasuaLogger.Info("Barricada", $"{name} prendida");
    }

    // Alias con y sin tilde para compatibilidad con código existente
    public void RecibirDano(float cantidad) => RecibirDaño(cantidad);
    public void RecibirDaño(float cantidad)
    {
        _vida -= cantidad;
        if (_vida <= 0)
        {
            if (_fuego != null) _fuego.Stop();
            Destroy(gameObject, 0.5f);
        }
    }
}
