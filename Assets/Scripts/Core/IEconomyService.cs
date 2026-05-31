// Assets/Scripts/Core/IEconomyService.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONTRATO — Servicio de economía del jugador (dinero y puntuación)
//
//  Misiones y sistemas de gameplay hablan con esta interfaz en lugar de
//  referenciar GameManagerAltsasua directamente.
//
//  Implementación actual: GameManagerAltsasua
//  Consumidores: MisionesAltsasua, MisionesSec, SistemaGrafitis,
//                SistemaLogros, SistemaGuardado
// ═══════════════════════════════════════════════════════════════════════════

public interface IEconomyService
{
    /// <summary>Dinero actual del jugador.</summary>
    int Dinero { get; }

    /// <summary>Puntuación acumulada.</summary>
    int Puntuacion { get; }

    /// <summary>Añade cantidad al dinero y a la puntuación.</summary>
    void GanarDinero(int cantidad);

    /// <summary>Intenta gastar dinero. Devuelve false si no alcanza.</summary>
    bool GastarDinero(int cantidad);
}
