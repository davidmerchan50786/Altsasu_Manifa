// Assets/Scripts/Core/TerrenoGlobal.cs
// ═══════════════════════════════════════════════════════════════════════════
//  TERRENO GLOBAL — capa de COMPATIBILIDAD para lectores del terreno
//
//  Con el mosaico V2 hay 48 Terrains: Terrain.activeTerrain devuelve uno
//  ARBITRARIO y SampleHeight sobre él da alturas erróneas fuera de su tile.
//
//  Este helper estático es el sustituto directo:
//      float y   = TerrenoGlobal.AlturaMundo(pos);   // antes: activeTerrain.SampleHeight
//      Terrain t = TerrenoGlobal.TerrainEn(pos);     // antes: Terrain.activeTerrain
//
//  Resuelve vía ServiceLocator<ITerrainService> (tile-aware, O(1)) y cae a
//  Terrain.activeTerrain solo si el servicio aún no está registrado, de modo
//  que los ~20 lectores existentes pueden migrar incrementalmente sin romper
//  escenas legacy de un solo terreno.
//
//  Capa CORE: sin dependencias hacia Systems/Runtime.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

public static class TerrenoGlobal
{
    /// <summary>Altura del suelo (Y mundo) bajo la posición. Siempre usable;
    /// 0 solo si no hay ni servicio ni Terrain activo.</summary>
    public static float AlturaMundo(Vector3 posicionMundo)
    {
        // Preferir el muestreo matemático bit-exacto (Mosaico V3 Fase 0) si está activo
        // y AUTO-VALIDADO: Terrain.SampleHeight con el mosaico devuelve un tile arbitrario
        // (ver CLAUDE.md); el muestreador lee el RAW lattice 1/64 → correcto y determinista.
        var precis = ServiceLocator.Get<IMuestreadorAlturaPrecisa>();
        if (precis != null && precis.Listo)
            return precis.AlturaMundo(posicionMundo);

        var svc = ServiceLocator.Get<ITerrainService>();
        if (svc != null)
            return svc.AlturaMundo(posicionMundo);

        var t = Terrain.activeTerrain;
        if (t != null)
            return t.SampleHeight(posicionMundo) + t.transform.position.y;

        return 0f;
    }

    /// <summary>Sobrecarga (x, z).</summary>
    public static float AlturaMundo(float x, float z) => AlturaMundo(new Vector3(x, 0f, z));

    /// <summary>Tile del mosaico que contiene la posición (el más fino), o el
    /// terreno único, o Terrain.activeTerrain como último recurso.</summary>
    public static Terrain TerrainEn(Vector3 posicionMundo)
    {
        var svc = ServiceLocator.Get<ITerrainService>();
        if (svc != null)
        {
            var t = svc.TerrainEn(posicionMundo);
            if (t != null) return t;
        }
        return Terrain.activeTerrain;
    }

    /// <summary>True si el suelo jugable es el mosaico multi-tile.</summary>
    public static bool EsMosaico => ServiceLocator.Get<ITerrainService>()?.EsMosaico ?? false;
}
