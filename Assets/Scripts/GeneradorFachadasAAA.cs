// Assets/Scripts/GeneradorFachadasAAA.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GENERADOR DE FACHADAS AAA — ventanas, balcones, cornisas procedurales
//
//  Grid ventanas:
//    columnas = FloorToInt(ancho / 1.15f), filas = niveles
//    Ventana vasca: Kit_Window_Upper_Convex pisos 0-1, Straight superiores
//    Planta baja residencial: puerta + Canopy_Full
//    Planta baja comercio: escaparate transparent 80%
//    Balcones pisos 1-3 pre-1940: Canopy_Full + Canopy_Beam_Short + barandilla Metal negro
//    Color fachada: sample ortofoto_alsasua_REAL.png centroide 2m altura → tint ±8% Perlin
//
//  Panel de electrical en paredes traseras de comercios (altura 1.4m)
//  Contenedores basura procedurales: 1 cada 3-4 portales
//  Farola Lantern_01 con PointLight 2700K intensity 3.5 en acera
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class GeneradorFachadasAAA : MonoBehaviour
{
    public static GeneradorFachadasAAA Instance { get; private set; }

    [Header("Parámetros de ventanas")]
    [Tooltip("Paso módulo ventana: columnas = FloorToInt(ancho / moduloVentana)")]
    public float moduloVentana     = 1.15f;
    public float anchoVentanaVasca = 0.55f;
    public float altoVentanaVasca  = 1.25f;
    public float anchoVentanaRecta = 0.65f;
    public float altoVentanaRecta  = 1.20f;

    [Header("Parámetros generales")]
    public float alturaZocalo    = 0.28f;
    public float alturaCornisa   = 0.22f;
    public bool  generarBalcones = true;
    public bool  generarGraffiti = true;
    public bool  generarProps    = true;

    // ── Materiales ────────────────────────────────────────────────────────
    Material _matVidrio, _matMarco, _matBalcon, _matCornisa, _matZocalo;
    Material _matHierroNegro, _matPersiana, _matRotulo;
    Material _matGraffiti, _matContenedor;

    // Ortofoto para sampling de color
    Texture2D _ortofoto;
    Rect _ortoRect; // coordenadas Unity que cubre la ortofoto

    // ── Estadísticas ──────────────────────────────────────────────────────
    int _edificiosProcesados;
    int _portalesAcumulados; // para contenedores basura cada 3-4 portales

    // ── Textos graffiti vascos ─────────────────────────────────────────────
    static readonly string[] TEXTOS_GRAFFITI = {
        "ASKATASUNA", "PRESOAK ETXERA", "INDEPENDENTZIA",
        "AMNISTIA", "GORA EUSKAL HERRIA", "ACAB", "ETA",
        "NO PASARÁN", "RESISTENCIA", "HERRIRA", "BIZI NAIZ",
    };

    // ─────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        // Solo activar si SistemaEdificiosAAA no existe en escena
        if (FindFirstObjectByType<SistemaEdificiosAAA>() == null)
            GeneradorMundoOSM.OnMundoGenerado += () => StartCoroutine(EnriquecerEdificios());
    }

    IEnumerator EnriquecerEdificios()
    {
        yield return new WaitForSeconds(1f);

        CrearMateriales();
        CargarOrtofoto();

        if (GeneradorGeometriaPrecisa.Instance != null) yield break;

        var edificios = GameObject.Find("Edificios_OSM");
        if (edificios == null) yield break;

        int procesados = 0;
        foreach (Transform edif in edificios.transform)
        {
            if (edif == null) continue;
            var mf = edif.GetComponent<MeshFilter>();
            if (mf?.sharedMesh == null) continue;

            EnriquecerEdificio(edif, mf.sharedMesh);
            procesados++;
            _edificiosProcesados++;

            if (procesados % 15 == 0) yield return null;
        }

        AlsasuaLogger.Info("FachadasAAA",
            $"✅ {procesados} edificios enriquecidos con fachadas detalladas");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  ENRIQUECIMIENTO POR EDIFICIO
    // ─────────────────────────────────────────────────────────────────────

    void EnriquecerEdificio(Transform edif, Mesh meshBase)
    {
        var bounds  = meshBase.bounds;
        float h     = bounds.size.y;
        float w     = bounds.size.x;
        float d     = bounds.size.z;
        int   niveles = Mathf.Max(1, Mathf.RoundToInt(h / GeoDataAlsasua.ALT_PLANTA));

        // Determinar arquetipo desde FusionadorEdificiosUltra o nombre
        ArquetipoVasco arquetipo = DeterminarArquetipo(edif, bounds);
        bool esHistorico  = arquetipo == ArquetipoVasco.UrbanoPre1940;
        bool esComercio   = arquetipo is ArquetipoVasco.Comercio or ArquetipoVasco.Bar;

        // Color fachada desde ortofoto ± Perlin 8%
        Color colorFachada = SampleColorOrtofoto(
            bounds.center.x, bounds.min.y + 2f, bounds.center.z);
        AplicarTintFachada(edif.GetComponent<MeshRenderer>(), colorFachada, edif.GetInstanceID());

        // ── Zócalo ────────────────────────────────────────────────────────
        AnadirZocalo(edif, bounds);

        // ── Cornisa superior ──────────────────────────────────────────────
        AnadirCornisa(edif, bounds, h);

        // ── Ventanas por planta ───────────────────────────────────────────
        for (int piso = 0; piso < niveles; piso++)
        {
            float yBase       = bounds.min.y + alturaZocalo + piso * GeoDataAlsasua.ALT_PLANTA;
            bool  esPlantaBaja = piso == 0;
            bool  usarConvex  = esHistorico && (piso <= 1);

            AnadirVentanasEnFachada(edif, bounds, yBase, w, d, esPlantaBaja,
                                     usarConvex, esComercio);
        }

        // ── Balcones pre-1940: pisos 1-3 ─────────────────────────────────
        if (generarBalcones && esHistorico && niveles > 1)
        {
            for (int piso = 1; piso <= Mathf.Min(2, niveles - 1); piso++)
            {
                float yBalcon = bounds.min.y + piso * GeoDataAlsasua.ALT_PLANTA + 0.9f;
                AnadirBalconHistorico(edif, bounds, yBalcon, w);
            }
        }
        else if (generarBalcones && niveles > 2 && Random.value < 0.4f)
        {
            float yBalcon = bounds.min.y + 1 * GeoDataAlsasua.ALT_PLANTA + 0.8f;
            AnadirBalconSimple(edif, bounds, yBalcon, w);
        }

        // ── Props: contenedor basura cada 3-4 portales ────────────────────
        if (generarProps)
        {
            _portalesAcumulados++;
            if (_portalesAcumulados % Random.Range(3, 5) == 0)
                AnadirContenedorBasura(edif, bounds);
        }

        // ── Panel eléctrico en fachada trasera comercios ──────────────────
        if (generarProps && esComercio)
            AnadirPanelElectrico(edif, bounds, 1.4f);

        // ── Graffiti ──────────────────────────────────────────────────────
        if (generarGraffiti && (esHistorico || arquetipo == ArquetipoVasco.Solar)
            && Random.value < 0.35f)
            AnadirGraffiti(edif, bounds);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  ZÓCALO
    // ─────────────────────────────────────────────────────────────────────

    void AnadirZocalo(Transform edif, Bounds b)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Zocalo";
        go.transform.SetParent(edif);
        go.transform.position   = new Vector3(b.center.x, b.min.y + alturaZocalo * 0.5f, b.center.z);
        go.transform.localScale = new Vector3(b.size.x + 0.06f, alturaZocalo, b.size.z + 0.06f);
        go.GetComponent<Renderer>().sharedMaterial = _matZocalo;
        go.isStatic = true;
        Object.Destroy(go.GetComponent<Collider>());
    }

    // ─────────────────────────────────────────────────────────────────────
    //  CORNISA
    // ─────────────────────────────────────────────────────────────────────

    void AnadirCornisa(Transform edif, Bounds b, float h)
    {
        // Banda principal
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Cornisa";
        go.transform.SetParent(edif);
        go.transform.position   = new Vector3(b.center.x, b.min.y + h + alturaCornisa * 0.5f, b.center.z);
        go.transform.localScale = new Vector3(b.size.x + 0.32f, alturaCornisa, b.size.z + 0.32f);
        go.GetComponent<Renderer>().sharedMaterial = _matCornisa;
        go.isStatic = true;
        Object.Destroy(go.GetComponent<Collider>());

        // Gola inferior (perfil moldurado)
        var gola = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gola.name = "Cornisa_Gola";
        gola.transform.SetParent(edif);
        gola.transform.position   = new Vector3(b.center.x, b.min.y + h + 0.03f, b.center.z);
        gola.transform.localScale = new Vector3(b.size.x + 0.16f, 0.09f, b.size.z + 0.16f);
        gola.GetComponent<Renderer>().sharedMaterial = _matCornisa;
        gola.isStatic = true;
        Object.Destroy(gola.GetComponent<Collider>());
    }

    // ─────────────────────────────────────────────────────────────────────
    //  VENTANAS
    // ─────────────────────────────────────────────────────────────────────

    void AnadirVentanasEnFachada(Transform edif, Bounds b, float yBase,
                                  float w, float d, bool esPlantaBaja,
                                  bool usarConvex, bool esComercio)
    {
        // Grid: columnas = FloorToInt(ancho / moduloVentana)
        int nVentW = Mathf.Max(1, Mathf.FloorToInt(w / moduloVentana));
        int nVentD = Mathf.Max(1, Mathf.FloorToInt(d / moduloVentana));

        float offsetY  = yBase + (esPlantaBaja ? 1.0f : 0.9f);
        float altVent  = usarConvex ? altoVentanaVasca : altoVentanaRecta;
        float anchVent = usarConvex ? anchoVentanaVasca : anchoVentanaRecta;

        if (esPlantaBaja && esComercio)
        {
            // Planta baja comercial: escaparate 80% transparente
            CrearEscaparate(edif, b, w);
            return;
        }

        if (esPlantaBaja)
        {
            // Planta baja residencial: puerta central + Canopy
            int colCentral = nVentW / 2;
            for (int i = 0; i < nVentW; i++)
            {
                float x = b.min.x + (i + 0.5f) * (w / nVentW);
                if (i == colCentral)
                    CrearPuertaConCanopy(edif, new Vector3(x, yBase, b.min.z - 0.01f));
                else
                    CrearVentana(edif, new Vector3(x, offsetY, b.min.z - 0.01f),
                        Quaternion.Euler(0, 180, 0), 1.2f, 1.8f, usarConvex);
            }
            return;
        }

        // Fachada frontal y trasera
        for (int i = 0; i < nVentW; i++)
        {
            float x = b.min.x + (i + 0.5f) * (w / nVentW);
            CrearVentana(edif, new Vector3(x, offsetY, b.min.z - 0.01f),
                Quaternion.Euler(0, 180, 0), anchVent, altVent, usarConvex);
            CrearVentana(edif, new Vector3(x, offsetY, b.max.z + 0.01f),
                Quaternion.identity, anchVent, altVent, usarConvex);
        }
        // Laterales
        for (int i = 0; i < nVentD; i++)
        {
            float z = b.min.z + (i + 0.5f) * (d / nVentD);
            CrearVentana(edif, new Vector3(b.min.x - 0.01f, offsetY, z),
                Quaternion.Euler(0, 90, 0), anchVent, altVent, usarConvex);
            CrearVentana(edif, new Vector3(b.max.x + 0.01f, offsetY, z),
                Quaternion.Euler(0, -90, 0), anchVent, altVent, usarConvex);
        }
    }

    void CrearVentana(Transform edif, Vector3 pos, Quaternion rot,
                      float ancho, float alto, bool esConvex)
    {
        var go = new GameObject(esConvex ? "Ventana_Convex" : "Ventana");
        go.transform.SetParent(edif);
        go.transform.SetPositionAndRotation(pos, rot);
        go.isStatic = true;

        // Vierteaguas bajo ventana
        var vierteaguas = GameObject.CreatePrimitive(PrimitiveType.Cube);
        vierteaguas.name = "Vierteaguas";
        vierteaguas.transform.SetParent(go.transform);
        vierteaguas.transform.localPosition = new Vector3(0, -alto * 0.5f - 0.04f, 0.04f);
        vierteaguas.transform.localScale    = new Vector3(ancho + 0.12f, 0.05f, 0.14f);
        vierteaguas.transform.localRotation = Quaternion.Euler(-15f, 0f, 0f);
        vierteaguas.GetComponent<Renderer>().sharedMaterial = _matCornisa;
        Object.Destroy(vierteaguas.GetComponent<Collider>());

        // Marco exterior
        var marco = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marco.name = "Marco";
        marco.transform.SetParent(go.transform);
        marco.transform.localPosition = Vector3.zero;
        marco.transform.localScale    = new Vector3(ancho + 0.08f, alto + 0.08f, 0.07f);
        marco.GetComponent<Renderer>().sharedMaterial = _matMarco;
        Object.Destroy(marco.GetComponent<Collider>());

        // Vidrio
        var vidrio = GameObject.CreatePrimitive(PrimitiveType.Cube);
        vidrio.name = "Vidrio";
        vidrio.transform.SetParent(go.transform);
        vidrio.transform.localPosition = new Vector3(0, 0, -0.015f);
        vidrio.transform.localScale    = new Vector3(ancho, alto, 0.02f);
        vidrio.GetComponent<Renderer>().sharedMaterial = _matVidrio;
        Object.Destroy(vidrio.GetComponent<Collider>());

        // Dintel superior (arco para convex)
        if (esConvex)
        {
            var dintel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            dintel.name = "Dintel";
            dintel.transform.SetParent(go.transform);
            dintel.transform.localPosition = new Vector3(0, alto * 0.5f + 0.055f, 0);
            dintel.transform.localScale    = new Vector3(ancho + 0.12f, 0.11f, 0.10f);
            dintel.GetComponent<Renderer>().sharedMaterial = _matCornisa;
            Object.Destroy(dintel.GetComponent<Collider>());
        }

        // División central
        var division = GameObject.CreatePrimitive(PrimitiveType.Cube);
        division.name = "Division";
        division.transform.SetParent(go.transform);
        division.transform.localPosition = new Vector3(0, 0, -0.012f);
        division.transform.localScale    = new Vector3(0.04f, alto, 0.025f);
        division.GetComponent<Renderer>().sharedMaterial = _matMarco;
        Object.Destroy(division.GetComponent<Collider>());
    }

    void CrearPuertaConCanopy(Transform edif, Vector3 pos)
    {
        var go = new GameObject("Puerta");
        go.transform.SetParent(edif);
        go.transform.position = pos;
        go.isStatic = true;

        // Puerta
        var hoja = GameObject.CreatePrimitive(PrimitiveType.Cube);
        hoja.name = "HojaPuerta";
        hoja.transform.SetParent(go.transform);
        hoja.transform.localPosition = new Vector3(0, 1.05f, 0);
        hoja.transform.localScale    = new Vector3(1.0f, 2.1f, 0.07f);
        hoja.GetComponent<Renderer>().sharedMaterial = _matMarco;
        Object.Destroy(hoja.GetComponent<Collider>());

        // Canopy_Full sobre puerta
        var canopy = GameObject.CreatePrimitive(PrimitiveType.Cube);
        canopy.name = "Canopy_Full";
        canopy.transform.SetParent(go.transform);
        canopy.transform.localPosition = new Vector3(0, 2.4f, -0.5f);
        canopy.transform.localScale    = new Vector3(1.4f, 0.08f, 1.0f);
        canopy.GetComponent<Renderer>().sharedMaterial = _matPersiana;
        Object.Destroy(canopy.GetComponent<Collider>());

        // Soporte canopy
        var soporte = GameObject.CreatePrimitive(PrimitiveType.Cube);
        soporte.name = "CanopiSoporte";
        soporte.transform.SetParent(go.transform);
        soporte.transform.localPosition = new Vector3(0, 2.1f, -0.25f);
        soporte.transform.localScale    = new Vector3(0.06f, 0.6f, 0.06f);
        soporte.GetComponent<Renderer>().sharedMaterial = _matHierroNegro;
        Object.Destroy(soporte.GetComponent<Collider>());
    }

    void CrearEscaparate(Transform edif, Bounds b, float w)
    {
        float anchoEsc = w * 0.72f;
        float altoEsc  = 2.2f;

        // Marco
        var marco = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marco.name = "EscaparateMarco";
        marco.transform.SetParent(edif);
        marco.transform.position   = new Vector3(b.center.x, b.min.y + altoEsc * 0.5f + 0.1f, b.min.z - 0.04f);
        marco.transform.localScale = new Vector3(anchoEsc + 0.08f, altoEsc + 0.08f, 0.06f);
        marco.GetComponent<Renderer>().sharedMaterial = _matMarco;
        marco.isStatic = true;
        Object.Destroy(marco.GetComponent<Collider>());

        // Vidrio 80% transparente
        var vidrio = GameObject.CreatePrimitive(PrimitiveType.Cube);
        vidrio.name = "EscaparateVidrio";
        vidrio.transform.SetParent(edif);
        vidrio.transform.position   = new Vector3(b.center.x, b.min.y + altoEsc * 0.5f + 0.1f, b.min.z - 0.02f);
        vidrio.transform.localScale = new Vector3(anchoEsc, altoEsc, 0.02f);
        vidrio.GetComponent<Renderer>().sharedMaterial = _matVidrio;
        vidrio.isStatic = true;
        Object.Destroy(vidrio.GetComponent<Collider>());

        // Persiana metálica enrollada en parte superior
        var persiana = GameObject.CreatePrimitive(PrimitiveType.Cube);
        persiana.name = "Persiana";
        persiana.transform.SetParent(edif);
        persiana.transform.position   = new Vector3(b.center.x, b.min.y + altoEsc + 0.12f, b.min.z - 0.06f);
        persiana.transform.localScale = new Vector3(anchoEsc, 0.12f, 0.10f);
        persiana.GetComponent<Renderer>().sharedMaterial = _matPersiana;
        persiana.isStatic = true;
        Object.Destroy(persiana.GetComponent<Collider>());
    }

    // ─────────────────────────────────────────────────────────────────────
    //  BALCONES
    // ─────────────────────────────────────────────────────────────────────

    // Balcón histórico pre-1940: Canopy_Full + Canopy_Beam_Short + barandilla hierro negro
    void AnadirBalconHistorico(Transform edif, Bounds b, float y, float w)
    {
        float anchoB = Mathf.Min(w * 0.65f, 2.4f);
        float bz     = b.min.z - 1.05f;
        float bx0    = b.center.x - anchoB * 0.5f;

        var go = new GameObject("Balcon_Historico");
        go.transform.SetParent(edif);
        go.isStatic = true;

        // Losa (Canopy_Full)
        var losa = GameObject.CreatePrimitive(PrimitiveType.Cube);
        losa.name = "BalconLosa";
        losa.transform.SetParent(go.transform);
        losa.transform.position   = new Vector3(b.center.x, y, b.min.z - 0.55f);
        losa.transform.localScale = new Vector3(anchoB, 0.12f, 1.1f);
        losa.GetComponent<Renderer>().sharedMaterial = _matCornisa;
        Object.Destroy(losa.GetComponent<Collider>());

        // Viga soporte (Canopy_Beam_Short)
        var viga = GameObject.CreatePrimitive(PrimitiveType.Cube);
        viga.name = "BalconViga";
        viga.transform.SetParent(go.transform);
        viga.transform.position   = new Vector3(b.center.x, y - 0.06f, b.min.z - 0.28f);
        viga.transform.localScale = new Vector3(anchoB * 0.8f, 0.08f, 0.55f);
        viga.GetComponent<Renderer>().sharedMaterial = _matHierroNegro;
        Object.Destroy(viga.GetComponent<Collider>());

        // Barandilla hierro negro — barras verticales
        int nBarras = Mathf.Max(3, (int)(anchoB * 3f));
        for (int k = 0; k <= nBarras; k++)
        {
            float bx = bx0 + k * (anchoB / nBarras);
            var barra = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barra.name = $"Barra{k}";
            barra.transform.SetParent(go.transform);
            barra.transform.position   = new Vector3(bx, y + 0.44f, bz);
            barra.transform.localScale = new Vector3(0.03f, 0.8f, 0.03f);
            barra.GetComponent<Renderer>().sharedMaterial = _matHierroNegro;
            Object.Destroy(barra.GetComponent<Collider>());
        }

        // Barra horizontal superior e inferior
        var hBarS = GameObject.CreatePrimitive(PrimitiveType.Cube);
        hBarS.name = "BarraHSup";
        hBarS.transform.SetParent(go.transform);
        hBarS.transform.position   = new Vector3(b.center.x, y + 0.86f, bz);
        hBarS.transform.localScale = new Vector3(anchoB, 0.05f, 0.05f);
        hBarS.GetComponent<Renderer>().sharedMaterial = _matHierroNegro;
        Object.Destroy(hBarS.GetComponent<Collider>());

        var hBarI = GameObject.CreatePrimitive(PrimitiveType.Cube);
        hBarI.name = "BarraHInf";
        hBarI.transform.SetParent(go.transform);
        hBarI.transform.position   = new Vector3(b.center.x, y + 0.06f, bz);
        hBarI.transform.localScale = new Vector3(anchoB, 0.04f, 0.04f);
        hBarI.GetComponent<Renderer>().sharedMaterial = _matHierroNegro;
        Object.Destroy(hBarI.GetComponent<Collider>());
    }

    void AnadirBalconSimple(Transform edif, Bounds b, float y, float w)
    {
        float anchoB = Mathf.Min(w - 0.5f, 2.2f);

        var losa = GameObject.CreatePrimitive(PrimitiveType.Cube);
        losa.name = "BalconSimple";
        losa.transform.SetParent(edif);
        losa.transform.position   = new Vector3(b.center.x, y, b.min.z - 0.5f);
        losa.transform.localScale = new Vector3(anchoB, 0.12f, 1.0f);
        losa.GetComponent<Renderer>().sharedMaterial = _matCornisa;
        losa.isStatic = true;
        Object.Destroy(losa.GetComponent<Collider>());

        var pretil = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pretil.name = "BalconPretil";
        pretil.transform.SetParent(edif);
        pretil.transform.position   = new Vector3(b.center.x, y + 0.45f, b.min.z - 0.95f);
        pretil.transform.localScale = new Vector3(anchoB, 0.9f, 0.1f);
        pretil.GetComponent<Renderer>().sharedMaterial = _matCornisa;
        pretil.isStatic = true;
        Object.Destroy(pretil.GetComponent<Collider>());
    }

    // ─────────────────────────────────────────────────────────────────────
    //  PROPS URBANOS
    // ─────────────────────────────────────────────────────────────────────

    void AnadirContenedorBasura(Transform edif, Bounds b)
    {
        float x = b.min.x + Random.Range(0.1f, 0.9f) * b.size.x;
        float z = b.min.z - 0.7f;
        float y = GeoDataAlsasua.AlturaTerreno(x, z);

        // Contenedor procedural (2 colores: verde + azul)
        Color[] colores = { new Color(0.1f, 0.45f, 0.2f), new Color(0.1f, 0.2f, 0.7f) };
        Color col = colores[Random.Range(0, 2)];

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = $"Contenedor_{(col.g > 0.3f ? "Organico" : "Papel")}";
        go.transform.SetParent(edif.parent ?? edif);
        go.transform.position   = new Vector3(x, y + 0.55f, z);
        go.transform.localScale = new Vector3(0.9f, 1.1f, 0.7f);
        var mat = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard")) { color = col };
        mat.SetFloat("_Smoothness", 0.5f);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.isStatic = true;
    }

    void AnadirPanelElectrico(Transform edif, Bounds b, float alturaPanel)
    {
        // En fachada trasera (Z+) a la derecha
        float x = b.max.x - 0.6f;
        float z = b.max.z + 0.04f;
        float y = b.min.y + alturaPanel;

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "PanelElectrico";
        go.transform.SetParent(edif);
        go.transform.position   = new Vector3(x, y, z);
        go.transform.localScale = new Vector3(0.4f, 0.55f, 0.1f);
        go.transform.rotation   = Quaternion.identity;
        go.GetComponent<Renderer>().sharedMaterial =
            new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"))
            { color = new Color(0.3f, 0.3f, 0.32f) };
        go.isStatic = true;
        Object.Destroy(go.GetComponent<Collider>());
    }

    // ─────────────────────────────────────────────────────────────────────
    //  GRAFFITI
    // ─────────────────────────────────────────────────────────────────────

    void AnadirGraffiti(Transform edif, Bounds b)
    {
        string texto = TEXTOS_GRAFFITI[Random.Range(0, TEXTOS_GRAFFITI.Length)];
        bool frente  = Random.value > 0.5f;

        Vector3 pos;
        Quaternion rot;
        if (frente)
        {
            pos = new Vector3(b.center.x + Random.Range(-b.size.x * 0.2f, b.size.x * 0.2f),
                              b.min.y + Random.Range(0.8f, 2.2f), b.min.z - 0.02f);
            rot = Quaternion.Euler(0, 180, 0);
        }
        else
        {
            pos = new Vector3(b.min.x - 0.02f,
                              b.min.y + Random.Range(0.8f, 2.2f),
                              b.center.z + Random.Range(-b.size.z * 0.2f, b.size.z * 0.2f));
            rot = Quaternion.Euler(0, 90, 0);
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = $"Graffiti_{texto.Replace(" ", "_")}";
        go.transform.SetParent(edif);
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = new Vector3(Random.Range(1.2f, 2.5f), Random.Range(0.4f, 0.7f), 1f);

        Color col = Random.value < 0.4f ? new Color(0.85f, 0.05f, 0.05f, 0.9f)
                  : Random.value < 0.7f ? new Color(0.05f, 0.1f, 0.75f, 0.85f)
                                        : new Color(0.1f, 0.1f, 0.1f, 0.9f);
        var mat = new Material(Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color")) { color = col };
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Object.Destroy(go.GetComponent<Collider>());
        go.isStatic = true;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  COLOR ORTOFOTO
    // ─────────────────────────────────────────────────────────────────────

    void CargarOrtofoto()
    {
        // Cargar ortofoto REAL desde Resources o desde ruta directa
        _ortofoto = Resources.Load<Texture2D>("ortofoto_alsasua_REAL");
#if UNITY_EDITOR
        if (_ortofoto == null)
            _ortofoto = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/AlsasuaData/ortofoto_alsasua_REAL.png");
#endif
        // Bounds approx del mapa (Unity coords)
        _ortoRect = new Rect(GeoDataAlsasua.OX - 800f, GeoDataAlsasua.OZ - 800f, 1600f, 1600f);

        if (_ortofoto != null)
            AlsasuaLogger.Info("FachadasAAA", "Ortofoto cargada para sampling de color");
    }

    Color SampleColorOrtofoto(float worldX, float worldY, float worldZ)
    {
        if (_ortofoto == null)
            return new Color(0.78f, 0.74f, 0.68f);

        // Mapear Unity XZ → UV en la ortofoto
        float u = Mathf.InverseLerp(_ortoRect.xMin, _ortoRect.xMax, worldX);
        float v = Mathf.InverseLerp(_ortoRect.yMin, _ortoRect.yMax, worldZ);
        u = Mathf.Clamp01(u);
        v = Mathf.Clamp01(v);

        // GetPixelBilinear requiere que la textura sea Read/Write — usar fallback si no
        try
        {
            return _ortofoto.GetPixelBilinear(u, v);
        }
        catch
        {
            return new Color(0.78f, 0.74f, 0.68f);
        }
    }

    void AplicarTintFachada(MeshRenderer mr, Color colorBase, int id)
    {
        if (mr == null) return;

        // Perturbación Perlin ±8%
        float noise = (Mathf.PerlinNoise(id * 0.0013f, 0.5f) - 0.5f) * 0.16f;
        Color tint = new Color(
            Mathf.Clamp01(colorBase.r + noise),
            Mathf.Clamp01(colorBase.g + noise * 0.85f),
            Mathf.Clamp01(colorBase.b + noise * 0.70f));

        // Aplicar via MaterialPropertyBlock (sin instanciar material)
        var mpb = new MaterialPropertyBlock();
        mr.GetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", tint);
        mr.SetPropertyBlock(mpb);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────────────────

    ArquetipoVasco DeterminarArquetipo(Transform edif, Bounds bounds)
    {
        string n = edif.name.ToLower();
        if (n.Contains("bar") || n.Contains("taberna")) return ArquetipoVasco.Bar;
        if (n.Contains("commercial") || n.Contains("comercio") || n.Contains("shop"))
            return ArquetipoVasco.Comercio;
        if (n.Contains("church") || n.Contains("chapel") || n.Contains("eliza"))
            return ArquetipoVasco.Iglesia;
        if (n.Contains("industrial") || n.Contains("nave"))
            return ArquetipoVasco.NaveIndustrial;
        if (n.Contains("school") || n.Contains("ikastola"))
            return ArquetipoVasco.EquipamientoPublico;

        // Heurística por altura: >5 pisos → moderno
        int niveles = Mathf.RoundToInt(bounds.size.y / GeoDataAlsasua.ALT_PLANTA);
        if (niveles > 5) return ArquetipoVasco.ModernoPost1975;
        if (niveles > 3) return ArquetipoVasco.Bloque1940_1975;
        return ArquetipoVasco.UrbanoPre1940;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  MATERIALES
    // ─────────────────────────────────────────────────────────────────────

    void CrearMateriales()
    {
        var shaderHDRP  = Shader.Find("HDRP/Lit")   ?? Shader.Find("Standard");
        var shaderUnlit = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color");

        // Vidrio HDRP: Smoothness=0.92, IOR=1.52
        _matVidrio = new Material(shaderHDRP)
            { name = "M_Vidrio", color = new Color(0.6f, 0.8f, 0.9f, 0.22f) };
        _matVidrio.SetFloat("_Smoothness", 0.92f);
        _matVidrio.SetFloat("_Metallic",   0.02f);
        if (_matVidrio.HasProperty("_IOR")) _matVidrio.SetFloat("_IOR", 1.52f);
        if (_matVidrio.HasProperty("_Ior")) _matVidrio.SetFloat("_Ior", 1.52f);

        _matMarco = MatColor(shaderHDRP, new Color(0.22f, 0.19f, 0.16f), "M_Marco", 0.25f);
        _matBalcon = MatColor(shaderHDRP, new Color(0.30f, 0.28f, 0.26f), "M_Balcon", 0.45f);

        _matHierroNegro = new Material(shaderHDRP)
            { name = "M_HierroNegro", color = new Color(0.12f, 0.11f, 0.13f) };
        _matHierroNegro.SetFloat("_Smoothness", 0.55f);
        _matHierroNegro.SetFloat("_Metallic",   0.90f);

        _matCornisa  = MatColor(shaderHDRP, new Color(0.84f, 0.81f, 0.76f), "M_Cornisa", 0.38f);
        _matZocalo   = MatColor(shaderHDRP, new Color(0.48f, 0.44f, 0.40f), "M_Zocalo",  0.42f);
        _matPersiana = MatColor(shaderHDRP, new Color(0.50f, 0.50f, 0.52f), "M_Persiana",0.72f);
        _matRotulo   = MatColor(shaderHDRP, new Color(0.85f, 0.82f, 0.75f), "M_Rotulo",  0.35f);
        _matContenedor = MatColor(shaderHDRP, new Color(0.1f, 0.45f, 0.2f), "M_Contenedor", 0.45f);

        _matGraffiti = new Material(shaderUnlit ?? shaderHDRP)
            { name = "M_Graffiti", color = new Color(0.85f, 0.05f, 0.05f, 0.88f) };
    }

    Material MatColor(Shader shader, Color color, string nombre, float smoothness = 0.35f)
    {
        var m = new Material(shader) { name = nombre, color = color };
        m.SetFloat("_Smoothness", smoothness);
        m.enableInstancing = true;
        return m;
    }
}
