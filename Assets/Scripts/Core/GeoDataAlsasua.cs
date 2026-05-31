// Assets/Scripts/GeoDataAlsasua.cs
// ═══════════════════════════════════════════════════════════════════════════
//  DATOS GEOGRÁFICOS CENTRALIZADOS — constantes y utilidades del terreno
//
//  Único punto de verdad para:
//    · Offsets OSM→Unity (Herriko Plaza como origen)
//    · Constante de altura por planta
//    · AlturaTerreno() — antes duplicada en 5 generadores
//    · Puntos geográficos clave del pueblo
//
//  Uso:
//    float y = GeoDataAlsasua.AlturaTerreno(x, z);
//    float ux = vert.x + GeoDataAlsasua.OX;
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

public static class GeoDataAlsasua
{
    // ── Coordenadas geográficas reales ────────────────────────────────────
    public const double LATITUD_CENTRO   = 42.9003;
    public const double LONGITUD_CENTRO  = -2.1665;
    public const double M_POR_GRADO_LAT  = 111_320.0;
    public const double M_POR_GRADO_LON  = 81_560.0; // a lat 42.9°N

    // ── Offsets OSM → Unity ───────────────────────────────────────────────
    /// <summary>Offset X: origen OSM relativo a Herriko Plaza en Unity.</summary>
    public const float OX = 1918f;

    /// <summary>Offset Z: origen OSM relativo a Herriko Plaza en Unity.</summary>
    public const float OZ = 8570f;

    // ── Métricas urbanas ──────────────────────────────────────────────────
    /// <summary>Altura estándar por planta (m) cuando el edificio OSM no especifica altura.</summary>
    public const float ALT_PLANTA = 3.2f;

    /// <summary>Altura de fallback cuando no hay terreno activo.</summary>
    public const float ALT_FALLBACK = 240f;

    // ── Puntos geográficos clave ──────────────────────────────────────────
    public static readonly Vector3 HerrikoPlaza    = new(1918f, 0f, 8570f);
    public static readonly Vector3 EstacionTren    = new(1650f, 0f, 8200f);
    public static readonly Vector3 CuartelGC       = new(2180f, 0f, 8720f);
    public static readonly Vector3 CarreteraN1     = new(1900f, 0f, 8000f);
    public static readonly Vector3 MonteAralar     = new(3200f, 0f, 9400f);
    public static readonly Vector3 BarrioNorte     = new(1820f, 0f, 8850f);
    public static readonly Vector3 PoligonoIsasia  = new(2300f, 0f, 8400f);

    // ── AlturaTerreno — implementación única con caché de Terrain ────────

    private static Terrain _terrainCache;

    /// <summary>
    /// Devuelve la altura del terreno en las coordenadas Unity (x, z).
    /// Cachea el Terrain activo; si no hay terreno usa raycast como fallback.
    /// Antes duplicada en: GeneradorMundoOSM, GeneradorGeometriaPrecisa,
    /// AplicadorOrtofoto, PosicionadorPrecisionUrbana, GeneradorCallesAltsasu.
    /// </summary>
    public static float AlturaTerreno(float x, float z)
    {
        if (_terrainCache == null) _terrainCache = Terrain.activeTerrain;

        if (_terrainCache != null)
            return _terrainCache.SampleHeight(new Vector3(x, 0, z))
                 + _terrainCache.transform.position.y;

        // Fallback: raycast desde el cielo
        if (Physics.Raycast(new Vector3(x, 1000f, z), Vector3.down, out var hit, 2000f))
            return hit.point.y;

        return ALT_FALLBACK;
    }

    /// <summary>Sobrecarga Vector3 — ignora la componente Y del input.</summary>
    public static float AlturaTerreno(Vector3 pos) => AlturaTerreno(pos.x, pos.z);

    /// <summary>Invalida la caché del terreno (llamar tras cargar nueva escena o terreno).</summary>
    public static void InvalidarCache() => _terrainCache = null;

    // ── Utilidades de distancia 2D ────────────────────────────────────────

    /// <summary>Distancia ignorando la componente Y (útil para zonas geográficas).</summary>
    public static float Dist2D(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Convierte vértice OSM (relativo) a posición Unity absoluta sin altura.</summary>
    public static Vector3 OSMaUnity(float osmX, float osmZ)
        => new(osmX + OX, 0f, osmZ + OZ);

    /// <summary>Convierte vértice OSM a posición Unity con altura de terreno.</summary>
    public static Vector3 OSMaUnityConAltura(float osmX, float osmZ)
    {
        float x = osmX + OX, z = osmZ + OZ;
        return new Vector3(x, AlturaTerreno(x, z), z);
    }

    /// <summary>Altura total de un edificio OSM según niveles o height explícita.</summary>
    public static float AlturaEdificio(int levels, float height)
        => height > 0f ? height : Mathf.Max(ALT_PLANTA, levels * ALT_PLANTA);
}
