// Assets/Scripts/ControladorVehiculoJugador.cs
// ═══════════════════════════════════════════════════════════════════════════
//  VEHÍCULO CONDUCIBLE — Pilares de Gorka Games traducidos a Unity
//
//  Gorka Pillar 1 — Prototipado modular:
//    Tecla E cerca del coche → entrada/salida + transición suave de cámara
//    (equivale al "Enter Vehicle" Blueprint de Gorka, sin Overlap Event invisible)
//
//  Gorka Pillar 2 — Chaos Vehicles → WheelCollider + Fórmula de Pacejka:
//    F_y = D · sin( C · arctan( B·α − E·(B·α − arctan(B·α)) ) )
//    WheelCollider gestiona suspensión y colisión; Pacejka reemplaza la
//    curva de fricción lateral con física de neumático real.
//
//  Setup en el Editor:
//    1. Añade este componente al GO raíz del coche (con Rigidbody).
//    2. Crea 4 GOs vacíos hijos para los WheelColliders (FL, FR, RL, RR).
//    3. Asígnalos en el Inspector (rDI, rDD, rTI, rTD).
//    4. Asigna meshes de rueda opcionales (solo cosméticos).
//    5. Crea un Transform vacío dentro del coche llamado "AsientoConductor"
//       y asígnalo — la cámara se interpola hasta él al entrar.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class ControladorVehiculoJugador : VehiculoBase, IInteractable
{
    // ── Ruedas ────────────────────────────────────────────────────────────────
    [Header("═══ RUEDAS ═══")]
    [Tooltip("WheelCollider delantera izquierda (Front Left)")]
    [SerializeField] private WheelCollider rDI;
    [Tooltip("WheelCollider delantera derecha (Front Right)")]
    [SerializeField] private WheelCollider rDD;
    [Tooltip("WheelCollider trasera izquierda (Rear Left)")]
    [SerializeField] private WheelCollider rTI;
    [Tooltip("WheelCollider trasera derecha (Rear Right)")]
    [SerializeField] private WheelCollider rTD;

    [Tooltip("Mesh visual de la rueda DI — solo cosmético, se sincroniza con el WheelCollider")]
    [SerializeField] private Transform meshDI;
    [Tooltip("Mesh visual de la rueda DD")]
    [SerializeField] private Transform meshDD;
    [Tooltip("Mesh visual de la rueda TI")]
    [SerializeField] private Transform meshTI;
    [Tooltip("Mesh visual de la rueda TD")]
    [SerializeField] private Transform meshTD;

    // ── Motor / Transmisión ───────────────────────────────────────────────────
    [Header("═══ MOTOR ═══")]
    [Tooltip("Par motor máximo (N·m). Mitsubishi Montero ≈ 300, Furgoneta ≈ 220.")]
    [SerializeField] private float parMaximo          = 300f;
    [Tooltip("Par de frenado de servicio (N·m). Proporcional al peso del vehículo.")]
    [SerializeField] private float parFreno           = 800f;
    [Tooltip("Par del freno de mano — solo ruedas traseras (N·m). Alto = derrape.")]
    [SerializeField] private float parFrenoMano       = 1400f;
    [Tooltip("Ángulo máximo de giro de las ruedas delanteras (°).")]
    [SerializeField] private float anguloMaxDireccion = 35f;
    [Tooltip("Velocidad máxima del vehículo (m/s). 25 ≈ 90 km/h, 33 ≈ 120 km/h.")]
    [SerializeField] private new float velocidadMax   = 25f; // oculta VehiculoBase.velocidadMax a propósito (CS0108)
    [Tooltip("Tracción: true = 4WD (Montero off-road), false = RWD (coches normales).")]
    [SerializeField] private bool  cuatroRuedas       = false;

    // ── Fórmula Mágica de Pacejka ─────────────────────────────────────────────
    [Header("═══ PACEJKA (neumáticos) ═══")]
    [Tooltip("B — Factor de rigidez. Mayor = neumático más abrupto en el límite.")]
    [SerializeField] private float pacejkaB           = 10f;
    [Tooltip("C — Factor de forma. 1.9 = turismo asfalto · 1.5 = off-road barro.")]
    [SerializeField] private float pacejkaC           = 1.9f;
    [Tooltip("D — Pico de adherencia (fracción de carga normal). 1.0 = seco · 0.65 = mojado.")]
    [SerializeField] private float pacejkaD           = 1.0f;
    [Tooltip("E — Factor de curvatura. Negativo → neumático de carretera clásico.")]
    [SerializeField] private float pacejkaE           = -1f;

    // ── Suspensión / Centro de masa ───────────────────────────────────────────
    [Header("═══ SUSPENSIÓN ═══")]
    [Tooltip("Offset vertical del centro de masa (m). Más negativo = más estable en curvas.")]
    [SerializeField] private float alturaCentroMasa   = -0.35f;
    [Tooltip("Fuerza anti-vuelco lateral (N). Evita que el coche vuele en curvas cerradas.")]
    [SerializeField] private float antiRollForce      = 3500f;
    [Tooltip("Rigidez de muelle de suspensión (N/m). SUV ≈ 35000, deportivo ≈ 55000.")]
    [SerializeField] private float rigidezMuelle      = 35000f;
    [Tooltip("Amortiguación de suspensión (N·s/m).")]
    [SerializeField] private float amortiguacion      = 4500f;

    // ── Cámara ────────────────────────────────────────────────────────────────
    [Header("═══ CÁMARA ═══")]
    [Tooltip("Transform vacío dentro del coche en el asiento del conductor. " +
             "La cámara se interpola hasta aquí al entrar (Gorka Pillar 1).")]
    [SerializeField] private Transform asientoConductor;
    [Tooltip("Velocidad de la transición de cámara al entrar/salir (Lerp t/s).")]
    [SerializeField] private float velocidadTransCamara = 5f;

    // ── Efectos ───────────────────────────────────────────────────────────────
    [Header("═══ EFECTOS ═══")]
    [Tooltip("Partículas de humo de las ruedas al derrapar (0=DI, 1=DD, 2=TI, 3=TD).")]
    [SerializeField] private ParticleSystem[] humoRuedas;
    [SerializeField] private AudioSource motorAudio;
    [SerializeField] private AudioClip   clipMotor;

    // ── Eventos para el sistema de misiones ───────────────────────────────────
    public static event System.Action<ControladorVehiculoJugador> OnJugadorEntro;
    public static event System.Action<ControladorVehiculoJugador> OnJugadorSalio;

    // ── Salud del vehículo ────────────────────────────────────────────────────
    // IDamageable (Vida, VidaMax, EstaMuerto, RecibirDano, Curar) → heredados de VehiculoBase
    // vidaMaxima, _vida, _destruido → en VehiculoBase (SerializeField = 100 por defecto;
    //   ajustar a 400 en el prefab Interceptor_Jugador)
    private ParticleSystem _humoMotor;   // humo al 50% HP
    private ParticleSystem _fuegoCoche;  // fuego al 20% HP

    // RecibirDano y Curar → heredados de VehiculoBase. Feedback visual en OnDanoRecibido().

    protected override void OnDanoRecibido(int cantidad, Vector3 origen, TipoDano tipo)
    {
        float fraccion = (float)_vida / vidaMaxima;

        if (fraccion <= 0.5f && _humoMotor == null)
            _humoMotor = CrearParticula(Vector3.up * 0.8f, new Color(0.3f,0.3f,0.3f,0.8f), 1.5f);

        if (fraccion <= 0.2f && _fuegoCoche == null)
        {
            _fuegoCoche = CrearParticula(Vector3.up * 0.6f, new Color(1f,0.3f,0f,0.9f), 2f);
            if (jugadorDentro)
                AlsasuaLogger.Warn("Vehiculo", "⚠ ¡El coche está en llamas! ¡Sal!");
        }
    }

    protected override void IniciarDestruccion()
    {
        _destruido = true;
        if (jugadorDentro) ForzarSalida();
        SistemaDestruccion.Instance?.ExplotarCoche(gameObject);
        ServiceLocator.Get<IWantedSystem>()?.AumentarBusqueda(2);
    }

    private ParticleSystem CrearParticula(Vector3 offsetLocal, Color color, float size)
    {
        var go  = new GameObject("VFX_Daño");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = offsetLocal;
        var ps  = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startColor     = color;
        main.startSize      = size;
        main.startLifetime  = 1.5f;
        main.startSpeed     = 1.5f;
        main.loop           = true;
        var em  = ps.emission; em.rateOverTime = 12f;
        var sh  = ps.shape;   sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.2f;
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.y = new ParticleSystem.MinMaxCurve(1f, 2f);
        ps.Play();
        return ps;
    }

    // IInteractable
    public string TextoInteraccion  => "Entrar en vehículo  [E]";
    public float  RadioInteraccion  => 3.5f;
    public bool   PuedeInteractuar  => !jugadorDentro && !_destruido;
    public void   OnInteractuar(ControladorJugador jugador) => EntraJugador(jugador);

    // ── Estado interno ────────────────────────────────────────────────────────
    private Rigidbody          rb;
    private bool               jugadorDentro;
    private ControladorJugador controlJugador;
    private Transform          camaraRef;          // cámara desacoplada del jugador
    private Vector3            camPosAntes;
    private Quaternion         camRotAntes;

    private float inputAcel;
    private float inputDir;
    private bool  inputFrenoMano;

    // Cámara en vehículo — ángulos de órbita independientes del spring arm del jugador
    private float _camH;   // ángulo horizontal de cámara en vehículo
    private float _camV = 10f; // ángulo vertical (ligeramente picado)
    [Header("═══ CÁMARA EN VEHÍCULO ═══")]
    [Tooltip("Distancia de la cámara al asiento del conductor mientras se conduce.")]
    [SerializeField] private float distanciaOrbitaCoche   = 6f;
    [Tooltip("Altura del pivot de cámara sobre el coche (m).")]
    [SerializeField] private float alturaPivotCoche       = 1.2f;
    [Tooltip("Sensibilidad horizontal de cámara al conducir.")]
    [SerializeField] private float sensibilidadCamH       = 2.8f;
    [Tooltip("Sensibilidad vertical de cámara al conducir.")]
    [SerializeField] private float sensibilidadCamV       = 2.2f;
    [Tooltip("Límite vertical inferior de cámara en coche (°).")]
    [SerializeField] private float limCamV_Min            = -15f;
    [Tooltip("Límite vertical superior de cámara en coche (°).")]
    [SerializeField] private float limCamV_Max            =  45f;

    // Cache del array de ruedas — evita alloc en cada FixedUpdate (blocker crítico de GC)
    private WheelCollider[] _todasLasRuedas;
    private Coroutine       _corrutinaCamara; // para cancelarla en ForzarSalida

    // Huellas de neumático (conexión con SistemaHuellasAsfalto).
    // Timers de throttle: [0..3] derrape por rueda, [4] frenada por eje trasero.
    // Sin throttle, spawnear cada FixedUpdate agotaría el pool de 64 decals al instante.
    private readonly float[] _huellaTimer = new float[5];

    // OPT: LayerMask para spring arm de cámara, cacheada en Awake.
    // LayerMask.GetMask hace string lookup por nombre → coste O(capas) cada Update.
    // Ahorro estimado: ~0.02 ms/frame (pequeño pero constante mientras se conduce).
    private int _maskSpringArmCached;

    // Propiedades públicas
    /// <summary>
    /// Devuelve <c>true</c> si hay un jugador conduciendo este vehículo en este momento.
    /// Usado por <see cref="ControladorJugador"/> para evitar entrar en un coche ya ocupado,
    /// y por el HUD para mostrar el velocímetro.
    /// </summary>
    public bool  JugadorDentro => jugadorDentro;

    /// <summary>
    /// Velocidad actual del vehículo en km/h, calculada a partir de la magnitud del
    /// <c>Rigidbody.linearVelocity</c>. Devuelve 0 si el Rigidbody aún no está inicializado.
    /// </summary>
    public float VelocidadKmh  => _rb != null ? _rb.linearVelocity.magnitude * 3.6f : 0f;

    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    protected override void OnAwakeVehiculo()
    {
        rb = _rb; // alias local para compatibilidad con código existente
        rb.centerOfMass = new Vector3(0f, alturaCentroMasa, 0f);
        rb.mass         = 1400f;
        ConfigurarWheelColliders();
        _todasLasRuedas = new[] { rDI, rDD, rTI, rTD };
        // OPT: cachear LayerMask del spring arm — evita string lookup cada Update
        _maskSpringArmCached = ~LayerMask.GetMask("Player", "Ignore Raycast");
        // _vida ya inicializado por VehiculoBase.Awake() con vidaMaxima
    }

    private void Update()
    {
        // E → SOLO salir cuando el jugador ya está dentro.
        // El ENTRAR se gestiona exclusivamente desde ControladorJugador.TentarEntrarVehiculo()
        // para evitar la doble detección (ambos scripts leen E el mismo frame).
        var kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame && jugadorDentro)
            SalirVehiculo();

        if (jugadorDentro)
        {
            LeerInput();
            ActualizarCamaraVehiculo();
        }
        SincronizarMeshesRuedas();
        ActualizarMotorSonido();
    }

    private void FixedUpdate()
    {
        if (!jugadorDentro) return;
        AplicarMotor();
        AplicarDireccion();
        AplicarPacejka();
        AplicarAntiRoll();
        LimitarVelocidad();
        ActualizarHuellas();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  HUELLAS DE NEUMÁTICO  → SistemaHuellasAsfalto
    //  Frenada fuerte (eje trasero) + derrape lateral (por rueda). Throttled.
    // ─────────────────────────────────────────────────────────────────────────

    private void ActualizarHuellas()
    {
        if (SistemaHuellasAsfalto.Instance == null) return;

        float speed = rb.linearVelocity.magnitude;
        if (speed < 4f) return;                       // ~14 km/h mínimo para dejar marca

        Quaternion orient = Quaternion.LookRotation(
            Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized, Vector3.up);

        // ── Frenada fuerte: marca recta negra bajo el eje trasero ───────────
        bool frenadaFuerte = (inputAcel < -0.05f || inputFrenoMano) && speed > 8f;
        if (frenadaFuerte && rTI != null && rTD != null
            && rTI.GetGroundHit(out WheelHit hTI) && rTD.GetGroundHit(out WheelHit hTD))
        {
            _huellaTimer[4] -= Time.fixedDeltaTime;
            if (_huellaTimer[4] <= 0f)
            {
                _huellaTimer[4] = 0.10f;
                float intensidad = Mathf.Clamp01(speed / velocidadMax);
                SistemaHuellasAsfalto.RegistrarFrenada(hTI.point, hTD.point, orient, intensidad);
            }
        }

        // ── Derrape lateral: arco por rueda que patina ──────────────────────
        for (int i = 0; i < _todasLasRuedas.Length; i++)
        {
            var wc = _todasLasRuedas[i];
            if (wc == null || !wc.GetGroundHit(out WheelHit hit)) continue;

            float slip = Mathf.Abs(hit.sidewaysSlip);
            if (slip < 0.5f) continue;                // umbral de derrape

            _huellaTimer[i] -= Time.fixedDeltaTime;
            if (_huellaTimer[i] > 0f) continue;
            _huellaTimer[i] = 0.09f;

            SistemaHuellasAsfalto.RegistrarDerrape(
                hit.point, orient, Mathf.Clamp01(slip), izquierda: hit.sidewaysSlip < 0f);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CONFIGURACIÓN DE WHEELCOLLIDERS
    // ─────────────────────────────────────────────────────────────────────────

    private void ConfigurarWheelColliders()
    {
        var suspension = new JointSpring
        {
            spring        = rigidezMuelle,
            damper        = amortiguacion,
            targetPosition = 0.5f
        };

        // Curva de fricción base (Pacejka sobreescribirá la fuerza lateral en FixedUpdate)
        var friccionBase = CurvaFriccion(1.5f);
        var friccionTrasera = CurvaFriccion(1.2f); // trasera más suelta → sobreviraje suave

        foreach (var wc in Ruedas())
        {
            if (wc == null) continue;
            wc.suspensionSpring    = suspension;
            wc.radius              = 0.35f;
            wc.suspensionDistance  = 0.2f;
            wc.forwardFriction     = friccionBase;
            wc.sidewaysFriction    = friccionBase;
        }
        if (rTI) rTI.sidewaysFriction = friccionTrasera;
        if (rTD) rTD.sidewaysFriction = friccionTrasera;
    }

    private static WheelFrictionCurve CurvaFriccion(float stiffness)
        => new WheelFrictionCurve
        {
            extremumSlip  = 0.4f,
            extremumValue = 1.0f,
            asymptoteSlip = 0.8f,
            asymptoteValue = 0.75f,
            stiffness     = stiffness
        };

    // ─────────────────────────────────────────────────────────────────────────
    //  GORKA PILLAR 1 — ENTRADA / SALIDA + TRANSICIÓN DE CÁMARA
    //  Gorka usa un "Is Near Vehicle" node en su Blueprint.
    //  Aquí: OverlapSphere físico + interpolación suave de cámara.
    // ─────────────────────────────────────────────────────────────────────────

    private void IntentarEntrar()
    {
        // Buscar al jugador dentro del radio de interacción (3.5 m)
        // QueryTriggerInteraction.Ignore evita falsos positivos con triggers de zona
        var cols = Physics.OverlapSphere(transform.position, 3.5f,
                       Physics.AllLayers, QueryTriggerInteraction.Ignore);
        foreach (var col in cols)
        {
            var jug = col.GetComponent<ControladorJugador>()
                   ?? col.GetComponentInParent<ControladorJugador>();
            if (jug != null) { EntraJugador(jug); return; }
        }
    }

    /// <summary>
    /// Hace entrar al jugador en el vehículo: desactiva el <see cref="CharacterController"/>
    /// y el <see cref="ControladorJugador"/>, oculta los renderers del personaje, lo emparenta
    /// bajo el coche en el asiento del conductor e inicia la transición de cámara (0.55 s).
    /// <para>
    /// Llamado desde <see cref="ControladorJugador.TentarEntrarVehiculo"/> (tecla E) o desde
    /// <see cref="IntentarEntrar"/> si la detección la hace el propio vehículo.
    /// No llames a este método si <see cref="JugadorDentro"/> es <c>true</c>.
    /// </para>
    /// </summary>
    /// <param name="jugador">Instancia del <see cref="ControladorJugador"/> que entra al vehículo.</param>
    public void EntraJugador(ControladorJugador jugador)
    {
        controlJugador = jugador;
        jugadorDentro  = true;

        // Desactivar movimiento a pie
        var cc = jugador.GetComponent<CharacterController>();
        if (cc) cc.enabled = false;
        jugador.enabled = false;

        // Guardar cámara original
        camaraRef   = jugador.CamaraTP?.transform;
        if (camaraRef != null)
        {
            camPosAntes = camaraRef.position;
            camRotAntes = camaraRef.rotation;
        }

        // Meter al jugador en el asiento (invisible mientras conduce)
        jugador.transform.SetParent(transform);
        jugador.transform.localPosition = asientoConductor != null
            ? asientoConductor.localPosition
            : new Vector3(-0.4f, 0.45f, 0.05f);
        jugador.transform.localRotation = Quaternion.identity;
        foreach (var r in jugador.GetComponentsInChildren<Renderer>())
            r.enabled = false;

        // Inicializar ángulo de cámara con el yaw actual del coche (evita salto brusco)
        _camH = transform.eulerAngles.y;
        _camV = 10f;

        // Notificar al GameManager (la IA de persecución y el HUD lo necesitan)
        ServiceLocator.Get<ISpawnService>()?.SetJugadorEnVehiculo(true);

        // Evento para el sistema de misiones
        OnJugadorEntro?.Invoke(this);

        // Transición de cámara: hombro → asiento conductor
        if (_corrutinaCamara != null) StopCoroutine(_corrutinaCamara);
        _corrutinaCamara = StartCoroutine(TransicionCamara(entrando: true));
        AlsasuaLogger.Info("Vehiculo", $"Jugador entró en {name}.");
    }

    private void SalirVehiculo()
    {
        if (controlJugador == null) return;

        jugadorDentro = false;

        // Evento para el sistema de misiones
        OnJugadorSalio?.Invoke(this);

        // Sacar al jugador a un lado del coche
        controlJugador.transform.SetParent(null);
        controlJugador.transform.position =
            transform.position + transform.right * 2.4f + Vector3.up * 0.6f;
        controlJugador.transform.rotation = transform.rotation;

        // Reactivar movimiento a pie
        var cc = controlJugador.GetComponent<CharacterController>();
        if (cc) cc.enabled = true;
        controlJugador.enabled = true;
        foreach (var r in controlJugador.GetComponentsInChildren<Renderer>())
            r.enabled = true;

        // Notificar al GameManager
        ServiceLocator.Get<ISpawnService>()?.SetJugadorEnVehiculo(false);

        // Transición de cámara: asiento → spring arm original
        if (_corrutinaCamara != null) StopCoroutine(_corrutinaCamara);
        _corrutinaCamara = StartCoroutine(TransicionCamara(entrando: false));
        controlJugador = null;
        AlsasuaLogger.Info("Vehiculo", $"Jugador salió de {name}.");
    }

    private System.Collections.IEnumerator TransicionCamara(bool entrando)
    {
        if (camaraRef == null) yield break;

        float t = 0f, dur = 0.55f;
        Vector3    p0 = camaraRef.position, p1;
        Quaternion r0 = camaraRef.rotation, r1;

        if (entrando)
        {
            if (asientoConductor != null)
            {
                p1 = asientoConductor.position + asientoConductor.up * 0.12f;
                r1 = asientoConductor.rotation;
            }
            else
            {
                // Fallback si asientoConductor no está asignado en el prefab:
                // cámara va detrás y encima del coche, mirando hacia adelante.
                p1 = transform.position
                   + transform.up    * alturaPivotCoche
                   - transform.forward * distanciaOrbitaCoche;
                r1 = Quaternion.LookRotation(transform.forward, Vector3.up);
            }
        }
        else
        {
            // Cámara vuelve al spring arm del jugador
            p1 = camPosAntes;
            r1 = camRotAntes;
        }

        while (t < dur)
        {
            t += Time.deltaTime;
            float s = Mathf.SmoothStep(0f, 1f, t / dur);
            camaraRef.position = Vector3.Lerp(p0, p1, s);
            camaraRef.rotation = Quaternion.Slerp(r0, r1, s);
            yield return null;
        }
        camaraRef.position = p1;
        camaraRef.rotation = r1;
        _corrutinaCamara = null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  INPUT
    // ─────────────────────────────────────────────────────────────────────────

    private void LeerInput()
    {
        var kb  = Keyboard.current;
        var gp  = Gamepad.current;

        if (kb != null)
        {
            // W/S = aceleración, A/D = dirección
            float acelPos = (kb.wKey.isPressed ? 1f : 0f) + (kb.upArrowKey.isPressed ? 1f : 0f);
            float acelNeg = (kb.sKey.isPressed ? 1f : 0f) + (kb.downArrowKey.isPressed ? 1f : 0f);
            inputAcel = Mathf.Clamp(acelPos - acelNeg, -1f, 1f);

            float dirPos = (kb.dKey.isPressed ? 1f : 0f) + (kb.rightArrowKey.isPressed ? 1f : 0f);
            float dirNeg = (kb.aKey.isPressed ? 1f : 0f) + (kb.leftArrowKey.isPressed ? 1f : 0f);
            inputDir = Mathf.Clamp(dirPos - dirNeg, -1f, 1f);

            inputFrenoMano = kb.spaceKey.isPressed;
        }
        else if (gp != null)
        {
            // Gamepad: stick izquierdo + gatillo izquierdo para freno de mano
            inputAcel      = gp.leftStick.y.ReadValue();
            inputDir       = gp.leftStick.x.ReadValue();
            inputFrenoMano = gp.leftShoulder.isPressed;
        }
        else
        {
            inputAcel = inputDir = 0f;
            inputFrenoMano = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GORKA PILLAR 2 — MOTOR  (Chaos Vehicle System → WheelCollider)
    //  Gorka configura TorqueCurve y MaxRPM en el VehicleMovementComponent.
    //  Aquí: par real con reducción suave al acercarse a velocidadMax.
    // ─────────────────────────────────────────────────────────────────────────

    private void AplicarMotor()
    {
        float velRatio = rb.linearVelocity.magnitude / velocidadMax;
        // Curva de par: cae cuadráticamente al acercarse a velocidadMax
        float par = inputAcel * parMaximo * Mathf.Clamp01(1f - velRatio * velRatio * 0.6f);

        if (cuatroRuedas)
        {
            // 4WD: par distribuido 40 % delante / 60 % atrás
            if (rDI) rDI.motorTorque = par * 0.4f;
            if (rDD) rDD.motorTorque = par * 0.4f;
            if (rTI) rTI.motorTorque = par * 0.6f;
            if (rTD) rTD.motorTorque = par * 0.6f;
        }
        else
        {
            // RWD: solo ruedas traseras
            if (rDI) rDI.motorTorque = 0f;
            if (rDD) rDD.motorTorque = 0f;
            if (rTI) rTI.motorTorque = par;
            if (rTD) rTD.motorTorque = par;
        }

        // Freno de servicio al soltar/revertir el acelerador
        float frenoAct = inputAcel < -0.05f ? Mathf.Abs(inputAcel) * parFreno : 0f;
        if (rDI) rDI.brakeTorque = frenoAct * 0.65f;
        if (rDD) rDD.brakeTorque = frenoAct * 0.65f;
        if (rTI) rTI.brakeTorque = frenoAct * 0.35f;
        if (rTD) rTD.brakeTorque = frenoAct * 0.35f;

        // Freno de mano (Space) — bloquea solo ruedas traseras → derrape
        if (inputFrenoMano)
        {
            if (rTI) { rTI.motorTorque = 0f; rTI.brakeTorque = parFrenoMano; }
            if (rTD) { rTD.motorTorque = 0f; rTD.brakeTorque = parFrenoMano; }
        }
    }

    private void AplicarDireccion()
    {
        // Reducción de ángulo a alta velocidad (simplificación de la geometría Ackermann)
        float factorVel = 1f - Mathf.Clamp01(rb.linearVelocity.magnitude / velocidadMax) * 0.45f;
        float angulo    = inputDir * anguloMaxDireccion * factorVel;
        if (rDI) rDI.steerAngle = angulo;
        if (rDD) rDD.steerAngle = angulo;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  FÓRMULA MÁGICA DE PACEJKA
    //  Gorka usa las curvas de Chaos (basadas internamente en Pacejka).
    //  Nosotros las calculamos explícitamente para poder ajustar B, C, D, E
    //  por tipo de superficie (asfalto seco/mojado, barro, adoquín).
    //
    //  F_y = D · sin( C · arctan( B·α − E·(B·α − arctan(B·α)) ) )
    //  α   = ángulo de deslizamiento lateral (rad)
    // ─────────────────────────────────────────────────────────────────────────

    private void AplicarPacejka()
    {
        // Usar el array cacheado en Awake — sin alloc en FixedUpdate
        for (int i = 0; i < _todasLasRuedas.Length; i++)
        {
            var wc = _todasLasRuedas[i];
            if (wc == null || !wc.GetGroundHit(out WheelHit hit)) continue;

            // Slip angle en radianes (hit.sidewaysSlip ya es normalizado ≈ tangente del ángulo)
            float alpha = Mathf.Atan(hit.sidewaysSlip);

            float Fy = pacejkaD * Mathf.Sin(
                pacejkaC * Mathf.Atan(
                    pacejkaB * alpha
                    - pacejkaE * (pacejkaB * alpha - Mathf.Atan(pacejkaB * alpha))
                )
            );

            // Escalar por carga normal (suspensión comprimida = más agarre)
            float fuerzaNormal  = wc.sprungMass * Physics.gravity.magnitude;
            Vector3 fuerzaLat   = hit.sidewaysDir * (-Fy * fuerzaNormal);
            rb.AddForceAtPosition(fuerzaLat, hit.point, ForceMode.Force);

            // Humo de derrape
            if (humoRuedas != null && i < humoRuedas.Length && humoRuedas[i] != null)
            {
                bool derrapando = Mathf.Abs(hit.sidewaysSlip) > 0.45f;
                if (derrapando && !humoRuedas[i].isPlaying) humoRuedas[i].Play();
                else if (!derrapando && humoRuedas[i].isPlaying) humoRuedas[i].Stop();
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  BARRA ANTI-ROLL — evita el vuelco en curvas cerradas de Alsasua
    // ─────────────────────────────────────────────────────────────────────────

    private void AplicarAntiRoll()
    {
        BarraAntiRoll(rDI, rDD);
        BarraAntiRoll(rTI, rTD);
    }

    private void BarraAntiRoll(WheelCollider izq, WheelCollider der)
    {
        if (izq == null || der == null) return;
        bool gI = izq.GetGroundHit(out WheelHit hI);
        bool gD = der.GetGroundHit(out WheelHit hD);

        float vI = gI ? (-izq.transform.InverseTransformPoint(hI.point).y - izq.radius)
                        / izq.suspensionDistance : 1f;
        float vD = gD ? (-der.transform.InverseTransformPoint(hD.point).y - der.radius)
                        / der.suspensionDistance : 1f;

        float fuerza = (vI - vD) * antiRollForce;
        if (gI) rb.AddForceAtPosition(izq.transform.up * -fuerza, izq.transform.position);
        if (gD) rb.AddForceAtPosition(der.transform.up *  fuerza, der.transform.position);
    }

    private void LimitarVelocidad()
    {
        // Una sola lectura de magnitude (evita recalcular sqrt dos veces)
        float speed = rb.linearVelocity.magnitude;
        if (speed > velocidadMax)
            rb.linearVelocity = (rb.linearVelocity / speed) * velocidadMax;
    }

    /// <summary>
    /// Expulsa al jugador del vehículo de forma segura aunque la corrutina de cámara
    /// esté activa. Llamar desde <see cref="ControladorJugador.RecibirDano"/> cuando
    /// el jugador muere dentro del coche para evitar estado corrupto (CC desactivado,
    /// renderer invisible, transform hijo del coche).
    /// </summary>
    public void ForzarSalida()
    {
        if (!jugadorDentro) return;
        // Cancelar la corrutina de transición de cámara si está en curso
        if (_corrutinaCamara != null) { StopCoroutine(_corrutinaCamara); _corrutinaCamara = null; }
        SalirVehiculo();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CÁMARA EN VEHÍCULO — spring arm orbital independiente
    // ─────────────────────────────────────────────────────────────────────────

    private void ActualizarCamaraVehiculo()
    {
        if (camaraRef == null) return;

        var m  = Mouse.current;
        var kb = Keyboard.current;
        var gp = UnityEngine.InputSystem.Gamepad.current;

        // Leer input de cámara (ratón o stick derecho)
        if (m != null && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 delta = m.delta.ReadValue();
            _camH += delta.x * sensibilidadCamH * 0.05f;
            _camV -= delta.y * sensibilidadCamV * 0.05f;
        }
        else if (gp != null)
        {
            Vector2 stickDer = gp.rightStick.ReadValue();
            if (stickDer.magnitude > 0.08f)
            {
                _camH += stickDer.x * sensibilidadCamH * 2.5f * Time.deltaTime;
                _camV -= stickDer.y * sensibilidadCamV * 2.5f * Time.deltaTime;
            }
        }

        _camV = Mathf.Clamp(_camV, limCamV_Min, limCamV_Max);

        // Pivot: encima del coche
        Vector3 pivot = transform.position + Vector3.up * alturaPivotCoche;

        // Posición orbital
        Quaternion rot    = Quaternion.Euler(_camV, _camH, 0f);
        Vector3    offset = rot * (Vector3.back * distanciaOrbitaCoche);

        // Spring arm: evitar que la cámara traspase geometría
        // OPT: usa _maskSpringArmCached (cacheada en Awake) — no recalcular string lookup cada frame
        Vector3 posFinal = pivot + offset;
        if (Physics.Linecast(pivot, posFinal, out RaycastHit hit, _maskSpringArmCached))
            posFinal = hit.point + (pivot - posFinal).normalized * 0.2f;

        // Suavizado
        camaraRef.position = Vector3.Lerp(camaraRef.position, posFinal, velocidadTransCamara * Time.deltaTime);
        camaraRef.LookAt(pivot);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  COSMÉTICA
    // ─────────────────────────────────────────────────────────────────────────

    private void SincronizarMeshesRuedas()
    {
        SincMesh(rDI, meshDI); SincMesh(rDD, meshDD);
        SincMesh(rTI, meshTI); SincMesh(rTD, meshTD);
    }

    private static void SincMesh(WheelCollider wc, Transform m)
    {
        if (wc == null || m == null) return;
        wc.GetWorldPose(out Vector3 pos, out Quaternion rot);
        m.SetPositionAndRotation(pos, rot);
    }

    private void ActualizarMotorSonido()
    {
        if (motorAudio == null || clipMotor == null) return;
        if (!jugadorDentro) { if (motorAudio.isPlaying) motorAudio.Stop(); return; }
        if (!motorAudio.isPlaying) { motorAudio.clip = clipMotor; motorAudio.loop = true; motorAudio.Play(); }
        motorAudio.pitch = 0.75f
                         + Mathf.Abs(inputAcel) * 0.9f
                         + rb.linearVelocity.magnitude / velocidadMax * 0.35f;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    private WheelCollider[] Ruedas() => new[] { rDI, rDD, rTI, rTD };

    private void OnDrawGizmosSelected()
    {
        // Radio de entrada al vehículo
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 3.5f);
        // Asiento del conductor
        if (asientoConductor != null)
        { Gizmos.color = Color.yellow; Gizmos.DrawSphere(asientoConductor.position, 0.13f); }
    }
}
