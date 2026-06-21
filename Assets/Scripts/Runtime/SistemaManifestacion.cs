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
using Unity.Jobs;
using Unity.Mathematics;
using URandom = UnityEngine.Random;

public class SistemaManifestacion : SingletonMono<SistemaManifestacion>
{
    // ── Boids coordinator ─────────────────────────────────────────────────
    readonly List<ManifestanteIA> _agentes = new();

    // Arrays persistentes — se reasignan solo cuando cambia n (evita 40+ allocs/s a 10Hz)
    NativeArray<float3> _bPos;
    NativeArray<float3> _bVel;
    NativeArray<float3> _bNuevaPos;
    NativeArray<float3> _bNuevaVel;
    int _bCapacidad; // tamaño actual de los arrays

    float _timerBoids;
    const float BOIDS_TICK = 0.1f; // 10 Hz

    public void RegistrarAgente(ManifestanteIA agente) => _agentes.Add(agente);
    public void DesregistrarAgente(ManifestanteIA agente) => _agentes.Remove(agente);

    void Update()
    {
        if (UnityEngine.InputSystem.Keyboard.current?.mKey.wasPressedThisFrame == true)
        {
            if (_activa) TerminarManifestacion();
            else StartCoroutine(IniciarManifestacion());
        }

        if (!_activa || _agentes.Count < 2) return;
        _timerBoids -= Time.deltaTime;
        if (_timerBoids > 0) return;
        _timerBoids = BOIDS_TICK;
        TickBoids();
    }

    void AsegurarCapacidad(int n)
    {
        // Solo reasignar si n SUPERA la capacidad actual — no reducir al bajar agentes.
        // Esto evita reasignaciones costosas cada vez que un manifestante muere.
        if (_bCapacidad >= n) return;

        if (_bPos.IsCreated)      _bPos.Dispose();
        if (_bVel.IsCreated)      _bVel.Dispose();
        if (_bNuevaPos.IsCreated) _bNuevaPos.Dispose();
        if (_bNuevaVel.IsCreated) _bNuevaVel.Dispose();

        _bPos      = new NativeArray<float3>(n, Allocator.Persistent);
        _bVel      = new NativeArray<float3>(n, Allocator.Persistent);
        _bNuevaPos = new NativeArray<float3>(n, Allocator.Persistent);
        _bNuevaVel = new NativeArray<float3>(n, Allocator.Persistent);
        _bCapacidad = n;
    }

    // Snapshot reutilizable — evita alloc y protege contra modificación concurrente de _agentes
    readonly List<ManifestanteIA> _snapshot = new();

    void TickBoids()
    {
        // Snapshot: copia los agentes vivos en un buffer fijo para esta iteración.
        // Si un ManifestanteIA muere y llama DesregistrarAgente() durante este tick,
        // el snapshot no cambia → sin IndexOutOfRange ni accesos a null destruidos.
        _snapshot.Clear();
        foreach (var a in _agentes)
            if (a != null) _snapshot.Add(a);

        // Limpiar nulls acumulados en la lista maestra (muertos sin desregistrar)
        if (_snapshot.Count != _agentes.Count)
            _agentes.RemoveAll(a => a == null);

        int n = _snapshot.Count;
        if (n == 0) return;

        AsegurarCapacidad(n);

        // Copiar estado actual (solo agentes vivos del snapshot)
        for (int i = 0; i < n; i++)
        {
            var p = _snapshot[i].transform.position;
            var v = _snapshot[i].VelocidadBoids;
            _bPos[i] = new float3(p.x, p.y, p.z);
            _bVel[i] = new float3(v.x, v.y, v.z);
        }

        // Config según tipo del primer agente válido
        var cfg = _snapshot[0].tipo == TipoManifestante.Disturbios
            ? IntegradorMatematicas.BOIDS_DISTURBIOS
            : IntegradorMatematicas.BOIDS_MANIFESTANTE;

        var job = new JobBoidsUpdate
        {
            posiciones        = _bPos,
            velocidades       = _bVel,
            radioSeparacion   = cfg.radioSep,
            radioAlineacion   = cfg.radioAlin,
            radioCohesion     = cfg.radioCoh,
            pesoSeparacion    = cfg.pesoSep,
            pesoAlineacion    = cfg.pesoAlin,
            pesoCohesion      = cfg.pesoCoh,
            velocidadMax      = cfg.velMax,
            fuerza            = cfg.fuerza,
            deltaTime         = BOIDS_TICK,
            objetivo          = new float3(centroManifestacion.x, centroManifestacion.y, centroManifestacion.z),
            pesoObjetivo      = cfg.pesoObj,
            nuevasVelocidades = _bNuevaVel,
            nuevasPosiciones  = _bNuevaPos,
        };
        job.Schedule(n, 8).Complete();

        // Aplicar resultados — snapshot garantiza índices válidos
        for (int i = 0; i < n; i++)
        {
            _snapshot[i].AplicarBoids(
                new Vector3(_bNuevaPos[i].x, _bNuevaPos[i].y, _bNuevaPos[i].z),
                new Vector3(_bNuevaVel[i].x, _bNuevaVel[i].y, _bNuevaVel[i].z));
        }
    }

    // FIX: era `void OnDestroy()`, que ocultaba el OnDestroy privado de SingletonMono<T>
    // → Instance nunca se anulaba → MissingReferenceException tras recarga de escena
    // (ManifestanteIA.Start hace _sistema?.RegistrarAgente, y el ?. no ve el fake-null de Unity).
    // Ahora usa el hook correcto OnDestroyed(), como el resto de singletons del proyecto.
    protected override void OnDestroyed()
    {
        if (_crIniciar != null) StopCoroutine(_crIniciar);
        if (_bPos.IsCreated)      _bPos.Dispose();
        if (_bVel.IsCreated)      _bVel.Dispose();
        if (_bNuevaPos.IsCreated) _bNuevaPos.Dispose();
        if (_bNuevaVel.IsCreated) _bNuevaVel.Dispose();
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
    public bool    activaAlInicio = true;

    // ─── Estado ────────────────────────────────────────────────────────────
    bool     _activa;
    public bool EnCurso => _activa;

    /// <summary>true si una misión gobierna la manifestación (el director no la toca).</summary>
    public bool ControladaPorMision { get; set; }
    Coroutine _crIniciar;
    readonly List<ManifestanteIA> _manifestantes = new();
    readonly List<GameObject>     _barricadas    = new();
    AudioSource _srcConsignas, _srcDisturbios;
    SistemaApoyoPopular _apoyo;

    void Start()
    {
        _apoyo = SistemaApoyoPopular.Instance;
        InicializarAudio();
        if (activaAlInicio) _crIniciar = StartCoroutine(IniciarManifestacion());
    }

    // ── Inicio de manifestación ───────────────────────────────────────────

    public IEnumerator IniciarManifestacion()
    {
        if (_activa) yield break;
        _activa = true;
        Debug.Log("[Manifestacion] Manifestación iniciada en Nafarroa Kalea.");

        // Integración opcional: moral de multitud + dispositivo antidisturbios.
        // GetComponent una vez por manifestación (no por frame) — convención OK.
        GetComponent<SistemaMoralManifestacion>()?.PrepararConvocatoria();
        SistemaCargasPoliciales.Instance?.ActivarDispositivo();

        // Calcular altura real
        float y = Terrain.activeTerrain != null
            ? Terrain.activeTerrain.SampleHeight(centroManifestacion) + 0.1f
            : 240f;
        centroManifestacion.y = y;

        // 1. Barricadas
        yield return StartCoroutine(ColocarBarricadas());

        // 2. Todas las formaciones en paralelo (bloque + jarrai + guardia)
        var c1 = StartCoroutine(SpawnManifestantesPacificos());
        var c2 = StartCoroutine(SpawnGrupoDisturbios());
        yield return c1;
        yield return c2;

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
                barricada = Instantiate(prefabBarricada, pos, Quaternion.Euler(0, URandom.Range(-20f, 20f), 0));
            }
            else
            {
                // Fallback: apilar cubos como barricada
                barricada = new GameObject($"Barricada_{i}");
                barricada.transform.position = pos;
                // OPT: MaterialPropertyBlock para colorear fallback sin crear instancias de Material
                // Ahorro: 4 × numBarricadas Material instances evitadas (4×6 = 24 leaks en barricadas).
                var mpbBarricada = new MaterialPropertyBlock();
                mpbBarricada.SetColor("_BaseColor", new Color(0.15f, 0.15f, 0.15f));
                mpbBarricada.SetColor("_Color",     new Color(0.15f, 0.15f, 0.15f));
                for (int b = 0; b < 4; b++)
                {
                    var cubo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cubo.transform.SetParent(barricada.transform);
                    cubo.transform.localPosition = new Vector3(URandom.Range(-1.5f, 1.5f), b * 0.55f, URandom.Range(-0.3f, 0.3f));
                    cubo.transform.localScale = new Vector3(1.2f, 0.5f, 0.6f);
                    cubo.GetComponent<MeshRenderer>().SetPropertyBlock(mpbBarricada); // evita material instance
                }
            }
            barricada.name = $"Barricada_{i}";
            _barricadas.Add(barricada);
            yield return null;
        }
    }

    GameObject ResolverPrefabManifestante()
    {
        if (prefabManifestante != null) return prefabManifestante;
        var cfg = ConfiguradorAssetsAAA.Instance;
        if (cfg?.prefabsManifestante?.Length > 0)
            return cfg.prefabsManifestante[URandom.Range(0, cfg.prefabsManifestante.Length)];
        return CreateDefaultManifestante(false);
    }

    GameObject ResolverPrefabJarrai()
    {
        var cfg = ConfiguradorAssetsAAA.Instance;
        if (cfg?.prefabsJarrai?.Length > 0)
            return cfg.prefabsJarrai[URandom.Range(0, cfg.prefabsJarrai.Length)];
        return ResolverPrefabManifestante();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  FORMACIONES — disposición espacial realista de la manifestación
    //
    //  Eje Z+: dirección norte (hacia la Guardia Civil)
    //
    //  [OBSERVADORES]  scattered ±60m, detrás y lados
    //  [BLOQUE PRINCIPAL manifestantes] filas de 10, z = -5 a -50
    //  [VANGUARDIA JARRAI] semicírculo, z = +10 a +25
    //  [BARRICADAS] z ≈ +30
    //  [GUARDIA CIVIL] línea, z ≈ +55
    // ══════════════════════════════════════════════════════════════════════

    // Coordenadas relativas al centro de manifestación (Z+ = hacia guardia)
    const float Z_BLOQUE_INICIO  = -5f;    // inicio del bloque principal
    const float Z_BLOQUE_FIN     = -50f;   // fondo del bloque
    const float Z_JARRAI         = 15f;    // vanguardia
    const float Z_GUARDIA        = 55f;    // línea de guardia
    const float ANCHO_BLOQUE     = 35f;    // anchura del bloque (columnas)
    const float PASO_FILA        = 1.4f;   // separación entre filas
    const float PASO_COLUMNA     = 1.2f;   // separación entre columnas
    const int   COLUMNAS_BLOQUE  = 10;

    float AlturaTerreno(Vector3 pos) =>
        Terrain.activeTerrain != null ? Terrain.activeTerrain.SampleHeight(pos) : centroManifestacion.y;

    Vector3 PosConAltura(Vector3 base_) { base_.y = AlturaTerreno(base_) + 0.05f; return base_; }

    IEnumerator SpawnManifestantesPacificos()
    {
        // ── 1. BLOQUE PRINCIPAL (manifestantes en filas) ─────────────────
        var prefabM = ResolverPrefabManifestante();
        var cfg = ConfiguradorAssetsAAA.Instance;
        int filas = Mathf.CeilToInt((float)numManifestantes / COLUMNAS_BLOQUE);

        for (int i = 0; i < numManifestantes; i++)
        {
            int fila   = i / COLUMNAS_BLOQUE;
            int col    = i % COLUMNAS_BLOQUE;
            float x    = (col - COLUMNAS_BLOQUE * 0.5f) * PASO_COLUMNA + URandom.Range(-0.3f, 0.3f);
            float z    = Z_BLOQUE_INICIO - fila * PASO_FILA + URandom.Range(-0.2f, 0.2f);
            var pos    = PosConAltura(centroManifestacion + new Vector3(x, 0, z));

            // Variedad: Kenney crowd en las filas traseras
            GameObject prefabUsar = prefabM;
            if (fila > filas / 2 && cfg?.prefabsCivilKenney?.Length > 0)
                prefabUsar = cfg.prefabsCivilKenney[URandom.Range(0, cfg.prefabsCivilKenney.Length)];

            var go = Instantiate(prefabUsar, pos, Quaternion.Euler(0, URandom.Range(-20f, 20f), 0));
            go.name = $"Manifestante_{i}";
            go.transform.localScale = Vector3.one * URandom.Range(0.88f, 1.05f);

            var ia = go.AddComponent<ManifestanteIA>();
            ia.tipo = TipoManifestante.Pacifico;
            ia.centro = centroManifestacion;
            ia.apoyoSistema = _apoyo;
            _manifestantes.Add(ia);

            ImpostoresNPCDistantes.Instance?.Registrar(go, ImpostoresNPCDistantes.Faccion.Manifestante);

            if (i % 8 == 0) yield return null;
        }

        // ── 2. OBSERVADORES CIVILES (perimetro trasero, curiosos) ────────
        int numObservadores = Mathf.RoundToInt(numManifestantes * 0.25f);
        for (int i = 0; i < numObservadores; i++)
        {
            float angle = URandom.Range(Mathf.PI * 0.6f, Mathf.PI * 1.4f); // arco trasero
            float radio = URandom.Range(25f, 65f);
            var pos = PosConAltura(centroManifestacion + new Vector3(
                Mathf.Cos(angle) * radio, 0, Mathf.Sin(angle) * radio - 30f));

            GameObject prefabUsar = prefabM;
            if (cfg?.prefabsCivil?.Length > 0)
                prefabUsar = cfg.prefabsCivil[URandom.Range(0, cfg.prefabsCivil.Length)];

            var go = Instantiate(prefabUsar, pos, Quaternion.Euler(0, URandom.Range(0f, 360f), 0));
            go.name = $"Observador_{i}";
            go.transform.localScale = Vector3.one * URandom.Range(0.88f, 1.05f);

            var ia = go.AddComponent<ManifestanteIA>();
            ia.tipo = TipoManifestante.Pacifico;
            ia.centro = centroManifestacion;
            ia.apoyoSistema = _apoyo;
            _manifestantes.Add(ia);

            ImpostoresNPCDistantes.Instance?.Registrar(go, ImpostoresNPCDistantes.Faccion.Civilian);

            if (i % 5 == 0) yield return null;
        }
    }

    IEnumerator SpawnGrupoDisturbios()
    {
        // ── 3. VANGUARDIA JARRAI (semicírculo, cara a la guardia) ────────
        var prefabJ = ResolverPrefabJarrai();
        var prefabEnc = prefabEncapuchado ?? CreateDefaultManifestante(true);

        for (int i = 0; i < numDisturbios; i++)
        {
            // Semicírculo delante del bloque, apuntando hacia la guardia
            float t     = (float)i / numDisturbios;
            float angle = Mathf.Lerp(-Mathf.PI * 0.45f, Mathf.PI * 0.45f, t);
            float radio = URandom.Range(8f, 20f);
            float z     = Z_JARRAI + Mathf.Cos(angle) * radio * 0.4f + URandom.Range(-1f, 1f);
            float x     = Mathf.Sin(angle) * radio + URandom.Range(-0.5f, 0.5f);
            var pos     = PosConAltura(centroManifestacion + new Vector3(x, 0, z));

            // Alternar jarrai / encapuchado
            var prefabUsar = (i % 3 == 0) ? prefabEnc : prefabJ;
            var go = Instantiate(prefabUsar, pos,
                Quaternion.Euler(0, URandom.Range(-10f, 10f), 0)); // mirando norte
            go.name = $"Jarrai_{i}";

            var ia = go.AddComponent<ManifestanteIA>();
            ia.tipo = TipoManifestante.Disturbios;
            ia.centro = centroManifestacion;
            ia.apoyoSistema = _apoyo;
            _manifestantes.Add(ia);

            ImpostoresNPCDistantes.Instance?.Registrar(go, ImpostoresNPCDistantes.Faccion.Jarrai);

            if (i % 5 == 0) yield return null;
        }

        // ── 4. LÍNEA DE GUARDIA CIVIL (al norte de las barricadas) ───────
        var cfgGuardia = ConfiguradorAssetsAAA.Instance;
        if (cfgGuardia?.prefabsGuardia?.Length > 0)
        {
            int numGuardia = Mathf.Max(8, numDisturbios / 2);
            for (int i = 0; i < numGuardia; i++)
            {
                float x = (i - numGuardia * 0.5f) * 2.0f; // 2m entre agentes
                var pos = PosConAltura(centroManifestacion + new Vector3(x, 0, Z_GUARDIA));

                var prefabG = cfgGuardia.prefabsGuardia[i % cfgGuardia.prefabsGuardia.Length];
                var go = Instantiate(prefabG, pos,
                    Quaternion.Euler(0, 180f, 0)); // mirando sur (hacia manifestantes)
                go.name = $"Guardia_{i}";
                go.tag = "GuardiaCivil";

                var ia = go.AddComponent<ManifestanteIA>();
                ia.tipo = TipoManifestante.Pacifico;
                ia.centro = centroManifestacion + Vector3.forward * Z_GUARDIA;
                ia.apoyoSistema = _apoyo;
                _manifestantes.Add(ia);

                ImpostoresNPCDistantes.Instance?.Registrar(go, ImpostoresNPCDistantes.Faccion.GuardiaCivil);

                if (i % 4 == 0) yield return null;
            }
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

    // public: SistemaMoralManifestacion la invoca en la desbandada por carga policial
    public void TerminarManifestacion()
    {
        _activa = false;
        // Fin natural (tecla M / misión): notificar moral. Si vino de desbandada,
        // NotificarFin ya corrió y su guard interno evita el doble evento.
        GetComponent<SistemaMoralManifestacion>()?.NotificarFin(porCarga: false);
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
        // OPT: MaterialPropertyBlock en lugar de .material (evita instancia de Material por prefab)
        // Ahorro: 2 Material leaks evitados por CreateDefaultManifestante (template reutilizado).
        var mpbBody = new MaterialPropertyBlock();
        mpbBody.SetColor("_BaseColor", encapuchado ? Color.black : new Color(0.3f, 0.5f, 0.8f));
        mpbBody.SetColor("_Color",     encapuchado ? Color.black : new Color(0.3f, 0.5f, 0.8f));
        go.GetComponent<MeshRenderer>().SetPropertyBlock(mpbBody);
        // Keffiyeh (cubo pequeño sobre la cabeza)
        var kef = GameObject.CreatePrimitive(PrimitiveType.Cube);
        kef.transform.SetParent(go.transform);
        kef.transform.localPosition = new Vector3(0, 0.55f, 0);
        kef.transform.localScale = new Vector3(0.6f, 0.25f, 0.6f);
        var mpbKef = new MaterialPropertyBlock();
        mpbKef.SetColor("_BaseColor", encapuchado ? Color.black : new Color(0.8f, 0.1f, 0.1f));
        mpbKef.SetColor("_Color",     encapuchado ? Color.black : new Color(0.8f, 0.1f, 0.1f));
        kef.GetComponent<MeshRenderer>().SetPropertyBlock(mpbKef);
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

// RequireComponent: Unity AÑADE el Rigidbody automáticamente al añadir este
// componente y NO permite quitarlo → imposible el MissingComponentException
// que spameaba en FixedUpdate (decenas de NPCs × cada frame físico) y dejaba
// el FPS en 0, ahogando las corrutinas de arranque (mundo atascado en la
// pantalla de carga). El fix por re-adquisición no bastaba (otro sistema
// destruía el Rigidbody, o el orden de ejecución lo pillaba antes de Start).
[RequireComponent(typeof(Rigidbody))]
public class ManifestanteIA : MonoBehaviour, IAgente
{
    // IAgente
    public Vector3 Posicion   => transform.position;
    public bool    EstaActivo => gameObject.activeInHierarchy;
    public void    Alertar(Vector3 origen) { /* manifestantes no huyen por disparos */ }

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
        _sistema = SistemaManifestacion.Instance;
        _sistema?.RegistrarAgente(this);
        SistemaIA.Registrar(this);
        ElegirObjetivo();
    }

    void OnDestroy() { _sistema?.DesregistrarAgente(this); SistemaIA.Desregistrar(this); }

    void FixedUpdate()
    {
        _timer -= Time.fixedDeltaTime;
        if (_timer < 0) ElegirObjetivo();

        var jugador = AltsasuCore.Jugador;
        // OPT: sqrMagnitude evita sqrt — con 100+ manifestantes a 50Hz son 5000 sqrt/s eliminados.
        // Ahorro estimado: ~0.5-1.0 ms/frame en manifestaciones grandes.
        bool jugadorCerca = jugador != null
            && (transform.position - jugador.position).sqrMagnitude < 64f; // 8² = 64

        if (tipo == TipoManifestante.Disturbios && jugadorCerca)
        {
            // Disturbios: si ve al jugador, actúa (tira cosas)
            _objetivo = transform.position; // se queda
        }

        // FIX (playtest): re-adquirir el Rigidbody si falta → evita MissingComponentException
        // cada FixedUpdate (Manifestante sin Rigidbody) que inundaba la consola.
        if (_rb == null) _rb = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();

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
        _timer = URandom.Range(3f, 10f);
        if (tipo == TipoManifestante.Pacifico)
            _objetivo = centro + new Vector3(URandom.Range(-30f, 30f), 0, URandom.Range(-15f, 50f));
        else
            _objetivo = centro + new Vector3(URandom.Range(-20f, 20f), 0, URandom.Range(-25f, 15f));
        float y = Terrain.activeTerrain != null ? Terrain.activeTerrain.SampleHeight(_objetivo) : 240f;
        _objetivo.y = y;
    }
}
