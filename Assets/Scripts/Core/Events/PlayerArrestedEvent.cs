// Assets/Scripts/Core/Events/PlayerArrestedEvent.cs
// ═══════════════════════════════════════════════════════════════════════════
//  PLAYER ARRESTED EVENT
//
//  Struct ligero para EventBus (sin allocations). Espejo de PlayerDeathEvent
//  para el flujo de detención (la Policía Foral arresta al jugador).
//
//  USO con EventBus (código):
//    EventBus.Publish(new PlayerArrestedEvent { posicion = transform.position });
//    EventBus.Subscribe<PlayerArrestedEvent>(OnPlayerArrested);
//
//  Publicado por: CerebroGOAPPolicia.Arrestar().
//  Escuchado por: HUDCanvas (fade negro + "detenido"). GameManagerAltsasua puede
//  suscribirse para la política de respawn/game-over (decisión de gameplay).
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

/// <summary>Disparado cuando un policía consuma el arresto del jugador.</summary>
public struct PlayerArrestedEvent
{
    /// <summary>Posición Unity donde se produjo el arresto.</summary>
    public Vector3 posicion;
    /// <summary>Nombre del policía que arrestó (para log/telemetría).</summary>
    public string policia;
}
