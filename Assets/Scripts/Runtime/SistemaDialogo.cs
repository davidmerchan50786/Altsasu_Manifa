// Assets/Scripts/Runtime/SistemaDialogo.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE DIÁLOGO / BRANCHING — motor de conversaciones con ramificación.
//
//  · Conversaciones como ScriptableObject (menú Alsasua/Conversación) o creadas
//    por código. Cada nodo: hablante + texto + (opciones con destino) o
//    (siguiente lineal). Las opciones ramifican saltando a un nodo por id.
//  · UI UGUI auto-construida (panel inferior + nombre + texto + opciones
//    numeradas). Sin EventSystem: se elige con teclas 1-9, Espacio avanza.
//  · Dispara eventos por nombre (SistemaDialogo.AlEvento) para que misiones,
//    apoyo popular, etc. reaccionen ("dar_arma", "subir_apoyo", …).
//
//  Uso:
//    SistemaDialogo.I.Iniciar(miConversacion);
//    SistemaDialogo.AlEvento += e => { if (e=="subir_apoyo") ... };
//
//  Capa RUNTIME. Auto-arranque del singleton; sin montaje en escena.
// ═══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

[Serializable]
public class OpcionDialogo
{
    public string texto;
    public string destino;   // id del nodo al que salta
    public string evento;    // opcional: evento que dispara al elegirla
}

[Serializable]
public class NodoDialogo
{
    public string id;
    public string hablante;
    [TextArea(2, 5)] public string texto;
    public string evento;                 // evento al ENTRAR en el nodo
    public string siguiente;              // nodo lineal (si no hay opciones)
    public OpcionDialogo[] opciones;
}

[CreateAssetMenu(menuName = "Alsasua/Conversación", fileName = "Conversacion")]
public class ConversacionDialogo : ScriptableObject
{
    public string nodoInicial = "inicio";
    public NodoDialogo[] nodos;
}

[DefaultExecutionOrder(95)]
public sealed class SistemaDialogo : MonoBehaviour
{
    public static SistemaDialogo I { get; private set; }
    public static event Action<string> AlEvento;
    public bool EnDialogo { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaDialogo");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaDialogo>();
    }

    Dictionary<string, NodoDialogo> _nodos;
    NodoDialogo _actual;

    Canvas _canvas; CanvasGroup _grupo;
    Text _nombre, _cuerpo;
    readonly List<Text> _opcUI = new();
    RectTransform _opcCont;

    void Awake() { if (I != null) { Destroy(gameObject); return; } I = this; ConstruirUI(); Ocultar(); }

    // ── API ───────────────────────────────────────────────────────────────
    public void Iniciar(ConversacionDialogo conv)
    {
        if (conv == null || conv.nodos == null || conv.nodos.Length == 0) return;
        _nodos = new Dictionary<string, NodoDialogo>();
        foreach (var n in conv.nodos) if (n != null && !string.IsNullOrEmpty(n.id)) _nodos[n.id] = n;
        EnDialogo = true;
        _grupo.alpha = 1f;
        IrA(conv.nodoInicial);
    }

    public void Terminar()
    {
        EnDialogo = false;
        Ocultar();
    }

    // ── Navegación ────────────────────────────────────────────────────────
    void IrA(string id)
    {
        if (_nodos == null || !_nodos.TryGetValue(id, out _actual)) { Terminar(); return; }
        if (!string.IsNullOrEmpty(_actual.evento)) AlEvento?.Invoke(_actual.evento);
        Pintar(_actual);
    }

    void Avanzar()
    {
        if (_actual == null) { Terminar(); return; }
        bool tieneOpciones = _actual.opciones != null && _actual.opciones.Length > 0;
        if (tieneOpciones) return;                          // espera elección
        if (!string.IsNullOrEmpty(_actual.siguiente)) IrA(_actual.siguiente);
        else Terminar();
    }

    void Elegir(int i)
    {
        if (_actual?.opciones == null || i < 0 || i >= _actual.opciones.Length) return;
        var o = _actual.opciones[i];
        if (!string.IsNullOrEmpty(o.evento)) AlEvento?.Invoke(o.evento);
        if (!string.IsNullOrEmpty(o.destino)) IrA(o.destino);
        else Terminar();
    }

    void Update()
    {
        if (!EnDialogo) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.escapeKey.wasPressedThisFrame) { Terminar(); return; }

        bool tieneOpciones = _actual != null && _actual.opciones != null && _actual.opciones.Length > 0;
        if (!tieneOpciones)
        {
            if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame) Avanzar();
            return;
        }

        var digitos = new[] { kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key,
                              kb.digit5Key, kb.digit6Key, kb.digit7Key, kb.digit8Key, kb.digit9Key };
        for (int i = 0; i < _actual.opciones.Length && i < digitos.Length; i++)
            if (digitos[i].wasPressedThisFrame) { Elegir(i); return; }
    }

    // ── Pintado ───────────────────────────────────────────────────────────
    void Pintar(NodoDialogo n)
    {
        _nombre.text = string.IsNullOrEmpty(n.hablante) ? "" : n.hablante;
        _cuerpo.text = n.texto;

        foreach (var t in _opcUI) t.gameObject.SetActive(false);
        if (n.opciones != null)
        {
            for (int i = 0; i < n.opciones.Length; i++)
            {
                Text t = i < _opcUI.Count ? _opcUI[i] : CrearOpcionUI();
                t.gameObject.SetActive(true);
                t.text = $"{i + 1}.  {n.opciones[i].texto}";
            }
        }
        bool hayOpc = n.opciones != null && n.opciones.Length > 0;
        _pista.text = hayOpc ? "Pulsa el número de tu respuesta" : "Espacio para continuar  ·  Esc para salir";
    }

    void Ocultar() { if (_grupo != null) _grupo.alpha = 0f; }

    // ── Construcción UI ───────────────────────────────────────────────────
    Text _pista;
    static Font FuenteUI() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    void ConstruirUI()
    {
        var go = new GameObject("Dialogo_Canvas");
        go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 5200;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        _grupo = go.AddComponent<CanvasGroup>();

        var panel = new GameObject("Panel", typeof(RectTransform));
        var prt = (RectTransform)panel.transform; prt.SetParent(go.transform, false);
        prt.anchorMin = new Vector2(0.5f, 0f); prt.anchorMax = new Vector2(0.5f, 0f);
        prt.pivot = new Vector2(0.5f, 0f);
        prt.anchoredPosition = new Vector2(0, 40); prt.sizeDelta = new Vector2(1300, 320);
        panel.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.07f, 0.92f);

        _nombre = NuevoTexto(prt, new Vector2(0, 1), new Vector2(30, -16), new Vector2(600, 40), 24, TextAnchor.UpperLeft);
        _nombre.color = new Color(0.95f, 0.78f, 0.35f);

        _cuerpo = NuevoTexto(prt, new Vector2(0, 1), new Vector2(30, -60), new Vector2(1240, 130), 21, TextAnchor.UpperLeft);

        var opc = new GameObject("Opciones", typeof(RectTransform));
        _opcCont = (RectTransform)opc.transform; _opcCont.SetParent(prt, false);
        _opcCont.anchorMin = new Vector2(0, 0); _opcCont.anchorMax = new Vector2(1, 0);
        _opcCont.pivot = new Vector2(0, 0);
        _opcCont.anchoredPosition = new Vector2(30, 44); _opcCont.sizeDelta = new Vector2(-60, 150);
        var vlg = opc.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 4; vlg.childForceExpandHeight = false; vlg.childControlHeight = true; vlg.childControlWidth = true;

        _pista = NuevoTexto(prt, new Vector2(1, 0), new Vector2(-20, 12), new Vector2(620, 24), 14, TextAnchor.LowerRight);
        _pista.color = new Color(0.6f, 0.6f, 0.65f);
    }

    Text CrearOpcionUI()
    {
        var t = NuevoTexto(_opcCont, new Vector2(0, 1), Vector2.zero, new Vector2(1200, 28), 19, TextAnchor.MiddleLeft);
        var le = t.gameObject.AddComponent<LayoutElement>(); le.minHeight = 28;
        t.color = new Color(0.85f, 0.9f, 1f);
        _opcUI.Add(t);
        return t;
    }

    static Text NuevoTexto(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize, TextAnchor align)
    {
        var go = new GameObject("Texto", typeof(RectTransform));
        var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(anchor.x, anchor.y);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var t = go.AddComponent<Text>();
        t.font = FuenteUI(); t.fontSize = fontSize; t.alignment = align; t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }
}
