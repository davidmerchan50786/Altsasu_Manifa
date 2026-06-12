// SistemaCargasPoliciales.cs — Director de cargas antidisturbios
// ═══════════════════════════════════════════════════════════════════════════
//  Capa GAMEPLAY. Coordina cargas de la Guardia contra la manifestación
//  activa, en oleadas con aviso previo (silbato/megáfono) para dar ventana
//  de reacción al jugador y a los boids.
//
//  No toca PoliciaForalIA (esa es IA individual contra el jugador): esto es
//  la capa de orden público. Escuadra propia, visual simple (prefab o
//  fallback), empuje físico y eventos para SistemaMoralManifestacion.
//
//  Escalada: la intensidad crece con la duración de la manifestación y con
//  el nivel de Se Busca (IWantedSystem). Con Joni de aliado (rep. futura),
//  las cargas se anuncian antes — gancho preparado para misiones.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SistemaCargasPoliciales : SingletonMono<SistemaCargasPoliciales>
{
    protected override bool DestroyGameObjectOnDuplicate => true;

    [Header("Escuadra")]
    [SerializeField] GameObject prefabAntidisturbio;   // si null: fallback cápsulas
    [Range(4, 24)] [SerializeField] int tamEscuadra = 10;
    [Tooltip("Separación lateral entre agentes en la línea (m)")]
    [SerializeField] float separacionLinea = 1.6f;

    [Header("Tempo de oleadas")]
    [Tooltip("Segundos desde el inicio de la manifestación hasta la primera carga")]
    [SerializeField] float primeraCarga = 120f;
    [Tooltip("Segundos entre cargas sucesivas")]
    [SerializeField] float entreCargas = 90f;
    [Tooltip("Segundos de aviso (silbato) antes de cada carga")]
    [SerializeField] float segundosAviso = 6f;

    [Header("Carga")]
    [Tooltip("Distancia desde la que se forma la línea (m del centro)")]
    [SerializeField] float distanciaFormacion = 60f;
    [Tooltip("Metros que avanza la línea en cada carga")]
    [SerializeField] float profundidadCarga = 35f;
    [SerializeField] float velocidadCarga = 6f;
    [SerializeField] float velocidadRepliegue = 2f;

    [Header("Audio")]
    [SerializeField] AudioClip clipSilbato;
    [SerializeField] AudioClip clipCarga;      // botas, escudos, gritos

    readonly List<Transform> _escuadra = new();
    Coroutine _crDirector;
    AudioSource _src;
    float _inicioManifa;
    int _numCarga;

    void Start()
    {
        _src = gameObject.AddComponent<AudioSource>();
        _src.spatialBlend = 1f;
        _src.maxDistance = 120f;
    }

    void OnEnable()
    {
        EventBus.Subscribe<MoralManifestacionEvent>(OnMoral);
        EventBus.Subscribe<ManifestacionTerminadaEvent>(OnFinManifa);
    }

    void OnDisable()
    {
        EventBus.Unsubscribe<MoralManifestacionEvent>(OnMoral);
        EventBus.Unsubscribe<ManifestacionTerminadaEvent>(OnFinManifa);
    }

    // ── API ──────────────────────────────────────────────────────────────────

    /// <summary>Llamar al iniciar la manifestación (junto a PrepararConvocatoria).</summary>
    public void ActivarDispositivo()
    {
        if (_crDirector != null) StopCoroutine(_crDirector);
        _inicioManifa = Time.time;
        _numCarga = 0;
        _crDirector = StartCoroutine(Director());
    }

    public void DesactivarDispositivo()
    {
        if (_crDirector != null) { StopCoroutine(_crDirector); _crDirector = null; }
        LimpiarEscuadra();
    }

    // ── Director de oleadas ──────────────────────────────────────────────────

    IEnumerator Director()
    {
        yield return new WaitForSeconds(primeraCarga);

        var manifa = SistemaManifestacion.Instance;
        while (manifa != null && manifa.EnCurso)
        {
            yield return StartCoroutine(EjecutarCarga(manifa));
            yield return new WaitForSeconds(entreCargas);
        }
        LimpiarEscuadra();
    }

    IEnumerator EjecutarCarga(SistemaManifestacion manifa)
    {
        _numCarga++;

        // Intensidad: escala con nº de carga y nivel de Se Busca
        int wanted = ServiceLocator.Get<IWantedSystem>()?.NivelBusqueda ?? 0;
        float intensidad = Mathf.Clamp01(0.4f + _numCarga * 0.15f + wanted * 0.05f);

        // Origen: lado aleatorio de la multitud, a distancia de formación
        Vector3 centro = manifa.centroManifestacion;
        float angulo = Random.Range(0f, Mathf.PI * 2f);
        Vector3 lado = new Vector3(Mathf.Cos(angulo), 0f, Mathf.Sin(angulo));
        Vector3 origen = centro + lado * distanciaFormacion;
        if (Terrain.activeTerrain != null)
            origen.y = Terrain.activeTerrain.SampleHeight(origen) + 0.1f;
        Vector3 dirCarga = -lado;   // hacia la multitud

        // 1. Formar la línea
        AsegurarEscuadra();
        Vector3 lateral = Vector3.Cross(Vector3.up, dirCarga);
        for (int i = 0; i < _escuadra.Count; i++)
        {
            float off = (i - _escuadra.Count / 2f) * separacionLinea;
            var pos = origen + lateral * off;
            if (Terrain.activeTerrain != null)
                pos.y = Terrain.activeTerrain.SampleHeight(pos) + 1f;
            _escuadra[i].position = pos;
            _escuadra[i].rotation = Quaternion.LookRotation(dirCarga);
            _escuadra[i].gameObject.SetActive(true);
        }

        // 2. Aviso (ventana de reacción)
        EventBus.Publish(new AvisoCargaPolicialEvent
        {
            origen = origen,
            segundosHastaCarga = segundosAviso
        });
        ReproducirEn(origen, clipSilbato);
        yield return new WaitForSeconds(segundosAviso);

        // 3. CARGA — la línea avanza; el evento hace huir a los boids
        EventBus.Publish(new CargaPolicialEvent
        {
            origen = origen,
            direccion = dirCarga,
            intensidad = intensidad
        });
        ReproducirEn(origen, clipCarga);

        float avanzado = 0f;
        while (avanzado < profundidadCarga)
        {
            float paso = velocidadCarga * Time.deltaTime;
            avanzado += paso;
            for (int i = 0; i < _escuadra.Count; i++)
                _escuadra[i].position += dirCarga * paso;
            yield return null;
        }

        // 4. Repliegue lento a la posición de formación
        while (avanzado > 0f)
        {
            float paso = velocidadRepliegue * Time.deltaTime;
            avanzado -= paso;
            for (int i = 0; i < _escuadra.Count; i++)
                _escuadra[i].position -= dirCarga * paso;
            yield return null;
        }

        for (int i = 0; i < _escuadra.Count; i++)
            _escuadra[i].gameObject.SetActive(false);
    }

    // ── Reacciones ───────────────────────────────────────────────────────────

    void OnMoral(MoralManifestacionEvent evt)
    {
        // Multitud ya muy tocada → la Guardia aprieta: acorta el descanso entre cargas
        if (evt.moral < 30f && entreCargas > 45f) entreCargas = 45f;
    }

    void OnFinManifa(ManifestacionTerminadaEvent evt) => DesactivarDispositivo();

    // ── Escuadra (pool, no Instantiate por carga) ────────────────────────────

    void AsegurarEscuadra()
    {
        if (_escuadra.Count >= tamEscuadra) return;

        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", new Color(0.08f, 0.10f, 0.18f)); // azul antidisturbios
        mpb.SetColor("_Color",     new Color(0.08f, 0.10f, 0.18f));

        for (int i = _escuadra.Count; i < tamEscuadra; i++)
        {
            GameObject go;
            if (prefabAntidisturbio != null)
            {
                go = Instantiate(prefabAntidisturbio, transform);
            }
            else
            {
                // Fallback estilo proyecto: cápsula + escudo (cubo aplanado)
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.transform.SetParent(transform);
                go.GetComponent<MeshRenderer>().SetPropertyBlock(mpb);
                var escudo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                escudo.transform.SetParent(go.transform);
                escudo.transform.localPosition = new Vector3(0, 0, 0.4f);
                escudo.transform.localScale = new Vector3(0.8f, 1.4f, 0.08f);
                escudo.GetComponent<MeshRenderer>().SetPropertyBlock(mpb);
            }
            go.name = $"Antidisturbio_{i}";
            go.SetActive(false);
            _escuadra.Add(go.transform);
        }
    }

    void LimpiarEscuadra()
    {
        for (int i = 0; i < _escuadra.Count; i++)
            if (_escuadra[i] != null) _escuadra[i].gameObject.SetActive(false);
    }

    void ReproducirEn(Vector3 pos, AudioClip clip)
    {
        if (clip == null || _src == null) return;
        _src.transform.position = pos;
        _src.PlayOneShot(clip);
    }

    protected override void OnDestroyed()
    {
        if (_crDirector != null) StopCoroutine(_crDirector);
    }
}
