// Assets/Scripts/Runtime/SistemaDrogas.cs
// ═══════════════════════════════════════════════════════════════════════════
//  DROGAS — colocón con efectos VISUALES, de AUDIO y de SISTEMAS (mecánica de
//  juego ficticia, estilo mundo abierto). NO es información real de consumo.
//
//  Tecla U abre el menú; eliges con 1-4 (la borrachera viene de las acciones J):
//    1 Porro  → calma (−paranoia), bruma cálida, puntería algo peor.
//    2 Speed  → acelerón, destellos fríos, +paranoia, bajón al final.
//    3 Chute  → adormece el dolor (recibes menos daño), viñeta oscura, lento.
//    4 Tripi  → alucina: arcoíris ondulante, no aciertas un tiro, larga duración.
//
//  Expone modificadores que leen el disparo (MultDispersion) y el daño recibido
//  (ReduccionDanoRecibido) y el movimiento (MultVelocidad). Overlay y audio
//  autocontenidos (no tocan la cámara ni el render HDRP).
//  Capa RUNTIME. Auto-arranque; sin montaje en escena.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public enum Sustancia { Ninguna, Porro, Speed, Chute, Tripi }

[DefaultExecutionOrder(80)]
public sealed class SistemaDrogas : MonoBehaviour
{
    public static Sustancia Activa { get; private set; } = Sustancia.Ninguna;
    // incluye el efecto de la borrachera (de SistemaAccionesPersonaje)
    public static float MultDispersion       => _multDisp * (1f + SistemaAccionesPersonaje.Borrachera / 80f) * SistemaAdiccion.FactorMono;
    public static float ReduccionDanoRecibido => _reduDano;
    public static float MultVelocidad        => _multVel;

    static float _multDisp = 1f, _reduDano = 1f, _multVel = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaDrogas");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaDrogas>();
    }

    bool _menu;
    float _tRestante;
    Canvas _canvas; Image _overlay;
    AudioSource _audio;

    void Awake() { Construir(); }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.uKey.wasPressedThisFrame) _menu = !_menu;
            else if (_menu)
            {
                if (kb.digit1Key.wasPressedThisFrame) Tomar(Sustancia.Porro);
                else if (kb.digit2Key.wasPressedThisFrame) Tomar(Sustancia.Speed);
                else if (kb.digit3Key.wasPressedThisFrame) Tomar(Sustancia.Chute);
                else if (kb.digit4Key.wasPressedThisFrame) Tomar(Sustancia.Tripi);
                else if (kb.escapeKey.wasPressedThisFrame) _menu = false;
            }
        }

        if (Activa != Sustancia.Ninguna)
        {
            _tRestante -= Time.deltaTime;
            AnimarOverlay();
            if (_tRestante <= 0f) Bajada();
        }
    }

    void Tomar(Sustancia s)
    {
        _menu = false;
        Activa = s;
        var ap = SistemaApoyoPopular.Instance;
        switch (s)
        {
            case Sustancia.Porro: _tRestante = 35f; _multDisp = 1.3f; _reduDano = 1f;   _multVel = 0.95f; ap?.RestarParanoia(15f); break;
            case Sustancia.Speed: _tRestante = 30f; _multDisp = 1.15f;_reduDano = 1f;   _multVel = 1.25f; ap?.SumarParanoia(10f); break;
            case Sustancia.Chute: _tRestante = 45f; _multDisp = 1.4f; _reduDano = 0.5f; _multVel = 0.7f;  ap?.RestarParanoia(25f); break;
            case Sustancia.Tripi: _tRestante = 60f; _multDisp = 1.9f; _reduDano = 1f;   _multVel = 0.85f; break;
        }
        _overlay.gameObject.SetActive(true);
        if (_audio != null) { _audio.pitch = s == Sustancia.Chute ? 0.6f : s == Sustancia.Speed ? 1.4f : 1f; _audio.volume = 0.18f; _audio.Play(); }
    }

    void Bajada()
    {
        // bajón: el speed deja paranoia; los demás, un poco de cansancio
        if (Activa == Sustancia.Speed) SistemaApoyoPopular.Instance?.SumarParanoia(18f);
        Activa = Sustancia.Ninguna;
        _multDisp = 1f; _reduDano = 1f; _multVel = 1f;
        _overlay.gameObject.SetActive(false);
        if (_audio != null) _audio.Stop();
    }

    void AnimarOverlay()
    {
        float t = Time.time;
        Color c; float a;
        switch (Activa)
        {
            case Sustancia.Porro: c = new Color(0.9f, 0.55f, 0.2f); a = 0.10f + Mathf.Sin(t * 1.5f) * 0.03f; break;
            case Sustancia.Speed: c = new Color(0.7f, 0.85f, 1f);   a = (Mathf.Sin(t * 12f) > 0.85f) ? 0.18f : 0.05f; break;
            case Sustancia.Chute: c = new Color(0.05f, 0.02f, 0.08f); a = 0.30f + Mathf.Sin(t * 0.8f) * 0.06f; break;
            case Sustancia.Tripi: Color.RGBToHSV(Color.red, out _, out _, out _); c = Color.HSVToRGB(Mathf.Repeat(t * 0.15f, 1f), 0.7f, 1f); a = 0.16f + Mathf.Sin(t * 2.3f) * 0.05f; break;
            default: c = Color.clear; a = 0f; break;
        }
        c.a = a;
        _overlay.color = c;
    }

    void Construir()
    {
        var go = new GameObject("Drogas_Canvas"); go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>(); _canvas.renderMode = RenderMode.ScreenSpaceOverlay; _canvas.sortingOrder = 4600;
        var ov = new GameObject("Overlay", typeof(RectTransform)); var rt=(RectTransform)ov.transform; rt.SetParent(go.transform,false);
        rt.anchorMin=Vector2.zero; rt.anchorMax=Vector2.one; rt.offsetMin=Vector2.zero; rt.offsetMax=Vector2.zero;
        _overlay = ov.AddComponent<Image>(); _overlay.raycastTarget = false; _overlay.color = Color.clear;
        ov.SetActive(false);

        _audio = gameObject.AddComponent<AudioSource>();
        _audio.loop = true; _audio.playOnAwake = false; _audio.spatialBlend = 0f;
        _audio.clip = GenerarHum();
    }

    static AudioClip GenerarHum()
    {
        int rate = 44100, len = rate; // 1 s
        var data = new float[len];
        for (int i = 0; i < len; i++)
            data[i] = Mathf.Sin(2f * Mathf.PI * 90f * i / rate) * 0.5f
                    + Mathf.Sin(2f * Mathf.PI * 45f * i / rate) * 0.3f;
        var clip = AudioClip.Create("hum", len, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    void OnGUI()
    {
        if (_menu)
        {
            var st = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
            st.normal.textColor = Color.white;
            GUI.Box(new Rect(Screen.width*0.5f-130, Screen.height*0.5f-80, 260, 150),
                "  Consumir (Esc cierra)\n\n  1  Porro\n  2  Speed\n  3  Chute\n  4  Tripi", st);
        }
        if (Activa != Sustancia.Ninguna)
        {
            var s2 = new GUIStyle(GUI.skin.label){ fontSize=13 }; s2.normal.textColor=new Color(0.8f,0.7f,0.9f);
            GUI.Label(new Rect(20, Screen.height-118, 220, 22), $"Colocado: {Activa} ({Mathf.CeilToInt(_tRestante)}s)", s2);
        }
    }
}
