// Assets/Scripts/HUDJugador.cs
// HUD Canvas completo — reemplaza OnGUI() de ControladorJugador y SistemaDisparo.
//
// Se auto-construye desde código: sin prefabs ni assets externos.
// Usa UnityEngine.UI (uGUI), escala con la resolución de pantalla (CanvasScaler).
//
// ── SETUP ─────────────────────────────────────────────────────────────────
//   1. Crear un GameObject vacío "HUD" en la escena.
//   2. Añadir este componente.
//   3. (Opcional) Arrastrar ControladorJugador y SistemaDisparo en el Inspector.
//      Si no se asignan, los busca automáticamente en Awake().
//
// ── ELEMENTOS ──────────────────────────────────────────────────────────────
//   · Flash rojo pantalla completa (daño recibido)
//   · Barra de vida con texto y texto de estado encima (esquina inf-izq)
//   · Crosshair: cruz al apuntar (RMB) / punto en 3ª persona
//   · Panel de munición con barra de recarga animada (esquina inf-der)
//   · Panel de controles con fade-out (esquina sup-izq)

using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("Alsasua/HUD Jugador")]
public sealed class HUDJugador : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════════════
    //  INSPECTOR
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ REFERENCIAS (auto-buscadas si vacías) ═══")]
    [SerializeField] private ControladorJugador jugador;
    [SerializeField] private SistemaDisparo     disparo;

    [Header("═══ CONFIGURACIÓN ═══")]
    [Tooltip("Segundos que el panel de controles permanece visible al inicio.")]
    [SerializeField] private float duracionControles = 12f;

    // ═══════════════════════════════════════════════════════════════════════
    //  ELEMENTOS UI INTERNOS
    // ═══════════════════════════════════════════════════════════════════════

    private Image        _flashDano;
    private Image        _rellenoVida;
    private Text         _textoVida;
    private Text         _textoEstado;
    private Image        _crosshairH, _crosshairV, _crosshairDot;
    private Text         _textoAmmo;
    private Image        _barraRecarga;
    private GameObject   _goFondoRecarga, _goBarraRecarga;
    private CanvasGroup  _grupoControles;

    private float _timerControles;

    // ── Dirty-check: evita alloc de string cada frame si los valores no cambian ──
    // FIX GC: $"❤  {vida} / {vidaMax}" y $"🔫 {balas}…" allocan aunque los int no cambien.
    // Guardar el último valor renderizado y reconstruir el string solo cuando difiere.
    private int    _vidaAnterior      = -1;
    private int    _vidaMaxAnterior   = -1;
    private string _estadoAnterior    = null;
    private bool   _cargandoAnterior  = false;
    private int    _balasAnterior     = -1;
    private int    _balasCargAnterior = -1;
    private int    _balasResAnterior  = -1;
    private int    _timerRecargaTick  = -1;  // FloorToInt(timer * 10) — granularidad F1

    // ── Recursos estáticos (compartidos entre instancias) ────────────────
    private static Font   _font;
    private static Sprite _spriteBlanco;

    // ═══════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ═══════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (jugador == null) jugador = Object.FindFirstObjectByType<ControladorJugador>();
        if (disparo == null) disparo  = Object.FindFirstObjectByType<SistemaDisparo>();

        try { PrepararRecursos(); }
        catch (System.Exception ex) { AlsasuaLogger.Warn("HUDJugador", $"PrepararRecursos falló (normal en tests): {ex.Message}"); }

        ConstruirCanvas();
        _timerControles = duracionControles;
    }

    private void Update()
    {
        if (jugador == null) return;

        ActualizarVida();
        ActualizarFlash();
        ActualizarCrosshair();
        ActualizarAmmo();
        ActualizarControles();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  RECURSOS ESTÁTICOS
    // ═══════════════════════════════════════════════════════════════════════

    private static void PrepararRecursos()
    {
        // Fuente integrada de Unity (siempre disponible, sin instalación)
        if (_font == null)
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        // Sprite blanco para fondos y barras
        // Unity destruye texturas en runtime al salir del play mode → re-crear si null
        if (_spriteBlanco == null)
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px  = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply(false);
            _spriteBlanco = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CONSTRUCCIÓN DEL CANVAS
    // ═══════════════════════════════════════════════════════════════════════

    private void ConstruirCanvas()
    {
        // ── Canvas raíz ──────────────────────────────────────────────────
        var cvGO = new GameObject("_Canvas_HUD");
        cvGO.transform.SetParent(transform, false);

        var cv = cvGO.AddComponent<Canvas>();
        cv.renderMode   = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 100;

        var cs = cvGO.AddComponent<CanvasScaler>();
        cs.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920f, 1080f);
        cs.matchWidthOrHeight  = 0.5f;   // compromiso entre landscape y portrait

        cvGO.AddComponent<GraphicRaycaster>();

        var root = cvGO.transform;

        // ── Construir elementos ───────────────────────────────────────────
        ConstruirFlash(root);
        ConstruirBarraVida(root);
        ConstruirCrosshair(root);
        ConstruirPanelAmmo(root);
        ConstruirPanelControles(root);
    }

    // ── Flash de daño (fullscreen) ───────────────────────────────────────

    private void ConstruirFlash(Transform root)
    {
        var go = NuevoGO("FlashDano", root);
        var rt = RT(go);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        _flashDano = Img(go, new Color(1f, 0f, 0f, 0f));
    }

    // ── Barra de vida (inferior-izquierda) ──────────────────────────────

    private void ConstruirBarraVida(Transform root)
    {
        // Texto de estado — encima de la barra
        var goEstado = NuevoGO("TextoEstado", root);
        FijarEsquina(goEstado, Vector2.zero, new Vector2(20f, 46f), new Vector2(260f, 20f));
        _textoEstado = Txt(goEstado, "EN GUARDIA", 11, new Color(1f, 1f, 0.8f, 0.9f));
        _textoEstado.alignment = TextAnchor.MiddleLeft;

        // Fondo de la barra
        var goFondo = NuevoGO("FondoVida", root);
        FijarEsquina(goFondo, Vector2.zero, new Vector2(20f, 22f), new Vector2(260f, 22f));
        Img(goFondo, new Color(0f, 0f, 0f, 0.70f));

        // Relleno dinámico (filled horizontal)
        var goRelleno = NuevoGO("RellenoVida", root);
        FijarEsquina(goRelleno, Vector2.zero, new Vector2(20f, 22f), new Vector2(260f, 22f));
        var imgR = Img(goRelleno, Color.green);
        imgR.type       = Image.Type.Filled;
        imgR.fillMethod = Image.FillMethod.Horizontal;
        imgR.fillAmount = 1f;
        _rellenoVida    = imgR;

        // Texto de vida
        var goTxt = NuevoGO("TextoVida", root);
        FijarEsquina(goTxt, Vector2.zero, new Vector2(26f, 22f), new Vector2(254f, 22f));
        _textoVida = Txt(goTxt, "❤  100 / 100", 11, Color.white);
        _textoVida.alignment = TextAnchor.MiddleLeft;
    }

    // ── Crosshair (centro de pantalla) ──────────────────────────────────

    private void ConstruirCrosshair(Transform root)
    {
        var goH = NuevoGO("CrosshairH", root);
        FijarCentro(goH, 28f, 2f);
        _crosshairH = Img(goH, new Color(1f, 1f, 1f, 0.95f));

        var goV = NuevoGO("CrosshairV", root);
        FijarCentro(goV, 2f, 28f);
        _crosshairV = Img(goV, new Color(1f, 1f, 1f, 0.95f));

        // Punto para modo 3ª persona (no apuntando)
        var goDot = NuevoGO("CrosshairDot", root);
        FijarCentro(goDot, 5f, 5f);
        _crosshairDot = Img(goDot, new Color(1f, 1f, 1f, 0.75f));
    }

    // ── Panel de munición (inferior-derecha) ────────────────────────────

    private void ConstruirPanelAmmo(Transform root)
    {
        // Contenedor anclado a la esquina inferior-derecha
        var goCont = NuevoGO("PanelAmmo", root);
        FijarEsquina(goCont, new Vector2(1f, 0f), new Vector2(-10f, 10f), new Vector2(164f, 54f));

        // Fondo (stretching dentro del contenedor)
        var goFondo = NuevoGO("FondoAmmo", goCont.transform);
        Estirar(goFondo, Vector2.zero, Vector2.zero);
        Img(goFondo, new Color(0f, 0f, 0f, 0.55f));

        // Texto de munición (mitad superior del contenedor)
        var goTxt = NuevoGO("TextoAmmo", goCont.transform);
        var rtT = RT(goTxt);
        rtT.anchorMin = new Vector2(0f, 0.40f);
        rtT.anchorMax = Vector2.one;
        rtT.offsetMin = new Vector2(6f, 0f);
        rtT.offsetMax = new Vector2(-6f, -4f);
        _textoAmmo = Txt(goTxt, "🔫 30 / 30   [120]", 13, Color.white);
        _textoAmmo.alignment = TextAnchor.MiddleRight;

        // Fondo barra recarga (mitad inferior)
        _goFondoRecarga = NuevoGO("FondoRecarga", goCont.transform);
        var rtFR = RT(_goFondoRecarga);
        rtFR.anchorMin = Vector2.zero;
        rtFR.anchorMax = new Vector2(1f, 0.38f);
        rtFR.offsetMin = new Vector2(4f, 4f);
        rtFR.offsetMax = new Vector2(-4f, 0f);
        Img(_goFondoRecarga, new Color(0f, 0f, 0f, 0.40f));
        _goFondoRecarga.SetActive(false);

        // Barra de recarga (filled horizontal, encima del fondo)
        _goBarraRecarga = NuevoGO("BarraRecarga", goCont.transform);
        var rtB = RT(_goBarraRecarga);
        rtB.anchorMin = Vector2.zero;
        rtB.anchorMax = new Vector2(1f, 0.38f);
        rtB.offsetMin = new Vector2(4f, 4f);
        rtB.offsetMax = new Vector2(-4f, 0f);
        var imgB = Img(_goBarraRecarga, new Color(1f, 0.85f, 0.1f, 0.9f));
        imgB.type       = Image.Type.Filled;
        imgB.fillMethod = Image.FillMethod.Horizontal;
        imgB.fillAmount = 0f;
        _barraRecarga   = imgB;
        _goBarraRecarga.SetActive(false);
    }

    // ── Panel de controles (superior-izquierda, fade-out) ────────────────

    private void ConstruirPanelControles(Transform root)
    {
        var goCont = NuevoGO("PanelControles", root);
        FijarEsquina(goCont, new Vector2(0f, 1f), new Vector2(14f, -14f), new Vector2(212f, 120f));
        _grupoControles = goCont.AddComponent<CanvasGroup>();

        // Fondo
        var goFondo = NuevoGO("Fondo", goCont.transform);
        Estirar(goFondo, Vector2.zero, Vector2.zero);
        Img(goFondo, new Color(0f, 0f, 0f, 0.50f));

        // Texto de controles
        var goTxt = NuevoGO("Texto", goCont.transform);
        Estirar(goTxt, new Vector2(8f, 6f), new Vector2(-8f, -6f));
        var t = Txt(goTxt,
            "WASD · Mover        SHIFT · Correr\n"   +
            "SPACE · Saltar         C · Agacharse\n" +
            "RMB · Apuntar     LMB · Disparar\n"     +
            "F · Colocar bomba  G · Detonar\n"        +
            "ESC · Cursor libre",
            10, new Color(1f, 1f, 0.85f, 0.85f));
        t.alignment       = TextAnchor.UpperLeft;
        t.verticalOverflow = VerticalWrapMode.Overflow;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ACTUALIZACIÓN FRAME A FRAME
    // ═══════════════════════════════════════════════════════════════════════

    private void ActualizarVida()
    {
        float ratio = jugador.RatioVida;
        _rellenoVida.fillAmount = ratio;
        _rellenoVida.color = Color.Lerp(
            new Color(0.85f, 0.10f, 0.10f),
            new Color(0.10f, 0.80f, 0.20f), ratio);

        // FIX GC: reconstruir strings solo cuando los valores cambian.
        // La interpolación de strings alloca heap aunque los int sean idénticos al frame anterior.
        int vida    = jugador.Vida;
        int vidaMax = jugador.VidaMax;
        if (vida != _vidaAnterior || vidaMax != _vidaMaxAnterior)
        {
            _vidaAnterior    = vida;
            _vidaMaxAnterior = vidaMax;
            _textoVida.text  = $"❤  {vida} / {vidaMax}";
        }

        string estado = jugador.TextoEstado;
        if (!string.Equals(estado, _estadoAnterior, System.StringComparison.Ordinal))
        {
            _estadoAnterior   = estado;
            _textoEstado.text = estado;
        }
    }

    private void ActualizarFlash() =>
        _flashDano.color = new Color(1f, 0f, 0f,
            Mathf.Clamp01(jugador.FlashDano * 2.5f) * 0.55f);

    private void ActualizarCrosshair()
    {
        bool aim = jugador.EstaApuntandoP;
        _crosshairH.gameObject.SetActive(aim);
        _crosshairV.gameObject.SetActive(aim);
        _crosshairDot.gameObject.SetActive(!aim);
    }

    private void ActualizarAmmo()
    {
        if (disparo == null) return;

        bool cargando = disparo.EstaCargando;

        // FIX GC: activar/desactivar GOs solo en el flanco de cambio, no cada frame.
        if (cargando != _cargandoAnterior)
        {
            _goFondoRecarga.SetActive(cargando);
            _goBarraRecarga.SetActive(cargando);
            _cargandoAnterior = cargando;
            // Invalidar caché de la otra rama para forzar rebuild al cambiar de modo
            _timerRecargaTick = -1;
            _balasAnterior    = -1;
        }

        if (cargando)
        {
            _barraRecarga.fillAmount = disparo.ProgressRecarga;

            // FIX GC: "RECARGANDO... X.Xs" solo cambia cada 100 ms (F1 = 1 decimal).
            // Usamos FloorToInt(timer*10) como clave dirty-check → 1 alloc cada 100 ms máx.
            int tick = Mathf.FloorToInt(disparo.TimerRecarga * 10f);
            if (tick != _timerRecargaTick)
            {
                _timerRecargaTick    = tick;
                _textoAmmo.text      = $"RECARGANDO... {disparo.TimerRecarga:F1}s";
                _textoAmmo.color     = Color.yellow;
            }
        }
        else
        {
            int balas     = disparo.Balas;
            int balasCarg = disparo.BalasMaxCargador;
            int balasRes  = disparo.BalasReserva;

            // FIX GC: reconstruir solo cuando cambian los contadores de munición.
            if (balas != _balasAnterior || balasCarg != _balasCargAnterior || balasRes != _balasResAnterior)
            {
                _balasAnterior     = balas;
                _balasCargAnterior = balasCarg;
                _balasResAnterior  = balasRes;
                _textoAmmo.text    = $"🔫 {balas} / {balasCarg}   [{balasRes}]";
                _textoAmmo.color   = Color.white;
            }
        }
    }

    private void ActualizarControles()
    {
        if (_grupoControles == null || !_grupoControles.gameObject.activeSelf) return;

        _timerControles -= Time.deltaTime;
        if (_timerControles <= 0f)
        {
            _grupoControles.gameObject.SetActive(false);
        }
        else if (_timerControles < 3f)
        {
            _grupoControles.alpha = _timerControles / 3f;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HELPERS DE CONSTRUCCIÓN UI
    // ═══════════════════════════════════════════════════════════════════════

    // Crea un GO con RectTransform y lo emparenta
    private static GameObject NuevoGO(string nombre, Transform parent)
    {
        var go = new GameObject(nombre, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    // Obtiene el RectTransform del GO
    private static RectTransform RT(GameObject go) =>
        go.GetComponent<RectTransform>();

    // Ancla a una esquina fija: anchor = pivot = esquina, pos y size en pixels
    private static void FijarEsquina(GameObject go, Vector2 esquina, Vector2 pos, Vector2 size)
    {
        var rt = RT(go);
        rt.anchorMin        = esquina;
        rt.anchorMax        = esquina;
        rt.pivot            = esquina;
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
    }

    // Centra el elemento en pantalla
    private static void FijarCentro(GameObject go, float w, float h)
    {
        var rt = RT(go);
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = new Vector2(w, h);
    }

    // Estira el elemento para llenar su padre, con márgenes
    private static void Estirar(GameObject go, Vector2 offsetMin, Vector2 offsetMax)
    {
        var rt = RT(go);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    // Añade una Image con sprite blanco y color
    private static Image Img(GameObject go, Color color)
    {
        var img = go.AddComponent<Image>();
        img.sprite        = _spriteBlanco;
        img.color         = color;
        img.raycastTarget = false;
        return img;
    }

    // Añade un Text con la fuente integrada
    private static Text Txt(GameObject go, string contenido, int tamano, Color color)
    {
        var t = go.AddComponent<Text>();
        if (_font != null) t.font = _font;
        t.text               = contenido;
        t.fontSize           = tamano;
        t.color              = color;
        t.raycastTarget      = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow   = VerticalWrapMode.Overflow;
        return t;
    }
}
