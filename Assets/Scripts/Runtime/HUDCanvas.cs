// Assets/Scripts/HUDCanvas.cs
// ═══════════════════════════════════════════════════════════════════════════
//  HUD CANVAS UGUI — reemplaza el OnGUI legacy de HUDAAA
//
//  Elementos:
//    • Barra vida + armadura (sliders)
//    • Dinero con animación contador
//    • Estrellas wanted (1-5 con pulso en cambio)
//    • Apoyo popular (barra horizontal)
//    • Hora del día + icono clima
//    • Minimapa radar (RenderTexture circular)
//    • Marcadores de misión en world space (WorldSpaceMarker)
//    • Indicador de dirección del daño (arco en pantalla)
//    • Velocímetro analógico (dial cuando se conduce)
//    • Brújula (barra superior)
//    • Crosshair dinámico
//    • Texto de misión activa (esquina inferior izquierda)
//
//  Construye todo el Canvas por código — sin necesidad de prefabs.
//  Compatible con HDRP. No requiere TextMeshPro.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

[DefaultExecutionOrder(60)]
public class HUDCanvas : MonoBehaviour
{
    public static HUDCanvas I { get; private set; }

    // ── Canvas principal ──────────────────────────────────────────────────
    Canvas      _canvas;
    CanvasScaler _scaler;

    // ── Vida / Armadura ───────────────────────────────────────────────────
    Slider  _barraVida, _barraArmadura;
    Image   _fillVida, _fillArmadura;
    Text    _txtVida;

    // ── Dinero ────────────────────────────────────────────────────────────
    Text    _txtDinero;
    int     _dineroMostrado;
    float   _timerDinero;

    // ── Wanted ────────────────────────────────────────────────────────────
    Image[] _estrellasWanted = new Image[5];
    int     _wantedActual;

    // ── Apoyo popular ─────────────────────────────────────────────────────
    Slider  _barraApoyo;
    Text    _txtApoyo;

    // ── Hora / Clima ──────────────────────────────────────────────────────
    Text    _txtHora;
    Text    _txtClima;

    // ── Misión activa ─────────────────────────────────────────────────────
    Text    _txtMisionTitulo;
    Text    _txtMisionObjetivo;
    float   _timerMision;

    // ── Velocímetro ───────────────────────────────────────────────────────
    GameObject  _panelVelocimetro;
    Image       _agujaVelocimetro;
    Text        _txtVelocidad;
    Text        _txtMarcha;

    // ── Brújula ───────────────────────────────────────────────────────────
    RectTransform _brujulaStrip;
    Text          _txtBrujula;

    // ── Crosshair ────────────────────────────────────────────────────────
    Image   _crossH, _crossV, _crossPunto;
    float   _crossSize = 10f, _crossTarget = 10f;

    // ── Indicador daño ────────────────────────────────────────────────────
    readonly List<DanoIndicator> _danoIndicators = new();

    // ── Minimapa ─────────────────────────────────────────────────────────
    RawImage     _minimapa;
    RenderTexture _miniRT;
    Camera        _miniCam;
    Image         _minimarco;

    // ── Marcadores de misión ──────────────────────────────────────────────
    readonly List<MarcadorMision> _marcadores = new();

    // ── Banner deslizable de misión ───────────────────────────────────────
    RectTransform   _bannerMision;
    Image           _bgBanner;
    Text            _bannerTxtTitulo, _bannerTxtObjetivo;
    bool            _bannerVisible;

    // ── Notificaciones de logros ──────────────────────────────────────────
    readonly Queue<string> _notifQueue = new();
    bool                   _notifMostrando;
    RectTransform          _panelNotif;
    Text                   _txtNotif;

    // ── Estado ────────────────────────────────────────────────────────────
    ControladorJugador          _jugador;
    int                         _dineroCached;    // actualizado por OnEconomiaCambia
    SistemaAtmosfera            _atm;
    ControladorVehiculoJugador  _vehiculo;
    float                       _apoyoActual;   // caché local — actualizado por evento

    // ── Colores ───────────────────────────────────────────────────────────
    static readonly Color COL_VIDA     = new(0.95f, 0.22f, 0.22f);
    static readonly Color COL_ARMADURA = new(0.30f, 0.68f, 1.00f);
    static readonly Color COL_APOYO    = new(0.22f, 0.85f, 0.30f);
    static readonly Color COL_WANTED   = new(1.00f, 0.82f, 0.10f);
    static readonly Color COL_HUD_BG   = new(0.00f, 0.00f, 0.00f, 0.62f);
    static readonly Color COL_HUD_BORDE= new(0.18f, 0.45f, 0.90f, 0.80f);

    // ════════════════════════════════════════════════════════════════════════
    //  BOOT
    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (I != null && I != this) { Destroy(this); return; }
        I = this;
    }

    void Start()
    {
        CrearCanvas();
        CrearPanelEstado();
        CrearWanted();
        CrearApoyo();
        CrearHoraSClima();
        CrearVelocimetro();
        CrearBrujula();
        CrearCrosshair();
        CrearMinimap();
        CrearMisionTexto();
        CrearBannerMision();
        CrearPanelNotificaciones();
        SuscribirEventos();
        BuscarReferencias();
        StartCoroutine(ProcesarColaNotificaciones());
    }

    void BuscarReferencias()
    {
        _dineroCached = ServiceLocator.Get<IEconomyService>()?.Dinero ?? 0;
        _atm = AltsasuCore.I?.atmosferaSystem;
        // _jugador se asigna cuando AltsasuCore dispara OnJugadorSpawned
        if (AltsasuCore.Jugador != null)
            _jugador = AltsasuCore.Jugador.GetComponent<ControladorJugador>();
        // apoyo inicial
        _apoyoActual = SistemaApoyoPopular.Instance != null
                     ? SistemaApoyoPopular.Instance.apoyo : 50f;
    }

    void SuscribirEventos()
    {
        ControladorJugador.OnDanoRecibido          += OnDano;
        GameManagerAltsasua.OnEstrellasCambia      += OnWanted;
        GameManagerAltsasua.OnEconomiaCambia       += OnEconomia;
        SistemaMisiones.OnMisionIniciada           += OnMisionIniciada;
        SistemaMisiones.OnObjetivoCompletado       += OnObjetivoCompletado;
        SistemaMisiones.OnMisionCompletada         += OnMisionCompletada;
        SistemaLogros.OnLogroDesbloqueado          += OnLogro;
        AltsasuCore.OnJugadorSpawned               += OnJugadorSpawned;
        SistemaApoyoPopular.OnApoyoCambia          += OnApoyo;   // FIX: método nombrado (antes lambda → el -= con otra lambda no quitaba nada → fuga de suscripción)
        ControladorVehiculoJugador.OnJugadorEntro  += OnEntroVehiculo;
        ControladorVehiculoJugador.OnJugadorSalio  += OnSalioVehiculo;
        // EventBus: fade a negro en muerte del jugador (desacoplado de ControladorJugador)
        EventBus.Subscribe<PlayerDeathEvent>(OnPlayerMuerto);
    }

    void DesuscribirEventos()
    {
        ControladorJugador.OnDanoRecibido          -= OnDano;
        GameManagerAltsasua.OnEstrellasCambia      -= OnWanted;
        GameManagerAltsasua.OnEconomiaCambia       -= OnEconomia;
        SistemaMisiones.OnMisionIniciada           -= OnMisionIniciada;
        SistemaMisiones.OnObjetivoCompletado       -= OnObjetivoCompletado;
        SistemaMisiones.OnMisionCompletada         -= OnMisionCompletada;
        SistemaLogros.OnLogroDesbloqueado          -= OnLogro;
        AltsasuCore.OnJugadorSpawned               -= OnJugadorSpawned;
        SistemaApoyoPopular.OnApoyoCambia          -= OnApoyo;
        ControladorVehiculoJugador.OnJugadorEntro  -= OnEntroVehiculo;
        ControladorVehiculoJugador.OnJugadorSalio  -= OnSalioVehiculo;
        EventBus.Unsubscribe<PlayerDeathEvent>(OnPlayerMuerto);
    }

    void OnApoyo(float v) => _apoyoActual = v;

    void OnPlayerMuerto(PlayerDeathEvent evt)
    {
        // Fade a negro y mensaje de muerte en pantalla
        _notifQueue.Enqueue("☠  Has muerto");
        StartCoroutine(FadeNegroMuerte());
    }

    IEnumerator FadeNegroMuerte()
    {
        // Crear overlay negro temporal para el efecto de muerte
        var overlayGO = new GameObject("FadeMuerte");
        overlayGO.transform.SetParent(_canvas.transform, false);
        var rt = overlayGO.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = overlayGO.AddComponent<Image>();
        img.color = new Color(0, 0, 0, 0);

        // Fade in
        float t = 0f;
        while (t < 1f) { t += Time.unscaledDeltaTime * 1.5f; img.color = new Color(0, 0, 0, Mathf.Clamp01(t)); yield return null; }
        yield return new WaitForSecondsRealtime(1.5f);
        // Fade out
        t = 1f;
        while (t > 0f) { t -= Time.unscaledDeltaTime * 1.5f; img.color = new Color(0, 0, 0, Mathf.Clamp01(t)); yield return null; }
        Destroy(overlayGO);
    }

    void OnJugadorSpawned(Transform t)
        => _jugador = t.GetComponent<ControladorJugador>();

    void OnEntroVehiculo(ControladorVehiculoJugador v)
    {
        _vehiculo = v;
        if (_panelVelocimetro != null) _panelVelocimetro.SetActive(true);
    }

    void OnSalioVehiculo(ControladorVehiculoJugador _)
    {
        _vehiculo = null;
        if (_panelVelocimetro != null) _panelVelocimetro.SetActive(false);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  UPDATE
    // ════════════════════════════════════════════════════════════════════════

    // PERF: Camera.main hace FindGameObjectWithTag("MainCamera") en CADA acceso. El HUD lo
    // tocaba 2×/frame (brújula + marcadores). Cacheado con refresh-if-null (la cámara TP se
    // recrea al respawnear → no se puede cachear solo en Start).
    Camera _camHUD;
    Camera CamaraHUD() { if (_camHUD == null) _camHUD = Camera.main; return _camHUD; }

    void Update()
    {
        ActualizarVida();
        ActualizarDinero();
        ActualizarApoyo();
        ActualizarHoraClima();
        ActualizarVehiculo();
        ActualizarBrujula();
        ActualizarCrosshair();
        ActualizarMinimap();
        ActualizarDanoIndicators();
        ActualizarMarcadores();
    }

    void ActualizarVida()
    {
        if (_jugador == null) return;
        if (_barraVida != null) _barraVida.value = Mathf.Lerp(_barraVida.value, _jugador.RatioVida, Time.deltaTime * 5f);
        if (_txtVida   != null) _txtVida.text    = $"{_jugador.Vida}";
    }

    void ActualizarDinero()
    {
        if (_txtDinero == null) return;
        int meta = _dineroCached;
        if (_dineroMostrado != meta)
        {
            _dineroMostrado = (int)Mathf.MoveTowards(_dineroMostrado, meta, Time.deltaTime * 500f);
            _txtDinero.text = $"€ {_dineroMostrado:N0}";
            _txtDinero.color = _dineroMostrado < meta
                ? new Color(0.3f, 1f, 0.3f) : new Color(1f, 0.4f, 0.3f);
        }
        else _txtDinero.color = Color.white;
    }

    void OnEconomia(int dinero, int _) => _dineroCached = dinero;

    void ActualizarApoyo()
    {
        if (_barraApoyo == null) return;
        _barraApoyo.value = Mathf.Lerp(_barraApoyo.value, _apoyoActual / 100f, Time.deltaTime * 2f);
        if (_txtApoyo != null) _txtApoyo.text = $"♥ {_apoyoActual:F0}%";
    }

    void ActualizarHoraClima()
    {
        if (_atm == null || _txtHora == null) return;
        float h  = _atm.HoraDelDia;
        int   hh = (int)h, mm = (int)((h - hh) * 60f);
        _txtHora.text = $"{hh:00}:{mm:00}";
        if (_txtClima != null) _txtClima.text = ClimaIcono();
    }

    void ActualizarVehiculo()
    {
        if (_panelVelocimetro == null || _vehiculo == null) return;

        var rb = _vehiculo.GetComponent<Rigidbody>();
        float kmh = rb != null ? rb.linearVelocity.magnitude * 3.6f : 0f;
        if (_txtVelocidad != null) _txtVelocidad.text = $"{kmh:F0}";
        if (_agujaVelocimetro != null)
        {
            float angulo = Mathf.Lerp(135f, -135f, Mathf.Clamp01(kmh / 200f));
            _agujaVelocimetro.rectTransform.localEulerAngles = new Vector3(0, 0, angulo);
        }
    }

    void ActualizarBrujula()
    {
        if (_txtBrujula == null) return;
        var cam = CamaraHUD();
        if (cam == null) return;
        float yaw = cam.transform.eulerAngles.y;
        string[] dirs = { "N","NE","E","SE","S","SO","O","NO","N" };
        string dir = dirs[Mathf.RoundToInt(yaw / 45f) % 8];
        _txtBrujula.text = $"{dir}  {yaw:F0}°";
        if (_brujulaStrip != null)
            _brujulaStrip.anchoredPosition = new Vector2(-yaw * 2f, 0f);
    }

    void ActualizarCrosshair()
    {
        // Crosshair se abre al disparar o al correr
        float vel = 0f;
        if (_jugador != null)
        {
            var rb = _jugador.GetComponent<Rigidbody>();
            vel = rb != null ? rb.linearVelocity.magnitude : 0f;
        }
        _crossTarget = Mathf.Lerp(6f, 22f, vel / 8f);
        _crossSize   = Mathf.Lerp(_crossSize, _crossTarget, Time.deltaTime * 8f);
        if (_crossH != null) _crossH.rectTransform.sizeDelta = new Vector2(_crossSize * 2f, 2f);
        if (_crossV != null) _crossV.rectTransform.sizeDelta = new Vector2(2f, _crossSize * 2f);
    }

    float _miniTimer;
    void ActualizarMinimap()
    {
        if (_miniCam == null) return;
        var j = AltsasuCore.Jugador;
        if (j == null) return;
        _miniCam.transform.position = j.position + Vector3.up * 80f;
        _miniCam.transform.eulerAngles = new Vector3(90f, 0f, 0f);
        // BUG FIX (auditoría): render manual ~6-7 fps en vez de la escena completa cada frame.
        _miniTimer -= Time.deltaTime;
        if (_miniTimer <= 0f) { _miniTimer = 0.15f; _miniCam.Render(); }
    }

    void ActualizarDanoIndicators()
    {
        for (int i = _danoIndicators.Count - 1; i >= 0; i--)
        {
            var ind = _danoIndicators[i];
            ind.timer -= Time.deltaTime;
            if (ind.timer <= 0f || ind.image == null)
            {
                if (ind.image != null) Destroy(ind.image.gameObject);
                _danoIndicators.RemoveAt(i);
                continue;
            }
            float alpha = Mathf.Clamp01(ind.timer / ind.duracion);
            ind.image.color = new Color(1f, 0.1f, 0.1f, alpha * 0.85f);
        }
    }

    void ActualizarMarcadores()
    {
        var cam = CamaraHUD();
        if (cam == null) return;
        foreach (var m in _marcadores)
        {
            if (m.objetivo == null || m.imagen == null) continue;
            var vp = cam.WorldToViewportPoint(m.objetivo.position);
            bool visible = vp.z > 0 && vp.x > 0 && vp.x < 1 && vp.y > 0 && vp.y < 1;
            m.imagen.gameObject.SetActive(visible);
            if (!visible) continue;
            m.imagen.rectTransform.anchorMin = m.imagen.rectTransform.anchorMax = new Vector2(vp.x, vp.y);
            m.imagen.rectTransform.anchoredPosition = Vector2.zero;
            float dist = Vector3.Distance(AltsasuCore.Jugador?.position ?? Vector3.zero, m.objetivo.position);
            if (m.texto != null) m.texto.text = $"{m.label}\n{dist:F0}m";
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CONSTRUCCIÓN DEL CANVAS
    // ════════════════════════════════════════════════════════════════════════

    void CrearCanvas()
    {
        var go = new GameObject("HUDCanvas");
        DontDestroyOnLoad(go);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 10;
        _scaler = go.AddComponent<CanvasScaler>();
        _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        _scaler.referenceResolution = new Vector2(1920, 1080);
        _scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
    }

    // ── Panel superior izquierdo: vida + armadura + dinero ────────────────
    void CrearPanelEstado()
    {
        var panel = Panel("PanelEstado", _canvas.transform,
            new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(10, -10), new Vector2(260, 120));

        _barraVida    = CrearBarra(panel, "BarraVida",    new Vector2(0,-10), COL_VIDA,    1f, 40);
        _barraArmadura= CrearBarra(panel, "BarraArmadura",new Vector2(0,-56), COL_ARMADURA,0f, 30);
        _txtVida      = CrearText(panel,  "TxtVida",      new Vector2(6,-10), Color.white, 13, FontStyle.Bold);
        _txtDinero    = CrearText(panel,  "TxtDinero",    new Vector2(6,-90), new Color(1f,0.9f,0.3f), 16, FontStyle.Bold);
    }

    // ── Panel superior derecho: wanted ────────────────────────────────────
    void CrearWanted()
    {
        var panel = Panel("PanelWanted", _canvas.transform,
            new Vector2(1, 1), new Vector2(1, 1),
            new Vector2(-10, -10), new Vector2(180, 50));

        for (int i = 0; i < 5; i++)
        {
            var go = new GameObject($"Estrella{i}");
            go.transform.SetParent(panel.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(30, 30);
            rt.anchorMin = rt.anchorMax = new Vector2(1,1);
            rt.anchoredPosition = new Vector2(-(i * 34f + 16f), -22f);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.3f, 0.3f, 0.3f, 0.7f);
            _estrellasWanted[i] = img;

            // Etiqueta estrella
            var lbl = new GameObject("Lbl");
            lbl.transform.SetParent(go.transform, false);
            var lt = lbl.AddComponent<RectTransform>();
            lt.sizeDelta = new Vector2(30,30); lt.anchoredPosition = Vector2.zero;
            var t = lbl.AddComponent<Text>();
            t.text = "★"; t.alignment = TextAnchor.MiddleCenter;
            t.fontSize = 18; t.color = new Color(0.4f,0.4f,0.4f);
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }

    // ── Apoyo popular ─────────────────────────────────────────────────────
    void CrearApoyo()
    {
        var panel = Panel("PanelApoyo", _canvas.transform,
            new Vector2(0,0), new Vector2(0,0),
            new Vector2(10, 10), new Vector2(200, 30));
        _barraApoyo = CrearBarra(panel, "BarraApoyo", new Vector2(0,0), COL_APOYO, 0.5f, 20);
        _txtApoyo   = CrearText(panel, "TxtApoyo", new Vector2(4,4), Color.white, 11, FontStyle.Normal);
    }

    // ── Hora y clima ──────────────────────────────────────────────────────
    void CrearHoraSClima()
    {
        var panel = Panel("PanelHora", _canvas.transform,
            new Vector2(0.5f,1), new Vector2(0.5f,1),
            new Vector2(-60,-6), new Vector2(120,32));
        _txtHora  = CrearText(panel,"TxtHora",  new Vector2(0,0), Color.white, 18, FontStyle.Bold);
        _txtHora.alignment = TextAnchor.MiddleCenter;
        _txtClima = CrearText(panel,"TxtClima", new Vector2(72,0), Color.white, 16, FontStyle.Normal);
    }

    // ── Velocímetro ───────────────────────────────────────────────────────
    void CrearVelocimetro()
    {
        _panelVelocimetro = Panel("PanelVelo", _canvas.transform,
            new Vector2(1,0), new Vector2(1,0),
            new Vector2(-10,10), new Vector2(100,100)).gameObject;
        _panelVelocimetro.SetActive(false);

        // Fondo circular
        var fondo = new GameObject("FondoVelo");
        fondo.transform.SetParent(_panelVelocimetro.transform, false);
        var rt = fondo.AddComponent<RectTransform>(); rt.sizeDelta = new Vector2(90,90);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f,0.5f); rt.anchoredPosition = Vector2.zero;
        var img = fondo.AddComponent<Image>();
        img.color = new Color(0.05f,0.05f,0.08f,0.88f);

        // Aguja (línea rotada)
        var aguja = new GameObject("Aguja");
        aguja.transform.SetParent(_panelVelocimetro.transform, false);
        var art = aguja.AddComponent<RectTransform>();
        art.sizeDelta = new Vector2(3, 30); art.pivot = new Vector2(0.5f, 0f);
        art.anchorMin = art.anchorMax = new Vector2(0.5f,0.5f); art.anchoredPosition = Vector2.zero;
        _agujaVelocimetro = aguja.AddComponent<Image>(); _agujaVelocimetro.color = new Color(1f,0.8f,0.2f);

        _txtVelocidad = CrearText(_panelVelocimetro.GetComponent<RectTransform>(),
            "TxtVelo", new Vector2(-10,-26), Color.white, 14, FontStyle.Bold);
        _txtVelocidad.alignment = TextAnchor.MiddleCenter;
        _txtVelocidad.text = "0";

        var km = CrearText(_panelVelocimetro.GetComponent<RectTransform>(),
            "TxtKMH", new Vector2(-10,-40), new Color(0.7f,0.7f,0.7f), 9, FontStyle.Normal);
        km.alignment = TextAnchor.MiddleCenter; km.text = "km/h";
    }

    // ── Brújula ───────────────────────────────────────────────────────────
    void CrearBrujula()
    {
        var panel = Panel("PanelBrujula", _canvas.transform,
            new Vector2(0.5f,1), new Vector2(0.5f,1),
            new Vector2(-120,-44), new Vector2(240,24));

        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = new Color(0,0,0,0.45f);

        _txtBrujula = CrearText(panel, "TxtBrujula", Vector2.zero, new Color(0.85f,0.85f,0.85f), 12, FontStyle.Normal);
        _txtBrujula.alignment = TextAnchor.MiddleCenter;

        // Tiras de puntos cardinales
        _brujulaStrip = new GameObject("BrujulaStrip").AddComponent<RectTransform>();
        _brujulaStrip.SetParent(panel, false);
        _brujulaStrip.sizeDelta = new Vector2(1440, 20);
        _brujulaStrip.anchorMin = _brujulaStrip.anchorMax = new Vector2(0.5f, 0.5f);
        string[] cards = { "N","45","E","135","S","225","O","315","N" };
        for (int i = 0; i < cards.Length; i++)
        {
            var t = new GameObject(cards[i]).AddComponent<Text>();
            t.text = cards[i]; t.alignment = TextAnchor.MiddleCenter;
            t.fontSize = 9; t.color = cards[i].Length == 1
                ? new Color(1f,0.8f,0.2f) : new Color(0.6f,0.6f,0.6f);
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var tr = t.GetComponent<RectTransform>();
            tr.SetParent(_brujulaStrip, false);
            tr.sizeDelta = new Vector2(36,20);
            tr.anchorMin = tr.anchorMax = new Vector2(0,0.5f);
            tr.anchoredPosition = new Vector2(i * 180f - 90f, 0);
        }
    }

    // ── Crosshair ────────────────────────────────────────────────────────
    void CrearCrosshair()
    {
        var parent = _canvas.transform;
        _crossH     = CrearLinea("CrossH", parent, new Vector2(_crossSize*2,2));
        _crossV     = CrearLinea("CrossV", parent, new Vector2(2,_crossSize*2));
        _crossPunto = CrearLinea("CrossPunto", parent, new Vector2(3,3));
        foreach (var img in new[]{_crossH,_crossV,_crossPunto})
        {
            var rt = img.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f,0.5f);
            rt.anchoredPosition = Vector2.zero;
            img.color = new Color(1f,1f,1f,0.82f);
        }
    }

    // ── Minimapa ─────────────────────────────────────────────────────────
    void CrearMinimap()
    {
        // RenderTexture
        _miniRT = new RenderTexture(256, 256, 16);
        _miniRT.Create();

        // Cámara superior ortográfica
        var camGO = new GameObject("MinimapCam");
        _miniCam = camGO.AddComponent<Camera>();
        _miniCam.orthographic = true;
        _miniCam.orthographicSize = 80f;
        _miniCam.farClipPlane = 500f;
        _miniCam.cullingMask = ~0; // todo
        _miniCam.targetTexture = _miniRT;
        _miniCam.clearFlags = CameraClearFlags.SolidColor;
        // BUG FIX (auditoría): sin auto-render cada frame; se renderiza manualmente con throttle.
        _miniCam.enabled = false;
        _miniCam.backgroundColor = new Color(0.08f, 0.10f, 0.14f);

        // Marco circular (panel)
        var panel = Panel("PanelMinimap", _canvas.transform,
            new Vector2(1,0), new Vector2(1,0),
            new Vector2(-10, 120), new Vector2(140, 140));

        _minimarco = panel.gameObject.AddComponent<Image>();
        _minimarco.color = COL_HUD_BG;

        // Imagen de la RenderTexture
        var raw = new GameObject("MinimapImg");
        raw.transform.SetParent(panel, false);
        var rawRT = raw.AddComponent<RectTransform>();
        rawRT.anchorMin = Vector2.zero; rawRT.anchorMax = Vector2.one;
        rawRT.offsetMin = rawRT.offsetMax = Vector2.zero;
        _minimapa = raw.AddComponent<RawImage>();
        _minimapa.texture = _miniRT;

        // Icono del jugador (punto central)
        var dot = new GameObject("MinimapJugador");
        dot.transform.SetParent(panel, false);
        var dotRT = dot.AddComponent<RectTransform>();
        dotRT.sizeDelta = new Vector2(8,8);
        dotRT.anchorMin = dotRT.anchorMax = new Vector2(0.5f,0.5f);
        dotRT.anchoredPosition = Vector2.zero;
        dot.AddComponent<Image>().color = new Color(0.3f,1f,0.4f);
    }

    // ── Texto de misión ───────────────────────────────────────────────────
    void CrearMisionTexto()
    {
        var panel = Panel("PanelMision", _canvas.transform,
            new Vector2(0,0), new Vector2(0,0),
            new Vector2(10, 50), new Vector2(360, 70));

        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = new Color(0,0,0,0.55f);

        _txtMisionTitulo   = CrearText(panel,"TxtMTitulo",  new Vector2(10,42), new Color(1f,0.9f,0.3f), 13, FontStyle.Bold);
        _txtMisionObjetivo = CrearText(panel,"TxtMObjetivo",new Vector2(10,20), new Color(0.85f,0.85f,0.85f), 11, FontStyle.Normal);
        _txtMisionTitulo.text = "";
        _txtMisionObjetivo.text = "";
    }

    // ════════════════════════════════════════════════════════════════════════
    //  API PÚBLICA
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Añade marcador de misión en world space sobre un transform.</summary>
    public static void AnadirMarcador(Transform objetivo, string label, Color color)
    {
        if (I == null || I._canvas == null) return;
        var go = new GameObject($"Marcador_{label}");
        go.transform.SetParent(I._canvas.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(80, 30);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);

        var img = go.AddComponent<Image>();
        img.color = new Color(color.r, color.g, color.b, 0.75f);

        var textoGO = new GameObject("Lbl");
        textoGO.transform.SetParent(go.transform, false);
        var trt = textoGO.AddComponent<RectTransform>();
        trt.sizeDelta = new Vector2(80,30); trt.anchoredPosition = Vector2.zero;
        var txt = textoGO.AddComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 10; txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleCenter;

        I._marcadores.Add(new MarcadorMision { objetivo = objetivo, imagen = img, texto = txt, label = label });
    }

    public static void EliminarMarcadores() { if (I == null) return;
        foreach (var m in I._marcadores) if (m.imagen != null) Destroy(m.imagen.gameObject);
        I._marcadores.Clear(); }

    /// <summary>Muestra indicador de dirección del daño (arco rojo en borde de pantalla).</summary>
    public static void MostrarDano(Vector3 origenMundo)
    {
        if (I == null || I._canvas == null) return;
        var go = new GameObject("DanoInd");
        go.transform.SetParent(I._canvas.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(80, 12);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);

        // Calcular ángulo hacia el origen del daño en espacio pantalla
        var cam = Camera.main;
        if (cam != null && AltsasuCore.Jugador != null)
        {
            Vector3 dir = origenMundo - AltsasuCore.Jugador.position;
            dir.y = 0;
            float angle = Vector3.SignedAngle(cam.transform.forward, dir, Vector3.up);
            rt.localEulerAngles = new Vector3(0, 0, -angle);
            float dist = 200f;
            rt.anchoredPosition = new Vector2(Mathf.Sin(angle * Mathf.Deg2Rad) * dist,
                                              Mathf.Cos(angle * Mathf.Deg2Rad) * dist);
        }
        var img = go.AddComponent<Image>();
        img.color = new Color(1f, 0.1f, 0.1f, 0.8f);
        const float DUR = 1.2f;
        I._danoIndicators.Add(new DanoIndicator { image = img, timer = DUR, duracion = DUR });
    }

    /// <summary>Muestra pantalla de victoria al completar M12.</summary>
    public static void MostrarVictoria()
    {
        if (I == null) return;
        var go = new GameObject("PanelVictoria");
        go.transform.SetParent(I._canvas.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var bg = go.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.75f);
        var txt = new GameObject("TxtVictoria").AddComponent<Text>();
        txt.transform.SetParent(go.transform, false);
        var rtt = txt.GetComponent<RectTransform>();
        rtt.anchorMin = Vector2.zero; rtt.anchorMax = Vector2.one;
        rtt.offsetMin = rtt.offsetMax = Vector2.zero;
        txt.text = "★ ASKATASUNA ★\nEl pueblo de Alsasua ha resistido.";
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 36; txt.fontStyle = FontStyle.Bold;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = new Color(1f, 0.9f, 0.2f);
        I.StartCoroutine(I.FadeOutVictoria(go, 8f));
    }

    System.Collections.IEnumerator FadeOutVictoria(GameObject panel, float delay)
    {
        yield return new WaitForSeconds(delay);
        var imgs = panel.GetComponentsInChildren<Graphic>();
        float t = 0f;
        while (t < 2f)
        {
            t += Time.deltaTime;
            float a = 1f - t / 2f;
            foreach (var g in imgs) { var c = g.color; c.a = a; g.color = c; }
            yield return null;
        }
        Destroy(panel);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CALLBACKS
    // ════════════════════════════════════════════════════════════════════════

    // BUG FIX (auditoría): apuntar al atacante real, no al origen del mundo.
    void OnDano(int cantidad) => MostrarDano(ControladorJugador.UltimoOrigenDano);

    void OnWanted(int nivel)
    {
        _wantedActual = nivel;
        for (int i = 0; i < 5; i++)
        {
            if (_estrellasWanted[i] == null) continue;
            bool activa = i < nivel;
            _estrellasWanted[i].color = activa ? COL_WANTED : new Color(0.3f,0.3f,0.3f,0.7f);
            if (activa) StartCoroutine(PulsarEstrella(_estrellasWanted[i]));
        }
    }

    IEnumerator PulsarEstrella(Image img)
    {
        float t = 0f;
        while (t < 0.3f) { t += Time.unscaledDeltaTime;
            img.transform.localScale = Vector3.one * (1f + Mathf.Sin(t * Mathf.PI / 0.3f) * 0.4f);
            yield return null; }
        img.transform.localScale = Vector3.one;
    }

    void OnMisionIniciada(string nombre)
    {
        if (_txtMisionTitulo   != null) _txtMisionTitulo.text   = nombre;
        if (_txtMisionObjetivo != null) _txtMisionObjetivo.text = "▶ ...";
        // Banner (texto propio + slide)
        if (_bannerTxtTitulo   != null) _bannerTxtTitulo.text   = nombre;
        if (_bannerTxtObjetivo != null) _bannerTxtObjetivo.text = "▶ ...";
        MostrarBanner(true);
    }
    void OnObjetivoCompletado(string desc)
    {
        if (_txtMisionObjetivo != null) _txtMisionObjetivo.text = "✅ " + desc;
        if (_bannerTxtObjetivo != null) _bannerTxtObjetivo.text = "✅ " + desc;
        StartCoroutine(FlashMision());
        StartCoroutine(FlashBannerVerde());
    }
    void OnMisionCompletada(string nombre)
    {
        if (_txtMisionTitulo   != null) _txtMisionTitulo.text   = "🏆 " + nombre;
        if (_txtMisionObjetivo != null) _txtMisionObjetivo.text = "¡Misión completada!";
        if (_bannerTxtTitulo   != null) _bannerTxtTitulo.text   = "🏆 " + nombre;
        if (_bannerTxtObjetivo != null) _bannerTxtObjetivo.text = "¡Misión completada!";
        StartCoroutine(OcultarBannerTras(6f));
        StartCoroutine(OcultarMisionTrasDelay(5f));
    }

    IEnumerator FlashMision()
    {
        if (_txtMisionObjetivo == null) yield break;
        Color orig = _txtMisionObjetivo.color;
        for (int i = 0; i < 3; i++)
        {
            _txtMisionObjetivo.color = COL_WANTED; yield return new WaitForSecondsRealtime(0.15f);
            _txtMisionObjetivo.color = orig;        yield return new WaitForSecondsRealtime(0.15f);
        }
    }

    IEnumerator OcultarMisionTrasDelay(float t)
    {
        yield return new WaitForSecondsRealtime(t);
        if (_txtMisionTitulo   != null) _txtMisionTitulo.text   = "";
        if (_txtMisionObjetivo != null) _txtMisionObjetivo.text = "";
    }

    // ════════════════════════════════════════════════════════════════════════
    //  HELPERS DE CONSTRUCCIÓN
    // ════════════════════════════════════════════════════════════════════════

    RectTransform Panel(string nombre, Transform padre, Vector2 anchorMin, Vector2 anchorMax,
                        Vector2 pos, Vector2 size)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.pivot = anchorMin;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    Slider CrearBarra(RectTransform padre, string nombre, Vector2 pos, Color color, float valor, int altura)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0,1); rt.anchorMax = new Vector2(1,1);
        rt.pivot = new Vector2(0,1);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(-20, altura);

        var sl = go.AddComponent<Slider>();
        sl.minValue = 0f; sl.maxValue = 1f; sl.value = valor;

        // Background
        var bg = new GameObject("BG"); bg.transform.SetParent(go.transform, false);
        var bgRT = bg.AddComponent<RectTransform>(); bgRT.anchorMin=Vector2.zero; bgRT.anchorMax=Vector2.one; bgRT.offsetMin=bgRT.offsetMax=Vector2.zero;
        var bgImg = bg.AddComponent<Image>(); bgImg.color = new Color(0.1f,0.1f,0.1f,0.8f);

        // Fill area
        var fa = new GameObject("FillArea"); fa.transform.SetParent(go.transform,false);
        var faRT = fa.AddComponent<RectTransform>(); faRT.anchorMin=Vector2.zero; faRT.anchorMax=Vector2.one; faRT.offsetMin=faRT.offsetMax=Vector2.zero;

        var fill = new GameObject("Fill"); fill.transform.SetParent(fa.transform,false);
        var fillRT = fill.AddComponent<RectTransform>(); fillRT.anchorMin=Vector2.zero; fillRT.anchorMax=Vector2.one; fillRT.offsetMin=fillRT.offsetMax=Vector2.zero;
        var fillImg = fill.AddComponent<Image>(); fillImg.color = color;

        sl.fillRect = fillRT;
        if (nombre == "BarraVida")    _fillVida     = fillImg;
        if (nombre == "BarraArmadura")_fillArmadura = fillImg;
        return sl;
    }

    Text CrearText(RectTransform padre, string nombre, Vector2 pos, Color color, int size, FontStyle style)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0,1); rt.anchorMax = new Vector2(1,1);
        rt.pivot = new Vector2(0,1); rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(-10,size+4);
        var t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = size; t.color = color; t.fontStyle = style;
        go.AddComponent<Shadow>().effectColor = new Color(0,0,0,0.7f);
        return t;
    }

    Image CrearLinea(string nombre, Transform padre, Vector2 size)
    {
        var go = new GameObject(nombre); go.transform.SetParent(padre, false);
        go.AddComponent<RectTransform>().sizeDelta = size;
        return go.AddComponent<Image>();
    }

    string ClimaIcono()
    {
        var clima = AltsasuCore.I?.climaSystem;
        if (clima == null) return "☀";
        return clima.climaActual switch {
            SistemaClima.EstadoClima.Sol          => "☀",
            SistemaClima.EstadoClima.Nublado       => "☁",
            SistemaClima.EstadoClima.LluviaLigera  => "🌧",
            SistemaClima.EstadoClima.Tormenta      => "⛈",
            SistemaClima.EstadoClima.Niebla        => "🌫",
            SistemaClima.EstadoClima.NieveLigera   => "❄",
            _ => "☀"
        };
    }

    void OnDestroy()
    {
        DesuscribirEventos();
        if (_miniRT != null) _miniRT.Release();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  BANNER DESLIZABLE DE MISIÓN
    // ════════════════════════════════════════════════════════════════════════

    void CrearBannerMision()
    {
        _bannerMision = Panel("BannerMision", _canvas.transform,
            new Vector2(0, 0), new Vector2(0, 0),
            new Vector2(-420f, 60f), new Vector2(390f, 80f));

        _bgBanner = _bannerMision.gameObject.AddComponent<Image>();
        _bgBanner.color = new Color(0.06f, 0.06f, 0.10f, 0.92f);

        // Borde azul izquierdo
        var borde = Panel("Borde", _bannerMision,
            new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(5f, 0f));
        borde.gameObject.AddComponent<Image>().color = new Color(0.3f, 0.6f, 1f);

        _bannerTxtTitulo   = CrearText(_bannerMision, "BannerTitulo",
            new Vector2(14f, 54f), new Color(1f, 0.92f, 0.4f), 13, FontStyle.Bold);
        _bannerTxtObjetivo = CrearText(_bannerMision, "BannerObjetivo",
            new Vector2(14f, 28f), new Color(0.85f, 0.85f, 0.9f), 11, FontStyle.Normal);
    }

    void MostrarBanner(bool visible)
    {
        if (_bannerVisible == visible || _bannerMision == null) return;
        _bannerVisible = visible;
        StopCoroutine("AnimarBanner");
        StartCoroutine(AnimarBanner(visible));
    }

    IEnumerator AnimarBanner(bool mostrar)
    {
        float dur = 0.35f, t = 0f;
        float desde = _bannerMision.anchoredPosition.x;
        float hasta = mostrar ? 16f : -440f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            _bannerMision.anchoredPosition = new Vector2(Mathf.Lerp(desde, hasta, t / dur), 60f);
            yield return null;
        }
        _bannerMision.anchoredPosition = new Vector2(hasta, 60f);
    }

    IEnumerator FlashBannerVerde()
    {
        if (_bgBanner == null) yield break;
        Color orig = _bgBanner.color;
        for (int i = 0; i < 3; i++)
        {
            _bgBanner.color = new Color(0.08f, 0.28f, 0.08f, 0.92f);
            yield return new WaitForSecondsRealtime(0.12f);
            _bgBanner.color = orig;
            yield return new WaitForSecondsRealtime(0.12f);
        }
    }

    IEnumerator OcultarBannerTras(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        MostrarBanner(false);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  NOTIFICACIONES DE LOGROS
    // ════════════════════════════════════════════════════════════════════════

    void CrearPanelNotificaciones()
    {
        _panelNotif = Panel("PanelNotif", _canvas.transform,
            new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(420f, -20f), new Vector2(380f, 70f)); // fuera de pantalla (derecha)

        var bg = _panelNotif.gameObject.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.12f, 0.95f);

        var borde = Panel("BordeNotif", _panelNotif,
            new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(4f, 0f));
        borde.gameObject.AddComponent<Image>().color = new Color(1f, 0.85f, 0.1f);

        _txtNotif = CrearText(_panelNotif, "TxtNotif",
            new Vector2(12f, 38f), new Color(1f, 0.95f, 0.7f), 11, FontStyle.Bold);
    }

    void OnLogro(SistemaLogros.Logro logro)
        => _notifQueue.Enqueue($"{logro.Icono}  {logro.Nombre}\n{logro.Descripcion}");

    IEnumerator ProcesarColaNotificaciones()
    {
        while (true)
        {
            if (_notifQueue.Count > 0 && !_notifMostrando)
                yield return StartCoroutine(MostrarNotificacion(_notifQueue.Dequeue()));
            yield return new WaitForSecondsRealtime(0.5f);
        }
    }

    IEnumerator MostrarNotificacion(string msg)
    {
        _notifMostrando = true;
        if (_txtNotif != null) _txtNotif.text = msg;

        // Slide desde la derecha
        float dur = 0.3f, t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float x = Mathf.Lerp(420f, -16f, t / dur);
            if (_panelNotif != null) _panelNotif.anchoredPosition = new Vector2(x, -20f);
            yield return null;
        }

        yield return new WaitForSecondsRealtime(3.5f);

        // Slide hacia fuera
        t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float x = Mathf.Lerp(-16f, 420f, t / dur);
            if (_panelNotif != null) _panelNotif.anchoredPosition = new Vector2(x, -20f);
            yield return null;
        }

        _notifMostrando = false;
    }

    // ── Tipos auxiliares ──────────────────────────────────────────────────
    class DanoIndicator { public Image image; public float timer, duracion; }
    class MarcadorMision { public Transform objetivo; public Image imagen; public Text texto; public string label; }
}
