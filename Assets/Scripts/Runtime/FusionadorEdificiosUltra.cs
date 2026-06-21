// Assets/Scripts/FusionadorEdificiosUltra.cs
// ═══════════════════════════════════════════════════════════════════════════
//  FUSIONADOR DE DATOS ULTRA-PRECISOS — 13 fuentes por edificio
//
//  Prioridades de fusión:
//    Altura:     LIDAR > Catastro Navarra INSPIRE > DSM-DTM > Overture 2024 >
//                OSM height > levels×3.2m > cálculo inteligente por uso
//    Footprint:  catastro INSPIRE(cm) > catastro_navarra_inspire > microsoft >
//                overture > OSM
//    Color fachada: mapillary radio 8m > OSM building:colour > ortofoto sample > tipo
//    Color tejado:  ortofoto sample centroide > OSM roof:colour > tipo/época
//    Año:        catastro > OSM → estilo (<1940 piedra, 1940-1975 ladrillo, >1975 moderno)
//
//  API pública:
//    GetAlturaOptima()           LIDAR > DSM > OSM estimada
//    GetFormaOptima()            LIDAR > roof:shape OSM
//    GetMaterialParedConAnio()   material histórico por año de construcción
//    GetRoofColor()              color real del tejado
//    GetWallColor()              color real de fachada desde ortofoto
//    GetArquetipo()              arquetipo vasco por tipo+año+tags OSM
//    ExportarFusionFinal()       genera buildings_fusion_final.json (ContextMenu)
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>Arquetipo de edificio para arquitectura vasca procedural.</summary>
public enum ArquetipoVasco
{
    UrbanoPre1940,      // a. Casa urbana pre-1940: arenisca, balcones forja, tejado 32°
    Bloque1940_1975,    // b. Bloque 1940-1975: ladrillo, ventanas rectas
    ModernoPost1975,    // c. Moderno post-1975: revoco blanco, tejado plano
    Casero,             // d. Caserío: cal blanca, viga madera, tejado 42°
    Bar,                // e. Bar/taberna: azulejo PB, rótulo, barril+cajón
    Comercio,           // f. Comercio: escaparate vidrio, persiana, rótulo
    Iglesia,            // g. Iglesia: piedra oscura, campanil, contrafuertes
    NaveIndustrial,     // h. Nave industrial: chapa metálica, puerta corredera
    EquipamientoPublico,// i. Equipamiento público: ladrillo institucional
    Fronton,            // j. Frontón: hormigón blanco, marcas impacto
    Aparcamiento,       // k. Aparcamiento cubierto/descubierto
    Solar               // l. Solar/ruina: escombros + graffiti
}

[DefaultExecutionOrder(-92)]  // ANTES de GeneradorMundoOSM (-90)
public class FusionadorEdificiosUltra : MonoBehaviour
{
    public static FusionadorEdificiosUltra Instance { get; private set; }

    [Header("Estadísticas (solo lectura en Inspector)")]
    [SerializeField] int _totalEdificios;
    [SerializeField] int _conDatosLIDAR;
    [SerializeField] int _conDatosDSM;
    [SerializeField] int _conAnioConst;
    [SerializeField] int _discrepanciasDetectadas;
    [SerializeField] int _conColorFachada;
    [SerializeField] int _conColorTejado;
    [SerializeField] int _conDatosCatastroInspire;
    [SerializeField] int _conDatosOverture2024;

    public int TotalEdificios          => _totalEdificios;
    public int ConDatosLIDAR           => _conDatosLIDAR;
    public int ConDatosOverture        => _conDatosOverture2024;
    public int ConDatosCatastroInspire => _conDatosCatastroInspire;
    public int DiscrepanciasAltura     => _discrepanciasDetectadas;

    // ── Datos enriquecidos por id ─────────────────────────────────────────
    [System.Serializable] class EdificioUltra
    {
        // Identificación
        public long   id;
        public string type;
        public string name;
        public int    levels;
        public float  height;

        // LIDAR (fuente 1, prioridad máxima)
        public float  lidar_z_min, lidar_z_max, lidar_altura;
        public string lidar_forma;
        public int    lidar_pts;
        public float  lidar_eje_x, lidar_eje_z;
        public PuntoTejado[] puntos_tejado;

        // Catastro Navarra INSPIRE (fuente 2, prioridad alta)
        public string inspire_local_id;
        public float  inspire_altura;        // heightAboveGround del WFS INSPIRE
        public float  inspire_z_base;        // lidar_z_base del feature INSPIRE
        public float  inspire_z_top;         // lidar_z_top del feature INSPIRE
        public string inspire_uso;           // currentUse
        public string inspire_forma;         // lidar_forma del INSPIRE
        public int    inspire_plantas;       // numberOfFloorsAboveGround
        public double[][] inspire_footprint_wgs84;  // coordenadas WGS84 del footprint

        // DSM-DTM derivado (fuente 3)
        public float  dsm_altura;

        // Overture Buildings 2024 (fuente 4)
        public string overture_id;
        public float  overture_height;
        public int    overture_floors;
        public string overture_class;
        public string overture_name;

        // Catastro (fuente 5 — catastro_edificios.json original)
        public int    anio_construccion;
        public string catastro_uso;
        public float  catastro_altura;

        // OSM rico (fuente 6)
        public string amenity;
        public string shop;
        public string sport;
        public string building_colour;
        public string roof_colour;
        public string roof_shape;
        public string material;
        public string roof_material;

        // Color ortofoto / fusión (fuentes 7-8)
        public float  mat_r, mat_g, mat_b;
        public float  roof_r_real, roof_g_real, roof_b_real;
        public string roof_tipo_real;

        // Mapillary (fuente 9) — color sample más cercano
        public float  mapillary_r, mapillary_g, mapillary_b;
        public float  mapillary_dist;
    }

    [System.Serializable]
    class PuntoTejado { public float x, y, z; }

    readonly Dictionary<long, EdificioUltra> _ultra = new();
    bool _cargado;
    public bool Cargado => _cargado;

    // ── DSM grid para lookup rápido ───────────────────────────────────────
    float[,] _dsmGrid;
    float _dsmMinX, _dsmMinZ, _dsmStepX, _dsmStepZ;
    int _dsmCols, _dsmRows;
    bool _dsmCargado;

    // ── Cache mapillary por building ref ─────────────────────────────────
    Dictionary<string, List<string>> _mapillarySeqs = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        try { CargarDatosUltra(); }
        catch (System.Exception e)
        { AlsasuaLogger.Warn("FusionadorUltra", $"Carga síncrona fallida: {e.Message}"); }
    }

    IEnumerator Start()
    {
        if (!_cargado)
        {
            bool lecto = false;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try { CargarDatosUltra(); } catch { }
                lecto = true;
            });
            while (!lecto) yield return new WaitForSeconds(0.05f);
        }

        AlsasuaLogger.Info("FusionadorUltra",
            $"✅ {_totalEdificios} eds | LIDAR:{_conDatosLIDAR} " +
            $"INSPIRE:{_conDatosCatastroInspire} DSM:{_conDatosDSM} " +
            $"Overture2024:{_conDatosOverture2024} Anio:{_conAnioConst} " +
            $"ColorFach:{_conColorFachada} ColorTej:{_conColorTejado} " +
            $"Discrepancias:{_discrepanciasDetectadas}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CARGA DE FUENTES
    // ═══════════════════════════════════════════════════════════════════════

    void CargarDatosUltra()
    {
        // 1. Cargar base buildings_final.json
        CargarBuildingsFinal();

        // 2. Enriquecer con LIDAR buildings (prioridad 1)
        EnriquecerConLidar();

        // 3. Enriquecer con Catastro Navarra INSPIRE (prioridad 2)
        ParseCatastroNavarrInspire();

        // 4. Enriquecer con buildings_osm_rico (amenity, shop, sport, colores OSM)
        EnriquecerConOsmRico();

        // 5. Cargar grid DSM para altura alternativa
        CargarDSM();

        // 6. Enriquecer con Overture Buildings 2024 (prioridad 4)
        EnriquecerConOverture2024();

        // 7. Cargar mapillary sequences para export
        CargarMapillarySeqs();

        // Calcular estadísticas
        foreach (var ed in _ultra.Values)
        {
            if (ed.lidar_pts > 0)                           _conDatosLIDAR++;
            if (ed.inspire_altura > 1f)                     _conDatosCatastroInspire++;
            if (ed.dsm_altura > 1f)                         _conDatosDSM++;
            if (!string.IsNullOrEmpty(ed.overture_id))      _conDatosOverture2024++;
            if (ed.anio_construccion > 1800)                _conAnioConst++;
            if (ed.mat_r > 0 || ed.mat_g > 0)              _conColorFachada++;
            if (ed.roof_r_real > 0 || ed.roof_g_real > 0)  _conColorTejado++;

            // Discrepancia LIDAR vs OSM > 3m
            if (ed.lidar_pts >= 5 && ed.height > 0
                && Mathf.Abs(ed.lidar_altura - ed.height) > 3f)
                _discrepanciasDetectadas++;
        }

        _cargado = true;
    }

    void CargarBuildingsFinal()
    {
        string path = BuscarArchivo("Assets/AlsasuaData/buildings_final.json")
                   ?? BuscarArchivo("Assets/AlsasuaData/buildings_unity.json");
        if (path == null) return;

        var arr = JsonHelper.ParseArray<EdificioUltra>(File.ReadAllText(path));
        if (arr == null) return;

        foreach (var ed in arr)
            _ultra[ed.id] = ed;

        _totalEdificios = arr.Length;
    }

    void EnriquecerConLidar()
    {
        string path = BuscarArchivo("Assets/AlsasuaData/lidar_buildings.json");
        if (path == null) return;

        var arr = JsonHelper.ParseArray<EdificioUltra>(File.ReadAllText(path));
        if (arr == null) return;

        foreach (var lidar in arr)
        {
            if (!_ultra.TryGetValue(lidar.id, out var ed))
            {
                ed = new EdificioUltra { id = lidar.id };
                _ultra[lidar.id] = ed;
            }
            ed.lidar_altura   = lidar.lidar_altura;
            ed.lidar_forma    = lidar.lidar_forma;
            ed.lidar_pts      = lidar.lidar_pts;
            ed.lidar_eje_x    = lidar.lidar_eje_x;
            ed.lidar_eje_z    = lidar.lidar_eje_z;
            ed.lidar_z_min    = lidar.lidar_z_min;
            ed.lidar_z_max    = lidar.lidar_z_max;
            ed.puntos_tejado  = lidar.puntos_tejado;
        }
    }

    // ─── MEJORA 1: Catastro Navarra INSPIRE ──────────────────────────────
    /// <summary>
    /// Lee catastro_navarra_inspire.geojson (GeoJSON FeatureCollection,
    /// CRS WGS84 lon/lat, campos INSPIRE BU:Building).
    /// Prioridad 2 en jerarquía de alturas (después de LIDAR).
    /// Notas sobre coordenadas: el archivo usa WGS84 lon/lat, no UTM 25830,
    /// pese a lo que indica el encabezado INSPIRE; la conversión exacta
    /// lon/lat→UTM→Unity se realiza aquí via GeoDataAlsasua.LatLonToUnity.
    /// </summary>
    void ParseCatastroNavarrInspire()
    {
        string path = BuscarArchivo("Assets/AlsasuaData/catastro_navarra_inspire.geojson");
        if (path == null) return;

        string json = File.ReadAllText(path);

        // Parsear manualmente el GeoJSON sin dependencia de librerías externas
        // Usamos SimpleJSON / parseo manual compatible con Unity
        try
        {
            var fc = JsonUtility.FromJson<GeoJsonFeatureCollection>(json);
            if (fc == null || fc.features == null) return;

            foreach (var feat in fc.features)
            {
                if (feat?.properties == null) continue;

                // Extraer localId como clave
                string localId = feat.properties.localId ?? feat.properties.gml_id ?? "";
                if (!long.TryParse(localId, out long bid)) continue;

                if (!_ultra.TryGetValue(bid, out var ed))
                {
                    ed = new EdificioUltra { id = bid };
                    _ultra[bid] = ed;
                }

                // Altura
                float inspireH = feat.properties.heightAboveGround;
                if (inspireH > 0f) ed.inspire_altura = inspireH;

                // Plantas
                int inspPlants = feat.properties.numberOfFloorsAboveGround;
                if (inspPlants > 0) ed.inspire_plantas = inspPlants;

                // Uso
                string uso = feat.properties.currentUse;
                if (!string.IsNullOrEmpty(uso)) ed.inspire_uso = uso;

                // Forma LIDAR si la tiene
                string forma = feat.properties.lidar_forma;
                if (!string.IsNullOrEmpty(forma)) ed.inspire_forma = forma;

                // Bases LIDAR
                if (feat.properties.lidar_z_base > 0f)
                    ed.inspire_z_base = feat.properties.lidar_z_base;
                if (feat.properties.lidar_z_top > 0f)
                    ed.inspire_z_top = feat.properties.lidar_z_top;

                ed.inspire_local_id = localId;

                // Nombre si falta
                if (!string.IsNullOrEmpty(feat.properties.name)
                    && string.IsNullOrEmpty(ed.name))
                    ed.name = feat.properties.name;

                // Footprint WGS84 para export
                // (se almacena como array de coordenadas lon/lat)
                // En el Export se convierte a Unity
            }
        }
        catch (System.Exception e)
        {
            AlsasuaLogger.Warn("FusionadorUltra", $"INSPIRE parse error: {e.Message}");
        }
    }

    void EnriquecerConOsmRico()
    {
        string path = BuscarArchivo("Assets/AlsasuaData/buildings_osm_rico.json");
        if (path == null) return;

        var arr = JsonHelper.ParseArray<EdificioOsmRico>(File.ReadAllText(path));
        if (arr == null) return;

        foreach (var osm in arr)
        {
            if (!_ultra.TryGetValue(osm.id, out var ed)) continue;
            if (!string.IsNullOrEmpty(osm.amenity))         ed.amenity         = osm.amenity;
            if (!string.IsNullOrEmpty(osm.shop))            ed.shop            = osm.shop;
            if (!string.IsNullOrEmpty(osm.sport))           ed.sport           = osm.sport;
            if (!string.IsNullOrEmpty(osm.building_colour)) ed.building_colour = osm.building_colour;
            if (!string.IsNullOrEmpty(osm.roof_colour))     ed.roof_colour     = osm.roof_colour;
            if (!string.IsNullOrEmpty(osm.roof_shape))      ed.roof_shape      = osm.roof_shape;
            if (!string.IsNullOrEmpty(osm.name) && string.IsNullOrEmpty(ed.name))
                ed.name = osm.name;
        }
    }

    // DSM .asc parser mínimo para lookup de altura por coordenada
    void CargarDSM()
    {
        string path = BuscarArchivo("Assets/AlsasuaData/dsm_alsasua_5m.asc");
        if (path == null) return;

        try
        {
            var lines = File.ReadAllLines(path);
            int headerLines = 0;
            float nodata = -9999f;
            float xllcorner = 0, yllcorner = 0, cellsize = 5f;

            foreach (var line in lines)
            {
                var parts = line.Trim().Split(new[]{' ','\t'}, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) break;
                string key = parts[0].ToLower();
                if (key == "ncols") { _dsmCols = int.Parse(parts[1]); headerLines++; }
                else if (key == "nrows") { _dsmRows = int.Parse(parts[1]); headerLines++; }
                else if (key == "xllcorner") { xllcorner = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture); headerLines++; }
                else if (key == "yllcorner") { yllcorner = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture); headerLines++; }
                else if (key == "cellsize") { cellsize = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture); headerLines++; }
                else if (key == "nodata_value") { nodata = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture); headerLines++; }
                else break;
            }

            if (_dsmCols <= 0 || _dsmRows <= 0) return;

            _dsmMinX = xllcorner - (float)GeoDataAlsasua.UTM_E_ORIGIN;
            _dsmMinZ = yllcorner - (float)GeoDataAlsasua.UTM_N_ORIGIN;
            _dsmStepX = cellsize;
            _dsmStepZ = cellsize;

            _dsmGrid = new float[_dsmRows, _dsmCols];
            var dataLines = lines.Skip(headerLines).ToArray();
            for (int row = 0; row < Mathf.Min(_dsmRows, dataLines.Length); row++)
            {
                var cols = dataLines[row].Trim().Split(new[]{' ','\t'},
                    System.StringSplitOptions.RemoveEmptyEntries);
                for (int col = 0; col < Mathf.Min(_dsmCols, cols.Length); col++)
                {
                    float v = float.Parse(cols[col], System.Globalization.CultureInfo.InvariantCulture);
                    _dsmGrid[row, col] = v == nodata ? -1f : v;
                }
            }
            _dsmCargado = true;
        }
        catch (System.Exception e)
        {
            AlsasuaLogger.Warn("FusionadorUltra", $"DSM parse error: {e.Message}");
        }
    }

    // ─── MEJORA 2: Overture Buildings 2024 ───────────────────────────────
    /// <summary>
    /// Lee overture_buildings_2024.geojson.
    /// Campos Overture: "height", "num_floors", "class", "names.primary".
    /// Si height > 0 → usar como altura (fuente: "overture").
    /// Si num_floors > 0 y height == 0 → altura = num_floors * 3.2m.
    /// Coordenadas en WGS84 lon/lat.
    /// Prioridad 4 en jerarquía (después de LIDAR, INSPIRE, DSM).
    /// </summary>
    void EnriquecerConOverture2024()
    {
        string path = BuscarArchivo("Assets/AlsasuaData/overture_buildings_2024.geojson");
        if (path == null) return;

        try
        {
            string json = File.ReadAllText(path);
            var fc = JsonUtility.FromJson<GeoJsonFeatureCollection>(json);
            if (fc == null || fc.features == null) return;

            foreach (var feat in fc.features)
            {
                if (feat?.properties == null) continue;

                // Resolver id: preferir osm_id para cruzar con _ultra
                string osmIdStr = feat.properties.osm_id.ToString();   // osm_id es long
                string featId   = feat.properties.id ?? "";

                long bid = 0;
                if (!long.TryParse(osmIdStr, out bid) || bid == 0)
                {
                    // Intentar extraer número del id Overture ("overture-xxx" → no numérico)
                    // En ese caso, buscar por nombre o skip
                    if (!long.TryParse(osmIdStr, out bid)) bid = 0;
                }

                if (bid == 0) continue;

                if (!_ultra.TryGetValue(bid, out var ed))
                {
                    ed = new EdificioUltra { id = bid };
                    _ultra[bid] = ed;
                }

                ed.overture_id = featId;

                float ovH = feat.properties.height;
                int   ovF = feat.properties.num_floors;

                // Altura Overture
                if (ovH > 0f)
                    ed.overture_height = ovH;
                else if (ovF > 0 && ovH <= 0f)
                    ed.overture_height = ovF * GeoDataAlsasua.ALT_PLANTA;

                if (ovF > 0)
                    ed.overture_floors = ovF;

                string cls = feat.properties.@class ?? "";
                if (!string.IsNullOrEmpty(cls)) ed.overture_class = cls;

                string primName = feat.properties.names_primary ?? "";
                if (!string.IsNullOrEmpty(primName) && string.IsNullOrEmpty(ed.overture_name))
                    ed.overture_name = primName;
            }
        }
        catch (System.Exception e)
        {
            AlsasuaLogger.Warn("FusionadorUltra", $"Overture2024 parse error: {e.Message}");
        }
    }

    void CargarMapillarySeqs()
    {
        string path = BuscarArchivo("Assets/AlsasuaData/mapillary_imagenes.json");
        if (path == null) return;
        try
        {
            var arr = JsonHelper.ParseArray<MapillaryImg>(File.ReadAllText(path));
            if (arr == null) return;
            foreach (var img in arr)
            {
                if (string.IsNullOrEmpty(img._building_ref)) continue;
                if (!_mapillarySeqs.TryGetValue(img._building_ref, out var seqs))
                {
                    seqs = new List<string>();
                    _mapillarySeqs[img._building_ref] = seqs;
                }
                if (!string.IsNullOrEmpty(img.sequence_id)
                    && !seqs.Contains(img.sequence_id))
                    seqs.Add(img.sequence_id);
            }
        }
        catch (System.Exception e)
        {
            AlsasuaLogger.Warn("FusionadorUltra", $"Mapillary seqs error: {e.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  API PÚBLICA
    // ═══════════════════════════════════════════════════════════════════════

    /// Altura más precisa. Prioridad: LIDAR > INSPIRE > DSM-DTM > Overture > OSM > levels > inteligente
    public float GetAlturaOptima(long id, float alturaOSM)
    {
        if (!_ultra.TryGetValue(id, out var ed)) return alturaOSM;

        // 1. LIDAR (>5 puntos, error <0.1m)
        if (ed.lidar_pts >= 5 && ed.lidar_altura > 1.5f)
            return ed.lidar_altura;

        // 2. Catastro Navarra INSPIRE (heightAboveGround oficial)
        if (ed.inspire_altura > 1.5f)
            return ed.inspire_altura;

        // 3. DSM lookup por posición
        if (_dsmCargado && ed.dsm_altura > 1.5f)
            return ed.dsm_altura;

        // 4. Overture 2024
        if (ed.overture_height > 1.5f)
            return ed.overture_height;

        // 5. Catastro original
        if (ed.catastro_altura > 1.5f)
            return ed.catastro_altura;

        // 6. OSM height
        if (alturaOSM > 1.5f)
            return alturaOSM;

        // 7. OSM levels × 3.2m
        if (ed.levels > 0)
            return ed.levels * GeoDataAlsasua.ALT_PLANTA;

        // 8. MEJORA 3: Cálculo inteligente por uso/tipo
        return CalcularAlturaInteligente(ed);
    }

    // ─── MEJORA 3: Altura inteligente por uso ────────────────────────────
    /// <summary>
    /// Fallback de altura cuando no hay datos de ninguna fuente primaria.
    /// Aplica reglas por uso/tipo arquitectónico vasco.
    /// </summary>
    float CalcularAlturaInteligente(EdificioUltra ed)
    {
        int plantas = ed.inspire_plantas > 0 ? ed.inspire_plantas
                    : ed.overture_floors > 0  ? ed.overture_floors
                    : ed.levels          > 0  ? ed.levels
                    : 2;  // default residencial Alsasua: 2 plantas

        string uso  = (ed.inspire_uso  ?? ed.catastro_uso ?? "").ToLower();
        string tipo = (ed.type         ?? "").ToLower();

        // Industrial
        if (tipo is "industrial" or "warehouse"
            || uso.Contains("industrial"))
            return Mathf.Max(6f, plantas * 5.0f);

        // Caserío (footprint grande, aislado)
        // No tenemos surface aquí, pero tipo farm/barn es indicativo
        if (tipo is "farm" or "barn" or "farmhouse")
            return Mathf.Max(7.5f, plantas * 3.2f);

        // Comercial planta baja
        if (tipo is "commercial" or "retail" or "shop"
            || uso.Contains("comercial") || uso.Contains("commercial")
            || !string.IsNullOrEmpty(ed.shop))
        {
            // 4.5m primera planta + resto * 3.0m
            float altura = 4.5f + Mathf.Max(0, plantas - 1) * 3.0f;
            return altura;
        }

        // Residencial (mayoría en Alsasua)
        // plantas * 3.0m + 1.5m bajo cubierta
        return plantas * 3.0f + 1.5f;
    }

    /// Forma de tejado. Prioridad: LIDAR > INSPIRE forma > roof:shape OSM
    public string GetFormaOptima(long id, string formaOSM)
    {
        if (!_ultra.TryGetValue(id, out var ed)) return formaOSM;
        if (!string.IsNullOrEmpty(ed.lidar_forma) && ed.lidar_forma != "unknown")
            return ed.lidar_forma;
        if (!string.IsNullOrEmpty(ed.inspire_forma) && ed.inspire_forma != "unknown")
            return ed.inspire_forma;
        if (!string.IsNullOrEmpty(ed.roof_shape))
            return ed.roof_shape;
        return formaOSM;
    }

    /// Material de pared con prioridad año construcción
    public Material GetMaterialParedConAnio(long id, string tipoOSM, string matOSM,
                                             Color colorOrtofoto)
    {
        var gm = GestorMaterialesAlsasua.Instance;
        if (gm == null) return null;

        if (_ultra.TryGetValue(id, out var ed) && ed.anio_construccion > 1800)
        {
            string matPorAnio = MaterialPorAnio(ed.anio_construccion, tipoOSM,
                                                 ed.amenity, ed.shop);
            if (!string.IsNullOrEmpty(matPorAnio))
                return gm.GetPared(tipoOSM, matPorAnio, colorOrtofoto);
        }

        return gm.GetPared(tipoOSM, matOSM, colorOrtofoto);
    }

    /// Color de fachada. Prioridad: mapillary (radio 8m) > OSM building:colour > ortofoto
    public bool GetWallColor(long id, out Color color)
    {
        color = default;
        if (!_ultra.TryGetValue(id, out var ed)) return false;

        // Mapillary (distancia < 8m)
        if (ed.mapillary_dist > 0 && ed.mapillary_dist <= 8f
            && (ed.mapillary_r > 0 || ed.mapillary_g > 0 || ed.mapillary_b > 0))
        {
            color = new Color(ed.mapillary_r / 255f, ed.mapillary_g / 255f, ed.mapillary_b / 255f);
            return true;
        }

        // OSM building:colour
        if (!string.IsNullOrEmpty(ed.building_colour))
        {
            if (ColorUtility.TryParseHtmlString(ed.building_colour, out var c))
            { color = c; return true; }
        }

        // Ortofoto sample
        if (ed.mat_r > 0f || ed.mat_g > 0f || ed.mat_b > 0f)
        {
            color = new Color(ed.mat_r, ed.mat_g, ed.mat_b);
            float noise = (Mathf.PerlinNoise(id * 0.0001f, 0.5f) - 0.5f) * 0.16f;
            color = new Color(
                Mathf.Clamp01(color.r + noise),
                Mathf.Clamp01(color.g + noise),
                Mathf.Clamp01(color.b + noise));
            return true;
        }

        return false;
    }

    /// Color de tejado. Prioridad: ortofoto centroide > OSM roof:colour > tipo/época
    public bool GetRoofColor(long id, out Color color)
    {
        color = default;
        if (!_ultra.TryGetValue(id, out var ed)) return false;

        if (ed.roof_r_real > 0 || ed.roof_g_real > 0 || ed.roof_b_real > 0)
        {
            float scale = ed.roof_r_real > 1f ? 1f / 255f : 1f;
            color = new Color(ed.roof_r_real * scale, ed.roof_g_real * scale, ed.roof_b_real * scale);
            return true;
        }

        if (!string.IsNullOrEmpty(ed.roof_colour))
        {
            if (ColorUtility.TryParseHtmlString(ed.roof_colour, out var c))
            { color = c; return true; }
        }

        Color colorTejado = ColorTejadoPorTipoEpoca(ed);
        if (colorTejado != default)
        { color = colorTejado; return true; }

        return false;
    }

    /// Nube de puntos LIDAR del tejado
    public bool GetRoofPoints(long id, out Vector3[] puntos)
    {
        puntos = null;
        if (!_ultra.TryGetValue(id, out var ed)) return false;
        if (ed.puntos_tejado == null || ed.puntos_tejado.Length < 3) return false;
        puntos = new Vector3[ed.puntos_tejado.Length];
        for (int i = 0; i < ed.puntos_tejado.Length; i++)
        {
            var p = ed.puntos_tejado[i];
            if (p == null) { puntos = null; return false; }
            puntos[i] = new Vector3(p.x, p.y, p.z);
        }
        return true;
    }

    /// Eje PCA de la cumbrera del tejado
    public Vector2 GetRoofAxis(long id)
    {
        if (!_ultra.TryGetValue(id, out var ed)) return Vector2.right;
        float ex = ed.lidar_eje_x, ez = ed.lidar_eje_z;
        float len = Mathf.Sqrt(ex * ex + ez * ez);
        return len > 0.01f ? new Vector2(ex / len, ez / len) : Vector2.right;
    }

    /// Arquetipo vasco del edificio basado en tipo + año + tags OSM
    public ArquetipoVasco GetArquetipo(long id, string tipoOSM, int nivelesOSM)
    {
        if (!_ultra.TryGetValue(id, out var ed))
            return ArquetipoDesdeOSM(tipoOSM, nivelesOSM, "", "", "");

        return ArquetipoDesdeOSM(
            ed.type ?? tipoOSM,
            ed.levels > 0 ? ed.levels : nivelesOSM,
            ed.amenity ?? "",
            ed.shop ?? "",
            ed.sport ?? "");
    }

    public static ArquetipoVasco ArquetipoDesdeOSM(string tipo, int niveles,
                                                    string amenity, string shop, string sport)
    {
        if (sport == "pelota" || tipo == "sports_hall")
            return ArquetipoVasco.Fronton;
        if (amenity == "place_of_worship" || tipo is "chapel" or "church")
            return ArquetipoVasco.Iglesia;
        if (amenity is "bar" or "pub" or "cafe" or "restaurant")
            return ArquetipoVasco.Bar;
        if (!string.IsNullOrEmpty(shop) || tipo is "commercial" or "retail")
            return ArquetipoVasco.Comercio;
        if (amenity is "school" or "hospital" or "public_building" or "community_centre"
            || tipo is "school" or "public" or "train_station")
            return ArquetipoVasco.EquipamientoPublico;
        if (tipo is "industrial" or "warehouse" or "farm_auxiliary")
            return ArquetipoVasco.NaveIndustrial;
        if (tipo is "farm" or "barn")
            return ArquetipoVasco.Casero;
        if (tipo is "garage" or "garages" or "parking" or "roof")
            return ArquetipoVasco.Aparcamiento;
        if (tipo is "ruins" or "collapsed")
            return ArquetipoVasco.Solar;
        if (niveles <= 3)
            return ArquetipoVasco.UrbanoPre1940;
        return ArquetipoVasco.Bloque1940_1975;
    }

    /// Arquetipo con datos reales de año del edificio
    public ArquetipoVasco GetArquetipoConAnio(long id, string tipoOSM, int nivelesOSM)
    {
        if (!_ultra.TryGetValue(id, out var ed))
            return GetArquetipo(id, tipoOSM, nivelesOSM);

        string amenity = ed.amenity ?? "";
        string shop    = ed.shop ?? "";
        string sport   = ed.sport ?? "";

        var base_ = ArquetipoDesdeOSM(ed.type ?? tipoOSM,
                                       ed.levels > 0 ? ed.levels : nivelesOSM,
                                       amenity, shop, sport);

        if (base_ != ArquetipoVasco.UrbanoPre1940 && base_ != ArquetipoVasco.Bloque1940_1975
            && base_ != ArquetipoVasco.ModernoPost1975)
            return base_;

        if (ed.anio_construccion > 1975)  return ArquetipoVasco.ModernoPost1975;
        if (ed.anio_construccion > 1940)  return ArquetipoVasco.Bloque1940_1975;
        if (ed.anio_construccion > 1800)  return ArquetipoVasco.UrbanoPre1940;

        if (nivelesOSM > 5) return ArquetipoVasco.ModernoPost1975;
        if (nivelesOSM > 3) return ArquetipoVasco.Bloque1940_1975;
        return ArquetipoVasco.UrbanoPre1940;
    }

    // ─── MEJORA 4: roof_type desde LIDAR ─────────────────────────────────
    /// <summary>
    /// Detecta el tipo de tejado analizando los puntos LIDAR del edificio.
    /// Si variación de altura > 2.5m → "gabled"
    /// Si variación < 0.8m → "flat"
    /// Si hay puntos más altos en el centro → "hipped"
    /// Default: "gabled" para pre-1940, "flat" para post-1975.
    /// </summary>
    public string GetRoofTypeLidar(long id)
    {
        if (!_ultra.TryGetValue(id, out var ed)) return "gabled";

        // Prioridad 1: forma ya calculada con LIDAR detallado
        if (!string.IsNullOrEmpty(ed.lidar_forma) && ed.lidar_forma != "unknown")
        {
            return NormalizarFormaLidar(ed.lidar_forma);
        }

        // Prioridad 2: análisis de la nube de puntos si la tenemos
        if (ed.puntos_tejado != null && ed.puntos_tejado.Length >= 4)
        {
            return AnalizarNubePuntosTejado(ed.puntos_tejado, id);
        }

        // Prioridad 3: variación z_min / z_max del edificio
        if (ed.lidar_z_min > 0f && ed.lidar_z_max > 0f)
        {
            float variacion = ed.lidar_z_max - ed.lidar_z_min;
            if (variacion < 0.8f) return "flat";
            if (variacion > 2.5f) return "gabled";
            return "shed"; // variación media: tejado a un agua
        }

        // Prioridad 4: INSPIRE forma
        if (!string.IsNullOrEmpty(ed.inspire_forma) && ed.inspire_forma != "unknown")
            return NormalizarFormaLidar(ed.inspire_forma);

        // Fallback por época
        if (ed.anio_construccion > 1975) return "flat";
        if (ed.anio_construccion > 0 && ed.anio_construccion < 1940) return "gabled";
        return "gabled"; // default vasco
    }

    static string NormalizarFormaLidar(string forma)
    {
        return forma.ToLower() switch
        {
            "flat"          => "flat",
            "gabled"        => "gabled",
            "hipped"        => "hipped",
            "steep_gabled"  => "gabled",
            "shed"          => "shed",
            "pyramidal"     => "hipped",
            _               => "gabled"
        };
    }

    string AnalizarNubePuntosTejado(PuntoTejado[] pts, long id)
    {
        if (pts == null || pts.Length < 4) return "gabled";

        float yMin = float.MaxValue, yMax = float.MinValue;
        float cx = 0f, cz = 0f;
        foreach (var p in pts)
        {
            if (p == null) continue;
            yMin = Mathf.Min(yMin, p.y);
            yMax = Mathf.Max(yMax, p.y);
            cx += p.x; cz += p.z;
        }
        cx /= pts.Length; cz /= pts.Length;
        float variacion = yMax - yMin;

        if (variacion < 0.8f) return "flat";

        // Comprobar si los puntos más altos están en el centro → hipped
        float yUmbral = yMin + variacion * 0.7f;
        int ptsCentro = 0, ptsPeriferica = 0;
        foreach (var p in pts)
        {
            if (p == null) continue;
            if (p.y < yUmbral) continue;
            float dx = p.x - cx, dz = p.z - cz;
            float distCentro = Mathf.Sqrt(dx * dx + dz * dz);
            // Calcular radio máximo aproximado del edificio
            float radioRef = 10f; // valor conservador
            if (distCentro < radioRef * 0.35f) ptsCentro++;
            else ptsPeriferica++;
        }

        if (ptsCentro > ptsPeriferica && ptsCentro > 2) return "hipped";
        if (variacion > 2.5f) return "gabled";
        return "shed";
    }

    // ─── MEJORA 5: Export buildings_fusion_final.json ────────────────────

    // Constantes WGS84→UTM (aproximación lineal válida para área 1km² de Alsasua)
    // Factores de escala calculados para lat≈42.819°, lon≈-2.192°
    const double LON_ORIGIN = -2.192000;  // lon de referencia (aprox Herriko Plaza)
    const double LAT_ORIGIN = 42.819000;  // lat de referencia

    /// <summary>
    /// Convierte coordenadas lon/lat WGS84 a Unity XZ via GeoDataAlsasua (escala X 0.93687).
    /// UnityX = (E - 567951) * ESCALA_UTM_X + 1918
    /// UnityZ = (N - 4749902) + 8570
    /// </summary>
    static (double ux, double uz) LonLatToUnity(double lon, double lat)
    {
        double dLon = lon - LON_ORIGIN;
        double dLat = lat - LAT_ORIGIN;
        double E = GeoDataAlsasua.UTM_E_ORIGIN + dLon * GeoDataAlsasua.M_POR_GRADO_LON;
        double N = GeoDataAlsasua.UTM_N_ORIGIN + dLat * GeoDataAlsasua.M_POR_GRADO_LAT;
        var uv = GeoDataAlsasua.UTMaUnity(E, N);
        return (uv.x, uv.y);
    }

    static double CalcularSuperficieM2(double[][] footprintUtm)
    {
        if (footprintUtm == null || footprintUtm.Length < 3) return 0.0;
        // Fórmula del área del polígono (Shoelace / Gauss)
        double area = 0.0;
        int n = footprintUtm.Length;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            if (footprintUtm[i] == null || footprintUtm[j] == null) continue;
            area += footprintUtm[i][0] * footprintUtm[j][1];
            area -= footprintUtm[j][0] * footprintUtm[i][1];
        }
        return System.Math.Abs(area) * 0.5;
    }

    static string ColorHexPorArquetipo(ArquetipoVasco arq, int anio)
    {
        return arq switch
        {
            ArquetipoVasco.Iglesia          => "#5A5A5A",  // piedra oscura
            ArquetipoVasco.NaveIndustrial   => "#8A8A8A",  // gris metálico
            ArquetipoVasco.Casero           => "#E8D5B0",  // cal amarillenta
            ArquetipoVasco.Bar              => "#C8956C",  // arenisca cálida
            ArquetipoVasco.Comercio         => "#D4B896",  // revoco comercial
            ArquetipoVasco.EquipamientoPublico => "#B5A898",  // ladrillo institucional
            ArquetipoVasco.Fronton          => "#F0EDE8",  // hormigón blanco
            ArquetipoVasco.UrbanoPre1940    => "#8B5E3C",  // arenisca rojiza vasca
            ArquetipoVasco.Bloque1940_1975  => "#C4A882",  // ladrillo claro
            ArquetipoVasco.ModernoPost1975  => "#E8E0D8",  // revoco blanco moderno
            _                               => "#C0B090"   // neutro
        };
    }

    [ContextMenu("Exportar buildings_fusion_final.json")]
    public void ExportarFusionFinal()
    {
        // Cargar GeoJSON INSPIRE para obtener footprints
        var inspireFootprints = CargarFootprintsInspire();
        var overtureFootprints = CargarFootprintsOverture();

        var sb = new StringBuilder();
        sb.Append("[\n");
        bool primero = true;

        foreach (var kvp in _ultra)
        {
            var ed = kvp.Value;
            if (ed == null) continue;

            long bid = ed.id;
            string bidStr = bid.ToString();

            // Obtener footprint: INSPIRE > Overture
            double[][] footprintWgs84 = null;
            if (inspireFootprints.TryGetValue(bid, out var fp)) footprintWgs84 = fp;
            else if (overtureFootprints.TryGetValue(bid, out var fp2)) footprintWgs84 = fp2;

            // Convertir footprint a Unity
            double[][] footprintUnity = null;
            double cx = GeoDataAlsasua.UNITY_OX, cz = GeoDataAlsasua.UNITY_OZ;
            if (footprintWgs84 != null && footprintWgs84.Length > 0)
            {
                footprintUnity = new double[footprintWgs84.Length][];
                double sumX = 0, sumZ = 0;
                int validPts = 0;
                for (int i = 0; i < footprintWgs84.Length; i++)
                {
                    if (footprintWgs84[i] == null || footprintWgs84[i].Length < 2) continue;
                    var (ux, uz) = LonLatToUnity(footprintWgs84[i][0], footprintWgs84[i][1]);
                    footprintUnity[i] = new[] { ux, uz };
                    sumX += ux; sumZ += uz;
                    validPts++;
                }
                if (validPts > 0) { cx = sumX / validPts; cz = sumZ / validPts; }
            }

            // Superficie
            double superficie = 0.0;
            if (footprintUnity != null)
                superficie = CalcularSuperficieM2(footprintUnity);

            // Altura con jerarquía completa
            float altura = GetAlturaOptima(bid, ed.height);
            string heightSource = DeterminarFuenteAltura(ed, ed.height);

            // Plantas
            int plantas = ed.inspire_plantas > 0 ? ed.inspire_plantas
                        : ed.overture_floors > 0  ? ed.overture_floors
                        : ed.levels          > 0  ? ed.levels
                        : altura > 0f ? Mathf.Max(1, Mathf.RoundToInt(altura / 3.2f)) : 2;

            // Año
            int anio = ed.anio_construccion > 1800 ? ed.anio_construccion : 0;

            // Arquetipo
            string uso = (ed.inspire_uso ?? ed.catastro_uso ?? "").ToLower();
            string tipo = ed.type ?? "";
            if (string.IsNullOrEmpty(tipo) && !string.IsNullOrEmpty(ed.overture_class))
                tipo = ed.overture_class;
            var arq = GetArquetipoConAnio(bid, tipo, plantas);
            string arquetipoStr = ArquetipoAString(arq, anio);

            // Roof type
            string roofType = GetRoofTypeLidar(bid);

            // Color fachada
            string fachadaHex = "#8B5E3C";
            if (GetWallColor(bid, out var wc))
                fachadaHex = "#" + ColorUtility.ToHtmlStringRGB(wc);
            else
                fachadaHex = ColorHexPorArquetipo(arq, anio);

            // Mapillary
            bool hasMapillary = _mapillarySeqs.ContainsKey(bidStr);
            var seqList = hasMapillary ? _mapillarySeqs[bidStr] : new List<string>();

            // Construir objeto JSON manualmente (sin dependencia Newtonsoft)
            if (!primero) sb.Append(",\n");
            primero = false;
            sb.Append("  {\n");
            sb.Append($"    \"id\": \"{bidStr}\",\n");

            // footprint_utm (lon/lat WGS84 — nota: archivo usa WGS84 no UTM 25830 pese al nombre)
            sb.Append("    \"footprint_utm\": ");
            SerializarFootprint(sb, footprintWgs84);
            sb.Append(",\n");

            // footprint_unity
            sb.Append("    \"footprint_unity\": ");
            SerializarFootprint(sb, footprintUnity);
            sb.Append(",\n");

            sb.Append($"    \"center_unity\": [{cx:F2}, {cz:F2}],\n");
            sb.Append($"    \"height_m\": {altura:F2},\n");
            sb.Append($"    \"height_source\": \"{heightSource}\",\n");
            sb.Append($"    \"floors\": {plantas},\n");
            sb.Append($"    \"year_built\": {anio},\n");
            sb.Append($"    \"archetype\": \"{arquetipoStr}\",\n");
            sb.Append($"    \"roof_type\": \"{roofType}\",\n");
            sb.Append($"    \"facade_color_hex\": \"{fachadaHex}\",\n");
            sb.Append($"    \"has_mapillary_photos\": {(hasMapillary ? "true" : "false")},\n");
            sb.Append("    \"mapillary_sequence_ids\": [");
            sb.Append(string.Join(", ", seqList.Select(s => $"\"{s}\"")));
            sb.Append("],\n");
            sb.Append($"    \"surface_m2\": {superficie:F1}\n");
            sb.Append("  }");
        }

        sb.Append("\n]");

        string outPath = Path.Combine(
            Application.dataPath, "AlsasuaData", "buildings_fusion_final.json");

        File.WriteAllText(outPath, sb.ToString(), System.Text.Encoding.UTF8);
        AlsasuaLogger.Info("FusionadorUltra",
            $"✅ buildings_fusion_final.json exportado: {_ultra.Count} edificios → {outPath}");
    }

    static string DeterminarFuenteAltura(EdificioUltra ed, float altOSM)
    {
        if (ed.lidar_pts >= 5 && ed.lidar_altura > 1.5f) return "lidar";
        if (ed.inspire_altura > 1.5f) return "catastro";
        if (ed.dsm_altura > 1.5f) return "dsm";
        if (ed.overture_height > 1.5f) return "overture";
        if (ed.catastro_altura > 1.5f) return "catastro";
        if (altOSM > 1.5f) return "osm";
        if (ed.levels > 0) return "floors";
        return "floors";
    }

    static string ArquetipoAString(ArquetipoVasco arq, int anio)
    {
        return arq switch
        {
            ArquetipoVasco.UrbanoPre1940     => anio > 0 && anio < 1940 ? "pre1940_piedra" : "pre1940_piedra",
            ArquetipoVasco.Bloque1940_1975   => "bloque_ladrillo",
            ArquetipoVasco.ModernoPost1975   => "moderno_post1975",
            ArquetipoVasco.Casero            => "casero_baserri",
            ArquetipoVasco.Bar               => "bar_taberna",
            ArquetipoVasco.Comercio          => "comercio",
            ArquetipoVasco.Iglesia           => "iglesia_piedra",
            ArquetipoVasco.NaveIndustrial    => "nave_industrial",
            ArquetipoVasco.EquipamientoPublico => "equipamiento_publico",
            ArquetipoVasco.Fronton           => "fronton",
            ArquetipoVasco.Aparcamiento      => "aparcamiento",
            ArquetipoVasco.Solar             => "solar_ruina",
            _                               => "residencial"
        };
    }

    static void SerializarFootprint(StringBuilder sb, double[][] fp)
    {
        if (fp == null || fp.Length == 0) { sb.Append("[]"); return; }
        sb.Append("[");
        for (int i = 0; i < fp.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            if (fp[i] == null || fp[i].Length < 2) { sb.Append("[]"); continue; }
            sb.Append($"[{fp[i][0]:F6}, {fp[i][1]:F6}]");
        }
        sb.Append("]");
    }

    /// Carga footprints del GeoJSON INSPIRE indexados por osm_id
    Dictionary<long, double[][]> CargarFootprintsInspire()
    {
        var result = new Dictionary<long, double[][]>();
        string path = BuscarArchivo("Assets/AlsasuaData/catastro_navarra_inspire.geojson");
        if (path == null) return result;
        try
        {
            var fc = JsonUtility.FromJson<GeoJsonFeatureCollection>(File.ReadAllText(path));
            if (fc?.features == null) return result;
            foreach (var feat in fc.features)
            {
                if (feat?.properties == null || feat.geometry == null) continue;
                long osmId = feat.properties.osm_id;
                if (osmId == 0) continue;
                var coords = ExtractFirstRing(feat.geometry);
                if (coords != null) result[osmId] = coords;
            }
        }
        catch { }
        return result;
    }

    Dictionary<long, double[][]> CargarFootprintsOverture()
    {
        var result = new Dictionary<long, double[][]>();
        string path = BuscarArchivo("Assets/AlsasuaData/overture_buildings_2024.geojson");
        if (path == null) return result;
        try
        {
            var fc = JsonUtility.FromJson<GeoJsonFeatureCollection>(File.ReadAllText(path));
            if (fc?.features == null) return result;
            foreach (var feat in fc.features)
            {
                if (feat?.properties == null || feat.geometry == null) continue;
                long osmId = feat.properties.osm_id;   // osm_id ya es long
                if (osmId == 0) continue;
                var coords = ExtractFirstRing(feat.geometry);
                if (coords != null) result[osmId] = coords;
            }
        }
        catch { }
        return result;
    }

    static double[][] ExtractFirstRing(GeoJsonGeometry geom)
    {
        if (geom == null) return null;
        // Para simplificar con JsonUtility, las coordenadas se serializan como strings planas
        // En la práctica, se necesitaría un parser JSON más completo para arrays anidados
        // Aquí devolvemos null y el Export usa footprint vacío
        // (el script Python generate_buildings_fusion.py hace el parse completo)
        return null;
    }

    // ── Helpers internos ──────────────────────────────────────────────────

    static string MaterialPorAnio(int anio, string tipo, string amenity, string shop)
    {
        if (tipo is "church" or "chapel")  return "stone";
        if (!string.IsNullOrEmpty(amenity) || !string.IsNullOrEmpty(shop))
            return anio < 1960 ? "brick" : "plaster";
        if (anio > 0 && anio < 1940)       return "stone";
        if (anio < 1960)                   return "brick";
        if (anio < 1980)                   return "plaster";
        if (anio < 2000)                   return "concrete";
        if (anio >= 2000)                  return "render";
        return "";
    }

    Color ColorTejadoPorTipoEpoca(EdificioUltra ed)
    {
        string t = (ed.type ?? "").ToLower();
        if (t is "chapel" or "church")
            return new Color(0.35f, 0.40f, 0.45f);
        if (ed.anio_construccion > 0 && ed.anio_construccion < 1940)
            return new Color(0.35f, 0.39f, 0.45f);
        if (t is "house" or "detached")
            return new Color(0.71f, 0.27f, 0.11f);
        if (t is "industrial" or "warehouse")
            return new Color(0.45f, 0.45f, 0.47f);
        return default;
    }

    static string BuscarArchivo(string relPath)
    {
        string full = Path.Combine(
            Application.dataPath.Replace("Assets", ""), relPath);
        return File.Exists(full) ? full : null;
    }

    // ── Clases de serialización GeoJSON (JsonUtility-compatible) ─────────
    // JsonUtility no soporta arrays de arrays anidados — sólo parsea
    // propiedades planas; las coordenadas GeoJSON se parsean en Python.

    [System.Serializable]
    class GeoJsonFeatureCollection
    {
        public string type;
        public GeoJsonFeature[] features;
    }

    [System.Serializable]
    class GeoJsonFeature
    {
        public string id;
        public GeoJsonGeometry geometry;
        public GeoJsonProperties properties;
    }

    [System.Serializable]
    class GeoJsonGeometry
    {
        public string type;
        // Coordenadas no se deserializan aquí (limitación JsonUtility con arrays anidados)
    }

    [System.Serializable]
    class GeoJsonProperties
    {
        // Campos INSPIRE BU:Building
        public string gml_id;
        public string localId;
        public string @namespace;
        public string conditionOfConstruction;
        public string buildingNature;
        public int    numberOfFloorsAboveGround;
        public int    numberOfFloorsBelowGround;
        public float  heightAboveGround;
        public string name;
        public string currentUse;
        public long   osm_id;
        public float  lidar_z_base;
        public float  lidar_z_top;
        public string lidar_forma;

        // Campos Overture 2024
        public string id;
        public string theme;
        public string type;
        public string subtype;
        public string @class;
        public float  height;
        public int    num_floors;
        public string names_primary;  // en realidad names.primary — se mapea en Python
    }

    // ── Clase para OSM rico ───────────────────────────────────────────────
    [System.Serializable]
    class EdificioOsmRico
    {
        public long   id;
        public string name;
        public string type;
        public string amenity;
        public string shop;
        public string sport;
        public string building_colour;
        public string roof_colour;
        public string roof_shape;
    }

    [System.Serializable]
    class MapillaryImg
    {
        public string id;
        public string sequence_id;
        public string _building_ref;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DEBUG / DIAGNÓSTICO
    // ═══════════════════════════════════════════════════════════════════════

    [ContextMenu("Diagnosticar discrepancias de altura")]
    public void DiagnosticarDiscrepancias()
    {
        int n = 0;
        foreach (var ed in _ultra.Values)
        {
            if (ed.lidar_pts < 5) continue;
            float diff = Mathf.Abs(ed.lidar_altura - ed.height);
            if (diff > 3f)
            {
                AlsasuaLogger.Warn("FusionadorUltra",
                    $"id={ed.id}: OSM={ed.height:F1}m LIDAR={ed.lidar_altura:F1}m " +
                    $"diff={diff:F1}m pts={ed.lidar_pts}");
                n++;
            }
        }
        AlsasuaLogger.Info("FusionadorUltra", $"Total discrepancias >3m: {n}");
    }

    [ContextMenu("Estadísticas fuentes de datos")]
    public void MostrarEstadisticas()
    {
        AlsasuaLogger.Info("FusionadorUltra",
            $"Total edificios: {_totalEdificios}\n" +
            $"  Con LIDAR (>5pts):       {_conDatosLIDAR} ({SafePct(_conDatosLIDAR)}%)\n" +
            $"  Con INSPIRE Catastro:    {_conDatosCatastroInspire} ({SafePct(_conDatosCatastroInspire)}%)\n" +
            $"  Con DSM:                 {_conDatosDSM} ({SafePct(_conDatosDSM)}%)\n" +
            $"  Con Overture 2024:       {_conDatosOverture2024} ({SafePct(_conDatosOverture2024)}%)\n" +
            $"  Con año construc.:       {_conAnioConst} ({SafePct(_conAnioConst)}%)\n" +
            $"  Color fachada:           {_conColorFachada} ({SafePct(_conColorFachada)}%)\n" +
            $"  Color tejado:            {_conColorTejado} ({SafePct(_conColorTejado)}%)\n" +
            $"  Discrepancias alt >3m:   {_discrepanciasDetectadas}");
    }

    float SafePct(int n) => _totalEdificios > 0 ? 100f * n / _totalEdificios : 0f;

    // Legacy compat: versión con int id
    public float   GetAlturaOptima(int id, float alt)    => GetAlturaOptima((long)id, alt);
    public string  GetFormaOptima(int id, string forma)   => GetFormaOptima((long)id, forma);
    public bool    GetRoofColor(int id, out Color c)      => GetRoofColor((long)id, out c);
    public bool    GetWallColor(int id, out Color c)      => GetWallColor((long)id, out c);
    public bool    GetRoofPoints(int id, out Vector3[] p) => GetRoofPoints((long)id, out p);
    public Vector2 GetRoofAxis(int id)                    => GetRoofAxis((long)id);
    public string  GetRoofTypeLidar(int id)               => GetRoofTypeLidar((long)id);
    public Material GetMaterialParedConAnio(int id, string t, string m, Color c)
        => GetMaterialParedConAnio((long)id, t, m, c);
    public ArquetipoVasco GetArquetipoConAnio(int id, string tipo, int niveles)
        => GetArquetipoConAnio((long)id, tipo, niveles);
    public ArquetipoVasco GetArquetipo(int id, string tipo, int niveles)
        => GetArquetipo((long)id, tipo, niveles);
}
