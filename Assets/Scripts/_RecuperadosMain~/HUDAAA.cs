// HUDAAA.cs — HUD unificado estilo GTA con todas las barras del juego.
// Vida · Armadura · Dinero · Wanted stars · Apoyo · Paranoia · Honor
// Arma actual · Hora del día · Clima · Mini indicadores

using UnityEngine;
using UnityEngine.UI;

public class HUDAAA : MonoBehaviour
{
    // ── Referencias (se autoconstruyen si faltan) ─────────────────────────
    Canvas  _canvas;

    // Paneles
    GameObject _panelEstado;
    GameObject _panelArma;
    GameObject _panelSuper;

    // Barras de estado — usamos Slider para controlar el fill correctamente
    Slider   _barraVida, _barraArmadura;
    Slider   _barraApoyo, _barraHonor, _barraParanoia;
    Text     _txtVida, _txtDinero, _txtWanted, _txtArma;
    Text     _txtHora, _txtClima, _txtApoyo, _txtParanoia;
    Image    _iconoClima;

    // Panel de vehículo
    GameObject _panelVehiculo;
    Text       _txtVelocidad;

    // Referencias a sistemas
    GameManagerAltsasua      _gm;
    SistemaApoyoPopular      _apoyo;
    SistemaClima             _clima;
    SistemaAtmosfera         _atmosfera;
    SistemaArmasExtendido    _armas;
    ControladorVehiculoJugador _vehiculo;

    // Colores
    static readonly Color COL_VIDA      = new(0.95f, 0.25f, 0.25f);
    static readonly Color COL_ARMADURA  = new(0.35f, 0.70f, 1.00f);
    static readonly Color COL_APOYO     = new(0.25f, 0.85f, 0.35f);
    static readonly Color COL_HONOR     = new(1.00f, 0.85f, 0.10f);
    static readonly Color COL_PARANOIA  = new(0.90f, 0.15f, 0.90f);
    static readonly Color COL_FONDO     = new(0.00f, 0.00f, 0.00f, 0.65f);

    void Start()
    {
        // Buscar o crear Canvas
        _canvas = FindFirstObjectByType<Canvas>();
        if (_canvas == null)
        {
            var cgo = new GameObject("Canvas_HUDAAA");
            _canvas = cgo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;
            cgo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cgo.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
            cgo.AddComponent<GraphicRaycaster>();
        }

        ConstruirHUD();
        BuscarSistemas();
    }

    void Update()
    {
        BuscarSistemas();
        ActualizarHUD();
    }

    void BuscarSistemas()
    {
        if (_gm        == null) _gm        = FindFirstObjectByType<GameManagerAltsasua>();
        if (_apoyo     == null) _apoyo     = SistemaApoyoPopular.Instance;
        if (_clima     == null) _clima     = FindFirstObjectByType<SistemaClima>();
        if (_atmosfera == null) _atmosfera = FindFirstObjectByType<SistemaAtmosfera>();
        if (_armas     == null) _armas     = FindFirstObjectByType<SistemaArmasExtendido>();

        // Vehiculo: mantener referencia si el jugador sigue dentro; buscar uno nuevo si no
        if (_vehiculo == null || !_vehiculo.JugadorDentro)
            _vehiculo = FindFirstObjectByType<ControladorVehiculoJugador>() ?? _vehiculo;
    }

    // ── Actualización ─────────────────────────────────────────────────────

    void ActualizarHUD()
    {
        // Vida y armadura del GameManager
        if (_gm != null)
        {
            float pctVida = (float)_gm.dinero / Mathf.Max(1, _gm.dinero);  // placeholder
            // vida real desde Health del jugador
            var jugador = AltsasuCore.Jugador;
            if (jugador != null)
            {
                // Health no existe — usar ControladorJugador.Vida / RatioVida
                var ctrl = jugador.GetComponent<ControladorJugador>();
                if (ctrl != null)
                {
                    if (_barraVida != null) _barraVida.value = ctrl.RatioVida;
                    if (_txtVida   != null) _txtVida.text = $"❤ {ctrl.Vida}";
                }
            }
            // Dinero y wanted
            if (_txtDinero  != null) _txtDinero.text  = $"${_gm.dinero:N0}";
            if (_txtWanted  != null) _txtWanted.text  = new string('★', _gm.nivelBusqueda) + new string('☆', 5 - _gm.nivelBusqueda);
        }

        // Apoyo, honor, paranoia
        if (_apoyo != null)
        {
            if (_barraApoyo    != null) _barraApoyo.value    = _apoyo.apoyo    / 100f;
            if (_barraHonor    != null) _barraHonor.value    = _apoyo.honor    / 100f;
            if (_barraParanoia != null) _barraParanoia.value = _apoyo.paranoia / 100f;
            if (_txtApoyo      != null) _txtApoyo.text    = $"Apoyo: {_apoyo.apoyo:F0}%";
            if (_txtParanoia   != null)
            {
                _txtParanoia.text  = _apoyo.paranoia > 70f
                    ? $"⚠ PARANOIA: {_apoyo.paranoia:F0}%"
                    : $"Paranoia: {_apoyo.paranoia:F0}%";
                _txtParanoia.color = _apoyo.paranoia > 70f ? COL_PARANOIA : Color.white;
            }
        }

        // Arma
        if (_armas != null && _txtArma != null)
            _txtArma.text = _armas.armaActual switch {
                SistemaArmasExtendido.TipoArma.Spray          => "🎨 SPRAY",
                SistemaArmasExtendido.TipoArma.Tirachinas     => "🪨 TIRACHINAS",
                SistemaArmasExtendido.TipoArma.Molotov        => "🔥 MOLOTOV",
                SistemaArmasExtendido.TipoArma.BanderaEuskadi => "🏴 IKURRIÑA",
                SistemaArmasExtendido.TipoArma.BombaLapa      => "💣 BOMBA LAPA",
                SistemaArmasExtendido.TipoArma.CocheBomba     => "🚗💥 COCHE BOMBA",
                _ => _armas.armaActual.ToString()
            };

        // Panel vehículo: visible solo cuando el jugador está conduciendo
        bool enCoche = _vehiculo != null && _vehiculo.JugadorDentro;
        if (_panelVehiculo != null) _panelVehiculo.SetActive(enCoche);
        if (enCoche && _txtVelocidad != null)
            _txtVelocidad.text = $"{_vehiculo.VelocidadKmh:F0} km/h";

        // Hora y clima
        if (_atmosfera != null && _txtHora != null)
        {
            float h = _atmosfera.HoraDelDia;
            _txtHora.text = $"{(int)h:00}:{(int)((h % 1f) * 60):00}";
        }
        if (_clima    != null && _txtClima != null) _txtClima.text = _clima.climaActual switch {
            SistemaClima.EstadoClima.Sol           => "☀",
            SistemaClima.EstadoClima.Nublado       => "☁",
            SistemaClima.EstadoClima.LluviaLigera  => "🌧",
            SistemaClima.EstadoClima.Tormenta      => "⛈",
            SistemaClima.EstadoClima.Niebla        => "🌫",
            SistemaClima.EstadoClima.NieveLigera   => "❄",
            _ => "?"
        };
    }

    // ── Construcción del HUD ──────────────────────────────────────────────

    void ConstruirHUD()
    {
        // ── Panel inferior izquierdo: estado del jugador ──────────────────
        _panelEstado = CrearPanel("PanelEstado", new Vector2(10, 10), new Vector2(220, 130), new Vector2(0,0), new Vector2(0,0));

        // Barra vida
        _barraVida     = CrearBarra(_panelEstado.transform, "BarraVida",  new Vector2(10,110), new Vector2(180,14), COL_VIDA);
        _txtVida       = CrearTexto(_panelEstado.transform, "TxtVida",    new Vector2(10,95),  "❤ 100", 12, COL_VIDA);
        _barraArmadura = CrearBarra(_panelEstado.transform, "BarraArm",   new Vector2(10,80),  new Vector2(180,10), COL_ARMADURA);

        // Barras políticas
        _barraApoyo    = CrearBarra(_panelEstado.transform, "BarraApoyo", new Vector2(10,65),  new Vector2(180,10), COL_APOYO);
        _txtApoyo      = CrearTexto(_panelEstado.transform, "TxtApoyo",   new Vector2(10,50),  "Apoyo: 50%", 10, COL_APOYO);
        _barraHonor    = CrearBarra(_panelEstado.transform, "BarraHonor", new Vector2(10,38),  new Vector2(120,8),  COL_HONOR);
        _barraParanoia = CrearBarra(_panelEstado.transform, "BarraParan", new Vector2(10,26),  new Vector2(180,10), COL_PARANOIA);
        _txtParanoia   = CrearTexto(_panelEstado.transform, "TxtParan",   new Vector2(10,12),  "Paranoia: 0%", 9, Color.white);

        // ── Panel superior izquierdo: dinero y wanted ─────────────────────
        _panelSuper  = CrearPanel("PanelSuper", new Vector2(10, -10), new Vector2(240, 56), new Vector2(0,1), new Vector2(0,1));
        _txtDinero   = CrearTexto(_panelSuper.transform, "TxtDinero",  new Vector2(10,40), "$500", 22, new Color(0.1f,1f,0.3f));
        _txtWanted   = CrearTexto(_panelSuper.transform, "TxtWanted",  new Vector2(10,14), "☆☆☆☆☆", 24, new Color(1f,0.85f,0f));

        // ── Panel inferior derecho: arma, hora, clima ─────────────────────
        _panelArma   = CrearPanel("PanelArma",  new Vector2(-10, 10), new Vector2(180, 70), new Vector2(1,0), new Vector2(1,0));
        _txtArma     = CrearTexto(_panelArma.transform, "TxtArma",   new Vector2(-10,55), "👊 PUÑOS", 16, Color.white);
        _txtHora     = CrearTexto(_panelArma.transform, "TxtHora",   new Vector2(-10,32), "16:00",   18, new Color(1f,0.95f,0.7f));
        _txtClima    = CrearTexto(_panelArma.transform, "TxtClima",  new Vector2(-10,10), "☀",       22, Color.white);

        // Configurar anchors correctos de los textos
        FixAnchor(_txtDinero.rectTransform,   new Vector2(0,1));
        FixAnchor(_txtWanted.rectTransform,   new Vector2(0,1));
        FixAnchor(_txtArma.rectTransform,     new Vector2(1,1));
        FixAnchor(_txtHora.rectTransform,     new Vector2(1,1));
        FixAnchor(_txtClima.rectTransform,    new Vector2(1,1));

        // ── Panel inferior central: velocímetro de vehículo ──────────────────
        // Empieza oculto; ActualizarHUD lo activa solo cuando JugadorDentro
        _panelVehiculo = CrearPanel("PanelVehiculo", new Vector2(-90, 10), new Vector2(180, 52),
                                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        _txtVelocidad  = CrearTexto(_panelVehiculo.transform, "TxtVelocidad",
                                    new Vector2(10, 42), "0 km/h", 28, Color.white);
        FixAnchor(_txtVelocidad.rectTransform, new Vector2(0f, 1f));
        var lblVehiculo = CrearTexto(_panelVehiculo.transform, "LblVehiculo",
                                    new Vector2(10, 14), "VELOCIDAD", 9, new Color(1f, 1f, 1f, 0.6f));
        FixAnchor(lblVehiculo.rectTransform, new Vector2(0f, 1f));
        _panelVehiculo.SetActive(false);

        // Ayuda de teclas (pequeña, esquina superior derecha)
        var ayuda = CrearTexto(_canvas.transform, "TxtAyuda",
            new Vector2(-10, -10), "M=Manifa | Ctrl+W=Clima | G=Grafiti | 1-9=Armas",
            9, new Color(1,1,1,0.5f));
        FixAnchor(ayuda.rectTransform, new Vector2(1,1), new Vector2(1,1));
    }

    // ── Helpers de construcción UI ────────────────────────────────────────

    GameObject CrearPanel(string nombre, Vector2 pos, Vector2 size, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(_canvas.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.pivot     = anchorMin;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        go.AddComponent<Image>().color = COL_FONDO;
        return go;
    }

    // Devuelve el Slider para controlar el fill con .value (0-1)
    static Slider CrearBarra(Transform padre, string nombre, Vector2 pos, Vector2 size, Color color)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0,1); rt.anchorMax = new Vector2(0,1);
        rt.pivot = new Vector2(0,1); rt.anchoredPosition = pos; rt.sizeDelta = size;

        // Fondo oscuro
        var bg = new GameObject("BG"); bg.transform.SetParent(go.transform, false);
        var bgRT = bg.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one; bgRT.sizeDelta = Vector2.zero;
        bg.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 0.8f);

        // Fill area
        var fillArea = new GameObject("FillArea"); fillArea.transform.SetParent(go.transform, false);
        var faRT = fillArea.AddComponent<RectTransform>();
        faRT.anchorMin = new Vector2(0,0.25f); faRT.anchorMax = new Vector2(1,0.75f);
        faRT.offsetMin = new Vector2(5,0); faRT.offsetMax = new Vector2(-5,0);

        // Fill image
        var fill = new GameObject("Fill"); fill.transform.SetParent(fillArea.transform, false);
        var fillRT = fill.AddComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one; fillRT.sizeDelta = Vector2.zero;
        fill.AddComponent<Image>().color = color;

        // Slider — apunta fillRect al RectTransform del fill
        var slider = go.AddComponent<Slider>();
        slider.interactable = false;
        slider.fillRect     = fillRT;       // ← clave: fillRect controla el tamaño del fill
        slider.direction    = Slider.Direction.LeftToRight;
        slider.minValue = 0f; slider.maxValue = 1f; slider.value = 1f;

        return slider; // devolver Slider para asignar .value
    }

    static Text CrearTexto(Transform padre, string nombre, Vector2 pos, string contenido, int size, Color color)
    {
        var go = new GameObject(nombre); go.transform.SetParent(padre, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0,1); rt.anchorMax = new Vector2(0,1);
        rt.pivot = new Vector2(0,1); rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(200, size + 4);
        var t = go.AddComponent<Text>();
        t.text = contenido; t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = size; t.color = color; t.fontStyle = FontStyle.Bold;
        go.AddComponent<Shadow>().effectColor = new Color(0,0,0,0.8f);
        return t;
    }

    static void FixAnchor(RectTransform rt, Vector2 anchor, Vector2? anchorMax = null)
    {
        rt.anchorMin = anchor; rt.anchorMax = anchorMax ?? anchor; rt.pivot = anchor;
    }

    // ── Pantalla de Victoria (M12 clímax) ────────────────────────────────
    private static bool _mostrandoVictoria = false;

    public static void MostrarVictoria()
    {
        _mostrandoVictoria = true;
        // Detener el tiempo del juego suavemente
        Time.timeScale = 0.3f;
        AlsasuaLogger.Info("HUDAAA", "★★★ ASKATASUNA ★★★ — Victoria del pueblo");
    }

    private void OnGUI()
    {
        if (!_mostrandoVictoria) return;

        // Fondo negro semitransparente
        var col = new Color(0f, 0f, 0f, 0.82f);
        GUI.color = col;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);

        // Texto central
        GUI.color = Color.white;
        var estilo = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 52,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            richText  = true,
            wordWrap  = true
        };

        float w = Screen.width;
        float h = Screen.height;

        GUI.Label(new Rect(0, h * 0.28f, w, 80),
            "<color=#FFD700>★  ASKATASUNA  ★</color>", estilo);

        estilo.fontSize = 26;
        GUI.Label(new Rect(0, h * 0.45f, w, 50),
            "<color=#AAFFAA>El pueblo de Alsasua ha resistido.</color>", estilo);

        estilo.fontSize = 18;
        GUI.Label(new Rect(0, h * 0.56f, w, 40),
            "<color=#CCCCCC>Gora Euskal Herria — Presoak Etxera</color>", estilo);

        // Botón para salir
        estilo.fontSize = 16;
        GUI.color = new Color(1f, 1f, 1f, 0.9f);
        if (GUI.Button(new Rect(w / 2f - 100f, h * 0.72f, 200f, 44f), "Volver al menú"))
        {
            _mostrandoVictoria = false;
            Time.timeScale = 1f;
            UnityEngine.SceneManagement.SceneManager.LoadScene(0);
        }
        GUI.color = Color.white;
    }
}
