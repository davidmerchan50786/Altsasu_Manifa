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
    const float TIMEOUT      = 90f;  // nunca bloquear más de esto
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

        bool terminar = (todo && trans > MIN_VISIBLE) || trans > TIMEOUT;
        if (terminar) _completo = true;

        if (_completo)
        {
            _progVisual = Mathf.Lerp(_progVisual, 1f, Time.unscaledDeltaTime * 6f);
            _alpha -= Time.unscaledDeltaTime / FADE_DUR;
            if (_alpha <= 0f) Destroy(gameObject);
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

        // Paso actual (el primero no listo)
        string paso = "Listo";
        foreach (var p in _pasos)
            if (!p.listo()) { paso = p.nombre + "…"; break; }
        GUI.Label(new Rect(0, yPaso, w, 30), paso, _stPaso);

        // Barra
        float bw = Mathf.Min(560f, w * 0.6f), bh = 14f;
        float bx = (w - bw) / 2f, by = yBarra;
        GUI.DrawTexture(new Rect(bx, by, bw, bh), _texBarraFondo);
        GUI.DrawTexture(new Rect(bx, by, bw * _progVisual, bh), _texBarra);

        // Porcentaje
        GUI.Label(new Rect(0, by + 18, w, 24), $"{_progVisual * 100f:F0}%", _stPct);

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
