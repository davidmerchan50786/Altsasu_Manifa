// Assets/Scripts/Core/Events/PlayerDeathEvent.cs
// ═══════════════════════════════════════════════════════════════════════════
//  PLAYER DEATH EVENT
//
//  Struct ligero para EventBus (sin allocations). También existe como
//  ScriptableObject GameEvent para wiring desde el Inspector.
//
//  USO con EventBus (código):
//    EventBus.Publish(new PlayerDeathEvent { posicion = transform.position });
//    EventBus.Subscribe<PlayerDeathEvent>(OnPlayerDeath);
//
//  USO con ScriptableObject (Inspector):
//    Crear asset: Create → Altsasu → Events → PlayerDeathEvent
//    Drag el asset al campo del emisor; los receptores se suscriben en Inspector.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.Events;

// ── Struct para EventBus (zero alloc) ────────────────────────────────────────
/// <summary>
/// Publicado por ControladorJugador cuando la salud llega a 0.
/// Escuchado por: GameManagerAltsasua (respawn), HUDCanvas (fade negro),
/// SistemaPolish (efecto muerte), SistemaLogros, AudioManager.
/// </summary>
public struct PlayerDeathEvent
{
    /// <summary>Posición Unity donde murió el jugador.</summary>
    public Vector3 posicion;
    /// <summary>Causa de la muerte (bala, explosión, caída…).</summary>
    public string causa;
}

// ── ScriptableObject GameEvent (Inspector wiring) ────────────────────────────
/// <summary>
/// Asset ScriptableObject que permite disparar OnPlayerDeath desde el Inspector
/// sin referencias directas entre clases. Complementa (no reemplaza) EventBus.
/// </summary>
[CreateAssetMenu(menuName = "Altsasu/Events/PlayerDeathEvent", fileName = "PlayerDeathEvent")]
public class PlayerDeathEventSO : ScriptableObject
{
    [Tooltip("Listeners registrados desde el Inspector (drag componentes aquí).")]
    public UnityEvent<PlayerDeathEvent> OnRaise;

    /// <summary>Llamar para notificar a todos los listeners Inspector + EventBus.</summary>
    public void Raise(PlayerDeathEvent data)
    {
        OnRaise?.Invoke(data);
        EventBus.Publish(data);
    }
}
