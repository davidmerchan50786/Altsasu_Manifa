// Assets/Scripts/Core/ITerrainService.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONTRATO DEL TERRENO JUGABLE — capa CORE
//
//  Una única fuente de verdad sobre el suelo del mundo. Los consumidores
//  (Cesium, NavMesh, árboles, spawn, bootstrapper) NO buscan Terrain.activeTerrain
//  ni hacen polling ciego: preguntan a este servicio vía
//      ServiceLocator.Get<ITerrainService>()
//  o se suscriben a TerrenoListoEvent (EventBus).
//
//  Implementación: ServicioTerreno (capa WORLD/Systems).
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

/// <summary>Ciclo de vida del terreno jugable.</summary>
public enum EstadoTerreno
{
    Inicializando, // el servicio existe pero aún no ha decidido proveedor
    Generando,     // generación en curso (parseo DEM en hilo de fondo, etc.)
    Listo,         // hay suelo jugable definitivo (ver Fuente)
    Fallido        // ningún proveedor pudo dar suelo (no debería ocurrir: hay plano)
}

/// <summary>Proveedor que acabó dando el suelo.</summary>
public enum FuenteTerreno
{
    Ninguna,
    ExistenteValidado, // un Terrain de Alsasua ya presente en la escena (validado)
    DEM,               // generado desde dem_unity_1025.raw (6×6 km)
    Plano,             // plano de emergencia — jugable pero sin relieve
    Mosaico            // mosaico V2 multi-resolución (48 tiles, manifest_v2.json)
}

public interface ITerrainService
{
    EstadoTerreno Estado { get; }
    FuenteTerreno Fuente { get; }

    /// <summary>
    /// Terrain jugable. Con mosaico devuelve el TILE ANCLA (el que contiene
    /// Herriko Plaza) por compatibilidad con lectores de un solo terreno.
    /// Null si Fuente=Plano o aún no está Listo.
    /// </summary>
    Terrain Terreno { get; }

    bool EstaListo { get; }

    /// <summary>Altura del suelo (Y mundo) bajo una posición. Usable siempre:
    /// devuelve la altura del plano de emergencia si no hay Terrain.</summary>
    float AlturaMundo(Vector3 posicionMundo);

    /// <summary>Tile del mosaico que contiene la posición (el más fino si hay
    /// varios). Con terreno único devuelve ese terreno. Null si no hay tile.</summary>
    Terrain TerrainEn(Vector3 posicionMundo);

    /// <summary>Todos los tiles del suelo jugable (1 elemento si no es mosaico,
    /// vacío si Fuente=Plano). Lista estable una vez Listo.</summary>
    System.Collections.Generic.IReadOnlyList<Terrain> Tiles { get; }

    /// <summary>True si el suelo es el mosaico multi-tile V2.</summary>
    bool EsMosaico { get; }
}
