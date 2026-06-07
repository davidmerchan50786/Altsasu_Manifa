// Assets/Scripts/Core/Events/ZoneChangedEvent.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ZONE CHANGED EVENT
//
//  Publicado por SistemaZonas cuando el jugador entra en una nueva zona
//  de gameplay (manifestación, zona policial, zona tranquila, etc.).
//
//  USO con EventBus:
//    EventBus.Publish(new ZoneChangedEvent { zonaAnterior = prev, zonaNueva = nueva });
//    EventBus.Subscribe<ZoneChangedEvent>(OnZonaCambiada);
//
//  Emisores: SistemaZonas (trigger OnTriggerEnter de zonas), GestorZonasAlsasua
//  Receptores: SistemaReverbZonas (cambiar reverb), AudioManager (música ambiental),
//              SistemaIA (alertar NPCs), HUDCanvas (nombre de zona), SistemaManifestacion.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.Events;

// ── Enum de tipos de zona ────────────────────────────────────────────────────
public enum TipoZona
{
    Desconocida = 0,
    CalleNormal,
    ZonaComercial,
    ZonaResidencial,
    ZonaPolicial,
    ZonaManifestacion,
    ZonaIndustrial,
    ZonaNatural,
    Interior,
}

// ── Struct para EventBus ─────────────────────────────────────────────────────
/// <summary>
/// El jugador ha entrado en una zona diferente del mundo.
/// </summary>
public struct ZoneChangedEvent
{
    /// <summary>Tipo de zona de la que viene el jugador.</summary>
    public TipoZona zonaAnterior;
    /// <summary>Tipo de zona a la que acaba de entrar el jugador.</summary>
    public TipoZona zonaNueva;
    /// <summary>Nombre legible de la zona nueva (ej: "Herriko Plaza").</summary>
    public string nombreZona;
    /// <summary>Centro de la zona en coordenadas Unity.</summary>
    public Vector3 centroUnity;
}

// ── ScriptableObject GameEvent ────────────────────────────────────────────────
[CreateAssetMenu(menuName = "Altsasu/Events/ZoneChangedEvent", fileName = "ZoneChangedEvent")]
public class ZoneChangedEventSO : ScriptableObject
{
    [Tooltip("Listeners registrados desde el Inspector.")]
    public UnityEvent<ZoneChangedEvent> OnRaise;

    public void Raise(ZoneChangedEvent data)
    {
        OnRaise?.Invoke(data);
        EventBus.Publish(data);
    }
}
