// Assets/Scripts/VehiculoBase.cs
// ═══════════════════════════════════════════════════════════════════════════
//  BASE CLASS para vehículos — boilerplate compartido entre:
//    · VehiculoNPC              (tráfico IA con waypoints)
//    · ControladorVehiculoJugador (vehículo del jugador con WheelColliders)
//
//  Centraliza:
//    · Rigidbody + inicialización en Awake
//    · IDamageable completo (vida, vidaMax, destruido, RecibirDano, Curar)
//    · velocidadMax como parámetro configurable
//    · Hooks abstractos/virtuales para lógica específica de cada tipo
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public abstract class VehiculoBase : MonoBehaviour, IDamageable
{
    [Header("Salud")]
    [Tooltip("Puntos de vida máximos. Al llegar a 0 se llama IniciarDestruccion().")]
    [SerializeField] protected int vidaMaxima = 100;

    [Header("Movimiento")]
    [Tooltip("Velocidad máxima del vehículo (m/s).")]
    [SerializeField] protected float velocidadMax = 10f;

    // ── Estado ────────────────────────────────────────────────────────────
    protected Rigidbody _rb;
    protected int       _vida;
    protected bool      _destruido;

    // ── IDamageable ───────────────────────────────────────────────────────
    public int  Vida       => _vida;
    public int  VidaMax    => vidaMaxima;
    public bool EstaMuerto => _destruido;

    // ════════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ════════════════════════════════════════════════════════════════════════

    protected virtual void Awake()
    {
        _rb   = GetComponent<Rigidbody>();
        _vida = vidaMaxima;

        // Publica el vehículo en el Omni-Grid → consultas de proximidad (atropellos,
        // IA de tráfico, "vehículos cerca del jugador") en O(1). Aditivo: si no hay
        // grid, PublicadorGrid es no-op.
        if (GetComponent<PublicadorGrid>() == null)
            gameObject.AddComponent<PublicadorGrid>().Configurar(TipoEspacial.Vehiculo, 2f);

        OnAwakeVehiculo();
    }

    /// <summary>Hook para inicialización adicional en Awake (sin reemplazar el boilerplate).</summary>
    protected virtual void OnAwakeVehiculo() { }

    // ════════════════════════════════════════════════════════════════════════
    //  IDamageable — implementación compartida
    // ════════════════════════════════════════════════════════════════════════

    public void RecibirDano(int cantidad, Vector3 origen = default, TipoDano tipo = TipoDano.Bala)
    {
        if (_destruido) return;
        _vida = Mathf.Max(0, _vida - cantidad);
        OnDanoRecibido(cantidad, origen, tipo);
        if (_vida <= 0) IniciarDestruccion();
    }

    public void Curar(int cantidad)
        => _vida = Mathf.Min(vidaMaxima, _vida + cantidad);

    // ════════════════════════════════════════════════════════════════════════
    //  HOOKS ABSTRACTOS
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Feedback visual/sonoro al recibir daño (oscurecer carrocería, humo, etc.).
    /// Se llama ANTES de comprobar si vida ≤ 0.
    /// </summary>
    protected abstract void OnDanoRecibido(int cantidad, Vector3 origen, TipoDano tipo);

    /// <summary>
    /// Lógica de destrucción específica del vehículo (explosión, ForzarSalida, evento…).
    /// Se llama cuando _vida llega a 0. Responsabilidad de la subclase poner _destruido = true.
    /// </summary>
    protected abstract void IniciarDestruccion();
}
