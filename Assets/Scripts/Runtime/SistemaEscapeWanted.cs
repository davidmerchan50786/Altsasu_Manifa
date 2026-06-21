// Assets/Scripts/Runtime/SistemaEscapeWanted.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE ESCAPE DEL WANTED — deescalada correcta estilo GTA
//
//  PROBLEMA QUE CORRIGE:
//    GameManagerAltsasua reducía las estrellas cada N segundos sin importar
//    si había policía cerca del jugador. Con este sistema el timer de
//    deescalada solo corre cuando NO hay policía en radio de búsqueda.
//
//  FLUJO:
//    · Cada 0.5 s comprueba si hay algún PoliciaForalIA activo dentro del
//      radio de búsqueda (radioEscape, default 200 m).
//    · Si hay policía:  llama IWantedSystem.BloquearDeescalada(true)
//                       → el timer de GameManager no corre.
//                       Muestra HUD "¡BUSCADO!" con estrellas parpadeando.
//    · Si no hay policía y wanted > 0: muestra barra de progreso de escape;
//                       cuando se llena → IWantedSystem.BloquearDeescalada(false)
//                       y deja que GameManager reduzca una estrella solo.
//    · Si wanted == 0:  oculta todo el HUD de escape.
//
//  HUD:
//    · Panel slide-in en la parte superior central (aparece cuando wanted > 0)
//    · Estrellas parpadeando mientras hay policía cerca
//    · Barra de progreso de escape (rellena cuando no hay policía)
//    · Texto "Escapa de la Policía" / "Zona despejada"
//
//  Auto-arranca. No requiere montaje en escena.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(65)]
public sealed class SistemaEscapeWanted : MonoBehaviour
{
    // ── Config ────────────────────────────────────────────────────────────────
    [Header("Escape")]
    [Tooltip("Radio (m) en el que un policía activo impide la deescalada.")]
    [SerializeField] float radioEscape      = 200f;
    [Tooltip("Segundos sin policía para que la barra de escape se llene completamente.")]
    [SerializeField] float tiempoEscape     = 5f;

    // ── Bootstrap ─────────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<SistemaEscapeWanted>() != null) return;
        var go = new GameObject("SistemaEscapeWanted");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaEscapeWanted>();
    }

    // ── Estado ────────────────────────────────────────────────────────────────
    IWantedSystem _wanted;
    float         _pollTimer;
    float         _escapeProg;       // 0..1 (barra de progreso)
    bool          _policiaCerca;
    bool          _uiCreada;

    // ── HUD ───────────────────────────────────────────────────────────────────
    Canvas        _canvas;
    CanvasGroup   _grupo;
    RectTransform _panel;
    Text          _txtEstado;
    Image         _barraFill;
    Image[]       _estrellasHUD = new Image[5];
    float         _parpadeTimer;
    bool          _parpadeEncendido;

    // ── Colores ───────────────────────────────────────────────────────────────
    static readonly Color COL_PELIGRO  = new(1.00f, 0.15f, 0.15f);
    static readonly Color COL_ESCAPE   = new(0.25f, 0.85f, 0.30f);
    static readonly Color COL_BARRA_BG = new(0.10f, 0.10f, 0.10f, 0.75f);

    // ═══════════════════════════════════════════════════════════════════════════

    void Start() => CrearHUD();

    void Update()
    {
        _wanted ??= ServiceLocator.Get<IWantedSystem>();
        if (_wanted == null) return;

        int nivel = _wanted.NivelBusqueda;

        // Ocultar todo si wanted == 0
        if (nivel == 0)
        {
            _wanted.BloquearDeescalada(false);
            _escapeProg = 0f;
            OcultarHUD();
            return;
        }

        MostrarHUD();

        // Sondeo de policía cada 0.5 s (no cada frame — hay N policías)
        _pollTimer -= Time.deltaTime;
        if (_pollTimer <= 0f)
        {
            _pollTimer     = 0.5f;
            _policiaCerca  = HayPoliciaEnRadio();
        }

        if (_policiaCerca)
        {
            // Policía en radio → bloquear deescalada, resetear barra
            _wanted.BloquearDeescalada(true);
            _escapeProg = Mathf.Max(0f, _escapeProg - Time.deltaTime * 0.5f); // barra retrocede más lento
            ActualizarHUD(nivel, enPeligro: true);
        }
        else
        {
            // Sin policía → dejar correr la deescalada, llenar barra
            _wanted.BloquearDeescalada(false);
            _escapeProg = Mathf.MoveTowards(_escapeProg, 1f, Time.deltaTime / tiempoEscape);
            ActualizarHUD(nivel, enPeligro: false);
        }
    }

    // ── Detección de policía ──────────────────────────────────────────────────

    bool HayPoliciaEnRadio()
    {
        var jugador = AltsasuCore.Jugador;
        if (jugador == null) return false;

        foreach (var p in FindObjectsByType<PoliciaForalIA>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (p.EstaMuerto) continue;
            if (Vector3.Distance(p.transform.position, jugador.position) <= radioEscape)
                return true;
        }
        return false;
    }

    // ── HUD ───────────────────────────────────────────────────────────────────

    void ActualizarHUD(int nivel, bool enPeligro)
    {
        if (!_uiCreada) return;

        // Texto de estado
        if (_txtEstado != null)
            _txtEstado.text = enPeligro ? "¡BUSCADO!" : "Zona despejada";

        // Barra de progreso de escape
        if (_barraFill != null)
        {
            _barraFill.fillAmount = _escapeProg;
            _barraFill.color      = enPeligro ? COL_PELIGRO : COL_ESCAPE;
        }

        // Parpadeo de estrellas cuando hay policía
        if (enPeligro)
        {
            _parpadeTimer -= Time.deltaTime;
            if (_parpadeTimer <= 0f)
            {
                _parpadeTimer      = 0.30f;
                _parpadeEncendido  = !_parpadeEncendido;
            }
        }
        else
        {
            _parpadeEncendido = true;
        }

        for (int i = 0; i < 5; i++)
        {
            if (_estrellasHUD[i] == null) continue;
            bool activa  = i < nivel;
            bool visible = activa && (_parpadeEncendido || !enPeligro);
            _estrellasHUD[i].color = visible
                ? new Color(1f, 0.82f, 0.10f)
                : new Color(0.25f, 0.20f, 0.05f, 0.5f);
            _estrellasHUD[i].gameObject.SetActive(activa || !enPeligro);
        }
    }

    void MostrarHUD()
    {
        if (!_uiCreada || _grupo == null) return;
        _grupo.alpha = Mathf.MoveTowards(_grupo.alpha, 1f, Time.deltaTime * 5f);
    }

    void OcultarHUD()
    {
        if (!_uiCreada || _grupo == null) return;
        _grupo.alpha = Mathf.MoveTowards(_grupo.alpha, 0f, Time.deltaTime * 3f);
    }

    // ── Construcción de UI ────────────────────────────────────────────────────

    void CrearHUD()
    {
        // Canvas propio para no pisar el HUD principal
        var canvasGO = new GameObject("EscapeWantedCanvas");
        canvasGO.transform.SetParent(transform);
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode    = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder  = 55; // encima del minimapa (50), debajo del menú de pausa
        var cs = canvasGO.AddComponent<CanvasScaler>();
        cs.uiScaleMode       = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920, 1080);
        canvasGO.AddComponent<GraphicRaycaster>();

        // Panel central superior
        var panelGO = new GameObject("PanelEscape");
        panelGO.transform.SetParent(_canvas.transform, false);
        _panel = panelGO.AddComponent<RectTransform>();
        _panel.anchorMin        = new Vector2(0.5f, 1f);
        _panel.anchorMax        = new Vector2(0.5f, 1f);
        _panel.pivot            = new Vector2(0.5f, 1f);
        _panel.anchoredPosition = new Vector2(0f, -8f);
        _panel.sizeDelta        = new Vector2(340f, 80f);

        var bg = panelGO.AddComponent<Image>();
        bg.color = new Color(0.04f, 0.04f, 0.06f, 0.88f);

        _grupo = panelGO.AddComponent<CanvasGroup>();
        _grupo.alpha = 0f;

        // Estrellas (fila superior)
        float starW = 28f, starGap = 32f;
        float totalW = 5 * starGap;
        float startX = -totalW * 0.5f + starGap * 0.5f;

        for (int i = 0; i < 5; i++)
        {
            var sGO = new GameObject($"Star{i}");
            sGO.transform.SetParent(_panel, false);
            var sRT = sGO.AddComponent<RectTransform>();
            sRT.sizeDelta        = new Vector2(starW, starW);
            sRT.anchorMin        = sRT.anchorMax = new Vector2(0.5f, 1f);
            sRT.anchoredPosition = new Vector2(startX + i * starGap, -10f);
            _estrellasHUD[i]     = sGO.AddComponent<Image>();
            _estrellasHUD[i].color = new Color(0.25f, 0.20f, 0.05f, 0.5f);

            // Dibujar ★ como texto (sin sprite)
            var txtGO = new GameObject("StarTxt");
            txtGO.transform.SetParent(sGO.transform, false);
            var tRT = txtGO.AddComponent<RectTransform>();
            tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
            tRT.offsetMin = tRT.offsetMax = Vector2.zero;
            var t = txtGO.AddComponent<Text>();
            t.text      = "★";
            t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize  = 20;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color     = new Color(1f, 0.82f, 0.10f, 0f); // inherit from Image
        }

        // Texto de estado
        var txtGO2 = new GameObject("TxtEstado");
        txtGO2.transform.SetParent(_panel, false);
        var tRT2 = txtGO2.AddComponent<RectTransform>();
        tRT2.anchorMin        = new Vector2(0f, 0.5f);
        tRT2.anchorMax        = new Vector2(1f, 0.5f);
        tRT2.anchoredPosition = new Vector2(0f, -4f);
        tRT2.sizeDelta        = new Vector2(0f, 20f);
        _txtEstado            = txtGO2.AddComponent<Text>();
        _txtEstado.text       = "¡BUSCADO!";
        _txtEstado.font       = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _txtEstado.fontSize   = 14;
        _txtEstado.fontStyle  = FontStyle.Bold;
        _txtEstado.alignment  = TextAnchor.MiddleCenter;
        _txtEstado.color      = COL_PELIGRO;

        // Barra de escape (fondo + relleno)
        var barraGO = new GameObject("BarraEscape");
        barraGO.transform.SetParent(_panel, false);
        var barraRT = barraGO.AddComponent<RectTransform>();
        barraRT.anchorMin        = new Vector2(0.05f, 0f);
        barraRT.anchorMax        = new Vector2(0.95f, 0f);
        barraRT.anchoredPosition = new Vector2(0f, 10f);
        barraRT.sizeDelta        = new Vector2(0f, 10f);
        var barraBG = barraGO.AddComponent<Image>();
        barraBG.color = COL_BARRA_BG;

        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(barraGO.transform, false);
        var fillRT = fillGO.AddComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = new Vector2(1f, 1f);
        fillRT.offsetMin = fillRT.offsetMax = Vector2.zero;
        _barraFill            = fillGO.AddComponent<Image>();
        _barraFill.color      = COL_ESCAPE;
        _barraFill.type       = Image.Type.Filled;
        _barraFill.fillMethod = Image.FillMethod.Horizontal;
        _barraFill.fillAmount = 0f;

        _uiCreada = true;
    }
}
