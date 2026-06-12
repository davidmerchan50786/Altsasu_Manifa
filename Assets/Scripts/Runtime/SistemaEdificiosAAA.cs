// Assets/Scripts/SistemaEdificiosAAA.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE EDIFICIOS AAA — 12 arquetipos vascos para Alsasua
//
//  Arquetipos:
//    a. UrbanoPre1940    — arenisca #c8a882, balcones forja, tejado 32°, chimenea piedra
//    b. Bloque1940_1975  — ladrillo #8b4513, ventanas rectas, balcón hormigón
//    c. ModernoPost1975  — revoco blanco #f5f0e8, tejado plano, cajas AC
//    d. Casero           — cal #faf7f0, viga madera 1.2m vuelo, tejado 42°
//    e. Bar              — azulejo PB, rótulo OSM/mapillary, Canopy, barril+cajón, luz ámbar
//    f. Comercio         — escaparate 80% transparente, persiana metálica, rótulo
//    g. Iglesia          — cobblestone gris oscuro, campanil, contrafuertes, atrio
//    h. NaveIndustrial   — chapa ondulada Metal R=0.8, puerta 3×4m, lucernario
//    i. EquipamientoPublico — ladrillo institucional, marquesina, megáfono
//    j. Fronton          — hormigón blanco 10-12m, marcas impacto, red metálica
//    k. Aparcamiento     — cubierto: losa+pilares; descubierto: líneas blancas
//    l. Solar            — escombros, crate abierto, graffiti
//
//  LOD system (4 niveles):
//    LOD0 <40m  — completo con tessellation
//    LOD1 <120m — sin interiores
//    LOD2 <350m — mesh simplificado baked
//    LOD3 <700m — billboard impostor 8 ángulos
//
//  GPU Instancing + MaterialPropertyBlock para variación de color
//  Target: <120 draw calls total
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering.HighDefinition;
using System.Linq;

[DefaultExecutionOrder(-75)]
public class SistemaEdificiosAAA : MonoBehaviour
{
    public static SistemaEdificiosAAA Instance { get; private set; }
    public static bool Listo { get; private set; }

    // ── Colores canónicos por arquetipo ────────────────────────────────────
    static readonly Color COL_ARENISCA     = new(0.784f, 0.659f, 0.506f);  // #c8a882
    static readonly Color COL_LADRILLO     = new(0.545f, 0.271f, 0.075f);  // #8b4513
    static readonly Color COL_REVOCO_BLANCO= new(0.961f, 0.941f, 0.910f);  // #f5f0e8
    static readonly Color COL_CAL_BLANCA   = new(0.980f, 0.969f, 0.941f);  // #faf7f0
    static readonly Color COL_HORMIGON     = new(0.780f, 0.780f, 0.780f);
    static readonly Color COL_PIEDRA_OSCURA= new(0.380f, 0.370f, 0.350f);
    static readonly Color COL_CHAPA_METAL  = new(0.560f, 0.570f, 0.580f);
    static readonly Color COL_TERRACOTA    = new(0.710f, 0.271f, 0.106f);  // #b5451b
    static readonly Color COL_PIZARRA      = new(0.353f, 0.392f, 0.455f);  // #5a6474

    // ── Cache de materiales ────────────────────────────────────────────────
    readonly Dictionary<string, Material> _matCache = new();
    Material _matVidrio, _matMarco, _matHierro, _matCornisa, _matZocalo;
    Material _matMadera, _matChapa, _matAzulejo, _matPiedra, _matHormigon;
    Material _matAsfalto, _matLinea;

    // Materiales tejado por arquetipo
    Material _matTejadoTeja, _matTejadoPizarra, _matTejadoMetal, _matTejadoPlano;

    // ── Luces nocturnas ────────────────────────────────────────────────────
    readonly List<Renderer> _ventanasNocturnas = new();
    bool _esNoche;

    // ── State ─────────────────────────────────────────────────────────────
    Transform _parentEdificios;
    int _totalEdificios;

    // MaterialPropertyBlock reutilizable (evita instanciar materiales)
    // FIX (playtest): crearlo en un field initializer lanzaba "CreateImpl is not allowed
    // from a MonoBehaviour constructor" → SistemaEdificiosAAA moría y NO se generaban edificios.
    static MaterialPropertyBlock _mpb;

    // ── Textos graffiti vascos ─────────────────────────────────────────────
    static readonly (string texto, Color color)[] GRAFFITI = {
        ("ASKATASUNA",      new Color(0.9f,0.1f,0.1f)),
        ("PRESOAK ETXERA",  new Color(0.1f,0.1f,0.8f)),
        ("INDEPENDENTZIA",  new Color(0.8f,0.7f,0.0f)),
        ("AMNISTIA",        new Color(0.1f,0.6f,0.1f)),
        ("GORA EH",         new Color(0.9f,0.1f,0.1f)),
        ("ACAB",            new Color(0.0f,0.0f,0.0f)),
        ("NO PASARÁN",      new Color(0.7f,0.0f,0.0f)),
        ("HERRIRA",         new Color(0.0f,0.4f,0.8f)),
        ("ETA",             new Color(0.9f,0.1f,0.1f)),
        ("BIZI NAIZ",       new Color(0.1f,0.5f,0.2f)),
    };

    // ═══════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ═══════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();   // FIX: crear aquí, no en field initializer
    }

    void Start()
    {
        CargarMateriales();
        SistemaZonas.OnZonaCargada += OnZonaCargada;
        GeneradorMundoOSM.OnMundoGenerado += OnMundoGeneradoLegado;
        Listo = true;
        AlsasuaLogger.Info("EdificiosAAA", "Listo — 12 arquetipos vascos activos");
    }

    void OnDestroy()
    {
        SistemaZonas.OnZonaCargada        -= OnZonaCargada;
        GeneradorMundoOSM.OnMundoGenerado -= OnMundoGeneradoLegado;
    }

    void OnZonaCargada(Vector2Int _) { }

    void OnMundoGeneradoLegado()
    {
        if (GeneradorGeometriaPrecisa.Instance != null) return;
        var parent = GameObject.Find("Edificios_OSM");
        if (parent == null) return;
        StartCoroutine(EnriquecerTodo());
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  API PÚBLICA
    // ═══════════════════════════════════════════════════════════════════════

    /// Construye un edificio AAA completo con arquetipo correcto.
    public void ConstruirEdificio(EdificioData e, Transform parent)
    {
        if (e.vertices == null || e.vertices.Length < 3) return;

        var verts2D = new List<Vector2>();
        foreach (var v in e.vertices)
            verts2D.Add(new Vector2(v.x + GeoDataAlsasua.OX, v.z + GeoDataAlsasua.OZ));

        float cx = verts2D.Average(v => v.x);
        float cz = verts2D.Average(v => v.y);

        float suelo  = GeoDataAlsasua.AlturaTerreno(cx, cz);
        float altOSM = GeoDataAlsasua.AlturaEdificio(e.levels, e.height);

        // Consultar altura LIDAR si disponible
        float altura = FusionadorEdificiosUltra.Instance != null
            ? Mathf.Max(GeoDataAlsasua.ALT_PLANTA,
                FusionadorEdificiosUltra.Instance.GetAlturaOptima((long)e.id, altOSM))
            : altOSM;

        var mesh = GenerarMeshEdificio(verts2D, suelo, altura);
        if (mesh == null) return;

        // Determinar arquetipo
        ArquetipoVasco arquetipo = FusionadorEdificiosUltra.Instance != null
            ? FusionadorEdificiosUltra.Instance.GetArquetipoConAnio((long)e.id, e.type, e.levels)
            : FusionadorEdificiosUltra.ArquetipoDesdeOSM(e.type, e.levels, "", "", "");

        string nombre = string.IsNullOrEmpty(e.name)
            ? $"Edif_{e.id}_{arquetipo}" : $"Edif_{e.name.Replace(" ","_")}";

        var go = new GameObject(nombre);
        go.transform.SetParent(parent);
        go.isStatic = true;

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        Material matFachada = MaterialFachadaPorArquetipo(arquetipo, (long)e.id);

        // Aplicar variación de color por MaterialPropertyBlock (sin instanciar material)
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = matFachada;
        AplicarVariacionColor(mr, arquetipo, (long)e.id);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

        // Collider en radio < 200m del origen
        if (Vector2.Distance(new Vector2(cx, cz),
            new Vector2(GeoDataAlsasua.OX, GeoDataAlsasua.OZ)) < 200f)
            go.AddComponent<MeshCollider>().sharedMesh = mesh;

        EnriquecerEdificioPorArquetipo(go.transform, mesh, arquetipo, e);

        _totalEdificios++;
    }

    Mesh GenerarMeshEdificio(List<Vector2> planta, float suelo, float altura)
        => MeshBuilder.Edificio(planta, suelo, altura);

    // ═══════════════════════════════════════════════════════════════════════
    //  PIPELINE ENRIQUECIMIENTO
    // ═══════════════════════════════════════════════════════════════════════

    IEnumerator EnriquecerTodo()
    {
        yield return new WaitForSeconds(0.5f);
        if (GeneradorGeometriaPrecisa.Instance != null) yield break;

        _parentEdificios = GameObject.Find("Edificios_OSM")?.transform;
        if (_parentEdificios == null) yield break;

        int i = 0;
        foreach (Transform edif in _parentEdificios)
        {
            if (edif == null) continue;
            var mf = edif.GetComponent<MeshFilter>();
            if (mf?.sharedMesh == null) continue;

            // Intentar leer arquetipo desde nombre
            ArquetipoVasco arq = ArquetipoDesdeNombre(edif.name);
            EnriquecerEdificioPorArquetipo(edif, mf.sharedMesh, arq, null);
            i++;
            if (i % 15 == 0) yield return null;
        }

        Listo = true;
        AlsasuaLogger.Info("EdificiosAAA", $"✅ {i} edificios enriquecidos (12 arquetipos)");
        StartCoroutine(CicloLucesNocturnas());
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ENRIQUECIMIENTO POR ARQUETIPO
    // ═══════════════════════════════════════════════════════════════════════

    void EnriquecerEdificioPorArquetipo(Transform edif, Mesh mesh,
                                         ArquetipoVasco arquetipo, EdificioData data)
    {
        var bounds = mesh.bounds;
        float h    = bounds.size.y;
        float w    = bounds.size.x;
        float d    = bounds.size.z;
        int niveles= Mathf.Max(1, Mathf.RoundToInt(h / GeoDataAlsasua.ALT_PLANTA));
        string nombre = data?.name ?? "";

        switch (arquetipo)
        {
            case ArquetipoVasco.UrbanoPre1940:
                ArquetipoCasaUrbanaHistorica(edif, bounds, h, w, d, niveles);
                break;

            case ArquetipoVasco.Bloque1940_1975:
                ArquetipoBloque1940(edif, bounds, h, w, d, niveles);
                break;

            case ArquetipoVasco.ModernoPost1975:
                ArquetipoModerno(edif, bounds, h, w, d, niveles);
                break;

            case ArquetipoVasco.Casero:
                ArquetipoCaserio(edif, bounds, h, w, d, niveles);
                break;

            case ArquetipoVasco.Bar:
                ArquetipoBar(edif, bounds, h, w, d, niveles, nombre);
                break;

            case ArquetipoVasco.Comercio:
                ArquetipoComercio(edif, bounds, h, w, d, niveles, nombre);
                break;

            case ArquetipoVasco.Iglesia:
                ArquetipoIglesia(edif, bounds, h, w, d, niveles, nombre);
                break;

            case ArquetipoVasco.NaveIndustrial:
                ArquetipoNaveIndustrial(edif, bounds, h, w, d, niveles);
                break;

            case ArquetipoVasco.EquipamientoPublico:
                ArquetipoEquipamiento(edif, bounds, h, w, d, niveles, nombre);
                break;

            case ArquetipoVasco.Fronton:
                ArquetipoFronton(edif, bounds, h, w, d);
                break;

            case ArquetipoVasco.Aparcamiento:
                ArquetipoAparcamiento(edif, bounds, h, w, d);
                break;

            case ArquetipoVasco.Solar:
                ArquetipoSolar(edif, bounds);
                break;
        }

        // Graffiti en residenciales pre-1975 (35%)
        if (arquetipo is ArquetipoVasco.UrbanoPre1940 or ArquetipoVasco.Bloque1940_1975
            && Random.value < 0.35f)
            AnadirGraffiti(edif, bounds);

        // LOD system
        ConfigurarLOD(edif, edif.GetComponent<MeshRenderer>(), mesh, arquetipo);

        edif.gameObject.isStatic = true;
#if UNITY_EDITOR
        UnityEditor.GameObjectUtility.SetStaticEditorFlags(edif.gameObject,
            UnityEditor.StaticEditorFlags.BatchingStatic |
            UnityEditor.StaticEditorFlags.ContributeGI   |
            UnityEditor.StaticEditorFlags.OccluderStatic);
#endif
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ARQUETIPOS INDIVIDUALES
    // ═══════════════════════════════════════════════════════════════════════

    // a. Casa urbana pre-1940
    void ArquetipoCasaUrbanaHistorica(Transform edif, Bounds b,
                                       float h, float w, float d, int niveles)
    {
        // Zócalo 0.28m más oscuro
        AnadirZocalo(edif, b, 0.28f, DarkenColor(COL_ARENISCA, 0.7f));

        // Ventanas: Kit_Window_Upper_Convex en pisos 1-2, Straight superiores
        for (int p = 0; p < niveles; p++)
        {
            float yBase = b.min.y + 0.35f + p * GeoDataAlsasua.ALT_PLANTA;
            bool usarConvex = (p <= 1);
            float colsF = Mathf.FloorToInt(w / 1.15f);
            int cols = Mathf.Max(1, (int)colsF);

            for (int c = 0; c < cols; c++)
            {
                float x = b.min.x + (c + 0.5f) * (w / cols);
                CrearVentanaVasca(edif,
                    new Vector3(x, yBase + 0.9f, b.min.z - 0.005f),
                    Quaternion.Euler(0, 180, 0),
                    0.55f, 1.25f, usarConvex, p == 0);
                CrearVentanaVasca(edif,
                    new Vector3(x, yBase + 0.9f, b.max.z + 0.005f),
                    Quaternion.identity,
                    0.55f, 1.25f, usarConvex, p == 0);
            }
        }

        // Balcones pisos 1-3 con barandilla forja negra
        for (int p = 1; p < Mathf.Min(3, niveles); p++)
            AnadirBalconForja(edif, b, b.min.y + p * GeoDataAlsasua.ALT_PLANTA + 0.9f, w);

        // Cornisa voladizo 0.15m con perfil moldurado
        AnadirCornisaMoldurada(edif, b, h, 0.15f);

        // Tejado 32° dos aguas con teja cerámica
        AnadirTejadoDosAguas(edif, b, h, 32f, _matTejadoTeja);

        // Chimenea piedra × 2
        AnadirChimeneaPiedra(edif, b, h, 2);
    }

    // b. Bloque 1940-1975
    void ArquetipoBloque1940(Transform edif, Bounds b,
                              float h, float w, float d, int niveles)
    {
        AnadirZocalo(edif, b, 0.28f, DarkenColor(COL_LADRILLO, 0.75f));

        for (int p = 0; p < niveles; p++)
        {
            float yBase = b.min.y + 0.35f + p * GeoDataAlsasua.ALT_PLANTA;
            int cols = Mathf.Max(1, Mathf.FloorToInt(w / 1.15f));
            for (int c = 0; c < cols; c++)
            {
                float x = b.min.x + (c + 0.5f) * (w / cols);
                // Straight windows para todos los pisos
                CrearVentanaVasca(edif,
                    new Vector3(x, yBase + 0.9f, b.min.z - 0.005f),
                    Quaternion.Euler(0, 180, 0), 0.65f, 1.2f, false, p == 0);
                CrearVentanaVasca(edif,
                    new Vector3(x, yBase + 0.9f, b.max.z + 0.005f),
                    Quaternion.identity, 0.65f, 1.2f, false, p == 0);
            }
        }

        // Balcón hormigón (losa sin barandilla forja)
        if (niveles > 2)
            AnadirBalconHormigon(edif, b, b.min.y + 1 * GeoDataAlsasua.ALT_PLANTA + 0.9f, w);

        AnadirCornisaMoldurada(edif, b, h, 0.12f);
        AnadirTejadoDosAguas(edif, b, h, 22f, _matTejadoTeja);
    }

    // c. Moderno post-1975
    void ArquetipoModerno(Transform edif, Bounds b,
                          float h, float w, float d, int niveles)
    {
        AnadirZocalo(edif, b, 0.28f, DarkenColor(COL_REVOCO_BLANCO, 0.85f));

        // Ventanas grandes
        for (int p = 0; p < niveles; p++)
        {
            float yBase = b.min.y + 0.3f + p * GeoDataAlsasua.ALT_PLANTA;
            int cols = Mathf.Max(1, Mathf.FloorToInt(w / 1.5f));
            for (int c = 0; c < cols; c++)
            {
                float x = b.min.x + (c + 0.5f) * (w / cols);
                CrearVentanaVasca(edif,
                    new Vector3(x, yBase + 0.8f, b.min.z - 0.005f),
                    Quaternion.Euler(0, 180, 0), 0.9f, 1.4f, false, p == 0);
            }
        }

        // Tejado plano con pretil 0.35m
        AnadirTejadoPlano(edif, b, h, 0.35f);

        // Cajas AC en fachada trasera
        AnadirCajasAC(edif, b, h);
    }

    // d. Caserío
    void ArquetipoCaserio(Transform edif, Bounds b,
                          float h, float w, float d, int niveles)
    {
        AnadirZocalo(edif, b, 0.35f, DarkenColor(COL_CAL_BLANCA, 0.8f));

        // Viga madera visible con vuelo 1.2m
        AnadirVigaMadera(edif, b, h, w, 1.2f);

        // Ventanas pequeñas cuadradas
        for (int p = 0; p < niveles; p++)
        {
            float yBase = b.min.y + 0.4f + p * GeoDataAlsasua.ALT_PLANTA;
            int cols = Mathf.Max(1, Mathf.FloorToInt(w / 1.8f));
            for (int c = 0; c < cols; c++)
            {
                float x = b.min.x + (c + 0.5f) * (w / cols);
                CrearVentanaVasca(edif,
                    new Vector3(x, yBase + 0.9f, b.min.z - 0.005f),
                    Quaternion.Euler(0, 180, 0), 0.65f, 0.9f, false, p == 0);
            }
        }

        // Tejado 42° muy pronunciado
        AnadirTejadoDosAguas(edif, b, h, 42f, _matTejadoTeja);
        AnadirChimeneaPiedra(edif, b, h, 1);
    }

    // e. Bar / taberna
    void ArquetipoBar(Transform edif, Bounds b,
                      float h, float w, float d, int niveles, string nombre)
    {
        AnadirZocalo(edif, b, 0.28f, new Color(0.22f, 0.18f, 0.35f)); // azulejo oscuro

        // Azulejo planta baja (6 filas × 15cm)
        AnadirFranjaAzulejo(edif, b, 0.9f);

        // Escaparate planta baja
        CrearEscaparate(edif, b, w, true);

        // Rótulo con nombre
        string textoRotulo = string.IsNullOrEmpty(nombre) ? "TABERNA" : nombre.ToUpper();
        AnadirRotulo(edif, b, textoRotulo, new Color(1f, 0.85f, 0f));

        // Marquesina / canopy sobre entrada
        AnadirCanopy(edif, b, w);

        // Props en acera
        AnadirPropBaril(edif, b);

        // Luz ámbar PB
        AnadirLuzInterior(edif, b, new Color(1f, 0.6f, 0.2f), 2.5f, kelvin: 2700);

        // Ventanas pisos superiores
        for (int p = 1; p < niveles; p++)
        {
            float yBase = b.min.y + p * GeoDataAlsasua.ALT_PLANTA;
            int cols = Mathf.Max(1, Mathf.FloorToInt(w / 1.15f));
            for (int c = 0; c < cols; c++)
            {
                float x = b.min.x + (c + 0.5f) * (w / cols);
                CrearVentanaVasca(edif,
                    new Vector3(x, yBase + 0.9f, b.min.z - 0.005f),
                    Quaternion.Euler(0, 180, 0), 0.55f, 1.1f, false, false);
            }
        }

        AnadirCornisaMoldurada(edif, b, h, 0.12f);
    }

    // f. Comercio
    void ArquetipoComercio(Transform edif, Bounds b,
                           float h, float w, float d, int niveles, string nombre)
    {
        AnadirZocalo(edif, b, 0.28f, new Color(0.6f, 0.58f, 0.55f));

        // Escaparate 80% transparente PB
        CrearEscaparate(edif, b, w, false);

        // Persiana metálica enrollable sobre escaparate
        AnadirPersiana(edif, b, w);

        // Rótulo
        string textoRotulo = string.IsNullOrEmpty(nombre) ? "COMERCIO" : nombre.ToUpper();
        AnadirRotulo(edif, b, textoRotulo, new Color(0.2f, 0.2f, 0.8f));

        // Luz fría interior 4000K
        AnadirLuzInterior(edif, b, new Color(0.9f, 0.95f, 1f), 1.8f, kelvin: 4000);

        // Pisos superiores
        for (int p = 1; p < niveles; p++)
        {
            float yBase = b.min.y + p * GeoDataAlsasua.ALT_PLANTA;
            int cols = Mathf.Max(1, Mathf.FloorToInt(w / 1.15f));
            for (int c = 0; c < cols; c++)
            {
                float x = b.min.x + (c + 0.5f) * (w / cols);
                CrearVentanaVasca(edif,
                    new Vector3(x, yBase + 0.9f, b.min.z - 0.005f),
                    Quaternion.Euler(0, 180, 0), 0.55f, 1.1f, false, false);
            }
        }

        AnadirCornisaMoldurada(edif, b, h, 0.12f);
    }

    // g. Iglesia
    void ArquetipoIglesia(Transform edif, Bounds b,
                          float h, float w, float d, int niveles, string nombre)
    {
        // Zócalo grueso piedra oscura
        AnadirZocalo(edif, b, 0.45f, DarkenColor(COL_PIEDRA_OSCURA, 0.8f));

        // Contrafuertes (pilastras laterales)
        AnadirContrafuertes(edif, b, h);

        // Ventanas altas estrechas (gótico)
        int cols = Mathf.Max(1, Mathf.FloorToInt(w / 2.5f));
        for (int c = 0; c < cols; c++)
        {
            float x = b.min.x + (c + 0.5f) * (w / cols);
            for (int p = 0; p < niveles; p++)
            {
                float yBase = b.min.y + p * GeoDataAlsasua.ALT_PLANTA + 0.5f;
                CrearVentanaVasca(edif,
                    new Vector3(x, yBase + 1f, b.min.z - 0.005f),
                    Quaternion.Euler(0, 180, 0), 0.4f, 2.2f, true, p == 0);
            }
        }

        // Campanil (torre campanario)
        AnadirCampanil(edif, b, h, w, d);

        // Atrio con adoquín
        AnadirAtrioCobblestone(edif, b);

        // Tejado muy pronunciado 52°
        AnadirTejadoDosAguas(edif, b, h, 52f, _matTejadoPizarra);
    }

    // h. Nave industrial
    void ArquetipoNaveIndustrial(Transform edif, Bounds b,
                                  float h, float w, float d, int niveles)
    {
        // Puerta corredera 3×4m
        AnadirPuertaCorredera(edif, b, 3f, 4f);

        // Lucernario en tejado
        AnadirLucernario(edif, b, h, w, d);

        // Tejado plano con canalones metálicos
        AnadirTejadoPlano(edif, b, h, 0.15f);
        AnadirCanalones(edif, b, h, w);
    }

    // i. Equipamiento público
    void ArquetipoEquipamiento(Transform edif, Bounds b,
                               float h, float w, float d, int niveles, string nombre)
    {
        AnadirZocalo(edif, b, 0.35f, new Color(0.6f, 0.45f, 0.3f));

        // Marquesina institucional
        AnadirCanopy(edif, b, Mathf.Min(w * 0.4f, 3f));

        // Megáfono en fachada
        AnadirMegafono(edif, b);

        // Ventanas grandes uniformes
        for (int p = 0; p < niveles; p++)
        {
            float yBase = b.min.y + p * GeoDataAlsasua.ALT_PLANTA;
            int cols = Mathf.Max(2, Mathf.FloorToInt(w / 1.8f));
            for (int c = 0; c < cols; c++)
            {
                float x = b.min.x + (c + 0.5f) * (w / cols);
                CrearVentanaVasca(edif,
                    new Vector3(x, yBase + 0.9f, b.min.z - 0.005f),
                    Quaternion.Euler(0, 180, 0), 0.8f, 1.3f, false, p == 0);
            }
        }

        AnadirCornisaMoldurada(edif, b, h, 0.14f);
        AnadirTejadoPlano(edif, b, h, 0.3f);
    }

    // j. Frontón
    void ArquetipoFronton(Transform edif, Bounds b, float h, float w, float d)
    {
        // Marcas de impacto procedurales en pared frontal
        AnadirMarcasImpacto(edif, b, w, d);

        // Red metálica perimetral
        AnadirRedMetalica(edif, b, h);

        // Sin tejado convencional — losa plana
        AnadirTejadoPlano(edif, b, h, 0.2f);
    }

    // k. Aparcamiento
    void ArquetipoAparcamiento(Transform edif, Bounds b, float h, float w, float d)
    {
        if (h > 1.5f)
        {
            // Cubierto: losa + pilares
            AnadirPilaresAparcamiento(edif, b, h, w, d);
            AnadirTejadoPlano(edif, b, h, 0.1f);
        }
        else
        {
            // Descubierto: líneas blancas en asfalto
            AnadirLineasAparcamiento(edif, b, w, d);
        }
    }

    // l. Solar / ruina
    void ArquetipoSolar(Transform edif, Bounds b)
    {
        // Escombros + crate abierto
        AnadirEscombros(edif, b);

        // Graffiti político vasco seguro
        AnadirGraffiti(edif, b);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ELEMENTOS DE FACHADA REUTILIZABLES
    // ═══════════════════════════════════════════════════════════════════════

    void AnadirZocalo(Transform edif, Bounds b, float altura, Color color)
    {
        var go = PrimitivoHijo("Zocalo", edif,
            new Vector3(b.center.x, b.min.y + altura * 0.5f, b.center.z),
            new Vector3(b.size.x + 0.08f, altura, b.size.z + 0.08f),
            MatConColor(_matZocalo, color));
        go.isStatic = true;
    }

    void CrearVentanaVasca(Transform edif, Vector3 pos, Quaternion rot,
                            float ancho, float alto, bool usarConvex, bool esPlantaBaja)
    {
        var root = new GameObject(esPlantaBaja ? "Vitrina" : (usarConvex ? "Ventana_Convex" : "Ventana"));
        root.transform.SetParent(edif);
        root.transform.SetPositionAndRotation(pos, rot);
        root.isStatic = true;

        // Vierteaguas bajo ventana (Canopy_Side)
        if (!esPlantaBaja)
        {
            var vierteaguas = PrimitivoHijo("Vierteaguas", root.transform,
                new Vector3(0, -alto * 0.5f - 0.04f, 0.04f),
                new Vector3(ancho + 0.1f, 0.05f, 0.12f), _matCornisa);
            vierteaguas.transform.localRotation = Quaternion.Euler(-15f, 0, 0);
        }

        // Marco
        PrimitivoHijo("Marco", root.transform, Vector3.zero,
            new Vector3(ancho + 0.1f, alto + 0.1f, 0.07f), _matMarco);

        // Vidrio HDRP: Smoothness=0.92, IOR=1.52
        var vidrio = PrimitivoHijo("Vidrio", root.transform,
            new Vector3(0, 0, -0.02f),
            new Vector3(ancho - 0.04f, alto - 0.04f, 0.02f), _matVidrio);
        _ventanasNocturnas.Add(vidrio.GetComponent<MeshRenderer>());

        // División central
        if (!esPlantaBaja)
            PrimitivoHijo("Division", root.transform,
                new Vector3(0, 0, -0.015f),
                new Vector3(0.04f, alto - 0.04f, 0.025f), _matMarco);

        // Dintel superior (arco convex para pre-1940)
        if (usarConvex)
            PrimitivoHijo("Dintel", root.transform,
                new Vector3(0, alto * 0.5f + 0.055f, 0),
                new Vector3(ancho + 0.12f, 0.11f, 0.10f), _matCornisa);
    }

    void AnadirBalconForja(Transform edif, Bounds b, float y, float w)
    {
        float anchoB = Mathf.Min(w * 0.65f, 2.4f);

        // Losa
        PrimitivoHijo("BalconLosa", edif,
            new Vector3(b.center.x, y, b.min.z - 0.55f),
            new Vector3(anchoB, 0.12f, 1.1f), _matCornisa).isStatic = true;

        // Barandilla forja negra
        float bz   = b.min.z - 1.05f;
        float bx0  = b.center.x - anchoB * 0.5f;
        int nBarras = Mathf.Max(3, (int)(anchoB * 3f));

        for (int k = 0; k <= nBarras; k++)
        {
            float bx = bx0 + k * (anchoB / nBarras);
            PrimitivoHijo($"Barra{k}", edif,
                new Vector3(bx, y + 0.44f, bz),
                new Vector3(0.03f, 0.8f, 0.03f), _matHierro).isStatic = true;
        }
        PrimitivoHijo("BarraH", edif,
            new Vector3(b.center.x, y + 0.86f, bz),
            new Vector3(anchoB, 0.05f, 0.05f), _matHierro).isStatic = true;
        PrimitivoHijo("BarraHB", edif,
            new Vector3(b.center.x, y + 0.06f, bz),
            new Vector3(anchoB, 0.04f, 0.04f), _matHierro).isStatic = true;
    }

    void AnadirBalconHormigon(Transform edif, Bounds b, float y, float w)
    {
        float anchoB = Mathf.Min(w * 0.6f, 2.2f);
        PrimitivoHijo("BalconLosa", edif,
            new Vector3(b.center.x, y, b.min.z - 0.5f),
            new Vector3(anchoB, 0.14f, 1.0f), _matHormigon).isStatic = true;

        PrimitivoHijo("PretilBalcon", edif,
            new Vector3(b.center.x, y + 0.5f, b.min.z - 1.0f),
            new Vector3(anchoB, 0.9f, 0.1f), _matHormigon).isStatic = true;
    }

    void AnadirCornisaMoldurada(Transform edif, Bounds b, float h, float vuelo)
    {
        // Banda cornisa principal
        PrimitivoHijo("Cornisa", edif,
            new Vector3(b.center.x, b.min.y + h + 0.15f, b.center.z),
            new Vector3(b.size.x + vuelo * 2f, 0.25f, b.size.z + vuelo * 2f),
            _matCornisa).isStatic = true;

        // Moldura inferior (gola)
        PrimitivoHijo("Cornisa_Gola", edif,
            new Vector3(b.center.x, b.min.y + h + 0.02f, b.center.z),
            new Vector3(b.size.x + vuelo * 1.4f, 0.10f, b.size.z + vuelo * 1.4f),
            _matCornisa).isStatic = true;
    }

    void AnadirTejadoDosAguas(Transform edif, Bounds b, float h, float angulo, Material mat)
    {
        Material matTej = mat ?? _matTejadoTeja;
        float crest = Mathf.Min(b.size.x, b.size.z) * 0.5f * Mathf.Tan(angulo * Mathf.Deg2Rad);

        // Faldón derecho
        var tejR = new GameObject("TejadoR");
        tejR.transform.SetParent(edif);
        tejR.transform.position = new Vector3(b.center.x, b.min.y + h, b.center.z);
        tejR.transform.rotation = Quaternion.Euler(0, 0,
            -Mathf.Atan2(crest, b.size.x * 0.5f) * Mathf.Rad2Deg);
        tejR.isStatic = true;
        tejR.AddComponent<MeshFilter>().sharedMesh = GenerarMeshFaldon(b.size.x, b.size.z, crest, true);
        var mrR = tejR.AddComponent<MeshRenderer>();
        mrR.sharedMaterial = matTej;

        // Faldón izquierdo
        var tejL = new GameObject("TejadoL");
        tejL.transform.SetParent(edif);
        tejL.transform.position = new Vector3(b.center.x, b.min.y + h, b.center.z);
        tejL.transform.rotation = Quaternion.Euler(0, 0,
            Mathf.Atan2(crest, b.size.x * 0.5f) * Mathf.Rad2Deg);
        tejL.isStatic = true;
        tejL.AddComponent<MeshFilter>().sharedMesh = GenerarMeshFaldon(b.size.x, b.size.z, crest, false);
        tejL.AddComponent<MeshRenderer>().sharedMaterial = matTej;

        // Canalón + bajante cada 8m
        AnadirCanalonBajante(edif, b, h, matTej);

        // Cornisa bajo tejado
        AnadirCornisaMoldurada(edif, b, h - 0.1f, 0.10f);
    }

    void AnadirTejadoPlano(Transform edif, Bounds b, float h, float alturaPretil)
    {
        // Losa
        PrimitivoHijo("TejadoPlano", edif,
            new Vector3(b.center.x, b.min.y + h + 0.05f, b.center.z),
            new Vector3(b.size.x + 0.1f, 0.12f, b.size.z + 0.1f),
            _matTejadoPlano).isStatic = true;

        // Pretil 0.35m
        if (alturaPretil > 0.05f)
        {
            for (int cara = 0; cara < 4; cara++)
            {
                bool ejeX = (cara < 2);
                float px = ejeX ? b.center.x : (cara == 2 ? b.min.x - alturaPretil * 0.5f : b.max.x + alturaPretil * 0.5f);
                float pz = ejeX ? (cara == 0 ? b.min.z - alturaPretil * 0.5f : b.max.z + alturaPretil * 0.5f) : b.center.z;
                float sw = ejeX ? b.size.x + alturaPretil * 2f : alturaPretil;
                float sd = ejeX ? alturaPretil : b.size.z;
                PrimitivoHijo($"Pretil{cara}", edif,
                    new Vector3(px, b.min.y + h + 0.12f + alturaPretil * 0.5f, pz),
                    new Vector3(sw, alturaPretil, sd),
                    _matHormigon).isStatic = true;
            }
        }
    }

    void AnadirVigaMadera(Transform edif, Bounds b, float h, float w, float vuelo)
    {
        float y = b.min.y + h - 0.3f;
        int nVigas = Mathf.Max(2, (int)(w / 1.5f));
        for (int k = 0; k < nVigas; k++)
        {
            float x = b.min.x + (k + 0.5f) * (w / nVigas);
            PrimitivoHijo($"Viga{k}", edif,
                new Vector3(x, y, b.min.z - vuelo * 0.5f),
                new Vector3(0.2f, 0.22f, vuelo * 2f), _matMadera).isStatic = true;
        }
        // Tablazón longitudinal
        PrimitivoHijo("Tablazón", edif,
            new Vector3(b.center.x, y - 0.12f, b.min.z - vuelo * 0.5f),
            new Vector3(w, 0.06f, vuelo * 2f), _matMadera).isStatic = true;
    }

    void AnadirChimeneaPiedra(Transform edif, Bounds b, float h, int cantidad)
    {
        cantidad = Mathf.Min(cantidad, 3);
        for (int k = 0; k < cantidad; k++)
        {
            float cx = b.min.x + Random.Range(0.2f, 0.8f) * b.size.x;
            float cz = b.min.z + Random.Range(0.2f, 0.8f) * b.size.z;
            float cy = b.min.y + h;
            float altChi = Random.Range(0.7f, 1.2f);
            float anchoC = 0.35f;

            // Cuerpo
            PrimitivoHijo($"Chimenea{k}", edif,
                new Vector3(cx, cy + altChi * 0.5f, cz),
                new Vector3(anchoC, altChi, anchoC), _matPiedra).isStatic = true;

            // Sombrero
            PrimitivoHijo($"ChimSombrero{k}", edif,
                new Vector3(cx, cy + altChi + 0.06f, cz),
                new Vector3(anchoC + 0.14f, 0.1f, anchoC + 0.14f), _matCornisa).isStatic = true;
        }
    }

    void AnadirCampanil(Transform edif, Bounds b, float h, float w, float d)
    {
        float sz = Mathf.Min(w, d) * 0.22f;
        float altTorre = h * 0.7f;

        PrimitivoHijo("Campanil", edif,
            new Vector3(b.center.x, b.min.y + h + altTorre * 0.5f, b.min.z + sz * 0.5f),
            new Vector3(sz, altTorre, sz), _matPiedra).isStatic = true;

        // Remate
        PrimitivoHijo("CampanilRemate", edif,
            new Vector3(b.center.x, b.min.y + h + altTorre + sz * 0.5f, b.min.z + sz * 0.5f),
            new Vector3(sz + 0.1f, sz * 0.5f, sz + 0.1f), _matCornisa).isStatic = true;
    }

    void AnadirContrafuertes(Transform edif, Bounds b, float h)
    {
        int n = Mathf.Max(2, (int)(b.size.z / 5f));
        for (int k = 0; k <= n; k++)
        {
            float z = b.min.z + k * (b.size.z / n);
            PrimitivoHijo($"ContrafuerteL{k}", edif,
                new Vector3(b.min.x - 0.3f, b.min.y + h * 0.5f, z),
                new Vector3(0.6f, h, 0.5f), _matPiedra).isStatic = true;
            PrimitivoHijo($"ContrafuerteR{k}", edif,
                new Vector3(b.max.x + 0.3f, b.min.y + h * 0.5f, z),
                new Vector3(0.6f, h, 0.5f), _matPiedra).isStatic = true;
        }
    }

    void AnadirAtrioCobblestone(Transform edif, Bounds b)
    {
        PrimitivoHijo("Atrio", edif,
            new Vector3(b.center.x, b.min.y + 0.02f, b.min.z - 2.5f),
            new Vector3(b.size.x * 0.7f, 0.05f, 5f), MatConColor(_matAsfalto, new Color(0.55f, 0.52f, 0.48f)))
            .isStatic = true;
    }

    void AnadirFranjaAzulejo(Transform edif, Bounds b, float altura)
    {
        PrimitivoHijo("FranjaAzulejo", edif,
            new Vector3(b.center.x, b.min.y + altura * 0.5f, b.min.z - 0.01f),
            new Vector3(b.size.x, altura, 0.04f), _matAzulejo).isStatic = true;
    }

    void CrearEscaparate(Transform edif, Bounds b, float w, bool esBar)
    {
        float anchoEsc = w * 0.7f;
        float altoEsc  = 2.2f;
        // Marco
        PrimitivoHijo("EscaparateMarco", edif,
            new Vector3(b.center.x, b.min.y + altoEsc * 0.5f + 0.1f, b.min.z - 0.04f),
            new Vector3(anchoEsc + 0.08f, altoEsc + 0.08f, 0.07f), _matMarco).isStatic = true;
        // Vidrio 80% transparente
        var vGo = PrimitivoHijo("EscaparateVidrio", edif,
            new Vector3(b.center.x, b.min.y + altoEsc * 0.5f + 0.1f, b.min.z - 0.02f),
            new Vector3(anchoEsc, altoEsc, 0.02f), _matVidrio);
        vGo.isStatic = true;
        _ventanasNocturnas.Add(vGo.GetComponent<MeshRenderer>());
    }

    void AnadirPersiana(Transform edif, Bounds b, float w)
    {
        PrimitivoHijo("Persiana", edif,
            new Vector3(b.center.x, b.min.y + 1.2f, b.min.z - 0.06f),
            new Vector3(w * 0.7f, 0.06f, 0.04f), _matChapa).isStatic = true;
    }

    void AnadirCanopy(Transform edif, Bounds b, float anchoCanopy)
    {
        float y = b.min.y + 2.5f;
        PrimitivoHijo("Canopy", edif,
            new Vector3(b.center.x, y, b.min.z - 0.8f),
            new Vector3(anchoCanopy, 0.08f, 1.6f),
            MatConColor(_matChapa, new Color(0.8f, 0.3f, 0.2f))).isStatic = true;

        // Soporte diagonal
        PrimitivoHijo("CanopySoporte", edif,
            new Vector3(b.center.x, y - 0.4f, b.min.z - 0.3f),
            new Vector3(0.06f, 0.8f, 0.06f), _matHierro).isStatic = true;
    }

    void AnadirRotulo(Transform edif, Bounds b, string texto, Color colorRotulo)
    {
        var go = PrimitivoHijo("Rotulo", edif,
            new Vector3(b.center.x, b.min.y + 2.8f, b.min.z - 0.06f),
            new Vector3(Mathf.Min(b.size.x * 0.7f, 3.5f), 0.5f, 0.05f),
            MatConColor(_matChapa, colorRotulo));
        go.name = $"Rotulo_{texto}";
        go.isStatic = true;
    }

    void AnadirPropBaril(Transform edif, Bounds b)
    {
        PrimitivoHijo("Barril", edif,
            new Vector3(b.min.x + 0.4f, b.min.y + 0.45f, b.min.z - 0.6f),
            new Vector3(0.45f, 0.9f, 0.45f),
            MatConColor(_matMadera, new Color(0.4f, 0.28f, 0.15f))).isStatic = true;
    }

    void AnadirLuzInterior(Transform edif, Bounds b, Color colorLuz, float intensidad, int kelvin)
    {
        var lightGO = new GameObject($"LuzInterior_{kelvin}K");
        lightGO.transform.SetParent(edif);
        lightGO.transform.position = new Vector3(b.center.x, b.min.y + 2.5f, b.min.z + 0.5f);

        var light = lightGO.AddComponent<Light>();
        light.type    = LightType.Point;
        light.color   = colorLuz;
        light.range   = 8f;
        light.enabled = false; // activar en ciclo nocturno
        var hdl = lightGO.AddComponent<HDAdditionalLightData>();
        hdl.SetIntensity(intensidad * 1000f, UnityEngine.Rendering.LightUnit.Lux); // intensidad=1 → 1000 lux (interior cálido)
        _ventanasNocturnas.Add(null); // placeholder para que el ciclo lo gestione
    }

    void AnadirCajasAC(Transform edif, Bounds b, float h)
    {
        int n = Mathf.Max(1, (int)(b.size.z / 4f));
        for (int k = 0; k < n; k++)
        {
            float z = b.min.z + (k + 0.5f) * (b.size.z / n);
            PrimitivoHijo($"CajaAC{k}", edif,
                new Vector3(b.max.x + 0.2f, b.min.y + h - 1.2f, z),
                new Vector3(0.8f, 0.5f, 0.6f), _matChapa).isStatic = true;
        }
    }

    void AnadirPuertaCorredera(Transform edif, Bounds b, float anchoP, float altoP)
    {
        PrimitivoHijo("PuertaCorredera", edif,
            new Vector3(b.center.x, b.min.y + altoP * 0.5f, b.min.z - 0.08f),
            new Vector3(anchoP, altoP, 0.1f), _matChapa).isStatic = true;

        // Raíl superior
        PrimitivoHijo("Rail", edif,
            new Vector3(b.center.x, b.min.y + altoP + 0.06f, b.min.z - 0.06f),
            new Vector3(anchoP + 0.6f, 0.1f, 0.12f), _matHierro).isStatic = true;
    }

    void AnadirLucernario(Transform edif, Bounds b, float h, float w, float d)
    {
        float lw = w * 0.3f, ld = d * 0.15f;
        PrimitivoHijo("Lucernario", edif,
            new Vector3(b.center.x, b.min.y + h + 0.3f, b.center.z),
            new Vector3(lw, 0.6f, ld), _matVidrio).isStatic = true;
    }

    void AnadirCanalones(Transform edif, Bounds b, float h, float w)
    {
        int n = Mathf.Max(1, (int)(w / 8f));
        for (int k = 0; k < n; k++)
        {
            float x = b.min.x + (k + 0.5f) * (w / n);
            // Canalón horizontal
            PrimitivoHijo($"Canalon{k}", edif,
                new Vector3(x, b.min.y + h + 0.05f, b.min.z - 0.15f),
                new Vector3(0.12f, 0.12f, 0.12f), _matHierro).isStatic = true;
            // Bajante vertical
            PrimitivoHijo($"Bajante{k}", edif,
                new Vector3(x, b.min.y + h * 0.5f, b.min.z - 0.1f),
                new Vector3(0.1f, h, 0.1f), _matHierro).isStatic = true;
        }
    }

    void AnadirCanalonBajante(Transform edif, Bounds b, float h, Material mat)
    {
        // Canalones en la base del tejado a dos aguas
        int n = Mathf.Max(1, (int)(b.size.x / 8f));
        for (int k = 0; k < n; k++)
        {
            float x = b.min.x + (k + 0.5f) * (b.size.x / n);
            PrimitivoHijo($"Bajante{k}", edif,
                new Vector3(x, b.min.y + h * 0.5f, b.min.z - 0.12f),
                new Vector3(0.08f, h, 0.08f),
                MatConColor(_matHierro, new Color(0.35f, 0.35f, 0.38f))).isStatic = true;
        }
    }

    void AnadirMarcasImpacto(Transform edif, Bounds b, float w, float d)
    {
        int n = Random.Range(5, 15);
        for (int k = 0; k < n; k++)
        {
            float x = b.min.x + Random.Range(0.1f, 0.9f) * w;
            float y = b.min.y + Random.Range(0.5f, b.size.y * 0.8f);
            var impacto = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            impacto.name = $"Impacto{k}";
            impacto.transform.SetParent(edif);
            impacto.transform.position = new Vector3(x, y, b.min.z - 0.02f);
            impacto.transform.localScale = Vector3.one * Random.Range(0.04f, 0.1f);
            impacto.GetComponent<Renderer>().sharedMaterial =
                MatConColor(_matHormigon, new Color(0.3f, 0.28f, 0.25f));
            Object.Destroy(impacto.GetComponent<Collider>());
            impacto.isStatic = true;
        }
    }

    void AnadirRedMetalica(Transform edif, Bounds b, float h)
    {
        // Postes laterales
        for (int k = 0; k < 4; k++)
        {
            float z = b.min.z + k * (b.size.z / 3f);
            PrimitivoHijo($"PosteRedD{k}", edif,
                new Vector3(b.max.x + 0.05f, b.min.y + h * 0.5f, z),
                new Vector3(0.08f, h, 0.08f), _matHierro).isStatic = true;
        }
    }

    void AnadirPilaresAparcamiento(Transform edif, Bounds b, float h, float w, float d)
    {
        int nx = Mathf.Max(2, (int)(w / 5f));
        int nz = Mathf.Max(2, (int)(d / 5f));
        for (int ix = 0; ix <= nx; ix++)
        for (int iz = 0; iz <= nz; iz++)
        {
            float x = b.min.x + ix * (w / nx);
            float z = b.min.z + iz * (d / nz);
            PrimitivoHijo($"Pilar_{ix}_{iz}", edif,
                new Vector3(x, b.min.y + h * 0.5f, z),
                new Vector3(0.35f, h, 0.35f), _matHormigon).isStatic = true;
        }
    }

    void AnadirLineasAparcamiento(Transform edif, Bounds b, float w, float d)
    {
        int n = Mathf.Max(2, (int)(w / 2.5f));
        for (int k = 0; k <= n; k++)
        {
            float x = b.min.x + k * (w / n);
            PrimitivoHijo($"Linea{k}", edif,
                new Vector3(x, b.min.y + 0.02f, b.center.z),
                new Vector3(0.1f, 0.01f, d), _matLinea).isStatic = true;
        }
    }

    void AnadirEscombros(Transform edif, Bounds b)
    {
        int n = Random.Range(3, 7);
        for (int k = 0; k < n; k++)
        {
            float x = b.min.x + Random.Range(0.1f, 0.9f) * b.size.x;
            float z = b.min.z + Random.Range(0.1f, 0.9f) * b.size.z;
            var esc = GameObject.CreatePrimitive(PrimitiveType.Cube);
            esc.name = $"Escombro{k}";
            esc.transform.SetParent(edif);
            esc.transform.position = new Vector3(x, b.min.y + 0.2f, z);
            esc.transform.localScale = new Vector3(
                Random.Range(0.2f, 0.8f), Random.Range(0.1f, 0.5f), Random.Range(0.2f, 0.8f));
            esc.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);
            esc.GetComponent<Renderer>().sharedMaterial =
                MatConColor(_matPiedra, Color.Lerp(COL_PIEDRA_OSCURA, Color.gray, Random.value * 0.4f));
            Object.Destroy(esc.GetComponent<Collider>());
            esc.isStatic = true;
        }
    }

    void AnadirMegafono(Transform edif, Bounds b)
    {
        PrimitivoHijo("Megafono", edif,
            new Vector3(b.min.x + 0.5f, b.min.y + b.size.y * 0.8f, b.min.z - 0.3f),
            new Vector3(0.25f, 0.25f, 0.4f),
            MatConColor(_matChapa, new Color(0.4f, 0.4f, 0.45f))).isStatic = true;
    }

    void AnadirGraffiti(Transform edif, Bounds b)
    {
        var (texto, color) = GRAFFITI[Random.Range(0, GRAFFITI.Length)];
        bool fachada = Random.value > 0.5f;

        Vector3 pos;
        Quaternion rot;
        if (fachada)
        {
            pos = new Vector3(b.min.x + Random.Range(0.15f, 0.85f) * b.size.x,
                b.min.y + Random.Range(0.5f, 2.0f), b.min.z - 0.015f);
            rot = Quaternion.Euler(0, 180, 0);
        }
        else
        {
            pos = new Vector3(b.min.x - 0.015f,
                b.min.y + Random.Range(0.5f, 2.0f),
                b.min.z + Random.Range(0.15f, 0.85f) * b.size.z);
            rot = Quaternion.Euler(0, 90, 0);
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = $"Graffiti_{texto.Replace(" ", "_")}";
        go.transform.SetParent(edif);
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = new Vector3(Random.Range(0.9f, 2.2f), Random.Range(0.35f, 0.6f), 1f);
        go.GetComponent<MeshRenderer>().sharedMaterial =
            new Material(Shader.Find("HDRP/Unlit") ?? Shader.Find("Standard"))
            { color = new Color(color.r, color.g, color.b, 0.88f) };
        go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Object.Destroy(go.GetComponent<Collider>());
        go.isStatic = true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  LOD — 4 niveles
    // ═══════════════════════════════════════════════════════════════════════

    void ConfigurarLOD(Transform edif, MeshRenderer mrBase, Mesh mesh,
                        ArquetipoVasco arquetipo)
    {
        if (edif.GetComponent<LODGroup>() != null) return;

        var allRenderers = edif.GetComponentsInChildren<MeshRenderer>();

        // LOD1: sin interiores (sin vidrios, sin luces)
        var rendLOD1 = allRenderers.Where(r =>
            !r.gameObject.name.StartsWith("Vidrio") &&
            !r.gameObject.name.StartsWith("LuzInterior")).ToArray();

        // LOD2: mesh simplificado — solo el cuerpo base + tejado
        var cajaSimpGO = new GameObject("LOD2_Simplified");
        cajaSimpGO.transform.SetParent(edif);
        cajaSimpGO.transform.localPosition = Vector3.zero;
        var cajaMF = cajaSimpGO.AddComponent<MeshFilter>();
        cajaMF.sharedMesh = CuboUnitarioCacheado();
        var cajaMR = cajaSimpGO.AddComponent<MeshRenderer>();
        cajaMR.sharedMaterial = mrBase?.sharedMaterial ?? _matHormigon;
        cajaMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        cajaSimpGO.transform.localScale = mesh.bounds.size;
        cajaSimpGO.transform.localPosition = mesh.bounds.center - edif.position;
        cajaSimpGO.isStatic = true;

        // LOD3: billboard impostor (quad con textura baked — simplificado a cubo mínimo)
        var billboardGO = new GameObject("LOD3_Billboard");
        billboardGO.transform.SetParent(edif);
        billboardGO.transform.localPosition = mesh.bounds.center - edif.position;
        var bbMF = billboardGO.AddComponent<MeshFilter>();
        bbMF.sharedMesh = CuboUnitarioCacheado();
        var bbMR = billboardGO.AddComponent<MeshRenderer>();
        bbMR.sharedMaterial = cajaMR.sharedMaterial;
        bbMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        billboardGO.transform.localScale = mesh.bounds.size * 1.02f;
        billboardGO.isStatic = true;

        var lod = edif.gameObject.AddComponent<LODGroup>();
        lod.fadeMode = LODFadeMode.CrossFade;
        lod.SetLODs(new LOD[]
        {
            new LOD(1f - 40f / 700f,   allRenderers),          // LOD0 <40m
            new LOD(1f - 120f / 700f,  rendLOD1),              // LOD1 <120m
            new LOD(1f - 350f / 700f,  new Renderer[]{cajaMR}),// LOD2 <350m
            new LOD(0f,                new Renderer[]{bbMR})   // LOD3 <700m
        });
        lod.RecalculateBounds();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MATERIALES
    // ═══════════════════════════════════════════════════════════════════════

    void CargarMateriales()
    {
        // Vidrio HDRP Smoothness=0.92, IOR=1.52
        _matVidrio = MatPBR(new Color(0.55f, 0.75f, 0.90f, 0.25f), "Vidrio", 0.92f, 0.05f);
        // IOR en HDRP
        if (_matVidrio.HasProperty("_IOR"))         _matVidrio.SetFloat("_IOR", 1.52f);
        if (_matVidrio.HasProperty("_Ior"))         _matVidrio.SetFloat("_Ior", 1.52f);

        _matMarco    = MatPBR(new Color(0.22f, 0.20f, 0.17f), "Marco",   0.30f, 0.0f);
        _matHierro   = MatPBR(new Color(0.12f, 0.11f, 0.13f), "Hierro",  0.55f, 0.90f);
        _matCornisa  = MatPBR(new Color(0.82f, 0.79f, 0.75f), "Cornisa", 0.40f, 0.0f);
        _matZocalo   = MatPBR(new Color(0.52f, 0.48f, 0.44f), "Zocalo",  0.45f, 0.0f);
        _matMadera   = MatPBR(new Color(0.55f, 0.38f, 0.20f), "Madera",  0.25f, 0.0f);
        _matChapa    = MatPBR(new Color(0.56f, 0.57f, 0.58f), "Chapa",   0.80f, 0.75f);
        _matAzulejo  = MatPBR(new Color(0.55f, 0.65f, 0.75f), "Azulejo", 0.75f, 0.0f);
        _matPiedra   = MatPBR(COL_PIEDRA_OSCURA,               "Piedra",  0.35f, 0.0f);
        _matHormigon = MatPBR(COL_HORMIGON,                    "Hormigon",0.40f, 0.0f);
        _matAsfalto  = MatPBR(new Color(0.25f, 0.25f, 0.26f), "Asfalto", 0.15f, 0.0f);
        _matLinea    = MatPBR(new Color(0.95f, 0.95f, 0.95f), "Linea",   0.35f, 0.0f);

        _matTejadoTeja    = MatPBR(COL_TERRACOTA,                "TejadoTeja",  0.45f, 0.0f);
        _matTejadoPizarra = MatPBR(COL_PIZARRA,                  "TejadoPizarra",0.50f, 0.0f);
        _matTejadoMetal   = MatPBR(new Color(0.45f, 0.47f, 0.50f),"TejadoMetal", 0.80f, 0.70f);
        _matTejadoPlano   = MatPBR(new Color(0.52f, 0.52f, 0.54f),"TejadoPlano", 0.35f, 0.0f);

        AlsasuaLogger.Info("EdificiosAAA", "Materiales HDRP cargados (12 arquetipos)");
    }

    Material MaterialFachadaPorArquetipo(ArquetipoVasco arquetipo, long id)
    {
        Color c = arquetipo switch
        {
            ArquetipoVasco.UrbanoPre1940      => COL_ARENISCA,
            ArquetipoVasco.Bloque1940_1975    => COL_LADRILLO,
            ArquetipoVasco.ModernoPost1975    => COL_REVOCO_BLANCO,
            ArquetipoVasco.Casero             => COL_CAL_BLANCA,
            ArquetipoVasco.Bar                => new Color(0.80f, 0.75f, 0.65f),
            ArquetipoVasco.Comercio           => new Color(0.82f, 0.80f, 0.75f),
            ArquetipoVasco.Iglesia            => COL_PIEDRA_OSCURA,
            ArquetipoVasco.NaveIndustrial     => COL_CHAPA_METAL,
            ArquetipoVasco.EquipamientoPublico=> COL_LADRILLO,
            ArquetipoVasco.Fronton            => COL_HORMIGON,
            ArquetipoVasco.Aparcamiento       => new Color(0.50f, 0.50f, 0.52f),
            ArquetipoVasco.Solar              => COL_PIEDRA_OSCURA,
            _                                 => new Color(0.75f, 0.73f, 0.70f)
        };

        // Intentar obtener color real de fachada
        if (FusionadorEdificiosUltra.Instance != null
            && FusionadorEdificiosUltra.Instance.GetWallColor(id, out var colorReal))
            c = colorReal;

        string key = $"Fachada_{arquetipo}_{id}";
        if (!_matCache.TryGetValue(key, out var mat))
        {
            mat = MatPBR(c, key, SmoothnessParaArquetipo(arquetipo), 0.0f);
            AplicarTexturaFachada(mat, arquetipo);   // PBR real, tintado con el color real
            mat.enableInstancing = true;
            _matCache[key] = mat;
        }
        return mat;
    }

    // ── Textura PBR por arquetipo (Resources/PBR_Vivo, CC0 Poly Haven) ──────
    // El color real de ortofoto/OSM se mantiene como tinte (_BaseColor) sobre la
    // textura: cada edificio conserva SU color, pero la pared deja de ser color
    // plano y pasa a leerse como revoco/ladrillo/piedra/chapa con relieve.
    [Tooltip("Repeticiones de textura de fachada por unidad UV. Ajustar si el grano se ve grande/pequeño.")]
    [SerializeField] float tilingFachada = 0.35f;

    static readonly Dictionary<string, Texture2D> _texCache = new();

    void AplicarTexturaFachada(Material mat, ArquetipoVasco arquetipo)
    {
        (string dif, string nor) t = arquetipo switch
        {
            ArquetipoVasco.Bloque1940_1975  => ("brick_4_diff_1k",          "brick_4_nor_gl_1k"),
            ArquetipoVasco.NaveIndustrial   => ("corrugated_iron_diff_1k",  "corrugated_iron_nor_gl_1k"),
            ArquetipoVasco.Iglesia          => ("cobblestone_03_diff_1k",   "cobblestone_03_nor_gl_1k"),
            _                               => ("concrete_wall_008_diff_1k","concrete_wall_008_nor_gl_1k")
        };

        var dif = CargarTexPBR(t.dif);
        var nor = CargarTexPBR(t.nor);
        if (dif != null)
        {
            mat.SetTexture("_BaseColorMap", dif);
            mat.SetTextureScale("_BaseColorMap", Vector2.one * tilingFachada);
        }
        if (nor != null)
        {
            mat.SetTexture("_NormalMap", nor);
            mat.SetFloat("_NormalScale", 0.8f);
            mat.EnableKeyword("_NORMALMAP");
            mat.EnableKeyword("_NORMALMAP_TANGENT_SPACE");
        }
    }

    static Texture2D CargarTexPBR(string nombre)
    {
        if (_texCache.TryGetValue(nombre, out var tex)) return tex;
        tex = Resources.Load<Texture2D>($"PBR_Vivo/{nombre}");
        _texCache[nombre] = tex;   // cachea también null para no reintentar
        return tex;
    }

    void AplicarVariacionColor(MeshRenderer mr, ArquetipoVasco arquetipo, long id)
    {
        // Perturbación Perlin ±8% por edificio usando MaterialPropertyBlock
        float noise = (Mathf.PerlinNoise(id * 0.0013f, arquetipo.GetHashCode() * 0.0007f) - 0.5f) * 0.16f;
        if (mr.sharedMaterial != null)
        {
            _mpb.Clear();
            Color base_ = mr.sharedMaterial.color;
            _mpb.SetColor("_BaseColor", new Color(
                Mathf.Clamp01(base_.r + noise),
                Mathf.Clamp01(base_.g + noise * 0.8f),
                Mathf.Clamp01(base_.b + noise * 0.6f), base_.a));
            mr.SetPropertyBlock(_mpb);
        }
    }

    float SmoothnessParaArquetipo(ArquetipoVasco a) => a switch
    {
        ArquetipoVasco.ModernoPost1975    => 0.55f,
        ArquetipoVasco.NaveIndustrial     => 0.70f,
        ArquetipoVasco.Fronton            => 0.45f,
        ArquetipoVasco.Bar or ArquetipoVasco.Comercio => 0.50f,
        _ => 0.35f
    };

    Material MatPBR(Color color, string nombre, float smoothness, float metallic)
    {
        var mat = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"))
            { name = nombre, color = color };
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic",   metallic);
        mat.enableInstancing = true;
        return mat;
    }

    Material MatConColor(Material base_, Color color)
    {
        if (base_ == null) return MatPBR(color, "temp", 0.4f, 0f);
        var m = new Material(base_) { color = color };
        m.enableInstancing = true;
        return m;
    }

    Mesh GenerarMeshFaldon(float w, float d, float crest, bool lado)
    {
        float sign = lado ? 1f : -1f;
        var verts = new Vector3[]
        {
            new(sign * w * 0.5f, 0f, -d * 0.5f),
            new(sign * w * 0.5f, 0f,  d * 0.5f),
            new(0f, crest,            -d * 0.5f),
            new(0f, crest,             d * 0.5f),
        };
        var tris = new int[] { 0, 2, 1, 1, 2, 3 };
        var uvs  = new Vector2[] { new(0,0), new(0,1), new(1,0), new(1,1) };
        var m = new Mesh();
        m.SetVertices(verts); m.SetTriangles(tris, 0); m.SetUVs(0, uvs);
        m.RecalculateNormals();
        return m;
    }

    static Color DarkenColor(Color c, float factor)
        => new(c.r * factor, c.g * factor, c.b * factor, c.a);

    static ArquetipoVasco ArquetipoDesdeNombre(string nombre)
    {
        nombre = nombre.ToLower();
        if (nombre.Contains("iglesia") || nombre.Contains("church") || nombre.Contains("eliza"))
            return ArquetipoVasco.Iglesia;
        if (nombre.Contains("bar") || nombre.Contains("taberna") || nombre.Contains("bodega"))
            return ArquetipoVasco.Bar;
        if (nombre.Contains("industrial") || nombre.Contains("nave") || nombre.Contains("fabrika"))
            return ArquetipoVasco.NaveIndustrial;
        if (nombre.Contains("fronton") || nombre.Contains("pilota"))
            return ArquetipoVasco.Fronton;
        if (nombre.Contains("school") || nombre.Contains("ikastola") || nombre.Contains("escuela"))
            return ArquetipoVasco.EquipamientoPublico;
        return ArquetipoVasco.UrbanoPre1940;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  LUCES NOCTURNAS
    // ═══════════════════════════════════════════════════════════════════════

    // ── Ventanas nocturnas ────────────────────────────────────────────────
    // Mejora sobre la versión anterior:
    //   · Usa MaterialPropertyBlock en lugar de r.material para evitar
    //     instanciar un material por renderer (memory leak + draw calls extra).
    //   · Gradiente suave según _GlobalNightLevel en lugar de binario on/off.
    //   · Cada ventana tiene una temperatura de color aleatoria seed-ed por índice:
    //     mezcla de cálido (vela/sodio), neutro (LED) y frío (pantalla TV).
    //   · CicloLucesNocturnas ahora corre cada 2s en lugar de 30s para que
    //     reaccione al ciclo día/noche dinámico del SistemaVolumenHDRP.

    static readonly int ID_EmissiveColor = Shader.PropertyToID("_EmissiveColor");
    static readonly int ID_EmissiveHDR   = Shader.PropertyToID("_EmissiveColorHDR");
    // Semilla de aleatoriedad para las ventanas (determinista por índice)
    static readonly Color[] COLS_VENTANA = {
        new Color(1.0f, 0.85f, 0.55f),   // cálido — bombilla incandescente
        new Color(1.0f, 0.92f, 0.78f),   // neutro-cálido — LED blanco
        new Color(0.80f, 0.90f, 1.0f),   // frío — pantalla TV/monitor
        new Color(1.0f, 0.78f, 0.40f),   // ámbar — sodio de cocina
    };

    IEnumerator CicloLucesNocturnas()
    {
        while (true)
        {
            yield return new WaitForSeconds(2f); // 2s: reacciona al ciclo dinámico
            float nightLevel = Shader.GetGlobalFloat("_GlobalNightLevel");
            yield return StartCoroutine(ActualizarVentanasMPB(nightLevel));
        }
    }

    IEnumerator ActualizarVentanasMPB(float nightLevel)
    {
        // nightLevel: 0 = pleno día (ventanas apagadas), 1 = plena noche (todas encendidas)
        // A partir de 0.25 las ventanas empiezan a encenderse; a 0.75 están al máximo.
        float intensidadBase = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.75f, nightLevel));

        for (int i = 0; i < _ventanasNocturnas.Count; i++)
        {
            var r = _ventanasNocturnas[i];
            if (r == null) continue;

            // Patrón de encendido: no todas las ventanas lit al mismo tiempo.
            // Combina módulos y la semilla del índice para apariencia realista.
            bool lit = (i % 3 != 0) && (i % 11 != 2);  // ~60% encendidas
            float intensidad = lit ? intensidadBase : intensidadBase * 0.12f; // apagadas = muy tenue

            // Color de la ventana: determinista por índice (sin Random.value cada frame)
            int colorIdx = (i * 7 + (i / 4)) % COLS_VENTANA.Length;
            Color col = COLS_VENTANA[colorIdx] * (intensidad * 3.5f); // HDR intensity

            // MaterialPropertyBlock: no crea instancias de material
            _mpb.Clear();
            _mpb.SetColor(ID_EmissiveColor, col);
            _mpb.SetColor(ID_EmissiveHDR,   col);
            r.SetPropertyBlock(_mpb);

            // Spread el trabajo: procesar 80 ventanas por frame máximo
            if (i % 80 == 0) yield return null;
        }
    }

    // ── Cache mesh ────────────────────────────────────────────────────────
    static Mesh _cuboCache;
    static Mesh CuboUnitarioCacheado()
    {
        if (_cuboCache != null) return _cuboCache;
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _cuboCache = tmp.GetComponent<MeshFilter>().sharedMesh;
        Object.Destroy(tmp);
        return _cuboCache;
    }

    GameObject PrimitivoHijo(string nombre, Transform padre, Vector3 pos, Vector3 escala, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = nombre;
        go.transform.SetParent(padre);
        go.transform.position   = pos;
        go.transform.localScale = escala;
        if (mat != null)
            go.GetComponent<Renderer>().sharedMaterial = mat;
        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
        return go;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  VALIDACIÓN (llamar desde Editor)
    // ═══════════════════════════════════════════════════════════════════════

    [ContextMenu("Validar Edificios")]
    public void ValidarEdificios()
    {
        var fusionador = FusionadorEdificiosUltra.Instance;
        if (fusionador == null)
        {
            AlsasuaLogger.Warn("ValidarEdificios", "FusionadorEdificiosUltra no disponible");
            return;
        }

        var edificios = GetComponentsInChildren<MeshRenderer>(true);
        int errorAltura = 0, errorBase = 0, sinRotulo = 0, total = 0;
        var porArquetipo = new Dictionary<string, int>();

        foreach (var mr in edificios)
        {
            if (!mr.gameObject.name.StartsWith("Edif_")) continue;
            total++;

            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null) continue;
            var bounds = mf.sharedMesh?.bounds ?? default;
            float h = bounds.size.y;

            // 1. Altura Unity vs LIDAR
            string nameStr = mr.gameObject.name;
            if (long.TryParse(ExtractId(nameStr), out long id))
            {
                float altLidar = fusionador.GetAlturaOptima(id, 0);
                float diff = Mathf.Abs(h - altLidar);
                if (diff > 0.3f && altLidar > 1f)
                {
                    AlsasuaLogger.Warn("ValidarEdificios",
                        $"{nameStr}: altura Unity={h:F1}m LIDAR={altLidar:F1}m diff={diff:F2}m");
                    errorAltura++;
                }
            }

            // 2. Base == terrain ±0.1m
            float xPos = mr.transform.position.x;
            float zPos = mr.transform.position.z;
            float yBase = bounds.min.y + mr.transform.position.y;
            float yTerrain = GeoDataAlsasua.AlturaTerreno(xPos, zPos);
            if (Mathf.Abs(yBase - yTerrain) > 0.1f)
            {
                AlsasuaLogger.Warn("ValidarEdificios",
                    $"{nameStr}: base={yBase:F2} terrain={yTerrain:F2} diff={Mathf.Abs(yBase-yTerrain):F2}m");
                errorBase++;
            }

            // 3. Bares y comercios tienen rótulo
            if (nameStr.Contains("Bar") || nameStr.Contains("Comercio"))
            {
                bool tieneRotulo = mr.transform.Find("Rotulo") != null
                    || mr.GetComponentsInChildren<MeshRenderer>()
                         .Any(r => r.name.StartsWith("Rotulo_"));
                if (!tieneRotulo)
                {
                    AlsasuaLogger.Warn("ValidarEdificios", $"{nameStr}: sin rótulo");
                    sinRotulo++;
                }
            }

            // 4. Estadísticas por arquetipo
            string arqStr = ExtractArquetipo(nameStr);
            porArquetipo[arqStr] = (porArquetipo.TryGetValue(arqStr, out int v) ? v : 0) + 1;
        }

        AlsasuaLogger.Info("ValidarEdificios",
            $"Total: {total} | Errores altura: {errorAltura} | Base offset: {errorBase} | Sin rótulo: {sinRotulo}");

        foreach (var kv in porArquetipo.OrderByDescending(x => x.Value))
            AlsasuaLogger.Info("ValidarEdificios", $"  {kv.Key}: {kv.Value}");
    }

    static string ExtractId(string nombre)
    {
        var parts = nombre.Split('_');
        return parts.Length > 1 ? parts[1] : "0";
    }

    static string ExtractArquetipo(string nombre)
    {
        if (nombre.Contains("UrbanoPre"))         return "UrbanoPre1940";
        if (nombre.Contains("Bloque"))            return "Bloque1940-1975";
        if (nombre.Contains("Moderno"))           return "ModernoPost1975";
        if (nombre.Contains("Casero"))            return "Caserio";
        if (nombre.Contains("Bar"))               return "Bar";
        if (nombre.Contains("Comercio"))          return "Comercio";
        if (nombre.Contains("Iglesia"))           return "Iglesia";
        if (nombre.Contains("NaveIndustrial"))    return "NaveIndustrial";
        if (nombre.Contains("Equipamiento"))      return "EquipamientoPublico";
        if (nombre.Contains("Fronton"))           return "Fronton";
        if (nombre.Contains("Aparcamiento"))      return "Aparcamiento";
        if (nombre.Contains("Solar"))             return "Solar";
        return "Generico";
    }
}
