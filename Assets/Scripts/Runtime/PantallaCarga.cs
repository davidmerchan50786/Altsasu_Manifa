// Assets/Scripts/Runtime/PantallaCarga.cs
// ═══════════════════════════════════════════════════════════════════════════
//  PANTALLA DE CARGA DE ARRANQUE
//
//  El mundo se genera en runtime (terreno DEM, splatmap, edificios OSM,
//  NavMesh, jugador...) y los primeros 10-30 s van a tirones. Esta pantalla
//  tapa ese proceso con fondo negro + barra de progreso y se desvanece sola
//  cuando los sistemas clave reportan listo (o al agotar el timeout).
//
//  Auto-arranca en Play (RuntimeInitializeOnLoadMethod) — no necesita estar
//  en la escena ni configuración. OnGUI puro: cero dependencias de Canvas/TMP.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;

public class PantallaCarga : MonoBehaviour
{
    const float TIMEOUT      = 35f;  // tope DURO: entra al juego pase lo que pase
    const float MIN_VISIBLE  = 2f;   // mínimo en pantalla (evita parpadeo)
    const float FADE_DUR     = 1.2f; // fundido de salida

    struct Paso
    {
        public string nombre;
        public System.Func<bool> listo;
        public float peso;
        public Paso(string n, System.Func<bool> l, float p) { nombre = n; listo = l; peso = p; }
    }

    readonly List<Paso> _pasos = new();
    float _t0, _progVisual, _alpha = 1f;
    bool  _completo;

    // Instrumentación de arranque: cuándo (s desde el boot) quedó listo cada paso.
    // Convierte el "tope de 35 s" en datos accionables: ves QUÉ paso tarda.
    float[] _tListo;
    bool    _logTotal;

    // PERF FIX: OnGUI corre 2×/frame (Layout+Repaint). Cachear strings para no
    // asignar basura cada pasada: el % solo se reconstruye al cambiar el entero,
    // y el texto del paso actual cuando cambia el paso no-listo.
    string _txtPct = "0%";
    int    _pctCache = -1;
    string _txtPaso = "";
    int    _pasoCache = -1;
    Texture2D _fondo; // cartel: Assets/Resources/UI/pantalla_carga.png (opcional)
    Texture2D _texNegro, _texBarra, _texBarraFondo;
    GUIStyle  _stTitulo, _stPaso, _stPct;
    bool _estilos;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<PantallaCarga>() != null) return;
        var go = new GameObject("PantallaCarga");
        DontDestroyOnLoad(go);
        go.AddComponent<PantallaCarga>();
    }

    void Awake()
    {
        _t0 = Time.realtimeSinceStartup;

        // Orden = orden visual de los mensajes. Pesos ≈ duración relativa real.
        _pasos.Add(new Paso("Generando terreno LIDAR",
            () => Terrain.activeTerrain != null, 0.20f));
        _pasos.Add(new Paso("Pintando biomas del valle",
            () => SistemaTerreno.Listo, 0.20f));
        _pasos.Add(new Paso("Levantando Altsasu (edificios OSM)",
            () => GeneradorMundoOSM.MundoListo, 0.25f));
        _pasos.Add(new Paso("Calculando rutas (NavMesh)",
            () => SistemaNavMesh.EstaListo, 0.15f));
        _pasos.Add(new Paso("Despertando al jugador",
            () => GameObject.FindGameObjectWithTag("Player") != null, 0.10f));
        _pasos.Add(new Paso("Últimos retoques",
            () => AltsasuCore.I != null, 0.10f));
        // Zona de spawn (streaming). Si no hay gestor de streaming, no aplica → listo al instante.
        _pasos.Add(new Paso("Preparando el entorno",
            () => ArranqueMundo.ZonaInicialListaONoAplica, 0.05f));

        _tListo = new float[_pasos.Count];
        for (int i = 0; i < _tListo.Length; i++) _tListo[i] = -1f;

        _texNegro      = Tex(new Color(0.04f, 0.05f, 0.06f));
        _texBarraFondo = Tex(new Color(0.15f, 0.16f, 0.18f));
        _texBarra      = Tex(new Color(0.95f, 0.75f, 0.10f)); // amarillo manifa

        // Cartel de portada (opcional): Assets/Resources/UI/pantalla_carga.png
        _fondo = Resources.Load<Texture2D>("UI/pantalla_carga");
    }

    static Texture2D Tex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c); t.Apply();
        return t;
    }

    void Update()
    {
        float trans = Time.realtimeSinceStartup - _t0;

        // Instrumentación: registrar y loguear el instante en que cada paso queda listo.
        for (int i = 0; i < _pasos.Count; i++)
            if (_tListo[i] < 0f && _pasos[i].listo())
            {
                _tListo[i] = trans;
                AlsasuaLogger.Info("Boot", $"[{trans:0.0}s] ✓ {_pasos[i].nombre}");
            }

        // Progreso real por pesos + objetivo
        float objetivo = 0f; bool todo = true;
        foreach (var p in _pasos)
        {
            if (p.listo()) objetivo += p.peso;
            else { todo = false; objetivo += p.peso * Mathf.Clamp01(trans / 45f) * 0.4f; }
        }
        objetivo = Mathf.Clamp01(objetivo);

        // La barra solo avanza (nunca retrocede) y se suaviza
        _progVisual = Mathf.Max(_progVisual,
            Mathf.Lerp(_progVisual, objetivo, Time.unscaledDeltaTime * 2.5f));

        // Robusto: con terreno (_pasos[0]) + jugador (_pasos[4]) ya es jugable;
        // biomas, edificios, NavMesh y árboles se siguen poblando EN SEGUNDO
        // PLANO tras quitar esta pantalla. Y tope DURO por si algo tarda — así
        // el arranque NUNCA se queda clavado esperando a un sistema lento.
        // Jugable = terreno + jugador + zona de spawn lista (esta última es no-op si
        // no hay streaming → no regresa el comportamiento actual sin Addressables).
        bool jugable = _pasos[0].listo() && _pasos[4].listo() && ArranqueMundo.ZonaInicialListaONoAplica;
        bool terminar = ((todo || jugable) && trans > MIN_VISIBLE) || trans > TIMEOUT;
        if (terminar) _completo = true;

        if (terminar && !_logTotal)
        {
            _logTotal = true;
            bool porTimeout = trans >= TIMEOUT && !(todo || jugable);
            AlsasuaLogger.Info("Boot",
                porTimeout
                    ? $"⚠ Pantalla de carga fuera por TIMEOUT a los {trans:0.0}s (terreno/jugador aún no listos)."
                    : $"Pantalla de carga fuera en {trans:0.0}s (terreno+jugador listos).");
        }

        if (_completo)
        {
            _progVisual = Mathf.Lerp(_progVisual, 1f, Time.unscaledDeltaTime * 6f);
            _alpha -= Time.unscaledDeltaTime / FADE_DUR;
            if (_alpha <= 0f) Destroy(gameObject);
        }

        // PERF FIX: precalcular las strings aquí (1×/frame) en vez de en OnGUI (2×/frame).
        int pct = Mathf.RoundToInt(_progVisual * 100f);
        if (pct != _pctCache) { _pctCache = pct; _txtPct = pct + "%"; }

        int pasoIdx = -1;  // primer paso no listo
        for (int i = 0; i < _pasos.Count; i++)
            if (!_pasos[i].listo()) { pasoIdx = i; break; }
        if (pasoIdx != _pasoCache)
        {
            _pasoCache = pasoIdx;
            _txtPaso = pasoIdx < 0 ? "Listo" : _pasos[pasoIdx].nombre + "…";
        }
    }

    void OnGUI()
    {
        if (_alpha <= 0f) return;
        InitEstilos();
        GUI.depth = -10000; // por encima de todo

        float w = Screen.width, h = Screen.height;
        var prev = GUI.color;

        // Fondo: cartel a pantalla completa (aspect-fill) o negro si no existe
        GUI.color = new Color(1f, 1f, 1f, _alpha);
        GUI.DrawTexture(new Rect(0, 0, w, h), _texNegro);
        if (_fondo != null)
        {
            float esc = Mathf.Max(w / _fondo.width, h / _fondo.height);
            float fw = _fondo.width * esc, fh = _fondo.height * esc;
            GUI.DrawTexture(new Rect((w - fw) / 2f, (h - fh) / 2f, fw, fh), _fondo);
            // Banda oscura inferior para que el texto y la barra se lean
            GUI.color = new Color(0f, 0f, 0f, 0.65f * _alpha);
            GUI.DrawTexture(new Rect(0, h * 0.86f, w, h * 0.14f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, _alpha);
        }

        // Título (solo si no hay cartel — el cartel ya trae el logo)
        if (_fondo == null)
            GUI.Label(new Rect(0, h * 0.32f, w, 60), "ALTSASU MANIFA", _stTitulo);

        // Con cartel: texto y barra en la banda inferior. Sin cartel: centrados.
        float yPaso  = _fondo != null ? h * 0.875f : h * 0.46f;
        float yBarra = _fondo != null ? h * 0.93f  : h * 0.52f;

        // Paso actual (string cacheada en Update — sin concat por frame)
        GUI.Label(new Rect(0, yPaso, w, 30), _txtPaso, _stPaso);

        // Barra
        float bw = Mathf.Min(560f, w * 0.6f), bh = 14f;
        float bx = (w - bw) / 2f, by = yBarra;
        GUI.DrawTexture(new Rect(bx, by, bw, bh), _texBarraFondo);
        GUI.DrawTexture(new Rect(bx, by, bw * _progVisual, bh), _texBarra);

        // Porcentaje (string cacheada en Update — sin interpolación/boxing por frame)
        GUI.Label(new Rect(0, by + 18, w, 24), _txtPct, _stPct);

        GUI.color = prev;
    }

    void InitEstilos()
    {
        if (_estilos) return; _estilos = true;
        _stTitulo = new GUIStyle(GUI.skin.label)
        { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
          normal = { textColor = new Color(0.95f, 0.93f, 0.88f) } };
        _stPaso = new GUIStyle(GUI.skin.label)
        { fontSize = 15, alignment = TextAnchor.MiddleCenter,
          normal = { textColor = new Color(0.75f, 0.76f, 0.78f) } };
        _stPct = new GUIStyle(_stPaso) { fontSize = 13 };
    }

    void OnDestroy()
    {
        Destroy(_texNegro); Destroy(_texBarra); Destroy(_texBarraFondo);
    }
}
