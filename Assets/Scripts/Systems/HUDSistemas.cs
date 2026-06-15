// Assets/Scripts/HUDSistemas.cs
// ═══════════════════════════════════════════════════════════════════════════
//  HUD SISTEMAS — overlay de debug + toasts de eventos del director
//
//  Se añade al mismo GO que HUDCanvas (o a cualquier GO en escena).
//  Crea su propio Canvas de overlay — no modifica HUDCanvas.
//
//  F3 → toggle del panel de debug con:
//    • FPS actual + frame-time p50/p95/p99 (SistemaTelemetria)
//    • Quality tier (SistemaOptimizacion)
//    • Director: estado + intensidad (DirectorMundo)
//    • Tensión musical (SistemaMusicaAdaptativa)
//    • Vehículos activos (SistemaTrafico)
//    • Sistemas on/off (Neblina, Impostores, CalidadGate)
//
//  Toasts:
//    DirectorMundo.OnEvento → texto grande centrado en pantalla, fade-out 3 s.
//    Texto solo aparece para eventos de impacto (Redada, Control, Disturbio).
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Text;

public class HUDSistemas : MonoBehaviour
{
    // ── Config ────────────────────────────────────────────────────────────
    [SerializeField] KeyCode teclaDebug = KeyCode.F3;

    // ── UI interna ────────────────────────────────────────────────────────
    Canvas    _canvas;
    Text      _txtDebug;
    Image     _bgDebug;
    Text      _txtToast;
    Image     _bgToast;

    bool      _debugVisible;
    float     _timerUpdate;
    Coroutine _toastCoroutine;

    static readonly StringBuilder _sb = new(512);

    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        CrearCanvas();
        CrearPanelDebug();
        CrearToast();
        SetDebugVisible(false);
    }

    void OnEnable()  => DirectorMundo.OnEvento += OnEvento;
    void OnDisable() => DirectorMundo.OnEvento -= OnEvento;

    // ════════════════════════════════════════════════════════════════════════
    //  UPDATE
    // ════════════════════════════════════════════════════════════════════════

    void Update()
    {
        if (Input.GetKeyDown(teclaDebug))
            SetDebugVisible(!_debugVisible);

        if (!_debugVisible) return;

        _timerUpdate += Time.unscaledDeltaTime;
        if (_timerUpdate < 0.25f) return;   // 4 Hz para el debug panel
        _timerUpdate = 0f;
        RefrescarDebug();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  PANEL DEBUG
    // ════════════════════════════════════════════════════════════════════════

    void RefrescarDebug()
    {
        _sb.Clear();

        // ── Frame-time ────────────────────────────────────────────────────
        float fps = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.001f);
        _sb.AppendLine($"FPS: <b>{fps:F0}</b>");

        var tel = SistemaTelemetria.Instance;
        if (tel != null)
            _sb.AppendLine($"p50:{tel.P50Ms:F1} p95:{tel.P95Ms:F1} p99:<color={ColorP99(tel.P99Ms)}>{tel.P99Ms:F1}</color> ms");

        // ── Calidad ───────────────────────────────────────────────────────
        int tier = SistemaOptimizacion.TierCalidad;
        string[] tierNames = { "Ultra", "Alto", "Medio", "Performance" };
        _sb.AppendLine($"Tier: <b>{tierNames[Mathf.Clamp(tier, 0, 3)]}</b> ({tier})");

        // ── Director ──────────────────────────────────────────────────────
        if (DirectorMundo.Instance != null)
        {
            float intensidad = DirectorMundo.IntensidadActual;
            string estado    = DirectorMundo.EstadoActual.ToString();
            string bar       = BarraASCII(intensidad, 10);
            _sb.AppendLine($"Director: {estado}");
            _sb.AppendLine($"  [{bar}] {intensidad:F2}");
        }

        // ── Música ────────────────────────────────────────────────────────
        if (SistemaMusicaAdaptativa.Instance != null)
        {
            float t = SistemaMusicaAdaptativa.TensionActual;
            _sb.AppendLine($"Tensión: [{BarraASCII(t, 8)}] {t:F2}");
        }

        // ── Tráfico ───────────────────────────────────────────────────────
        if (SistemaTrafico.Instance != null)
        {
            // Contar vehículos activos (SistemaTrafico no expone el count directamente;
            // aproximamos con DirectorMundo intensidad y el pool size)
            _sb.AppendLine($"Tráfico: activo");
        }

        // ── Sistemas on/off ───────────────────────────────────────────────
        _sb.AppendLine("Sistemas:");
        Append("Neblina",     SistemaNeblina.Instance?.enabled);
        Append("Impostores",  SistemaImpostores.Instance?.enabled);
        Append("CalidadGate", SistemaCalidadGate.Instance?.enabled);
        Append("Trafico",     SistemaTrafico.Instance?.enabled);
        Append("Tren",        SistemaTren.Instance?.enabled);

        if (_txtDebug != null) _txtDebug.text = _sb.ToString();
    }

    void Append(string nombre, bool? estado)
    {
        if (estado == null) return;
        string col = estado.Value ? "#88ff88" : "#ff8888";
        _sb.AppendLine($"  <color={col}>●</color> {nombre}");
    }

    static string ColorP99(float p99) => p99 < 20f ? "#88ff88" : p99 < 33f ? "#ffcc44" : "#ff4444";

    static string BarraASCII(float t, int largo)
    {
        int llenos = Mathf.RoundToInt(t * largo);
        return new string('█', llenos) + new string('░', largo - llenos);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  TOASTS — eventos del director
    // ════════════════════════════════════════════════════════════════════════

    void OnEvento(DirectorMundo.EventoMundo ev)
    {
        string texto = ev switch
        {
            DirectorMundo.EventoMundo.Redada          => "¡¡ REDADA !!",
            DirectorMundo.EventoMundo.ControlPolicial => "¡ CONTROL POLICIAL !",
            DirectorMundo.EventoMundo.Disturbio       => "¡ DISTURBIOS EN LA CALLE !",
            DirectorMundo.EventoMundo.PatrullaRefuerzo => "Refuerzo de patrullas",
            DirectorMundo.EventoMundo.MercadoDia      => "Día de mercado",
            _                                         => null
        };

        if (texto == null) return;

        if (_toastCoroutine != null) StopCoroutine(_toastCoroutine);
        _toastCoroutine = StartCoroutine(MostrarToast(texto, ev));
    }

    IEnumerator MostrarToast(string texto, DirectorMundo.EventoMundo ev)
    {
        if (_txtToast == null || _bgToast == null) yield break;

        _txtToast.text = texto;

        // Color por urgencia
        Color col = ev switch
        {
            DirectorMundo.EventoMundo.Redada          => new Color(1f, 0.15f, 0.1f, 0.92f),
            DirectorMundo.EventoMundo.ControlPolicial => new Color(0.9f, 0.6f, 0.0f, 0.85f),
            _                                         => new Color(0.1f, 0.3f, 0.8f, 0.75f),
        };
        _bgToast.color = col;

        // Aparece
        _bgToast.gameObject.SetActive(true);
        var canvasGroup = _bgToast.GetComponent<CanvasGroup>()
            ?? _bgToast.gameObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;

        float t = 0f;
        while (t < 0.3f) { t += Time.unscaledDeltaTime; canvasGroup.alpha = t / 0.3f; yield return null; }
        canvasGroup.alpha = 1f;

        yield return new WaitForSecondsRealtime(ev == DirectorMundo.EventoMundo.Redada ? 4f : 2.5f);

        // Desvanece
        t = 0f;
        while (t < 0.6f) { t += Time.unscaledDeltaTime; canvasGroup.alpha = 1f - t / 0.6f; yield return null; }
        _bgToast.gameObject.SetActive(false);
        _toastCoroutine = null;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CONSTRUCCIÓN DE UI
    // ════════════════════════════════════════════════════════════════════════

    void CrearCanvas()
    {
        var go = new GameObject("HUDSistemas_Canvas");
        go.transform.SetParent(transform);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 99;   // encima de todo
        go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        go.AddComponent<GraphicRaycaster>();
    }

    void CrearPanelDebug()
    {
        var bg = new GameObject("DebugBG");
        bg.transform.SetParent(_canvas.transform, false);
        _bgDebug = bg.AddComponent<Image>();
        _bgDebug.color = new Color(0f, 0f, 0f, 0.78f);

        var rt = bg.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot     = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(8f, -8f);
        rt.sizeDelta = new Vector2(280f, 0f);   // altura auto

        var txtGO = new GameObject("TxtDebug");
        txtGO.transform.SetParent(bg.transform, false);
        _txtDebug = txtGO.AddComponent<Text>();
        _txtDebug.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _txtDebug.fontSize  = 11;
        _txtDebug.color     = new Color(0.9f, 0.95f, 1f);
        _txtDebug.supportRichText = true;
        _txtDebug.verticalOverflow = VerticalWrapMode.Overflow;

        var trt = txtGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(8f, 6f);
        trt.offsetMax = new Vector2(-8f, -6f);

        // ContentSizeFitter para altura automática
        var csf = bg.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    void CrearToast()
    {
        var bg = new GameObject("ToastBG");
        bg.transform.SetParent(_canvas.transform, false);
        _bgToast = bg.AddComponent<Image>();

        var rt = bg.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0.75f);
        rt.anchorMax        = new Vector2(0.5f, 0.75f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = new Vector2(520f, 60f);

        var txtGO = new GameObject("TxtToast");
        txtGO.transform.SetParent(bg.transform, false);
        _txtToast = txtGO.AddComponent<Text>();
        _txtToast.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _txtToast.fontSize  = 22;
        _txtToast.fontStyle = FontStyle.Bold;
        _txtToast.color     = Color.white;
        _txtToast.alignment = TextAnchor.MiddleCenter;

        var trt = txtGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;

        _bgToast.gameObject.SetActive(false);
    }

    void SetDebugVisible(bool v)
    {
        _debugVisible = v;
        if (_bgDebug != null) _bgDebug.gameObject.SetActive(v);
        if (v) RefrescarDebug();
    }
}
