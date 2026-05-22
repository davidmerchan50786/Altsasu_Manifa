// SistemaManifestacion.cs
// Manifestación multitudinaria con 100+ participantes.
// Dos grupos: manifestantes pacíficos + grupo de disturbios.
// Barricadas dinámicas, fuego, cócteles molotov.
// Influye en la barra de apoyo popular.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Collections;
using Unity.Mathematics;

public class SistemaManifestacion : MonoBehaviour
{
    // ── Boids coordinator ─────────────────────────────────────────────────
    readonly List<ManifestanteIA> _agentes = new();
    NativeArray<float3> _bPosiciones;
    NativeArray<float3> _bVelocidades;
    bool _boidsActivo;
    float _timerBoids;
    const float BOIDS_TICK = 0.1f; // 10 Hz

    public void RegistrarAgente(ManifestanteIA agente) => _agentes.Add(agente);
    public void DesregistrarAgente(ManifestanteIA agente) => _agentes.Remove(agente);

    void Update()
    {
        // Tecla M para alternar manifestación
        if (UnityEngine.InputSystem.Keyboard.current?.mKey.wasPressedThisFrame == true)
        {
            if (_activa) TerminarManifestacion();
            else StartCoroutine(IniciarManifestacion());
        }

        // Tick Boids a 10Hz
        if (!_activa || _agentes.Count < 2) return;
        _timerBoids -= Time.deltaTime;
        if (_timerBoids > 0) return;
        _timerBoids = BOIDS_TICK;
        TickBoids();
    }

    void TickBoids()
    {
        int n = _agentes.Count;
        var pos = new NativeArray<float3>(n, Allocator.TempJob);
        var vel = new NativeArray<float3>(n, Allocator.TempJob);

        for (int i = 0; i < n; i++)
        {
            if (_agentes[i] == null) continue;
            var p = _agentes[i].transform.position;
            var v = _agentes[i].VelocidadBoids;
            pos[i] = new float3(p.x, p.y, p.z);
            vel[i] = new float3(v.x, v.y, v.z);
        }

        var cfg = _agentes[0].tipo == TipoManifestante.Disturbios
            ? IntegradorMatematicas.BOIDS_DISTURBIOS
            : IntegradorMatematicas.BOIDS_MANIFESTANTE;

        var (nPos, nVel) = IntegradorMatematicas.TickBoids(pos, vel,
            centroManifestacion, cfg, BOIDS_TICK);

        for (int i = 0; i < n; i++)
            if (_agentes[i] != null)
                _agentes[i].AplicarBoids(
                    new Vector3(nPos[i].x, nPos[i].y, nPos[i].z),
                    new Vector3(nVel[i].x, nVel[i].y, nVel[i].z));

        pos.Dispose(); vel.Dispose(); nPos.Dispose(); nVel.Dispose();
    }

    void OnDestroy()
    {
        if (_bPosiciones.IsCreated) _bPosiciones.Dispose();
        if (_bVelocidades.IsCreated) _bVelocidades.Dispose();
    }
    // ─── Inspector ─────────────────────────────────────────────────────────
    [Header("Prefabs manifestantes")]
    public GameObject prefabManifestante;   // civil con keffiyeh/pañuelo palestino
    public GameObject prefabEncapuchado;    // con capucha
    public GameObject prefabBanderaEuskadi;

    [Header("Números")]
    [Range(20, 200)] public int numManifestantes = 80;
    [Range(5,  50)]  public int numDisturbios    = 25;

    [Header("Posición central (cerca de Herriko Plaza)")]
    public Vector3 centroManifestacion = new(1808, 0, 8570); // Nafarroa Kalea

    [Header("Barricadas")]
    public GameObject prefabBarricada;     // neumáticos, vallas urbanas
    public int numBarricadas = 6;

    [Header("Efectos")]
    public GameObject prefabFuegoPequeño;  // fuego de barricada
    public GameObject prefabHumo;

    [Header("Audio")]
    public AudioClip clipConsignas;        // "Gora Euskal Herria!" "Askatasuna!"
    public AudioClip clipDisturbios;       // cristales, gritos, fuego

    [Header("Activación")]
    // Tecla M — new Input System
    // [HideInInspector] public KeyCode teclaManifestacion = KeyCode.M; // legacy eliminado
    public bool    activaAlInicio = false;

    // ─── Estado ────────────────────────────────────────────────────────────
    bool     _activa;
    public bool EnCurso => _activa;
    readonly List<ManifestanteIA> _manifestantes = new();
    readonly List<GameObject>     _barricadas    = new();
    AudioSource _srcConsignas, _srcDisturbios;
    SistemaApoyoPopular _apoyo;

    void Start()
    {
        _apoyo = SistemaApoyoPopular.Instance;
        InicializarAudio();
        if (activaAlInicio) StartCoroutine(IniciarManifestacion());
    }

    // ── Inicio de manifestación ───────────────────────────────────────────

    public IEnumerator IniciarManifestacion()
    {
        if (_activa) yield break;
        _activa = true;
        Debug.Log("[Manifestacion] Manifestación iniciada en Nafarroa Kalea.");

        // Calcular altura real
        float y = Terrain.activeTerrain != null
            ? Terrain.activeTerrain.SampleHeight(centroManifestacion) + 0.1f
            : 240f;
        centroManifestacion.y = y;

        // 1. Barricadas
        yield return StartCoroutine(ColocarBarricadas());

        // 2. Manifestantes pacíficos (forman bloque)
        yield return StartCoroutine(SpawnManifestantesPacificos());

        // 3. Grupo de disturbios (más adelante, zona barricadas)
        yield return StartCoroutine(SpawnGrupoDisturbios());

        // 4. Fuego en barricadas
        yield return new WaitForSeconds(30f);
        QuemarBarricadas();

        // Apoyo popular sube
        _apoyo?.SumarApoyo(20f, "Manifestación multitudinaria");

        // Audio
        if (_srcConsignas != null) _srcConsignas.Play();
    }

    IEnumerator ColocarBarricadas()
    {
        // Barricadas en Nafarroa Kalea a intervalos de 30m
        for (int i = 0; i < numBarricadas; i++)
        {
            float zOffset = (i - numBarricadas/2f) * 35f;
            var pos = centroManifestacion + new Vector3(0, 0, zOffset);
            float py = Terrain.activeTerrain != null ? Terrain.activeTerrain.SampleHeight(pos) : 240f;
            pos.y = py;

            GameObject barricada;
            if (prefabBarricada != null)
            {
                barricada = Instantiate(prefabBarricada, pos, Quaternion.Euler(0, Random.Range(-20f, 20f), 0));
            }
            else
            {
                // Fallback: apilar cubos como barricada
                barricada = new GameObject($"Barricada_{i}");
                barricada.transform.position = pos;
                for (int b = 0; b < 4; b++)
                {
                    var cubo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cubo.transform.SetParent(barricada.transform);
                    cubo.transform.localPosition = new Vector3(Random.Range(-1.5f, 1.5f), b * 0.55f, Random.Range(-0.3f, 0.3f));
                    cubo.transform.localScale = new Vector3(1.2f, 0.5f, 0.6f);
                    cubo.GetComponent<MeshRenderer>().material.color = new Color(0.15f, 0.15f, 0.15f); // neumático
                }
            }
            barricada.name = $"Barricada_{i}";
            _barricadas.Add(barricada);
            yield return null;
        }
    }

    IEnumerator SpawnManifestantesPacificos()
    {
        var prefab = prefabManifestante ?? CreateDefaultManifestante(false);
        for (int i = 0; i < numManifestantes; i++)
        {
            // Formar en bloque detrás de las barricadas
            float angle = (i / (float)numManifestantes) * 360f * Mathf.Deg2Rad;
            float radio = Random.Range(8f, 40f);
            var pos = centroManifestacion + new Vector3(Mathf.Cos(angle) * radio, 0, Mathf.Sin(angle) * radio + 30f);
            float py = Terrain.activeTerrain != null ? Terrain.activeTerrain.SampleHeight(pos) : 240f;
            pos.y = py;

            var go = Instantiate(prefab, pos, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
            go.name = $"Manifestante_{i}";
            go.transform.localScale = Vector3.one * Random.Range(0.85f, 1.05f);

            var ia = go.AddComponent<ManifestanteIA>();
            ia.tipo = TipoManifestante.Pacifico;
            ia.centro = centroManifestacion;
            ia.apoyoSistema = _apoyo;
            _manifestantes.Add(ia);

            if (i % 5 == 0) yield return null; // no bloquear
        }
    }

    IEnumerator SpawnGrupoDisturbios()
    {
        var prefab = prefabEncapuchado ?? CreateDefaultManifestante(true);
        for (int i = 0; i < numDisturbios; i++)
        {
            // Grupo de disturbios en la zona de barricadas
            var pos = centroManifestacion + new Vector3(Random.Range(-15f, 15f), 0, Random.Range(-20f, 20f));
            float py = Terrain.activeTerrain != null ? Terrain.activeTerrain.SampleHeight(pos) : 240f;
            pos.y = py;

            var go = Instantiate(prefab, pos, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
            go.name = $"Encapuchado_{i}";

            var ia = go.AddComponent<ManifestanteIA>();
            ia.tipo = TipoManifestante.Disturbios;
            ia.centro = centroManifestacion;
            ia.apoyoSistema = _apoyo;
            _manifestantes.Add(ia);

            if (i % 5 == 0) yield return null;
        }

        if (_srcDisturbios != null) _srcDisturbios.Play();
    }

    void QuemarBarricadas()
    {
        foreach (var b in _barricadas)
        {
            if (b == null) continue;
            // Añadir fuego
            var fuegoGO = prefabFuegoPequeño != null
                ? Instantiate(prefabFuegoPequeño, b.transform.position + Vector3.up, Quaternion.identity)
                : CrearFuegoSimple(b.transform.position + Vector3.up * 0.5f);
            fuegoGO.transform.SetParent(b.transform);
        }
        Debug.Log("[Manifestacion] ¡Barricadas en llamas!");
    }

    void TerminarManifestacion()
    {
        _activa = false;
        foreach (var m in _manifestantes) if (m != null) Destroy(m.gameObject);
        _manifestantes.Clear();
        foreach (var b in _barricadas) if (b != null) Destroy(b);
        _barricadas.Clear();
        if (_srcConsignas) _srcConsignas.Stop();
        if (_srcDisturbios) _srcDisturbios.Stop();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    static GameObject CreateDefaultManifestante(bool encapuchado)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = encapuchado ? "Encapuchado_Template" : "Manifestante_Template";
        var mat = go.GetComponent<MeshRenderer>().material;
        mat.color = encapuchado ? Color.black : new Color(0.3f, 0.5f, 0.8f);
        // Keffiyeh (cubo pequeño sobre la cabeza)
        var kef = GameObject.CreatePrimitive(PrimitiveType.Cube);
        kef.transform.SetParent(go.transform);
        kef.transform.localPosition = new Vector3(0, 0.55f, 0);
        kef.transform.localScale = new Vector3(0.6f, 0.25f, 0.6f);
        kef.GetComponent<MeshRenderer>().material.color = encapuchado ? Color.black : new Color(0.8f, 0.1f, 0.1f); // keffiyeh rojo/negro
        return go;
    }

    static GameObject CrearFuegoSimple(Vector3 pos)
    {
        var go = new GameObject("Fuego_Simple");
        go.transform.position = pos;
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startColor = new ParticleSystem.MinMaxGradient(Color.yellow, Color.red);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
        main.startSize  = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
        main.startLifetime = 1.5f; main.maxParticles = 100;
        var em = ps.emission; em.rateOverTime = 30f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 15;
        ps.Play();
        return go;
    }

    void InicializarAudio()
    {
        if (clipConsignas != null)
        {
            _srcConsignas = gameObject.AddComponent<AudioSource>();
            _srcConsignas.clip = clipConsignas; _srcConsignas.loop = true; _srcConsignas.volume = 0.65f; _srcConsignas.spatialBlend = 0.5f;
        }
        if (clipDisturbios != null)
        {
            _srcDisturbios = gameObject.AddComponent<AudioSource>();
            _srcDisturbios.clip = clipDisturbios; _srcDisturbios.loop = true; _srcDisturbios.volume = 0.5f; _srcDisturbios.spatialBlend = 0.5f;
        }
    }
}

// ── IA de manifestante ────────────────────────────────────────────────────

public enum TipoManifestante { Pacifico, Disturbios }

public class ManifestanteIA : MonoBehaviour
{
    public TipoManifestante    tipo;
    public Vector3             centro;
    public SistemaApoyoPopular apoyoSistema;

    // ── Boids API ─────────────────────────────────────────────────────────
    public Vector3 VelocidadBoids { get; private set; }
    bool _usandoBoids;

    public void AplicarBoids(Vector3 nuevaPos, Vector3 nuevaVel)
    {
        VelocidadBoids = nuevaVel;
        _usandoBoids   = true;
        _objetivo      = nuevaPos; // Boids da la dirección
    }

    Rigidbody _rb;
    float     _timer;
    Vector3   _objetivo;
    bool      _huyendo;
    SistemaManifestacion _sistema;

    void Start()
    {
        _rb = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();
        _rb.mass = 70f; _rb.linearDamping = 8f;
        _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        if (GetComponent<CapsuleCollider>() == null)
        {
            var col = gameObject.AddComponent<CapsuleCollider>();
            col.height = 1.8f; col.radius = 0.3f; col.center = new Vector3(0, 0.9f, 0);
        }
        _sistema = FindFirstObjectByType<SistemaManifestacion>();
        _sistema?.RegistrarAgente(this);
        ElegirObjetivo();
    }

    void OnDestroy() => _sistema?.DesregistrarAgente(this);

    void FixedUpdate()
    {
        _timer -= Time.fixedDeltaTime;
        if (_timer < 0) ElegirObjetivo();

        var jugador = AltsasuCore.Jugador;
        bool jugadorCerca = jugador != null && Vector3.Distance(transform.position, jugador.position) < 8f;

        if (tipo == TipoManifestante.Disturbios && jugadorCerca)
        {
            // Disturbios: si ve al jugador, actúa (tira cosas)
            _objetivo = transform.position; // se queda
        }

        Vector3 moveVel;
        if (_usandoBoids && VelocidadBoids.sqrMagnitude > 0.01f)
        {
            // Usar velocidad calculada por Boids (comportamiento de grupo)
            moveVel = VelocidadBoids;
            moveVel.y = _rb.linearVelocity.y;
        }
        else
        {
            // Fallback: movimiento directo al objetivo
            var dir = (_objetivo - transform.position); dir.y = 0;
            if (dir.magnitude < 0.5f) return;
            float speed = tipo == TipoManifestante.Disturbios ? 2.5f : 1.2f;
            moveVel = dir.normalized * speed;
            moveVel.y = _rb.linearVelocity.y;
        }
        _rb.linearVelocity = Vector3.Lerp(_rb.linearVelocity, moveVel, Time.fixedDeltaTime * 4f);
        var moveDir = new Vector3(moveVel.x, 0, moveVel.z);
        if (moveDir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(moveDir), Time.fixedDeltaTime * 5f);
    }

    void ElegirObjetivo()
    {
        _timer = Random.Range(3f, 10f);
        if (tipo == TipoManifestante.Pacifico)
            _objetivo = centro + new Vector3(Random.Range(-30f, 30f), 0, Random.Range(-15f, 50f));
        else
            _objetivo = centro + new Vector3(Random.Range(-20f, 20f), 0, Random.Range(-25f, 15f));
        float y = Terrain.activeTerrain != null ? Terrain.activeTerrain.SampleHeight(_objetivo) : 240f;
        _objetivo.y = y;
    }
}
