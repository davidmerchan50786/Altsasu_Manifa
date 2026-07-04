// Assets/Scripts/HUDManifestacion.cs
// ═══════════════════════════════════════════════════════════════════════════
//  HUD de la Manifestación — barra de apoyo popular, objetivo, salud
//  Creado en runtime con IMGUI (sin Canvas prefab requerido).
//  Se activa automáticamente cuando JuegoManifestacion arranca.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[DefaultExecutionOrder(50)]
public class HUDManifestacion : MonoBehaviour
{
    public static HUDManifestacion Instance { get; private set; }

    // ── Estado público ─────────────────────────────────────────────────────
    [Range(0f, 100f)] public float apoyoPopular   = 0f;    // 0-100%
    [Range(0f, 100f)] public float saludJugador   = 100f;  // 0-100
    public string    objetivo      = "Llega a la plaza con apoyo popular";
    public string    faseActual    = "Concentración";
    public bool      visible       = false;

    // ── Estilo IMGUI ────────────────────────────────────────────────────────
    GUIStyle _estiloFondo, _estiloTexto, _estiloObjetivo, _estiloFase;
    Texture2D _texFondo, _texApoyo, _texSalud, _texGuardia;
    bool _estilosCreados;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start() { }   // estilos se crean en el primer OnGUI (requieren contexto GUI)

    // ── Actualizar desde JuegoManifestacion ────────────────────────────────

    public void SetApoyo(float valor)         => apoyoPopular = Mathf.Clamp(valor, 0f, 100f);
    public void SetSalud(float valor)         => saludJugador = Mathf.Clamp(valor, 0f, 100f);
    public void SetObjetivo(string texto)     => objetivo     = texto;
    public void SetFase(string fase)          => faseActual   = fase;
    public void Mostrar(bool mostrar)         => visible      = mostrar;

    // ── IMGUI ───────────────────────────────────────────────────────────────

    void OnGUI()
    {
        if (!visible) return;
        if (!ArranqueMundo.BaselineListo) return;
        if (!_estilosCreados) CrearEstilos();

        float sw = Screen.width, sh = Screen.height;

        // Panel izquierdo (apoyo popular + objetivo)
        float panW = 320f, panH = 120f;
        GUI.Box(new Rect(20, 20, panW, panH), GUIContent.none, _estiloFondo);

        // — Apoyo popular
        GUI.Label(new Rect(30, 28, 200, 22), $"APOYO POPULAR  {apoyoPopular:F0}%", _estiloTexto);
        float barW = panW - 20f;
        GUI.DrawTexture(new Rect(30, 52, barW, 16), _texFondo);
        Color apoyoColor = apoyoPopular >= 50f ? new Color(0.1f,0.9f,0.3f)
                         : apoyoPopular >= 25f ? new Color(0.9f,0.8f,0.1f)
                                               : new Color(0.9f,0.2f,0.1f);
        DrawBar(new Rect(30, 52, barW * (apoyoPopular / 100f), 16), apoyoColor);

        // — Fase
        GUI.Label(new Rect(30, 74, 290, 20), $"⚡ {faseActual}", _estiloFase);

        // — Objetivo
        GUI.Label(new Rect(30, 96, 290, 20), objetivo, _estiloObjetivo);

        // Panel derecho — salud
        float phx = sw - 200f, phy = 20f, phw = 180f, phh = 60f;
        GUI.Box(new Rect(phx, phy, phw, phh), GUIContent.none, _estiloFondo);
        GUI.Label(new Rect(phx + 10, phy + 6, 160, 22), $"SALUD  {saludJugador:F0}", _estiloTexto);
        DrawBar(new Rect(phx + 10, phy + 30, (phw - 20f) * (saludJugador / 100f), 14),
            saludJugador > 50f ? new Color(0.2f, 0.9f, 0.2f) : new Color(0.9f, 0.3f, 0.1f));

        // — Teclas (parte inferior)
        GUI.Label(new Rect(20, sh - 36, 500, 28),
            "[ M ] Manifestación   [ WASD ] Mover   [ Espacio ] Acción",
            _estiloObjetivo);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    void DrawBar(Rect r, Color color)
    {
        var prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(r, _texApoyo);
        GUI.color = prev;
    }

    void CrearEstilos()
    {
        _texFondo   = CrearTex(new Color(0f, 0f, 0f, 0.65f));
        _texApoyo   = CrearTex(Color.white);
        _texSalud   = CrearTex(new Color(0.2f, 0.9f, 0.2f));
        _texGuardia = CrearTex(new Color(0.9f, 0.2f, 0.1f));

        _estiloFondo = new GUIStyle(GUI.skin.box)
        {
            normal  = { background = _texFondo },
            border  = new RectOffset(0, 0, 0, 0),
            padding = new RectOffset(0, 0, 0, 0)
        };

        _estiloTexto = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 13,
            fontStyle = FontStyle.Bold,
            normal    = { textColor = Color.white }
        };

        _estiloFase = new GUIStyle(_estiloTexto)
        {
            fontSize = 11,
            normal   = { textColor = new Color(1f, 0.85f, 0.2f) }
        };

        _estiloObjetivo = new GUIStyle(_estiloTexto)
        {
            fontSize = 10,
            normal   = { textColor = new Color(0.8f, 0.8f, 0.8f) }
        };

        _estilosCreados = true;
    }

    static Texture2D CrearTex(Color color)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, color);
        t.Apply();
        return t;
    }
}
