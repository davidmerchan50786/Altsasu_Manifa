// Assets/Scripts/SistemaLogros.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE LOGROS — achievements persistentes en PlayerPrefs
//
//  30 logros con condiciones automáticas + notificación en HUD.
//  Se evalúan cada frame via eventos y polling periódico.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections.Generic;

public class SistemaLogros : MonoBehaviour
{
    public static SistemaLogros Instance { get; private set; }
    public static event System.Action<Logro> OnLogroDesbloqueado;

    // ── Definición de logro ───────────────────────────────────────────────
    public class Logro
    {
        public string Id, Nombre, Descripcion, Icono;
        public bool   Desbloqueado => PlayerPrefs.GetInt("logro_" + Id, 0) == 1;
        public void   Desbloquear()
        {
            if (Desbloqueado) return;
            PlayerPrefs.SetInt("logro_" + Id, 1);
            PlayerPrefs.Save();
            OnLogroDesbloqueado?.Invoke(this);
            AlsasuaLogger.Info("Logros", $"🏆 {Nombre}");
        }
    }

    // ── Catálogo ──────────────────────────────────────────────────────────
    public static readonly List<Logro> Todos = new()
    {
        new Logro { Id="primer_paso",     Nombre="Lehen Urratsa",      Icono="👣", Descripcion="Da tus primeros pasos en Alsasua" },
        new Logro { Id="primer_coche",    Nombre="Txofer",             Icono="🚔", Descripcion="Roba tu primer vehículo" },
        new Logro { Id="primer_graffiti", Nombre="Pintatzaile",        Icono="🎨", Descripcion="Pinta tu primera pintada política" },
        new Logro { Id="5_graffitis",     Nombre="Margolaria",         Icono="🖌",  Descripcion="Pinta 5 graffitis" },
        new Logro { Id="20_graffitis",    Nombre="Askatasuna",         Icono="✊", Descripcion="Pinta 20 mensajes" },
        new Logro { Id="primera_manifa",  Nombre="Manifestante",       Icono="📢", Descripcion="Participa en una manifestación" },
        new Logro { Id="manifa_grande",   Nombre="Herria Altxatzen",   Icono="🔥", Descripcion="Mantén una manifestación 3 minutos" },
        new Logro { Id="5_estrellas",     Nombre="Nahi Dutena",        Icono="⭐", Descripcion="Alcanza 5 estrellas de búsqueda" },
        new Logro { Id="cero_estrellas",  Nombre="Itzalia",            Icono="🌑", Descripcion="Reduce el wanted a 0 desde 3 estrellas" },
        new Logro { Id="apoyo_50",        Nombre="Herriaren Laguna",   Icono="❤", Descripcion="Consigue 50% de apoyo popular" },
        new Logro { Id="apoyo_80",        Nombre="Heroiak",            Icono="🌟", Descripcion="Consigue 80% de apoyo popular" },
        new Logro { Id="primera_bomba",   Nombre="Pirotekniko",        Icono="💥", Descripcion="Detona tu primera bomba" },
        new Logro { Id="coche_destruido", Nombre="Ez Dago Laguntzarik",Icono="🔥", Descripcion="Destruye un coche policial" },
        new Logro { Id="primera_mision",  Nombre="Lehen Misioa",       Icono="📋", Descripcion="Completa tu primera misión" },
        new Logro { Id="5_misiones",      Nombre="Bidean",             Icono="🗺",  Descripcion="Completa 5 misiones" },
        new Logro { Id="12_misiones",     Nombre="Askapen Bidea",      Icono="🏁", Descripcion="Completa todas las misiones" },
        new Logro { Id="primera_barricada",Nombre="Hesia",             Icono="🛡", Descripcion="Coloca una barricada" },
        new Logro { Id="radio",           Nombre="Radio Askatasuna",   Icono="📻", Descripcion="Emite desde el monte Aralar" },
        new Logro { Id="corte_n1",        Nombre="Bidea Moztu",        Icono="🚧", Descripcion="Corta la N-1 durante 90 segundos" },
        new Logro { Id="nocturno",        Nombre="Gaueko Gizon",       Icono="🌙", Descripcion="Completa una misión de noche" },
        new Logro { Id="5000_dinero",     Nombre="Dirutza",            Icono="💶", Descripcion="Acumula 5000€" },
        new Logro { Id="velocidad",       Nombre="Korrika",            Icono="💨", Descripcion="Conduce a más de 160 km/h" },
        new Logro { Id="represalia",      Nombre="Biziraupena",        Icono="⚔", Descripcion="Sobrevive 90s con 3 estrellas" },
        new Logro { Id="presoak",         Nombre="Presoak Etxera",     Icono="✍", Descripcion="Completa la campaña de pintadas" },
        new Logro { Id="concentracion",   Nombre="Elkarretaratzea",    Icono="🕯",  Descripcion="Organiza una concentración nocturna" },
        new Logro { Id="victoria",        Nombre="ASKATASUNA",         Icono="🏆", Descripcion="Completa el juego" },
        new Logro { Id="sin_wanted",      Nombre="Fantasma",           Icono="👻", Descripcion="Completa una misión sin wanted" },
        new Logro { Id="kilometros",      Nombre="Bidaiari",           Icono="🚶", Descripcion="Recorre 10 km a pie" },
        new Logro { Id="fauna",           Nombre="Basoa",              Icono="🌿", Descripcion="Avista 3 especies de fauna" },
        new Logro { Id="amanecer",        Nombre="Eguzki",             Icono="🌅", Descripcion="Contempla un amanecer desde el monte" },
    };

    // ── Estado ────────────────────────────────────────────────────────────
    float _timerPoll = 0f;
    int   _grafitisCount;
    float _distanciaRecorrida;
    Vector3 _posAnterior;
    bool  _primeraVez = true;

    // Notificación en pantalla
    Logro _logroMostrado;
    float _timerNotif;
    GUIStyle _estiloNotif;

    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        SuscribirEventos();
    }

    void SuscribirEventos()
    {
        SistemaGrafitis.OnPintadaRealizada    += OnGraffiti;
        SistemaMisiones.OnMisionCompletada    += OnMisionCompletada;
        SistemaMisiones.OnMisionIniciada      += OnMisionIniciada;
        GameManagerAltsasua.OnEstrellasCambia += OnWanted;
        ControladorVehiculoJugador.OnJugadorEntro += _ => Todos.Find(l=>l.Id=="primer_coche")?.Desbloquear();
        SistemaGrafitis.OnPintadaRealizada    += () => Todos.Find(l=>l.Id=="primer_graffiti")?.Desbloquear();
        VehiculoNPC.OnVehiculoDestruido       += _ => Todos.Find(l=>l.Id=="coche_destruido")?.Desbloquear();
        OnLogroDesbloqueado                   += MostrarNotificacion;
    }

    void Update()
    {
        _timerPoll += Time.deltaTime;
        if (_timerNotif > 0f) _timerNotif -= Time.unscaledDeltaTime;

        // Primer paso
        if (_primeraVez && AltsasuCore.Jugador != null) { Todos.Find(l=>l.Id=="primer_paso")?.Desbloquear(); _primeraVez = false; }

        if (_timerPoll < 2f) return;
        _timerPoll = 0f;
        EvaluarPeriodico();
    }

    void EvaluarPeriodico()
    {
        var gm    = GameManagerAltsasua.Instance;
        var apoyo = SistemaApoyoPopular.Instance;
        var atm   = AltsasuCore.I?.atmosferaSystem;
        var j     = AltsasuCore.Jugador;

        // Distancia recorrida
        if (j != null)
        {
            _distanciaRecorrida += Vector3.Distance(j.position, _posAnterior);
            _posAnterior = j.position;
            if (_distanciaRecorrida > 10000f) Todos.Find(l=>l.Id=="kilometros")?.Desbloquear();
        }

        // Dinero
        if (gm != null && gm.dinero >= 5000) Todos.Find(l=>l.Id=="5000_dinero")?.Desbloquear();

        // Apoyo
        if (apoyo != null)
        {
            if (apoyo.apoyo >= 50f) Todos.Find(l=>l.Id=="apoyo_50")?.Desbloquear();
            if (apoyo.apoyo >= 80f) Todos.Find(l=>l.Id=="apoyo_80")?.Desbloquear();
        }

        // Cero wanted
        if (gm != null && gm.nivelBusqueda == 0) Todos.Find(l=>l.Id=="cero_estrellas")?.Desbloquear();

        // Amanecer en el monte
        if (atm != null && j != null)
        {
            float hora = atm.HoraDelDia;
            bool amanecer = hora > 6f && hora < 7.5f;
            float alturaJ = j.position.y;
            if (amanecer && alturaJ > 400f) Todos.Find(l=>l.Id=="amanecer")?.Desbloquear();
        }

        // Velocidad en coche
        var coche = FindFirstObjectByType<ControladorVehiculoJugador>();
        if (coche != null && coche.JugadorDentro)
        {
            var rb = coche.GetComponent<Rigidbody>();
            if (rb != null && rb.linearVelocity.magnitude * 3.6f > 160f)
                Todos.Find(l=>l.Id=="velocidad")?.Desbloquear();
        }
    }

    // ── Callbacks ─────────────────────────────────────────────────────────

    void OnGraffiti()
    {
        _grafitisCount++;
        if (_grafitisCount >= 1)  Todos.Find(l=>l.Id=="primer_graffiti")?.Desbloquear();
        if (_grafitisCount >= 5)  Todos.Find(l=>l.Id=="5_graffitis")?.Desbloquear();
        if (_grafitisCount >= 20) Todos.Find(l=>l.Id=="20_graffitis")?.Desbloquear();
    }

    void OnMisionCompletada(string nombre)
    {
        int completadas = PlayerPrefs.GetInt("misiones_completadas", 0) + 1;
        PlayerPrefs.SetInt("misiones_completadas", completadas);
        if (completadas >= 1)  Todos.Find(l=>l.Id=="primera_mision")?.Desbloquear();
        if (completadas >= 5)  Todos.Find(l=>l.Id=="5_misiones")?.Desbloquear();
        if (completadas >= 12) Todos.Find(l=>l.Id=="12_misiones")?.Desbloquear();
        if (nombre.Contains("Manifa Final") || nombre.Contains("ASKATASUNA"))
            Todos.Find(l=>l.Id=="victoria")?.Desbloquear();
    }

    void OnMisionIniciada(string _)
    {
        // Verificar si es de noche
        var atm = AltsasuCore.I?.atmosferaSystem;
        if (atm != null && !atm.EsDeDia) Todos.Find(l=>l.Id=="nocturno")?.Desbloquear();
    }

    void OnWanted(int nivel)
    {
        if (nivel >= 5) Todos.Find(l=>l.Id=="5_estrellas")?.Desbloquear();
    }

    // ── Notificación OnGUI ────────────────────────────────────────────────

    void MostrarNotificacion(Logro l)
    {
        _logroMostrado = l;
        _timerNotif = 4f;
    }

    void OnGUI()
    {
        if (_timerNotif <= 0f || _logroMostrado == null) return;

        if (_estiloNotif == null)
            _estiloNotif = new GUIStyle(GUI.skin.box)
            {
                fontSize = 14, richText = true,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white,
                           background = MenuPausa.MakeTex2x2(new Color(0.1f,0.35f,0.1f,0.9f)) },
                padding = new RectOffset(14, 14, 10, 10)
            };

        float alpha = Mathf.Clamp01(_timerNotif);
        GUI.color = new Color(1,1,1, alpha);
        float w = 320f, h = 54f;
        float x = Screen.width - w - 16f, y = Screen.height * 0.35f;
        GUI.Box(new Rect(x, y, w, h),
            $"{_logroMostrado.Icono}  <b>{_logroMostrado.Nombre}</b>\n<size=11><color=#AAFFAA>{_logroMostrado.Descripcion}</color></size>",
            _estiloNotif);
        GUI.color = Color.white;
    }

    void OnDestroy()
    {
        SistemaGrafitis.OnPintadaRealizada    -= OnGraffiti;
        SistemaMisiones.OnMisionCompletada    -= OnMisionCompletada;
        SistemaMisiones.OnMisionIniciada      -= OnMisionIniciada;
        GameManagerAltsasua.OnEstrellasCambia -= OnWanted;
        OnLogroDesbloqueado                   -= MostrarNotificacion;
    }
}
