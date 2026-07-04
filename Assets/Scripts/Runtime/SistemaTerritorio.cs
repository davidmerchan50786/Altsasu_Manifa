// Assets/Scripts/Runtime/SistemaTerritorio.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONTROL DE TERRITORIO — barrios de Altsasu que dominas según negocios y apoyo.
//
//  Divide la zona en barrios. El CONTROL de cada uno sale de los negocios que
//  extorsionas allí + tu apoyo popular. En el barrio donde estás, si lo controlas
//  (> 50 %):
//    · el vecindario te CUBRE → la policía te ve menos (FactorZona < 1),
//    · recibes un goteo de apoyo popular.
//  HUD: nombre del barrio y % de control.
//
//  Centros aproximados (offset de Herriko Plaza); ajústalos a tu mapa.
//  Capa RUNTIME. Auto-arranque; sin montaje en escena.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;

[DefaultExecutionOrder(91)]
public sealed class SistemaTerritorio : MonoBehaviour
{
    /// <summary>Factor de visión policial en el barrio actual (1 normal, &lt;1 si lo controlas).</summary>
    public static float FactorZona { get; private set; } = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaTerritorio");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaTerritorio>();
    }

    struct Zona { public string nombre; public Vector2 centro; public float radio; public float control;
        public Zona(string n, float x, float z, float r) { nombre = n; centro = new Vector2(x, z); radio = r; control = 0f; } }

    Zona[] _zonas;
    int _actual = -1;
    float _t;

    static readonly System.Collections.Generic.List<Vector3> _pintadas = new();
    /// <summary>Registra una pintada (PintadaTerritorio) que aumenta el control del barrio.</summary>
    public static void RegistrarPintada(Vector3 p) => _pintadas.Add(p);

    void Start()
    {
        float ox = GeoDataAlsasua.OX, oz = GeoDataAlsasua.OZ;
        _zonas = new[]
        {
            new Zona("Herriko Plaza",       ox,        oz,        250f),
            new Zona("Casco Viejo",         ox - 300f, oz + 200f, 280f),
            new Zona("Polígono industrial", ox + 700f, oz - 300f, 360f),
            new Zona("El Ensanche",         ox + 200f, oz - 500f, 300f),
            new Zona("La Vega",             ox - 500f, oz - 200f, 320f),
        };
    }

    void Update()
    {
        _t -= Time.deltaTime;
        if (_t > 0f) return;
        _t = 2f;

        Recalcular();

        var jug = AltsasuCore.Jugador;
        _actual = jug != null ? ZonaDe(jug.position) : -1;

        FactorZona = 1f;
        if (_actual >= 0 && _zonas[_actual].control > 0.5f)
        {
            FactorZona = 0.85f;
            SistemaApoyoPopular.Instance?.SumarApoyo(0.2f, "territorio");   // goteo
        }
    }

    void Recalcular()
    {
        var negocios = FindObjectsOfType<Negocio>();
        float apoyo01 = (SistemaApoyoPopular.Instance != null ? SistemaApoyoPopular.Instance.apoyo : 50f) / 100f;

        for (int i = 0; i < _zonas.Length; i++)
        {
            int total = 0, controlados = 0;
            foreach (var n in negocios)
            {
                if (n == null) continue;
                Vector2 p = new Vector2(n.transform.position.x, n.transform.position.z);
                if (Vector2.Distance(p, _zonas[i].centro) > _zonas[i].radio) continue;
                total++;
                if (n.estado == Negocio.Estado.Extorsionado) controlados++;
            }
            float porNegocios = total > 0 ? (float)controlados / total : 0f;
            // sin negocios, el apoyo da control parcial; con negocios, mezcla 70/30
            float ctrl = total > 0 ? Mathf.Lerp(apoyo01 * 0.5f, porNegocios, 0.7f) : apoyo01 * 0.4f;
            int pintadas = 0;
            foreach (var pp in _pintadas)
                if (Vector2.Distance(new Vector2(pp.x, pp.z), _zonas[i].centro) <= _zonas[i].radio) pintadas++;
            _zonas[i].control = Mathf.Clamp01(ctrl + Mathf.Min(0.3f, pintadas * 0.05f));
        }
    }

    int ZonaDe(Vector3 pos)
    {
        Vector2 p = new Vector2(pos.x, pos.z);
        int mejor = -1; float min = float.MaxValue;
        for (int i = 0; i < _zonas.Length; i++)
        {
            float d = Vector2.Distance(p, _zonas[i].centro);
            if (d <= _zonas[i].radio && d < min) { min = d; mejor = i; }
        }
        return mejor;
    }

    void OnGUI()
    {
        if (_actual < 0) return;
        var z = _zonas[_actual];
        var st = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
        st.normal.textColor = z.control > 0.5f ? new Color(0.6f, 0.9f, 0.6f) : Color.white;
        GUI.Box(new Rect(20, 20, 270, 28), $"  {z.nombre} — control {Mathf.RoundToInt(z.control * 100)}%", st);
    }
}
