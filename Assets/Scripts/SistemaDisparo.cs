// Assets/Scripts/SistemaDisparo.cs
// Sistema de disparo con raycast, cadencia, munición e impactos.
//
// ── MEJORAS RENDIMIENTO ────────────────────────────────────────────────────
//  · Object Pool para efectos: cero Instantiate/Destroy en runtime.
//    POOL_BURSTS (20) ParticleSystem pre-creados → flash cañón, polvo, sangre, chispas.
//    POOL_DECALS (50) esferas pre-creadas → marcas de bala.
//    Sin pool: a 8 dis/seg × 60s = 480 allocations → presión GC innecesaria.
//  · Dispersión dinámica: aumenta al moverse/saltar, se reduce al agacharse.
//    Hace el arma más realista y penaliza el "spray & pray".

using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Profiling;

public class SistemaDisparo : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════════════
    //  CONFIGURACIÓN
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ ARMA ═══")]
    [Tooltip("Distancia máxima de alcance del raycast de disparo (m).")]
    [SerializeField] private float alcanceDisparo     = 200f;
    [Tooltip("Puntos de daño aplicados al objetivo por cada bala impactada.")]
    [SerializeField] private int   danoPorBala        = 25;
    [Tooltip("Tiempo mínimo entre disparos (segundos). Determina la cadencia de fuego.")]
    [SerializeField] private float cadencia           = 0.12f;   // segundos entre disparos

    [Header("═══ DISPERSIÓN ═══")]
    [Tooltip("Dispersión base del arma estando parado y en suelo (radianes de cono).")]
    [SerializeField] private float dispersionBase     = 0.015f;  // parado, en suelo
    [Tooltip("Dispersión extra añadida al andar. Se escala por velocidad normalizada.")]
    [SerializeField] private float dispersionMovimiento = 0.022f; // añadido al andar
    [Tooltip("Dispersión extra añadida cuando el jugador está en el aire.")]
    [SerializeField] private float dispersionAire     = 0.040f;  // añadido al saltar
    [Tooltip("Multiplicador de dispersión al estar agachado (C). 0.4 = 60% menos dispersión.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float multAgachado       = 0.40f;   // multiplicador al agacharse
    [Tooltip("Multiplicador de dispersión al apuntar con RMB. 0.6 = 40% menos dispersión.")]
    [Range(0.5f, 1f)]
    [SerializeField] private float multApuntando      = 0.60f;   // multiplicador al apuntar (RMB)

    [Header("═══ MUNICIÓN ═══")]
    [Tooltip("Balas actuales en el cargador.")]
    [SerializeField] private int   balas             = 30;
    [Tooltip("Capacidad máxima del cargador.")]
    [SerializeField] private int   balasMaxCargador  = 30;
    [Tooltip("Balas en reserva (fuera del cargador). Al recargar se transfieren al cargador.")]
    [SerializeField] private int   balasReserva      = 120;
    [Tooltip("Duración de la animación de recarga (segundos).")]
    [SerializeField] private float tiempoRecarga     = 2.0f;

    [Header("═══ REFERENCIA ═══")]
    [Tooltip("Velocidad máxima de carrera (m/s) usada para normalizar la dispersión en movimiento.\n" +
             "Debe coincidir con ControladorJugador.velocidadCorrer para que el factor de penalización sea correcto.")]
    [SerializeField] private float velocidadCorrerRef = 8.5f;

    [Header("═══ CAPAS ═══")]
    [Tooltip("Capas de Unity que pueden recibir impacto de bala. Por defecto todo excepto 'Ignore Raycast'.")]
    [SerializeField] private LayerMask capasImpacto;   // qué capas reciben impacto

    // ═══════════════════════════════════════════════════════════════════════
    //  ESTADO INTERNO
    // ═══════════════════════════════════════════════════════════════════════

    private float              tiempoUltimoDisparo = 0f;
    private bool               estaCargando        = false;
    private float              timerRecarga        = 0f;
    private Camera             camara;
    private ControladorJugador controlJugador; // BUG 6 FIX: cachear en Awake(), no buscar cada disparo

    // Propiedades de solo lectura para HUDJugador
    public int   Balas            => balas;
    public int   BalasMaxCargador => balasMaxCargador;
    public int   BalasReserva     => balasReserva;
    public bool  EstaCargando     => estaCargando;
    public float TimerRecarga     => timerRecarga;
    /// <summary>Progreso de recarga 0→1 (0=inicio, 1=completada). 1 si no está recargando.</summary>
    public float ProgressRecarga  => estaCargando
        ? Mathf.Clamp01(1f - timerRecarga / Mathf.Max(tiempoRecarga, 0.01f))
        : 1f;

    // ── Object Pool ──────────────────────────────────────────────────────

    private const int POOL_BURSTS = 20;  // efectos de partículas (flash, polvo, sangre, chispas)
    private const int POOL_DECALS = 50;  // marcas de bala (esferas pequeñas)

    private struct SlotBurst
    {
        public GameObject     go;
        public ParticleSystem ps;
        public bool           enUso;
        public float          timerRetorno;
    }

    private struct SlotDecal
    {
        public GameObject go;
        public Renderer   rend;
        public bool       enUso;
        public float      timerRetorno;
    }

    private SlotBurst[] _poolBursts;
    private SlotDecal[] _poolDecals;

    // MaterialPropertyBlock y material compartido para décals (cero alloc en runtime)
    private MaterialPropertyBlock _pbDecal;
    private Material              _matDecalCompartido;  // BUG FIX: un único material para todos los décals (antes: N=50 materiales → memory leak)
    private static readonly int   _idBaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly Color _colorDecal  = new Color(0.08f, 0.08f, 0.08f);

    // ── Profiler marker ──────────────────────────────────────────────────
    // FIX T07/T08: NO usar campo static readonly con inicializador para ProfilerMarker.
    // En EditMode (tests), el inicializador estático se ejecuta antes de que Unity tenga
    // el profiler nativo listo, lanzando TypeInitializationException → Awake nunca corre
    // → _poolBursts y _poolDecals quedan null → T07 y T08 fallan.
    // Solución: constructor estático con try/catch aísla el fallo al propio marker.
    private static ProfilerMarker _markerDisparar;
    static SistemaDisparo()
    {
        try   { _markerDisparar = new ProfilerMarker("SistemaDisparo.Disparar"); }
        catch (System.Exception) { _markerDisparar = default; }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  UNITY
    // ═══════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        // Inicializar pools primero — así los tests de edit mode siempre los encuentran
        // no nulos aunque algún paso posterior lance excepción.
        InicializarPoolBursts();
        InicializarPoolDecals();

        camara = GetComponentInChildren<Camera>();
        if (camara == null) camara = Camera.main;

        // BUG 6 FIX: cachear la referencia al ControladorJugador en Awake().
        controlJugador = GetComponentInParent<ControladorJugador>()
                      ?? GetComponent<ControladorJugador>();

        // Por defecto: todas las capas EXCEPTO "Ignore Raycast" (capa 2).
        if (capasImpacto == 0)
            capasImpacto = ~(1 << 2);

        _pbDecal = new MaterialPropertyBlock();
        _pbDecal.SetColor(_idBaseColor, _colorDecal);
        _pbDecal.SetColor("_Color",     _colorDecal);
    }

    private void OnDestroy()
    {
        // BUG FIX: destruir el material compartido de los décals al desactivar el componente.
        // Sin esto, el material queda en memoria como objeto huérfano (leak).
        if (_matDecalCompartido != null) Object.Destroy(_matDecalCompartido);
    }

    private void Update()
    {
        // Recarga
        if (estaCargando)
        {
            timerRecarga -= Time.deltaTime;
            if (timerRecarga <= 0f)
                FinRecarga();
        }

        // Tecla R = recargar manualmente
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            IniciarRecarga();

        // Devolver slots de pool cuyo timer ha expirado
        TickPool();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  INICIALIZACIÓN DEL OBJECT POOL
    // ═══════════════════════════════════════════════════════════════════════

    private void InicializarPoolBursts()
    {
        _poolBursts = new SlotBurst[POOL_BURSTS];
        for (int i = 0; i < POOL_BURSTS; i++)
        {
            var go = new GameObject($"Burst_Pool_{i:D2}");
            go.transform.SetParent(transform);
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();

            // Configuración base — los módulos de ParticleSystem solo se pueden
            // configurar en play mode; en edit mode (tests) los dejamos en valores por defecto.
            if (Application.isPlaying)
            {
                var main = ps.main;
                main.playOnAwake    = false;
                main.loop           = false;
                main.gravityModifier = 0.4f;
                var em = ps.emission;
                em.rateOverTime = 0f;
            }

            _poolBursts[i] = new SlotBurst { go = go, ps = ps };
        }
    }

    private void InicializarPoolDecals()
    {
        _poolDecals = new SlotDecal[POOL_DECALS];
        var shader  = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Standard");

        // BUG FIX: UN solo material compartido para los 50 décals.
        // Antes: new Material(shader) por cada décal → 50 instancias huérfanas → memory leak.
        // Ahora: todos usan sharedMaterial → el PropertyBlock sobreescribe el color por instancia.
        if (shader != null)
        {
            _matDecalCompartido       = new Material(shader);
            _matDecalCompartido.color = _colorDecal;
        }

        for (int i = 0; i < POOL_DECALS; i++)
        {
            var go   = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name  = $"Decal_Pool_{i:D2}";
            go.transform.SetParent(transform);
            go.transform.localScale = Vector3.one * 0.04f;
            var decalCol = go.GetComponent<Collider>();
            if (decalCol != null)
            {
                if (Application.isPlaying) Object.Destroy(decalCol);
                else Object.DestroyImmediate(decalCol);
            }
            go.SetActive(false);

            var rend = go.GetComponent<Renderer>();
            if (_matDecalCompartido != null) rend.sharedMaterial = _matDecalCompartido;
            rend.SetPropertyBlock(_pbDecal);

            _poolDecals[i] = new SlotDecal { go = go, rend = rend };
        }
    }

    private void TickPool()
    {
        float dt = Time.deltaTime;
        for (int i = 0; i < POOL_BURSTS; i++)
        {
            if (!_poolBursts[i].enUso) continue;
            _poolBursts[i].timerRetorno -= dt;
            if (_poolBursts[i].timerRetorno <= 0f) DevolverBurst(i);
        }
        for (int i = 0; i < POOL_DECALS; i++)
        {
            if (!_poolDecals[i].enUso) continue;
            _poolDecals[i].timerRetorno -= dt;
            if (_poolDecals[i].timerRetorno <= 0f) DevolverDecal(i);
        }
    }

    private int SlotBurstLibre()
    {
        for (int i = 0; i < POOL_BURSTS; i++)
            if (!_poolBursts[i].enUso) return i;
        return -1;
    }

    private int SlotDecalLibre()
    {
        for (int i = 0; i < POOL_DECALS; i++)
            if (!_poolDecals[i].enUso) return i;
        return -1;
    }

    private void DevolverBurst(int slot)
    {
        _poolBursts[slot].ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        _poolBursts[slot].go.SetActive(false);
        _poolBursts[slot].enUso = false;
    }

    private void DevolverDecal(int slot)
    {
        _poolDecals[slot].go.SetActive(false);
        _poolDecals[slot].enUso = false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  API PÚBLICA
    // ═══════════════════════════════════════════════════════════════════════

    public void Disparar()
    {
        using var _prof = _markerDisparar.Auto();
        if (estaCargando) return;
        if (Time.time - tiempoUltimoDisparo < cadencia) return;
        if (balas <= 0) { IniciarRecarga(); return; }
        if (camara == null) return;

        tiempoUltimoDisparo = Time.time;
        balas--;

        // ── Técnica 3ª persona: two-step aiming ─────────────────────────────
        Vector3 origen    = camara.transform.position;
        Vector3 dirCamara = camara.transform.forward;

        // Step 1 — Punto de mira
        Vector3 puntoMira = Physics.Raycast(origen, dirCamara, out RaycastHit aimHit,
                                            alcanceDisparo, capasImpacto)
                          ? aimHit.point
                          : origen + dirCamara * alcanceDisparo;

        // Step 2 — Posición del arma
        Vector3 posArma = controlJugador != null
            ? controlJugador.transform.position
              + controlJugador.transform.forward * 0.38f
              + Vector3.up * 1.30f
              + controlJugador.transform.right * 0.25f
            : origen;

        // Dispersión dinámica: mayor en movimiento/aire, menor agachado/apuntando
        float disp = CalcularDispersion();

        Vector3 direccion = (puntoMira - posArma).normalized;
        Vector3 perpH = Vector3.Cross(direccion, Vector3.up).normalized;
        if (perpH == Vector3.zero) perpH = Vector3.right;
        Vector3 perpV = Vector3.Cross(direccion, perpH).normalized;
        direccion += perpH * Random.Range(-disp, disp)
                   + perpV * Random.Range(-disp, disp);

        EfectoDisparo(posArma, direccion);

        // Raycast definitivo desde el arma
        if (Physics.Raycast(posArma, direccion, out RaycastHit hit, alcanceDisparo, capasImpacto))
            ProcesarImpacto(hit);

        if (balas <= 0) IniciarRecarga();
    }

    // ── Dispersión dinámica ──────────────────────────────────────────────

    private float CalcularDispersion()
    {
        float d = dispersionBase;

        if (controlJugador != null)
        {
            // Penalización por movimiento (normalizado respecto a velocidad máxima de carrera ~8.5 m/s)
            // BUG FIX: eliminado GetComponent<CharacterController>() por frame → ahora constante hardcoded.
            float velH = controlJugador.VelocidadHoriz;
            if (velH > 0.5f)
                d += dispersionMovimiento * Mathf.Clamp01(velH / velocidadCorrerRef);

            // Penalización en el aire
            if (!controlJugador.EstaEnSueloP)
                d += dispersionAire;

            // Bonificación al agacharse
            if (controlJugador.EstaAgachadoP)
                d *= multAgachado;

            // Bonificación al apuntar (RMB)
            if (controlJugador.EstaApuntandoP)
                d *= multApuntando;
        }

        return d;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  IMPACTO
    // ═══════════════════════════════════════════════════════════════════════

    private void ProcesarImpacto(RaycastHit hit)
    {
        var enemigo = hit.collider.GetComponentInParent<EnemigoPatrulla>();
        if (enemigo != null)
        {
            enemigo.RecibirDano(danoPorBala);
            EfectoSangre(hit.point, hit.normal);
            return;
        }

        var vehiculo = hit.collider.GetComponentInParent<VehiculoNPC>();
        if (vehiculo != null)
        {
            vehiculo.RecibirDano(danoPorBala);
            EfectoChispa(hit.point, hit.normal);
            return;
        }

        // Coche del jugador también recibe daño
        var cocheJugador = hit.collider.GetComponentInParent<ControladorVehiculoJugador>();
        if (cocheJugador != null)
        {
            cocheJugador.RecibirDano(danoPorBala);
            EfectoChispa(hit.point, hit.normal);
            return;
        }

        // Impacto con detección de material (decal correcto + shake)
        SistemaImpactos.Impacto(hit);
        EfectoImpactoSuelo(hit.point, hit.normal);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  RECARGA
    // ═══════════════════════════════════════════════════════════════════════

    private void IniciarRecarga()
    {
        if (estaCargando) return;
        if (balasReserva <= 0) return;
        if (balas == balasMaxCargador) return;

        estaCargando = true;
        timerRecarga = tiempoRecarga;
        AlsasuaLogger.Info("Disparo", "Recargando...");
    }

    private void FinRecarga()
    {
        int necesarias  = balasMaxCargador - balas;
        int disponibles = Mathf.Min(necesarias, balasReserva);
        balas          += disponibles;
        balasReserva   -= disponibles;
        estaCargando    = false;
        AudioManager.I?.Play(AudioManager.Clip.Recarga);   // clic de recarga completada
        AlsasuaLogger.Info("Disparo", $"Recargado: {balas}/{balasMaxCargador}  Reserva: {balasReserva}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  EFECTOS VISUALES — Object Pool (cero Instantiate en runtime)
    // ═══════════════════════════════════════════════════════════════════════

    private void EfectoDisparo(Vector3 origen, Vector3 direccion)
    {
        CrearBurst(origen + direccion * 0.5f, new Color(1f, 0.8f, 0.3f), 0.05f, 8, 0.08f);
        AudioManager.I?.Play(AudioManager.Clip.Disparo, origen);
    }

    private void EfectoImpactoSuelo(Vector3 punto, Vector3 normal)
    {
        CrearBurst(punto, new Color(0.7f, 0.6f, 0.5f), 0.06f, 12, 0.3f);
        ColocarDecal(punto + normal * 0.01f, 8f);
        AudioManager.I?.Play(AudioManager.Clip.ImpactoSuelo, punto);
    }

    private void EfectoSangre(Vector3 punto, Vector3 normal)
    {
        CrearBurst(punto, new Color(0.6f, 0f, 0f), 0.06f, 15, 0.25f);
        AudioManager.I?.Play(AudioManager.Clip.ImpactoSangre, punto);
    }

    private void EfectoChispa(Vector3 punto, Vector3 normal)
    {
        CrearBurst(punto, new Color(1f, 0.9f, 0.3f), 0.04f, 20, 0.15f);
        AudioManager.I?.Play(AudioManager.Clip.ImpactoMetal, punto);
    }

    // ── Activar un slot de burst del pool ───────────────────────────────

    private void CrearBurst(Vector3 pos, Color color, float tamano, int cantidad, float duracion)
    {
        int slot = SlotBurstLibre();
        if (slot < 0)
        {
            // Pool lleno: reciclar el más antiguo (siempre tiene slot 0 como fallback)
            DevolverBurst(0);
            slot = 0;
        }

        ref SlotBurst s = ref _poolBursts[slot];
        s.go.transform.position = pos;
        s.go.SetActive(true);

        // Reconfigurar el ParticleSystem para este efecto concreto
        var main = s.ps.main;
        main.startLifetime  = duracion;
        main.startSpeed     = new ParticleSystem.MinMaxCurve(1f, 4f);
        main.startSize      = tamano;
        main.startColor     = color;
        main.maxParticles   = cantidad;

        var em = s.ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)cantidad) });

        var shape = s.ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.05f;

        s.ps.Play();
        s.enUso        = true;
        s.timerRetorno = duracion + 0.5f;
    }

    // ── Activar un décal de bala del pool ───────────────────────────────

    private void ColocarDecal(Vector3 pos, float duracion)
    {
        int slot = SlotDecalLibre();
        if (slot < 0) { DevolverDecal(0); slot = 0; }

        ref SlotDecal s = ref _poolDecals[slot];
        s.go.transform.position = pos;
        s.go.SetActive(true);
        s.enUso        = true;
        s.timerRetorno = duracion;
    }

    // OnGUI() eliminado — HUDJugador.cs gestiona la UI de munición vía Canvas uGUI.
}
