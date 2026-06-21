// Assets/Scripts/Runtime/PanelInventario.cs
// ═══════════════════════════════════════════════════════════════════════════
//  PANEL DE INVENTARIO — pulsa I (o Esc para cerrar).
//
//  Muestra una rejilla con TODAS las armas: las que posees con icono, nombre
//  y munición; las que no, atenuadas con candado. Lee SistemaArmasExtendido.
//  Construye su propia UI UGUI en runtime — no requiere montaje en escena.
//
//  Capa RUNTIME. Pensado para crecer: añade aquí slots de objetos de misión,
//  dinero, etc., cuando existan esos sistemas.
// ═══════════════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(60)]
public sealed class PanelInventario : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("PanelInventario");
        DontDestroyOnLoad(go);
        go.AddComponent<PanelInventario>();
    }

    SistemaArmasExtendido _armas;
    Canvas        _canvas;
    CanvasGroup   _grupo;
    RectTransform _grid;
    readonly Dictionary<int, Sprite> _sprites = new();
    bool _abierto;

    static Font FuenteUI() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    void Start()
    {
        ConstruirCanvas();
        _grupo.alpha = 0f; _grupo.blocksRaycasts = false;
    }

    void Update()
    {
        if (_armas == null)
        {
            _armas = FindObjectOfType<SistemaArmasExtendido>();
            if (_armas == null) return;
        }
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.iKey.wasPressedThisFrame) Alternar();
        else if (_abierto && kb.escapeKey.wasPressedThisFrame) Cerrar();
    }

    void Alternar() { if (_abierto) Cerrar(); else Abrir(); }

    void Abrir()
    {
        Refrescar();
        _abierto = true;
        _grupo.alpha = 1f; _grupo.blocksRaycasts = true;
    }

    void Cerrar()
    {
        _abierto = false;
        _grupo.alpha = 0f; _grupo.blocksRaycasts = false;
    }

    void Refrescar()
    {
        for (int i = _grid.childCount - 1; i >= 0; i--) Destroy(_grid.GetChild(i).gameObject);
        foreach (SistemaArmasExtendido.TipoArma t in System.Enum.GetValues(typeof(SistemaArmasExtendido.TipoArma)))
            CrearSlot(t, _armas.TieneArma(t));
    }

    void CrearSlot(SistemaArmasExtendido.TipoArma t, bool tiene)
    {
        int idx = (int)t;
        string nombre = idx < SistemaArmasExtendido.NombresArma.Length
                      ? SistemaArmasExtendido.NombresArma[idx] : t.ToString();

        var slot = new GameObject($"Slot_{t}", typeof(RectTransform));
        var rt = (RectTransform)slot.transform; rt.SetParent(_grid, false);
        var fondo = slot.AddComponent<Image>();
        fondo.color = tiene ? new Color(0.12f, 0.13f, 0.16f, 0.95f)
                            : new Color(0.07f, 0.07f, 0.08f, 0.95f);

        var sprite = SpriteDe(idx);
        var icoGO = new GameObject("Icono", typeof(RectTransform));
        var irt = (RectTransform)icoGO.transform; irt.SetParent(rt, false);
        irt.anchorMin = new Vector2(0.5f, 1f); irt.anchorMax = new Vector2(0.5f, 1f);
        irt.pivot = new Vector2(0.5f, 1f);
        irt.anchoredPosition = new Vector2(0, -12);
        irt.sizeDelta = new Vector2(80, 80);
        var ico = icoGO.AddComponent<Image>();
        if (sprite != null) { ico.sprite = sprite; ico.preserveAspect = true; ico.color = tiene ? Color.white : new Color(1,1,1,0.25f); }
        else ico.color = new Color(1,1,1,0);

        var nomGO = new GameObject("Nombre", typeof(RectTransform));
        var nrt = (RectTransform)nomGO.transform; nrt.SetParent(rt, false);
        nrt.anchorMin = new Vector2(0, 0); nrt.anchorMax = new Vector2(1, 0);
        nrt.pivot = new Vector2(0.5f, 0f);
        nrt.anchoredPosition = new Vector2(0, 30); nrt.sizeDelta = new Vector2(-8, 26);
        var nom = nomGO.AddComponent<Text>();
        nom.font = FuenteUI(); nom.alignment = TextAnchor.MiddleCenter; nom.fontSize = 15;
        nom.color = tiene ? Color.white : new Color(0.5f, 0.5f, 0.5f);
        nom.text = nombre;

        var munGO = new GameObject("Municion", typeof(RectTransform));
        var mrt = (RectTransform)munGO.transform; mrt.SetParent(rt, false);
        mrt.anchorMin = new Vector2(0, 0); mrt.anchorMax = new Vector2(1, 0);
        mrt.pivot = new Vector2(0.5f, 0f);
        mrt.anchoredPosition = new Vector2(0, 8); mrt.sizeDelta = new Vector2(-8, 22);
        var mun = munGO.AddComponent<Text>();
        mun.font = FuenteUI(); mun.alignment = TextAnchor.MiddleCenter; mun.fontSize = 13;
        if (!tiene) { mun.color = new Color(0.55f,0.45f,0.2f); mun.text = "bloqueada"; }
        else
        {
            int m = _armas.MunicionDe(t);
            mun.color = new Color(0.75f, 0.8f, 0.9f);
            mun.text = (m >= 999 || m < 0) ? "∞" : $"x{m}";
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

    // ── Canvas + panel ────────────────────────────────────────────────────
    void ConstruirCanvas()
    {
        var go = new GameObject("Inventario_Canvas");
        go.transform.SetParent(transform, false);

        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 4900;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        _grupo = go.AddComponent<CanvasGroup>();

        var bgGO = new GameObject("Backdrop", typeof(RectTransform));
        var brt = (RectTransform)bgGO.transform; brt.SetParent(go.transform, false);
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
        bgGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

        var panelGO = new GameObject("Panel", typeof(RectTransform));
        var prt = (RectTransform)panelGO.transform; prt.SetParent(go.transform, false);
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(1180, 760);
        panelGO.AddComponent<Image>().color = new Color(0.06f, 0.06f, 0.08f, 0.92f);

        var titGO = new GameObject("Titulo", typeof(RectTransform));
        var trt = (RectTransform)titGO.transform; trt.SetParent(prt, false);
        trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.anchoredPosition = new Vector2(0, -18); trt.sizeDelta = new Vector2(-40, 50);
        var tit = titGO.AddComponent<Text>();
        tit.font = FuenteUI(); tit.alignment = TextAnchor.MiddleLeft; tit.fontSize = 30;
        tit.color = Color.white; tit.text = "Inventario   ·   armamento";

        var gridGO = new GameObject("Grid", typeof(RectTransform));
        _grid = (RectTransform)gridGO.transform; _grid.SetParent(prt, false);
        _grid.anchorMin = new Vector2(0, 0); _grid.anchorMax = new Vector2(1, 1);
        _grid.offsetMin = new Vector2(30, 30); _grid.offsetMax = new Vector2(-30, -80);
        var glg = gridGO.AddComponent<GridLayoutGroup>();
        glg.cellSize = new Vector2(210, 200);
        glg.spacing  = new Vector2(16, 16);
        glg.padding  = new RectOffset(6, 6, 6, 6);
        glg.childAlignment = TextAnchor.UpperLeft;
    }
}
