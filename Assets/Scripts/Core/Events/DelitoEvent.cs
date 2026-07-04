// Assets/Scripts/Core/Events/DelitoEvent.cs
// ═══════════════════════════════════════════════════════════════════════════
//  DELITO EVENT
//
//  Struct ligero para EventBus (sin allocations). Se publica cuando el jugador
//  comete un delito LOCALIZABLE (destrucción, disparo, agresión…). Desacopla el
//  código de delito de quién reacciona: hoy lo escucha SistemaTestigos (los
//  vecinos que lo ven te delatan o te cubren según el apoyo). Mañana podría
//  escucharlo cualquier otro sistema sin tocar los sitios de delito.
//
//  USO con EventBus (código):
//    EventBus.Publish(new DelitoEvent { lugar = GeoDataAlsasua.JugadorPos(), gravedad = 0.5f });
//    EventBus.Subscribe<DelitoEvent>(OnDelito);
//
//  Publicado por: SistemaDestruccion, SistemaConsecuencias, SistemaArmasExtendido.
//  Escuchado por: SistemaTestigos (cuando está activo).
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

/// <summary>Disparado cuando el jugador comete un delito visible en un lugar.</summary>
public struct DelitoEvent
{
    /// <summary>Posición Unity del delito (normalmente la del jugador).</summary>
    public Vector3 lugar;
    /// <summary>Gravedad 0..1 (pintada ~0.2 … algo gordo ~1.0).</summary>
    public float gravedad;
}
