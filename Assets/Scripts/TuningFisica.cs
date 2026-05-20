// Assets/Scripts/TuningFisica.cs
// ═══════════════════════════════════════════════════════════════════════════
//  TUNING DE FÍSICA — ajustes runtime para vehículos y jugador
//
//  • WheelCollider tuning: fricción longitudinal/lateral Pacejka
//  • Suspensión adaptativa según carga y velocidad
//  • Anti-roll bar para evitar volcado
//  • Jugador: fricción de suelo adaptativa (correr/caminar/agacharse)
//  • Gravedad personalizada con componente de viento
//  • Physics.bounceThreshold, sleepThreshold optimizados
//
//  Se aplica en Awake — los valores son editables en Inspector.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[DefaultExecutionOrder(-50)]
public class TuningFisica : MonoBehaviour
{
    public static TuningFisica Instance { get; private set; }

    // ── WheelCollider (Pacejka) ───────────────────────────────────────────
    [Header("Fricción longitudinal (aceleración/frenada)")]
    [Range(1f, 3f)] public float extremumSlipLong  = 0.4f;
    [Range(1f, 5f)] public float extremumValueLong = 1.0f;
    [Range(1f, 3f)] public float asymptoteSlipLong = 0.8f;
    [Range(0.5f,3f)]public float asymptoteValueLong= 0.75f;

    [Header("Fricción lateral (giro)")]
    [Range(0.1f,1f)] public float extremumSlipLat  = 0.2f;
    [Range(1f, 4f)]  public float extremumValueLat = 1.0f;
    [Range(0.5f,2f)] public float asymptoteSlipLat = 0.5f;
    [Range(0.5f,3f)] public float asymptoteValueLat= 0.75f;

    [Header("Suspensión")]
    [Range(0.1f,0.5f)] public float springDist      = 0.3f;
    [Range(10000f,60000f)]public float springForce  = 35000f;
    [Range(1000f,6000f)] public float springDamper  = 4500f;

    [Header("Anti-roll bar")]
    [Range(0f, 50000f)] public float fuerzaAntiRoll = 8000f;

    [Header("Jugador")]
    [Range(0f, 10f)] public float friccionSuelo        = 6f;
    [Range(0f, 10f)] public float friccionSueloCorrer  = 4f;
    [Range(0f, 10f)] public float friccionSueloAgachar = 8f;

    [Header("Física global")]
    public float gravedad   = -9.81f;
    public float bounceThreshold = 2f;
    public float sleepThreshold  = 0.005f;

    // ── Estado ────────────────────────────────────────────────────────────
    WheelFrictionCurve _fricLong, _fricLat;

    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        AplicarFisicaGlobal();
        CalcularCurvas();
    }

    void Start()
    {
        AplicarWheelColliders();
    }

    void AplicarFisicaGlobal()
    {
        Physics.gravity          = new Vector3(0f, gravedad, 0f);
        Physics.bounceThreshold  = bounceThreshold;
        Physics.sleepThreshold   = sleepThreshold;
        Physics.defaultContactOffset = 0.01f;
        Physics.defaultSolverIterations        = 8;
        Physics.defaultSolverVelocityIterations = 2;
    }

    void CalcularCurvas()
    {
        _fricLong = new WheelFrictionCurve
        {
            extremumSlip      = extremumSlipLong,
            extremumValue     = extremumValueLong,
            asymptoteSlip     = asymptoteSlipLong,
            asymptoteValue    = asymptoteValueLong,
            stiffness         = 1f
        };
        _fricLat = new WheelFrictionCurve
        {
            extremumSlip      = extremumSlipLat,
            extremumValue     = extremumValueLat,
            asymptoteSlip     = asymptoteSlipLat,
            asymptoteValue    = asymptoteValueLat,
            stiffness         = 1f
        };
    }

    void AplicarWheelColliders()
    {
        var wcs = FindObjectsByType<WheelCollider>(FindObjectsSortMode.None);
        foreach (var wc in wcs)
        {
            wc.forwardFriction  = _fricLong;
            wc.sidewaysFriction = _fricLat;

            var spring = wc.suspensionSpring;
            spring.spring   = springForce;
            spring.damper   = springDamper;
            spring.targetPosition = 0.5f;
            wc.suspensionSpring = spring;
            wc.suspensionDistance = springDist;
            wc.forceAppPointDistance = 0.1f;
        }
        if (wcs.Length > 0)
            AlsasuaLogger.Info("TuningFisica", $"Tuning aplicado a {wcs.Length} WheelColliders");
    }

    void FixedUpdate()
    {
        AplicarAntiRoll();
        AplicarFriccionJugador();
    }

    // ── Anti-roll bar ─────────────────────────────────────────────────────

    void AplicarAntiRoll()
    {
        var vehiculo = AltsasuCore.I?.traficoSystem;
        // Anti-roll sobre el coche del jugador
        var cocheJugador = FindFirstObjectByType<ControladorVehiculoJugador>();
        if (cocheJugador == null || !cocheJugador.JugadorDentro) return;

        var wcs = cocheJugador.GetComponentsInChildren<WheelCollider>();
        if (wcs.Length < 4) return;

        // Eje delantero (wcs[0]=FL, wcs[1]=FR)
        AplicarARB(wcs[0], wcs[1], cocheJugador.GetComponent<Rigidbody>());
        // Eje trasero (wcs[2]=RL, wcs[3]=RR)
        AplicarARB(wcs[2], wcs[3], cocheJugador.GetComponent<Rigidbody>());
    }

    void AplicarARB(WheelCollider wcL, WheelCollider wcR, Rigidbody rb)
    {
        if (wcL == null || wcR == null || rb == null) return;
        WheelHit hitL, hitR;
        bool enSueloL = wcL.GetGroundHit(out hitL);
        bool enSueloR = wcR.GetGroundHit(out hitR);

        float viajeL = enSueloL ? (-wcL.transform.InverseTransformPoint(hitL.point).y - wcL.radius) / wcL.suspensionDistance : 0f;
        float viajeR = enSueloR ? (-wcR.transform.InverseTransformPoint(hitR.point).y - wcR.radius) / wcR.suspensionDistance : 0f;

        float fuerza = (viajeL - viajeR) * fuerzaAntiRoll;
        if (enSueloL) rb.AddForceAtPosition(wcL.transform.up * -fuerza, wcL.transform.position);
        if (enSueloR) rb.AddForceAtPosition(wcR.transform.up *  fuerza, wcR.transform.position);
    }

    // ── Fricción jugador ──────────────────────────────────────────────────

    void AplicarFriccionJugador()
    {
        var jugador = AltsasuCore.Jugador;
        if (jugador == null) return;
        var rb = jugador.GetComponent<Rigidbody>();
        if (rb == null) return;

        var kb = UnityEngine.InputSystem.Keyboard.current;
        bool corriendo = kb != null && kb.leftShiftKey.isPressed;
        bool agachado  = kb != null && kb.leftCtrlKey.isPressed;

        float fric = agachado ? friccionSueloAgachar
                   : corriendo ? friccionSueloCorrer
                   : friccionSuelo;
        rb.linearDamping = fric;
    }

    // ── API pública ───────────────────────────────────────────────────────

    /// <summary>Re-aplica el tuning a todos los WheelColliders (útil tras spawn de coche).</summary>
    public static void ReaplicarWheels() => Instance?.AplicarWheelColliders();

    /// <summary>Ajusta la gravedad temporalmente (viento, zona especial).</summary>
    public static void SetGravedad(float y) => Physics.gravity = new Vector3(0f, y, 0f);
}
