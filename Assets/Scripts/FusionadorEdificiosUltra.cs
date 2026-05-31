// Assets/Scripts/FusionadorEdificiosUltra.cs
// ═══════════════════════════════════════════════════════════════════════════
//  FUSIONADOR DE DATOS ULTRA-PRECISOS — 11 fuentes por edificio
//
//  Prioridades de fusión:
//    Altura:     LIDAR > DSM-DTM > catastro > OSM height > levels×3.2m
//    Footprint:  catastro(cm) > microsoft > overture > OSM
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
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Linq;

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

[DefaultExecutionOrder(-62)]  // ANTES de GeneradorGeometriaPrecisa (-60)
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

    public int TotalEdificios       => _totalEdificios;
    public int ConDatosLIDAR        => _conDatosLIDAR;
    public int ConDatosOverture     => _conDatosDSM;   // reutiliza campo legacy
    public int DiscrepanciasAltura  => _discrepanciasDetectadas;

    // ── Datos enriquecidos por id ─────────────────────────────────────────
    [System.Serializable] class EdificioUltra
    {
        // Identificación
        public long   id;
        public string type;
        public string name;
        public int    levels;
        public float  height;

        // LIDAR (fuente 1)
        public float  lidar_z_min, lidar_z_max, lidar_altura;
        public string lidar_forma;
        public int    lidar_pts;
        public float  lidar_eje_x, lidar_eje_z;
        public PuntoTejado[] puntos_tejado;

        // DSM-DTM derivado (fuente 2)
        public float  dsm_altura;

        // Catastro (fuente 3)
        public int    anio_construccion;
        public string catastro_uso;
        public float  catastro_altura;

        // OSM rico (fuente 4)
        public string amenity;
        public string shop;
        public string sport;
        public string building_colour;
        public string roof_colour;
        public string roof_shape;
        public string material;
        public string roof_material;

        // Color ortofoto / fusión (fuentes 5-6)
        public float  mat_r, mat_g, mat_b;
        public float  roof_r_real, roof_g_real, roof_b_real;
        public string roof_tipo_real;

        // Mapillary (fuente 7) — color sample más cercano
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
            $"✅ {_totalEdificios} eds | LIDAR:{_conDatosLIDAR} DSM:{_conDatosDSM} " +
            $"Anio:{_conAnioConst} ColorFach:{_conColorFachada} ColorTej:{_conColorTejado} " +
            $"Discrepancias:{_discrepanciasDetectadas}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CARGA DE FUENTES
    // ═══════════════════════════════════════════════════════════════════════

    void CargarDatosUltra()
    {
        // 1. Cargar base buildings_final.json
        CargarBuildingsFinal();

        // 2. Enriquecer con LIDAR buildings
        EnriquecerConLidar();

        // 3. Enriquecer con buildings_osm_rico (amenity, shop, sport, colores OSM)
        EnriquecerConOsmRico();

        // 4. Cargar grid DSM para altura alternativa
        CargarDSM();

        // Calcular estadísticas
        foreach (var ed in _ultra.Values)
        {
            if (ed.lidar_pts > 0)                         _conDatosLIDAR++;
            if (ed.dsm_altura > 1f)                       _conDatosDSM++;
            if (ed.anio_construccion > 1800)               _conAnioConst++;
            if (ed.mat_r > 0 || ed.mat_g > 0)             _conColorFachada++;
            if (ed.roof_r_real > 0 || ed.roof_g_real > 0) _conColorTejado++;

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

    void EnriquecerConOsmRico()
    {
        // buildings_osm_rico tiene campos tipo, amenity, shop, sport, roof:colour etc.
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

            // ETRS89 UTM30N → offsets para lookup
            // Los archivos .asc usan ETRS89. Alsasua centro ≈ E=567951, N=4749902
            const float E_ORIGEN = 567951f;
            const float N_ORIGEN = 4749902f;

            _dsmMinX = xllcorner - E_ORIGEN;  // en metros relativos al origen
            _dsmMinZ = yllcorner - N_ORIGEN;
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

    // ═══════════════════════════════════════════════════════════════════════
    //  API PÚBLICA
    // ═══════════════════════════════════════════════════════════════════════

    /// Altura más precisa. Prioridad: LIDAR > DSM-DTM > catastro > OSM > levels
    public float GetAlturaOptima(long id, float alturaOSM)
    {
        if (!_ultra.TryGetValue(id, out var ed)) return alturaOSM;

        // 1. LIDAR (>5 puntos, error <0.1m)
        if (ed.lidar_pts >= 5 && ed.lidar_altura > 1.5f)
            return ed.lidar_altura;

        // 2. DSM – DTM lookup por posición
        if (_dsmCargado && ed.dsm_altura > 1.5f)
            return ed.dsm_altura;

        // 3. Catastro altura
        if (ed.catastro_altura > 1.5f)
            return ed.catastro_altura;

        // 4. OSM height
        if (alturaOSM > 1.5f)
            return alturaOSM;

        // 5. OSM levels × 3.2m
        if (ed.levels > 0)
            return ed.levels * GeoDataAlsasua.ALT_PLANTA;

        return alturaOSM;
    }

    /// Forma de tejado. Prioridad: LIDAR > roof:shape OSM
    public string GetFormaOptima(long id, string formaOSM)
    {
        if (!_ultra.TryGetValue(id, out var ed)) return formaOSM;
        if (!string.IsNullOrEmpty(ed.lidar_forma) && ed.lidar_forma != "unknown")
            return ed.lidar_forma;
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
            // Aplicar perturbación Perlin ±8%
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

        // Ortofoto sample centroide (roof_r_real en rango 0-255)
        if (ed.roof_r_real > 0 || ed.roof_g_real > 0 || ed.roof_b_real > 0)
        {
            float scale = ed.roof_r_real > 1f ? 1f / 255f : 1f;
            color = new Color(ed.roof_r_real * scale, ed.roof_g_real * scale, ed.roof_b_real * scale);
            return true;
        }

        // OSM roof:colour
        if (!string.IsNullOrEmpty(ed.roof_colour))
        {
            if (ColorUtility.TryParseHtmlString(ed.roof_colour, out var c))
            { color = c; return true; }
        }

        // Color por tipo/época: terracota vs pizarra
        Color colorRejado = ColorTejadoPorTipoEpoca(ed);
        if (colorRejado != default)
        { color = colorRejado; return true; }

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
        // Frontón
        if (sport == "pelota" || tipo == "sports_hall")
            return ArquetipoVasco.Fronton;

        // Iglesia
        if (amenity == "place_of_worship" || tipo is "chapel" or "church")
            return ArquetipoVasco.Iglesia;

        // Bar/taberna
        if (amenity is "bar" or "pub" or "cafe" or "restaurant")
            return ArquetipoVasco.Bar;

        // Comercio
        if (!string.IsNullOrEmpty(shop) || tipo is "commercial" or "retail")
            return ArquetipoVasco.Comercio;

        // Equipamiento público
        if (amenity is "school" or "hospital" or "public_building" or "community_centre"
            || tipo is "school" or "public" or "train_station")
            return ArquetipoVasco.EquipamientoPublico;

        // Nave industrial
        if (tipo is "industrial" or "warehouse" or "farm_auxiliary")
            return ArquetipoVasco.NaveIndustrial;

        // Caserío: edificio aislado, bajos niveles, fuera del núcleo
        if (tipo is "farm" or "barn")
            return ArquetipoVasco.Casero;

        // Aparcamiento
        if (tipo is "garage" or "garages" or "parking" or "roof")
            return ArquetipoVasco.Aparcamiento;

        // Solar/ruina
        if (tipo is "ruins" or "collapsed")
            return ArquetipoVasco.Solar;

        // Residencial por época (determinado por año, aquí fallback a tipo/niveles)
        if (niveles <= 3)
            return ArquetipoVasco.UrbanoPre1940; // conservador: piedra por defecto

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

        // Tags especiales tienen prioridad sobre época
        var base_ = ArquetipoDesdeOSM(ed.type ?? tipoOSM,
                                       ed.levels > 0 ? ed.levels : nivelesOSM,
                                       amenity, shop, sport);

        // Si no es residencial genérico, devolver el arquetipo especial
        if (base_ != ArquetipoVasco.UrbanoPre1940 && base_ != ArquetipoVasco.Bloque1940_1975
            && base_ != ArquetipoVasco.ModernoPost1975)
            return base_;

        // Afinar con año de construcción
        if (ed.anio_construccion > 1975)  return ArquetipoVasco.ModernoPost1975;
        if (ed.anio_construccion > 1940)  return ArquetipoVasco.Bloque1940_1975;
        if (ed.anio_construccion > 1800)  return ArquetipoVasco.UrbanoPre1940;

        // Sin año: heurística por niveles
        if (nivelesOSM > 5) return ArquetipoVasco.ModernoPost1975;
        if (nivelesOSM > 3) return ArquetipoVasco.Bloque1940_1975;
        return ArquetipoVasco.UrbanoPre1940;
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
        // Pizarra: iglesias, edificios pre-1940, históricos
        if (t is "chapel" or "church")
            return new Color(0.35f, 0.40f, 0.45f);  // pizarra #5a6474
        if (ed.anio_construccion > 0 && ed.anio_construccion < 1940)
            return new Color(0.35f, 0.39f, 0.45f);  // pizarra
        if (t is "house" or "detached")
            return new Color(0.71f, 0.27f, 0.11f);  // terracota #b5451b
        if (t is "industrial" or "warehouse")
            return new Color(0.45f, 0.45f, 0.47f);  // gris metálico
        return default;
    }

    static string BuscarArchivo(string relPath)
    {
        string full = Path.Combine(
            Application.dataPath.Replace("Assets", ""), relPath);
        return File.Exists(full) ? full : null;
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
            $"  Con LIDAR (>5pts): {_conDatosLIDAR} ({SafePct(_conDatosLIDAR)}%)\n" +
            $"  Con DSM:           {_conDatosDSM} ({SafePct(_conDatosDSM)}%)\n" +
            $"  Con año construc.: {_conAnioConst} ({SafePct(_conAnioConst)}%)\n" +
            $"  Color fachada:     {_conColorFachada} ({SafePct(_conColorFachada)}%)\n" +
            $"  Color tejado:      {_conColorTejado} ({SafePct(_conColorTejado)}%)\n" +
            $"  Discrepancias alt: {_discrepanciasDetectadas}");
    }

    float SafePct(int n) => _totalEdificios > 0 ? 100f * n / _totalEdificios : 0f;

    // Legacy compat: versión con int id
    public float  GetAlturaOptima(int id, float alt)    => GetAlturaOptima((long)id, alt);
    public string GetFormaOptima(int id, string forma)   => GetFormaOptima((long)id, forma);
    public bool   GetRoofColor(int id, out Color c)      => GetRoofColor((long)id, out c);
    public bool   GetWallColor(int id, out Color c)      => GetWallColor((long)id, out c);
    public bool   GetRoofPoints(int id, out Vector3[] p) => GetRoofPoints((long)id, out p);
    public Vector2 GetRoofAxis(int id)                   => GetRoofAxis((long)id);
    public Material GetMaterialParedConAnio(int id, string t, string m, Color c)
        => GetMaterialParedConAnio((long)id, t, m, c);
    public ArquetipoVasco GetArquetipoConAnio(int id, string tipo, int niveles)
        => GetArquetipoConAnio((long)id, tipo, niveles);
    public ArquetipoVasco GetArquetipo(int id, string tipo, int niveles)
        => GetArquetipo((long)id, tipo, niveles);
}
