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

    /// <summary>Lanza una OLEADA de refuerzos policiales cerca de 'posicion', con
    /// tamaño escalado por el nivel de búsqueda (mínimo 'cantidadBase'), llegada
    /// escalonada en el tiempo y cooldown global anti-spam. Devuelve el tamaño de
    /// oleada despachado, o 0 si está en cooldown / ya hay una en curso. La invoca
    /// PoliciaForalIA cuando el plan GOAP decide pedir apoyo.</summary>
    int SolicitarRefuerzosPolicia(Vector3 posicion, int cantidadBase);

    /// <summary>True mientras el jugador conduce un vehículo.</summary>
    bool JugadorEnVehiculo { get; }

    /// <summary>Notifica el estado de conducción del jugador.</summary>
    void SetJugadorEnVehiculo(bool enVehiculo);
}
