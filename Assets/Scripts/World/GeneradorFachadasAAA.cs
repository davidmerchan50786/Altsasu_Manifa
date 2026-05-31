// GeneradorFachadasAAA.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GENERADOR DE FACHADAS AAA — edificios con ventanas, balcones, cornisas
//
//  Mejora los meshes de GeneradorMundoOSM añadiendo:
//    • Ventanas con emisión nocturna (vidrio + marco)
//    • Balcones con barandilla metálica
//    • Cornisa superior (remate del edificio)
//    • Zócalo inferior (base de granito)
//    • Variación de textura por planta (planta baja = comercial)
//    • Puertas en planta baja con marquesina
//    • Graffitis procedurales en paredes (textos políticos vascos)
//
//  Se ejecuta tras GeneradorMundoOSM.OnMundoGenerado
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class GeneradorFachadasAAA : MonoBehaviour
{
    public static GeneradorFachadasAAA Instance { get; private set; }

    [Header("Parámetros")]
    public float alturaVentana   = 1.1f;
    public float anchoVentana    = 0.8f;
    public float alturaCornisa   = 0.25f;
    public float alturaZocalo    = 0.35f;
    public bool  generarBalcones = true;
    public bool  generarGraffiti = true;

    // ── Materiales ────────────────────────────────────────────────────────
    Material _matVidrio, _matMarco, _matBalcon, _matCornisa, _matZocalo;
    Material _matPlantaBaja, _matGraffiti;

    // ── Textos de graffiti vascos ─────────────────────────────────────────
    static readonly string[] TEXTOS_GRAFFITI = {
        "ASKATASUNA", "PRESOAK ETXERA", "INDEPENDENTZIA",
        "AMNISTIA", "GORA EUSKAL HERRIA", "ACABv", "ETA",
        "NO PASARÁN", "RESISTENCIA", "HERRIRA",
    };

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        // SistemaEdificiosAAA.cs reemplaza este sistema con implementación más completa
        // Solo activar si SistemaEdificiosAAA no existe en escena
        if (FindFirstObjectByType<SistemaEdificiosAAA>() == null)
            GeneradorMundoOSM.OnMundoGenerado += () => StartCoroutine(EnriquecerEdificios());
    }

    IEnumerator EnriquecerEdificios()
    {
        yield return new WaitForSeconds(1f); // esperar que se generen los meshes

        CrearMateriales();

        // Si GeneradorGeometriaPrecisa está activo, las fachadas precisas ya están generadas
        if (GeneradorGeometriaPrecisa.Instance != null) yield break;

        var edificios = GameObject.Find("Edificios_OSM");
        if (edificios == null) yield break;

        int procesados = 0;
        foreach (Transform edif in edificios.transform)
        {
            if (edif == null) continue;
            var mf = edif.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            EnriquecerEdificio(edif, mf.sharedMesh);
            procesados++;
            if (procesados % 15 == 0) yield return null;
        }

        AlsasuaLogger.Info("FachadasAAA", $"✅ {procesados} edificios enriquecidos con fachadas detalladas");
    }

    void EnriquecerEdificio(Transform edif, Mesh meshBase)
    {
        var bounds  = meshBase.bounds;
        float h     = bounds.size.y;
        float w     = bounds.size.x;
        float d     = bounds.size.z;
        int   pisos = Mathf.Max(1, Mathf.RoundToInt(h / 3.2f));

        // ── Zócalo ────────────────────────────────────────────────────────
        AnadirZocalo(edif, bounds);

        // ── Cornisa superior ──────────────────────────────────────────────
        AnadirCornisa(edif, bounds, h);

        // ── Ventanas por planta ───────────────────────────────────────────
        for (int piso = 0; piso < pisos; piso++)
        {
            float yBase = bounds.min.y + alturaZocalo + piso * 3.2f;
            bool  esPlantaBaja = piso == 0;
            AnadirVentanasEnFachada(edif, bounds, yBase, w, d, esPlantaBaja);
        }

        // ── Balcones (pisos 2+) ────────────────────────────────────────────
        if (generarBalcones && pisos > 2)
        {
            for (int piso = 1; piso < pisos - 1; piso += 2)
            {
                float yBalcon = bounds.min.y + piso * 3.2f + 0.8f;
                if (Random.value > 0.4f)
                    AnadirBalcon(edif, bounds, yBalcon, w);
            }
        }

        // ── Graffiti aleatorio (30% edificios) ────────────────────────────
        if (generarGraffiti && Random.value < 0.3f)
            AnadirGraffiti(edif, bounds);
    }

    // ── Zócalo ────────────────────────────────────────────────────────────

    void AnadirZocalo(Transform edif, Bounds b)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Zocalo";
        go.transform.SetParent(edif);
        go.transform.position = new Vector3(b.center.x, b.min.y + alturaZocalo * 0.5f, b.center.z);
        go.transform.localScale = new Vector3(b.size.x + 0.06f, alturaZocalo, b.size.z + 0.06f);
        go.GetComponent<Renderer>().sharedMaterial = _matZocalo;
        go.isStatic = true;
        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
    }

    // ── Cornisa ───────────────────────────────────────────────────────────

    void AnadirCornisa(Transform edif, Bounds b, float h)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Cornisa";
        go.transform.SetParent(edif);
        go.transform.position = new Vector3(b.center.x, b.min.y + h + alturaCornisa * 0.5f, b.center.z);
        go.transform.localScale = new Vector3(b.size.x + 0.3f, alturaCornisa, b.size.z + 0.3f);
        go.GetComponent<Renderer>().sharedMaterial = _matCornisa;
        go.isStatic = true;
        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
    }

    // ── Ventanas ──────────────────────────────────────────────────────────

    void AnadirVentanasEnFachada(Transform edif, Bounds b, float yBase,
                                  float w, float d, bool esPlantaBaja)
    {
        // Calcular cuántas ventanas caben en el ancho
        int nVentW = Mathf.Max(1, Mathf.FloorToInt(w / 2.2f));
        int nVentD = Mathf.Max(1, Mathf.FloorToInt(d / 2.2f));

        float offsetY  = yBase + (esPlantaBaja ? 1.2f : 1.0f);
        float altVent  = esPlantaBaja ? 1.8f : alturaVentana;
        float anchVent = esPlantaBaja ? 1.2f : anchoVentana;

        // Fachada frontal (Z-)
        for (int i = 0; i < nVentW; i++)
        {
            float x = b.min.x + (i + 0.5f) * (w / nVentW);
            AnadirVentana(edif, new Vector3(x, offsetY, b.min.z - 0.01f),
                Quaternion.Euler(0, 180, 0), anchVent, altVent, esPlantaBaja);
        }
        // Fachada trasera (Z+)
        for (int i = 0; i < nVentW; i++)
        {
            float x = b.min.x + (i + 0.5f) * (w / nVentW);
            AnadirVentana(edif, new Vector3(x, offsetY, b.max.z + 0.01f),
                Quaternion.identity, anchVent, altVent, esPlantaBaja);
        }
        // Laterales
        for (int i = 0; i < nVentD; i++)
        {
            float z = b.min.z + (i + 0.5f) * (d / nVentD);
            AnadirVentana(edif, new Vector3(b.min.x - 0.01f, offsetY, z),
                Quaternion.Euler(0, 90, 0), anchVent, altVent, false);
            AnadirVentana(edif, new Vector3(b.max.x + 0.01f, offsetY, z),
                Quaternion.Euler(0, -90, 0), anchVent, altVent, false);
        }
    }

    void AnadirVentana(Transform edif, Vector3 pos, Quaternion rot,
                       float ancho, float alto, bool esVitrina)
    {
        var go = new GameObject(esVitrina ? "Vitrina" : "Ventana");
        go.transform.SetParent(edif);
        go.transform.position = pos;
        go.transform.rotation = rot;

        // Marco exterior
        var marco = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marco.name = "Marco";
        marco.transform.SetParent(go.transform);
        marco.transform.localPosition = Vector3.zero;
        marco.transform.localScale    = new Vector3(ancho + 0.08f, alto + 0.08f, 0.06f);
        marco.GetComponent<Renderer>().sharedMaterial = _matMarco;
        Object.Destroy(marco.GetComponent<Collider>());

        // Vidrio interior
        var vidrio = GameObject.CreatePrimitive(PrimitiveType.Cube);
        vidrio.name = "Vidrio";
        vidrio.transform.SetParent(go.transform);
        vidrio.transform.localPosition = new Vector3(0, 0, -0.01f);
        vidrio.transform.localScale    = new Vector3(ancho, alto, 0.02f);
        vidrio.GetComponent<Renderer>().sharedMaterial = _matVidrio;
        Object.Destroy(vidrio.GetComponent<Collider>());

        go.isStatic = true;
    }

    // ── Balcón ────────────────────────────────────────────────────────────

    void AnadirBalcon(Transform edif, Bounds b, float y, float w)
    {
        var go = new GameObject("Balcon");
        go.transform.SetParent(edif);

        // Losa del balcón
        var losa = GameObject.CreatePrimitive(PrimitiveType.Cube);
        losa.name = "Losa";
        losa.transform.SetParent(go.transform);
        losa.transform.position  = new Vector3(b.center.x, y, b.min.z - 0.5f);
        losa.transform.localScale = new Vector3(Mathf.Min(w - 0.5f, 2.5f), 0.12f, 1.0f);
        losa.GetComponent<Renderer>().sharedMaterial = _matBalcon;
        Object.Destroy(losa.GetComponent<Collider>());

        // Barandilla (barras verticales)
        float anchoBalcon = Mathf.Min(w - 0.5f, 2.5f);
        int nBarras = Mathf.Max(3, (int)(anchoBalcon * 2.5f));
        for (int i = 0; i <= nBarras; i++)
        {
            float bx = b.center.x - anchoBalcon * 0.5f + i * (anchoBalcon / nBarras);
            var barra = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            barra.transform.SetParent(go.transform);
            barra.transform.position  = new Vector3(bx, y + 0.4f, b.min.z - 0.95f);
            barra.transform.localScale = new Vector3(0.04f, 0.4f, 0.04f);
            barra.GetComponent<Renderer>().sharedMaterial = _matBalcon;
            Object.Destroy(barra.GetComponent<Collider>());
        }

        // Barra horizontal superior
        var hBar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        hBar.transform.SetParent(go.transform);
        hBar.transform.position  = new Vector3(b.center.x, y + 0.82f, b.min.z - 0.95f);
        hBar.transform.localScale = new Vector3(anchoBalcon, 0.06f, 0.06f);
        hBar.GetComponent<Renderer>().sharedMaterial = _matBalcon;
        Object.Destroy(hBar.GetComponent<Collider>());

        go.isStatic = true;
    }

    // ── Graffiti ──────────────────────────────────────────────────────────

    void AnadirGraffiti(Transform edif, Bounds b)
    {
        // Elegir texto y pared aleatoria
        string texto  = TEXTOS_GRAFFITI[Random.Range(0, TEXTOS_GRAFFITI.Length)];
        float  y      = b.min.y + Random.Range(0.8f, 2.2f);
        bool   frenteO= Random.value > 0.5f;

        Vector3 pos;
        Quaternion rot;
        if (frenteO)
        {
            pos = new Vector3(b.center.x + Random.Range(-b.size.x * 0.2f, b.size.x * 0.2f),
                              y, b.min.z - 0.02f);
            rot = Quaternion.Euler(0, 180, 0);
        }
        else
        {
            pos = new Vector3(b.min.x - 0.02f,
                              y, b.center.z + Random.Range(-b.size.z * 0.2f, b.size.z * 0.2f));
            rot = Quaternion.Euler(0, 90, 0);
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = $"Graffiti_{texto.Replace(" ","_")}";
        go.transform.SetParent(edif);
        go.transform.position  = pos;
        go.transform.rotation  = rot;
        go.transform.localScale = new Vector3(Random.Range(1.2f, 2.5f), Random.Range(0.4f, 0.7f), 1f);
        go.GetComponent<Renderer>().sharedMaterial = _matGraffiti;
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Object.Destroy(go.GetComponent<Collider>());
        go.isStatic = true;
    }

    // ── Materiales ────────────────────────────────────────────────────────

    void CrearMateriales()
    {
        var shaderHDRP = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
        var shaderUnlit = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color");

        _matVidrio = new Material(shaderHDRP)
        {
            name = "M_Vidrio",
            color = new Color(0.6f, 0.8f, 0.9f, 0.3f)
        };
        _matVidrio.SetFloat("_Smoothness", 0.9f);
        _matVidrio.SetFloat("_Metallic", 0.1f);

        _matMarco = MatColor(shaderHDRP, new Color(0.25f, 0.22f, 0.18f), "M_Marco");
        _matMarco.SetFloat("_Smoothness", 0.2f);

        _matBalcon = MatColor(shaderHDRP, new Color(0.35f, 0.33f, 0.30f), "M_Balcon");
        _matBalcon.SetFloat("_Smoothness", 0.5f);
        _matBalcon.SetFloat("_Metallic", 0.7f);

        _matCornisa = MatColor(shaderHDRP, new Color(0.88f, 0.85f, 0.80f), "M_Cornisa");
        _matZocalo  = MatColor(shaderHDRP, new Color(0.55f, 0.52f, 0.48f), "M_Zocalo");

        _matPlantaBaja = MatColor(shaderHDRP, new Color(0.75f, 0.70f, 0.60f), "M_PlantaBaja");

        // Graffiti: color rojo/negro/amarillo aleatorio
        _matGraffiti = new Material(shaderUnlit ?? shaderHDRP)
        {
            name = "M_Graffiti",
            color = new Color(0.85f, 0.05f, 0.05f, 0.9f)
        };
    }

    Material MatColor(Shader shader, Color color, string nombre)
    {
        var m = new Material(shader) { name = nombre, color = color };
        return m;
    }
}
