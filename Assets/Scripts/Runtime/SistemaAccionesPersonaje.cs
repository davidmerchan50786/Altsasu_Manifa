// Assets/Scripts/Runtime/SistemaAccionesPersonaje.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ACCIONES DE PERSONAJE — emotes mundanos (humor de calle).
//
//  Tecla J abre el menú; eliges con 1-6:
//    1 Fumar   → calma (baja paranoia).
//    2 Beber   → calma más, pero sube la BORRACHERA (a tope, te tambaleas).
//    3 Escupir → cosmético.
//    4 Vomitar → baja la borrachera (alivio).
//    5 Mear    → alivio; en público, queda feo (baja un pelín el apoyo).
//    6 Cagar   → ídem.
//  Los actos "sucios" dejan una mancha en el suelo. Capa RUNTIME. Auto-arranque.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(80)]
public sealed class SistemaAccionesPersonaje : MonoBehaviour
{
    public static float Borrachera { get; private set; }   // 0-100

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaAccionesPersonaje");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaAccionesPersonaje>();
    }

    bool _menu;
    float _tAviso; string _aviso;

    void Update()
    {
        if (_tAviso > 0f) _tAviso -= Time.unscaledDeltaTime;
        if (Borrachera > 0f) Borrachera = Mathf.Max(0f, Borrachera - Time.deltaTime * 1.5f);   // se pasa sola

        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.jKey.wasPressedThisFrame) { _menu = !_menu; return; }
        if (!_menu) return;

        if (kb.digit1Key.wasPressedThisFrame) Hacer(0);
        else if (kb.digit2Key.wasPressedThisFrame) Hacer(1);
        else if (kb.digit3Key.wasPressedThisFrame) Hacer(2);
        else if (kb.digit4Key.wasPressedThisFrame) Hacer(3);
        else if (kb.digit5Key.wasPressedThisFrame) Hacer(4);
        else if (kb.digit6Key.wasPressedThisFrame) Hacer(5);
        else if (kb.escapeKey.wasPressedThisFrame) _menu = false;
    }

    void Hacer(int i)
    {
        _menu = false;
        var ap = SistemaApoyoPopular.Instance;
        switch (i)
        {
            case 0: ap?.RestarParanoia(8f);  Avisar("Te enciendes un cigarro"); break;
            case 1: ap?.RestarParanoia(12f); Borrachera = Mathf.Min(100f, Borrachera + 25f); Avisar("Le das un trago"); break;
            case 2: Avisar("Escupes al suelo"); Mancha(new Color(0.7f,0.7f,0.75f), 0.15f); break;
            case 3: Borrachera = Mathf.Max(0f, Borrachera - 50f); Avisar("Vomitas. Mejor fuera que dentro"); Mancha(new Color(0.5f,0.55f,0.2f), 0.4f); ap?.RestarApoyo(1f,"guarrada"); break;
            case 4: Avisar("Meas contra la pared"); Mancha(new Color(0.8f,0.7f,0.2f), 0.3f); ap?.RestarApoyo(1f,"guarrada"); break;
            case 5: Avisar("Cagas donde no debes"); Mancha(new Color(0.35f,0.22f,0.1f), 0.35f); ap?.RestarApoyo(2f,"guarrada"); break;
        }
    }

    void Mancha(Color c, float escala)
    {
        var jug = AltsasuCore.Jugador;
        Vector3 baseP = jug != null ? jug.position + jug.forward * 0.6f : transform.position;
        if (Physics.Raycast(baseP + Vector3.up * 1f, Vector3.down, out var hit, 3f)) baseP = hit.point;

        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Mancha";
        Destroy(go.GetComponent<Collider>());
        go.transform.position = baseP + Vector3.up * 0.02f;
        go.transform.localScale = new Vector3(escala, 0.01f, escala);
        UtilMaterial.Tenir(go.GetComponent<MeshRenderer>(), c);
        Destroy(go, 30f);
    }

    void Avisar(string s) { _aviso = s; _tAviso = 2.5f; }

    void OnGUI()
    {
        if (_menu)
        {
            var st = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
            st.normal.textColor = Color.white;
            GUI.Box(new Rect(Screen.width * 0.5f - 130, Screen.height * 0.5f - 90, 260, 178),
                "  Acciones (Esc cierra)\n\n  1  Fumar\n  2  Beber\n  3  Escupir\n  4  Vomitar\n  5  Mear\n  6  Cagar", st);
        }
        if (Borrachera > 40f)
        {
            var s2 = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            s2.normal.textColor = new Color(0.9f, 0.7f, 0.3f);
            GUI.Label(new Rect(20, Screen.height - 90, 200, 22), $"Borrachera: {Mathf.RoundToInt(Borrachera)}%", s2);
        }
        if (_tAviso > 0f)
        {
            var st = new GUIStyle(GUI.skin.box) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            st.normal.textColor = Color.white;
            GUI.Box(new Rect(Screen.width * 0.5f - 180, 230, 360, 30), _aviso, st);
        }
    }
}
