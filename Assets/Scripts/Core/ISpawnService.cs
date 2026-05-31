// Assets/Scripts/Core/ISpawnService.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONTRATO — Servicio de spawn de entidades dinámicas (policía, enemigos)
//
//  Implementación actual: GameManagerAltsasua
//  Consumidores: PoliciaForalIA (spawn de refuerzos), SistemaManifestacion
//
//  Nota: el spawn de vegetación (GameManagerAltsasua.SembrarArboles) debería
//  moverse a SistemaVegetacion en una refactorización futura — se documenta aquí
//  como deuda técnica identificada.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

public interface ISpawnService
{
    /// <summary>Notifica que un enemigo ha sido eliminado (para gestión de pool).</summary>
    void EnemigoEliminado(GameObject enemigo);

    /// <summary>True mientras el jugador conduce un vehículo.</summary>
    bool JugadorEnVehiculo { get; }

    /// <summary>Notifica el estado de conducción del jugador.</summary>
    void SetJugadorEnVehiculo(bool enVehiculo);
}
