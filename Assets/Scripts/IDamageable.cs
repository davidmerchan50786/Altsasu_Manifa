// Assets/Scripts/IDamageable.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONTRATO DE DAÑO — todo lo que puede recibir daño implementa esta interfaz
//
//  Implementantes actuales:
//    · ControladorJugador  — jugador (vida 100, Curar disponible)
//    · PoliciaForalIA      — enemigo NPC (vida 120, sin curación)
//    · VehiculoNPC         — vehículo IA (vida 80, explota al llegar a 0)
//
//  Uso desde sistemas de combate:
//    var damageable = hit.collider.GetComponent<IDamageable>();
//    damageable?.RecibirDano(25, hit.point, TipoDano.Bala);
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

public interface IDamageable
{
    int  Vida    { get; }
    int  VidaMax { get; }
    bool EstaMuerto { get; }

    /// <summary>Aplica daño. origen y tipo son opcionales.</summary>
    void RecibirDano(int cantidad, Vector3 origen = default, TipoDano tipo = TipoDano.Bala);

    /// <summary>Restaura vida. Las implementaciones que no soporten curación pueden ignorarlo.</summary>
    void Curar(int cantidad);
}

public enum TipoDano
{
    Bala,
    Explosion,
    Fuego,
    Impacto,   // colisión física
    Caida,
}
