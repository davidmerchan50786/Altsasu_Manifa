// Assets/Scripts/SistemaRotulosZona.cs
// ═══════════════════════════════════════════════════════════════════════════
//  RÓTULOS DE ZONA — estilo GTA: al entrar a un barrio/zona aparece su nombre
//  con un subtítulo, fundido suave, abajo a la derecha. Autónomo y aislado.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-10)]
public class SistemaRotulosZona : MonoBehaviour
{
    struct Zona
    {
        public string Nombre, Subtitulo;
        public Vector3 Centro;
        public float Radio;
        public Zona(string n, string s, Vector3 c, float r) { Nombre = n; Subtitulo = s; Centro = c; Radio = r; }
    }

    static readonly Zona[] ZONAS =
    {
        new Zona("HERRIKO PLAZA",   "Altsasu · Centro",       GeoDataAlsasua.HerrikoPlaza,  140f),
        new Zona("BARRIO NORTE",    "Altsasu",                GeoDataAlsasua.BarrioNorte,   150f),
        new Zona("ESTACIÓN",        "Adif · Madrid–Irún",     GeoDataAlsasua.EstacionTren,  130f),
        new Zona("CUARTEL",         "Guardia Civil",          GeoDataAlsasua.CuartelGC,     110f),
        new Zona("AUTOVÍA A-1",     "N-1 · Madrid–Irún",      GeoDataAlsasua.CarreteraN1,   160f),
        new Zona("POLÍGONO ISASIA", "Zona industrial",        GeoDataAlsasua.PoligonoIsasia,180f),
        new Zona("SIERRA DE ARALAR","Monte",                  GeoDataAlsasua.MonteAralar,   500f),
    };

    Transform _jugador;
    Text   _txtNombre, _txtSub;
    CanvasGroup _grupo;
    int    _zonaActual = -1;
    float  _timerPoll;
    float  _alphaObjetivo;
    Coroutine _ocultar;

    void Start() => CrearUI();

    void CrearUI()
    {
        var canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            var cgo = new GameObject("Canvas_Rotulos");
            canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            cgo.AddComponent<CanvasScaler>();
            cgo.AddComponent<GraphicRaycaster>();
        }

        var panel = new GameObject("Rotulo_Zona");
        panel.transform.SetParent(canvas.transform, false);
        var rt = panel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 0); rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(1, 0);
        rt.anchoredPosition = new Vector2(-40, 40);
        rt.sizeDelta = new Vector2(520, 90);
        _grupo = panel.AddComponent<CanvasGroup>();
        _grupo.alpha = 0f;

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _txtNombre = CrearTexto(panel.transform, font, 40, FontStyle.Bold, TextAnchor.LowerRight,
                                new Color(1f, 0.92f, 0.5f), new Vector2(0, 22));
        _txtSub    = CrearTexto(panel.transform, font, 20, FontStyle.Italic, TextAnchor.LowerRight,
                                new Color(0.9f, 0.9f, 0.9f), new Vector2(0, 0));
    }

    static Text CrearTexto(Transform padre, Font font, int size, FontStyle style, TextAnchor anchor, Color color, Vector2 pos)
    {
        var go = new GameObject("Txt");
        go.transform.SetParent(padre, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(0, pos.y); rt.offsetMax = new Vector2(0, pos.y);
        var t = go.AddComponent<Text>();
        t.font = font; t.fontSize = size; t.fontStyle = style;
        t.alignment = anchor; t.color = color; t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        var sh = go.AddComponent<Shadow>();
        sh.effectColor = new Color(0, 0, 0, 0.7f); sh.effectDistance = new Vector2(2, -2);
        return t;
    }

    void Update()
    {
        if (_jugador == null)
        {
            _jugador = AltsasuCore.Jugador;
            if (_jugador == null && Camera.main != null) _jugador = Camera.main.transform;
            if (_jugador == null) return;
        }

        _timerPoll -= Time.deltaTime;
        if (_timerPoll <= 0f)
        {
            _timerPoll = 0.4f;
            int z = ZonaEn(_jugador.position);
            if (z != _zonaActual)
            {
                _zonaActual = z;
                if (z >= 0) MostrarRotulo(ZONAS[z]);
            }
        }

        if (_grupo != null)
            _grupo.alpha = Mathf.MoveTowards(_grupo.alpha, _alphaObjetivo, Time.deltaTime * 2f);
    }

    int ZonaEn(Vector3 pos)
    {
        int mejor = -1; float mejorDist = float.MaxValue;
        for (int i = 0; i < ZONAS.Length; i++)
        {
            float d = GeoDataAlsasua.Dist2D(pos, ZONAS[i].Centro);
            if (d < ZONAS[i].Radio && d < mejorDist) { mejorDist = d; mejor = i; }
        }
        return mejor;
    }

    void MostrarRotulo(Zona z)
    {
        if (_txtNombre == null) return;
        _txtNombre.text = z.Nombre;
        _txtSub.text    = z.Subtitulo;
        _alphaObjetivo  = 1f;
        if (_ocultar != null) StopCoroutine(_ocultar);
        _ocultar = StartCoroutine(OcultarTras(4.5f));
    }

    System.Collections.IEnumerator OcultarTras(float seg)
    {
        yield return new WaitForSeconds(seg);
        _alphaObjetivo = 0f;
    }
}
