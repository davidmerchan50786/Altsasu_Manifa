// Assets/Scripts/Runtime/SistemaAdiccion.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ADICCIÓN / MONO — la otra cara de las drogas y el alcohol (mecánica ficticia).
//
//  Consumir (SistemaDrogas / borrachera) sube la ADICCIÓN. Si llevas mucho sin
//  consumir y tu adicción es alta, entra el MONO:
//    · te tiemblan las manos → peor puntería (FactorMono, que lee SistemaDrogas),
//    · sube la paranoia a ratos,
//    · un velo gris parpadea en pantalla.
//  Volver a consumir lo calma. La adicción baja muy despacio con abstinencia.
//
//  Capa RUNTIME. Auto-arranque; sin montaje en escena.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(81)]
public sealed class SistemaAdiccion : MonoBehaviour
{
    public static float Adiccion { get; private set; }   // 0-100
    public static bool  EnMono   { get; private set; }
    /// <summary>Multiplicador de dispersión por el temblor del mono (1 normal, ↑ con el mono).</summary>
    public static float FactorMono => EnMono ? 1f + Adiccion / 200f : 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaAdiccion");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaAdiccion>();
    }

    Sustancia _ultima = Sustancia.Ninguna;
    float _ultBorrachera;
    float _tSinConsumir, _tParanoia;

    Canvas _canvas; Image _velo;

    void Awake() { Construir(); }

    void Update()
    {
        float dt = Time.deltaTime;

        // ── Detectar consumo ──────────────────────────────────────────────
        bool consumido = false;
        if (SistemaDrogas.Activa != Sustancia.Ninguna && _ultima == Sustancia.Ninguna) { Adiccion = Mathf.Min(100f, Adiccion + 8f); consumido = true; }
        _ultima = SistemaDrogas.Activa;
        if (SistemaAccionesPersonaje.Borrachera > _ultBorrachera + 1f) { Adiccion = Mathf.Min(100f, Adiccion + 2f); consumido = true; }
        _ultBorrachera = SistemaAccionesPersonaje.Borrachera;
        bool colocado = SistemaDrogas.Activa != Sustancia.Ninguna || SistemaAccionesPersonaje.Borrachera > 20f;

        if (consumido || colocado) _tSinConsumir = 0f;
        else _tSinConsumir += dt;

        // adicción baja muy despacio en abstinencia larga
        if (_tSinConsumir > 90f) Adiccion = Mathf.Max(0f, Adiccion - dt * 0.4f);

        // ── Mono ──────────────────────────────────────────────────────────
        EnMono = Adiccion > 40f && _tSinConsumir > 60f && !colocado;
        if (EnMono)
        {
            _tParanoia -= dt;
            if (_tParanoia <= 0f) { _tParanoia = 6f; SistemaApoyoPopular.Instance?.SumarParanoia(6f); }
            float a = 0.12f + Mathf.Abs(Mathf.Sin(Time.time * 9f)) * 0.06f;   // parpadeo nervioso
            _velo.color = new Color(0.4f, 0.4f, 0.42f, a);
            _velo.gameObject.SetActive(true);
        }
        else _velo.gameObject.SetActive(false);
    }

    void Construir()
    {
        var go = new GameObject("Adiccion_Canvas"); go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>(); _canvas.renderMode = RenderMode.ScreenSpaceOverlay; _canvas.sortingOrder = 4590;
        var ov = new GameObject("Velo", typeof(RectTransform)); var rt=(RectTransform)ov.transform; rt.SetParent(go.transform,false);
        rt.anchorMin=Vector2.zero; rt.anchorMax=Vector2.one; rt.offsetMin=Vector2.zero; rt.offsetMax=Vector2.zero;
        _velo = ov.AddComponent<Image>(); _velo.raycastTarget=false; _velo.color=Color.clear; ov.SetActive(false);
    }

    void OnGUI()
    {
        if (!EnMono) return;
        var s2 = new GUIStyle(GUI.skin.label){ fontSize=14, fontStyle=FontStyle.Italic };
        s2.normal.textColor = new Color(0.85f,0.6f,0.6f);
        GUI.Label(new Rect(Screen.width*0.5f-110, Screen.height-50, 240, 22), "Te tiemblan las manos…", s2);
    }
}
