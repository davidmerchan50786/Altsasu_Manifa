// Assets/Scripts/Runtime/SistemaDisfraz.cs
// ═══════════════════════════════════════════════════════════════════════════
//  DISFRAZ / ENCUBIERTO — pasar desapercibido entre la gente.
//
//    · Tecla H → ponerte/quitarte el disfraz (capucha, ropa de paisano…).
//      Encubierto: la policía te RECONOCE menos (reduce su alcance de visión)
//      → puedes acercarte y perder una persecución.
//    · Se te CAE el disfraz si disparas (te delatas) — y queda un breve
//      enfriamiento antes de poder volver a encubrirte.
//
//  Lo consulta PoliciaForalIA (FactorReconocimiento). Capa RUNTIME.
//  Auto-arranque; sin montaje en escena.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(90)]
public sealed class SistemaDisfraz : MonoBehaviour
{
    public static bool  Encubierto { get; private set; }
    /// <summary>Multiplicador de reconocimiento (alcance de visión policial). 1 normal, &lt;1 encubierto.</summary>
    public static float FactorReconocimiento => Encubierto ? 0.4f : 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaDisfraz");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaDisfraz>();
    }

    const float ENFRIAMIENTO = 8f;
    float _cooldown;
    float _tAviso; string _aviso;

    void OnEnable()  => SistemaArmasExtendido.AlDisparar += Delatar;
    void OnDisable() => SistemaArmasExtendido.AlDisparar -= Delatar;

    void Delatar(Vector3 _)
    {
        if (!Encubierto) return;
        Encubierto = false;
        _cooldown = ENFRIAMIENTO;
        Avisar("Disparas: se te cae el disfraz");
    }

    void Update()
    {
        if (_cooldown > 0f) _cooldown -= Time.deltaTime;
        if (_tAviso > 0f) _tAviso -= Time.unscaledDeltaTime;

        var kb = Keyboard.current;
        if (kb == null || !kb.hKey.wasPressedThisFrame) return;

        if (Encubierto) { Encubierto = false; Avisar("Te quitas el disfraz"); }
        else if (_cooldown > 0f) Avisar($"Aún te recuerdan ({Mathf.CeilToInt(_cooldown)} s)");
        else { Encubierto = true; Avisar("Encubierto: pasas desapercibido"); }
    }

    void Avisar(string s) { _aviso = s; _tAviso = 2.5f; }
    void OnGUI()
    {
        if (Encubierto)
        {
            var s2 = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            s2.normal.textColor = new Color(0.6f, 0.9f, 0.7f);
            GUI.Label(new Rect(20, Screen.height - 60, 200, 24), "● Encubierto", s2);
        }
        if (_tAviso <= 0f) return;
        var st = new GUIStyle(GUI.skin.box) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
        st.normal.textColor = Color.white;
        GUI.Box(new Rect(Screen.width * 0.5f - 200, 160, 400, 32), _aviso, st);
    }
}
