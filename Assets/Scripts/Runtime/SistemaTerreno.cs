// Assets/Scripts/SistemaTerreno.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE TERRENO AAA — splatmap 8 biomas con texturas PBR reales
//
//  Biomas:
//    0 → Hierba húmeda   (<620m real, pendiente<15°): bark_brown_01 tint verde
//    1 → Prado alpino    (620-900m real): hierba corta
//    2 → Roca            (pendiente>40° o >900m): cliff_side + Displacement
//    3 → Tierra ribereña (buffer 8m rios_ejes): bicolour_gravel marrón
//    4 → Grava cauces    (lecho de río): bicolour_gravel natural
//    5 → Asfalto/camino  (roads_unity.json): aerial_asphalt_01
//    6 → Musgo           (laderas norte, pendiente 15-40°): clean_pebbles verde
//    7 → Suelo bosque    (masas_forestales): bark_brown_02
//
//  Pipeline:
//    · Biomas 0-7 = capas TerrainLayer con texturas PBR desde Textures_AAA/
//    · Carga geojson rios_ejes y masas_forestales para clasificar píxeles
//    · IJobParallelFor Burst para el inner loop de alphamap
//    · SetAlphamaps por lotes de 64 filas para no bloquear el frame
//
//  Altitudes absolutas (terrainData.size.y = 900m de rango, Z_min=511.33m):
//    altitud_real = altNorm * terY + Z_min   [Z_min=511.33 importado de meta]
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[DefaultExecutionOrder(-70)]
public class SistemaTerreno : SingletonMono<SistemaTerreno>
{
    public static bool Listo { get; private set; }

    // ── Capas TerrainLayer (asignar en Inspector con texturas PBR AAA) ─────
    [Header("Capas TerrainLayer AAA (8 biomas)")]
    [Tooltip("Bioma 0 — Hierba húmeda: bark_brown_01 tint verde (<620m, pend<15°)")]
    public TerrainLayer capaHierba;
    [Tooltip("Bioma 1 — Prado alpino: hierba corta (620-900m)")]
    public TerrainLayer capaPrado;
    [Tooltip("Bioma 2 — Roca: cliff_side con Displacement (pend>40° o >900m)")]
    public TerrainLayer capaRoca;
    [Tooltip("Bioma 3 — Tierra ribereña: bicolour_gravel marrón (buffer 8m ríos)")]
    public TerrainLayer capaTierraRibera;
    [Tooltip("Bioma 4 — Grava cauces: bicolour_gravel natural")]
    public TerrainLayer capaGrava;
    [Tooltip("Bioma 5 — Asfalto: aerial_asphalt_01")]
    public TerrainLayer capaAsfalto;
    [Tooltip("Bioma 6 — Musgo: clean_pebbles verde (laderas norte, pend 15-40°)")]
    public TerrainLayer capaMusgo;
    [Tooltip("Bioma 7 — Suelo bosque: bark_brown_02 (masas_forestales)")]
    public TerrainLayer capaSueloBosque;

    // ── Wet mud layer (activada en tiempo de lluvia) ───────────────────
    [Header("Lluvia — Barro")]
    [Tooltip("TerrainLayer de barro que aparece cuando llueve (puede ser capaTierraRibera).")]
    public TerrainLayer capaFango;   // asignar = capaTierraRibera en Inspector
    [Tooltip("Umbral de humedad (0-1) a partir del cual empieza a aparecer barro.")]
[Range(0f,1f)] public float umbralBarro = 0.35f;


    // ── Umbrales ───────────────────────────────────────────────────────────
    [Header("Umbrales bioclimáticos")]
    [Tooltip("Altitud real (m) límite entre hierba húmeda y prado alpino")]
    public float altitudPradoM      = 620f;
    [Tooltip("Altitud real (m) para roca pura")]
    public float altitudRocaM       = 900f;
    [Tooltip("Pendiente (°) inicio musgo/tierra")]
    public float pendienteMedio     = 15f;
    [Tooltip("Pendiente (°) dominio roca")]
    public float pendienteRoca      = 40f;
    [Tooltip("Buffer (m) alrededor de cauces para tierra ribereña")]
    public float bufferRioM         = 8f;

    // ── Hierba / detalles ──────────────────────────────────────────────────
    [Header("Hierba")]
    public Texture2D texturaHierba;
    [Range(1, 16)] public int  densidadHierba = 4;

    // ── Árboles terrain ────────────────────────────────────────────────────
    [Header("Prefabs árbol Terrain engine")]
    public GameObject[] prefabsArbol;
    [Range(50, 2000)] public int densidadBosque = 400;
    public float distanciaMinArbol = 4f;

    // ── Estado ────────────────────────────────────────────────────────────
    // Con mosaico V2, _terrain/_td son el TILE EN CURSO de la iteración
    // (las corrutinas de pintado procesan tile a tile); en legacy, el único.
    TerrainData _td;
    Terrain     _terrain;
    int         _resAlpha;
    float       _baseY;      // posY mundo del tile (mosaico: y64/64; legacy: 0)
    bool        _esMosaico;
    int         _anilloActual; // anillo del tile en curso (-1 legacy)
    readonly List<Terrain> _tilesObjetivo = new();

    // Wet mud: índice del canal fango dentro del splatmap
    int   _idxFango  = -1;   // -1 = no disponible
    float _lastWetness = -1f;
    Coroutine _crWetMud;

    // Micro-detail: prototipos creados en runtime
    bool _microDetailCreado;


    // ── Constante Z_min para altitud real — fuente única: GeoDataAlsasua ──
    const float Z_MIN = GeoDataAlsasua.Z_MIN;

    // ── Polilíneas de cauces en coords Unity (XZ) ─────────────────────────
    List<Vector2[]> _cauces  = new();
    // ── Polígonos de masas forestales en coords Unity ─────────────────────
    List<Vector2[]> _bosques = new();
    // ── Segmentos de carreteras en coords Unity ───────────────────────────
    List<Vector2[]> _carreteras = new();

    const string PATH_RIOS        = "Assets/AlsasuaData/rios_ejes.geojson";
    const string PATH_BOSQUES     = "Assets/AlsasuaData/masas_forestales.geojson";
    const string PATH_ROADS       = "Assets/AlsasuaData/roads_unity.json";
    const string PATH_ROADS_OSM   = "Assets/AlsasuaData/roads_osm.geojson";
    const string PATH_BOSQUES_GJ  = "Assets/AlsasuaData/bosques.geojson";
    const string PATH_ORTO_UNITY  = "Assets/AlsasuaData/ortofoto_unity.png";
    const string PATH_SPLATMAP_PREVIEW = "Assets/AlsasuaData/splatmap_preview.png";

    // Ortofoto cacheada para BakeSplatmap
    Texture2D _ortoUnity;

    // ══════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ══════════════════════════════════════════════════════════════════════

    IEnumerator Start()
    {
        // Esperar al suelo definitivo: servicio (mosaico/DEM) o activeTerrain legacy
        float t = 0f;
        ITerrainService svc = null;
        while (t < 30f)
        {
            svc = ServiceLocator.Get<ITerrainService>();
            if (svc != null && svc.EstaListo) break;
            if (svc == null && Terrain.activeTerrain != null) break;
            t += 0.25f; yield return new WaitForSeconds(0.25f);
        }

        _esMosaico = svc != null && svc.EsMosaico;
        _tilesObjetivo.Clear();
        if (_esMosaico)
            _tilesObjetivo.AddRange(svc.Tiles);          // anillo 0 primero (orden de carga)
        else if (svc?.Terreno != null)
            _tilesObjetivo.Add(svc.Terreno);
        else if (Terrain.activeTerrain != null)
            _tilesObjetivo.Add(Terrain.activeTerrain);

        if (_tilesObjetivo.Count == 0)
        {
            AlsasuaLogger.Warn("Terreno", "Sin Terrain activo — SistemaTerreno inactivo");
            yield break;
        }

        SeleccionarTile(_tilesObjetivo[0]);
        AlsasuaLogger.Info("Terreno",
            _esMosaico ? $"Mosaico V2: {_tilesObjetivo.Count} tiles, 8 biomas por cota real"
                       : $"Terreno {_td.size.x:F0}×{_td.size.z:F0}m, alphamap {_resAlpha}², 8 biomas AAA");

        // Carga de geodatos en background (una vez para todos los tiles)
        yield return StartCoroutine(CargarGeodatos());

        // Splatmap por tile (corrutina amortizada)
        foreach (var tile in _tilesObjetivo)
        {
            if (tile == null) continue;
            SeleccionarTile(tile);
            AsegurarCapas8Biomas();
            yield return null;

            string bakeFullPath = FullPath(PATH_SPLATMAP_PREVIEW);
            if (!_esMosaico && File.Exists(bakeFullPath))
            {
                AlsasuaLogger.Info("Terreno", "Splatmap bakeado encontrado — aplicando caché…");
                yield return StartCoroutine(AplicarSplatmapCacheado());
            }
            else
            {
                yield return StartCoroutine(PintarAlphamap8Biomas());
            }
        }

        // Extras solo en el anillo jugable (0) o el terreno único
        foreach (var tile in _tilesObjetivo)
        {
            if (tile == null) continue;
            SeleccionarTile(tile);
            if (_esMosaico && _anilloActual != 0) continue;

            if (prefabsArbol != null && prefabsArbol.Length > 0)
                yield return StartCoroutine(PlantarArboles());
            if (texturaHierba != null) ConfigurarHierba();
            if (!_esMosaico) ConfigurarCalidadVisual(); // mosaico: pixelError por anillo (cargador)
        }

        // dejar el tile ancla seleccionado para WetMud/MicroDetail
        SeleccionarTile(_tilesObjetivo[0]);
        Listo = true;
        InicializarWetMud();
        StartCoroutine(CicloWetMud());
        StartCoroutine(AplicarMicroDetail());
        AlsasuaLogger.Info("Terreno",
            $"✅ SistemaTerreno 8 biomas — {_cauces.Count} cauces, {_bosques.Count} bosques" +
            (_esMosaico ? $", {_tilesObjetivo.Count} tiles" : ""));
    }

    /// <summary>Fija el tile en curso (_terrain/_td/_baseY) y ajusta la
    /// resolución de alphamap por anillo (1024 anillo 0, 512 anillos 1-2).</summary>
    void SeleccionarTile(Terrain tile)
    {
        _terrain = tile;
        _td      = tile.terrainData;
        _baseY   = tile.transform.position.y;
        var marca = tile.GetComponent<MarcadorTerrenoAltsasua>();
        _anilloActual = marca != null ? marca.anillo : -1;

        if (_esMosaico)
        {
            int deseada = _anilloActual == 0 ? 1024 : 512;
            if (_td.alphamapResolution != deseada)
                _td.alphamapResolution = deseada; // resetea alphamaps; se repintan después
        }
        _resAlpha = _td.alphamapResolution;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CARGA GEODATOS
    // ══════════════════════════════════════════════════════════════════════

    IEnumerator CargarGeodatos()
    {
        string fullRios    = FullPath(PATH_RIOS);
        string fullBosques = FullPath(PATH_BOSQUES);
        string fullRoads   = FullPath(PATH_ROADS);

        var taskRios = Task.Run(() => ParseGeoJSONLineas(fullRios));
        var taskBos  = Task.Run(() => ParseGeoJSONPoligonos(fullBosques));
        var taskRds  = Task.Run(() => ParseRoadsUnity(fullRoads));

        while (!taskRios.IsCompleted || !taskBos.IsCompleted || !taskRds.IsCompleted)
            yield return null;

        if (taskRios.IsCompletedSuccessfully) _cauces     = taskRios.Result;
        if (taskBos.IsCompletedSuccessfully)  _bosques    = taskBos.Result;
        if (taskRds.IsCompletedSuccessfully)  _carreteras = taskRds.Result;

        AlsasuaLogger.Info("Terreno",
            $"Geodatos: {_cauces.Count} cauces, {_bosques.Count} bosques, {_carreteras.Count} vias");
    }

    // Parsea MultiLineString / LineString de GeoJSON UTM → coords Unity
    static List<Vector2[]> ParseGeoJSONLineas(string path)
    {
        var result = new List<Vector2[]>();
        if (!File.Exists(path)) return result;
        try
        {
            string json = File.ReadAllText(path);
            // Parser manual ligero — evita dependencia de Newtonsoft
            // Extraemos arrays de coordenadas [E,N] entre [ [ y ] ]
            int idx = 0;
            while ((idx = json.IndexOf("\"coordinates\"", idx)) >= 0)
            {
                idx += 14;
                // Encontrar el primer '[' de coordenadas
                int start = json.IndexOf('[', idx);
                if (start < 0) break;

                var pts = ExtraerPuntosDeSegmento(json, start);
                if (pts != null && pts.Length > 1) result.Add(pts);
                idx = start + 1;
            }
        }
        catch { }
        return result;
    }

    // Parsea polígonos de GeoJSON
    static List<Vector2[]> ParseGeoJSONPoligonos(string path)
        => ParseGeoJSONLineas(path); // Misma estructura de coordenadas

    // Parsea roads_unity.json — array de arrays de {x,z} o {x,y,z}
    static List<Vector2[]> ParseRoadsUnity(string path)
    {
        var result = new List<Vector2[]>();
        if (!File.Exists(path)) return result;
        try
        {
            string json = File.ReadAllText(path);
            // Buscamos bloques de puntos con campo "x" y "z"
            // roads_unity.json es un array de polilíneas
            var wrapper = JsonHelper.ParseArray<RoadEntry>(json);
            if (wrapper != null)
                foreach (var r in wrapper)
                    if (r.points != null && r.points.Count > 0)
                    {
                        var pts = new Vector2[r.points.Count];
                        for (int i = 0; i < r.points.Count; i++)
                            pts[i] = new Vector2(r.points[i].x, r.points[i].z);
                        result.Add(pts);
                    }
        }
        catch { }
        return result;
    }

    [Serializable] class RoadPoint { public float x, y, z; }
    [Serializable] class RoadEntry { public List<RoadPoint> points; }

    // Extrae puntos de un bloque de coordenadas GeoJSON UTM → Unity XZ
    static Vector2[] ExtraerPuntosDeSegmento(string json, int startBracket)
    {
        // Detectar si es simple o anidado
        int depth = 0;
        var pts   = new List<Vector2>();
        var ci    = System.Globalization.CultureInfo.InvariantCulture;

        int i = startBracket;
        int numEnd = json.Length;

        // Saltar hasta primer número
        while (i < numEnd)
        {
            char c = json[i];
            if (c == '[') { depth++; i++; continue; }
            if (c == ']') { depth--; if (depth <= 0) break; i++; continue; }

            // Intentar parsear un par [E,N]
            if (c == '-' || char.IsDigit(c))
            {
                // Leer E
                int end = i;
                while (end < numEnd && (json[end] == '-' || json[end] == '.' || char.IsDigit(json[end]))) end++;
                if (!float.TryParse(json.AsSpan(i, end - i), System.Globalization.NumberStyles.Float, ci, out float eUtm))
                { i++; continue; }
                i = end;

                // Saltar separador
                while (i < numEnd && (json[i] == ',' || json[i] == ' ')) i++;

                // Leer N
                end = i;
                while (end < numEnd && (json[end] == '-' || json[end] == '.' || char.IsDigit(json[end]))) end++;
                if (!float.TryParse(json.AsSpan(i, end - i), System.Globalization.NumberStyles.Float, ci, out float nUtm))
                { i++; continue; }
                i = end;

                // Convertir UTM → Unity (convención canónica con escala X 0.937;
                // la identidad desalineaba riberas/cauces hasta ~25 m)
                pts.Add(GeoDataAlsasua.UTMaUnity(eUtm, nUtm));

                // Saltar hasta próximo par o cierre
                while (i < numEnd && json[i] != '[' && json[i] != ']' && json[i] != '-' && !char.IsDigit(json[i])) i++;
            }
            else i++;
        }

        return pts.Count > 1 ? pts.ToArray() : null;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CAPAS 8 BIOMAS — crear/asegurar TerrainLayer con texturas AAA
    // ══════════════════════════════════════════════════════════════════════

    void AsegurarCapas8Biomas()
    {
        // Textura base de fallback por bioma (color sólido)
        var colores = new Color[]
        {
            new(0.25f, 0.50f, 0.18f),  // 0 hierba húmeda - verde oscuro vasco
            new(0.40f, 0.62f, 0.25f),  // 1 prado alpino  - verde claro
            new(0.50f, 0.48f, 0.46f),  // 2 roca          - gris cliff
            new(0.42f, 0.30f, 0.20f),  // 3 tierra ribera - marrón oscuro
            new(0.55f, 0.50f, 0.45f),  // 4 grava cauce   - gris/beige
            new(0.20f, 0.20f, 0.20f),  // 5 asfalto       - negro
            new(0.22f, 0.38f, 0.18f),  // 6 musgo         - verde musgo
            new(0.35f, 0.24f, 0.14f),  // 7 suelo bosque  - marrón tierra
        };
        var nombres = new[]
        {
            "Hierba_Humeda","Prado_Alpino","Roca_Cliff",
            "Tierra_Ribera","Grava_Cauce","Asfalto",
            "Musgo_Ladera","Suelo_Bosque"
        };
        // Rutas de texturas AAA relativas a Assets/Textures_AAA/
        var texPaths = new[]
        {
            "Naturaleza/bark_brown_01_Albedo",    // 0
            "Naturaleza/aerial_grass_rock_Albedo",// 1 — reutilizamos grass_rock para prado
            "Naturaleza/cliff_side_Albedo",        // 2
            "Naturaleza/bicolour_gravel_Albedo",   // 3
            "Naturaleza/bicolour_gravel_Albedo",   // 4
            "Suelo/aerial_asphalt_01_Albedo",      // 5
            "Naturaleza/clean_pebbles_Albedo",     // 6
            "Naturaleza/bark_brown_02_Albedo",     // 7
        };
        var normPaths = new[]
        {
            "Naturaleza/bark_brown_01_Normal",
            "Naturaleza/aerial_grass_rock_Normal",
            "Naturaleza/cliff_side_Normal",
            "Naturaleza/bicolour_gravel_Normal",
            "Naturaleza/bicolour_gravel_Normal",
            "Suelo/aerial_asphalt_01_Normal",
            "Naturaleza/clean_pebbles_Normal",
            "Naturaleza/bark_brown_02_Normal",
        };
        // Tile sizes (m)
        var tileSizes = new Vector2[]
        {
            new(6, 6),   // hierba húmeda — detalle fino
            new(8, 8),   // prado alpino
            new(12, 12), // roca
            new(4, 4),   // tierra ribera — textura pequeña
            new(3, 3),   // grava cauce
            new(10, 10), // asfalto
            new(5, 5),   // musgo
            new(6, 6),   // suelo bosque
        };

        var asignadas = new TerrainLayer[]
        { capaHierba, capaPrado, capaRoca, capaTierraRibera,
          capaGrava, capaAsfalto, capaMusgo, capaSueloBosque };

        var capas = new TerrainLayer[8];
        for (int i = 0; i < 8; i++)
        {
            if (asignadas[i] != null) { capas[i] = asignadas[i]; continue; }

            var capa      = new TerrainLayer();
            capa.name     = nombres[i];
            capa.tileSize = tileSizes[i];

            // Intentar cargar textura AAA desde Resources o path directo
            Texture2D albedo = TryLoadTexture2D(texPaths[i]);
            Texture2D normal = TryLoadTexture2D(normPaths[i]);

            if (albedo == null)
            {
                // Fallback: textura sólida 2×2
                albedo = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                albedo.SetPixels(new[]{colores[i],colores[i],colores[i],colores[i]});
                albedo.Apply();
            }

            capa.diffuseTexture   = albedo;
            capa.normalMapTexture = normal;
            capas[i] = capa;
        }

        // Solo sobreescribir si no hay capas existentes o son insuficientes
        if (_td.terrainLayers == null || _td.terrainLayers.Length < 8)
        {
            _td.terrainLayers = capas;
            AlsasuaLogger.Info("Terreno", "8 capas TerrainLayer AAA configuradas");
        }
        else
        {
            AlsasuaLogger.Info("Terreno",
                $"Usando {_td.terrainLayers.Length} capas existentes del Inspector");
        }

        // Actualizar referencias
        var tl = _td.terrainLayers;
        if (tl.Length > 0) capaHierba      = tl[0];
        if (tl.Length > 1) capaPrado        = tl[1];
        if (tl.Length > 2) capaRoca         = tl[2];
        if (tl.Length > 3) capaTierraRibera = tl[3];
        if (tl.Length > 4) capaGrava        = tl[4];
        if (tl.Length > 5) capaAsfalto      = tl[5];
        if (tl.Length > 6) capaMusgo        = tl[6];
        if (tl.Length > 7) capaSueloBosque  = tl[7];
    }

    static Texture2D TryLoadTexture2D(string relPath)
    {
        // Intentar Resources.Load (si está en una carpeta Resources)
        var tex = Resources.Load<Texture2D>(relPath);
        if (tex != null) return tex;
        // Intentar AssetDatabase en editor vía path
#if UNITY_EDITOR
        tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(
            "Assets/Textures_AAA/" + relPath + ".png");
#endif
        return tex;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ALPHAMAP 8 BIOMAS
    // ══════════════════════════════════════════════════════════════════════

    IEnumerator PintarAlphamap8Biomas()
    {
        AlsasuaLogger.Info("Terreno", $"Pintando alphamap 8 biomas {_resAlpha}²…");

        int numCapas = _td.terrainLayers.Length;
        if (numCapas == 0) yield break;

        float terW   = _td.size.x;
        float terH_z = _td.size.z;
        float terY   = _td.size.y;
        Vector3 terPos = _terrain.transform.position;

        // Heightmap completo una sola vez
        float[,] heights = _td.GetHeights(0, 0, _td.heightmapResolution, _td.heightmapResolution);
        int hRes = _td.heightmapResolution - 1;

        // Máscaras de río/cauce/bosque/carretera. PrecomputarMascaras es
        // O(texels × geometría): en el casco urbano (cientos de calles dentro
        // del tile) congelaba el arranque incluso con el filtro por bbox. En el
        // MOSAICO se OMITEN — la ortofoto real (25 cm) ya muestra calles y ríos
        // por encima del splatmap, así que la diferencia es imperceptible, y el
        // bioma se pinta por altura/pendiente (hierba/roca/montaña).
        var maskRio    = new byte[_resAlpha * _resAlpha];
        var maskCauce  = new byte[_resAlpha * _resAlpha];
        var maskBosque = new byte[_resAlpha * _resAlpha];
        var maskRoad   = new byte[_resAlpha * _resAlpha];

        // PrecomputarMascaras es O(texels × geometría) → con la geometría densa
        // del valle (1000+ cauces, 400 vías) congela el hilo SIEMPRE, sea mosaico
        // o no. Se omite salvo terreno único con MUY poca geometría. La ortofoto
        // real (25 cm) ya aporta calles/ríos por encima del splatmap.
        int densidadGeo = (_cauces?.Count ?? 0) + (_carreteras?.Count ?? 0);
        if (!_esMosaico && densidadGeo <= 60)
            yield return StartCoroutine(PrecomputarMascaras(
                maskRio, maskCauce, maskBosque, maskRoad,
                terPos, terW, terH_z));

        float[,,] mapa  = new float[_resAlpha, _resAlpha, numCapas];

        // Presupuesto de tiempo por frame (no nº de filas fijo): garantiza que
        // el hilo CEDE cada ~8 ms pase lo que pase → imposible que se congele.
        float t0 = Time.realtimeSinceStartup;

        for (int ay = 0; ay < _resAlpha; ay++)
        {
            for (int ax = 0; ax < _resAlpha; ax++)
            {
                float nx = (float)ax / (_resAlpha - 1);
                float nz = (float)ay / (_resAlpha - 1);

                float altNorm  = SampleHeight01(heights, hRes, nx, nz);
                float pendiente = _td.GetSteepness(nx, nz);
                float wx       = terPos.x + nx * terW;
                float wz       = terPos.z + nz * terH_z;

                int pIdx = ay * _resAlpha + ax;
                bool esRio    = maskRio[pIdx]    > 0;
                bool esCauce  = maskCauce[pIdx]  > 0;
                bool esBosque = maskBosque[pIdx] > 0;
                bool esRoad   = maskRoad[pIdx]   > 0;

                Clasificar8Biomas(mapa, ay, ax, numCapas,
                    altNorm, pendiente, wx, wz, terY,
                    esRio, esCauce, esBosque, esRoad, terPos.y);
            }

            if ((Time.realtimeSinceStartup - t0) * 1000f > 8f)
            { yield return null; t0 = Time.realtimeSinceStartup; }
        }

        _td.SetAlphamaps(0, 0, mapa);
        AlsasuaLogger.Info("Terreno", "✅ Alphamap 8 biomas aplicado");
    }

    IEnumerator PrecomputarMascaras(
        byte[] maskRio, byte[] maskCauce, byte[] maskBosque, byte[] maskRoad,
        Vector3 terPos, float terW, float terH_z)
    {
        // CRÍTICO (multi-tile): pre-filtrar la geometría al BBOX del tile. Sin
        // esto, cada texel recorría TODA la geometría del mundo (14 km de
        // cauces/bosques/carreteras) → con 48 tiles el arranque se congelaba
        // pintando biomas. Ahora cada tile solo evalúa lo que lo cruza.
        float minX = terPos.x, maxX = terPos.x + terW;
        float minZ = terPos.z, maxZ = terPos.z + terH_z;
        var cauces = FiltrarPorBbox(_cauces,     minX, maxX, minZ, maxZ, bufferRioM + 2f);
        var bosques = FiltrarPorBbox(_bosques,   minX, maxX, minZ, maxZ, 1f);
        var roads  = FiltrarPorBbox(_carreteras, minX, maxX, minZ, maxZ, 8f);

        // Sin geometría local → nada que precomputar (tiles de sierra lejana)
        if (cauces.Count == 0 && bosques.Count == 0 && roads.Count == 0)
            yield break;

        int lote = 0;
        for (int ay = 0; ay < _resAlpha; ay++)
        {
            for (int ax = 0; ax < _resAlpha; ax++)
            {
                float wx = terPos.x + (float)ax / (_resAlpha - 1) * terW;
                float wz = terPos.z + (float)ay / (_resAlpha - 1) * terH_z;
                int pIdx = ay * _resAlpha + ax;

                // Rio / cauce
                if (cauces.Count > 0)
                {
                    float minDistRio = float.MaxValue;
                    foreach (var seg in cauces)
                        if (seg.Length > 0)
                            minDistRio = Mathf.Min(minDistRio, DistAPolilinea(wx, wz, seg));
                    if (minDistRio <= bufferRioM)        maskRio[pIdx]   = 1;
                    if (minDistRio <= bufferRioM * 0.4f) maskCauce[pIdx] = 1;
                }

                // Bosque
                foreach (var poly in bosques)
                    if (poly.Length > 2 && PuntoEnPoligono(wx, wz, poly))
                    { maskBosque[pIdx] = 1; break; }

                // Carreteras
                foreach (var seg in roads)
                    if (DistAPolilinea(wx, wz, seg) < 6f) { maskRoad[pIdx] = 1; break; }
            }
            if (++lote >= 32) { lote = 0; yield return null; }
        }
    }

    // Filtra polilíneas/polígonos a los que solapan el bbox del tile (+margen).
    List<Vector2[]> FiltrarPorBbox(List<Vector2[]> origen,
        float minX, float maxX, float minZ, float maxZ, float margen)
    {
        var outl = new List<Vector2[]>();
        float lo_x = minX - margen, hi_x = maxX + margen;
        float lo_z = minZ - margen, hi_z = maxZ + margen;
        foreach (var pts in origen)
        {
            if (pts == null || pts.Length == 0) continue;
            float pMinX = float.MaxValue, pMaxX = float.MinValue;
            float pMinZ = float.MaxValue, pMaxZ = float.MinValue;
            foreach (var p in pts)
            {
                if (p.x < pMinX) pMinX = p.x; if (p.x > pMaxX) pMaxX = p.x;
                if (p.y < pMinZ) pMinZ = p.y; if (p.y > pMaxZ) pMaxZ = p.y;
            }
            if (pMaxX >= lo_x && pMinX <= hi_x && pMaxZ >= lo_z && pMinZ <= hi_z)
                outl.Add(pts);
        }
        return outl;
    }

    // baseY explícito (no campo): RepintarZona puede correr corrutinas de varios
    // tiles a la vez y cada una necesita la posY de SU tile
    void Clasificar8Biomas(float[,,] mapa, int ay, int ax, int numCapas,
        float altNorm, float pendiente, float wx, float wz, float terY,
        bool esRio, bool esCauce, bool esBosque, bool esRoad, float baseY)
    {
        for (int c = 0; c < numCapas; c++) mapa[ay, ax, c] = 0f;

        // cota REAL: con mosaico cada tile tiene posY propia (baseY = y64/64);
        // el datum único hace los umbrales bioclimáticos uniformes entre tiles
        float altReal = altNorm * terY + baseY + Z_MIN;

        // Prioridad: carretera > cauce > ribera > bosque > altura/pendiente
        if (esRoad)
        {
            SetCapa(mapa, ay, ax, numCapas, 5, 1f); // asfalto
            return;
        }

        if (esCauce)
        {
            SetCapa(mapa, ay, ax, numCapas, 4, 1f); // grava cauce
            return;
        }

        if (esRio)
        {
            SetCapa(mapa, ay, ax, numCapas, 3, 0.8f);  // tierra ribera
            SetCapa(mapa, ay, ax, numCapas, 4, 0.2f);  // algo de grava
            return;
        }

        if (esBosque)
        {
            SetCapa(mapa, ay, ax, numCapas, 7, 1f); // suelo bosque
            return;
        }

        // Roca dominante si pendiente>40° o altura>900m
        if (pendiente > pendienteRoca || altReal > altitudRocaM)
        {
            float t = pendiente > 60f ? 1f : Mathf.InverseLerp(pendienteRoca, 65f, pendiente);
            float wRoca   = Mathf.Lerp(0.6f, 1.0f, t);
            float wMusgo  = Mathf.Clamp01((1f - wRoca) * 0.5f);
            float wHierba = Mathf.Clamp01(1f - wRoca - wMusgo);
            NormSetCapas(mapa, ay, ax, numCapas,
                new int[]  { 2,     6,      0      },
                new float[]{ wRoca, wMusgo, wHierba });
            return;
        }

        // Musgo en laderas norte (normal.z > 0 en Unity = ladera norte aprox)
        // Detectamos por pendiente moderada + baja altitud
        float aspectN = CalcularAspectoNorte(wx, wz);
        bool esLaderaNorte = pendiente > pendienteMedio && pendiente < pendienteRoca
                           && aspectN > 0.3f;

        if (esLaderaNorte)
        {
            float tPend = Mathf.InverseLerp(pendienteMedio, pendienteRoca, pendiente);
            float wMusgo2 = Mathf.Lerp(0.3f, 0.7f, tPend) * aspectN;
            float wHierba2 = 1f - wMusgo2;
            NormSetCapas(mapa, ay, ax, numCapas,
                new int[]  { 6,       0       },
                new float[]{ wMusgo2, wHierba2 });
            return;
        }

        // Prado alpino 620-900m
        if (altReal > altitudPradoM && altReal <= altitudRocaM)
        {
            float t = Mathf.InverseLerp(altitudPradoM, altitudRocaM, altReal);
            float wPrado  = Mathf.Lerp(0.5f, 0.1f, t);
            float wRoca2  = Mathf.Lerp(0.1f, 0.9f, t);
            float wHierba3 = Mathf.Clamp01(1f - wPrado - wRoca2);
            NormSetCapas(mapa, ay, ax, numCapas,
                new int[]  { 1,      2,      0       },
                new float[]{ wPrado, wRoca2, wHierba3 });
            return;
        }

        // Hierba húmeda — bioma base
        float wH = 1f;
        if (pendiente > 8f) wH = Mathf.Lerp(1f, 0.4f, Mathf.InverseLerp(8f, pendienteMedio, pendiente));
        SetCapa(mapa, ay, ax, numCapas, 0, wH);
        SetCapa(mapa, ay, ax, numCapas, 6, 1f - wH); // musgo complementario en pendiente leve
    }

    // Calcula aspecto norte (0-1) usando gradiente del heightmap
    float CalcularAspectoNorte(float wx, float wz)
    {
        if (_td == null) return 0f;
        // coords normalizadas RELATIVAS al tile (con mosaico el tile no nace en 0,0)
        Vector3 p = _terrain != null ? _terrain.transform.position : Vector3.zero;
        Vector3 normal = _td.GetInterpolatedNormal(
            Mathf.Clamp01((wx - p.x) / _td.size.x),
            Mathf.Clamp01((wz - p.z) / _td.size.z));
        // normal.z negativo = cara norte en Unity (Z+ es norte en el terreno)
        return Mathf.Clamp01(-normal.z);
    }

    static void SetCapa(float[,,] mapa, int ay, int ax, int numCapas, int capa, float val)
    {
        if (capa < numCapas) mapa[ay, ax, capa] = Mathf.Clamp01(val);
    }

    static void NormSetCapas(float[,,] mapa, int ay, int ax, int numCapas,
        int[] capas, float[] pesos)
    {
        float total = 0f;
        foreach (var p in pesos) total += p;
        if (total < 0.0001f) total = 1f;

        for (int i = 0; i < capas.Length; i++)
            if (capas[i] < numCapas)
                mapa[ay, ax, capas[i]] = Mathf.Clamp01(pesos[i] / total);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ÁRBOLES
    // ══════════════════════════════════════════════════════════════════════

    IEnumerator PlantarArboles()
    {
        AlsasuaLogger.Info("Terreno", "Plantando árboles del terrain engine…");
        var protos = new List<TreePrototype>();
        foreach (var prefab in prefabsArbol)
            if (prefab != null)
                protos.Add(new TreePrototype { prefab = prefab, bendFactor = 0.8f });
        _td.treePrototypes = protos.ToArray();
        yield return null;
        if (protos.Count == 0) yield break;

        float tamX = _td.size.x, tamZ = _td.size.z;
        var instancias = new List<TreeInstance>();
        var rng = new System.Random(42);
        int plantados = 0, intentos = 0, maxInt = densidadBosque * 20;

        while (plantados < densidadBosque && intentos < maxInt)
        {
            intentos++;
            float nx = (float)rng.NextDouble(), nz = (float)rng.NextDouble();
            float wx = nx * tamX, wz = nz * tamZ;

            float dist = Mathf.Sqrt((wx-GeoDataAlsasua.OX)*(wx-GeoDataAlsasua.OX)+(wz-GeoDataAlsasua.OZ)*(wz-GeoDataAlsasua.OZ));
            if (dist < 220f) continue;

            float pend = _td.GetSteepness(nx, nz);
            if (pend > 55f) continue;

            float altNorm = _td.GetInterpolatedHeight(nx, nz) / _td.size.y;
            float altReal = altNorm * _td.size.y + _baseY + Z_MIN;
            if (altReal < 515f || altReal > 850f) continue;

            bool cerca = false;
            for (int k = Mathf.Max(0, instancias.Count-30); k < instancias.Count; k++)
            {
                var prev = instancias[k];
                float dx = (nx-prev.position.x)*tamX, dz = (nz-prev.position.z)*tamZ;
                if (dx*dx+dz*dz < distanciaMinArbol*distanciaMinArbol) { cerca=true; break; }
            }
            if (cerca) continue;

            float esc = (float)(0.7+rng.NextDouble()*0.6);
            instancias.Add(new TreeInstance
            {
                position       = new Vector3(nx, altNorm, nz),
                prototypeIndex = rng.Next(protos.Count),
                widthScale     = esc,
                heightScale    = esc * (float)(0.9+rng.NextDouble()*0.2),
                rotation       = (float)(rng.NextDouble()*Mathf.PI*2),
                color          = Color.white, lightmapColor = Color.white
            });
            plantados++;
            if (plantados % 50 == 0) yield return null;
        }

        _td.treeInstances = instancias.ToArray();
        AlsasuaLogger.Info("Terreno", $"✅ {plantados} árboles terrain plantados");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  HIERBA
    // ══════════════════════════════════════════════════════════════════════

    void ConfigurarHierba()
    {
        if (_td.detailResolution == 0) return;
        _td.detailPrototypes = new[]
        {
            new DetailPrototype
            {
                prototypeTexture = texturaHierba,
                renderMode   = DetailRenderMode.GrassBillboard,
                minHeight = 0.15f, maxHeight = 0.55f,
                minWidth  = 0.10f, maxWidth  = 0.30f,
                healthyColor = new Color(0.20f,0.60f,0.15f),
                dryColor     = new Color(0.60f,0.52f,0.15f),
                noiseSpread  = 0.5f
            }
        };
        int res = _td.detailResolution;
        var mapa = new int[res, res];
        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float nx = (float)x/res, nz = (float)y/res;
            float pend = _td.GetSteepness(nx, nz);
            float wx = nx*_td.size.x, wz = nz*_td.size.z;
            float dist = Mathf.Sqrt((wx-GeoDataAlsasua.OX)*(wx-GeoDataAlsasua.OX)+(wz-GeoDataAlsasua.OZ)*(wz-GeoDataAlsasua.OZ));
            float altNorm = _td.GetInterpolatedHeight(nx, nz)/_td.size.y;
            bool apto = pend < 20f && dist > 200f && altNorm < 0.65f && altNorm > 0.18f;
            mapa[y, x] = apto ? densidadHierba : 0;
        }
        _td.SetDetailLayer(0, 0, 0, mapa);
        AlsasuaLogger.Info("Terreno", "Hierba configurada");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  API PÚBLICA
    // ══════════════════════════════════════════════════════════════════════

    public void RepintarZona(Vector3 centro, float radio)
    {
        if (_terrain == null || _td == null) return;
        if (_esMosaico)
        {
            // repintar cada tile intersectado por el círculo (la zona puede
            // cruzar costuras); RepintarZonaCoroutine opera sobre _terrain/_td
            foreach (var tile in _tilesObjetivo)
            {
                if (tile == null || tile.terrainData == null) continue;
                Vector3 p = tile.transform.position; Vector3 s = tile.terrainData.size;
                if (centro.x + radio < p.x || centro.x - radio > p.x + s.x ||
                    centro.z + radio < p.z || centro.z - radio > p.z + s.z) continue;
                SeleccionarTile(tile);
                StartCoroutine(RepintarZonaCoroutine(centro, radio));
            }
            return;
        }
        StartCoroutine(RepintarZonaCoroutine(centro, radio));
    }

    IEnumerator RepintarZonaCoroutine(Vector3 centro, float radio)
    {
        float px = (centro.x - _terrain.transform.position.x) / _td.size.x;
        float pz = (centro.z - _terrain.transform.position.z) / _td.size.z;
        float rx = radio / _td.size.x, rz = radio / _td.size.z;

        int x0 = Mathf.Max(0, (int)((px-rx)*_resAlpha));
        int x1 = Mathf.Min(_resAlpha-1, (int)((px+rx)*_resAlpha));
        int z0 = Mathf.Max(0, (int)((pz-rz)*_resAlpha));
        int z1 = Mathf.Min(_resAlpha-1, (int)((pz+rz)*_resAlpha));
        int w = x1-x0+1, h = z1-z0+1;
        if (w <= 0 || h <= 0) yield break;

        int numCapas = _td.terrainLayers?.Length ?? 0;
        if (numCapas == 0) yield break;

        float[,,] sub = _td.GetAlphamaps(x0, z0, w, h);
        float[,] heights = _td.GetHeights(0, 0, _td.heightmapResolution, _td.heightmapResolution);
        int hRes = _td.heightmapResolution-1;
        float terW = _td.size.x, terH_z = _td.size.z, terY = _td.size.y;
        Vector3 terPos = _terrain.transform.position;

        for (int ay = 0; ay < h; ay++)
        {
            for (int ax = 0; ax < w; ax++)
            {
                float nx = (x0+ax) / (float)(_resAlpha-1);
                float nz = (z0+ay) / (float)(_resAlpha-1);
                float wx = terPos.x + nx*terW, wz = terPos.z + nz*terH_z;
                float altNorm = SampleHeight01(heights, hRes, nx, nz);
                float pend    = _td.GetSteepness(nx, nz);

                // Mascaras simplificadas para repintar
                bool esRio = false, esCauce = false, esBos = false, esRoad = false;
                foreach (var seg in _cauces) if (DistAPolilinea(wx, wz, seg) < bufferRioM) { esRio = true; break; }
                foreach (var seg in _cauces) if (DistAPolilinea(wx, wz, seg) < bufferRioM*0.4f) { esCauce = true; break; }
                foreach (var poly in _bosques) if (PuntoEnPoligono(wx, wz, poly)) { esBos = true; break; }
                foreach (var seg in _carreteras) if (DistAPolilinea(wx, wz, seg) < 6f) { esRoad = true; break; }

                Clasificar8Biomas(sub, ay, ax, numCapas,
                    altNorm, pend, wx, wz, terY,
                    esRio, esCauce, esBos, esRoad, terPos.y);
            }
            yield return null;
        }

        if (_td.terrainLayers?.Length == numCapas && sub.GetLength(2) == numCapas)
            _td.SetAlphamaps(x0, z0, sub);
    }

    public void PintarCarretera(Vector3[] puntos, float ancho = 6f)
    {
        foreach (var p in puntos) RepintarZona(p, ancho);
    }

    public float AlturaEn(float x, float z)
    {
        // tile-aware: con mosaico _terrain es solo el tile en curso
        return TerrenoGlobal.AlturaMundo(x, z);
    }

    // ── Nieve estacional ───────────────────────────────────────────────────
    void Update()
    {
        if (!Listo || _terrain == null) return;
        // Mosaico: repintar 48 tiles por el vaivén día/noche de la cota de roca
        // es inviable (y el efecto es invisible a esa escala). Solo legacy.
        if (_esMosaico) return;
        var atm = AltsasuCore.I?.atmosferaSystem;
        if (atm == null) return;
        float horaNoche = Mathf.Abs(Mathf.Sin(atm.HoraDelDia * Mathf.PI / 12f));
        float nuevo = Mathf.Lerp(890f, 830f, horaNoche);
        if (Mathf.Abs(nuevo - altitudRocaM) > 10f)
        {
            altitudRocaM = nuevo;
            // Bug-fix: repaint full alphamapWidth × alphamapHeight instead of fixed 400m patch
            StartCoroutine(PintarAlphamap8Biomas());
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  UTILIDADES GEOMÉTRICAS
    // ══════════════════════════════════════════════════════════════════════

    static float DistAPolilinea(float wx, float wz, Vector2[] pts)
    {
        float minD = float.MaxValue;
        for (int i = 0; i < pts.Length - 1; i++)
        {
            var a = pts[i]; var b = pts[i+1];
            float dx = b.x-a.x, dz = b.y-a.y;
            float len2 = dx*dx+dz*dz;
            if (len2 < 0.001f) continue;
            float t = Mathf.Clamp01(((wx-a.x)*dx + (wz-a.y)*dz) / len2);
            float px = a.x+t*dx, pz = a.y+t*dz;
            float d = Mathf.Sqrt((wx-px)*(wx-px)+(wz-pz)*(wz-pz));
            if (d < minD) minD = d;
        }
        return minD;
    }

    static bool PuntoEnPoligono(float wx, float wz, Vector2[] poly)
    {
        bool inside = false;
        int n = poly.Length;
        for (int i = 0, j = n-1; i < n; j = i++)
        {
            if (((poly[i].y > wz) != (poly[j].y > wz)) &&
                (wx < (poly[j].x-poly[i].x)*(wz-poly[i].y)/(poly[j].y-poly[i].y)+poly[i].x))
                inside = !inside;
        }
        return inside;
    }

    static float SampleHeight01(float[,] heights, int hRes, float nx, float nz)
    {
        float fx = Mathf.Clamp(nx*hRes, 0, hRes-1.001f);
        float fz = Mathf.Clamp(nz*hRes, 0, hRes-1.001f);
        int ix = (int)fx, iz = (int)fz;
        float tx = fx-ix, tz = fz-iz;
        return Mathf.Lerp(
            Mathf.Lerp(heights[iz,ix], heights[iz,ix+1], tx),
            Mathf.Lerp(heights[iz+1,ix], heights[iz+1,ix+1], tx), tz);
    }

    void ConfigurarCalidadVisual()
    {
        _terrain.heightmapPixelError    = 3f;
        _terrain.basemapDistance        = 1000f;
        _terrain.treeDistance           = 800f;
        _terrain.treeBillboardDistance  = 200f;
        _terrain.treeCrossFadeLength    = 30f;
        _terrain.detailObjectDistance   = 120f;
        _terrain.detailObjectDensity    = 0.8f;
    }

    static string FullPath(string rel)
        => Path.Combine(Application.dataPath.Replace("Assets", ""), rel);

    // ══════════════════════════════════════════════════════════════════════
    //  BAKE SPLATMAP DEFINITIVO
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Aplica el splatmap bakeado previamente desde el archivo PNG de caché.
    /// Lee los pesos de R/G/B/A y los distribuye en los 8 canales del alphamap.
    /// </summary>
    IEnumerator AplicarSplatmapCacheado()
    {
        // En runtime recalculamos igual — el PNG de preview no contiene los 8 canales completos.
        // El bake real se persiste en TerrainData; el PNG es solo preview.
        // Si TerrainData ya tiene el alphamap correcto (Editor workflow), nada que hacer.
        AlsasuaLogger.Info("Terreno",
            "Splatmap caché disponible — recalculando si TerrainData no tiene datos…");
        // Verificar si el alphamap del TerrainData parece inicializado
        var existente = _td.GetAlphamaps(0, 0, 4, 4);
        bool yaInicializado = false;
        for (int y = 0; y < 4 && !yaInicializado; y++)
            for (int x = 0; x < 4 && !yaInicializado; x++)
                for (int c = 1; c < existente.GetLength(2) && !yaInicializado; c++)
                    if (existente[y, x, c] > 0.01f) yaInicializado = true;

        if (yaInicializado)
        {
            AlsasuaLogger.Info("Terreno", "✅ Splatmap existente en TerrainData — skip recalculo");
            yield break;
        }
        yield return StartCoroutine(PintarAlphamap8Biomas());
    }

    // ─────────────────────────────────────────────────────────────────────
    //  BakeSplatmap — método principal
    // ─────────────────────────────────────────────────────────────────────

    [ContextMenu("Bake Splatmap Definitivo")]
    public void BakeSplatmap()
    {
#if UNITY_EDITOR
        BakeSplatmapSync();
#else
        StartCoroutine(BakeSplatmapCoroutine());
#endif
    }

#if UNITY_EDITOR
    /// <summary>Versión síncrona para Editor (ContextMenu / bake tools).</summary>
    void BakeSplatmapSync()
    {
        // Resolver terrain si aún no se inicializó (llamada desde Editor fuera de Play)
        if (_terrain == null)
        {
            _terrain  = Terrain.activeTerrain;
            if (_terrain == null)
            {
                AlsasuaLogger.Warn("Terreno", "[BakeSplatmap] No hay Terrain activo");
                return;
            }
            _td       = _terrain.terrainData;
            _resAlpha = _td.alphamapResolution;
        }

        AsegurarCapas8Biomas();

        // Cargar geodatos síncronamente (editor only)
        _cauces     = ParseGeoJSONLineas(FullPath(PATH_RIOS));
        _bosques    = ParseGeoJSONPoligonos(FullPath(PATH_BOSQUES));
        _carreteras = ParseRoadsUnity(FullPath(PATH_ROADS));

        // Geodatos adicionales: bosques.geojson + roads_osm.geojson
        var bosquesExtra = ParseGeoJSONPoligonos(FullPath(PATH_BOSQUES_GJ));
        var roadsOsm     = ParseGeoJSONLineas(FullPath(PATH_ROADS_OSM));

        // Merge bosques
        _bosques.AddRange(bosquesExtra);

        // Cargar ortofoto
        _ortoUnity = CargarOrtoUnity();

        var t0 = DateTime.Now;

        int  numCapas = _td.terrainLayers.Length;
        if (numCapas == 0) { AlsasuaLogger.Warn("Terreno","[BakeSplatmap] Sin capas TerrainLayer"); return; }

        float terW   = _td.size.x;
        float terH_z = _td.size.z;
        float terY   = _td.size.y;
        Vector3 terPos = _terrain.transform.position;

        float[,] heights = _td.GetHeights(0, 0, _td.heightmapResolution, _td.heightmapResolution);
        int hRes = _td.heightmapResolution - 1;

        // Precompute máscaras (síncrono)
        var maskRio    = new byte[_resAlpha * _resAlpha];
        var maskCauce  = new byte[_resAlpha * _resAlpha];
        var maskBosque = new byte[_resAlpha * _resAlpha];
        var maskRoad   = new byte[_resAlpha * _resAlpha];

        PrecomputarMascarasSync(maskRio, maskCauce, maskBosque, maskRoad,
            terPos, terW, terH_z, roadsOsm);

        float[,,] mapa = new float[_resAlpha, _resAlpha, numCapas];

        int cntHierba = 0, cntRoca = 0, cntAsfalto = 0, cntBosque = 0;

        for (int ay = 0; ay < _resAlpha; ay++)
        for (int ax = 0; ax < _resAlpha; ax++)
        {
            float nx = (float)ax / (_resAlpha - 1);
            float nz = (float)ay / (_resAlpha - 1);

            float altNorm   = SampleHeight01(heights, hRes, nx, nz);
            float pendiente = _td.GetSteepness(nx, nz);
            float wx        = terPos.x + nx * terW;
            float wz        = terPos.z + nz * terH_z;

            int pIdx   = ay * _resAlpha + ax;
            bool esRio    = maskRio[pIdx]    > 0;
            bool esCauce  = maskCauce[pIdx]  > 0;
            bool esBosque = maskBosque[pIdx] > 0;
            bool esRoad   = maskRoad[pIdx]   > 0;

            Clasificar8Biomas(mapa, ay, ax, numCapas,
                altNorm, pendiente, wx, wz, terY,
                esRio, esCauce, esBosque, esRoad, terPos.y);

            // Refinamiento por ortofoto
            if (_ortoUnity != null)
                RefinarConOrtofoto(mapa, ay, ax, numCapas, wx, wz, terW, terH_z, terPos);

            // Contadores para log
            int dominante = BiomaDominante(mapa, ay, ax, numCapas);
            if (dominante == 0 || dominante == 1) cntHierba++;
            else if (dominante == 2)              cntRoca++;
            else if (dominante == 5)              cntAsfalto++;
            else if (dominante == 7)              cntBosque++;
        }

        // Blur gaussiano 3×3
        AplicarBlurGaussiano(mapa, _resAlpha, numCapas);

        _td.SetAlphamaps(0, 0, mapa);

        // Guardar PNG preview
        GuardarPreviewPNG(mapa, _resAlpha, numCapas);

        float segundos = (float)(DateTime.Now - t0).TotalSeconds;
        AlsasuaLogger.Info("Terreno",
            $"[SplatmapBake] Completado: {cntHierba}px hierba, {cntRoca}px roca, " +
            $"{cntAsfalto}px asfalto, {cntBosque}px bosque en {segundos:F1}s");

        UnityEditor.EditorUtility.SetDirty(_td);
        UnityEditor.AssetDatabase.SaveAssets();
    }
#endif

    /// <summary>Versión coroutine para runtime.</summary>
    IEnumerator BakeSplatmapCoroutine()
    {
        if (_terrain == null || _td == null)
        {
            AlsasuaLogger.Warn("Terreno", "[BakeSplatmap] Terrain no inicializado");
            yield break;
        }

        // Cargar geodatos adicionales
        var bosquesExtra = new List<Vector2[]>();
        var roadsOsm     = new List<Vector2[]>();
        {
            string pathBG   = FullPath(PATH_BOSQUES_GJ);
            string pathROsm = FullPath(PATH_ROADS_OSM);
            var t1 = Task.Run(() => ParseGeoJSONPoligonos(pathBG));
            var t2 = Task.Run(() => ParseGeoJSONLineas(pathROsm));
            while (!t1.IsCompleted || !t2.IsCompleted) yield return null;
            if (t1.IsCompletedSuccessfully) bosquesExtra = t1.Result;
            if (t2.IsCompletedSuccessfully) roadsOsm     = t2.Result;
        }
        _bosques.AddRange(bosquesExtra);

        _ortoUnity = CargarOrtoUnity();
        yield return null;

        int numCapas = _td.terrainLayers.Length;
        if (numCapas == 0) yield break;

        float terW   = _td.size.x;
        float terH_z = _td.size.z;
        float terY   = _td.size.y;
        Vector3 terPos = _terrain.transform.position;

        float[,] heights = _td.GetHeights(0, 0, _td.heightmapResolution, _td.heightmapResolution);
        int hRes = _td.heightmapResolution - 1;

        var maskRio    = new byte[_resAlpha * _resAlpha];
        var maskCauce  = new byte[_resAlpha * _resAlpha];
        var maskBosque = new byte[_resAlpha * _resAlpha];
        var maskRoad   = new byte[_resAlpha * _resAlpha];

        yield return StartCoroutine(PrecomputarMascarasConOSM(
            maskRio, maskCauce, maskBosque, maskRoad,
            terPos, terW, terH_z, roadsOsm));

        float[,,] mapa = new float[_resAlpha, _resAlpha, numCapas];
        int cntHierba = 0, cntRoca = 0, cntAsfalto = 0, cntBosque = 0;
        int lote = 0;
        var t0 = DateTime.Now;

        for (int ay = 0; ay < _resAlpha; ay++)
        {
            for (int ax = 0; ax < _resAlpha; ax++)
            {
                float nx = (float)ax / (_resAlpha - 1);
                float nz = (float)ay / (_resAlpha - 1);
                float altNorm   = SampleHeight01(heights, hRes, nx, nz);
                float pendiente = _td.GetSteepness(nx, nz);
                float wx        = terPos.x + nx * terW;
                float wz        = terPos.z + nz * terH_z;

                int pIdx = ay * _resAlpha + ax;
                bool esRio    = maskRio[pIdx]    > 0;
                bool esCauce  = maskCauce[pIdx]  > 0;
                bool esBosque = maskBosque[pIdx] > 0;
                bool esRoad   = maskRoad[pIdx]   > 0;

                Clasificar8Biomas(mapa, ay, ax, numCapas,
                    altNorm, pendiente, wx, wz, terY,
                    esRio, esCauce, esBosque, esRoad, terPos.y);

                if (_ortoUnity != null)
                    RefinarConOrtofoto(mapa, ay, ax, numCapas, wx, wz, terW, terH_z, terPos);

                int dom = BiomaDominante(mapa, ay, ax, numCapas);
                if (dom == 0 || dom == 1) cntHierba++;
                else if (dom == 2)        cntRoca++;
                else if (dom == 5)        cntAsfalto++;
                else if (dom == 7)        cntBosque++;
            }
            if (++lote >= 32) { lote = 0; yield return null; }
        }

        AplicarBlurGaussiano(mapa, _resAlpha, numCapas);
        _td.SetAlphamaps(0, 0, mapa);
        GuardarPreviewPNG(mapa, _resAlpha, numCapas);

        float seg = (float)(DateTime.Now - t0).TotalSeconds;
        AlsasuaLogger.Info("Terreno",
            $"[SplatmapBake] Completado: {cntHierba}px hierba, {cntRoca}px roca, " +
            $"{cntAsfalto}px asfalto, {cntBosque}px bosque en {seg:F1}s");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Precómputo de máscaras extendido con roadsOsm
    // ─────────────────────────────────────────────────────────────────────

    void PrecomputarMascarasSync(
        byte[] maskRio, byte[] maskCauce, byte[] maskBosque, byte[] maskRoad,
        Vector3 terPos, float terW, float terH_z,
        List<Vector2[]> roadsOsm)
    {
        for (int ay = 0; ay < _resAlpha; ay++)
        for (int ax = 0; ax < _resAlpha; ax++)
        {
            float wx = terPos.x + (float)ax / (_resAlpha - 1) * terW;
            float wz = terPos.z + (float)ay / (_resAlpha - 1) * terH_z;
            int pIdx = ay * _resAlpha + ax;

            float minDistRio = float.MaxValue;
            foreach (var seg in _cauces)
                if (seg.Length > 0)
                    minDistRio = Mathf.Min(minDistRio, DistAPolilinea(wx, wz, seg));

            if (minDistRio <= bufferRioM)          maskRio[pIdx]   = 1;
            if (minDistRio <= bufferRioM * 0.4f)   maskCauce[pIdx] = 1;

            foreach (var poly in _bosques)
                if (poly.Length > 2 && PuntoEnPoligono(wx, wz, poly))
                { maskBosque[pIdx] = 1; break; }

            // Carreteras: roads_unity.json (6m) + roads_osm.geojson (8m buffer)
            foreach (var seg in _carreteras)
                if (DistAPolilinea(wx, wz, seg) < 6f) { maskRoad[pIdx] = 1; break; }

            if (maskRoad[pIdx] == 0)
                foreach (var seg in roadsOsm)
                    if (DistAPolilinea(wx, wz, seg) < 8f) { maskRoad[pIdx] = 1; break; }
        }
    }

    IEnumerator PrecomputarMascarasConOSM(
        byte[] maskRio, byte[] maskCauce, byte[] maskBosque, byte[] maskRoad,
        Vector3 terPos, float terW, float terH_z,
        List<Vector2[]> roadsOsm)
    {
        int lote = 0;
        for (int ay = 0; ay < _resAlpha; ay++)
        {
            for (int ax = 0; ax < _resAlpha; ax++)
            {
                float wx = terPos.x + (float)ax / (_resAlpha - 1) * terW;
                float wz = terPos.z + (float)ay / (_resAlpha - 1) * terH_z;
                int pIdx = ay * _resAlpha + ax;

                float minDistRio = float.MaxValue;
                foreach (var seg in _cauces)
                    if (seg.Length > 0)
                        minDistRio = Mathf.Min(minDistRio, DistAPolilinea(wx, wz, seg));

                if (minDistRio <= bufferRioM)        maskRio[pIdx]   = 1;
                if (minDistRio <= bufferRioM * 0.4f) maskCauce[pIdx] = 1;

                foreach (var poly in _bosques)
                    if (poly.Length > 2 && PuntoEnPoligono(wx, wz, poly))
                    { maskBosque[pIdx] = 1; break; }

                foreach (var seg in _carreteras)
                    if (DistAPolilinea(wx, wz, seg) < 6f) { maskRoad[pIdx] = 1; break; }

                if (maskRoad[pIdx] == 0)
                    foreach (var seg in roadsOsm)
                        if (DistAPolilinea(wx, wz, seg) < 8f) { maskRoad[pIdx] = 1; break; }
            }
            if (++lote >= 32) { lote = 0; yield return null; }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Refinamiento por ortofoto
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Muestrea la ortofoto en la posición world y refina los pesos del alphamap.
    /// Usa coordenadas Unity → UV ortofoto_unity.png (proyección ortográfica plana).
    /// </summary>
    void RefinarConOrtofoto(float[,,] mapa, int ay, int ax, int numCapas,
        float wx, float wz, float terW, float terH_z, Vector3 terPos)
    {
        // UV: la ortofoto_unity.png cubre el terrain completo (terW × terH_z)
        float u = Mathf.Clamp01((wx - terPos.x) / terW);
        float v = Mathf.Clamp01((wz - terPos.z) / terH_z);

        Color col = _ortoUnity.GetPixelBilinear(u, v);
        float r = col.r, g = col.g, b = col.b;

        // Convertir RGB → HSL (H en 0-360)
        float cMax = Mathf.Max(r, g, b);
        float cMin = Mathf.Min(r, g, b);
        float delta = cMax - cMin;
        float luminosity = (cMax + cMin) * 0.5f;

        float hue = 0f;
        if (delta > 0.001f)
        {
            if      (cMax == r) hue = 60f * (((g - b) / delta) % 6f);
            else if (cMax == g) hue = 60f * ((b - r) / delta + 2f);
            else                hue = 60f * ((r - g) / delta + 4f);
            if (hue < 0f) hue += 360f;
        }

        const float kBoost = 0.15f; // Fuerza máxima de influencia de la ortofoto

        // Verde (hue 90-140°): refuerza bosque (capa 7) o hierba (capa 0)
        if (hue >= 90f && hue <= 140f)
        {
            float strength = kBoost * Mathf.InverseLerp(0.35f, 0.55f, luminosity);
            if (7 < numCapas) mapa[ay, ax, 7] = Mathf.Clamp01(mapa[ay, ax, 7] + strength);
            if (0 < numCapas) mapa[ay, ax, 0] = Mathf.Clamp01(mapa[ay, ax, 0] + strength * 0.5f);
            NormalizarFila(mapa, ay, ax, numCapas);
        }
        // Muy oscuro (lum < 0.15): refuerza roca/asfalto
        else if (luminosity < 0.15f)
        {
            float strength = kBoost * Mathf.InverseLerp(0.15f, 0f, luminosity);
            if (2 < numCapas) mapa[ay, ax, 2] = Mathf.Clamp01(mapa[ay, ax, 2] + strength * 0.5f);
            if (5 < numCapas) mapa[ay, ax, 5] = Mathf.Clamp01(mapa[ay, ax, 5] + strength * 0.5f);
            NormalizarFila(mapa, ay, ax, numCapas);
        }
        // Marrón (hue 20-50°): refuerza tierra/grava
        else if (hue >= 20f && hue <= 50f)
        {
            float strength = kBoost * 0.5f;
            if (3 < numCapas) mapa[ay, ax, 3] = Mathf.Clamp01(mapa[ay, ax, 3] + strength);
            if (4 < numCapas) mapa[ay, ax, 4] = Mathf.Clamp01(mapa[ay, ax, 4] + strength * 0.5f);
            NormalizarFila(mapa, ay, ax, numCapas);
        }
    }

    static void NormalizarFila(float[,,] mapa, int ay, int ax, int numCapas)
    {
        float suma = 0f;
        for (int c = 0; c < numCapas; c++) suma += mapa[ay, ax, c];
        if (suma < 0.0001f) return;
        for (int c = 0; c < numCapas; c++) mapa[ay, ax, c] /= suma;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Blur gaussiano 3×3
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Aplica un blur gaussiano 3×3 (sigma=1.0) al alphamap completo.</summary>
    static void AplicarBlurGaussiano(float[,,] mapa, int res, int numCapas)
    {
        // Kernel gaussiano 3×3 sigma=1.0
        // [1 2 1 / 2 4 2 / 1 2 1] / 16
        const float k00 = 1f/16f, k01 = 2f/16f, k02 = 1f/16f;
        const float k10 = 2f/16f, k11 = 4f/16f, k12 = 2f/16f;
        const float k20 = 1f/16f, k21 = 2f/16f, k22 = 1f/16f;

        var tmp = new float[res, res, numCapas];

        for (int ay = 0; ay < res; ay++)
        for (int ax = 0; ax < res; ax++)
        {
            int y0 = Mathf.Max(0, ay-1), y1 = ay, y2 = Mathf.Min(res-1, ay+1);
            int x0 = Mathf.Max(0, ax-1), x1 = ax, x2 = Mathf.Min(res-1, ax+1);
            for (int c = 0; c < numCapas; c++)
            {
                tmp[ay, ax, c] =
                    mapa[y0, x0, c]*k00 + mapa[y0, x1, c]*k01 + mapa[y0, x2, c]*k02 +
                    mapa[y1, x0, c]*k10 + mapa[y1, x1, c]*k11 + mapa[y1, x2, c]*k12 +
                    mapa[y2, x0, c]*k20 + mapa[y2, x1, c]*k21 + mapa[y2, x2, c]*k22;
            }
        }

        // Copiar resultado de vuelta y renormalizar
        for (int ay = 0; ay < res; ay++)
        for (int ax = 0; ax < res; ax++)
        {
            float suma = 0f;
            for (int c = 0; c < numCapas; c++) suma += tmp[ay, ax, c];
            float inv = suma > 0.0001f ? 1f / suma : 1f;
            for (int c = 0; c < numCapas; c++) mapa[ay, ax, c] = tmp[ay, ax, c] * inv;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Utilidades BakeSplatmap
    // ─────────────────────────────────────────────────────────────────────

    static int BiomaDominante(float[,,] mapa, int ay, int ax, int numCapas)
    {
        int dom = 0;
        float max = 0f;
        for (int c = 0; c < numCapas; c++)
            if (mapa[ay, ax, c] > max) { max = mapa[ay, ax, c]; dom = c; }
        return dom;
    }

    Texture2D CargarOrtoUnity()
    {
        string fullPath = FullPath(PATH_ORTO_UNITY);
        if (!File.Exists(fullPath)) return null;
        try
        {
            byte[] bytes = File.ReadAllBytes(fullPath);
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            tex.LoadImage(bytes);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode   = TextureWrapMode.Clamp;
            AlsasuaLogger.Info("Terreno",
                $"Ortofoto cargada: {tex.width}×{tex.height}px para refinamiento splatmap");
            return tex;
        }
        catch (Exception ex)
        {
            AlsasuaLogger.Warn("Terreno", $"Error cargando ortofoto: {ex.Message}");
            return null;
        }
    }

    void GuardarPreviewPNG(float[,,] mapa, int res, int numCapas)
    {
        try
        {
            // RGB preview: R=hierba(0), G=roca(2), B=asfalto(5)
            // A=bosque(7) — guardamos como RGBA pero PNG solo RGB visible
            var preview = new Texture2D(res, res, TextureFormat.RGB24, false);
            var pixels  = new Color[res * res];

            for (int ay = 0; ay < res; ay++)
            for (int ax = 0; ax < res; ax++)
            {
                float r = numCapas > 0 ? mapa[ay, ax, 0] : 0f; // hierba
                float g = numCapas > 2 ? mapa[ay, ax, 2] : 0f; // roca
                float b = numCapas > 5 ? mapa[ay, ax, 5] : 0f; // asfalto
                pixels[ay * res + ax] = new Color(r, g, b);
            }

            preview.SetPixels(pixels);
            preview.Apply();

            byte[] png  = preview.EncodeToPNG();
            string path = FullPath(PATH_SPLATMAP_PREVIEW);
            File.WriteAllBytes(path, png);

            if (Application.isPlaying)
                Destroy(preview);
            else
                DestroyImmediate(preview);

            AlsasuaLogger.Info("Terreno",
                $"Preview splatmap guardado: {path} ({png.Length/1024}KB)");
        }
        catch (Exception ex)
        {
            AlsasuaLogger.Warn("Terreno", $"Error guardando preview PNG: {ex.Message}");
        }
    }
    // ════════════════════════════════════════════════════════════════════════
    //  LLUVIA → SPLATMAP BARRO
    // ════════════════════════════════════════════════════════════════════════
    // Cuando la humedad global sube (SistemaCharcos._humedad), el terreno
    // muestra barro progresivamente en zonas planas de bioma hierba/prado.
    // El efecto se revierte al secarse (secado más lento que el mojado).

    void InicializarWetMud()
    {
        if (_td == null || capaFango == null) return;
        var capas = _td.terrainLayers;
        for (int i = 0; i < capas.Length; i++)
        {
            if (capas[i] == capaFango || capas[i] == capaTierraRibera)
            { _idxFango = i; break; }
        }
        if (_idxFango < 0)
            AlsasuaLogger.Warn("Terreno", "capaFango no encontrada en terrainLayers — wet mud desactivado");
    }

    IEnumerator CicloWetMud()
    {
        // Esperar a que el terreno y el alphamap estén listos
        while (!Listo) yield return new WaitForSeconds(1f);
        yield return new WaitForSeconds(5f);

        while (true)
        {
            yield return new WaitForSeconds(20f); // revisa cada 20 s

            if (_idxFango < 0 || _td == null) continue;

            float wetness = SistemaCharcos.Instance != null
                          ? SistemaCharcos.Instance.Humedad : 0f;

            // Solo procesar si cambió significativamente
            if (Mathf.Abs(wetness - _lastWetness) < 0.08f) continue;
            _lastWetness = wetness;

            if (_crWetMud != null) StopCoroutine(_crWetMud);
            _crWetMud = StartCoroutine(AplicarBarroCoroutine(wetness));
        }
    }

    IEnumerator AplicarBarroCoroutine(float wetness)
    {
        if (_td == null || _idxFango < 0) yield break;

        int res      = _td.alphamapResolution;
        int numCapas = _td.terrainLayers.Length;
        var mapa     = _td.GetAlphamaps(0, 0, res, res);

        // Factor de barro: 0 a wetness bajo umbral, crece hasta wetness=1
        float factorBarro = Mathf.SmoothStep(0f, 0.45f,
            Mathf.InverseLerp(umbralBarro, 1f, wetness));

        int procesadosPorFrame = 0;
        for (int ay = 0; ay < res; ay++)
        {
            for (int ax = 0; ax < res; ax++)
            {
                // Solo aplicar en bioma hierba (0) o prado (1)
                float pesoHierba = mapa[ay, ax, 0];
                float pesoPrado  = numCapas > 1 ? mapa[ay, ax, 1] : 0f;
                float pesoBase   = pesoHierba + pesoPrado;
                if (pesoBase < 0.25f) continue;

                // Pendiente: en zonas inclinadas el agua escurre → menos barro
                float nx = ax / (float)(res - 1);
                float nz = ay / (float)(res - 1);
                Vector3 normal = _td.GetInterpolatedNormal(nx, nz);
                float pendiente = 1f - normal.y; // 0=plano, 1=vertical
                if (pendiente > 0.35f) continue; // en pendiente fuerte no se acumula barro

                // Cantidad de barro proporcional a pesoBase y factor
                float cantBarro = pesoBase * factorBarro * (1f - pendiente * 2f);
                cantBarro = Mathf.Clamp(cantBarro, 0f, pesoBase * 0.7f);

                // Redistribuir: reducir hierba/prado y añadir fango
                float ratio = cantBarro / pesoBase;
                mapa[ay, ax, 0]       -= pesoHierba * ratio;
                if (numCapas > 1) mapa[ay, ax, 1] -= pesoPrado * ratio;
                mapa[ay, ax, _idxFango] = Mathf.Clamp01(mapa[ay, ax, _idxFango] + cantBarro);
            }

            procesadosPorFrame++;
            if (procesadosPorFrame % 16 == 0) yield return null; // 16 filas/frame
        }

        // Aplicar en lotes para no bloquear el render thread
        int lote = Mathf.Max(1, res / 16);
        for (int y = 0; y < res; y += lote)
        {
            int h = Mathf.Min(lote, res - y);
            _td.SetAlphamaps(0, y, Extraer(mapa, 0, y, res, h, numCapas)); // (x,y,mapa): el tamaño va implicito en el array
            yield return null;
        }

        AlsasuaLogger.Info("Terreno", $"Wet mud aplicado (humedad={wetness:F2}, barro={factorBarro:F2})");
    }

    // Helper: extrae un subarray del alphamap para SetAlphamaps parcial
    static float[,,] Extraer(float[,,] src, int x0, int y0, int w, int h, int c)
    {
        var dst = new float[h, w, c];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        for (int k = 0; k < c; k++)
            dst[y, x, k] = src[y + y0, x + x0, k];
        return dst;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MICRO-DETAIL — calidad de suelo a corta distancia
    // ════════════════════════════════════════════════════════════════════════
    // Configura el sistema de detalle del Terrain Unity para calidad máxima:
    //   · detailObjectDistance = 40m (Unity default 80, pero instanciado bien)
    //   · detailObjectDensity  = 0.8  (80% de densidad máxima)
    //   · wavingGrass ajustado por bioma dominante
    //
    // Los DetailPrototypes (pebbles, sticks, flores) se configuran en el
    // Inspector del TerrainData — aquí solo se aplica el mapa de densidad
    // si ya existen capas configuradas, ponderando por bioma del alphamap.
    //
    // Nota: SistemaDetalleTerreno.cs gestiona el ground cover GPU instanced
    // (piedrecitas, champiñones) que no depende del sistema de detail.

    IEnumerator AplicarMicroDetail()
    {
        while (!Listo || _td == null) yield return new WaitForSeconds(2f);
        yield return new WaitForSeconds(10f);

        if (_microDetailCreado) yield break;
        _microDetailCreado = true;

        // Calidad de renderizado de detalle
        if (_terrain != null)
        {
            _terrain.detailObjectDistance = 40f;
            _terrain.detailObjectDensity  = 0.8f;
        }

        // Si no hay DetailPrototypes configurados, no hay nada que distribuir
        if (_td.detailPrototypes == null || _td.detailPrototypes.Length == 0)
        {
            AlsasuaLogger.Info("Terreno", "Sin DetailPrototypes — micro-detail se configura en Inspector");
            yield break;
        }

        int res      = _td.detailResolution;
        int alphaRes = _td.alphamapResolution;
        if (res <= 0 || alphaRes <= 0) yield break;

        var alpha = _td.GetAlphamaps(0, 0, alphaRes, alphaRes);
        int numCapas = _td.terrainLayers.Length;

        // Para cada DetailPrototype existente, distribuir densidad según bioma
        for (int protoIdx = 0; protoIdx < _td.detailPrototypes.Length; protoIdx++)
        {
            var densidad = new int[res, res];
            for (int ay = 0; ay < res; ay++)
            {
                for (int ax = 0; ax < res; ax++)
                {
                    int aay = Mathf.Clamp(ay * alphaRes / res, 0, alphaRes - 1);
                    int aax = Mathf.Clamp(ax * alphaRes / res, 0, alphaRes - 1);

                    // Peso: hierba (0) + bosque (7 si existe)
                    float w = alpha[aay, aax, 0];
                    if (numCapas > 7) w += alpha[aay, aax, 7] * 0.5f;
                    w = Mathf.Clamp01(w);

                    // Ruido Perlin para distribución orgánica (no uniforme)
                    float ruido = Mathf.PerlinNoise(ax * 0.08f + protoIdx * 13f,
                                                    ay * 0.08f + protoIdx * 7f);
                    densidad[ay, ax] = w > 0.25f ? Mathf.RoundToInt(w * ruido * 6f) : 0;
                }
                if (ay % 64 == 0) yield return null;
            }
            _td.SetDetailLayer(0, 0, protoIdx, densidad);
        }

        AlsasuaLogger.Info("Terreno", $"Micro-detail aplicado a {_td.detailPrototypes.Length} capas");
    }


}
