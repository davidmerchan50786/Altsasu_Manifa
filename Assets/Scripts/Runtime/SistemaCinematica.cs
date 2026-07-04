// Assets/Scripts/Runtime/SistemaCinematica.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CINEMÁTICAS IN-ENGINE — secuenciador de tomas para cutscenes de misión.
//
//  Reproduce una SecuenciaCine = lista de TOMAS (posición de cámara + punto al
//  que mira + duración + rótulo + evento). Durante la cinemática:
//    · activa una cámara de cine propia (HDRP-safe) con prioridad alta,
//    · pone barras letterbox (look cine negro) y un rótulo de subtítulo,
//    · dispara el evento de cada toma (SistemaCinematica.AlEvento) para que
//      misiones / apoyo popular reaccionen,
//    · al terminar restaura todo y llama onFin.
//
//  Saltable con ESC. EnCurso (static) para que otros sistemas se congelen.
//  Capa RUNTIME. Auto-arranque del singleton; sin montaje en escena.
// ═══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.HighDefinition;

[Serializable]
public struct TomaCine
{
    public Vector3 pos;          // posición de cámara
    public Vector3 mira;         // punto al que mira
    public float   duracion;     // s
    public bool    corte;        // true = corte seco; false = travelling suave desde la toma previa
    public string  rotulo;       // subtítulo opcional
    public string  evento;       // evento opcional al entrar en la toma
}

[Serializable]
public class SecuenciaCine
{
    public List<TomaCine> tomas = new();
}

[DefaultExecutionOrder(98)]
public sealed class SistemaCinematica : MonoBehaviour
{
    public static SistemaCinematica I { get; private set; }
    public static event Action<string> AlEvento;
    public static bool EnCurso { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaCinematica");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaCinematica>();
    }

    Camera _cam;
    Canvas _canvas;
    RectTransform _barraSup, _barraInf;
    Text _rotulo;
    Coroutine _co;

    void Awake() { if (I != null) { Destroy(gameObject); return; } I = this; Construir(); }

    public void Reproducir(SecuenciaCine sec, Action onFin = null)
    {
        if (sec == null || sec.tomas == null || sec.tomas.Count == 0) { onFin?.Invoke(); return; }
        if (_co != null) StopCoroutine(_co);
        _co = StartCoroutine(Run(sec, onFin));
    }

    IEnumerator Run(SecuenciaCine sec, Action onFin)
    {
        EnCurso = true;
        _cam.gameObject.SetActive(true);
        _canvas.enabled = true;
        yield return Letterbox(true);

        Vector3 prevPos = sec.tomas[0].pos;
        for (int i = 0; i < sec.tomas.Count; i++)
        {
            var t = sec.tomas[i];
            if (!string.IsNullOrEmpty(t.evento)) AlEvento?.Invoke(t.evento);
            _rotulo.text = t.rotulo ?? "";

            float dur = Mathf.Max(0.1f, t.duracion);
            float e = 0f;
            Vector3 desde = t.corte ? t.pos : prevPos;
            while (e < 1f)
            {
                e += Time.unscaledDeltaTime / dur;
                Vector3 p = t.corte ? t.pos : Vector3.Lerp(desde, t.pos, Mathf.SmoothStep(0, 1, e));
                _cam.transform.position = p;
                _cam.transform.rotation = Quaternion.LookRotation((t.mira - p).normalized, Vector3.up);
                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { goto fin; }
                yield return null;
            }
            prevPos = t.pos;
        }
        fin:
        yield return Letterbox(false);
        _canvas.enabled = false;
        _cam.gameObject.SetActive(false);
        EnCurso = false;
        _co = null;
        onFin?.Invoke();
    }

    IEnumerator Letterbox(bool entrar)
    {
        float t = 0f, dur = 0.4f;
        float objetivo = entrar ? 110f : 0f, ini = entrar ? 0f : 110f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / dur;
            float h = Mathf.Lerp(ini, objetivo, t);
            _barraSup.sizeDelta = new Vector2(0, h);
            _barraInf.sizeDelta = new Vector2(0, h);
            yield return null;
        }
    }

    // ── Construcción ──────────────────────────────────────────────────────
    void Construir()
    {
        var camGO = new GameObject("CamaraCine");
        camGO.transform.SetParent(transform, false);
        _cam = camGO.AddComponent<Camera>();
        _cam.depth = 20;                       // por encima de la cámara del jugador
        var hd = camGO.AddComponent<HDAdditionalCameraData>();
        hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;
        camGO.SetActive(false);

        var go = new GameObject("Cine_Canvas");
        go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 6000;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        _barraSup = Barra(go.transform, new Vector2(0, 1));
        _barraInf = Barra(go.transform, new Vector2(0, 0));

        var rGO = new GameObject("Rotulo", typeof(RectTransform));
        var rrt = (RectTransform)rGO.transform; rrt.SetParent(go.transform, false);
        rrt.anchorMin = new Vector2(0.5f, 0); rrt.anchorMax = new Vector2(0.5f, 0);
        rrt.pivot = new Vector2(0.5f, 0); rrt.anchoredPosition = new Vector2(0, 130);
        rrt.sizeDelta = new Vector2(1400, 60);
        _rotulo = rGO.AddComponent<Text>();
        _rotulo.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _rotulo.alignment = TextAnchor.MiddleCenter; _rotulo.fontSize = 24; _rotulo.color = Color.white;
        _rotulo.horizontalOverflow = HorizontalWrapMode.Wrap;

        _canvas.enabled = false;
    }

    RectTransform Barra(Transform parent, Vector2 anchor)
    {
        var go = new GameObject("Letterbox", typeof(RectTransform));
        var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0, anchor.y); rt.anchorMax = new Vector2(1, anchor.y);
        rt.pivot = new Vector2(0.5f, anchor.y);
        rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(0, 0);
        go.AddComponent<Image>().color = Color.black;
        return rt;
    }
}
