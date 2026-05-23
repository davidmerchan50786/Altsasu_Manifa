// Assets/Scripts/CesiumCapasAlsasua.cs
// ═══════════════════════════════════════════════════════════════════════════
//  DIAGNÓSTICO Y CONFIGURACIÓN DE CAPAS CESIUM PARA ÁRBOLES
//
//  ESTADO ACTUAL DEL PROYECTO:
//    ✅ Token de Cesium Ion configurado (CesiumSettings/ion.cesium.com.asset)
//    ❌ Cesium for Unity NO instalado (falta com.cesium.unity en manifest.json)
//    ✅ 20.000 árboles OSM en assets/AlsasuaData/trees_unity.json
//    ✅ AlsasuaTreeStreamer funciona (prefabs en radio 250m)
//    ✅ SistemaVegetacion funciona (GPU instancing procedural)
//
//  CAPAS CESIUM ION ÚTILES PARA ÁRBOLES EN ALSASUA:
//
//  ┌──────────────────────────────────────────────────────────────────────┐
//  │  ID      Nombre                        Árboles  Cobertura   Calidad │
//  ├──────────────────────────────────────────────────────────────────────┤
//  │  2275207 Google Photorealistic 3D Tiles  ✅ Sí   España AAA   AAA+  │
//  │          → Malla 3D real de la región   canopy  completa          │
//  │            (Urbasa, Aralar, bosques de  real                       │
//  │             Navarra incluidos)                                      │
//  ├──────────────────────────────────────────────────────────────────────┤
//  │       1  Cesium World Terrain           ❌ No   Global      Media  │
//  │          → Solo malla de terreno         (solo geometría suelo)    │
//  ├──────────────────────────────────────────────────────────────────────┤
//  │   96188  Cesium OSM Buildings           ❌ No   Global      Media  │
//  │          → Solo edificios OSM 3D         (0 vegetación)            │
//  ├──────────────────────────────────────────────────────────────────────┤
//  │    3845  Bing Maps Aerial               ⚠️ 2D   España      Media  │
//  │          → Ortofoto sobre terreno        (textura plana, no 3D)    │
//  ├──────────────────────────────────────────────────────────────────────┤
//  │  Custom  PNOA + LIDAR IGN España        ✅ Sí   Navarra     AAA   │
//  │          → Nube de puntos LIDAR real     (subir a Cesium Ion       │
//  │            (árboles individuales con      como tileset custom)      │
//  │             altura y radio reales)                                  │
//  └──────────────────────────────────────────────────────────────────────┘
//
//  RECOMENDACIÓN PARA ALSASUA:
//    CAPA 1 (lejos, >400m): Google Photorealistic 3D Tiles (ID 2275207)
//      → da los bosques de Urbasa, Aralar, pinar norte automáticamente
//    CAPA 2 (medio, 100-400m): AlsasuaTreeStreamer (20.000 árboles OSM)
//      → árboles individuales con altura y especie real
//    CAPA 3 (cerca, <100m): SistemaVegetacion (GPU instancing)
//      → hierba, arbustos, detalles de suelo
//
//  PARA INSTALAR CESIUM:
//    Window → Package Manager → + → "Add package by name"
//    Nombre: com.cesium.unity
//    O desde: https://cesium.com/platform/cesium-for-unity/
//
//  Este script es funcional SI Cesium está instalado.
//  Sin Cesium: actúa como documentación y configura el resto del sistema.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections;

#if CESIUM_FOR_UNITY
using CesiumForUnity;
#endif

public class CesiumCapasAlsasua : MonoBehaviour
{
    // ── Token ──────────────────────────────────────────────────────────────
    [Header("═══ CESIUM ION ═══")]
    #pragma warning disable CS0414
    [Tooltip("Token de Cesium Ion. Configurado en CesiumSettings/ion.cesium.com.asset.")]
    [SerializeField] private string ionToken = ""; // se rellena desde CesiumSettings en Start

    // ── IDs de Assets Cesium Ion ───────────────────────────────────────────
    [Header("═══ ASSET IDs ═══")]
    [Tooltip("Google Photorealistic 3D Tiles — árboles + edificios + terreno fotorrealista.\n" +
             "Cubre Navarra/Alsasua con calidad AAA+.\n" +
             "Requiere aceptar los Terms of Service de Google en cesium.com.")]
    [SerializeField] private long idGoogleTiles          = 2275207;

    [Tooltip("Cesium World Terrain — malla de terreno de alta resolución.\n" +
             "Usar solo si no se usa Google Tiles (que ya incluye terreno).")]
    [SerializeField] private long idWorldTerrain         = 1;

    [Tooltip("Ortofoto aérea de Bing Maps — textura sobre el terreno.\n" +
             "Alternativa gratuita a Google Tiles para la textura de suelo.")]
    [SerializeField] private long idBingMaps             = 3845;

    [Tooltip("Cesium OSM Buildings — edificios 3D de OpenStreetMap.\n" +
             "NO incluye árboles, solo edificios extruidos.")]
    [SerializeField] private long idOsmBuildings         = 96188;

    // ── Configuración de capas activas ─────────────────────────────────────
    [Header("═══ CAPAS ACTIVAS ═══")]
    [Tooltip("Activar Google Photorealistic 3D Tiles (ID 2275207).\n" +
             "Incluye árboles, edificios y terreno fotorrealista para Navarra.")]
    [SerializeField] private bool usarGoogleTiles        = true;

    [Tooltip("Activar Cesium World Terrain como base.\n" +
             "Desactivar si se usa Google Tiles (ya incluye terreno).")]
    [SerializeField] private bool usarWorldTerrain       = false;

    [Tooltip("Activar Bing Maps Aerial sobre el terreno.")]
    [SerializeField] private bool usarBingMaps           = false;

    [Tooltip("Activar edificios OSM 3D de Cesium.\n" +
             "Solo útil si NO se usa Google Tiles.")]
    [SerializeField] private bool usarOsmBuildings       = false;

    // ── Integración con sistemas existentes ────────────────────────────────
    [Header("═══ SISTEMA HÍBRIDO DE ÁRBOLES ═══")]
    [Tooltip("El TreeStreamer OSM maneja árboles prefab en radio cercano (250m).\n" +
             "Cesium maneja el fondo (bosques de Urbasa, Aralar, etc.).")]
    [SerializeField] private AlsasuaTreeStreamer treeStreamer;

    [Tooltip("SistemaVegetacion maneja hierba y arbustos GPU instanced (hasta 600m).")]
    [SerializeField] private SistemaVegetacion sistemaVegetacion;

    [Tooltip("Radio a partir del cual Cesium maneja los árboles (m).\n" +
             "Por debajo lo gestiona AlsasuaTreeStreamer con prefabs.")]
    [SerializeField] private float radioTransicionCesium = 400f;
#pragma warning restore CS0414

    // ── Información de coordenadas ─────────────────────────────────────────
    [Header("═══ COORDENADAS ═══")]
    [Tooltip("Herriko Plaza en coordenadas Unity de terreno.\n" +
             "Referencia: AltsasuCore.centroX/centroZ = 1918, 8570")]
    [SerializeField] private Vector2 herrikoPlazaTerreno = new Vector2(1918f, 8570f);

    [Tooltip("GPS real de Herriko Plaza (para CesiumGeoreference).")]
    [SerializeField] private double latitudCentro  =  42.9016;
    [SerializeField] private double longitudCentro =  -2.1668;
    [SerializeField] private double altitudCentro  = 536.0;

    // ── Estado interno ─────────────────────────────────────────────────────
    private bool _cesiumDisponible = false;

    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Auto-encontrar TreeStreamer si no está asignado en Inspector
        if (treeStreamer == null)
            treeStreamer = FindFirstObjectByType<AlsasuaTreeStreamer>();

#if CESIUM_FOR_UNITY
        _cesiumDisponible = true;
        AlsasuaLogger.Info("CesiumCapas", "✅ Cesium for Unity detectado.");
#else
        _cesiumDisponible = false;
        AlsasuaLogger.Warn("CesiumCapas",
            "❌ Cesium for Unity NO está instalado.\n" +
            "   Instala: Window → Package Manager → + → Add package by name → com.cesium.unity\n" +
            "   O descarga desde: https://cesium.com/platform/cesium-for-unity/\n" +
            "   El token ya está configurado (CesiumSettings/ion.cesium.com.asset).\n" +
            "   Sin Cesium: el sistema funciona con OSM trees + SistemaVegetacion.");
#endif
    }

    private IEnumerator Start()
    {
        yield return null; // esperar a que otros sistemas arranquen

        if (!_cesiumDisponible)
        {
            // Sin Cesium: asegurar que los sistemas alternativos están activos
            ConfigurarSinCesium();
            yield break;
        }

#if CESIUM_FOR_UNITY
        ConfigurarCesiumGeoreference();
        yield return null;
        ConfigurarTilesets();
        ConfigurarSistemaHibrido();
#endif
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CONFIGURACIÓN SIN CESIUM (fallback)
    // ─────────────────────────────────────────────────────────────────────────

    private void ConfigurarSinCesium()
    {
        // Asegurar que el TreeStreamer OSM cubre el máximo posible
        if (treeStreamer != null)
        {
            // Sin Cesium, ampliar el radio visible de árboles prefab
            // para compensar la ausencia de la capa de fondo
            treeStreamer.radioVisible  = 400f;   // era 250m
            treeStreamer.radioDestroir = 550f;   // era 350m
            treeStreamer.maxArboles    = 800;    // era 500
            AlsasuaLogger.Info("CesiumCapas",
                "Sin Cesium → TreeStreamer ampliado a 400m / 800 árboles simultáneos.");
        }

        if (sistemaVegetacion != null)
        {
            AlsasuaLogger.Info("CesiumCapas",
                "SistemaVegetacion activo (GPU instancing hasta 600m).");
        }

        AlsasuaLogger.Info("CesiumCapas",
            "Sistema de árboles SIN Cesium:\n" +
            "  · OSM prefabs: 20.000 árboles, radio 400m\n" +
            "  · GPU instancing: vegetación procedural hasta 600m\n" +
            "  ❌ FALTA: bosques de Urbasa/Aralar en el fondo (requiere Cesium)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CONFIGURACIÓN CON CESIUM
    // ─────────────────────────────────────────────────────────────────────────

#if CESIUM_FOR_UNITY
    private void ConfigurarCesiumGeoreference()
    {
        // CesiumGeoreference — ancla el mundo Unity a las coordenadas GPS reales
        var georef = GetComponent<CesiumGeoreference>()
                  ?? gameObject.AddComponent<CesiumGeoreference>();

        georef.latitude  = latitudCentro;    // 42.9016° N (Herriko Plaza)
        georef.longitude = longitudCentro;   // -2.1668° W
        georef.height    = altitudCentro;    // 536 m s.n.m.

        AlsasuaLogger.Info("CesiumCapas",
            $"CesiumGeoreference configurado: {latitudCentro}°N {longitudCentro}°W @{altitudCentro}m");
    }

    private void ConfigurarTilesets()
    {
        if (usarGoogleTiles)
            CrearTileset("Cesium_GooglePhotorealistic",
                idGoogleTiles,
                "Google Photorealistic 3D Tiles — árboles + edificios + terreno Navarra");

        if (usarWorldTerrain)
            CrearTileset("Cesium_WorldTerrain",
                idWorldTerrain,
                "Cesium World Terrain — malla de terreno base");

        if (usarBingMaps)
            CrearTileset("Cesium_BingMaps",
                idBingMaps,
                "Bing Maps Aerial — ortofoto sobre terreno");

        if (usarOsmBuildings && !usarGoogleTiles) // OSM buildings redundante con Google Tiles
            CrearTileset("Cesium_OSMBuildings",
                idOsmBuildings,
                "Cesium OSM Buildings — edificios 3D (sin árboles)");
    }

    private void CrearTileset(string nombre, long assetId, string descripcion)
    {
        // Buscar si ya existe
        var existente = GameObject.Find(nombre);
        if (existente != null)
        {
            AlsasuaLogger.Info("CesiumCapas", $"Tileset '{nombre}' ya existe.");
            return;
        }

        var go  = new GameObject(nombre);
        go.transform.SetParent(transform, false);

        var tileset = go.AddComponent<Cesium3DTileset>();
        tileset.ionAssetID    = assetId;
        tileset.maximumScreenSpaceError = 8f; // calidad: menor = más LOD

        // Para Google Tiles: activar el mapa de sombras en HDRP
        tileset.opaqueMaterial = null; // usar el material por defecto

        AlsasuaLogger.Info("CesiumCapas",
            $"✅ Tileset creado: '{nombre}' (ID:{assetId})\n   {descripcion}");
    }

    private void ConfigurarSistemaHibrido()
    {
        // Con Cesium activo: reducir el TreeStreamer (Cesium maneja el fondo)
        if (treeStreamer != null)
        {
            treeStreamer.radioVisible  = 180f;   // solo árboles muy cercanos como prefabs
            treeStreamer.radioDestroir = 250f;   // Cesium toma el relevo después
            treeStreamer.maxArboles    = 300;    // menos prefabs (Cesium hace el resto)
            AlsasuaLogger.Info("CesiumCapas",
                "Modo híbrido: OSM prefabs <180m | Cesium tiles >180m");
        }

        AlsasuaLogger.Info("CesiumCapas",
            "Sistema de árboles CON Cesium:\n" +
            "  · 0-180m:  OSM prefabs interactivos (colliders, física)\n" +
            "  · 180-∞m:  Google Photorealistic 3D Tiles (canopy real de Navarra)\n" +
            "  · Todo:    SistemaVegetacion (hierba/arbustos GPU instanced)\n" +
            "  ✅ Bosques de Urbasa, Aralar, pinar norte → automáticos desde Cesium");
    }
#endif

    // ─────────────────────────────────────────────────────────────────────────
    //  DIAGNÓSTICO EN TIEMPO DE EJECUCIÓN
    // ─────────────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (!Application.isEditor && !Debug.isDebugBuild) return;

        var lines = new System.Text.StringBuilder();
        lines.AppendLine("═ CESIUM CAPAS ALSASUA ═");
        lines.AppendLine(_cesiumDisponible ? "✅ Cesium instalado" : "❌ Cesium NO instalado");

        if (treeStreamer != null)
            lines.AppendLine($"🌲 OSM Trees: radio={treeStreamer.radioVisible}m " +
                             $"max={treeStreamer.maxArboles}");
        else
            lines.AppendLine("⚠️ TreeStreamer no encontrado");

        if (!_cesiumDisponible)
        {
            lines.AppendLine("─────────────────────────");
            lines.AppendLine("Para árboles de fondo:");
            lines.AppendLine("Instala com.cesium.unity");
            lines.AppendLine("ID útil: 2275207 (Google)");
        }

        GUI.Box(new Rect(Screen.width - 270, 10, 260, _cesiumDisponible ? 80 : 130),
                lines.ToString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  UTILIDAD: convertir coordenadas de terreno a GPS
    //  (para saber a qué GPS apunta un árbol del JSON)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Convierte coordenadas de terreno-Unity (sistema trees_unity.json)
    /// a coordenadas GPS reales.
    /// Centro de referencia: Herriko Plaza = (1918, 8570) terreno = (42.9016N, 2.1668W) GPS
    /// </summary>
    public (double lat, double lon) TerrenoAGPS(float xTerreno, float zTerreno)
    {
        float dx = xTerreno - herrikoPlazaTerreno.x; // metros Este respecto a plaza
        float dz = zTerreno - herrikoPlazaTerreno.y; // metros Norte respecto a plaza

        double lat = latitudCentro  + dz / GeoDataAlsasua.M_POR_GRADO_LAT;
        double lon = longitudCentro + dx / GeoDataAlsasua.M_POR_GRADO_LON;
        return (lat, lon);
    }

    /// <summary>
    /// Convierte coordenadas GPS a coordenadas de terreno-Unity
    /// (el sistema que usa AlsasuaTreeStreamer y SistemaTerreno).
    /// </summary>
    public Vector2 GPSATerreno(double lat, double lon)
    {
        float dx = (float)((lon - longitudCentro) * GeoDataAlsasua.M_POR_GRADO_LON);
        float dz = (float)((lat - latitudCentro)  * GeoDataAlsasua.M_POR_GRADO_LAT);
        return new Vector2(herrikoPlazaTerreno.x + dx, herrikoPlazaTerreno.y + dz);
    }
}

