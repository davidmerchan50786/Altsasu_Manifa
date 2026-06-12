// Assets/Scripts/Core/Events/TerrenoListoEvent.cs
// ═══════════════════════════════════════════════════════════════════════════
//  TERRENO LISTO EVENT
//
//  Publicado por ServicioTerreno (una sola vez por escena) cuando el suelo
//  jugable queda resuelto — adoptado, generado desde DEM, o plano de emergencia.
//
//  USO:
//    EventBus.Subscribe<TerrenoListoEvent>(OnTerrenoListo);   // en OnEnable
//    EventBus.Unsubscribe<TerrenoListoEvent>(OnTerrenoListo); // en OnDisable
//
//  Receptores típicos: CesiumFondoLejano (anclaje/calibración),
//  SistemaNavMesh (horneado), AlsasuaTreeStreamer (clasificación especies).
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

/// <summary>El suelo jugable de la escena está resuelto.</summary>
public struct TerrenoListoEvent
{
    /// <summary>Terrain jugable (null si fuente=Plano).</summary>
    public Terrain terreno;
    /// <summary>Proveedor que dio el suelo.</summary>
    public FuenteTerreno fuente;
    /// <summary>Segundos desde el arranque hasta tener suelo.</summary>
    public float segundosHastaListo;
}
