// Assets/Scripts/Runtime/SistemaProgresion.cs
// ═══════════════════════════════════════════════════════════════════════════
//  PROGRESIÓN — el peso del movimiento crece con el APOYO POPULAR.
//
//  El nivel (0-5) sale del apoyo popular (cada 20 %). A más nivel:
//    · MultiplicadorIngresos → tus negocios extorsionados rinden más.
//    · DescuentoTienda        → compras más baratas.
//    · ReduccionCalor (≥ niv 3)→ extorsionar levanta menos búsqueda.
//
//  Otros sistemas (SistemaTienda, SistemaEconomiaCriminal) consultan estos
//  valores estáticos. Capa RUNTIME. Auto-arranque; sin montaje en escena.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;

[DefaultExecutionOrder(92)]
public sealed class SistemaProgresion : MonoBehaviour
{
    public static int   Nivel { get; private set; }
    public static float MultiplicadorIngresos => 1f + Nivel * 0.15f;
    public static float DescuentoTienda       => Mathf.Min(0.30f, Nivel * 0.06f);
    public static int   ReduccionCalor        => Nivel >= 3 ? 1 : 0;

    static readonly string[] TITULOS =
    { "Forastero", "Conocido", "De fiar", "Referente", "Líder del barrio", "El alma de Altsasu" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaProgresion");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaProgresion>();
    }

    float _tAviso; string _aviso;

    void OnEnable()  => SistemaApoyoPopular.OnApoyoCambia += OnApoyo;
    void OnDisable() => SistemaApoyoPopular.OnApoyoCambia -= OnApoyo;

    void OnApoyo(float apoyo)
    {
        int nuevo = Mathf.Clamp((int)(apoyo / 20f), 0, 5);
        if (nuevo == Nivel) return;
        bool sube = nuevo > Nivel;
        Nivel = nuevo;
        _aviso = (sube ? "Subes a nivel " : "Bajas a nivel ") + Nivel + " · " + TITULOS[Nivel];
        _tAviso = 3.5f;
    }

    void Update() { if (_tAviso > 0f) _tAviso -= Time.unscaledDeltaTime; }
    void OnGUI()
    {
        if (_tAviso <= 0f) return;
        var st = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
        st.normal.textColor = Color.white;
        GUI.Box(new Rect(Screen.width * 0.5f - 200, 80, 400, 34), _aviso, st);
    }
}
