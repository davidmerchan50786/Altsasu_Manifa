// Assets/Scripts/Core/Events/MosaicoCompletoEvent.cs
// ═══════════════════════════════════════════════════════════════════════════
//  MOSAICO COMPLETO EVENT
//
//  Publicado por ServicioTerreno cuando TODOS los tiles del mosaico V2 están
//  instanciados con sus alturas (los 48). Distinto de TerrenoListoEvent, que
//  con mosaico se publica al completar el ANILLO 0 (el mundo ya es jugable).
//
//  Receptores típicos: SistemaDiagnostico (auditoría runtime), SistemaTerreno
//  (splatmaps de anillos exteriores), SistemaNevadasTerreno.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Todos los tiles del mosaico V2 están cargados.</summary>
public struct MosaicoCompletoEvent
{
    /// <summary>Número de tiles instanciados (== manifest).</summary>
    public int tiles;
    /// <summary>Segundos desde el arranque hasta completar el mosaico.</summary>
    public float segundosHastaCompleto;
}
