// Assets/Scripts/GestorMaterialesAlsasua.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GESTOR DE MATERIALES — mapea cada superficie de Alsasua al material
//  PBR correcto de la biblioteca local (280 materiales disponibles).
//
//  Jerarquía de prioridad para paredes:
//    1. building:material tag de OSM  (más específico)
//    2. Color real de ortofoto IGN    (verificado visualmente)
//    3. Tipo de edificio OSM          (inferido)
//    4. Material por defecto          (revoco vasco)
//
//  Para tejados:
//    1. roof_tipo_real de fotogrametría (pizarra_gris, cemento_gris_claro…)
//    2. roof:material / roof:colour OSM
//    3. Tipo de edificio OSM
//
//  Materiales clave de Alsasua/Altsasua (arquitectura vasca):
//    · Paredes:  revoco/enlucido (gris-beige) en planta superior
//                piedra arenisca en planta baja de edificios históricos
//    · Tejados:  pizarra gris (dominante), cemento gris, vegetación
//    · Aceras:   adoquín/losa de hormigón en peatonales
//                acera de hormigón en residencial
//    · Calzada:  asfalto, adoquín en zona histórica
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections.Generic;

[DefaultExecutionOrder(-85)]  // antes de IntegradorAssets(-80), SistemaZonas(-80), generadores
public class GestorMaterialesAlsasua : MonoBehaviour
{
    public static GestorMaterialesAlsasua Instance { get; private set; }

    // Cache: evita crear new Material() para cada uno de los 1030 edificios.
    // Clave = (instanceID_base, color32_packed). Limpiado en OnDestroy.
    readonly Dictionary<long, Material> _matCache = new();

    // ════════════════════════════════════════════════════════════════════
    //  MATERIALES POR CATEGORÍA (asignados desde CreadorEscenaPrincipal)
    // ════════════════════════════════════════════════════════════════════

    [Header("═══ PAREDES ═══")]
    [Tooltip("Revoco/enlucido gris — más común en Alsasua")]
    public Material matParedRevoco;         // rough_plaster_03
    [Tooltip("Revoco beige/cálido — bloques de pisos")]
    public Material matParedRevocoBeis;     // beige_wall_001
    [Tooltip("Arcilla/barro enlucido — casas rurales")]
    public Material matParedArcilaPlast;    // clay_plaster
    [Tooltip("Piedra arenisca — edificios históricos, iglesia")]
    public Material matParedPiedra;         // sandstone_brick_wall_01
    [Tooltip("Piedra de sillería — iglesia/palacio")]
    public Material matParedSilleria;       // stone_brick_wall_001
    [Tooltip("Ladrillo antiguo — edificios industriales/pre-guerra")]
    public Material matParedLadrillo;       // brick_wall_001
    [Tooltip("Ladrillo de iglesia — paramento eclesiástico")]
    public Material matParedIglesia;        // church_bricks_02
    [Tooltip("Hormigón liso — edificios modernos/industriales")]
    public Material matParedHormigon;       // brushed_concrete
    [Tooltip("Hormigón rayado — apartamentos modernos")]
    public Material matParedHormigonMod;    // brushed_concrete_2

    [Header("═══ TEJADOS ═══")]
    [Tooltip("Pizarra — tejado de pizarra vasca (dominante: 311/1030)")]
    public Material matTejadoPizarra;       // castle_wall_slates
    [Tooltip("Hormigón plano — azotea cemento (dominante: 343/1030)")]
    public Material matTejadoHormigon;      // anti_slip_concrete
    [Tooltip("Hormigón texturizado — azotea moderna")]
    public Material matTejadoHormigonTex;   // brushed_concrete_03
    [Tooltip("Teja cerámica — tejado con teja")]
    public Material matTejadoTeja;          // clay_roof_tiles_02
    [Tooltip("Cerámica gres — cubierta cerámica moderna")]
    public Material matTejadoCeramica;      // ceramic_roof_01
    [Tooltip("Fibrocemento — industrial/nave")]
    public Material matTejadoFibro;         // asbestos_sheet_02

    [Header("═══ ACERAS / SUELO PEATONAL ═══")]
    [Tooltip("Adoquín — Herriko Plaza y calles peatonales")]
    public Material matAceraAdoquin;        // cobblestone_floor_001
    [Tooltip("Adoquín grande — peatonal histórico")]
    public Material matAceraAdoquinGrande;  // cobblestone_large_01
    [Tooltip("Losa de hormigón — acera residencial")]
    public Material matAceraHormigon;       // anti_slip_concrete
    [Tooltip("Baldosa hidráulica — calle comercial")]
    public Material matAceraBaldosa;        // brick_pavement_02
    [Tooltip("Empedrado — calles históricas")]
    public Material matAceraEmpedrado;      // cobblestone_pavement

    [Header("═══ CALZADAS ═══")]
    [Tooltip("Asfalto — carreteras y calles asfaltadas")]
    public Material matCalzadaAsfalto;      // M_Asfalto_Carretera
    [Tooltip("Adoquín — zona histórica / calmado de tráfico")]
    public Material matCalzadaAdoquin;      // cobblestone_01
    [Tooltip("Tierra compactada — caminos")]
    public Material matCalzadaTierra;       // brown_mud_dry

    [Header("═══ BORDILLOS ═══")]
    public Material matBordillo;            // brushed_concrete_03

    [Header("═══ VENTANAS ═══")]
    public Material matVentana;             // vidrio — HDRP/Lit transparente
    public Material matPuerta;              // madera oscura

    // ════════════════════════════════════════════════════════════════════
    //  ESCALAS UV (metros por repetición de tesela)
    // ════════════════════════════════════════════════════════════════════

    static readonly Dictionary<string, float> UV_SCALE = new()
    {
        // Paredes
        ["revoco"]     = 1.5f,   // revoco liso — tesela grande
        ["beis"]       = 1.8f,
        ["arcilla"]    = 1.2f,
        ["piedra"]     = 0.6f,   // sillería — bloques visibles
        ["ladrillo"]   = 0.5f,   // ladrillo — coursing visible
        ["iglesia"]    = 0.55f,
        ["hormigon"]   = 2.5f,   // hormigón — muy liso, tesela grande
        // Tejados
        ["pizarra"]    = 0.35f,  // pizarras pequeñas y compactas
        ["hormigon_t"] = 3.0f,
        ["teja"]       = 0.5f,   // tejas superpuestas
        ["ceramica"]   = 0.6f,
        // Aceras
        ["adoquin"]    = 0.3f,   // adoquines pequeños ~20cm
        ["adoquin_g"]  = 0.5f,
        ["baldosa"]    = 0.4f,
        ["empedrado"]  = 0.25f,
        // Calzada
        ["asfalto"]    = 5.0f,   // asfalto — textura muy grande
        ["cobble"]     = 0.3f,
        // Default
        ["default"]    = 1.0f,
    };

    // ════════════════════════════════════════════════════════════════════
    //  BOOT
    // ════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        AsignarFallbacks();
    }

    // ════════════════════════════════════════════════════════════════════
    //  API PÚBLICA — CONSULTAR MATERIAL
    // ════════════════════════════════════════════════════════════════════

    /// Material para paredes de edificio.
    /// <param name="tipoOSM">type= del edificio</param>
    /// <param name="materialOSM">building:material tag OSM</param>
    /// <param name="colorReal">Color detectado desde ortofoto (puede ser default)</param>
    public Material GetPared(string tipoOSM, string materialOSM = "",
                              Color colorReal = default)
    {
        Material mat = null;

        // 1. Por building:material OSM
        switch ((materialOSM ?? "").ToLower())
        {
            case "concrete": case "reinforced_concrete":
                mat = matParedHormigon;      break;
            case "plaster":  case "render":  case "stucco":
                mat = matParedRevoco;        break;
            case "stone":
                mat = matParedPiedra;        break;
            case "brick":    case "red_brick":
                mat = matParedLadrillo;      break;
            case "sandstone":
                mat = matParedPiedra;        break;
        }

        // 2. Por tipo OSM si no hay material específico
        if (mat == null)
        {
            switch ((tipoOSM ?? "").ToLower())
            {
                case "church":    case "chapel":   case "cathedral":
                    mat = matParedIglesia;        break;
                case "industrial": case "warehouse": case "factory":
                    mat = matParedHormigon;       break;
                case "school":    case "university": case "college":
                    mat = matParedHormigonMod;    break;
                case "house":     case "detached":
                    mat = matParedRevocoBeis;     break;
                case "apartments": case "block":
                    mat = matParedRevoco;         break;
                default:
                    mat = matParedRevoco;         break;
            }
        }

        // 3. Tintado con color real de ortofoto (si diferencia significativa)
        if (colorReal != default && mat != null
            && !ColorEsGenerico(colorReal))
        {
            mat = TintarMaterial(mat, colorReal);
        }

        return mat ?? matParedRevoco ?? FallbackMat(new Color(0.78f, 0.75f, 0.70f));
    }

    /// Material para tejado.
    public Material GetTejado(string roofTipoReal, string roofMaterialOSM = "",
                               Color colorRealTejado = default)
    {
        // 1. Por tipo detectado en ortofoto IGN
        switch ((roofTipoReal ?? "").ToLower())
        {
            case "pizarra_gris":  case "pizarra_negra":
                return TintarSiColorReal(matTejadoPizarra,
                       colorRealTejado, roofTipoReal.Contains("negra")
                           ? new Color(0.25f, 0.25f, 0.27f)
                           : new Color(0.45f, 0.45f, 0.48f));

            case "cemento_gris_claro":
                return TintarSiColorReal(matTejadoHormigon,
                       colorRealTejado, new Color(0.72f, 0.72f, 0.72f));

            case "teja_marron":
                return TintarSiColorReal(matTejadoTeja,
                       colorRealTejado, new Color(0.58f, 0.35f, 0.22f));

            case "cubierta_vegetal":
                return TintarMaterial(
                    matTejadoHormigon ?? FallbackMat(Color.white),
                    new Color(0.32f, 0.52f, 0.25f));

            case "desconocido": case "":
                break;
        }

        // 2. Por roof:material OSM
        switch ((roofMaterialOSM ?? "").ToLower())
        {
            case "slate":       return matTejadoPizarra  ?? FallbackMat(new Color(0.42f,0.42f,0.44f));
            case "concrete":    return matTejadoHormigon ?? FallbackMat(new Color(0.68f,0.68f,0.68f));
            case "tiles":       return matTejadoTeja     ?? FallbackMat(new Color(0.58f,0.35f,0.22f));
            case "asbestos":    return matTejadoFibro    ?? FallbackMat(new Color(0.62f,0.62f,0.62f));
        }

        // 3. Fallback: pizarra (dominante en Basque Country)
        return matTejadoPizarra ?? FallbackMat(new Color(0.42f, 0.42f, 0.44f));
    }

    /// Material para acera/suelo peatonal.
    public Material GetAcera(string tipoVia, string surfaceOSM = "")
    {
        // Por surface OSM
        switch ((surfaceOSM ?? "").ToLower())
        {
            case "cobblestone": case "cobblestone:flattened":
                return matAceraAdoquin    ?? FallbackMat(new Color(0.52f,0.50f,0.48f));
            case "paving_stones":
                return matAceraAdoquinGrande ?? FallbackMat(new Color(0.56f,0.54f,0.50f));
            case "concrete":    case "asphalt":
                return matAceraHormigon   ?? FallbackMat(new Color(0.70f,0.69f,0.66f));
            case "sett":
                return matAceraEmpedrado  ?? FallbackMat(new Color(0.48f,0.46f,0.44f));
        }

        // Por tipo de vía
        switch ((tipoVia ?? "").ToLower())
        {
            case "pedestrian":  case "living_street":
                return matAceraAdoquin    ?? FallbackMat(new Color(0.55f,0.53f,0.50f));
            case "primary":     case "secondary":
                return matAceraBaldosa    ?? FallbackMat(new Color(0.68f,0.67f,0.64f));
            case "residential": case "tertiary":
                return matAceraHormigon   ?? FallbackMat(new Color(0.72f,0.71f,0.68f));
            default:
                return matAceraHormigon   ?? FallbackMat(new Color(0.72f,0.71f,0.68f));
        }
    }

    /// Material para calzada.
    public Material GetCalzada(string tipoVia, string surfaceOSM = "")
    {
        switch ((surfaceOSM ?? "").ToLower())
        {
            case "cobblestone": case "sett": case "paving_stones":
                return matCalzadaAdoquin ?? FallbackMat(new Color(0.42f,0.40f,0.38f));
            case "unpaved":     case "dirt":  case "gravel":
                return matCalzadaTierra  ?? FallbackMat(new Color(0.52f,0.44f,0.34f));
        }

        switch ((tipoVia ?? "").ToLower())
        {
            case "pedestrian":
                return matCalzadaAdoquin ?? FallbackMat(new Color(0.45f,0.43f,0.41f));
            case "track":       case "path":
                return matCalzadaTierra  ?? FallbackMat(new Color(0.52f,0.44f,0.34f));
            default:
                return matCalzadaAsfalto ?? FallbackMat(new Color(0.22f,0.22f,0.23f));
        }
    }

    /// Escala UV en metros por repetición de tesela para un tipo de material.
    public static float GetUVScale(string categoria)
        => UV_SCALE.TryGetValue(categoria, out float s) ? s : 1.0f;

    /// Escala UV inferida del tipo de edificio.
    public static float GetUVScalePared(string tipoOSM, string materialOSM = "")
    {
        switch ((materialOSM ?? "").ToLower())
        {
            case "concrete": case "reinforced_concrete": return GetUVScale("hormigon");
            case "stone": case "sandstone":              return GetUVScale("piedra");
            case "brick": case "red_brick":              return GetUVScale("ladrillo");
            case "plaster": case "stucco":               return GetUVScale("revoco");
        }
        return (tipoOSM ?? "") switch
        {
            "church" or "chapel"               => GetUVScale("ladrillo"),
            "industrial" or "warehouse"        => GetUVScale("hormigon"),
            "house" or "detached"              => GetUVScale("beis"),
            _                                  => GetUVScale("revoco"),
        };
    }

    // ════════════════════════════════════════════════════════════════════
    //  INTERNOS
    // ════════════════════════════════════════════════════════════════════

    static bool ColorEsGenerico(Color c)
        => Mathf.Abs(c.r - c.g) < 0.05f && Mathf.Abs(c.g - c.b) < 0.05f;

    Material TintarSiColorReal(Material base_, Color colorReal, Color defaultColor)
    {
        if (colorReal != default && !ColorEsGenerico(colorReal))
            return TintarMaterial(base_, colorReal);
        return TintarMaterial(base_, defaultColor);
    }

    // Devuelve un material con el color ajustado.
    // Usa caché para no crear new Material() por cada edificio.
    Material TintarMaterial(Material src, Color tint)
    {
        if (src == null) return FallbackMat(tint);
        long key = CacheKey(src.GetInstanceID(), tint);
        if (_matCache.TryGetValue(key, out var cached)) return cached;

        var m = new Material(src);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
        if (m.HasProperty("_Color"))     m.SetColor("_Color",     tint);
        _matCache[key] = m;
        return m;
    }

    static Material FallbackMat(Color c)
    {
        var m = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        m.color = c;
        return m;
    }

    // Empaqueta instanceID + color como clave long (evita boxing de tuple)
    static long CacheKey(int srcId, Color c)
    {
        int cr = Mathf.RoundToInt(c.r * 255);
        int cg = Mathf.RoundToInt(c.g * 255);
        int cb = Mathf.RoundToInt(c.b * 255);
        int colorPacked = (cr << 16) | (cg << 8) | cb;
        return ((long)srcId << 24) | (uint)colorPacked;
    }

    void OnDestroy()
    {
        foreach (var m in _matCache.Values)
            if (m != null) Destroy(m);
        _matCache.Clear();
        if (Instance == this) Instance = null;
    }

    // Rellena referencias nulas con fallbacks de runtime.
    void AsignarFallbacks()
    {
        if (matParedRevoco      == null) matParedRevoco      = FallbackMat(new Color(0.75f,0.73f,0.70f));
        if (matParedRevocoBeis  == null) matParedRevocoBeis  = FallbackMat(new Color(0.82f,0.78f,0.72f));
        if (matParedArcilaPlast == null) matParedArcilaPlast = FallbackMat(new Color(0.70f,0.62f,0.52f));
        if (matParedPiedra      == null) matParedPiedra      = FallbackMat(new Color(0.62f,0.59f,0.54f));
        if (matParedSilleria    == null) matParedSilleria    = FallbackMat(new Color(0.67f,0.63f,0.57f));
        if (matParedLadrillo    == null) matParedLadrillo    = FallbackMat(new Color(0.68f,0.42f,0.32f));
        if (matParedIglesia     == null) matParedIglesia     = FallbackMat(new Color(0.62f,0.55f,0.46f));
        if (matParedHormigon    == null) matParedHormigon    = FallbackMat(new Color(0.62f,0.62f,0.64f));
        if (matParedHormigonMod == null) matParedHormigonMod = FallbackMat(new Color(0.68f,0.68f,0.70f));

        if (matTejadoPizarra    == null) matTejadoPizarra    = FallbackMat(new Color(0.38f,0.38f,0.40f));
        if (matTejadoHormigon   == null) matTejadoHormigon   = FallbackMat(new Color(0.72f,0.72f,0.72f));
        if (matTejadoHormigonTex== null) matTejadoHormigonTex= FallbackMat(new Color(0.68f,0.68f,0.68f));
        if (matTejadoTeja       == null) matTejadoTeja       = FallbackMat(new Color(0.58f,0.35f,0.22f));
        if (matTejadoCeramica   == null) matTejadoCeramica   = FallbackMat(new Color(0.62f,0.38f,0.26f));
        if (matTejadoFibro      == null) matTejadoFibro      = FallbackMat(new Color(0.60f,0.60f,0.62f));

        if (matAceraAdoquin     == null) matAceraAdoquin     = FallbackMat(new Color(0.55f,0.53f,0.50f));
        if (matAceraAdoquinGrande==null) matAceraAdoquinGrande=FallbackMat(new Color(0.58f,0.56f,0.53f));
        if (matAceraHormigon    == null) matAceraHormigon    = FallbackMat(new Color(0.72f,0.71f,0.68f));
        if (matAceraBaldosa     == null) matAceraBaldosa     = FallbackMat(new Color(0.68f,0.66f,0.62f));
        if (matAceraEmpedrado   == null) matAceraEmpedrado   = FallbackMat(new Color(0.48f,0.46f,0.44f));

        if (matCalzadaAsfalto   == null) matCalzadaAsfalto   = FallbackMat(new Color(0.22f,0.22f,0.23f));
        if (matCalzadaAdoquin   == null) matCalzadaAdoquin   = FallbackMat(new Color(0.38f,0.36f,0.34f));
        if (matCalzadaTierra    == null) matCalzadaTierra    = FallbackMat(new Color(0.52f,0.44f,0.34f));

        if (matBordillo         == null) matBordillo         = FallbackMat(new Color(0.68f,0.68f,0.68f));

        if (matVentana          == null)
        {
            matVentana = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
            matVentana.color = new Color(0.55f, 0.65f, 0.75f, 0.35f);
            if (matVentana.HasProperty("_SurfaceType"))
                matVentana.SetFloat("_SurfaceType", 1f); // Transparent
        }
        if (matPuerta           == null) matPuerta           = FallbackMat(new Color(0.22f,0.16f,0.10f));
    }
}
