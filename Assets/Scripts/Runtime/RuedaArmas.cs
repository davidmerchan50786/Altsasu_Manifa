// Assets/Scripts/Runtime/RuedaArmas.cs
// ═══════════════════════════════════════════════════════════════════════════
//  RUEDA DE ARMAS — radial estilo GTA.
//
//  · Mantén TAB para abrir. Apunta con el ratón (usa delta, funciona con el
//    cursor bloqueado del gameplay). Suelta para equipar el arma resaltada.
//  · Cámara lenta (Time.timeScale) mientras está abierta.
//  · Solo muestra las armas que posees (SistemaArmasExtendido.TieneArma).
//  · Construye su propia UI UGUI en runtime — no requiere montaje en escena.
//
//  Capa RUNTIME. Se engancha a SistemaArmasExtendido (armaActual, TieneArma,
//  MunicionDe, iconosArmas, CambiarArma).
// ═══════════════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(60)]
public sealed class RuedaArmas : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("RuedaArmas");
        DontDestroyOnLoad(go);
        go.AddComponent<RuedaArmas>();
    }

    const float RADIO        = 200f;   // px desde el centro a cada arma
    const float ESCALA_LENTA = 0.25f;  // Time.timeScale mientras la rueda está abierta
    const float SENS_DIR     = 0.05f;  // sensibilidad del puntero direccional

    SistemaArmasExtendido _armas;
    Canvas        _canvas;
    CanvasGroup   _grupo;
    RectTransform _centro;
    Text          _titulo;

    struct Slot
    {
        public SistemaArmasExtendido.TipoArma tipo;
        public RectTransform rt;
        public Image fondo;
        public float ang;   // grados, convención pantalla (x derecha, y arriba)
    }
    readonly List<Slot> _slots = new();
    readonly Dictionary<int, Sprite> _sprites = new();

    bool    _abierta;
    int     _sel = -1;
    Vector2 _dir;
    float   _tsPrevio = 1f;

    static Font FuenteUI() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    void Start()
    {
        ConstruirCanvas();
        _grupo.alpha = 0f; _grupo.blocksRaycasts = false;
        _abierta = false;
    }

    void Update()
    {
        if (_armas == null)
        {
            _armas = FindObjectOfType<SistemaArmasExtendido>();
            if (_armas == null) return;
        }

        var kb = Keyboard.current;
        var ms = Mouse.current;
        bool quiereAbrir = kb != null && kb.tabKey.isPressed;

        if (quiereAbrir && !_abierta)      Abrir();
        else if (!quiereAbrir && _abierta) ConfirmarYCerrar();

        if (_abierta)
        {
            if (ms != null)
            {
                _dir += ms.delta.ReadValue() * SENS_DIR;
                if (_dir.magnitude > 1f) _dir = _dir.normalized;
            }
            ActualizarSeleccion();
        }
    }

    void Abrir()
    {
        ReconstruirSlots();
        if (_slots.Count == 0) return;
        _abierta  = true;
        _dir      = Vector2.zero;
        _sel      = -1;
        _tsPrevio = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = ESCALA_LENTA;
        _grupo.alpha = 1f; _grupo.blocksRaycasts = true;
        ActualizarSeleccion();
    }

    void ConfirmarYCerrar()
    {
        if (_sel >= 0 && _sel < _slots.Count)
            _armas.CambiarArma(_slots[_sel].tipo);
        Cerrar();
    }

    void Cerrar()
    {
        _abierta = false;
        Time.timeScale = _tsPrevio <= 0f ? 1f : _tsPrevio;
        if (_grupo != null) { _grupo.alpha = 0f; _grupo.blocksRaycasts = false; }
    }

    void ActualizarSeleccion()
    {
        int nuevo = -1;
        if (_dir.sqrMagnitude > 0.04f)
        {
            float angPuntero = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg;
            float mejor = 999f;
            for (int i = 0; i < _slots.Count; i++)
            {
                float diff = Mathf.Abs(Mathf.DeltaAngle(angPuntero, _slots[i].ang));
                if (diff < mejor) { mejor = diff; nuevo = i; }
            }
        }

        if (nuevo != _sel)
        {
            _sel = nuevo;
            for (int i = 0; i < _slots.Count; i++)
            {
                bool s = (i == _sel);
                _slots[i].fondo.color = s ? new Color(0.90f, 0.30f, 0.15f, 0.95f)
                                          : new Color(0.10f, 0.10f, 0.12f, 0.80f);
                _slots[i].rt.localScale = Vector3.one * (s ? 1.15f : 1f);
            }
        }
        ActualizarTitulo();
    }

    void ActualizarTitulo()
    {
        if (_titulo == null) return;
        if (_sel < 0) { _titulo.text = "—"; return; }
        var t   = _slots[_sel].tipo;
        int idx = (int)t;
        string n = idx < SistemaArmasExtendido.NombresArma.Length
                 ? SistemaArmasExtendido.NombresArma[idx] : t.ToString();
        int mun = _armas.MunicionDe(t);
        _titulo.text = (mun >= 999 || mun < 0) ? n : $"{n}   ·   {mun}";
    }

    // ── Slots según armas poseídas ────────────────────────────────────────
    void ReconstruirSlots()
    {
        foreach (var s in _slots) if (s.rt != null) Destroy(s.rt.gameObject);
        _slots.Clear();

        var poseidas = new List<SistemaArmasExtendido.TipoArma>();
        foreach (SistemaArmasExtendido.TipoArma t in System.Enum.GetValues(typeof(SistemaArmasExtendido.TipoArma)))
            if (_armas.TieneArma(t)) poseidas.Add(t);

        int n = poseidas.Count;
        if (n == 0) return;

        for (int i = 0; i < n; i++)
        {
            float ang = 90f - i * (360f / n);          // empieza arriba, horario
            float rad = ang * Mathf.Deg2Rad;
            Vector2 pos = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * RADIO;

            var slotGO = new GameObject($"Slot_{poseidas[i]}", typeof(RectTransform));
            var rt = (RectTransform)slotGO.transform;
            rt.SetParent(_centro, false);
            rt.sizeDelta = new Vector2(118, 118);
            rt.anchoredPosition = pos;

            var fondo = slotGO.AddComponent<Image>();
            fondo.color = new Color(0.10f, 0.10f, 0.12f, 0.80f);

            var sprite = SpriteDe((int)poseidas[i]);
            if (sprite != null)
            {
                var icoGO = new GameObject("Icono", typeof(RectTransform));
                var irt = (RectTransform)icoGO.transform; irt.SetParent(rt, false);
                irt.sizeDelta = new Vector2(82, 82);
                var ico = icoGO.AddComponent<Image>();
                ico.sprite = sprite; ico.preserveAspect = true;
            }
            else
            {
                var lblGO = new GameObject("Label", typeof(RectTransform));
                var lrt = (RectTransform)lblGO.transform; lrt.SetParent(rt, false);
                lrt.sizeDelta = new Vector2(112, 112);
                var label = lblGO.AddComponent<Text>();
                label.font = FuenteUI();
                label.alignment = TextAnchor.MiddleCenter;
                label.fontSize = 15; label.color = Color.white;
                int idx = (int)poseidas[i];
                label.text = idx < SistemaArmasExtendido.NombresArma.Length
                           ? SistemaArmasExtendido.NombresArma[idx] : poseidas[i].ToString();
            }

            _slots.Add(new Slot { tipo = poseidas[i], rt = rt, fondo = fondo, ang = ang });
        }
    }

    Sprite SpriteDe(int idx)
    {
        if (_armas.iconosArmas == null || idx >= _armas.iconosArmas.Length) return null;
        var tex = _armas.iconosArmas[idx];
        if (tex == null) return null;
        if (_sprites.TryGetValue(idx, out var sp)) return sp;
        sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        _sprites[idx] = sp;
        return sp;
    }

    // ── Canvas raíz ───────────────────────────────────────────────────────
    void ConstruirCanvas()
    {
        var go = new GameObject("RuedaArmas_Canvas");
        go.transform.SetParent(transform, false);

        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 5000;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        _grupo = go.AddComponent<CanvasGroup>();

        var bgGO = new GameObject("Backdrop", typeof(RectTransform));
        var brt = (RectTransform)bgGO.transform; brt.SetParent(go.transform, false);
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
        bgGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

        var cGO = new GameObject("Centro", typeof(RectTransform));
        _centro = (RectTransform)cGO.transform; _centro.SetParent(go.transform, false);
        _centro.anchorMin = _centro.anchorMax = new Vector2(0.5f, 0.5f);
        _centro.anchoredPosition = Vector2.zero;

        var tGO = new GameObject("Titulo", typeof(RectTransform));
        var trt = (RectTransform)tGO.transform; trt.SetParent(_centro, false);
        trt.sizeDelta = new Vector2(420, 50); trt.anchoredPosition = Vector2.zero;
        _titulo = tGO.AddComponent<Text>();
        _titulo.font = FuenteUI();
        _titulo.alignment = TextAnchor.MiddleCenter;
        _titulo.fontSize = 26; _titulo.color = Color.white; _titulo.text = "—";
    }
}
