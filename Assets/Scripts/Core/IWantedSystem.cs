// Assets/Scripts/Core/IWantedSystem.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONTRATO — Sistema de nivel de búsqueda policial (wanted/estrellas)
//
//  Cualquier sistema de gameplay que genere calor policial habla con esta
//  interfaz en lugar de depender de GameManagerAltsasua directamente.
//
//  Implementación actual: GameManagerAltsasua
//  Consumidores: PoliciaForalIA, SistemaArmasExtendido, SistemaDestruccion,
//                MisionesAltsasua, MisionesSec, SistemaGrafitis
// ═══════════════════════════════════════════════════════════════════════════

public interface IWantedSystem
{
    /// <summary>Nivel actual (0-5).</summary>
    int NivelBusqueda { get; }

    /// <summary>Añade o resta estrellas. Negativo baja el nivel.</summary>
    void AumentarBusqueda(int cantidad);

    /// <summary>Fija el nivel directamente (sin sumar). Clamp 0-5.</summary>
    void FijarBusqueda(int nivel);
}
