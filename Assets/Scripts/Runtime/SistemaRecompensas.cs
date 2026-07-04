// Assets/Scripts/Runtime/SistemaRecompensas.cs
// ═══════════════════════════════════════════════════════════════════════════
//  RECOMPENSAS DE MISIÓN — al completar una misión, premia al jugador.
//
//  Se suscribe a SistemaMisiones.OnMisionCompletada(id) y entrega:
//    · dinero (IEconomyService.GanarDinero)
//    · apoyo popular (SistemaApoyoPopular)
//    · (opcional) desbloqueo de arma
//  con un cartel de recompensa en pantalla.
//
//  Define recompensas por misión con SistemaRecompensas.I.Definir(...); si una
//  misión no tiene recompensa definida, usa una por defecto.
//  Capa RUNTIME. Auto-arranque; sin montaje en escena.
// ═══════════════════════════════════════════════════════════════════════════
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public struct Recompensa
{
    public int    dinero;
    public float  apoyo;
    public string mensaje;
    public SistemaArmasExtendido.TipoArma? armaDesbloqueada;
}

[DefaultExecutionOrder(95)]
public sealed class SistemaRecompensas : MonoBehaviour
{
    public static SistemaRecompensas I { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaRecompensas");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaRecompensas>();
    }

    readonly Dictionary<string, Recompensa> _tabla = new();

    Canvas _canvas; CanvasGroup _grupo; Text _titulo, _detalle;

    void Awake() { if (I != null) { Destroy(gameObject); return; } I = this; Construir(); CargarPorDefecto(); }
    void OnEnable()  => SistemaMisiones.OnMisionCompletada += Otorgar;
    void OnDisable() => SistemaMisiones.OnMisionCompletada -= Otorgar;

    /// <summary>Define la recompensa de una misión por su id.</summary>
    public void Definir(string idMision, int dinero, float apoyo, string mensaje = "",
                        SistemaArmasExtendido.TipoArma? arma = null)
        => _tabla[idMision] = new Recompensa { dinero = dinero, apoyo = apoyo, mensaje = mensaje, armaDesbloqueada = arma };

    void Otorgar(string idMision)
    {
        Recompensa r = _tabla.TryGetValue(idMision, out var def)
            ? def
            : new Recompensa { dinero = 250, apoyo = 5f, mensaje = "Misión completada" };

        ServiceLocator.Get<IEconomyService>()?.GanarDinero(r.dinero);
        if (r.apoyo != 0f) SistemaApoyoPopular.Instance?.SumarApoyo(r.apoyo, "misión");
        if (r.armaDesbloqueada.HasValue)
            UnityEngine.Object.FindObjectOfType<SistemaArmasExtendido>()?.RecogerArma(r.armaDesbloqueada.Value, 30);

        Mostrar(r);
    }

    void CargarPorDefecto()
    {
        // Recompensas del Acto I (ids de ejemplo; ajusta a los reales de SistemaMisiones)
        Definir("M01", 200,  4f, "Recuperas lo de Joseba");
        Definir("M02", 300,  8f, "Amaia confía en ti");
        Definir("M03", 350,  3f, "El nombre del Francés sale a la luz");
        Definir("M04", 400,  6f, "El pueblo empieza a mirarte distinto");
        Definir("M06", 500, 12f, "La manifa fue tuya");
    }

    // ── UI ────────────────────────────────────────────────────────────────
    void Mostrar(Recompensa r)
    {
        _titulo.text  = string.IsNullOrEmpty(r.mensaje) ? "Misión completada" : r.mensaje;
        string apoyoTxt = r.apoyo > 0 ? $"   ·   +{r.apoyo:F0} apoyo" : "";
        string armaTxt  = r.armaDesbloqueada.HasValue ? "   ·   nueva arma" : "";
        _detalle.text = $"+{r.dinero} €{apoyoTxt}{armaTxt}";
        StopAllCoroutines();
        StartCoroutine(Fade());
    }

    IEnumerator Fade()
    {
        _grupo.alpha = 0f;
        float t = 0f;
        while (t < 1f) { t += Time.unscaledDeltaTime * 3f; _grupo.alpha = t; yield return null; }
        yield return new WaitForSecondsRealtime(3f);
        while (t > 0f) { t -= Time.unscaledDeltaTime * 1.5f; _grupo.alpha = Mathf.Clamp01(t); yield return null; }
        _grupo.alpha = 0f;
    }

    static Font FuenteUI() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    void Construir()
    {
        var go = new GameObject("Recompensa_Canvas"); go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>(); _canvas.renderMode = RenderMode.ScreenSpaceOverlay; _canvas.sortingOrder = 5300;
        var sc = go.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920,1080);
        _grupo = go.AddComponent<CanvasGroup>(); _grupo.alpha = 0f;

        var panel = new GameObject("Panel", typeof(RectTransform)); var prt=(RectTransform)panel.transform; prt.SetParent(go.transform,false);
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 1f); prt.pivot = new Vector2(0.5f, 1f);
        prt.anchoredPosition = new Vector2(0, -90); prt.sizeDelta = new Vector2(680, 96);
        panel.AddComponent<Image>().color = new Color(0.06f, 0.07f, 0.09f, 0.92f);

        _titulo = Txt(prt, new Vector2(0.5f,1), new Vector2(0,-16), new Vector2(640,34), 22, TextAnchor.MiddleCenter);
        _titulo.color = new Color(0.95f,0.85f,0.4f);
        _detalle = Txt(prt, new Vector2(0.5f,0), new Vector2(0,16), new Vector2(640,34), 24, TextAnchor.MiddleCenter);
    }
    static Text Txt(Transform p, Vector2 anc, Vector2 pos, Vector2 size, int fs, TextAnchor al)
    {
        var go=new GameObject("T",typeof(RectTransform)); var rt=(RectTransform)go.transform; rt.SetParent(p,false);
        rt.anchorMin=rt.anchorMax=anc; rt.pivot=new Vector2(0.5f,anc.y); rt.anchoredPosition=pos; rt.sizeDelta=size;
        var t=go.AddComponent<Text>(); t.font=FuenteUI(); t.fontSize=fs; t.alignment=al; t.color=Color.white; return t;
    }
}
