// Assets/Scripts/SistemaTren.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA TREN  (línea Madrid–Irún / Alsasua–Pamplona)
//
//  Construye una vía férrea visible (dos carriles + traviesas) que cruza el
//  valle pasando por la Estación de Tren de Alsasua, y hace circular un tren
//  (locomotora + vagones) con ciclo realista:
//
//      Llega desde un extremo → desacelera → PARA en la estación (espera con
//      faros y ventanas iluminadas) → arranca → se va por el otro extremo →
//      pausa fuera de escena → vuelve a entrar desde el extremo contrario.
//
//  Antes de esto sólo existía una zona con el nombre "Estación de Tren";
//  no había vías, ni tren, ni movimiento.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[DefaultExecutionOrder(-15)]
public class SistemaTren : MonoBehaviour
{
    public static SistemaTren Instance { get; private set; }

    [Header("Trazado de la vía")]
    [Tooltip("Dirección de la vía en el plano (se normaliza). Madrid–Irún cruza el valle.")]
    public Vector3 direccionVia = new(1f, 0f, 0.18f);
    [Tooltip("Longitud de la vía a cada lado de la estación (m).")]
    public float semiLongitud = 900f;
    [Tooltip("Separación entre los dos carriles (ancho de vía ibérico ≈ 1.668 m).")]
    public float anchoVia = 1.67f;
    [Tooltip("Separación entre traviesas (m).")]
    public float pasoTraviesa = 0.65f;

    [Header("Tren")]
    [Tooltip("Velocidad de crucero (m/s). 25 ≈ 90 km/h.")]
    public float velocidadCrucero = 25f;
    [Tooltip("Nº de vagones detrás de la locomotora.")]
    public int numVagones = 4;
    [Tooltip("Segundos parado en la estación.")]
    public float esperaEstacion = 14f;
    [Tooltip("Segundos fuera de escena entre pasadas.")]
    public float esperaFuera = 12f;

    // ── Estado interno ──────────────────────────────────────────────────────
    enum Fase { Entrando, Parado, Saliendo, Fuera }
    Fase   _fase;
    float  _s;                    // posición a lo largo de la vía [-semiLongitud … +semiLongitud]
    int    _sentido = 1;          // +1 o -1
    float  _velocidad;
    float  _timer;
    bool   _bocinaAprox;          // la bocina de aproximación suena una sola vez por entrada

    Vector3   _dir;               // dirección normalizada de la vía
    Vector3   _centro;            // punto central (estación)
    float     _sParada;           // posición s donde el morro para en el andén
    Transform _tren;              // raíz del convoy
    AudioSource _bocina;
    Light     _faro;
    float     _longitudConvoy;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        _dir = direccionVia; _dir.y = 0f;
        if (_dir.sqrMagnitude < 0.001f) _dir = Vector3.right;
        _dir.Normalize();
        _centro = GeoDataAlsasua.EstacionTren;
        _sParada = 0f;                       // para justo en el centro (andén)

        ConstruirVia();
        ConstruirTren();

        // Empieza entrando desde el extremo "negativo"
        _sentido  = 1;
        _s        = -semiLongitud;
        _velocidad = velocidadCrucero;
        _fase     = Fase.Entrando;
        ColocarConvoy();

        AlsasuaLogger.Info("Tren", $"Vía de {semiLongitud * 2f:F0} m y convoy de {numVagones + 1} unidades operativos.");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CONSTRUCCIÓN DE LA VÍA
    // ════════════════════════════════════════════════════════════════════════

    Vector3 PuntoVia(float s)
    {
        float x = _centro.x + _dir.x * s;
        float z = _centro.z + _dir.z * s;
        float y = GeoDataAlsasua.AlturaTerreno(x, z);
        return new Vector3(x, y, z);
    }

    void ConstruirVia()
    {
        var raiz = new GameObject("Via_Ferrea").transform;
        raiz.SetParent(transform, false);

        Vector3 perp = Vector3.Cross(_dir, Vector3.up).normalized;

        var matCarril = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        SetColor(matCarril, new Color(0.32f, 0.33f, 0.36f), 0.7f, 0.9f);
        var matTraviesa = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        SetColor(matTraviesa, new Color(0.18f, 0.14f, 0.11f), 0.2f, 0f);
        var matBalasto = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        SetColor(matBalasto, new Color(0.30f, 0.29f, 0.27f), 0.1f, 0f);

        // Balasto (lecho de la vía) — un box largo bajo los carriles
        var balasto = GameObject.CreatePrimitive(PrimitiveType.Cube);
        balasto.name = "Balasto";
        balasto.transform.SetParent(raiz, false);
        Vector3 cen = PuntoVia(0f);
        balasto.transform.position = cen + Vector3.up * 0.08f;
        balasto.transform.rotation = Quaternion.LookRotation(_dir, Vector3.up);
        balasto.transform.localScale = new Vector3(anchoVia + 1.6f, 0.16f, semiLongitud * 2f);
        balasto.GetComponent<Renderer>().sharedMaterial = matBalasto;
        Destroy(balasto.GetComponent<Collider>());

        // Dos carriles continuos
        for (int lado = -1; lado <= 1; lado += 2)
        {
            var carril = GameObject.CreatePrimitive(PrimitiveType.Cube);
            carril.name = lado < 0 ? "Carril_Izq" : "Carril_Der";
            carril.transform.SetParent(raiz, false);
            carril.transform.position = cen + perp * (anchoVia * 0.5f * lado) + Vector3.up * 0.2f;
            carril.transform.rotation = Quaternion.LookRotation(_dir, Vector3.up);
            carril.transform.localScale = new Vector3(0.12f, 0.16f, semiLongitud * 2f);
            carril.GetComponent<Renderer>().sharedMaterial = matCarril;
            Destroy(carril.GetComponent<Collider>());
        }

        // Traviesas (instanciadas con espaciado; se omiten algunas para no saturar)
        int n = Mathf.Clamp(Mathf.RoundToInt(semiLongitud * 2f / pasoTraviesa), 0, 3000);
        var traviesaParent = new GameObject("Traviesas").transform;
        traviesaParent.SetParent(raiz, false);
        for (int i = 0; i <= n; i++)
        {
            float s = -semiLongitud + i * pasoTraviesa;
            var t = GameObject.CreatePrimitive(PrimitiveType.Cube);
            t.transform.SetParent(traviesaParent, false);
            t.transform.position = PuntoVia(s) + Vector3.up * 0.12f;
            t.transform.rotation = Quaternion.LookRotation(_dir, Vector3.up);
            t.transform.localScale = new Vector3(anchoVia + 0.8f, 0.1f, 0.22f);
            t.GetComponent<Renderer>().sharedMaterial = matTraviesa;
            Destroy(t.GetComponent<Collider>());
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CONSTRUCCIÓN DEL TREN
    // ════════════════════════════════════════════════════════════════════════

    void ConstruirTren()
    {
        _tren = new GameObject("Convoy_Tren").transform;
        _tren.SetParent(transform, false);

        float largoLoco  = 18f;
        float largoVagon = 22f;
        float hueco      = 1.2f;

        // Chapa metálica real (CC0) tintada; respaldo a color plano si no está
        var matLoco  = TexturasVivo.MetalTintado(new Color(0.60f, 0.12f, 0.12f));   // rojo Renfe
        if (matLoco == null) { matLoco = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard")); SetColor(matLoco, new Color(0.55f, 0.10f, 0.10f), 0.6f, 0.1f); }
        var matVagon = TexturasVivo.MetalTintado(new Color(0.80f, 0.82f, 0.86f));   // gris claro
        if (matVagon == null) { matVagon = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard")); SetColor(matVagon, new Color(0.78f, 0.80f, 0.84f), 0.5f, 0.2f); }
        var matVent  = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        SetColor(matVent, new Color(0.05f, 0.06f, 0.08f), 0.95f, 0.2f);

        float offset = 0f;
        // Locomotora (al frente)
        ConstruirUnidad(_tren, "Locomotora", offset, largoLoco, 3.6f, 4.0f, matLoco, matVent, true);
        offset -= largoLoco * 0.5f + hueco;

        for (int v = 0; v < numVagones; v++)
        {
            offset -= largoVagon * 0.5f;
            ConstruirUnidad(_tren, $"Vagon_{v}", offset, largoVagon, 3.4f, 4.1f, matVagon, matVent, false);
            offset -= largoVagon * 0.5f + hueco;
        }
        _longitudConvoy = Mathf.Abs(offset);

        // Faro delantero
        var faroGO = new GameObject("Faro");
        faroGO.transform.SetParent(_tren, false);
        faroGO.transform.localPosition = new Vector3(0f, 2.2f, largoLoco * 0.5f);
        _faro = faroGO.AddComponent<Light>();
        _faro.type = LightType.Spot;
        _faro.range = 90f; _faro.spotAngle = 45f; _faro.intensity = 4f;
        _faro.color = new Color(1f, 0.97f, 0.85f);

        // Bocina
        _bocina = _tren.gameObject.AddComponent<AudioSource>();
        _bocina.spatialBlend = 1f; _bocina.maxDistance = 400f; _bocina.volume = 0.7f;
        _bocina.rolloffMode = AudioRolloffMode.Linear;
    }

    // Una caja-unidad con techo redondeado simulado, franja de ventanas y bogies.
    void ConstruirUnidad(Transform padre, string nombre, float offsetLocal, float largo,
                         float ancho, float alto, Material matCuerpo, Material matVent, bool esLoco)
    {
        var u = new GameObject(nombre).transform;
        u.SetParent(padre, false);
        u.localPosition = new Vector3(0f, 0f, offsetLocal);

        // Cuerpo
        var cuerpo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cuerpo.name = "Cuerpo";
        cuerpo.transform.SetParent(u, false);
        cuerpo.transform.localPosition = new Vector3(0f, alto * 0.5f + 0.9f, 0f);
        cuerpo.transform.localScale    = new Vector3(ancho, alto, largo);
        cuerpo.GetComponent<Renderer>().sharedMaterial = matCuerpo;
        Destroy(cuerpo.GetComponent<Collider>());

        // Franja de ventanas (un box oscuro algo más ancho)
        var vent = GameObject.CreatePrimitive(PrimitiveType.Cube);
        vent.name = "Ventanas";
        vent.transform.SetParent(u, false);
        vent.transform.localPosition = new Vector3(0f, alto * 0.5f + 1.7f, 0f);
        vent.transform.localScale    = new Vector3(ancho + 0.04f, alto * 0.35f, largo * 0.92f);
        var rv = vent.GetComponent<Renderer>();
        rv.sharedMaterial = matVent;
        Destroy(vent.GetComponent<Collider>());

        // Bogies (dos bloques bajos)
        for (int b = -1; b <= 1; b += 2)
        {
            var bog = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bog.transform.SetParent(u, false);
            bog.transform.localPosition = new Vector3(0f, 0.5f, b * largo * 0.32f);
            bog.transform.localScale    = new Vector3(ancho * 0.9f, 0.7f, 2.4f);
            var rb = bog.GetComponent<Renderer>();
            var matBog = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
            SetColor(matBog, new Color(0.08f, 0.08f, 0.09f), 0.3f, 0.4f);
            rb.sharedMaterial = matBog;
            Destroy(bog.GetComponent<Collider>());
        }

        if (esLoco)
        {
            // Morro frontal (cubo escalado hacia delante)
            var morro = GameObject.CreatePrimitive(PrimitiveType.Cube);
            morro.transform.SetParent(u, false);
            morro.transform.localPosition = new Vector3(0f, alto * 0.5f + 0.9f, largo * 0.5f + 0.6f);
            morro.transform.localScale    = new Vector3(ancho * 0.9f, alto * 0.8f, 1.4f);
            morro.GetComponent<Renderer>().sharedMaterial = matCuerpo;
            Destroy(morro.GetComponent<Collider>());
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CICLO DE MOVIMIENTO
    // ════════════════════════════════════════════════════════════════════════

    void Update()
    {
        switch (_fase)
        {
            case Fase.Entrando:  ActualizarEntrando();  break;
            case Fase.Parado:    ActualizarParado();    break;
            case Fase.Saliendo:  ActualizarSaliendo();  break;
            case Fase.Fuera:     ActualizarFuera();     break;
        }

        if (_fase != Fase.Fuera) ColocarConvoy();
    }

    void ActualizarEntrando()
    {
        // Desacelerar a medida que se acerca al andén (_sParada)
        float distAnden = Mathf.Abs(_sParada - _s);
        float vObjetivo = Mathf.Lerp(2.5f, velocidadCrucero, Mathf.Clamp01(distAnden / 220f));
        _velocidad = Mathf.MoveTowards(_velocidad, vObjetivo, 8f * Time.deltaTime);
        _s += _sentido * _velocidad * Time.deltaTime;

        // Bocina al aproximarse (una sola vez por entrada, no cada frame en la banda)
        if (!_bocinaAprox && distAnden < 260f) { Bocina(0.9f, 1.4f); _bocinaAprox = true; }

        if ((_sentido > 0 && _s >= _sParada) || (_sentido < 0 && _s <= _sParada))
        {
            _s = _sParada;
            _velocidad = 0f;
            _timer = esperaEstacion;
            _fase = Fase.Parado;
        }
    }

    void ActualizarParado()
    {
        _timer -= Time.deltaTime;
        if (_timer <= 0f)
        {
            Bocina(1f, 0.8f);     // pitido de salida
            _fase = Fase.Saliendo;
        }
    }

    void ActualizarSaliendo()
    {
        _velocidad = Mathf.MoveTowards(_velocidad, velocidadCrucero, 5f * Time.deltaTime);
        _s += _sentido * _velocidad * Time.deltaTime;

        float limite = semiLongitud + _longitudConvoy + 20f;
        if (Mathf.Abs(_s) >= limite)
        {
            _timer = esperaFuera;
            _fase = Fase.Fuera;
            if (_tren != null) _tren.gameObject.SetActive(false);
        }
    }

    void ActualizarFuera()
    {
        _timer -= Time.deltaTime;
        if (_timer <= 0f)
        {
            // Reaparece desde el extremo contrario, sentido invertido
            _sentido = -_sentido;
            _s = _sentido > 0 ? -semiLongitud - _longitudConvoy : semiLongitud + _longitudConvoy;
            _velocidad = velocidadCrucero;
            if (_tren != null) _tren.gameObject.SetActive(true);
            _bocinaAprox = false;        // rearmar la bocina para la próxima entrada
            _fase = Fase.Entrando;
        }
    }

    // Coloca y orienta todo el convoy sobre la vía, siguiendo el relieve.
    void ColocarConvoy()
    {
        if (_tren == null) return;
        Vector3 pos = PuntoVia(_s);
        _tren.position = pos + Vector3.up * 0.35f;
        _tren.rotation = Quaternion.LookRotation(_dir * _sentido, Vector3.up);

        // Faros y ventanas encendidos de noche o con poca luz / en parada
        if (_faro != null)
        {
            bool noche = RenderSettings.ambientIntensity < 0.6f;
            _faro.enabled = noche || _fase == Fase.Parado || _fase == Fase.Entrando;
        }
    }

    void Bocina(float vol, float dur)
    {
        if (_bocina == null) return;
        var clip = ConfiguradorAssetsAAA.Instance != null
            ? ConfiguradorAssetsAAA.Instance.ambienteExterior   // placeholder si no hay clip de bocina
            : null;
        if (clip != null) { _bocina.volume = vol; _bocina.PlayOneShot(clip, vol); }
    }

    // ── util ────────────────────────────────────────────────────────────────
    static void SetColor(Material m, Color c, float smoothness, float metallic)
    {
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", metallic);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
