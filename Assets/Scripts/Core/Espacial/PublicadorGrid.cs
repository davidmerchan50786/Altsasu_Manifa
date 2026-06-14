// Assets/Scripts/Core/Espacial/PublicadorGrid.cs
// ═══════════════════════════════════════════════════════════════════════════
//  PUBLICADOR GRID — productor genérico drop-on del Omni-Grid (capa CORE)
//
//  Arrástralo a cualquier GameObject que deba ser "visible" para el resto del
//  motor vía consultas espaciales (jugador, vehículos, props grandes). Publica su
//  posición al Omni-Grid cada frame (immediate-mode) con el TipoEspacial elegido.
//
//  Los NPC ya se publican solos (NPCBase); este componente cubre lo que NO es NPC.
//  Es aditivo: si no hay grid, no hace nada.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[AddComponentMenu("Alsasua/Espacial/Publicador Grid")]
public sealed class PublicadorGrid : MonoBehaviour
{
    [Tooltip("Tipo con el que se publica esta entidad en el Omni-Grid.")]
    [SerializeField] TipoEspacial tipo = TipoEspacial.Jugador;
    [Tooltip("Radio de influencia/tamaño (m) para tests de frustum y rangos.")]
    [SerializeField] float radio = 0.5f;

    IUnifiedSpatialGrid _grid;
    int _id;

    void Start()
    {
        _grid = ServiceLocator.Get<IUnifiedSpatialGrid>();
        _id   = gameObject.GetInstanceID();
    }

    void Update()
    {
        // Reintenta cachear si el grid aún no estaba listo en Start (orden de arranque).
        if (_grid == null) { _grid = ServiceLocator.Get<IUnifiedSpatialGrid>(); if (_grid == null) return; }
        _grid.Submit(new SpatialData { pos = transform.position, id = _id, tipo = tipo, radio = radio });
    }
}
