// Assets/Scripts/Runtime/ProveedorTrayectoriaInput.cs
// ═══════════════════════════════════════════════════════════════════════════
//  PROVEEDOR DE TRAYECTORIA — entrada del jugador (Locomoción Fase 1)
//
//  Diseño: Docs/arquitectura_locomocion.md §2.3
//
//  Implementa IProveedorTrayectoria leyendo la dirección/velocidad deseadas de
//  ControladorJugador (ya relativas a la cámara) y suavizándolas con un
//  spring-damper crítico propio → produce la "trayectoria deseada" con
//  inercia y peso que consume Motion Matching (o, mientras tanto,
//  LocomocionAnimatorFallback).
//
//  ControladorJugador.Awake() añade este componente automáticamente (igual que
//  hace con SistemaFootIK en el Mixamo) y lo conecta a LocomocionAnimatorFallback.
//  No toca el Animator ni el CharacterController — solo produce datos.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[RequireComponent(typeof(ControladorJugador))]
public sealed class ProveedorTrayectoriaInput : MonoBehaviour, IProveedorTrayectoria
{
    [Tooltip("Frecuencia del spring-damper (Hz) que suaviza la velocidad deseada. " +
             "Mayor = reacciona más rápido (menos inercia/peso).")]
    [SerializeField, Range(0.5f, 10f)] float frecuenciaHz = 3f;

    // Tiempos de muestreo de la trayectoria (convención del diseño: 0, 0.2, 0.4, 0.6 s).
    const float T1 = 0.2f, T2 = 0.4f, T3 = 0.6f;

    ControladorJugador _jugador;

    // Estado del spring-damper: velocidad "deseada" suavizada (m/s, mundo).
    Vector3 _velSuavizada;

    public Vector3 PosicionActual => _jugador.transform.position;
    public Vector3 ForwardActual  => _jugador.transform.forward;

    void Awake() => _jugador = GetComponent<ControladorJugador>();

    void Update()
    {
        Vector3 velObjetivo = _jugador.DireccionMovimientoDeseada * _jugador.VelocidadObjetivo;

        // Suavizado exponencial hacia velObjetivo — equivalente a un spring-damper
        // crítico (sin overshoot) con constante de tiempo 1/omega.
        float omega = frecuenciaHz * 2f * Mathf.PI;
        float alfa  = 1f - Mathf.Exp(-omega * Time.deltaTime);
        _velSuavizada = Vector3.Lerp(_velSuavizada, velObjetivo, alfa);
    }

    public TrayectoriaDeseada MuestrearAhora()
    {
        Vector3 pos = PosicionActual;
        Vector3 fwd = _velSuavizada.sqrMagnitude > 0.01f ? _velSuavizada.normalized : ForwardActual;

        return new TrayectoriaDeseada
        {
            t0 = 0f, t1 = T1, t2 = T2, t3 = T3,
            p0 = pos,
            p1 = pos + _velSuavizada * T1,
            p2 = pos + _velSuavizada * T2,
            p3 = pos + _velSuavizada * T3,
            d0 = fwd, d1 = fwd, d2 = fwd, d3 = fwd,
            velocidadFinal = _velSuavizada.magnitude
        };
    }
}
