// Assets/Scripts/GeneradorInterioresSimples.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GENERADOR DE INTERIORES SIMPLES — LOD0 visibles a <28m del jugador
//
//  Genera un quad 2×2m con textura procedural + PointLight interior para
//  edificios cercanos que tengan ventanas o escaparates visibles.
//  ObjectPool para quads/luces — sin instanciar/destruir cada frame.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections.Generic;
using System.IO;
using UnityEngine.Rendering.HighDefinition;

[DefaultExecutionOrder(-48)]
public class GeneradorInterioresSimples : MonoBehaviour
{
    public static GeneradorInterioresSimples Instance { get; private set; }

    // ── Configuración ──────────────────────────────────────────────────────
    [Header("Distancias LOD")]
    public float radioActivacion    = 28f;
    public float radioDesactivacion = 35f;

    [Header("Textura procedural")]
    public int resolucionTextura = 512;

    [Header("Rutas")]
    public string rutaFacadeTextures = "Assets/AlsasuaData/FacadeTextures/Processed";

    // ── Estado interno ─────────────────────────────────────────────────────
    Transform _jugador;

    struct DatosEdificio
    {
        public GameObject go;
        public Vector3    posicion;
        public string     arquetipo;
        public string     edificioId;
    }

    readonly List<DatosEdificio> _edificios = new List<DatosEdificio>();

    // Interiores activos: clave = InstanceID del GO de edificio
    readonly Dictionary<int, InteriorActivo> _interioresActivos = new Dictionary<int, InteriorActivo>();

    struct InteriorActivo
    {
        public GameObject quadGO;
        public Light      luz;
    }

    // ── Object Pool ────────────────────────────────────────────────────────
    readonly Queue<GameObject> _poolQuads = new Queue<GameObject>();
    readonly Queue<Light>      _poolLuces = new Queue<Light>();

    // ── Caché de texturas procedurales (una por arquetipo) ─────────────────
    readonly Dictionary<string, Texture2D> _texturasArquetipo = new Dictionary<string, Texture2D>();

    Shader _shaderUnlit;
    const string SHADER_UNLIT = "HDRP/Unlit";

    // ═══════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ═══════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        _shaderUnlit = Shader.Find(SHADER_UNLIT) ?? Shader.Find("Unlit/Texture");

        var jugadorGO = GameObject.FindWithTag("Player");
        if (jugadorGO != null) _jugador = jugadorGO.transform;

        // Pre-generar texturas procedurales por arquetipo
        _texturasArquetipo["Bar"]         = GenerarTexturaBar();
        _texturasArquetipo["Comercio"]    = GenerarTexturaComercio();
        _texturasArquetipo["Residencial"] = GenerarTexturaResidencial();
        _texturasArquetipo["Industrial"]  = GenerarTexturaIndustrial();
        _texturasArquetipo["Default"]     = GenerarTexturaResidencial();

        CachearEdificios();
    }

    void Update()
    {
        // Localizar jugador si aún no se tiene referencia
        if (_jugador == null)
        {
            var go = GameObject.FindWithTag("Player");
            if (go != null) _jugador = go.transform;
            else return;
        }

        Vector3 posJugador = _jugador.position;

        for (int i = 0; i < _edificios.Count; i++)
        {
            var dato = _edificios[i];
            if (dato.go == null) continue;

            float dist = Vector3.Distance(posJugador, dato.posicion);
            int   id   = dato.go.GetInstanceID();

            if (dist < radioActivacion && !_interioresActivos.ContainsKey(id))
                CrearInterior(dato);
            else if (dist > radioDesactivacion && _interioresActivos.ContainsKey(id))
                DestruirInterior(id);
        }
    }

    void OnDestroy()
    {
        foreach (var interior in _interioresActivos.Values)
        {
            if (interior.quadGO != null) Destroy(interior.quadGO);
            if (interior.luz    != null) Destroy(interior.luz.gameObject);
        }
        _interioresActivos.Clear();

        foreach (var tex in _texturasArquetipo.Values)
            if (tex != null) Destroy(tex);
        _texturasArquetipo.Clear();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CACHEAR EDIFICIOS
    // ═══════════════════════════════════════════════════════════════════════

    void CachearEdificios()
    {
        _edificios.Clear();
        var raiz = GameObject.Find("Edificios_AAA");
        if (raiz == null)
        {
            Debug.LogWarning("[GeneradorInterioresSimples] No se encontró 'Edificios_AAA' en escena.");
            return;
        }

        foreach (Transform hijo in raiz.transform)
        {
            if (hijo == null) continue;
            _edificios.Add(new DatosEdificio
            {
                go         = hijo.gameObject,
                posicion   = hijo.position,
                arquetipo  = ExtraerArquetipo(hijo.name),
                edificioId = ExtraerEdificioId(hijo.name),
            });
        }

        Debug.Log($"[GeneradorInterioresSimples] {_edificios.Count} edificios cacheados.");
    }

    static string ExtraerArquetipo(string nombre)
    {
        string n = nombre.ToUpperInvariant();
        if (n.Contains("BAR") || n.Contains("TABERNA") || n.Contains("RESTAURANTE")) return "Bar";
        if (n.Contains("COMERCIO") || n.Contains("TIENDA") || n.Contains("TALLER"))  return "Comercio";
        if (n.Contains("INDUSTRIAL") || n.Contains("NAVE") || n.Contains("FABRICA")) return "Industrial";
        return "Residencial";
    }

    static string ExtraerEdificioId(string nombre)
    {
        // Formato esperado: "Edificio_297646225" o "Bar_297646225_..."
        var partes = nombre.Split('_');
        foreach (var p in partes)
            if (p.Length > 5 && long.TryParse(p, out _)) return p;
        return string.Empty;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CREAR INTERIOR
    // ═══════════════════════════════════════════════════════════════════════

    void CrearInterior(DatosEdificio dato)
    {
        Transform fachada = EncontrarFachada(dato.go.transform);
        if (fachada == null) return;

        // Quad a 0.8m dentro de la fachada, normal apuntando hacia afuera
        Vector3 normal  = fachada.forward;
        Vector3 posQuad = fachada.position - normal * 0.8f;
        posQuad.y       = dato.posicion.y + 1.5f;

        // ── Quad interior ─────────────────────────────────────────────────
        GameObject quadGO = ObtenerQuadPool();
        quadGO.transform.position   = posQuad;
        quadGO.transform.rotation   = Quaternion.LookRotation(normal);
        quadGO.transform.localScale = new Vector3(2f, 2f, 1f);
        quadGO.SetActive(true);

        // Textura real si existe, si no procedural
        Texture2D textura = CargarTexturaReal(dato.edificioId)
                         ?? ObtenerTexturaProcedural(dato.arquetipo);

        var rend = quadGO.GetComponent<Renderer>();
        if (rend != null)
        {
            var mat = new Material(_shaderUnlit != null ? _shaderUnlit : Shader.Find("Standard"));
            mat.mainTexture      = textura;
            mat.enableInstancing = true;
            rend.sharedMaterial  = mat;
        }

        // ── Luz interior ──────────────────────────────────────────────────
        Light luz = ObtenerLuzPool();
        luz.transform.position = posQuad - normal * 0.3f;
        luz.gameObject.SetActive(true);
        ConfigurarLuz(luz, dato.arquetipo);

        // ── Vidrio fachada ─────────────────────────────────────────────────
        AplicarVidrio(dato.go.transform);

        _interioresActivos[dato.go.GetInstanceID()] = new InteriorActivo
        {
            quadGO = quadGO,
            luz    = luz,
        };
    }

    Transform EncontrarFachada(Transform raiz)
    {
        foreach (Transform t in raiz.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToUpperInvariant();
            if (n.Contains("FACHADA") || n.Contains("ESCAPARATE") || n.Contains("FRENTE"))
                return t;
        }
        var renderers = raiz.GetComponentsInChildren<Renderer>();
        return renderers.Length > 0 ? renderers[0].transform : null;
    }

    static void ConfigurarLuz(Light luz, string arquetipo)
    {
        luz.type    = LightType.Point;
        luz.shadows = LightShadows.None;

        var hd = luz.GetComponent<HDAdditionalLightData>();

        switch (arquetipo)
        {
            case "Bar":
                luz.color  = ColorTemperatura(2700f);
                luz.range  = 8f;
                if (hd != null) { hd.intensity = 2500f; hd.lightUnit = LightUnit.Lux; }
                break;
            case "Comercio":
                luz.color  = ColorTemperatura(4000f);
                luz.range  = 6f;
                if (hd != null) { hd.intensity = 1800f; hd.lightUnit = LightUnit.Lux; }
                break;
            case "Industrial":
                luz.color  = ColorTemperatura(5000f);
                luz.range  = 4f;
                if (hd != null) { hd.intensity = 800f; hd.lightUnit = LightUnit.Lux; }
                break;
            default: // Residencial
                luz.color  = ColorTemperatura(3200f);
                luz.range  = 5f;
                if (hd != null) { hd.intensity = 1200f; hd.lightUnit = LightUnit.Lux; }
                break;
        }
    }

    // Aproximación temperatura de color Kelvin → RGB
    static Color ColorTemperatura(float kelvin)
    {
        float t = Mathf.InverseLerp(2000f, 6500f, kelvin);
        return new Color(
            Mathf.Lerp(1.00f, 0.85f, t),
            Mathf.Lerp(0.72f, 0.93f, t),
            Mathf.Lerp(0.40f, 1.00f, t)
        );
    }

    static void AplicarVidrio(Transform raiz)
    {
        foreach (var rend in raiz.GetComponentsInChildren<Renderer>(true))
        {
            string n = rend.name.ToUpperInvariant();
            if (!n.Contains("ESCAPARATE") && !n.Contains("VIDRIO")) continue;

            foreach (var mat in rend.materials)
            {
                if (mat.HasProperty("_Smoothness"))
                    mat.SetFloat("_Smoothness", 0.92f);
                if (mat.HasProperty("_TransmittanceColorFactor"))
                    mat.SetFloat("_TransmittanceColorFactor", 0.85f);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DESTRUIR INTERIOR (devolver al pool)
    // ═══════════════════════════════════════════════════════════════════════

    void DestruirInterior(int edificioId)
    {
        if (!_interioresActivos.TryGetValue(edificioId, out var interior)) return;

        if (interior.quadGO != null)
        {
            interior.quadGO.SetActive(false);
            _poolQuads.Enqueue(interior.quadGO);
        }
        if (interior.luz != null)
        {
            interior.luz.gameObject.SetActive(false);
            _poolLuces.Enqueue(interior.luz);
        }

        _interioresActivos.Remove(edificioId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  OBJECT POOL
    // ═══════════════════════════════════════════════════════════════════════

    GameObject ObtenerQuadPool()
    {
        if (_poolQuads.Count > 0) return _poolQuads.Dequeue();

        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Interior_Quad";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform);
        return go;
    }

    Light ObtenerLuzPool()
    {
        if (_poolLuces.Count > 0) return _poolLuces.Dequeue();

        var go = new GameObject("Interior_Luz");
        go.transform.SetParent(transform);
        var l = go.AddComponent<Light>();
        go.AddComponent<HDAdditionalLightData>();
        return l;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TEXTURAS PROCEDURALES
    // ═══════════════════════════════════════════════════════════════════════

    Texture2D ObtenerTexturaProcedural(string arquetipo)
    {
        return _texturasArquetipo.TryGetValue(arquetipo, out var t) ? t : _texturasArquetipo["Default"];
    }

    Texture2D GenerarTexturaBar()
    {
        int res = resolucionTextura;
        var tex = new Texture2D(res, res, TextureFormat.RGB24, false);
        RellenarColor(tex, HexColor("#2a1a0e"));

        // Mostrador
        DibujarRect(tex, 0, Mathf.RoundToInt(res * 0.28f), res, Mathf.RoundToInt(res * 0.04f), HexColor("#5a3010"));

        // Botellas: rectángulos verticales alternos
        Color[] colorBotellas = { HexColor("#4a2e10"), HexColor("#1a0a04") };
        for (int i = 0; i < 12; i++)
        {
            int bx = Mathf.RoundToInt(res * (0.05f + i * 0.08f));
            int by = Mathf.RoundToInt(res * 0.30f);
            int bw = Mathf.RoundToInt(res * 0.04f);
            int bh = Mathf.RoundToInt(res * 0.45f);
            DibujarRect(tex, bx, by, bw, bh, colorBotellas[i % 2]);
        }

        tex.Apply();
        return tex;
    }

    Texture2D GenerarTexturaComercio()
    {
        int res = resolucionTextura;
        var tex = new Texture2D(res, res, TextureFormat.RGB24, false);
        RellenarColor(tex, HexColor("#f0f0f0"));

        // Estantes horizontales
        for (int i = 0; i < 5; i++)
        {
            int ey = Mathf.RoundToInt(res * (0.15f + i * 0.15f));
            DibujarRect(tex, 0, ey, res, Mathf.RoundToInt(res * 0.012f), HexColor("#cccccc"));
        }
        // Separadores verticales
        for (int i = 0; i < 4; i++)
        {
            int vx = Mathf.RoundToInt(res * (0.2f + i * 0.2f));
            DibujarRect(tex, vx, 0, Mathf.RoundToInt(res * 0.008f), res, HexColor("#e0e0e0"));
        }
        // Productos en estantes
        Color[] productos = { HexColor("#e74c3c"), HexColor("#3498db"), HexColor("#2ecc71"), HexColor("#f39c12") };
        int idx = 0;
        for (int fi = 0; fi < 5; fi++)
        {
            int py = Mathf.RoundToInt(res * (0.15f + fi * 0.15f)) + Mathf.RoundToInt(res * 0.015f);
            for (int ci = 0; ci < 8; ci++)
            {
                int px = Mathf.RoundToInt(res * (0.04f + ci * 0.12f));
                DibujarRect(tex, px, py, Mathf.RoundToInt(res * 0.07f), Mathf.RoundToInt(res * 0.09f), productos[idx++ % productos.Length]);
            }
        }

        tex.Apply();
        return tex;
    }

    Texture2D GenerarTexturaResidencial()
    {
        int res = resolucionTextura;
        var tex = new Texture2D(res, res, TextureFormat.RGB24, false);
        RellenarColor(tex, HexColor("#f5f0e0"));

        // Sofá
        DibujarRect(tex, Mathf.RoundToInt(res * 0.10f), Mathf.RoundToInt(res * 0.15f),
                    Mathf.RoundToInt(res * 0.60f), Mathf.RoundToInt(res * 0.25f), HexColor("#c8a87a"));
        // Respaldo sofá
        DibujarRect(tex, Mathf.RoundToInt(res * 0.10f), Mathf.RoundToInt(res * 0.38f),
                    Mathf.RoundToInt(res * 0.60f), Mathf.RoundToInt(res * 0.08f), HexColor("#b89060"));
        // Mesa baja
        DibujarRect(tex, Mathf.RoundToInt(res * 0.15f), Mathf.RoundToInt(res * 0.05f),
                    Mathf.RoundToInt(res * 0.45f), Mathf.RoundToInt(res * 0.08f), HexColor("#8b6914"));
        // Pantalla lámpara
        DibujarRect(tex, Mathf.RoundToInt(res * 0.75f), Mathf.RoundToInt(res * 0.60f),
                    Mathf.RoundToInt(res * 0.12f), Mathf.RoundToInt(res * 0.12f), HexColor("#fff0c0"));
        // Pie lámpara
        DibujarRect(tex, Mathf.RoundToInt(res * 0.80f), Mathf.RoundToInt(res * 0.05f),
                    Mathf.RoundToInt(res * 0.02f), Mathf.RoundToInt(res * 0.58f), HexColor("#888888"));

        tex.Apply();
        return tex;
    }

    Texture2D GenerarTexturaIndustrial()
    {
        int res = resolucionTextura;
        var tex = new Texture2D(res, res, TextureFormat.RGB24, false);
        RellenarColor(tex, HexColor("#303030"));
        tex.Apply();
        return tex;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    static void RellenarColor(Texture2D tex, Color c)
    {
        var pixels = new Color[tex.width * tex.height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
        tex.SetPixels(pixels);
    }

    static void DibujarRect(Texture2D tex, int x, int y, int w, int h, Color c)
    {
        x = Mathf.Clamp(x, 0, tex.width  - 1);
        y = Mathf.Clamp(y, 0, tex.height - 1);
        w = Mathf.Clamp(w, 1, tex.width  - x);
        h = Mathf.Clamp(h, 1, tex.height - y);
        for (int py = y; py < y + h; py++)
            for (int px = x; px < x + w; px++)
                tex.SetPixel(px, py, c);
    }

    static Color HexColor(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CARGA TEXTURA REAL DESDE DISCO
    // ═══════════════════════════════════════════════════════════════════════

    Texture2D CargarTexturaReal(string edificioId)
    {
        if (string.IsNullOrEmpty(edificioId)) return null;

        // Ruta relativa al directorio raíz del proyecto (junto a Assets/)
        string proyectoRaiz = Application.dataPath.Replace("/Assets", "").Replace("\\Assets", "");
        string dirPath      = Path.Combine(proyectoRaiz, rutaFacadeTextures.Replace("/", Path.DirectorySeparatorChar.ToString()));

        if (!Directory.Exists(dirPath)) return null;

        string[] archivos = Directory.GetFiles(dirPath, $"*{edificioId}*", SearchOption.TopDirectoryOnly);
        if (archivos.Length == 0) return null;

        byte[] bytes = File.ReadAllBytes(archivos[0]);
        var tex = new Texture2D(2, 2);
        if (tex.LoadImage(bytes)) return tex;

        Destroy(tex);
        return null;
    }
}
