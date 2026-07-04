// Assets/Scripts/Runtime/Negocio.cs
// ═══════════════════════════════════════════════════════════════════════════
//  NEGOCIO — bar / comercio / empresa / industria que el jugador puede poner
//  bajo "impuesto revolucionario" (extorsión, mecánica de juego ficticia).
//
//  Coloca este componente en el GameObject de un edificio. Es IInteractable:
//  con [E] el jugador lo extorsiona (vía SistemaEconomiaCriminal). Un negocio
//  bajo control paga un ingreso periódico; extorsionar sube la búsqueda y baja
//  algo el apoyo (coacción).
//
//  Capa RUNTIME. FICCIÓN — mecánica de crimen organizado de mundo abierto.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;

public sealed class Negocio : MonoBehaviour, IInteractable
{
    public enum Tipo { Bar, Comercio, Empresa, Industria }
    public enum Estado { Libre, Extorsionado }

    [SerializeField] public Tipo  tipo = Tipo.Bar;
    [SerializeField] public string nombre = "Negocio";
    public Estado estado { get; private set; } = Estado.Libre;

    /// <summary>Ingreso por minuto cuando está extorsionado (escala por tipo).</summary>
    public int IngresoMin => tipo switch
    {
        Tipo.Bar       => 30,
        Tipo.Comercio  => 50,
        Tipo.Empresa   => 120,
        Tipo.Industria => 300,
        _              => 30
    };

    void Start() { SistemaEconomiaCriminal.I?.Registrar(this); }
    void OnDestroy() { SistemaEconomiaCriminal.I?.Quitar(this); }

    public void PonerBajoControl() => estado = Estado.Extorsionado;
    public void Liberar()          => estado = Estado.Libre;

    // ── IInteractable ─────────────────────────────────────────────────────
    public string TextoInteraccion => estado == Estado.Libre
        ? $"[E] Cobrar impuesto revolucionario ({nombre})"
        : $"{nombre} — bajo control";
    public float RadioInteraccion => 3f;
    public bool  PuedeInteractuar => estado == Estado.Libre;
    public void  OnInteractuar(ControladorJugador jugador)
        => SistemaEconomiaCriminal.I?.Extorsionar(this);
}
