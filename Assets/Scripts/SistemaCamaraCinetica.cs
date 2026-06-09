// Assets/Scripts/SistemaCamaraCinetica.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CÁMARA CINÉTICA — FOV kick, pullback, anticipación de movimiento
//
//  Extiende el spring arm de ControladorJugador con efectos de cámara AAA:
//    • FOV kick (+7°) y pullback (+0.45 m) al esprintar, suavizados
//    • Anticipación: la cámara se adelanta hacia la dirección de movimiento
//    • Screen-shake por trauma (compatible con SistemaPolish)
//    • Anulados automáticamente al apuntar
//
//  Uso: añadir este componente al mismo GameObject que ControladorJugador.
//  No modifica ControladorJugador — lee su velocidad por Rigidbody.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class SistemaCamaraCinetica : MonoBehaviour
{
    public static SistemaCamaraCinetica Instance { get; private set; }

    [Header("FOV Kick")]
    [SerializeField] Camera camaraObjetivo;
    [SerializeField] float  fovBase      = 75f;
    [SerializeField] float  fovKickMax   =  7f;    // grados extra al esprintar
    [SerializeField] float  fovAttack    =  0.35f; // s de subida
    [SerializeField] float  fovRelease   =  0.6f;  // s de bajada

    [Header("Pullback")]
    [SerializeField] Transform armaCamara;          // pivot del spring arm (opcional)
    [SerializeField] float pullbackMax   =  0.45f;  // m de retroceso al esprintar
    [SerializeField] float pullbackAttack = 0.35f;
    [SerializeField] float pullbackRelease = 0.6f;

    [Header("Anticipación")]
    [SerializeField] float lookAheadFactor = 0.04f; // 0 = sin anticipación, 0.1 = agresivo
    [SerializeField] float lookAheadClamp  = 0.8f;  // m máximos de adelanto
    [SerializeField] float lookAheadSmooth = 5f;    // Hz de suavizado

    [Header("Velocidad de sprint")]
    [SerializeField] float velocidadSprint = 5.5f;  // m/s mínimo para activar kick

    // ── Estado interno ─────────────────────────────────────────────────────
    Rigidbody _rb;
    float     _fovActual;
    float     _pullActual;
    Vector3   _lookAheadActual;
    bool      _apuntando;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _rb = GetComponent<Rigidbody>();
        if (camaraObjetivo == null) camaraObjetivo = Camera.main;
        if (camaraObjetivo != null) _fovActual = camaraObjetivo.fieldOfView;
    }

    void LateUpdate()
    {
        if (camaraObjetivo == null) return;

        float speed    = _rb != null ? new Vector3(_rb.linearVelocity.x, 0, _rb.linearVelocity.z).magnitude : 0f;
        bool  sprinting = speed > velocidadSprint && !_apuntando;
        float dt        = Time.deltaTime;

        // ── FOV kick ──────────────────────────────────────────────────────
        float fovObj = sprinting ? fovBase + fovKickMax : fovBase;
        float fovVel = sprinting ? (1f / fovAttack) : (1f / fovRelease);
        _fovActual = Mathf.Lerp(_fovActual, fovObj, dt * fovVel * 10f);
        camaraObjetivo.fieldOfView = _fovActual;

        // ── Pullback ───────────────────────────────────────────────────────
        if (armaCamara != null)
        {
            float pullObj = sprinting ? pullbackMax : 0f;
            float pullVel = sprinting ? (1f / pullbackAttack) : (1f / pullbackRelease);
            _pullActual = Mathf.Lerp(_pullActual, pullObj, dt * pullVel * 10f);
            // Aplicar como offset local Z del spring arm
            var lp = armaCamara.localPosition;
            lp.z = -_pullActual;
            armaCamara.localPosition = lp;
        }

        // ── Anticipación de movimiento ─────────────────────────────────────
        if (!_apuntando && _rb != null)
        {
            Vector3 vel2D = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
            Vector3 ahead = vel2D * lookAheadFactor;
            ahead = Vector3.ClampMagnitude(ahead, lookAheadClamp);
            _lookAheadActual = Vector3.Lerp(_lookAheadActual, ahead, dt * lookAheadSmooth);

            // Aplicar como offset de mirada al objetivo de la cámara
            camaraObjetivo.transform.localPosition =
                Vector3.Lerp(camaraObjetivo.transform.localPosition,
                             _lookAheadActual, dt * lookAheadSmooth);
        }
        else
        {
            _lookAheadActual = Vector3.Lerp(_lookAheadActual, Vector3.zero, dt * lookAheadSmooth * 2f);
        }
    }

    /// <summary>Llamar desde ControladorJugador al activar/desactivar mira.</summary>
    public static void SetApuntando(bool apuntando)
    {
        if (Instance != null) Instance._apuntando = apuntando;
    }

    /// <summary>Añade trauma de screen-shake (delega a SistemaPolish si existe).</summary>
    public static void AddTrauma(float cantidad)
    {
        SistemaPolish.AddTrauma(cantidad);
    }
}
