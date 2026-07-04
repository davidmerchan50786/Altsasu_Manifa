// Assets/Scripts/Runtime/SistemaTienda.cs
// ═══════════════════════════════════════════════════════════════════════════
//  TIENDA — compra de munición, armas y botiquines con el dinero del jugador.
//
//    · SistemaTienda.I.Abrir()  (o tecla B de prueba) → panel con artículos.
//      Comprar con teclas 1-9; ESC cierra. Usa IEconomyService.GastarDinero.
//    · Catálogo por defecto: munición (pistola/escopeta/fusil), botiquín y
//      desbloqueo de fusil. Cada artículo aplica su efecto al comprarse
//      (RecogerArma, Curar…). Pásale otro catálogo para tiendas específicas.
//
//  UI UGUI auto-construida. Capa RUNTIME. Auto-arranque; sin montaje en escena.
// ═══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public struct Articulo
{
    public string nombre;
    public int    precio;
    public Action efecto;   // qué hace al comprarlo
    public Articulo(string n, int p, Action e) { nombre = n; precio = p; efecto = e; }
}

[DefaultExecutionOrder(94)]
public sealed class SistemaTienda : MonoBehaviour
{
    public static SistemaTienda I { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaTienda");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaTienda>();
    }

    Canvas _canvas; CanvasGroup _grupo; RectTransform _lista; Text _dinero, _aviso;
    bool _abierta;
    List<Articulo> _cat;
    float _tAviso;

    static Font FuenteUI() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    void Awake() { if (I != null) { Destroy(gameObject); return; } I = this; Construir(); _grupo.alpha = 0; _grupo.blocksRaycasts = false; }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.bKey.wasPressedThisFrame) { if (_abierta) Cerrar(); else Abrir(); }
        if (!_abierta) return;
        if (kb.escapeKey.wasPressedThisFrame) { Cerrar(); return; }

        var d = new[]{ kb.digit1Key,kb.digit2Key,kb.digit3Key,kb.digit4Key,kb.digit5Key,kb.digit6Key,kb.digit7Key,kb.digit8Key,kb.digit9Key };
        for (int i = 0; i < _cat.Count && i < d.Length; i++)
            if (d[i].wasPressedThisFrame) { Comprar(i); break; }

        if (_tAviso > 0f) { _tAviso -= Time.unscaledDeltaTime; if (_tAviso <= 0f) _aviso.text = ""; }
        RefrescarDinero();
    }

    public void Abrir(List<Articulo> catalogo = null)
    {
        _cat = catalogo ?? CatalogoPorDefecto();
        Pintar();
        _abierta = true; _grupo.alpha = 1; _grupo.blocksRaycasts = true;
        RefrescarDinero();
    }
    public void Cerrar() { _abierta = false; _grupo.alpha = 0; _grupo.blocksRaycasts = false; }

    void Comprar(int i)
    {
        var eco = ServiceLocator.Get<IEconomyService>();
        var a = _cat[i];
        if (eco == null) { Avisar("Economía no disponible"); return; }
        int precio = Mathf.RoundToInt(a.precio * (1f - SistemaProgresion.DescuentoTienda));
        if (eco.GastarDinero(precio)) { a.efecto?.Invoke(); Avisar($"Comprado: {a.nombre} ({precio} €)"); }
        else Avisar("Dinero insuficiente");
    }

    void Avisar(string s) { _aviso.text = s; _tAviso = 2.5f; }
    void RefrescarDinero()
    {
        var eco = ServiceLocator.Get<IEconomyService>();
        _dinero.text = eco != null ? $"Dinero: {eco.Dinero} €" : "Dinero: —";
    }

    // ── Catálogo por defecto (armería) ────────────────────────────────────
    static List<Articulo> CatalogoPorDefecto()
    {
        var armas = UnityEngine.Object.FindObjectOfType<SistemaArmasExtendido>();
        IDamageable jug = AltsasuCore.Jugador != null ? AltsasuCore.Jugador.GetComponent<IDamageable>() : null;

        return new List<Articulo>
        {
            new Articulo("Munición pistola x15", 50,  () => armas?.RecogerArma(SistemaArmasExtendido.TipoArma.Pistola, 15)),
            new Articulo("Munición escopeta x8", 80,  () => armas?.RecogerArma(SistemaArmasExtendido.TipoArma.Escopeta, 8)),
            new Articulo("Munición fusil x30",   120, () => armas?.RecogerArma(SistemaArmasExtendido.TipoArma.Fusil, 30)),
            new Articulo("Botiquín (+50 vida)",  100, () => jug?.Curar(50)),
            new Articulo("Desbloquear Fusil",    500, () => armas?.RecogerArma(SistemaArmasExtendido.TipoArma.Fusil, 30)),
        };
    }

    // ── UI ────────────────────────────────────────────────────────────────
    void Pintar()
    {
        for (int i = _lista.childCount - 1; i >= 0; i--) Destroy(_lista.GetChild(i).gameObject);
        for (int i = 0; i < _cat.Count; i++)
        {
            var fila = new GameObject($"Art{i}", typeof(RectTransform));
            var rt = (RectTransform)fila.transform; rt.SetParent(_lista, false);
            var le = fila.AddComponent<LayoutElement>(); le.minHeight = 34;
            var t = fila.AddComponent<Text>();
            t.font = FuenteUI(); t.fontSize = 18; t.alignment = TextAnchor.MiddleLeft; t.color = Color.white;
            t.text = $"{i + 1}.  {_cat[i].nombre}      —  {_cat[i].precio} €";
        }
    }

    void Construir()
    {
        var go = new GameObject("Tienda_Canvas"); go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>(); _canvas.renderMode = RenderMode.ScreenSpaceOverlay; _canvas.sortingOrder = 5100;
        var sc = go.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920,1080);
        _grupo = go.AddComponent<CanvasGroup>();

        var bg = new GameObject("BG", typeof(RectTransform)); var brt=(RectTransform)bg.transform; brt.SetParent(go.transform,false);
        brt.anchorMin=Vector2.zero; brt.anchorMax=Vector2.one; brt.offsetMin=Vector2.zero; brt.offsetMax=Vector2.zero;
        bg.AddComponent<Image>().color = new Color(0,0,0,0.72f);

        var panel = new GameObject("Panel", typeof(RectTransform)); var prt=(RectTransform)panel.transform; prt.SetParent(go.transform,false);
        prt.anchorMin=prt.anchorMax=new Vector2(0.5f,0.5f); prt.sizeDelta=new Vector2(780,560);
        panel.AddComponent<Image>().color = new Color(0.06f,0.06f,0.08f,0.95f);

        _dinero = Txt(prt, new Vector2(0,1), new Vector2(28,-20), new Vector2(400,34), 22, TextAnchor.UpperLeft);
        _dinero.color = new Color(0.95f,0.85f,0.4f);
        var titulo = Txt(prt, new Vector2(1,1), new Vector2(-28,-20), new Vector2(300,34), 26, TextAnchor.UpperRight); titulo.text="Tienda";

        var listaGO = new GameObject("Lista", typeof(RectTransform)); _lista=(RectTransform)listaGO.transform; _lista.SetParent(prt,false);
        _lista.anchorMin=new Vector2(0,0); _lista.anchorMax=new Vector2(1,1); _lista.offsetMin=new Vector2(28,70); _lista.offsetMax=new Vector2(-28,-70);
        var vlg = listaGO.AddComponent<VerticalLayoutGroup>(); vlg.spacing=6; vlg.childControlHeight=true; vlg.childControlWidth=true; vlg.childForceExpandHeight=false;

        _aviso = Txt(prt, new Vector2(0.5f,0), new Vector2(0,18), new Vector2(740,30), 16, TextAnchor.MiddleCenter);
        _aviso.color = new Color(0.7f,0.9f,1f);
    }

    static Text Txt(Transform p, Vector2 anc, Vector2 pos, Vector2 size, int fs, TextAnchor al)
    {
        var go=new GameObject("T",typeof(RectTransform)); var rt=(RectTransform)go.transform; rt.SetParent(p,false);
        rt.anchorMin=rt.anchorMax=anc; rt.pivot=new Vector2(anc.x,anc.y); rt.anchoredPosition=pos; rt.sizeDelta=size;
        var t=go.AddComponent<Text>(); t.font=FuenteUI(); t.fontSize=fs; t.alignment=al; t.color=Color.white; return t;
    }
}
