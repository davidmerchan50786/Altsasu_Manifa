// Assets/Scripts/Runtime/SistemaEventosMundo.cs
// ═══════════════════════════════════════════════════════════════════════════
//  EVENTOS DE MUNDO ALEATORIOS — textura de un valle industrial navarro.
//
//  Cada cierto tiempo lanza un evento ambiental con un TITULAR en pantalla y un
//  efecto leve (apoyo popular, o ventaja de sigilo temporal). Complementa al
//  DirectorMundo (que ya gestiona control/redada/disturbio/mercado).
//
//  FICCIÓN Y NEUTRALIDAD: son sucesos genéricos de época/lugar (huelgas, fiestas,
//  apagones, niebla, mercado, despidos), NO recreaciones de hechos violentos
//  reales ni contenido partidista.
//
//  Capa RUNTIME. Auto-arranque; sin montaje en escena.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;

[DefaultExecutionOrder(90)]
public sealed class SistemaEventosMundo : MonoBehaviour
{
    /// <summary>Ventaja de sigilo temporal (apagón/niebla): &lt;1 reduce la visión policial.</summary>
    public static float FactorEventoSigilo => Time.time < _sigiloHasta ? 0.7f : 1f;
    static float _sigiloHasta;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaEventosMundo");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaEventosMundo>();
    }

    struct Evento { public string titular; public float apoyo; public bool sigilo;
        public Evento(string t, float a, bool s = false) { titular = t; apoyo = a; sigilo = s; } }

    static readonly Evento[] CATALOGO =
    {
        new Evento("Huelga en la fábrica: los obreros paran la cadena.", 3f),
        new Evento("Concentración vecinal en la Herriko Plaza.",          2f),
        new Evento("Empiezan las fiestas del pueblo.",                    2f),
        new Evento("Día de mercado en la plaza.",                         1f),
        new Evento("Cierre de un taller: nuevos despidos en el valle.",   1f),
        new Evento("Funeral multitudinario por un vecino.",               2f),
        new Evento("Apagón en el casco viejo.",                           0f, true),
        new Evento("Niebla cerrada del Arakil.",                          0f, true),
        new Evento("Corte de luz y teléfono en el barrio.",              0f, true),
        new Evento("Control de carretera a la entrada del pueblo.",      0f),
    };

    const float MIN_INTERVALO = 180f, MAX_INTERVALO = 360f;
    float _t;
    float _tTitular; string _titular;

    void Start() { _t = Random.Range(MIN_INTERVALO, MAX_INTERVALO); }

    void Update()
    {
        if (_tTitular > 0f) _tTitular -= Time.unscaledDeltaTime;
        _t -= Time.deltaTime;
        if (_t > 0f) return;
        _t = Random.Range(MIN_INTERVALO, MAX_INTERVALO);
        Lanzar(CATALOGO[Random.Range(0, CATALOGO.Length)]);
    }

    void Lanzar(Evento e)
    {
        if (e.apoyo != 0f) SistemaApoyoPopular.Instance?.SumarApoyo(e.apoyo, "evento de mundo");
        if (e.sigilo) _sigiloHasta = Time.time + 75f;
        _titular = e.titular; _tTitular = 7f;
    }

    void OnGUI()
    {
        if (_tTitular <= 0f) return;
        var fondo = new GUIStyle(GUI.skin.box);
        fondo.normal.background = Texture2D.grayTexture;
        var st = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic };
        st.normal.textColor = Color.white;
        var r = new Rect(Screen.width * 0.5f - 360, 16, 720, 34);
        GUI.color = new Color(0, 0, 0, 0.7f); GUI.Box(r, GUIContent.none); GUI.color = Color.white;
        GUI.Label(r, "◆  " + _titular, st);
    }
}
