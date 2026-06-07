// Assets/Scripts/MenuPrincipal.cs
// ═══════════════════════════════════════════════════════════════════════════
//  Menú principal — escena 0 del juego.
//  OnGUI puro. Carga la escena Alsasua_Main al iniciar.
//  Aplica opciones guardadas al arrancar.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class MenuPrincipal : MonoBehaviour
{
    const string ESCENA_JUEGO  = "Alsasua_Main";
    const string SPLASH_RES    = "UI/splash_altsasu";   // Assets/Resources/UI/splash_altsasu.png

    // ── Estado ────────────────────────────────────────────────────────────
    enum Pantalla { Splash, Principal, Opciones, Controles, Creditos }
    Pantalla _pantalla = Pantalla.Splash;
    bool     _cargando = false;
    float    _progreso = 0f;
    string   _error    = "";

    Vector2 _scrollOpc, _scrollCtrl;

    // Estilos
    GUIStyle _sTitulo, _sSubtitulo, _sBoton, _sLabel, _sSlider, _sCreditos, _sIntro;
    bool     _estilosInit;

    // Splash
    Texture2D _splash;
    float     _splashAlpha   = 0f;       // fade-in
    float     _parpadeoTimer = 0f;
    bool      _parpadeoVis   = true;

    // Parallax: fondo procedural animado
    float _bgOffset;

    // ─────────────────────────────────────────────────────────────────────

    void Start()
    {
        SistemaOpciones.AplicarTodo();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
        Time.timeScale   = 1f;

        // Cargar imagen de splash desde Resources/UI/
        _splash = Resources.Load<Texture2D>(SPLASH_RES);
    }

    void Update()
    {
        _bgOffset += Time.deltaTime * 8f;

        if (_pantalla == Pantalla.Splash)
        {
            // Fade-in de la imagen
            _splashAlpha = Mathf.MoveTowards(_splashAlpha, 1f, Time.deltaTime * 1.5f);

            // Parpadeo "PULSA INTRO" — 1.4 ciclos/s
            _parpadeoTimer += Time.deltaTime;
            if (_parpadeoTimer >= 0.36f) { _parpadeoVis = !_parpadeoVis; _parpadeoTimer = 0f; }

            // Detectar INTRO / ENTER / ESPACIO / click
            bool pulsa = Input.GetKeyDown(KeyCode.Return)
                      || Input.GetKeyDown(KeyCode.KeypadEnter)
                      || Input.GetKeyDown(KeyCode.Space)
                      || Input.GetMouseButtonDown(0);

            // Con new Input System
#if ENABLE_INPUT_SYSTEM
            pulsa |= UnityEngine.InputSystem.Keyboard.current?.enterKey.wasPressedThisFrame == true
                  || UnityEngine.InputSystem.Keyboard.current?.spaceKey.wasPressedThisFrame == true;
#endif
            if (pulsa && _splashAlpha > 0.5f)
                _pantalla = Pantalla.Principal;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  GUI
    // ─────────────────────────────────────────────────────────────────────

    void OnGUI()
    {
        InicializarEstilos();

        if (_pantalla == Pantalla.Splash) { DibujarSplash(); return; }

        DibujarFondo();

        if (_cargando) { DibujarCarga(); return; }

        switch (_pantalla)
        {
            case Pantalla.Principal:  DibujarPrincipal(); break;
            case Pantalla.Opciones:   DibujarOpciones();  break;
            case Pantalla.Controles:  DibujarControles(); break;
            case Pantalla.Creditos:   DibujarCreditos();  break;
        }

        if (!string.IsNullOrEmpty(_error))
        {
            GUI.color = new Color(1f, 0.3f, 0.3f);
            GUI.Label(new Rect(10, Screen.height - 30, Screen.width, 24), "⚠ " + _error);
            GUI.color = Color.white;
        }
    }

    // ── Splash screen ────────────────────────────────────────────────────

    void DibujarSplash()
    {
        float W = Screen.width, H = Screen.height;

        // Fondo negro total
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);
        GUI.color = Color.white;

        if (_splash != null)
        {
            // Imagen centrada, máximo 90% de pantalla manteniendo ratio 16:9
            float imgRatio = (float)_splash.width / _splash.height;
            float maxW = W * 0.92f, maxH = H * 0.92f;
            float drawW, drawH;
            if (maxW / imgRatio <= maxH) { drawW = maxW; drawH = maxW / imgRatio; }
            else                         { drawH = maxH; drawW = maxH * imgRatio; }

            float ix = (W - drawW) * 0.5f, iy = (H - drawH) * 0.5f;

            GUI.color = new Color(1f, 1f, 1f, _splashAlpha);
            GUI.DrawTexture(new Rect(ix, iy, drawW, drawH), _splash, ScaleMode.ScaleToFit);
            GUI.color = Color.white;
        }
        else
        {
            // Fallback si la imagen no está: título con estilo GTA
            InicializarEstilos();
            GUI.color = new Color(1f, 1f, 1f, _splashAlpha);
            GUI.Label(new Rect(0, H * 0.32f, W, 80), "ALTSASU_MANIFA", _sTitulo);
            GUI.color = new Color(0.9f, 0.75f, 0.2f, _splashAlpha);
            GUI.Label(new Rect(0, H * 0.45f, W, 36), "EUSKAL HERRIAN, 80KO HAMARKADA", _sSubtitulo);
            GUI.color = Color.white;
        }

        // "PULSA INTRO" parpadeando — semi-transparente sobre la imagen
        if (_parpadeoVis && _splashAlpha > 0.4f)
        {
            InicializarEstilos();
            // Sombra
            GUI.color = new Color(0f, 0f, 0f, 0.7f * _splashAlpha);
            GUI.Label(new Rect(2, H * 0.872f, W, 42), "PULSA INTRO", _sIntro);
            // Texto
            GUI.color = new Color(1f, 0.95f, 0.4f, 0.88f * _splashAlpha);
            GUI.Label(new Rect(0, H * 0.868f, W, 42), "PULSA INTRO", _sIntro);
            GUI.color = Color.white;
        }
    }

    // ── Fondo procedural ──────────────────────────────────────────────────

    void DibujarFondo()
    {
        // Degradado de ciudad de noche
        GUI.color = new Color(0.04f, 0.04f, 0.08f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // Silueta de montañas procedural (líneas de puntos)
        float W = Screen.width, H = Screen.height;
        for (int i = 0; i < 120; i++)
        {
            float x = (i / 119f) * W;
            float yMonte = H * (0.55f + 0.12f * Mathf.PerlinNoise(i * 0.04f + _bgOffset * 0.002f, 0));
            GUI.color = new Color(0.10f, 0.12f, 0.18f, 0.9f);
            GUI.DrawTexture(new Rect(x, yMonte, W / 120f + 1, H - yMonte), Texture2D.whiteTexture);
        }

        // Luces de ventanas (puntos amarillos)
        var rng = new System.Random(42);
        GUI.color = new Color(1f, 0.95f, 0.6f, 0.5f);
        for (int i = 0; i < 80; i++)
        {
            float bx = (float)rng.NextDouble() * W;
            float by = H * 0.3f + (float)rng.NextDouble() * H * 0.3f;
            float parpadeo = 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(Time.time * 0.7f + i));
            GUI.color = new Color(1f, 0.95f, 0.6f, 0.4f * parpadeo);
            GUI.DrawTexture(new Rect(bx, by, 4, 3), Texture2D.whiteTexture);
        }
        GUI.color = Color.white;
    }

    // ── Pantalla principal ────────────────────────────────────────────────

    void DibujarPrincipal()
    {
        float W = Screen.width, H = Screen.height;
        float panelW = 400f;
        float panelX = (W - panelW) / 2f;

        // Título
        GUI.Label(new Rect(0, H * 0.08f, W, 70), "ALTSASU", _sTitulo);
        GUI.Label(new Rect(0, H * 0.17f, W, 32), "Manifa Simulator", _sSubtitulo);

        // Año y versión
        GUI.color = new Color(0.6f, 0.6f, 0.7f);
        GUI.Label(new Rect(0, H * 0.23f, W, 20),
            "Euskal Herria · 2024 · v3.0", _sSubtitulo);
        GUI.color = Color.white;

        float btnY = H * 0.35f;
        float btnH = 46f, gap = 12f;

        if (BotonCentrado(panelX, btnY, panelW, btnH, "▶  JUGAR"))
        {
            if (SceneExiste(ESCENA_JUEGO)) StartCoroutine(CargarEscena(ESCENA_JUEGO));
            else _error = $"Escena '{ESCENA_JUEGO}' no está en Build Settings. Ejecuta el Setup Maestro.";
        }
        btnY += btnH + gap;

        if (BotonCentrado(panelX, btnY, panelW, btnH, "⚙  OPCIONES"))   _pantalla = Pantalla.Opciones;
        btnY += btnH + gap;
        if (BotonCentrado(panelX, btnY, panelW, btnH, "🎮  CONTROLES"))  _pantalla = Pantalla.Controles;
        btnY += btnH + gap;
        if (BotonCentrado(panelX, btnY, panelW, btnH, "📜  CRÉDITOS"))   _pantalla = Pantalla.Creditos;
        btnY += btnH + gap;
        if (BotonCentrado(panelX, btnY, panelW, btnH, "❌  SALIR"))
        {
            SistemaOpciones.Guardar();
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        // Pie de página
        GUI.color = new Color(0.4f, 0.4f, 0.5f);
        GUI.Label(new Rect(0, H - 22f, W, 20),
            "Presoak Etxera  ·  Gora Euskal Herria  ·  Amnistia", _sSubtitulo);
        GUI.color = Color.white;
    }

    // ── Pantalla de opciones ──────────────────────────────────────────────

    void DibujarOpciones()
    {
        CabeceraPantalla("⚙  OPCIONES");

        float panelW = 500f, panelH = 420f;
        float x = (Screen.width - panelW) / 2f, y = Screen.height * 0.2f;
        GUILayout.BeginArea(new Rect(x, y, panelW, panelH));
        _scrollOpc = GUILayout.BeginScrollView(_scrollOpc);

        Seccion("AUDIO");
        SliderOpcion("Master",   SistemaOpciones.VolMaster,   v => SistemaOpciones.VolMaster   = v);
        SliderOpcion("Música",   SistemaOpciones.VolMusica,   v => SistemaOpciones.VolMusica   = v);
        SliderOpcion("Efectos",  SistemaOpciones.VolSFX,      v => SistemaOpciones.VolSFX      = v);
        SliderOpcion("Ambiente", SistemaOpciones.VolAmbiente, v => SistemaOpciones.VolAmbiente = v);

        Seccion("GRÁFICOS");
        SelectorOpcion("Calidad",    SistemaOpciones.NIVELES_CALIDAD, SistemaOpciones.Calidad,    i => SistemaOpciones.Calidad    = i);
        SelectorOpcion("Resolución", OpcionesRes(),                   SistemaOpciones.Resolucion, i => SistemaOpciones.Resolucion = i);
        ToggleOpcion("Pantalla completa", SistemaOpciones.Fullscreen,  v => SistemaOpciones.Fullscreen = v);
        ToggleOpcion("VSync",             SistemaOpciones.VSync,       v => SistemaOpciones.VSync      = v);
        SliderOpcion("FOV", SistemaOpciones.FOV, v => SistemaOpciones.FOV = v, 50f, 110f);

        Seccion("JUEGO");
        SliderOpcion("Sensibilidad", SistemaOpciones.SensRaton, v => SistemaOpciones.SensRaton = v, 0.5f, 8f);
        ToggleOpcion("Invertir Y",   SistemaOpciones.InvertirY,  v => SistemaOpciones.InvertirY  = v);
        ToggleOpcion("Subtítulos",   SistemaOpciones.Subtitulos, v => SistemaOpciones.Subtitulos = v);
        SelectorOpcion("Idioma",     SistemaOpciones.IDIOMAS,    SistemaOpciones.Idioma, i => SistemaOpciones.Idioma = i);

        GUILayout.EndScrollView();
        GUILayout.EndArea();

        BotonVolver(() => { SistemaOpciones.Guardar(); _pantalla = Pantalla.Principal; }, "💾 Guardar y volver");
    }

    // ── Pantalla de controles ─────────────────────────────────────────────

    void DibujarControles()
    {
        CabeceraPantalla("🎮  CONTROLES");

        float panelW = 500f, panelH = 400f;
        float x = (Screen.width - panelW) / 2f, y = Screen.height * 0.2f;
        GUILayout.BeginArea(new Rect(x, y, panelW, panelH));
        _scrollCtrl = GUILayout.BeginScrollView(_scrollCtrl);

        foreach (var kv in SistemaOpciones.AccionesDefault)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(kv.Key, _sLabel, GUILayout.Width(220));
            string tecla = SistemaOpciones.GetControl(kv.Key);
            GUILayout.Label(tecla.ToUpper(), _sSlider, GUILayout.Width(180));
            GUILayout.EndHorizontal();
            GUILayout.Space(2);
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();

        BotonVolver(() => _pantalla = Pantalla.Principal);
    }

    // ── Pantalla de créditos ──────────────────────────────────────────────

    void DibujarCreditos()
    {
        CabeceraPantalla("📜  CRÉDITOS");

        float panelW = 520f, panelH = 380f;
        float x = (Screen.width - panelW) / 2f, y = Screen.height * 0.2f;

        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(x - 10, y - 10, panelW + 20, panelH + 20), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(x, y, panelW, panelH));
        GUILayout.Label(_textoCreditos, _sCreditos);
        GUILayout.EndArea();

        BotonVolver(() => _pantalla = Pantalla.Principal);
    }

    const string _textoCreditos =
@"<b>ALTSASU — Manifa Simulator</b>

Desarrollado con Unity 6 HDRP
Datos OSM: OpenStreetMap · Navarra
DEM: IGN · PNOA 1m/px
Texturas CC0: Poly Haven
Arquitectura: data-oriented, GPU instancing

<b>Dedicado al pueblo de Alsasua</b>
y a todos los presos políticos vascos.

<i>Presoak Etxera  ·  Amnistia
Gora Euskal Herria Askatuta</i>

Unity 6 · C# · HDRP · NavMesh
Input System · GPU Instancing";

    // ── Carga de escena ───────────────────────────────────────────────────

    IEnumerator CargarEscena(string nombre)
    {
        _cargando = true;
        var op    = SceneManager.LoadSceneAsync(nombre);
        op.allowSceneActivation = false;
        while (op.progress < 0.9f)
        {
            _progreso = op.progress;
            yield return null;
        }
        _progreso = 1f;
        yield return new WaitForSeconds(0.5f);
        op.allowSceneActivation = true;
    }

    void DibujarCarga()
    {
        float W = Screen.width, H = Screen.height;
        GUI.Label(new Rect(0, H * 0.4f, W, 50), "Cargando Alsasua...", _sTitulo);
        float barW = W * 0.6f, barH = 20f;
        float barX = (W - barW) / 2f, barY = H * 0.52f;
        GUI.color = new Color(0.15f, 0.15f, 0.2f);
        GUI.DrawTexture(new Rect(barX, barY, barW, barH), Texture2D.whiteTexture);
        GUI.color = new Color(0.3f, 0.7f, 1f);
        GUI.DrawTexture(new Rect(barX, barY, barW * _progreso, barH), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(0, barY + 28, W, 24),
            $"{(_progreso * 100f):F0}%  —  Generando el mundo...", _sSubtitulo);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    bool BotonCentrado(float x, float y, float w, float h, string label)
        => GUI.Button(new Rect(x, y, w, h), label, _sBoton);

    void CabeceraPantalla(string titulo)
    {
        GUI.Label(new Rect(0, Screen.height * 0.07f, Screen.width, 50), titulo, _sTitulo);
    }

    void BotonVolver(System.Action accion, string label = "◄ Volver")
    {
        float W = Screen.width;
        if (GUI.Button(new Rect((W - 220f) / 2f, Screen.height * 0.88f, 220f, 40f), label, _sBoton))
            accion();
    }

    void Seccion(string t)
    {
        GUILayout.Space(6);
        GUI.color = new Color(0.7f, 0.85f, 1f);
        GUILayout.Label(t, _sSlider);
        GUI.color = Color.white;
    }

    void SliderOpcion(string label, float valor, System.Action<float> setter,
                      float min = 0f, float max = 1f)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _sLabel, GUILayout.Width(200));
        float nuevo = GUILayout.HorizontalSlider(valor, min, max);
        GUILayout.Label($"{nuevo:F2}", _sLabel, GUILayout.Width(38));
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
        if (GUILayout.Button("◄", GUILayout.Width(26))) setter((idx - 1 + opciones.Length) % opciones.Length);
        GUILayout.Label(opciones[Mathf.Clamp(idx, 0, opciones.Length - 1)], _sLabel, GUILayout.ExpandWidth(true));
        if (GUILayout.Button("►", GUILayout.Width(26))) setter((idx + 1) % opciones.Length);
        GUILayout.EndHorizontal();
    }

    string[] OpcionesRes()
    {
        var a = new string[SistemaOpciones.RESOLUCIONES.Length];
        for (int i = 0; i < a.Length; i++) a[i] = SistemaOpciones.RESOLUCIONES[i].label;
        return a;
    }

    bool SceneExiste(string nombre)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            if (System.IO.Path.GetFileNameWithoutExtension(
                UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i)) == nombre)
                return true;
        return false;
    }

    void InicializarEstilos()
    {
        if (_estilosInit) return; _estilosInit = true;

        _sTitulo = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 42, fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = new Color(1f, 0.92f, 0.4f) }
        };
        _sSubtitulo = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 16, alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = new Color(0.75f, 0.78f, 0.88f) }
        };
        _sBoton = new GUIStyle(GUI.skin.button)
        {
            fontSize = 16, fontStyle = FontStyle.Bold,
            normal   = { textColor = Color.white,
                         background = MenuPausa.MakeTex2x2(new Color(0.12f, 0.14f, 0.22f, 0.95f)) },
            hover    = { textColor = Color.white,
                         background = MenuPausa.MakeTex2x2(new Color(0.22f, 0.36f, 0.70f, 0.95f)) }
        };
        _sLabel = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14, normal = { textColor = new Color(0.88f, 0.88f, 0.92f) }
        };
        _sSlider = new GUIStyle(_sLabel)
        {
            fontStyle = FontStyle.Bold, fontSize = 12,
            normal    = { textColor = new Color(0.7f, 0.85f, 1f) }
        };
        _sCreditos = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 14, richText = true, wordWrap = true,
            alignment = TextAnchor.UpperCenter,
            normal    = { textColor = new Color(0.85f, 0.88f, 0.95f) }
        };
        _sIntro = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 22, fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = new Color(1f, 0.95f, 0.4f) }
        };
    }
}
