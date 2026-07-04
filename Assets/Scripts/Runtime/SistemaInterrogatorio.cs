// Assets/Scripts/Runtime/SistemaInterrogatorio.cs
// ═══════════════════════════════════════════════════════════════════════════
//  INTERROGATORIO — SOBREVIVIR a la detención (no ejercer nada).
//
//  El jugador, detenido, debe AGUANTAR sin delatar a los suyos. Es una secuencia
//  ABSTRACTA y elíptica (cine negro): la violencia queda FUERA DE PLANO —
//  fundido a negro, rótulos secos, el paso de horas que no cuentas— y lo que se
//  juega es el AGUANTE y la decisión de CALLAR.
//
//    · Cada ronda: un rótulo sobrio (no gráfico) y dos opciones:
//        [1] Callar  → resistes; gasta aguante.
//        [2] Hablar  → cedes; termina, pero pierdes un aliado y apoyo.
//    · Si aguantas todas las rondas con resolución > 0 → te sueltan sin haber
//      dicho nada: SUBE el apoyo popular (no te quebraron).
//    · Si la resolución llega a 0 → te quiebran: termina con consecuencia.
//
//  Llamar desde el flujo de arresto:  SistemaInterrogatorio.I.Iniciar();
//  (tecla K de prueba). Eventos: AlEvento("aguante_resistido"/"aguante_cedido").
//  Capa RUNTIME. Auto-arranque; sin montaje en escena.
// ═══════════════════════════════════════════════════════════════════════════
using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(97)]
public sealed class SistemaInterrogatorio : MonoBehaviour
{
    public static SistemaInterrogatorio I { get; private set; }
    public static event Action<string> AlEvento;
    public static bool EnCurso { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaInterrogatorio");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaInterrogatorio>();
    }

    // Rótulos sobrios, sin describir actos — solo el cerco y el cansancio.
    static readonly string[] RONDAS =
    {
        "El cuartel. Horas que no cuentas.",
        "Repiten la misma pregunta. Un nombre. El de Amaia.",
        "Frío. Sueño. No te dejan cerrar los ojos.",
        "Insisten. Te recuerdan que nadie sabe que estás aquí.",
        "Amanece, o eso crees. Solo queda aguantar un poco más.",
    };

    Canvas _canvas; CanvasGroup _grupo; Text _rotulo, _opciones; Image _barra;
    int _ronda; float _resolucion;

    void Awake()
    {
        if (I != null) { Destroy(gameObject); return; }
        I = this; Construir(); _grupo.alpha = 0;
        EventBus.Subscribe<PlayerArrestedEvent>(OnArrestado);   // detención → interrogatorio
    }
    void OnDestroy() { if (I == this) EventBus.Unsubscribe<PlayerArrestedEvent>(OnArrestado); }
    void OnArrestado(PlayerArrestedEvent _) => Iniciar();

    public void Iniciar()
    {
        if (EnCurso) return;
        EnCurso = true;
        _ronda = 0; _resolucion = 100f;
        _grupo.alpha = 1f;
        Pintar();
    }

    void Update()
    {
        if (EnCurso)
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.digit1Key.wasPressedThisFrame) Callar();
            else if (kb.digit2Key.wasPressedThisFrame) Hablar();
            return;
        }
        var k = Keyboard.current;
        if (k != null && k.kKey.wasPressedThisFrame) Iniciar();   // prueba
    }

    void Callar()
    {
        _resolucion -= UnityEngine.Random.Range(22f, 34f);   // aguantar cuesta
        if (_resolucion <= 0f) { Quebrado(); return; }
        _ronda++;
        if (_ronda >= RONDAS.Length) Resistido();
        else Pintar();
    }

    void Hablar() => Quebrado(cediste: true);

    void Resistido()
    {
        SistemaApoyoPopular.Instance?.SumarApoyo(12f, "no te quebraron");
        AlEvento?.Invoke("aguante_resistido");
        Cerrar("No has dicho nada. Te sueltan al alba, entero por dentro.");
    }

    void Quebrado(bool cediste = false)
    {
        SistemaApoyoPopular.Instance?.RestarApoyo(8f, cediste ? "cediste" : "te quebraron");
        AlEvento?.Invoke("aguante_cedido");
        Cerrar(cediste ? "Dices un nombre. Te sueltan. No te lo perdonarás."
                       : "No puedes más. Algo se rompe, y no son los huesos.");
    }

    void Cerrar(string epitafio)
    {
        EnCurso = false;
        _rotulo.text = epitafio;
        _opciones.text = "";
        _barra.fillAmount = Mathf.Clamp01(_resolucion / 100f);
        CancelInvoke(nameof(Ocultar));
        Invoke(nameof(Ocultar), 3.5f);
    }
    void Ocultar() { if (_grupo != null) _grupo.alpha = 0f; }

    void Pintar()
    {
        _rotulo.text = RONDAS[Mathf.Clamp(_ronda, 0, RONDAS.Length - 1)];
        _opciones.text = "[1] Callar        [2] Hablar";
        _barra.fillAmount = Mathf.Clamp01(_resolucion / 100f);
    }

    // ── UI: fundido oscuro + rótulos ──────────────────────────────────────
    static Font FuenteUI() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    void Construir()
    {
        var go = new GameObject("Interrogatorio_Canvas"); go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>(); _canvas.renderMode = RenderMode.ScreenSpaceOverlay; _canvas.sortingOrder = 6100;
        var sc = go.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920,1080);
        _grupo = go.AddComponent<CanvasGroup>();

        var bg = new GameObject("Negro", typeof(RectTransform)); var brt=(RectTransform)bg.transform; brt.SetParent(go.transform,false);
        brt.anchorMin=Vector2.zero; brt.anchorMax=Vector2.one; brt.offsetMin=Vector2.zero; brt.offsetMax=Vector2.zero;
        bg.AddComponent<Image>().color = new Color(0,0,0,0.96f);

        _rotulo = Txt(go.transform, new Vector2(0.5f,0.5f), new Vector2(0,60), new Vector2(1200,120), 28, TextAnchor.MiddleCenter);
        _rotulo.fontStyle = FontStyle.Italic;
        _opciones = Txt(go.transform, new Vector2(0.5f,0.5f), new Vector2(0,-60), new Vector2(900,40), 22, TextAnchor.MiddleCenter);
        _opciones.color = new Color(0.8f,0.85f,0.95f);

        var barGO = new GameObject("Aguante", typeof(RectTransform)); var bart=(RectTransform)barGO.transform; bart.SetParent(go.transform,false);
        bart.anchorMin=bart.anchorMax=new Vector2(0.5f,0.5f); bart.anchoredPosition=new Vector2(0,-120); bart.sizeDelta=new Vector2(360,10);
        barGO.AddComponent<Image>().color = new Color(0.2f,0.2f,0.2f);
        var fGO = new GameObject("Fill", typeof(RectTransform)); var frt=(RectTransform)fGO.transform; frt.SetParent(bart,false);
        frt.anchorMin=Vector2.zero; frt.anchorMax=Vector2.one; frt.offsetMin=Vector2.zero; frt.offsetMax=Vector2.zero;
        _barra = fGO.AddComponent<Image>(); _barra.color = new Color(0.7f,0.7f,0.6f);
        _barra.type = Image.Type.Filled; _barra.fillMethod = Image.FillMethod.Horizontal; _barra.fillOrigin = 0; _barra.fillAmount = 1f;
    }
    static Text Txt(Transform p, Vector2 anc, Vector2 pos, Vector2 size, int fs, TextAnchor al)
    {
        var go=new GameObject("T",typeof(RectTransform)); var rt=(RectTransform)go.transform; rt.SetParent(p,false);
        rt.anchorMin=rt.anchorMax=anc; rt.pivot=new Vector2(0.5f,0.5f); rt.anchoredPosition=pos; rt.sizeDelta=size;
        var t=go.AddComponent<Text>(); t.font=FuenteUI(); t.fontSize=fs; t.alignment=al; t.color=Color.white;
        t.horizontalOverflow=HorizontalWrapMode.Wrap; return t;
    }
}
