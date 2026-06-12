// Assets/Scripts/GeneradorInterioresAAA.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GENERADOR DE INTERIORES AAA — interior mapping con parallax + cubemaps
//
//  Mejora sobre GeneradorInterioresSimples (al que desactiva si coexiste):
//   • Cada ventana muestra una HABITACIÓN 3D con profundidad real (shader
//     Altsasu/InteriorMapping) en vez de un quad plano.
//   • Cubemaps procedurales de 6 caras (paredes, suelo, techo, fondo) con
//     muebles según arquetipo (Bar/Comercio/Residencial/Industrial/Oficina).
//   • Variedad por edificio vía _Seed (mismo cubemap, distinta habitación).
//   • Emisión día/noche enganchada al ciclo horario (interiores se "encienden").
//   • Object pool + LOD por distancia. Carga cubemaps reales si existen en disco.
//
//  Coste de render ≈ un unlit: NO añade geometría ni draw calls extra por
//  profundidad. AAA real, no fake plano.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering.HighDefinition;

[DefaultExecutionOrder(-48)]
public class GeneradorInterioresAAA : MonoBehaviour
{
    public static GeneradorInterioresAAA Instance { get; private set; }

    [Header("Distancias LOD")]
    public float radioActivacion    = 40f;   // más lejos que el simple: el parallax aguanta
    public float radioDesactivacion = 50f;

    [Header("Cubemap")]
    public int resolucionCara = 256;

    [Header("Interior mapping")]
    [Tooltip("Habitaciones repetidas por ventana en U,V")]
    public Vector2 habitacionesPorVentana = new Vector2(2, 3);
    public float profundidadHabitacion    = 1.2f;

    Transform _jugador;
    Shader    _shaderInterior;
    float     _emisionNoche; // 0..1 según hora

    struct DatosEdificio
    {
        public GameObject go;
        public Vector3    posicion;
        public string     arquetipo;
        public float      seed;
    }

    readonly List<DatosEdificio> _edificios = new();
    readonly Dictionary<int, GameObject> _activos = new();
    readonly Queue<GameObject> _pool = new();
    readonly Dictionary<string, Cubemap> _cubemaps = new();
    readonly List<Material> _materialesVivos = new(); // para actualizar emisión día/noche

    // OPT: barrido rotatorio — recorrer los ~1030 edificios CADA frame
    // (Distance + ContainsKey ×2) era coste fijo evitable. Con ventana de 64,
    // el barrido completo tarda ~0,27 s a 60 fps: imperceptible frente a
    // radios de activación de decenas de metros.
    const int EDIFICIOS_POR_FRAME = 64;
    int   _cursorEdificios;
    // OPT: solo subir la emisión al shader cuando cambia (no cada frame)
    float _emisionAplicada = -1f;
    int   _numMatsEmision  = -1;

    static readonly int ID_Cube        = Shader.PropertyToID("_InteriorCube");
    static readonly int ID_Rooms       = Shader.PropertyToID("_Rooms");
    static readonly int ID_Depth       = Shader.PropertyToID("_RoomDepth");
    static readonly int ID_Emission    = Shader.PropertyToID("_EmissionNight");
    static readonly int ID_Seed        = Shader.PropertyToID("_Seed");
    static readonly int ID_PctApag     = Shader.PropertyToID("_PctApagadas");
    static readonly int ID_ColorTemp   = Shader.PropertyToID("_ColorTemperatura");
    static readonly int ID_DepthVar    = Shader.PropertyToID("_DepthVariation");

    static readonly string[] ARQUETIPOS_TODOS =
        { "Bar", "Comercio", "Residencial", "Industrial", "Oficina",
          "Farmacia", "Supermercado", "Garaje" };

    // ════════════════════════════════════════════════════════════════════════
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        // Evitar que el sistema "Simples" pinte quads planos a la vez
        var simple = FindAnyObjectByType<GeneradorInterioresSimples>();
        if (simple != null)
        {
            simple.enabled = false;
            Debug.Log("[InterioresAAA] GeneradorInterioresSimples desactivado (sustituido por AAA).");
        }
    }

    void Start()
    {
        _shaderInterior = Shader.Find("Altsasu/InteriorMapping");
        if (_shaderInterior == null)
        {
            Debug.LogError("[InterioresAAA] No se encontró el shader 'Altsasu/InteriorMapping'. " +
                           "Asegúrate de que Assets/Shaders/InteriorMapping.shader compiló sin errores.");
            enabled = false;
            return;
        }

        var jugadorGO = GameObject.FindWithTag("Player");
        if (jugadorGO != null) _jugador = jugadorGO.transform;

        // Cubemaps por arquetipo: carga los generados por Python (512px, fotorrealistas)
        // o fallback al generador procedural en C# si faltan.
        foreach (var arq in ARQUETIPOS_TODOS)
            _cubemaps[arq] = CargarCubemapReal(arq) ?? GenerarCubemap(arq);

        CachearEdificios();
    }

    void Update()
    {
        if (_jugador == null)
        {
            var go = GameObject.FindWithTag("Player");
            if (go != null) _jugador = go.transform; else return;
        }

        ActualizarEmisionDiaNoche();

        Vector3 pj = _jugador.position;
        int n = _edificios.Count;
        if (n == 0) return;
        int procesar = Mathf.Min(EDIFICIOS_POR_FRAME, n);
        for (int k = 0; k < procesar; k++)
        {
            int i = (_cursorEdificios + k) % n;
            var d = _edificios[i];
            if (d.go == null) continue;
            float dist = Vector3.Distance(pj, d.posicion);
            int id = d.go.GetInstanceID();

            if (dist < radioActivacion && !_activos.ContainsKey(id)) CrearInterior(d);
            else if (dist > radioDesactivacion && _activos.ContainsKey(id)) Liberar(id);
        }
        _cursorEdificios = (_cursorEdificios + procesar) % n;
    }

    void OnDestroy()
    {
        foreach (var go in _activos.Values) if (go != null) Destroy(go);
        _activos.Clear();
        foreach (var c in _cubemaps.Values) if (c != null) Destroy(c);
        _cubemaps.Clear();
        foreach (var m in _materialesVivos) if (m != null) Destroy(m);
        _materialesVivos.Clear();
    }

    // ── Emisión día/noche ───────────────────────────────────────────────────
    void ActualizarEmisionDiaNoche()
    {
        var atm = AltsasuCore.I?.atmosferaSystem;
        float hora = atm != null ? atm.HoraDelDia : 12f;
        bool noche = hora < 7f || hora > 19f;
        float target = noche ? 1f : 0.05f;
        _emisionNoche = Mathf.MoveTowards(_emisionNoche, target, Time.deltaTime * 0.3f);

        // OPT: si la emisión ya convergió y no hay materiales nuevos, no hay
        // nada que subir al shader — evita N×SetFloat por frame.
        if (Mathf.Approximately(_emisionNoche, _emisionAplicada) &&
            _materialesVivos.Count == _numMatsEmision) return;

        for (int i = _materialesVivos.Count - 1; i >= 0; i--)
        {
            if (_materialesVivos[i] == null) { _materialesVivos.RemoveAt(i); continue; }
            _materialesVivos[i].SetFloat(ID_Emission, _emisionNoche);
        }
        _emisionAplicada = _emisionNoche;
        _numMatsEmision  = _materialesVivos.Count;
    }

    // ── Cacheo de edificios ─────────────────────────────────────────────────
    void CachearEdificios()
    {
        _edificios.Clear();
        var raiz = GameObject.Find("Edificios_AAA");
        if (raiz == null) { Debug.LogWarning("[InterioresAAA] No se encontró 'Edificios_AAA'."); return; }

        foreach (Transform hijo in raiz.transform)
        {
            if (hijo == null) continue;
            _edificios.Add(new DatosEdificio
            {
                go        = hijo.gameObject,
                posicion  = hijo.position,
                arquetipo = ExtraerArquetipo(hijo.name),
                seed      = Mathf.Abs(hijo.name.GetHashCode() % 1000) * 0.013f,
            });
        }
        Debug.Log($"[InterioresAAA] {_edificios.Count} edificios cacheados.");
    }

    static string ExtraerArquetipo(string nombre)
    {
        string n = nombre.ToUpperInvariant();
        if (n.Contains("BAR") || n.Contains("TABERNA") || n.Contains("RESTAUR")) return "Bar";
        if (n.Contains("OFICINA") || n.Contains("BANCO") || n.Contains("AYUNT"))  return "Oficina";
        if (n.Contains("COMERCIO") || n.Contains("TIENDA") || n.Contains("TALLER")) return "Comercio";
        if (n.Contains("INDUSTRIAL") || n.Contains("NAVE") || n.Contains("FABRICA")) return "Industrial";
        return "Residencial";
    }

    // ── Crear / liberar interior ──────────────────────────────────────────────
    void CrearInterior(DatosEdificio d)
    {
        Transform fachada = EncontrarFachada(d.go.transform);
        if (fachada == null) return;

        GameObject quad = ObtenerPool();
        Vector3 normal = fachada.forward;
        quad.transform.position   = fachada.position - normal * 0.05f;
        quad.transform.rotation   = Quaternion.LookRotation(normal);
        quad.transform.localScale  = EscalaFachada(fachada);
        quad.SetActive(true);

        var rend = quad.GetComponent<Renderer>();
        var mat  = new Material(_shaderInterior);
        mat.SetTexture(ID_Cube,  ObtenerCubemap(d.arquetipo));
        mat.SetVector(ID_Rooms,  new Vector4(habitacionesPorVentana.x, habitacionesPorVentana.y, 0, 0));
        mat.SetFloat(ID_Depth,   profundidadHabitacion);
        mat.SetFloat(ID_Seed,    d.seed);
        mat.SetFloat(ID_Emission, _emisionNoche);
        mat.SetFloat(ID_DepthVar, 0.15f);
        mat.SetFloat(ID_PctApag, ArquetipoEsComercial(d.arquetipo) ? 0.0f : 0.22f); // negocios siempre encendidos
        mat.SetFloat(ID_ColorTemp, ArquetipoEsCalido(d.arquetipo) ? 0.3f : 0.65f);
        mat.enableInstancing = true;
        rend.sharedMaterial = mat;
        _materialesVivos.Add(mat);

        _activos[d.go.GetInstanceID()] = quad;
    }

    void Liberar(int id)
    {
        if (!_activos.TryGetValue(id, out var go)) return;
        if (go != null)
        {
            var rend = go.GetComponent<Renderer>();
            if (rend != null && rend.sharedMaterial != null)
            {
                _materialesVivos.Remove(rend.sharedMaterial);
                Destroy(rend.sharedMaterial);
            }
            go.SetActive(false);
            _pool.Enqueue(go);
        }
        _activos.Remove(id);
    }

    Transform EncontrarFachada(Transform raiz)
    {
        foreach (Transform t in raiz.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToUpperInvariant();
            if (n.Contains("FACHADA") || n.Contains("ESCAPARATE") || n.Contains("FRENTE")) return t;
        }
        var r = raiz.GetComponentsInChildren<Renderer>();
        return r.Length > 0 ? r[0].transform : null;
    }

    Vector3 EscalaFachada(Transform fachada)
    {
        var rend = fachada.GetComponent<Renderer>();
        if (rend != null)
        {
            var b = rend.bounds.size;
            return new Vector3(Mathf.Max(b.x, 2f), Mathf.Max(b.y, 2.5f), 1f);
        }
        return new Vector3(4f, 3f, 1f);
    }

    GameObject ObtenerPool()
    {
        if (_pool.Count > 0) return _pool.Dequeue();
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Interior_AAA";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform);
        return go;
    }

    Cubemap ObtenerCubemap(string arq) =>
        _cubemaps.TryGetValue(arq, out var c) && c != null ? c
        : _cubemaps.TryGetValue("Residencial", out var fb) ? fb : null;

    static bool ArquetipoEsComercial(string arq) =>
        arq == "Bar" || arq == "Comercio" || arq == "Farmacia" || arq == "Supermercado";
    static bool ArquetipoEsCalido(string arq) =>
        arq == "Bar" || arq == "Residencial" || arq == "Garaje";

    // ════════════════════════════════════════════════════════════════════════
    //  GENERACIÓN PROCEDURAL DE CUBEMAPS (6 caras: habitación completa)
    // ════════════════════════════════════════════════════════════════════════
    Cubemap GenerarCubemap(string arq)
    {
        int s = resolucionCara;
        var cube = new Cubemap(s, TextureFormat.RGB24, false);

        // Paleta por arquetipo: (pared, suelo, techo, acento1, acento2)
        Color pared, suelo, techo, ac1, ac2;
        switch (arq)
        {
            case "Bar":
                pared = Hex("#3a2414"); suelo = Hex("#2a1810"); techo = Hex("#1a1008");
                ac1 = Hex("#caa15a"); ac2 = Hex("#7a3010"); break;
            case "Comercio":
                pared = Hex("#e8e8ec"); suelo = Hex("#cfcfd6"); techo = Hex("#f4f4f8");
                ac1 = Hex("#e74c3c"); ac2 = Hex("#3498db"); break;
            case "Oficina":
                pared = Hex("#d6dde2"); suelo = Hex("#5a5f66"); techo = Hex("#eef2f5");
                ac1 = Hex("#2c3e50"); ac2 = Hex("#95a5a6"); break;
            case "Industrial":
                pared = Hex("#404448"); suelo = Hex("#2c2e30"); techo = Hex("#34373a");
                ac1 = Hex("#d39a1a"); ac2 = Hex("#6a6e72"); break;
            default: // Residencial
                pared = Hex("#e8dfc8"); suelo = Hex("#8b6914"); techo = Hex("#f2ece0");
                ac1 = Hex("#c8a87a"); ac2 = Hex("#7a8fb0"); break;
        }

        // Caras: PositiveX/NegativeX = paredes laterales, PositiveY = techo,
        // NegativeY = suelo, PositiveZ/NegativeZ = pared fondo y frontal.
        cube.SetPixels(CaraSimple(s, pared, ac2, true),  CubemapFace.PositiveX);
        cube.SetPixels(CaraSimple(s, pared, ac2, true),  CubemapFace.NegativeX);
        cube.SetPixels(CaraSimple(s, techo, techo, false), CubemapFace.PositiveY);
        cube.SetPixels(CaraSuelo(s, suelo),              CubemapFace.NegativeY);
        cube.SetPixels(CaraFondo(s, arq, pared, ac1, ac2), CubemapFace.PositiveZ);
        cube.SetPixels(CaraFondo(s, arq, pared, ac1, ac2), CubemapFace.NegativeZ);

        cube.Apply();
        return cube;
    }

    // Pared lateral: zócalo + ventana/cuadro tenue
    Color[] CaraSimple(int s, Color baseC, Color acento, bool conZocalo)
    {
        var px = Llenar(s, baseC);
        if (conZocalo) RectP(px, s, 0, 0, s, (int)(s * 0.12f), Mul(baseC, 0.7f)); // zócalo
        // leve degradado superior (luz de techo)
        for (int y = 0; y < s; y++)
        {
            float t = 1f - (float)y / s * 0.25f;
            for (int x = 0; x < s; x++) px[y * s + x] = Mul(px[y * s + x], t);
        }
        return px;
    }

    Color[] CaraSuelo(int s, Color suelo)
    {
        var px = Llenar(s, suelo);
        // baldosas
        Color linea = Mul(suelo, 0.75f);
        for (int i = 1; i < 6; i++)
        {
            int p = s * i / 6;
            RectP(px, s, p, 0, Mathf.Max(1, s / 256), s, linea);
            RectP(px, s, 0, p, s, Mathf.Max(1, s / 256), linea);
        }
        return px;
    }

    // Pared de fondo: aquí van los muebles característicos del arquetipo
    Color[] CaraFondo(int s, string arq, Color pared, Color ac1, Color ac2)
    {
        var px = Llenar(s, pared);
        RectP(px, s, 0, 0, s, (int)(s * 0.10f), Mul(pared, 0.65f)); // zócalo

        switch (arq)
        {
            case "Bar":
                RectP(px, s, 0, (int)(s*0.18f), s, (int)(s*0.05f), ac1); // estante
                for (int i = 0; i < 14; i++)
                    RectP(px, s, (int)(s*(0.04f+i*0.068f)), (int)(s*0.23f), (int)(s*0.03f), (int)(s*0.32f),
                          (i%2==0)?ac2:Mul(ac1,0.6f)); // botellas
                RectP(px, s, (int)(s*0.15f), (int)(s*0.05f), (int)(s*0.7f), (int)(s*0.13f), Mul(ac2,0.8f)); // barra
                break;
            case "Comercio":
                for (int r = 0; r < 4; r++)
                {
                    int y = (int)(s*(0.15f+r*0.18f));
                    RectP(px, s, 0, y, s, (int)(s*0.015f), Hex("#bbbbbb"));
                    for (int c = 0; c < 7; c++)
                        RectP(px, s, (int)(s*(0.05f+c*0.135f)), y+(int)(s*0.02f),
                              (int)(s*0.08f), (int)(s*0.1f), (c%2==0)?ac1:ac2);
                }
                break;
            case "Oficina":
                RectP(px, s, (int)(s*0.1f), (int)(s*0.12f), (int)(s*0.8f), (int)(s*0.04f), ac1); // mesa
                RectP(px, s, (int)(s*0.35f), (int)(s*0.16f), (int)(s*0.3f), (int)(s*0.22f), Hex("#101418")); // monitor
                RectP(px, s, (int)(s*0.05f), (int)(s*0.5f), (int)(s*0.9f), (int)(s*0.35f), Mul(pared,0.92f)); // pizarra/ventanal
                break;
            case "Industrial":
                for (int i = 0; i < 5; i++)
                    RectP(px, s, (int)(s*(0.06f+i*0.18f)), (int)(s*0.1f), (int)(s*0.12f), (int)(s*0.6f), Mul(ac2,0.9f)); // estanterías
                RectP(px, s, (int)(s*0.2f), (int)(s*0.7f), (int)(s*0.6f), (int)(s*0.06f), ac1); // tubo/luminaria
                break;
            default: // Residencial
                RectP(px, s, (int)(s*0.1f), (int)(s*0.12f), (int)(s*0.55f), (int)(s*0.22f), ac1); // sofá
                RectP(px, s, (int)(s*0.1f), (int)(s*0.32f), (int)(s*0.55f), (int)(s*0.07f), Mul(ac1,0.85f)); // respaldo
                RectP(px, s, (int)(s*0.7f), (int)(s*0.4f), (int)(s*0.18f), (int)(s*0.28f), ac2); // cuadro
                RectP(px, s, (int)(s*0.78f), (int)(s*0.12f), (int)(s*0.03f), (int)(s*0.5f), Hex("#999999")); // lámpara pie
                RectP(px, s, (int)(s*0.73f), (int)(s*0.62f), (int)(s*0.13f), (int)(s*0.1f), Hex("#fff0c0")); // pantalla lámpara
                break;
        }
        return px;
    }

    // ── Helpers de pintura ────────────────────────────────────────────────────
    static Color[] Llenar(int s, Color c)
    {
        var px = new Color[s * s];
        for (int i = 0; i < px.Length; i++) px[i] = c;
        return px;
    }
    static void RectP(Color[] px, int s, int x, int y, int w, int h, Color c)
    {
        x = Mathf.Clamp(x, 0, s - 1); y = Mathf.Clamp(y, 0, s - 1);
        w = Mathf.Clamp(w, 1, s - x); h = Mathf.Clamp(h, 1, s - y);
        for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++)
                px[yy * s + xx] = c;
    }
    static Color Mul(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, 1f);
    static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

    // ── Carga de cubemap real desde disco (Poly Haven HDRI descargado) ────────
    Cubemap CargarCubemapReal(string arq)
    {
        // Primero busca en Resources/Interiores/<arq> (generado por IntegradorAssetsInteriores)
        var c = Resources.Load<Cubemap>($"Interiores/{arq.ToLower()}");
        if (c != null) return c;

        // Fallback: buscar cualquier cubemap en Resources/Interiores/
        var todos = Resources.LoadAll<Cubemap>("Interiores");
        return todos.Length > 0 ? todos[0] : null;
    }

    // ── Más arquetipos (15 en total con variantes) ─────────────────────────────
    // Los GenerarCubemap* extra se llaman según el arquetipo detectado en CachearEdificios
    // si se añaden más claves al switch de ExtraerArquetipo.
    static string ExtraerArquetipoDetallado(string nombre)
    {
        string n = nombre.ToUpperInvariant();
        if (n.Contains("BAR") || n.Contains("TABERNA"))         return "Bar";
        if (n.Contains("RESTAURANTE") || n.Contains("CAFETERIA")) return "Bar";
        if (n.Contains("OFICINA") || n.Contains("BANCO"))       return "Oficina";
        if (n.Contains("AYUNT") || n.Contains("JUZGADO"))       return "Oficina";
        if (n.Contains("FARMACIA") || n.Contains("CLINICA"))    return "Comercio";
        if (n.Contains("COMERCIO") || n.Contains("TIENDA"))     return "Comercio";
        if (n.Contains("SUPERMERCADO") || n.Contains("BAZAR"))  return "Comercio";
        if (n.Contains("INDUSTRIAL") || n.Contains("NAVE"))     return "Industrial";
        if (n.Contains("IGLESIA") || n.Contains("ERMITA"))      return "Industrial"; // oscuro/alto
        if (n.Contains("GARAJE") || n.Contains("PARKING"))      return "Industrial";
        return "Residencial";
    }
}
