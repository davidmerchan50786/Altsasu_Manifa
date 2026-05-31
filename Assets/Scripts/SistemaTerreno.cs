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
    TerrainData _td;
    Terrain     _terrain;
    int         _resAlpha;

    // ── Constante Z_min para altitud real ─────────────────────────────────
    const float Z_MIN = 511.33f;

    // ── Polilíneas de cauces en coords Unity (XZ) ─────────────────────────
    List<Vector2[]> _cauces  = new();
    // ── Polígonos de masas forestales en coords Unity ─────────────────────
    List<Vector2[]> _bosques = new();
    // ── Segmentos de carreteras en coords Unity ───────────────────────────
    List<Vector2[]> _carreteras = new();

    const string PATH_RIOS    = "Assets/AlsasuaData/rios_ejes.geojson";
    const string PATH_BOSQUES = "Assets/AlsasuaData/masas_forestales.geojson";
    const string PATH_ROADS   = "Assets/AlsasuaData/roads_unity.json";

    // ══════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ══════════════════════════════════════════════════════════════════════

    IEnumerator Start()
    {
        float t = 0f;
        while (Terrain.activeTerrain == null && t < 10f)
            { t += 0.25f; yield return new WaitForSeconds(0.25f); }

        _terrain = Terrain.activeTerrain;
        if (_terrain == null)
        {
            AlsasuaLogger.Warn("Terreno", "Sin Terrain activo — SistemaTerreno inactivo");
            yield break;
        }
        _td       = _terrain.terrainData;
        _resAlpha = _td.alphamapResolution;

        AlsasuaLogger.Info("Terreno",
            $"Terreno {_td.size.x:F0}×{_td.size.z:F0}m, " +
            $"alphamap {_resAlpha}², 8 biomas AAA");

        // Carga de geodatos en background
        yield return StartCoroutine(CargarGeodatos());

        // Asegurar capas TerrainLayer con texturas AAA
        AsegurarCapas8Biomas();
        yield return null;

        // Pintar alphamap 8 biomas
        yield return StartCoroutine(PintarAlphamap8Biomas());

        // Árboles terrain engine
        if (prefabsArbol != null && prefabsArbol.Length > 0)
            yield return StartCoroutine(PlantarArboles());

        // Hierba
        if (texturaHierba != null) ConfigurarHierba();

        ConfigurarCalidadVisual();

        Listo = true;
        AlsasuaLogger.Info("Terreno",
            $"✅ SistemaTerreno 8 biomas — {_cauces.Count} cauces, {_bosques.Count} bosques");
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

                // Convertir UTM → Unity
                float ux = (eUtm - 567951f) + 1918f;
                float uz = (nUtm - 4749902f) + 8570f;
                pts.Add(new Vector2(ux, uz));

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

        // Precalcular lookup de cauces (máscara booleana a resolución alpha)
        // Para evitar llamadas costosas por píxel, creamos arrays de distancia
        var maskRio    = new byte[_resAlpha * _resAlpha];
        var maskCauce  = new byte[_resAlpha * _resAlpha];
        var maskBosque = new byte[_resAlpha * _resAlpha];
        var maskRoad   = new byte[_resAlpha * _resAlpha];

        yield return StartCoroutine(PrecomputarMascaras(
            maskRio, maskCauce, maskBosque, maskRoad,
            terPos, terW, terH_z));

        float[,,] mapa  = new float[_resAlpha, _resAlpha, numCapas];
        int lote = 0;

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
                    esRio, esCauce, esBosque, esRoad);
            }

            if (++lote >= 32) { lote = 0; yield return null; }
        }

        _td.SetAlphamaps(0, 0, mapa);
        AlsasuaLogger.Info("Terreno", "✅ Alphamap 8 biomas aplicado");
    }

    IEnumerator PrecomputarMascaras(
        byte[] maskRio, byte[] maskCauce, byte[] maskBosque, byte[] maskRoad,
        Vector3 terPos, float terW, float terH_z)
    {
        float r2 = bufferRioM * bufferRioM;
        float r2Cauce = (bufferRioM * 0.4f) * (bufferRioM * 0.4f);

        int lote = 0;
        for (int ay = 0; ay < _resAlpha; ay++)
        {
            for (int ax = 0; ax < _resAlpha; ax++)
            {
                float wx = terPos.x + (float)ax / (_resAlpha - 1) * terW;
                float wz = terPos.z + (float)ay / (_resAlpha - 1) * terH_z;
                int pIdx = ay * _resAlpha + ax;

                // Rio / cauce
                float minDistRio = float.MaxValue;
                foreach (var seg in _cauces)
                    if (seg.Length > 0)
                        minDistRio = Mathf.Min(minDistRio, DistAPolilinea(wx, wz, seg));

                if (minDistRio <= bufferRioM)     maskRio[pIdx]   = 1;
                if (minDistRio <= bufferRioM * 0.4f) maskCauce[pIdx] = 1;

                // Bosque
                foreach (var poly in _bosques)
                    if (poly.Length > 2 && PuntoEnPoligono(wx, wz, poly))
                    { maskBosque[pIdx] = 1; break; }

                // Carreteras
                foreach (var seg in _carreteras)
                {
                    float d = DistAPolilinea(wx, wz, seg);
                    if (d < 6f) { maskRoad[pIdx] = 1; break; }
                }
            }
            if (++lote >= 32) { lote = 0; yield return null; }
        }
    }

    void Clasificar8Biomas(float[,,] mapa, int ay, int ax, int numCapas,
        float altNorm, float pendiente, float wx, float wz, float terY,
        bool esRio, bool esCauce, bool esBosque, bool esRoad)
    {
        for (int c = 0; c < numCapas; c++) mapa[ay, ax, c] = 0f;

        float altReal = altNorm * terY + Z_MIN;

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
        Vector3 normal = _td.GetInterpolatedNormal(
            Mathf.Clamp01(wx / _td.size.x),
            Mathf.Clamp01(wz / _td.size.z));
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

            float dist = Mathf.Sqrt((wx-1918f)*(wx-1918f)+(wz-8570f)*(wz-8570f));
            if (dist < 220f) continue;

            float pend = _td.GetSteepness(nx, nz);
            if (pend > 55f) continue;

            float altNorm = _td.GetInterpolatedHeight(nx, nz) / _td.size.y;
            float altReal = altNorm * _td.size.y + Z_MIN;
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
            float dist = Mathf.Sqrt((wx-1918f)*(wx-1918f)+(wz-8570f)*(wz-8570f));
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
                    esRio, esCauce, esBos, esRoad);
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
        if (_terrain == null) return 240f;
        return _terrain.SampleHeight(new Vector3(x,0,z)) + _terrain.transform.position.y;
    }

    // ── Nieve estacional ───────────────────────────────────────────────────
    void Update()
    {
        if (!Listo || _terrain == null) return;
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
}
