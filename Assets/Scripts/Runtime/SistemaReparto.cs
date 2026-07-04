// Assets/Scripts/Runtime/SistemaReparto.cs
// ═══════════════════════════════════════════════════════════════════════════
//  REPARTO / CONTRABANDO CON RUTAS — lleva un paquete de A a B a tiempo.
//
//  PuntoReparto (IInteractable) inicia el encargo: marca un destino, un tiempo y
//  una recompensa. Mientras LLEVAS el paquete, cada poco hay riesgo de que te
//  marquen (sube algo la búsqueda) — el contrabando quema. Llegas al destino a
//  tiempo → dinero + apoyo. Se acaba el tiempo → fallas.
//
//  HUD: flecha/distancia al destino y cuenta atrás. Capa RUNTIME. Auto-arranque.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;

[DefaultExecutionOrder(95)]
public sealed class SistemaReparto : MonoBehaviour
{
    public static SistemaReparto I { get; private set; }
    public static bool Activo { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaReparto");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaReparto>();
    }

    void Awake() { if (I != null) { Destroy(gameObject); return; } I = this; }

    Vector3 _destino; float _tiempo; int _recompensa; float _apoyo;
    float _tHeat; string _aviso; float _tAviso;
    Camera _cam;

    public bool Iniciar(Vector3 destino, float segundos, int recompensa, float apoyo = 5f)
    {
        if (Activo) { Avisar("Ya llevas un reparto"); return false; }
        _destino = destino; _tiempo = segundos; _recompensa = recompensa; _apoyo = apoyo;
        Activo = true; _tHeat = 10f;
        Avisar("Reparto iniciado: lleva el paquete a su destino");
        return true;
    }

    void Update()
    {
        if (_tAviso > 0f) _tAviso -= Time.unscaledDeltaTime;
        if (!Activo) return;

        _tiempo -= Time.deltaTime;
        if (_tiempo <= 0f) { Fallar(); return; }

        var jug = AltsasuCore.Jugador;
        if (jug == null) return;

        if (Vector3.Distance(jug.position, _destino) < 5f) { Entregar(); return; }

        // el contrabando quema: riesgo periódico de que te marquen
        _tHeat -= Time.deltaTime;
        if (_tHeat <= 0f) { _tHeat = 12f; if (Random.value < 0.4f) ServiceLocator.Get<IWantedSystem>()?.AumentarBusqueda(1); }
    }

    void Entregar()
    {
        ServiceLocator.Get<IEconomyService>()?.GanarDinero(_recompensa);
        SistemaApoyoPopular.Instance?.SumarApoyo(_apoyo, "reparto");
        Activo = false; Avisar($"Entregado: +{_recompensa} €");
    }
    void Fallar() { Activo = false; Avisar("Se acabó el tiempo: reparto fallido"); }

    void Avisar(string s) { _aviso = s; _tAviso = 3f; }

    void OnGUI()
    {
        if (Activo)
        {
            if (_cam == null) _cam = Camera.main;
            var jug = AltsasuCore.Jugador;
            float dist = (jug != null) ? Vector3.Distance(jug.position, _destino) : 0f;
            var st = new GUIStyle(GUI.skin.box) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            st.normal.textColor = Color.white;
            GUI.Box(new Rect(Screen.width * 0.5f - 170, 54, 340, 30),
                $"Reparto · {Mathf.RoundToInt(dist)} m · {Mathf.CeilToInt(_tiempo)} s", st);

            if (_cam != null)
            {
                Vector3 sp = _cam.WorldToScreenPoint(_destino + Vector3.up * 2f);
                if (sp.z > 0f)
                {
                    var s2 = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
                    s2.normal.textColor = new Color(1f, 0.85f, 0.3f);
                    GUI.Label(new Rect(sp.x - 20, Screen.height - sp.y - 20, 40, 40), "▼", s2);
                }
            }
        }
        if (_tAviso > 0f)
        {
            var st = new GUIStyle(GUI.skin.box) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            st.normal.textColor = Color.white;
            GUI.Box(new Rect(Screen.width * 0.5f - 200, 200, 400, 30), _aviso, st);
        }
    }
}
