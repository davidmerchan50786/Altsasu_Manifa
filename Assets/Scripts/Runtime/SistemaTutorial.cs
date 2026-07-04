// Assets/Scripts/SistemaTutorial.cs
// ═══════════════════════════════════════════════════════════════════════════
//  TUTORIAL IN-GAME — contextual, no bloqueante
//
//  Muestra pistas emergentes cuando el jugador hace algo por primera vez.
//  Se guarda qué pistas se han mostrado en PlayerPrefs.
//  Se descarta con cualquier tecla o tras 5 segundos.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class SistemaTutorial : SingletonMono<SistemaTutorial>
{
    // ── Pistas ────────────────────────────────────────────────────────────
    public enum Pista
    {
        Movimiento, Camara, Sprint, Agacharse, Saltar,
        EntrarVehiculo, Conducir, SalirVehiculo,
        ModoPintar, CambiarGraffiti, Pintar,
        ModoManifestacion, IniciarManifestacion,
        UsarBomba, DetonarBomba,
        PanelClima, Pausa,
        MisionIniciada, NavMeshListo,
        Wanted1, Wanted3,
    }

    static readonly Dictionary<Pista, (string titulo, string texto, string tecla)> PISTAS = new()
    {
        [Pista.Movimiento]          = ("Movimiento",        "WASD para moverte por Alsasua",                     "WASD"),
        [Pista.Camara]              = ("Cámara",            "Mueve el ratón para girar la cámara",               "🖱"),
        [Pista.Sprint]              = ("Correr",            "Mantén Shift para correr",                          "⇧ Shift"),
        [Pista.Agacharse]           = ("Agacharse",         "Ctrl izquierdo para agacharse / modo sigilo",       "Ctrl"),
        [Pista.Saltar]              = ("Saltar",            "Espacio para saltar",                               "Space"),
        [Pista.EntrarVehiculo]      = ("Entrar vehículo",   "Acércate a un coche y pulsa E para entrar",         "E"),
        [Pista.Conducir]            = ("Conducir",          "WASD para conducir · Espacio = freno de mano",      "WASD + Space"),
        [Pista.SalirVehiculo]       = ("Salir vehículo",    "Pulsa E de nuevo para salir del vehículo",          "E"),
        [Pista.ModoPintar]          = ("Modo spray",        "G activa/desactiva el spray. Acércate a una pared", "G"),
        [Pista.CambiarGraffiti]     = ("Cambiar pintada",   "Rueda del ratón para cambiar el diseño",            "🖱 rueda"),
        [Pista.Pintar]              = ("Pintar",            "Click izquierdo para pintar · Derecho = pegatina",  "🖱 click"),
        [Pista.ModoManifestacion]   = ("Manifestación",     "M abre el modo manifestación en la plaza",          "M"),
        [Pista.IniciarManifestacion]=("Convocar",           "Estás en la plaza — pulsa M para manifestarte",     "M"),
        [Pista.UsarBomba]           = ("Bomba",             "B coloca una bomba lapa en la superficie",          "B"),
        [Pista.DetonarBomba]        = ("Detonar",           "G detona la última bomba colocada",                 "G"),
        [Pista.PanelClima]          = ("Clima",             "Ctrl+W abre el panel de clima (sol/lluvia/nieve…)", "Ctrl+W"),
        [Pista.Pausa]               = ("Pausa",             "ESC abre el menú de pausa y opciones",              "ESC"),
        [Pista.MisionIniciada]      = ("Nueva misión",      "Mira la esquina inferior — te indica el objetivo",  "👁"),
        [Pista.NavMeshListo]        = ("IA activa",         "El NavMesh está listo — los policías patrullan",    ""),
        [Pista.Wanted1]             = ("1 estrella",        "La Policía Foral te busca. Piérdelos de vista.",    ""),
        [Pista.Wanted3]             = ("¡Alerta máxima!",  "3 estrellas — abandona la zona rápido",             ""),
    };

    // ── Cola de pistas ────────────────────────────────────────────────────
    readonly Queue<Pista> _cola = new();
    Pista?  _pistaActual;
    float   _timerPista;
    float   _timerInput;
    bool    _mostrando;

    GUIStyle _estiloFondo, _estiloTitulo, _estiloTexto, _estiloTecla;
    bool     _estilosInit;

    // ════════════════════════════════════════════════════════════════════════

    protected override void OnAwake() => SuscribirEventos();

    void OnMisionStart(string _)                 => Mostrar(Pista.MisionIniciada);
    void OnWanted(int n)                         { if (n==1) Mostrar(Pista.Wanted1); if (n==3) Mostrar(Pista.Wanted3); }
    void OnEntroVehiculo(ControladorVehiculoJugador _) { Mostrar(Pista.Conducir); Mostrar(Pista.SalirVehiculo); }

    void SuscribirEventos()
    {
        SistemaMisiones.OnMisionIniciada          += OnMisionStart;
        GameManagerAltsasua.OnEstrellasCambia     += OnWanted;
        ControladorVehiculoJugador.OnJugadorEntro += OnEntroVehiculo;
    }

    void Start()
    {
        // Tutorial inicial secuencial
        StartCoroutine(TutorialInicial());
    }

    IEnumerator TutorialInicial()
    {
        yield return new WaitForSeconds(2f);
        Mostrar(Pista.Movimiento);
        yield return new WaitForSeconds(6f);
        Mostrar(Pista.Camara);
        yield return new WaitForSeconds(6f);
        Mostrar(Pista.Sprint);
        yield return new WaitForSeconds(6f);
        Mostrar(Pista.EntrarVehiculo);
        yield return new WaitForSeconds(6f);
        Mostrar(Pista.ModoPintar);
        yield return new WaitForSeconds(6f);
        Mostrar(Pista.Pausa);
    }

    // ── API ───────────────────────────────────────────────────────────────

    public static void Mostrar(Pista p)
    {
        if (Instance == null) return;
        string key = "tut_" + p;
        if (PlayerPrefs.GetInt(key, 0) == 1) return; // ya mostrada
        Instance._cola.Enqueue(p);
    }

    // ── Update ────────────────────────────────────────────────────────────

    void Update()
    {
        if (_mostrando)
        {
            _timerPista -= Time.unscaledDeltaTime;
            _timerInput += Time.unscaledDeltaTime;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            bool tecla = _timerInput > 0.5f && kb != null && kb.anyKey.wasPressedThisFrame;
            if (tecla || _timerPista <= 0f)
            {
                _mostrando = false;
                if (_pistaActual.HasValue)
                    PlayerPrefs.SetInt("tut_" + _pistaActual.Value, 1);
                _pistaActual = null;
            }
        }
        else if (_cola.Count > 0)
        {
            _pistaActual  = _cola.Dequeue();
            _timerPista   = 5f;
            _timerInput   = 0f;
            _mostrando    = true;
        }
    }

    // ── OnGUI ─────────────────────────────────────────────────────────────

    void OnGUI()
    {
        if (!ArranqueMundo.BaselineListo) return;
        if (!_mostrando || !_pistaActual.HasValue) return;
        if (!PISTAS.TryGetValue(_pistaActual.Value, out var datos)) return;

        InicializarEstilos();

        float alpha = Mathf.Clamp01(_timerPista);
        GUI.color = new Color(1,1,1,alpha);

        float W = Screen.width, H = Screen.height;
        float w = 380f, h = 80f;
        float x = (W - w) / 2f, y = H * 0.78f;

        GUI.Box(new Rect(x, y, w, h), "", _estiloFondo);
        GUI.Label(new Rect(x+14, y+8, w-100, 22), datos.titulo, _estiloTitulo);
        GUI.Label(new Rect(x+14, y+30, w-100, 36), datos.texto,  _estiloTexto);

        if (!string.IsNullOrEmpty(datos.tecla))
        {
            float tw = 80f;
            GUI.Box(new Rect(x + w - tw - 10, y + (h-32)/2f, tw, 32), datos.tecla, _estiloTecla);
        }

        GUI.color = Color.white;
    }

    void InicializarEstilos()
    {
        if (_estilosInit) return; _estilosInit = true;
        _estiloFondo  = new GUIStyle(GUI.skin.box) { normal = { background = MenuPausa.MakeTex2x2(new Color(0.05f,0.08f,0.18f,0.92f)) } };
        _estiloTitulo = new GUIStyle(GUI.skin.label) { fontSize=13, fontStyle=FontStyle.Bold, normal={textColor=new Color(1f,0.9f,0.4f)} };
        _estiloTexto  = new GUIStyle(GUI.skin.label) { fontSize=12, wordWrap=true, normal={textColor=new Color(0.88f,0.88f,0.92f)} };
        _estiloTecla  = new GUIStyle(GUI.skin.box)   { fontSize=12, fontStyle=FontStyle.Bold, alignment=TextAnchor.MiddleCenter,
                         normal={textColor=Color.white, background=MenuPausa.MakeTex2x2(new Color(0.2f,0.4f,0.8f,0.9f))} };
    }

    void OnDestroy()
    {
        SistemaMisiones.OnMisionIniciada          -= OnMisionStart;
        GameManagerAltsasua.OnEstrellasCambia     -= OnWanted;
        ControladorVehiculoJugador.OnJugadorEntro -= OnEntroVehiculo;
    }
}
