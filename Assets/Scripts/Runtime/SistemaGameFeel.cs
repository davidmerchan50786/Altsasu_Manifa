// Assets/Scripts/Runtime/SistemaGameFeel.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GAME FEEL DE COMBATE — hitstop + números de daño flotantes.
//
//    · SistemaGameFeel.Impacto(pos, daño, crítico)  ← lo llaman el disparo y el
//      melee al acertar.
//        - Muestra un número de daño en el punto de impacto que sube y se
//          desvanece (los críticos/ejecuciones más grandes y en otro color).
//        - Aplica HITSTOP en golpes potentes/críticos: micro-congelación
//          (Time.timeScale muy bajo durante unos ms en tiempo real) que da
//          peso al impacto. Respeta el timeScale previo (p.ej. cámara lenta
//          de la rueda de armas).
//
//  No toca la cámara (cero conflicto con ControladorJugador). UI overlay con
//  los números proyectados a pantalla.
//  Capa RUNTIME. Auto-arranque; sin montaje en escena.
// ═══════════════════════════════════════════════════════════════════════════
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(120)]
public sealed class SistemaGameFeel : MonoBehaviour
{
    public static SistemaGameFeel I { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaGameFeel");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaGameFeel>();
    }

    const float HITSTOP_DUR = 0.06f;   // s en tiempo real
    const float HITSTOP_TS  = 0.04f;   // timeScale durante el hitstop
    const int   HITSTOP_MIN = 25;      // daño mínimo para activar hitstop

    Canvas _canvas;
    Camera _cam;
    bool   _enHitstop;

    struct Numero { public RectTransform rt; public Text txt; public Vector3 mundo; public float vida; }
    readonly List<Numero> _activos = new();
    readonly Stack<Numero> _pool = new();

    static Font FuenteUI() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    void Awake() { if (I != null) { Destroy(gameObject); return; } I = this; ConstruirCanvas(); }

    // ── API ───────────────────────────────────────────────────────────────
    public static void Impacto(Vector3 pos, int dano, bool critico)
    {
        if (I == null) return;
        I.MostrarNumero(pos, dano, critico);
        if (critico || dano >= HITSTOP_MIN) I.Hitstop();
    }

    void Hitstop()
    {
        if (_enHitstop) return;
        StartCoroutine(HitstopCo());
    }

    IEnumerator HitstopCo()
    {
        _enHitstop = true;
        float previo = Time.timeScale;
        if (previo > 0f)
        {
            Time.timeScale = HITSTOP_TS * previo;   // relativo: no rompe cámara lenta
            yield return new WaitForSecondsRealtime(HITSTOP_DUR);
            Time.timeScale = previo;
        }
        _enHitstop = false;
    }

    // ── Números de daño ───────────────────────────────────────────────────
    void MostrarNumero(Vector3 mundo, int dano, bool critico)
    {
        var n = _pool.Count > 0 ? _pool.Pop() : CrearNumero();
        n.mundo = mundo + new Vector3(Random.Range(-0.3f, 0.3f), 1.4f, Random.Range(-0.3f, 0.3f));
        n.vida = 1f;
        n.txt.text = critico ? dano + "!" : dano.ToString();
        n.txt.color = critico ? new Color(1f, 0.5f, 0.15f) : new Color(1f, 0.92f, 0.6f);
        n.txt.fontSize = critico ? 34 : 24;
        n.rt.gameObject.SetActive(true);
        _activos.Add(n);
    }

    void LateUpdate()
    {
        if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }
        for (int i = _activos.Count - 1; i >= 0; i--)
        {
            var n = _activos[i];
            n.vida -= Time.unscaledDeltaTime * 1.1f;
            n.mundo += Vector3.up * Time.unscaledDeltaTime * 0.8f;
            if (n.vida <= 0f)
            {
                n.rt.gameObject.SetActive(false);
                _activos.RemoveAt(i); _pool.Push(n);
                continue;
            }
            Vector3 sp = _cam.WorldToScreenPoint(n.mundo);
            if (sp.z < 0f) { n.rt.gameObject.SetActive(false); _activos.RemoveAt(i); _pool.Push(n); continue; }
            n.rt.gameObject.SetActive(true);
            n.rt.position = sp;
            var c = n.txt.color; c.a = Mathf.Clamp01(n.vida); n.txt.color = c;
            _activos[i] = n;
        }
    }

    Numero CrearNumero()
    {
        var go = new GameObject("DmgNum", typeof(RectTransform));
        var rt = (RectTransform)go.transform; rt.SetParent(_canvas.transform, false);
        rt.sizeDelta = new Vector2(120, 40);
        var t = go.AddComponent<Text>();
        t.font = FuenteUI(); t.alignment = TextAnchor.MiddleCenter; t.fontStyle = FontStyle.Bold;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        return new Numero { rt = rt, txt = t };
    }

    void ConstruirCanvas()
    {
        var go = new GameObject("GameFeel_Canvas");
        go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 4800;
        _cam = Camera.main;
    }
}
