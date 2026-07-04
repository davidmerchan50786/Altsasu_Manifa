// Assets/Scripts/Runtime/ControladorVehiculoSimple.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONTROLADOR DE VEHÍCULO ROBADO — conducción ligera sin WheelColliders
//
//  Se añade en runtime cuando el jugador roba un VehiculoNPC.
//  No necesita WheelColliders: usa AddForce + AddTorque sobre el Rigidbody
//  del coche ya existente. El NPC ya tiene constraints (FreezeRotationX/Z)
//  que evitan el vuelco.
//
//  Controles:
//    W / ↑   — Acelerar
//    S / ↓   — Frenar / marcha atrás
//    A / ←   — Girar izquierda
//    D / →   — Girar derecha
//    Espacio — Freno de mano (reduce velocidad rápido)
//    E       — Salir del vehículo
//
//  Cámara: reutiliza la CamaraTP del ControladorJugador con spring arm orbital.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class ControladorVehiculoSimple : MonoBehaviour
{
    // ── Motor ─────────────────────────────────────────────────────────────────
    [Header("Motor")]
    [Tooltip("Fuerza de aceleración (N). ~8000 = coche familiar.")]
    [SerializeField] float fuerzaMotor      = 8000f;
    [Tooltip("Fuerza de frenado de servicio (N).")]
    [SerializeField] float fuerzaFreno      = 6000f;
    [Tooltip("Velocidad máxima (m/s). 18 m/s ≈ 65 km/h.")]
    [SerializeField] float velocidadMax     = 18f;
    [Tooltip("Par de giro (N·m). Escalado por velocidad.")]
    [SerializeField] float parGiro          = 3500f;
    [Tooltip("Amortiguación de aire cuando el jugador no toca el acelerador.")]
    [SerializeField] float dragSinInput     = 2.5f;
    [SerializeField] float dragConInput     = 0.3f;

    // ── Cámara ────────────────────────────────────────────────────────────────
    [Header("Cámara")]
    [SerializeField] float distOrbitaCoche  = 6.5f;
    [SerializeField] float alturaPivot      = 1.3f;
    [SerializeField] float sensH            = 2.8f;
    [SerializeField] float sensV            = 2.2f;
    [SerializeField] float limV_Min         = -15f;
    [SerializeField] float limV_Max         =  45f;
    [SerializeField] float suavCamara       = 8f;

    // ── Estado ────────────────────────────────────────────────────────────────
    Rigidbody          _rb;
    ControladorJugador _jugador;
    Transform          _camaraRef;
    Vector3            _camPosAntes;
    Quaternion         _camRotAntes;

    float _camH;
    float _camV = 15f;
    int   _maskSpringArm;

    bool       _activo;
    Coroutine  _corCamara;

    // ═══════════════════════════════════════════════════════════════════════════
    //  INICIO — llamado por VehiculoNPC.IniciarRobo()
    // ═══════════════════════════════════════════════════════════════════════════

    public void IniciarConduccion(ControladorJugador jugador)
    {
        _jugador = jugador;
        _rb      = GetComponent<Rigidbody>();
        _maskSpringArm = ~LayerMask.GetMask("Player", "Ignore Raycast");

        // ── Guardar cámara ────────────────────────────────────────────────────
        _camaraRef = jugador.CamaraTP?.transform;
        if (_camaraRef != null)
        {
            _camPosAntes = _camaraRef.position;
            _camRotAntes = _camaraRef.rotation;
        }

        // ── Desactivar control a pie ──────────────────────────────────────────
        var cc = jugador.GetComponent<CharacterController>();
        if (cc) cc.enabled = false;
        jugador.enabled = false;

        // ── Sentar al jugador en el asiento (posición estándar conductor) ─────
        jugador.transform.SetParent(transform);
        jugador.transform.localPosition = new Vector3(-0.42f, 0.48f, 0.05f);
        jugador.transform.localRotation = Quaternion.identity;
        foreach (var r in jugador.GetComponentsInChildren<Renderer>())
            r.enabled = false;

        // ── Inicializar ángulo de cámara con el heading actual del coche ──────
        _camH = transform.eulerAngles.y;
        _camV = 15f;

        // ── Notificar al sistema de HUD y IA ──────────────────────────────────
        ServiceLocator.Get<ISpawnService>()?.SetJugadorEnVehiculo(true);
        ControladorVehiculoJugador.NotificarEntradaExterna(null);

        // ── Reducir drag de la física NPC que era alto ────────────────────────
        if (_rb != null) _rb.linearDamping = dragConInput;

        _activo = true;
        AlsasuaLogger.Info("VehiculoRobado", $"Jugador conduciendo '{name}' (vehículo robado).");

        // Transición de cámara
        if (_corCamara != null) StopCoroutine(_corCamara);
        _corCamara = StartCoroutine(TransicionCamara(entrando: true));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  UPDATE / FIXED UPDATE
    // ═══════════════════════════════════════════════════════════════════════════

    void Update()
    {
        if (!_activo) return;

        var kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame)
            TerminarConduccion();

        ActualizarCamara();
    }

    void FixedUpdate()
    {
        if (!_activo || _rb == null) return;
        AplicarFisicaCoche();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  FÍSICA SIMPLIFICADA  (AddForce + AddTorque sobre el RB del NPC)
    // ═══════════════════════════════════════════════════════════════════════════

    void AplicarFisicaCoche()
    {
        var kb = Keyboard.current;
        var gp = Gamepad.current;

        float acel = 0f, giro = 0f;
        bool frenoMano = false;

        if (kb != null)
        {
            acel = (kb.wKey.isPressed || kb.upArrowKey.isPressed    ? 1f : 0f)
                 - (kb.sKey.isPressed || kb.downArrowKey.isPressed   ? 1f : 0f);
            giro = (kb.dKey.isPressed || kb.rightArrowKey.isPressed  ? 1f : 0f)
                 - (kb.aKey.isPressed || kb.leftArrowKey.isPressed   ? 1f : 0f);
            frenoMano = kb.spaceKey.isPressed;
        }
        else if (gp != null)
        {
            acel      = gp.leftStick.y.ReadValue();
            giro      = gp.leftStick.x.ReadValue();
            frenoMano = gp.leftShoulder.isPressed;
        }

        float speed = _rb.linearVelocity.magnitude;

        // ── Aceleración / frenado ─────────────────────────────────────────────
        if (acel > 0.05f && speed < velocidadMax)
        {
            _rb.AddForce(transform.forward * fuerzaMotor * acel, ForceMode.Force);
            _rb.linearDamping = dragConInput;
        }
        else if (acel < -0.05f)
        {
            // Freno de servicio o marcha atrás
            if (speed > 1f && Vector3.Dot(_rb.linearVelocity, transform.forward) > 0f)
                _rb.AddForce(-_rb.linearVelocity.normalized * fuerzaFreno * Mathf.Abs(acel), ForceMode.Force);
            else if (speed < velocidadMax * 0.4f) // marcha atrás limitada
                _rb.AddForce(transform.forward * fuerzaMotor * 0.5f * acel, ForceMode.Force);
            _rb.linearDamping = dragConInput;
        }
        else
        {
            // Sin input: freno de motor progresivo
            _rb.linearDamping = frenoMano ? 6f : dragSinInput;
        }

        // ── Giro (solo cuando el coche se mueve) ──────────────────────────────
        if (Mathf.Abs(giro) > 0.05f && speed > 1.2f)
        {
            // Invertir giro al ir en reversa
            float sentido = Vector3.Dot(_rb.linearVelocity, transform.forward) >= 0f ? 1f : -1f;
            float factorVel = Mathf.Clamp01(speed / (velocidadMax * 0.5f)); // más giro a baja velocidad
            _rb.AddTorque(Vector3.up * parGiro * giro * sentido * factorVel, ForceMode.Force);
        }

        // ── Cap de velocidad ──────────────────────────────────────────────────
        if (speed > velocidadMax)
            _rb.linearVelocity = _rb.linearVelocity * (velocidadMax / speed);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  CÁMARA ORBITAL  (spring arm idéntico al de ControladorVehiculoJugador)
    // ═══════════════════════════════════════════════════════════════════════════

    void ActualizarCamara()
    {
        if (_camaraRef == null) return;

        var m  = Mouse.current;
        var gp = Gamepad.current;

        if (m != null && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 delta = m.delta.ReadValue();
            _camH += delta.x * sensH * 0.05f;
            _camV -= delta.y * sensV * 0.05f;
        }
        else if (gp != null)
        {
            Vector2 stickD = gp.rightStick.ReadValue();
            if (stickD.magnitude > 0.08f)
            {
                _camH += stickD.x * sensH * 2.5f * Time.deltaTime;
                _camV -= stickD.y * sensV * 2.5f * Time.deltaTime;
            }
        }

        _camV = Mathf.Clamp(_camV, limV_Min, limV_Max);

        Vector3 pivot  = transform.position + Vector3.up * alturaPivot;
        Vector3 offset = Quaternion.Euler(_camV, _camH, 0f) * (Vector3.back * distOrbitaCoche);
        Vector3 posObj = pivot + offset;

        if (Physics.Linecast(pivot, posObj, out RaycastHit hit, _maskSpringArm))
            posObj = hit.point + (pivot - posObj).normalized * 0.18f;

        _camaraRef.position = Vector3.Lerp(_camaraRef.position, posObj, suavCamara * Time.deltaTime);
        _camaraRef.LookAt(pivot);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  SALIR DEL VEHÍCULO
    // ═══════════════════════════════════════════════════════════════════════════

    void TerminarConduccion()
    {
        if (!_activo || _jugador == null) return;
        _activo = false;

        // Sacar al jugador a un lateral del coche
        _jugador.transform.SetParent(null);
        _jugador.transform.position = transform.position
                                    + transform.right * 2.5f
                                    + Vector3.up * 0.55f;
        _jugador.transform.rotation = transform.rotation;

        // Reactivar control a pie
        var cc = _jugador.GetComponent<CharacterController>();
        if (cc) cc.enabled = true;
        _jugador.enabled = true;
        foreach (var r in _jugador.GetComponentsInChildren<Renderer>())
            r.enabled = true;

        // El coche queda aparcado (NPC no se reactiva — coche robado abandona ruta)
        if (_rb != null) _rb.linearDamping = 5f; // frenar progresivamente

        ServiceLocator.Get<ISpawnService>()?.SetJugadorEnVehiculo(false);
        ControladorVehiculoJugador.NotificarSalidaExterna(null);

        // Transición de cámara de vuelta al spring arm del jugador
        if (_corCamara != null) StopCoroutine(_corCamara);
        _corCamara = StartCoroutine(TransicionCamara(entrando: false));

        AlsasuaLogger.Info("VehiculoRobado", $"Jugador salió de '{name}'.");
        Destroy(this, 0.7f); // dar tiempo a la transición de cámara
    }

    // ── Transición suave de cámara ────────────────────────────────────────────

    IEnumerator TransicionCamara(bool entrando)
    {
        if (_camaraRef == null) yield break;

        float t = 0f, dur = 0.5f;
        Vector3    p0 = _camaraRef.position;
        Quaternion r0 = _camaraRef.rotation;
        Vector3    p1;
        Quaternion r1;

        if (entrando)
        {
            Vector3 pivot = transform.position + Vector3.up * alturaPivot;
            p1 = pivot + Quaternion.Euler(_camV, _camH, 0f) * (Vector3.back * distOrbitaCoche);
            r1 = Quaternion.LookRotation(pivot - p1, Vector3.up);
        }
        else
        {
            p1 = _camPosAntes;
            r1 = _camRotAntes;
        }

        while (t < dur)
        {
            t += Time.deltaTime;
            float s = Mathf.SmoothStep(0f, 1f, t / dur);
            _camaraRef.position = Vector3.Lerp(p0, p1, s);
            _camaraRef.rotation = Quaternion.Slerp(r0, r1, s);
            yield return null;
        }
        _camaraRef.position = p1;
        _camaraRef.rotation = r1;
        _corCamara = null;
    }
}
