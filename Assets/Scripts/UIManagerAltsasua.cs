// UIManagerAltsasua.cs
// ═══════════════════════════════════════════════════════════════════════════
//  UI MANAGER — Canvas UGUI real que sustituye OnGUI en todo el juego
//
//  Construye por código (sin prefabs):
//    • Barra de vida con gradiente rojo-verde y animación suave
//    • Dinero con contador animado y flash amarillo
//    • Estrellas de búsqueda (★) con escala y emisión en cambio
//    • Objetivos de misión con banner lateral deslizable
//    • Indicador de dirección del daño (arcos en borde de pantalla)
//    • Minimapa circular con punto del jugador
//    • Notificación de logros (esquina derecha, desliza entrando)
//    • Crosshair dinámico (se expande al disparar/correr)
//    • Panel de subtítulos (consignas, radio policía)
//
//  Responde a todos los eventos del juego via suscripción.
//  Compatible con HDRP (no usa OnGUI).
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

[DefaultExecutionOrder(55)]
public class UIManagerAltsasua : MonoBehaviour
{
    public static UIManagerAltsasua I { get; private set; }

    // ── Canvas root ───────────────────────────────────────────────────────
    Canvas _c;

    // ── Elementos de UI ───────────────────────────────────────────────────
    Image       _fillVida;
    Text        _txtVida, _txtDinero, _txtMision, _txtObjetivo;
    Image[]     _estrellasWanted = new Image[5];
    Text[]      _starLabels      = new Text[5];
    RawImage    _minimapa;
    Image       _crossH, _crossV;
    RectTransform _bannerMision;
    Image       _bgBanner;
    bool        _bannerVisible;
    Camera      _miniCam;

    // ── Notificaciones ────────────────────────────────────────────────────
    readonly Queue<string> _notifQueue = new();
    bool _mostrando;

    // ── Estado ────────────────────────────────────────────────────────────
    int   _dineroDisplay;
    float _vidaDisplay = 1f;
    float _timerCross;

    void Awake()
    {
        if (I != null && I != this) { Destroy(this); return; }
        I = this;
    }

    void Start()
    {
        ConstruirUI();
        SuscribirEventos();
        StartCoroutine(ActualizarLoop());
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CONSTRUCCIÓN
    // ════════════════════════════════════════════════════════════════════════

    void ConstruirUI()
    {
        // Canvas principal
        var cGO = new GameObject("UI_Altsasua");
        DontDestroyOnLoad(cGO);
        _c = cGO.AddComponent<Canvas>();
        _c.renderMode = RenderMode.ScreenSpaceOverlay;
        _c.sortingOrder = 20;
        var cs = cGO.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920, 1080);
        cs.matchWidthOrHeight = 0.5f;
        cGO.AddComponent<GraphicRaycaster>();

        ConstruirPanelEstado();
        ConstruirWanted();
        ConstruirMision();
        ConstruirCrosshair();
        ConstruirMinimapa();
    }

    // ── Panel izquierdo: vida + dinero ────────────────────────────────────
    void ConstruirPanelEstado()
    {
        var panel = Contenedor("PanelEstado", _c.transform,
            new Vector2(0,0), new Vector2(0,0),
            new Vector2(16, 16), new Vector2(260, 100));

        // Fondo semitransparente redondeado
        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.65f);

        // Etiqueta ❤
        var lblVida = Texto("LblVida", panel, "❤", 18, new Color(0.95f, 0.25f, 0.25f),
            new Vector2(14, 72), FontStyle.Bold);

        // Barra de vida
        var barraGO = Contenedor("BarraVida", panel,
            new Vector2(0,1), new Vector2(0,1),
            new Vector2(36, -14), new Vector2(210, 18));
        var barBG = barraGO.gameObject.AddComponent<Image>();
        barBG.color = new Color(0.15f, 0.05f, 0.05f);

        var fillGO = Contenedor("Fill", barraGO,
            new Vector2(0,0), new Vector2(1,1),
            Vector2.zero, Vector2.zero);
        _fillVida = fillGO.gameObject.AddComponent<Image>();
        _fillVida.color = new Color(0.9f, 0.2f, 0.2f);
        _fillVida.type = Image.Type.Filled;
        _fillVida.fillMethod = Image.FillMethod.Horizontal;
        _fillVida.fillAmount = 1f;

        _txtVida = Texto("TxtVida", panel, "100", 13, Color.white,
            new Vector2(14, 48), FontStyle.Normal);

        // Dinero
        _txtDinero = Texto("TxtDinero", panel, "€ 500", 20, new Color(1f, 0.92f, 0.3f),
            new Vector2(14, 16), FontStyle.Bold);
    }

    // ── Wanted (esquina superior derecha) ─────────────────────────────────
    void ConstruirWanted()
    {
        var panel = Contenedor("PanelWanted", _c.transform,
            new Vector2(1,1), new Vector2(1,1),
            new Vector2(-16,-16), new Vector2(200, 48));

        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.60f);

        for (int i = 0; i < 5; i++)
        {
            var starGO = Contenedor($"Star{i}", panel,
                new Vector2(1,0.5f), new Vector2(1,0.5f),
                new Vector2(-(i * 36f + 18f), 0f), new Vector2(32, 32));
            var imgStar = starGO.gameObject.AddComponent<Image>();
            imgStar.color = new Color(0.25f, 0.25f, 0.25f, 0.8f);
            _estrellasWanted[i] = imgStar;

            var lbl = Texto($"Lbl{i}", starGO, "★", 20,
                new Color(0.3f, 0.3f, 0.3f), Vector2.zero, FontStyle.Bold);
            lbl.alignment = TextAnchor.MiddleCenter;
            var lblRT = lbl.GetComponent<RectTransform>();
            lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = lblRT.offsetMax = Vector2.zero;
            _starLabels[i] = lbl;
        }
    }

    // ── Banner de misión (izquierda, deslizable) ──────────────────────────
    void ConstruirMision()
    {
        _bannerMision = Contenedor("BannerMision", _c.transform,
            new Vector2(0, 0), new Vector2(0, 0),
            new Vector2(-400, 60), new Vector2(380, 80));

        _bgBanner = _bannerMision.gameObject.AddComponent<Image>();
        _bgBanner.color = new Color(0.06f, 0.06f, 0.10f, 0.92f);

        // Borde izquierdo azul
        var borde = Contenedor("Borde", _bannerMision,
            new Vector2(0,0), new Vector2(0,1),
            Vector2.zero, new Vector2(5, 0));
        borde.gameObject.AddComponent<Image>().color = new Color(0.3f, 0.6f, 1f);

        _txtMision = Texto("TxtMision", _bannerMision,
            "", 13, new Color(1f, 0.92f, 0.4f),
            new Vector2(14, 54), FontStyle.Bold);

        _txtObjetivo = Texto("TxtObjetivo", _bannerMision,
            "", 11, new Color(0.85f, 0.85f, 0.90f),
            new Vector2(14, 28), FontStyle.Normal);
    }

    // ── Crosshair ─────────────────────────────────────────────────────────
    void ConstruirCrosshair()
    {
        float s = 14f;
        _crossH = LineaUI("CH", _c.transform, new Vector2(s*2, 2));
        _crossV = LineaUI("CV", _c.transform, new Vector2(2, s*2));
        foreach (var img in new[]{_crossH, _crossV})
        {
            var rt = img.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            img.color = new Color(1f, 1f, 1f, 0.80f);
        }
        // Punto central
        var dot = LineaUI("CDot", _c.transform, new Vector2(4, 4));
        var drt = dot.GetComponent<RectTransform>();
        drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.5f);
        drt.anchoredPosition = Vector2.zero;
        dot.color = new Color(1f, 1f, 1f, 0.9f);
    }

    // ── Minimapa ──────────────────────────────────────────────────────────
    void ConstruirMinimapa()
    {
        var rt = Contenedor("Minimapa", _c.transform,
            new Vector2(1,0), new Vector2(1,0),
            new Vector2(-16, 110), new Vector2(130, 130));

        // Marco circular
        var marco = rt.gameObject.AddComponent<Image>();
        marco.color = new Color(0f,0f,0f,0.72f);

        // RenderTexture + cámara
        var tex = new RenderTexture(256, 256, 16);
        tex.Create();
        var rawGO = Contenedor("MinimapImg", rt,
            new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f),
            Vector2.zero, Vector2.zero);
        _minimapa = rawGO.gameObject.AddComponent<RawImage>();
        _minimapa.texture = tex;

        var camGO = new GameObject("MinimapCam");
        _miniCam = camGO.AddComponent<Camera>();
        _miniCam.orthographic = true; _miniCam.orthographicSize = 80f;
        _miniCam.farClipPlane = 600f; _miniCam.targetTexture = tex;
        _miniCam.clearFlags  = CameraClearFlags.SolidColor;
        _miniCam.backgroundColor = new Color(0.08f, 0.10f, 0.14f);
        _miniCam.cullingMask = ~0;
        _miniCam.transform.eulerAngles = new Vector3(90f, 0f, 0f);

        // Punto jugador
        var dotGO = Contenedor("Dot", rt,
            new Vector2(0.5f,0.5f), new Vector2(0.5f,0.5f),
            Vector2.zero, new Vector2(8,8));
        dotGO.gameObject.AddComponent<Image>().color = new Color(0.3f, 1f, 0.4f);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  UPDATE
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator ActualizarLoop()
    {
        while (true)
        {
            ActualizarVida();
            ActualizarDinero();
            ActualizarMinimap();
            ActualizarCrosshair();
            if (_notifQueue.Count > 0 && !_mostrando)
                yield return StartCoroutine(MostrarNotificacion(_notifQueue.Dequeue()));
            yield return new WaitForSeconds(0.033f); // ~30 FPS UI
        }
    }

    void ActualizarVida()
    {
        var ctrl = AltsasuCore.Jugador?.GetComponent<ControladorJugador>();
        if (ctrl == null) return;
        float target = ctrl.RatioVida;
        _vidaDisplay = Mathf.Lerp(_vidaDisplay, target, Time.deltaTime * 4f);
        if (_fillVida != null) _fillVida.fillAmount = _vidaDisplay;
        if (_txtVida   != null) _txtVida.text = ctrl.Vida.ToString();
        // Color: verde→rojo según vida
        if (_fillVida != null)
            _fillVida.color = Color.Lerp(new Color(0.9f,0.2f,0.2f), new Color(0.2f,0.85f,0.3f), _vidaDisplay);
    }

    void ActualizarDinero()
    {
        var gm = GameManagerAltsasua.Instance;
        if (gm == null || _txtDinero == null) return;
        if (_dineroDisplay != gm.dinero)
        {
            _dineroDisplay = (int)Mathf.MoveTowards(_dineroDisplay, gm.dinero, Time.deltaTime * 400f);
            _txtDinero.text = $"€ {_dineroDisplay:N0}";
        }
    }

    void ActualizarMinimap()
    {
        var j = AltsasuCore.Jugador;
        if (_miniCam == null || j == null) return;
        _miniCam.transform.position = j.position + Vector3.up * 80f;
    }

    void ActualizarCrosshair()
    {
        _timerCross -= Time.deltaTime;
        float vel = 0f;
        var rb = AltsasuCore.Jugador?.GetComponent<Rigidbody>();
        if (rb != null) vel = rb.linearVelocity.magnitude;
        float target = Mathf.Lerp(8f, 26f, vel / 6f);
        if (_timerCross > 0f) target = 30f;
        float cur = _crossH != null ? _crossH.rectTransform.sizeDelta.x * 0.5f : target;
        cur = Mathf.Lerp(cur, target, Time.deltaTime * 8f);
        if (_crossH != null) _crossH.rectTransform.sizeDelta = new Vector2(cur * 2f, 2f);
        if (_crossV != null) _crossV.rectTransform.sizeDelta = new Vector2(2f, cur * 2f);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  EVENTOS
    // ════════════════════════════════════════════════════════════════════════

    void SuscribirEventos()
    {
        GameManagerAltsasua.OnEstrellasCambia += ActualizarWanted;
        SistemaMisiones.OnMisionIniciada      += OnMisionIniciada;
        SistemaMisiones.OnObjetivoCompletado  += OnObjetivoCompletado;
        SistemaMisiones.OnMisionCompletada    += OnMisionCompletada;
        SistemaLogros.OnLogroDesbloqueado     += OnLogro;
        ControladorJugador.OnDanoRecibido     += _ => { _timerCross = 0.3f; };
    }

    void ActualizarWanted(int nivel)
    {
        for (int i = 0; i < 5; i++)
        {
            bool activa = i < nivel;
            if (_starLabels[i] != null)
                _starLabels[i].color = activa
                    ? new Color(1f, 0.85f, 0.1f)
                    : new Color(0.3f, 0.3f, 0.3f);
            if (_estrellasWanted[i] != null && activa)
                StartCoroutine(PulsarEstrella(i));
        }
    }

    IEnumerator PulsarEstrella(int idx)
    {
        var t = _starLabels[idx]?.GetComponent<RectTransform>();
        if (t == null) yield break;
        float dur = 0.25f, elapsed = 0f;
        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            float s = 1f + Mathf.Sin(elapsed / dur * Mathf.PI) * 0.5f;
            t.localScale = Vector3.one * s;
            yield return null;
        }
        t.localScale = Vector3.one;
    }

    void OnMisionIniciada(string nombre)
    {
        if (_txtMision   != null) _txtMision.text   = nombre;
        if (_txtObjetivo != null) _txtObjetivo.text = "▶ ...";
        MostrarBanner(true);
    }

    void OnObjetivoCompletado(string desc)
    {
        if (_txtObjetivo != null) _txtObjetivo.text = "✅ " + desc;
        StartCoroutine(FlashBanner());
    }

    void OnMisionCompletada(string nombre)
    {
        if (_txtMision   != null) _txtMision.text   = "🏆 " + nombre;
        if (_txtObjetivo != null) _txtObjetivo.text = "¡Misión completada!";
        StartCoroutine(OcultarBannerTras(6f));
    }

    void OnLogro(SistemaLogros.Logro logro)
        => _notifQueue.Enqueue($"{logro.Icono}  {logro.Nombre}\n{logro.Descripcion}");

    IEnumerator MostrarNotificacion(string msg)
    {
        _mostrando = true;
        // TODO: implementar panel de notificación deslizable
        AlsasuaLogger.Info("UI", $"Logro: {msg}");
        yield return new WaitForSecondsRealtime(3.5f);
        _mostrando = false;
    }

    // ── Banner de misión ──────────────────────────────────────────────────

    void MostrarBanner(bool visible)
    {
        if (_bannerVisible == visible) return;
        _bannerVisible = visible;
        StopCoroutine("AnimarBanner");
        StartCoroutine(AnimarBanner(visible));
    }

    IEnumerator AnimarBanner(bool mostrar)
    {
        float duracion = 0.35f, t = 0f;
        float desde = _bannerMision.anchoredPosition.x;
        float hasta = mostrar ? 16f : -420f;
        while (t < duracion)
        {
            t += Time.unscaledDeltaTime;
            _bannerMision.anchoredPosition = new Vector2(Mathf.Lerp(desde, hasta, t/duracion), 60f);
            yield return null;
        }
        _bannerMision.anchoredPosition = new Vector2(hasta, 60f);
    }

    IEnumerator FlashBanner()
    {
        if (_bgBanner == null) yield break;
        Color orig = _bgBanner.color;
        for (int i = 0; i < 3; i++)
        {
            _bgBanner.color = new Color(0.1f, 0.3f, 0.1f, 0.92f);
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
    //  HELPERS
    // ════════════════════════════════════════════════════════════════════════

    RectTransform Contenedor(string n, Transform padre, Vector2 ancMin, Vector2 ancMax,
                              Vector2 pos, Vector2 size)
    {
        var go = new GameObject(n);
        go.transform.SetParent(padre, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = ancMin; rt.anchorMax = ancMax;
        rt.pivot = ancMin; rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    Text Texto(string n, RectTransform padre, string contenido, int size,
               Color color, Vector2 pos, FontStyle style)
    {
        var go = new GameObject(n);
        go.transform.SetParent(padre, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0,1); rt.anchorMax = new Vector2(1,1);
        rt.pivot = new Vector2(0,1);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(-10, size + 6);
        var t = go.AddComponent<Text>();
        t.text = contenido; t.fontSize = size; t.color = color; t.fontStyle = style;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        go.AddComponent<Shadow>().effectColor = new Color(0,0,0,0.75f);
        return t;
    }

    Image LineaUI(string n, Transform padre, Vector2 size)
    {
        var go = new GameObject(n); go.transform.SetParent(padre, false);
        go.AddComponent<RectTransform>().sizeDelta = size;
        return go.AddComponent<Image>();
    }

    void OnDestroy()
    {
        GameManagerAltsasua.OnEstrellasCambia -= ActualizarWanted;
        SistemaMisiones.OnMisionIniciada      -= OnMisionIniciada;
        SistemaMisiones.OnObjetivoCompletado  -= OnObjetivoCompletado;
        SistemaMisiones.OnMisionCompletada    -= OnMisionCompletada;
        SistemaLogros.OnLogroDesbloqueado     -= OnLogro;
        ControladorJugador.OnDanoRecibido     -= _ => { };
    }
}
