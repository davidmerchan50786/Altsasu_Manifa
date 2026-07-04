// Assets/Scripts/MenuPausa.cs
// ═══════════════════════════════════════════════════════════════════════════
//  Menú de pausa — ESC durante el juego.
//  OnGUI puro, sin dependencias de Canvas ni TMPro.
//  Tabs: Continuar · Opciones · Controles · Salir
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

[DefaultExecutionOrder(100)]
public class MenuPausa : SingletonMono<MenuPausa>
{
    protected override bool DestroyGameObjectOnDuplicate => true;
    public static bool       Activo  { get; private set; }

    // ── Estado ────────────────────────────────────────────────────────────
    enum Tab { Principal, Opciones, Controles }
    Tab   _tab        = Tab.Principal;
    bool  _reasignando = false;
    string _accionReasignar = "";

    // Scroll en controles
    Vector2 _scrollControles;
    Vector2 _scrollOpciones;

    // ── Estilos (inicializados lazy) ──────────────────────────────────────
    GUIStyle _sVentana, _sTitulo, _sBoton, _sBotonTab, _sLabel, _sSliderLabel;
    bool _estilosInit;

    // ── Escena de menú principal ──────────────────────────────────────────
    const string ESCENA_MENU = "MenuPrincipal";

    // ─────────────────────────────────────────────────────────────────────

    void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
            TogglePausa();
    }

    public static void TogglePausa()
    {
        Activo = !Activo;
        Time.timeScale                    = Activo ? 0f : 1f;
        Cursor.lockState                  = Activo ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible                    = Activo;
        if (Instance != null) Instance._tab = Tab.Principal;
    }

    public static void Abrir()  { if (!Activo) TogglePausa(); }
    public static void Cerrar() { if (Activo)  TogglePausa(); }

    // ─────────────────────────────────────────────────────────────────────
    //  GUI
    // ─────────────────────────────────────────────────────────────────────

    void OnGUI()
    {
        if (!Activo) return;
        // Garantizar cursor libre en cada frame de GUI — ControladorJugador puede
        // intentar re-bloquearlo; aquí forzamos el estado correcto para el menú.
        if (Cursor.lockState != CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }
        InicializarEstilos();

        float w = 480f, h = 520f;
        float x = (Screen.width  - w) / 2f;
        float y = (Screen.height - h) / 2f;

        // Fondo oscuro
        GUI.color = new Color(0, 0, 0, 0.72f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(x, y, w, h), _sVentana);
        GUILayout.Space(12);

        // Título
        GUILayout.Label("⏸  PAUSA", _sTitulo);
        GUILayout.Space(8);

        // Tabs
        GUILayout.BeginHorizontal();
        if (TabBtn("▶ Continuar",  _tab == Tab.Principal)) _tab = Tab.Principal;
        if (TabBtn("⚙ Opciones",   _tab == Tab.Opciones))  _tab = Tab.Opciones;
        if (TabBtn("🎮 Controles", _tab == Tab.Controles)) _tab = Tab.Controles;
        GUILayout.EndHorizontal();
        GUILayout.Space(10);

        // Contenido del tab activo
        switch (_tab)
        {
            case Tab.Principal:  DibujarPrincipal(); break;
            case Tab.Opciones:   DibujarOpciones();  break;
            case Tab.Controles:  DibujarControles(); break;
        }

        GUILayout.EndArea();
    }

    // ── Tab Principal ─────────────────────────────────────────────────────

    void DibujarPrincipal()
    {
        GUILayout.Space(30);

        if (Boton("▶  Continuar"))        Cerrar();
        GUILayout.Space(6);
        if (Boton("🔄  Reiniciar zona"))
        {
            Cerrar();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
        GUILayout.Space(6);
        if (Boton("🏠  Menú Principal"))
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(ESCENA_MENU);
        }
        GUILayout.Space(6);
        if (Boton("❌  Salir del juego"))
        {
            SistemaOpciones.Guardar();
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }

    // ── Tab Opciones ──────────────────────────────────────────────────────

    void DibujarOpciones()
    {
        _scrollOpciones = GUILayout.BeginScrollView(_scrollOpciones, GUILayout.Height(380));

        Seccion("AUDIO");
        SliderOpcion("Master",    SistemaOpciones.VolMaster,   v => SistemaOpciones.VolMaster   = v);
        SliderOpcion("Música",    SistemaOpciones.VolMusica,   v => SistemaOpciones.VolMusica   = v);
        SliderOpcion("Efectos",   SistemaOpciones.VolSFX,      v => SistemaOpciones.VolSFX      = v);
        SliderOpcion("Ambiente",  SistemaOpciones.VolAmbiente, v => SistemaOpciones.VolAmbiente = v);

        Seccion("GRÁFICOS");
        SelectorOpcion("Calidad",     SistemaOpciones.NIVELES_CALIDAD, SistemaOpciones.Calidad,
            i => SistemaOpciones.Calidad = i);
        SelectorOpcion("Resolución",  OpcionesResolucion(),            SistemaOpciones.Resolucion,
            i => SistemaOpciones.Resolucion = i);
        ToggleOpcion("Pantalla completa", SistemaOpciones.Fullscreen,  v => SistemaOpciones.Fullscreen = v);
        ToggleOpcion("VSync",             SistemaOpciones.VSync,       v => SistemaOpciones.VSync      = v);
        SliderOpcion("Campo de visión (FOV)", SistemaOpciones.FOV, v => SistemaOpciones.FOV = v, 50f, 110f);

        Seccion("JUEGO");
        SliderOpcion("Sensibilidad ratón", SistemaOpciones.SensRaton, v => SistemaOpciones.SensRaton = v, 0.5f, 8f);
        ToggleOpcion("Invertir Y",         SistemaOpciones.InvertirY,  v => SistemaOpciones.InvertirY = v);
        ToggleOpcion("Subtítulos",         SistemaOpciones.Subtitulos, v => SistemaOpciones.Subtitulos = v);
        SelectorOpcion("Idioma",           SistemaOpciones.IDIOMAS,    SistemaOpciones.Idioma,
            i => SistemaOpciones.Idioma = i);

        GUILayout.EndScrollView();

        GUILayout.Space(4);
        if (Boton("💾  Guardar cambios")) SistemaOpciones.Guardar();
    }

    // ── Tab Controles ─────────────────────────────────────────────────────

    void DibujarControles()
    {
        _scrollControles = GUILayout.BeginScrollView(_scrollControles, GUILayout.Height(360));

        foreach (var kv in SistemaOpciones.AccionesDefault)
        {
            string accion = kv.Key;
            string tecla  = SistemaOpciones.GetControl(accion);

            GUILayout.BeginHorizontal();
            GUILayout.Label(accion, _sLabel, GUILayout.Width(200));

            bool reasignandoEsta = _reasignando && _accionReasignar == accion;
            string btnTxt = reasignandoEsta ? "[ Pulsa una tecla... ]" : tecla.ToUpper();
            GUI.color = reasignandoEsta ? new Color(1f, 0.9f, 0.3f) : Color.white;

            if (GUILayout.Button(btnTxt, _sBoton, GUILayout.Width(180)))
            {
                _reasignando     = !_reasignando || _accionReasignar != accion;
                _accionReasignar = accion;
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.Space(2);
        }

        GUILayout.EndScrollView();
        GUILayout.Space(6);

        GUILayout.BeginHorizontal();
        if (Boton("↩  Restablecer todo")) { SistemaOpciones.ResetControles(); _reasignando = false; }
        GUILayout.EndHorizontal();

        // Captura de tecla
        if (_reasignando && Event.current.type == EventType.KeyDown)
        {
            var key = Event.current.keyCode;
            if (key != KeyCode.Escape)
            {
                SistemaOpciones.SetControl(_accionReasignar, key.ToString().ToLower());
                _reasignando = false;
            }
            else _reasignando = false; // ESC cancela
        }
    }

    // ── Helpers de UI ─────────────────────────────────────────────────────

    bool TabBtn(string label, bool activo)
    {
        GUI.color = activo ? new Color(0.3f, 0.6f, 1f) : Color.white;
        bool r = GUILayout.Button(label, _sBotonTab);
        GUI.color = Color.white;
        return r;
    }

    bool Boton(string label)
        => GUILayout.Button(label, _sBoton, GUILayout.Height(38));

    void Seccion(string titulo)
    {
        GUILayout.Space(8);
        GUI.color = new Color(0.7f, 0.85f, 1f);
        GUILayout.Label(titulo, _sSliderLabel);
        GUI.color = Color.white;
        GUILayout.Space(2);
    }

    void SliderOpcion(string label, float valor, System.Action<float> setter,
                      float min = 0f, float max = 1f)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _sLabel, GUILayout.Width(200));
        float nuevo = GUILayout.HorizontalSlider(valor, min, max, GUILayout.ExpandWidth(true));
        GUILayout.Label($"{nuevo:F2}", _sLabel, GUILayout.Width(40));
        GUILayout.EndHorizontal();
        if (!Mathf.Approximately(nuevo, valor)) setter(nuevo);
    }

    void ToggleOpcion(string label, bool valor, System.Action<bool> setter)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _sLabel, GUILayout.Width(200));
        bool nuevo = GUILayout.Toggle(valor, valor ? "Sí" : "No");
        GUILayout.EndHorizontal();
        if (nuevo != valor) setter(nuevo);
    }

    void SelectorOpcion(string label, string[] opciones, int idx, System.Action<int> setter)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _sLabel, GUILayout.Width(200));
        if (GUILayout.Button("◄", GUILayout.Width(28)))
            setter((idx - 1 + opciones.Length) % opciones.Length);
        GUILayout.Label(opciones[Mathf.Clamp(idx, 0, opciones.Length - 1)],
            _sLabel, GUILayout.ExpandWidth(true));
        if (GUILayout.Button("►", GUILayout.Width(28)))
            setter((idx + 1) % opciones.Length);
        GUILayout.EndHorizontal();
    }

    string[] OpcionesResolucion()
    {
        var arr = new string[SistemaOpciones.RESOLUCIONES.Length];
        for (int i = 0; i < arr.Length; i++) arr[i] = SistemaOpciones.RESOLUCIONES[i].label;
        return arr;
    }

    void InicializarEstilos()
    {
        if (_estilosInit) return;
        _estilosInit = true;

        _sVentana = new GUIStyle(GUI.skin.box)
        {
            normal = { background = MakeTex(2, 2, new Color(0.08f, 0.08f, 0.12f, 0.97f)) }
        };
        _sTitulo = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 22, fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = new Color(0.9f, 0.85f, 0.5f) }
        };
        _sBoton = new GUIStyle(GUI.skin.button)
        {
            fontSize = 14, fontStyle = FontStyle.Bold,
            normal   = { textColor = Color.white,
                         background = MakeTex(2, 2, new Color(0.18f, 0.18f, 0.28f)) },
            hover    = { textColor = Color.white,
                         background = MakeTex(2, 2, new Color(0.28f, 0.38f, 0.68f)) }
        };
        _sBotonTab = new GUIStyle(_sBoton) { fontSize = 12 };
        _sLabel = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13, normal = { textColor = new Color(0.88f, 0.88f, 0.92f) }
        };
        _sSliderLabel = new GUIStyle(_sLabel)
        {
            fontStyle = FontStyle.Bold, fontSize = 11,
            normal    = { textColor = new Color(0.7f, 0.85f, 1f) }
        };
    }

    static Texture2D MakeTex(int w, int h, Color c)
    {
        var pix = new Color[w * h];
        for (int i = 0; i < pix.Length; i++) pix[i] = c;
        var t = new Texture2D(w, h);
        t.SetPixels(pix); t.Apply();
        return t;
    }

    // Helper público — usado también por MenuPrincipal
    public static Texture2D MakeTex2x2(Color c) => MakeTex(2, 2, c);
}
