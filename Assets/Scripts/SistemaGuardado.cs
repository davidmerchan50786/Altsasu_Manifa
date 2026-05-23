// Assets/Scripts/SistemaGuardado.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE GUARDADO — JSON + PlayerPrefs
//
//  Guarda y carga: posición jugador, misión activa, dinero, wanted,
//  apoyo popular, hora del día, clima, estadísticas de juego.
//
//  Slots: 3 ranuras de guardado.
//  Auto-guardado: cada 3 minutos y en cambio de misión.
//  Tecla: F5 = guardar, F9 = cargar último.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System;
using System.IO;

public class SistemaGuardado : SingletonMono<SistemaGuardado>
{
    protected override bool DestroyGameObjectOnDuplicate => true;

    const string CLAVE_SLOT    = "ultimo_slot";
    const float  AUTOSAVE_INT  = 180f; // 3 minutos

    float _timerAutoSave;

    // ── Estructura de datos guardados ─────────────────────────────────────
    [Serializable]
    public class DatosPartida
    {
        public int    version    = 1;
        public string fecha      = "";
        public string misionActual = "";
        public int    objetivoActual = 0;

        // Jugador
        public float  jugX, jugY, jugZ;
        public int    vida;
        public int    dinero;
        public int    nivelWanted;

        // Sistemas
        public float  apoyoPopular;
        public float  paranoia;
        public float  horaDelDia;
        public int    estadoClima;

        // Estadísticas
        public int    totalPintadas;
        public int    totalKmRecorridos;
        public int    totalManifestaciones;
        public float  tiempoJugado;
        public int    misionesCompletadas;
    }

    DatosPartida _datos = new();
    float        _tiempoSesion;

    // ════════════════════════════════════════════════════════════════════════

    void Start()
    {
        SuscribirEventos();
        if (HayGuardado(0)) CargarDesdeSlot(0);
    }

    void Update()
    {
        _tiempoSesion        += Time.deltaTime;
        _datos.tiempoJugado  += Time.deltaTime;
        _timerAutoSave       += Time.deltaTime;

        if (_timerAutoSave >= AUTOSAVE_INT)
        {
            _timerAutoSave = 0f;
            GuardarEnSlot(0); // slot 0 = auto-guardado
            AlsasuaLogger.Info("Guardado", "Auto-guardado completado");
        }

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;
        if (kb.f5Key.wasPressedThisFrame) GuardarEnSlot(PlayerPrefs.GetInt(CLAVE_SLOT, 0));
        if (kb.f9Key.wasPressedThisFrame) CargarDesdeSlot(PlayerPrefs.GetInt(CLAVE_SLOT, 0));
    }

    void SuscribirEventos()
    {
        SistemaMisiones.OnMisionCompletada  += _ => { _datos.misionesCompletadas++; GuardarEnSlot(0); };
        SistemaGrafitis.OnPintadaRealizada  += ()  => _datos.totalPintadas++;
        GameManagerAltsasua.OnEstrellasCambia += _ => CapturarEstado();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  GUARDAR
    // ════════════════════════════════════════════════════════════════════════

    public void GuardarEnSlot(int slot)
    {
        CapturarEstado();
        _datos.fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        string json = JsonUtility.ToJson(_datos, true);
        string path = RutaSlot(slot);
        try { File.WriteAllText(path, json); }
        catch (Exception e) { AlsasuaLogger.Warn("Guardado", $"Error guardando slot {slot}: {e.Message}"); return; }
        PlayerPrefs.SetInt(CLAVE_SLOT, slot);
        PlayerPrefs.Save();
        AlsasuaLogger.Info("Guardado", $"Guardado en slot {slot} — {path}");
    }

    void CapturarEstado()
    {
        var j  = AltsasuCore.Jugador;
        var gm = GameManagerAltsasua.Instance;
        var ap = SistemaApoyoPopular.Instance;
        var at = AltsasuCore.I?.atmosferaSystem;
        var cl = AltsasuCore.I?.climaSystem;
        var sm = SistemaMisiones.Instance;

        if (j  != null) { _datos.jugX = j.position.x; _datos.jugY = j.position.y; _datos.jugZ = j.position.z; }
        if (gm != null) { _datos.dinero = gm.dinero; _datos.nivelWanted = gm.nivelBusqueda; }
        if (ap != null) { _datos.apoyoPopular = ap.apoyo; _datos.paranoia = ap.paranoia; }
        if (at != null) { _datos.horaDelDia = at.HoraDelDia; }
        if (cl != null) { _datos.estadoClima = (int)cl.climaActual; }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CARGAR
    // ════════════════════════════════════════════════════════════════════════

    public void CargarDesdeSlot(int slot)
    {
        string path = RutaSlot(slot);
        if (!File.Exists(path)) { AlsasuaLogger.Warn("Guardado", $"Slot {slot} no existe"); return; }
        try
        {
            string json = File.ReadAllText(path);
            _datos = JsonUtility.FromJson<DatosPartida>(json);
        }
        catch (Exception e) { AlsasuaLogger.Warn("Guardado", $"Error cargando: {e.Message}"); return; }

        AplicarEstado();
        AlsasuaLogger.Info("Guardado", $"Cargado slot {slot} — {_datos.fecha}");
    }

    void AplicarEstado()
    {
        // Posición jugador
        var j = AltsasuCore.Jugador;
        if (j != null && (_datos.jugX != 0 || _datos.jugZ != 0))
            j.position = new Vector3(_datos.jugX, _datos.jugY, _datos.jugZ);

        // Dinero y wanted
        var gm = GameManagerAltsasua.Instance;
        if (gm != null)
        {
            gm.dinero         = _datos.dinero;
            gm.nivelBusqueda  = Mathf.Clamp(_datos.nivelWanted, 0, 5);
        }

        // Apoyo popular
        var ap = SistemaApoyoPopular.Instance;
        if (ap != null) { ap.apoyo = _datos.apoyoPopular; ap.paranoia = _datos.paranoia; }

        // Clima
        var cl = AltsasuCore.I?.climaSystem;
        if (cl != null) cl.CambiarClima((SistemaClima.EstadoClima)_datos.estadoClima);

        // Misión
        // (SistemaMisiones no se reinicia — la misión se retoma al vuelo)
    }

    // ════════════════════════════════════════════════════════════════════════
    //  UTILIDADES
    // ════════════════════════════════════════════════════════════════════════

    public static bool HayGuardado(int slot)
        => File.Exists(RutaSlot(slot));

    public static DatosPartida LeerCabecera(int slot)
    {
        string path = RutaSlot(slot);
        if (!File.Exists(path)) return null;
        try { return JsonUtility.FromJson<DatosPartida>(File.ReadAllText(path)); }
        catch { return null; }
    }

    static string RutaSlot(int slot)
        => Path.Combine(Application.persistentDataPath, $"alsasua_save_{slot}.json");

    public DatosPartida Datos => _datos;
}
