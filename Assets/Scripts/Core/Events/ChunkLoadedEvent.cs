// Assets/Scripts/Core/Events/ChunkLoadedEvent.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CHUNK LOADED / UNLOADED EVENT
//
//  Publicado por SistemaZonas cuando un chunk OSM se activa o desactiva.
//  Permite que Gameplay y UI reaccionen al streaming sin acoplar a SistemaZonas.
//
//  USO con EventBus:
//    EventBus.Publish(new ChunkLoadedEvent { coord = coord, cargado = true });
//    EventBus.Subscribe<ChunkLoadedEvent>(OnChunkCargado);
//
//  Emisores esperados: SistemaZonas.CargarZona / DescargarZona
//  Receptores típicos: SistemaNavMesh (re-hornear), SistemaEdificiosAAA,
//                      SistemaIA (registrar waypoints), AudioManager (ambience).
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.Events;

// ── Struct para EventBus ─────────────────────────────────────────────────────
/// <summary>
/// Un chunk del mundo OSM ha sido cargado (cargado=true) o descargado (cargado=false).
/// </summary>
public struct ChunkLoadedEvent
{
    /// <summary>Coordenada de chunk en la cuadrícula de zonas.</summary>
    public Vector2Int coord;
    /// <summary>True = recién cargado. False = recién descargado.</summary>
    public bool cargado;
    /// <summary>Centro del chunk en coordenadas Unity (metros).</summary>
    public Vector3 centroUnity;
}

// ── ScriptableObject GameEvent ────────────────────────────────────────────────
[CreateAssetMenu(menuName = "Altsasu/Events/ChunkLoadedEvent", fileName = "ChunkLoadedEvent")]
public class ChunkLoadedEventSO : ScriptableObject
{
    [Tooltip("Listeners registrados desde el Inspector.")]
    public UnityEvent<ChunkLoadedEvent> OnRaise;

    public void Raise(ChunkLoadedEvent data)
    {
        OnRaise?.Invoke(data);
        EventBus.Publish(data);
    }
}
