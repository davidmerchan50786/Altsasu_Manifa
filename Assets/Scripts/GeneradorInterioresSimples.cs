// Assets/Scripts/GeneradorInterioresSimples.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GENERADOR DE INTERIORES SIMPLES — LOD0 (<28m del jugador)
//
//  Genera quads 2×2m con luz interior para edificios cercanos.
//  ObjectPool para quads y PointLights — sin GC en Update.
//  Arquetipo detectado desde nombre del GameObject.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections.Generic;
using System.IO;

[DefaultExecutionOrder(-48)]
public class GeneradorInterioresSimples : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────
    public static GeneradorInterioresSimples Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────
    [Header("Distancias LOD")]
    [Tooltip("Radio de activación de interiores")]
    public float radioActivacion  = 28f;
    [Tooltip("Radio de desactivación (histéresis)")]
    public float radioDesactivacion = 35f;

    [Header("Pool sizes")]
    public int poolInicialQuads  = 20;
    public int poolInicialLuces  = 20;

    [Header("Textura fallback procesada")]
    [Tooltip("Directorio con texturas procesadas por edificio_id")]
    public string dirTexturasProcesadas =
        "Assets/AlsasuaData/FacadeTextures/Processed";

    // ── Tipos internos ────────────────────────────────────────────────────
    enum Arquetipo { Bar, Comercio, Residencial, Industrial }

    class DatosInterior
    {
        public GameObject quad;
        public Light      luz;
        public bool       activo;
    }

    // ── Estado interno ────────────────────────────────────────────────────
    Transform                         _jugador;
    List<GameObject>                  _edificios    = new List<GameObject>(256);
    Dictionary<GameObject, DatosInterior> _interiores = new Dictionary<GameObject, DatosInterior>(64);

    // Pools
    Queue<GameObject> _poolQuads  = new Queue<GameObject>();
    Queue<Light>      _poolLuces  = new Queue<Light>();

    // Cache de texturas generadas por arquetipo (evita recrearlas cada vez)
    Dictionary<Arquetipo, Texture2D> _texturasCache = new Dictionary<Arquetipo, Texture2D>();

    // ── Lifecycle ─────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        // Buscar jugador
        GameObject jugadorGO = GameObject.FindWithTag("Player");
        if (jugadorGO != null) _jugador = jugadorGO.transform;

        // Cachear todos los edificios bajo "Edificios_AAA"
        GameObject raizEdificios = GameObject.Find("Edificios_AAA");
        if (raizEdificios != null)
        {
            foreach (Transform hijo in raizEdificios.transform)
                _edificios.Add(hijo.gameObject);
        }

        // Pre-llenar pools
        for (int i = 0; i < poolInicialQuads; i++)
            _poolQuads.Enqueue(CrearQuadPool());

        for (int i = 0; i < poolInicialLuces; i++)
            _poolLuces.Enqueue(CrearLuzPool());

        // Pre-generar texturas de arquetipo
        GenerarTexturasCacheadas();
    }

    void Update()
    {
        if (_jugador == null) return;

        Vector3 posJugador = _jugador.position;

        for (int i = 0; i < _edificios.Count; i++)
        {
            GameObject edificio = _edificios[i];
            if (edificio == null) continue;

            float distSq = (edificio.transform.position - posJugador).sqrMagnitude;

            bool tieneInterior = _interiores.TryGetValue(edificio, out DatosInterior datos);

            if (!tieneInterior || !datos.activo)
            {
                if (distSq < radioActivacion * radioActivacion)
                    CrearInterior(edificio);
            }
            else
            {
                if (distSq > radioDesactivacion * radioDesactivacion)
                    DestruirInterior(edificio);
            }
        }
    }

    void OnDestroy()
    {
        // Limpiar texturas generadas proceduralmente
        foreach (var tex in _texturasCache.Values)
            if (tex != null) Destroy(tex);
    }

    // ── Crear / Destruir interiores ────────────────────────────────────────

    void CrearInterior(GameObject edificio)
    {
        // Detectar arquetipo
        Arquetipo arquetipo = DetectarArquetipo(edificio.name);

        // Determinar fachada principal (buscar renderer de escaparate/vidrio)
        Vector3 posQuad    = ObtenerPosicionInterior(edificio);
        Vector3 normalFachada = -edificio.transform.forward; // Normal hacia afuera

        // --- Quad interior ---
        GameObject quad = ObtenerQuadPool();
        quad.transform.position   = posQuad;
        quad.transform.rotation   = Quaternion.LookRotation(-normalFachada, Vector3.up);
        quad.transform.localScale = new Vector3(2f, 2f, 1f);
        quad.transform.SetParent(edificio.transform, true);

        // Textura: intentar textura procesada real primero
        Texture2D tex = CargarTexturaReal(edificio.name);
        if (tex == null)
            _texturasCache.TryGetValue(arquetipo, out tex);

        Renderer rend = quad.GetComponent<Renderer>();
        if (rend != null && tex != null)
            rend.material.mainTexture = tex;

        quad.SetActive(true);

        // --- Luz puntual ---
        Light luz          = ObtenerLuzPool();
        luz.transform.position = posQuad + normalFachada * 0.5f;
        luz.transform.SetParent(edificio.transform, true);
        ConfigurarLuz(luz, arquetipo);
        luz.gameObject.SetActive(true);

        // --- Vidrio fachada (smoothness/transmitancia) ---
        AplicarVidrio(edificio);

        // Registrar
        var datos = new DatosInterior { quad = quad, luz = luz, activo = true };
        _interiores[edificio] = datos;
    }

    void DestruirInterior(GameObject edificio)
    {
        if (!_interiores.TryGetValue(edificio, out DatosInterior datos)) return;

        // Devolver al pool
        if (datos.quad != null)
        {
            datos.quad.transform.SetParent(transform, true);
            datos.quad.SetActive(false);
            _poolQuads.Enqueue(datos.quad);
        }

        if (datos.luz != null)
        {
            datos.luz.transform.SetParent(transform, true);
            datos.luz.gameObject.SetActive(false);
            _poolLuces.Enqueue(datos.luz);
        }

        datos.activo = false;
    }

    // ── Detección de arquetipo ─────────────────────────────────────────────

    Arquetipo DetectarArquetipo(string nombre)
    {
        string n = nombre.ToLowerInvariant();
        if (n.Contains("bar") || n.Contains("taberna") || n.Contains("jatetxe") ||
            n.Contains("restaurante") || n.Contains("cafe"))
            return Arquetipo.Bar;
        if (n.Contains("comercio") || n.Contains("denda") || n.Contains("tienda") ||
            n.Contains("supermercado") || n.Contains("farmacia"))
            return Arquetipo.Comercio;
        if (n.Contains("industrial") || n.Contains("nave") || n.Contains("fabrika") ||
            n.Contains("almacen"))
            return Arquetipo.Industrial;
        return Arquetipo.Residencial;
    }

    // ── Posición interior (0.8m dentro de la fachada) ─────────────────────

    Vector3 ObtenerPosicionInterior(GameObject edificio)
    {
        // Centro del edificio desplazado hacia dentro 0.8m desde la fachada principal
        Vector3 centro = edificio.transform.position;
        // La fachada principal asume que -forward es el frente
        Vector3 dentro = edificio.transform.forward * 0.8f;
        // Altura: 1.5m sobre la base del edificio
        return new Vector3(centro.x + dentro.x, centro.y + 1.5f, centro.z + dentro.z);
    }

    // ── Vidrio ─────────────────────────────────────────────────────────────

    void AplicarVidrio(GameObject edificio)
    {
        Renderer[] renderers = edificio.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            string n = r.name.ToLowerInvariant();
            if (n.Contains("escaparate") || n.Contains("vidrio") || n.Contains("cristal"))
            {
                foreach (Material mat in r.materials)
                {
                    if (mat.HasProperty("_Smoothness"))
                        mat.SetFloat("_Smoothness", 0.92f);
                    if (mat.HasProperty("_TransmittanceColorFactor"))
                        mat.SetFloat("_TransmittanceColorFactor", 0.85f);
                }
            }
        }
    }

    // ── Luz por arquetipo ──────────────────────────────────────────────────

    void ConfigurarLuz(Light luz, Arquetipo arquetipo)
    {
        luz.type = LightType.Point;
        switch (arquetipo)
        {
            case Arquetipo.Bar:
                luz.color       = ColorDesdeKelvin(2700);
                luz.intensity   = 2.5f;
                luz.range       = 8f;
                break;
            case Arquetipo.Comercio:
                luz.color       = ColorDesdeKelvin(4000);
                luz.intensity   = 1.8f;
                luz.range       = 6f;
                break;
            case Arquetipo.Industrial:
                luz.color       = ColorDesdeKelvin(5000);
                luz.intensity   = 1.0f;
                luz.range       = 6f;
                break;
            default: // Residencial
                luz.color       = ColorDesdeKelvin(3200);
                luz.intensity   = 1.2f;
                luz.range       = 5f;
                break;
        }
        luz.shadows = LightShadows.None; // sin sombras para LOD de interiores
    }

    // Aproximación perceptual de temperatura de color (Kelvin → RGB lineal)
    Color ColorDesdeKelvin(int kelvin)
    {
        float t = kelvin / 6500f;
        float r = Mathf.Clamp01(1.0f);
        float g = Mathf.Clamp01(0.5f + 0.5f * t);
        float b = Mathf.Clamp01(t * 1.1f - 0.1f);
        return new Color(r, g, b);
    }

    // ── Generación de texturas procedurales ───────────────────────────────

    void GenerarTexturasCacheadas()
    {
        _texturasCache[Arquetipo.Bar]         = GenerarTexturaBar();
        _texturasCache[Arquetipo.Comercio]    = GenerarTexturaComercio();
        _texturasCache[Arquetipo.Residencial] = GenerarTexturaResidencial();
        _texturasCache[Arquetipo.Industrial]  = GenerarTexturaIndustrial();
    }

    // Bar: fondo marrón oscuro + rectángulos simulando botellas
    Texture2D GenerarTexturaBar()
    {
        int sz = 512;
        Texture2D tex = new Texture2D(sz, sz, TextureFormat.RGB24, false);
        Color fondoBar = HexAColor("#2a1a0e");
        Color botella  = HexAColor("#1a3320");
        Color etiqueta = HexAColor("#c8a040");

        // Fondo
        RellenarTextura(tex, fondoBar);

        // Botellas (rectángulos verticales)
        int[] posX = { 60, 140, 220, 300, 380, 450 };
        foreach (int px in posX)
        {
            // Cuerpo
            DibujarRect(tex, px, 120, 22, 110, botella);
            // Cuello
            DibujarRect(tex, px + 5, 230, 12, 40, botella);
            // Etiqueta
            DibujarRect(tex, px + 2, 150, 18, 50, etiqueta);
        }

        // Barra horizontal oscura
        DibujarRect(tex, 0, 80, sz, 30, HexAColor("#1a0e06"));

        tex.Apply();
        return tex;
    }

    // Comercio: fondo blanco + líneas verticales simulando estantes
    Texture2D GenerarTexturaComercio()
    {
        int sz = 512;
        Texture2D tex = new Texture2D(sz, sz, TextureFormat.RGB24, false);
        Color fondo    = HexAColor("#f0f0f0");
        Color estante  = HexAColor("#c8b090");
        Color producto = HexAColor("#e04030");

        RellenarTextura(tex, fondo);

        // Estantes horizontales
        int[] alturas = { 80, 180, 280, 380 };
        foreach (int y in alturas)
        {
            DibujarRect(tex, 0, y, sz, 8, estante);
            // Productos encima
            for (int x = 20; x < sz - 30; x += 40)
                DibujarRect(tex, x, y + 8, 25, 50, ColorAleatorio(x + y));
        }

        tex.Apply();
        return tex;
    }

    // Residencial: blanco cálido + silueta simple de muebles
    Texture2D GenerarTexturaResidencial()
    {
        int sz = 512;
        Texture2D tex = new Texture2D(sz, sz, TextureFormat.RGB24, false);
        Color fondo  = HexAColor("#f5f0e0");
        Color mueble = HexAColor("#8b6940");
        Color pared  = HexAColor("#e8e0d0");

        RellenarTextura(tex, fondo);

        // Pared del fondo más oscura
        DibujarRect(tex, 0, 0, sz, sz / 3, pared);

        // Sofá (silueta simple)
        DibujarRect(tex, 80,  100, 350, 80, mueble);  // base sofá
        DibujarRect(tex, 80,  180, 30,  60, mueble);  // brazo izq
        DibujarRect(tex, 400, 180, 30,  60, mueble);  // brazo der
        DibujarRect(tex, 110, 180, 290, 40, HexAColor("#7a5c30")); // respaldo

        // Mesa baja
        DibujarRect(tex, 150, 40, 200, 50, HexAColor("#6b4e28"));
        DibujarRect(tex, 155, 10, 15, 35, HexAColor("#6b4e28")); // pata izq
        DibujarRect(tex, 330, 10, 15, 35, HexAColor("#6b4e28")); // pata der

        tex.Apply();
        return tex;
    }

    // Industrial: gris oscuro vacío
    Texture2D GenerarTexturaIndustrial()
    {
        int sz = 512;
        Texture2D tex = new Texture2D(sz, sz, TextureFormat.RGB24, false);
        RellenarTextura(tex, HexAColor("#303030"));
        tex.Apply();
        return tex;
    }

    // ── Textura real procesada ─────────────────────────────────────────────

    Texture2D CargarTexturaReal(string nombreEdificio)
    {
        // Extraer edificio_id del nombre del GO (patrón: "Edificio_297646225_...")
        string[] partes = nombreEdificio.Split('_');
        foreach (string parte in partes)
        {
            if (parte.Length >= 6 && long.TryParse(parte, out _))
            {
                string ruta = Path.Combine(dirTexturasProcesadas, parte + ".png");
                if (File.Exists(ruta))
                {
                    byte[] bytes = File.ReadAllBytes(ruta);
                    Texture2D tex = new Texture2D(2, 2);
                    if (tex.LoadImage(bytes))
                    {
                        tex.name = "FacadeReal_" + parte;
                        return tex;
                    }
                }
                break;
            }
        }
        return null;
    }

    // ── Pool helpers ───────────────────────────────────────────────────────

    GameObject CrearQuadPool()
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "InteriorQuad_Pool";
        go.transform.SetParent(transform, false);
        // Destruir collider — los quads de interior no necesitan físicas
        Destroy(go.GetComponent<Collider>());

        Renderer rend = go.GetComponent<Renderer>();
        // Material HDRP/Unlit para que no reciba luz exterior
        Material mat = new Material(Shader.Find("HDRP/Unlit"));
        if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
        {
            // Fallback si HDRP/Unlit no está disponible en el contexto actual
            mat = new Material(Shader.Find("Unlit/Texture"));
        }
        mat.enableInstancing = true;
        rend.material = mat;

        go.SetActive(false);
        return go;
    }

    Light CrearLuzPool()
    {
        GameObject go = new GameObject("InteriorLight_Pool");
        go.transform.SetParent(transform, false);
        Light luz = go.AddComponent<Light>();
        luz.type    = LightType.Point;
        luz.shadows = LightShadows.None;
        go.SetActive(false);
        return luz;
    }

    GameObject ObtenerQuadPool()
    {
        if (_poolQuads.Count > 0)
            return _poolQuads.Dequeue();
        return CrearQuadPool();
    }

    Light ObtenerLuzPool()
    {
        if (_poolLuces.Count > 0)
            return _poolLuces.Dequeue();
        return CrearLuzPool();
    }

    // ── Utilidades textura ─────────────────────────────────────────────────

    void RellenarTextura(Texture2D tex, Color color)
    {
        Color[] pixels = new Color[tex.width * tex.height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        tex.SetPixels(pixels);
    }

    void DibujarRect(Texture2D tex, int x, int y, int w, int h, Color color)
    {
        int maxX = Mathf.Min(x + w, tex.width);
        int maxY = Mathf.Min(y + h, tex.height);
        for (int py = Mathf.Max(y, 0); py < maxY; py++)
            for (int px = Mathf.Max(x, 0); px < maxX; px++)
                tex.SetPixel(px, py, color);
    }

    Color HexAColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return Color.magenta;
        float r = System.Convert.ToInt32(hex.Substring(0, 2), 16) / 255f;
        float g = System.Convert.ToInt32(hex.Substring(2, 2), 16) / 255f;
        float b = System.Convert.ToInt32(hex.Substring(4, 2), 16) / 255f;
        return new Color(r, g, b);
    }

    // Color semi-aleatorio determinista para productos de comercio
    Color ColorAleatorio(int seed)
    {
        float h = (seed * 0.618033988f) % 1f;
        return Color.HSVToRGB(h, 0.7f, 0.85f);
    }
}
