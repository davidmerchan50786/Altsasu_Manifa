// Assets/Scripts/SistemaOpciones.cs
// ═══════════════════════════════════════════════════════════════════════════
//  Sistema de opciones persistentes — guardado en PlayerPrefs.
//  Cubre: Audio · Gráficos · Controles · Idioma
//  Accedido desde MenuPrincipal y MenuPausa.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.Audio;
using System.Collections.Generic;

public static class SistemaOpciones
{
    // ── Claves PlayerPrefs ────────────────────────────────────────────────
    const string K_VOL_MASTER  = "vol_master";
    const string K_VOL_MUSICA  = "vol_musica";
    const string K_VOL_SFX     = "vol_sfx";
    const string K_VOL_AMBIENTE = "vol_ambiente";
    const string K_CALIDAD     = "calidad";
    const string K_FULLSCREEN  = "fullscreen";
    const string K_RESOLUCION  = "resolucion";
    const string K_IDIOMA      = "idioma";
    const string K_SENS_RATON  = "sens_raton";
    const string K_INVERT_Y    = "invert_y";
    const string K_FOV         = "fov";
    const string K_SUBTITULOS  = "subtitulos";
    const string K_VSYNC       = "vsync";

    // ── Valores por defecto ───────────────────────────────────────────────
    public static float VolMaster   { get => PlayerPrefs.GetFloat(K_VOL_MASTER,   1f);   set { PlayerPrefs.SetFloat(K_VOL_MASTER,   value); AplicarAudio(); } }
    public static float VolMusica   { get => PlayerPrefs.GetFloat(K_VOL_MUSICA,   0.7f); set { PlayerPrefs.SetFloat(K_VOL_MUSICA,   value); AplicarAudio(); } }
    public static float VolSFX      { get => PlayerPrefs.GetFloat(K_VOL_SFX,      0.8f); set { PlayerPrefs.SetFloat(K_VOL_SFX,      value); AplicarAudio(); } }
    public static float VolAmbiente { get => PlayerPrefs.GetFloat(K_VOL_AMBIENTE, 0.6f); set { PlayerPrefs.SetFloat(K_VOL_AMBIENTE, value); AplicarAudio(); } }
    public static int   Calidad     { get => PlayerPrefs.GetInt(K_CALIDAD,     3);        set { PlayerPrefs.SetInt(K_CALIDAD,     value); AplicarCalidad(value); } }
    public static bool  Fullscreen  { get => PlayerPrefs.GetInt(K_FULLSCREEN,  1) == 1;  set { PlayerPrefs.SetInt(K_FULLSCREEN,  value ? 1 : 0); Screen.fullScreen = value; } }
    public static int   Resolucion  { get => PlayerPrefs.GetInt(K_RESOLUCION,  0);        set { PlayerPrefs.SetInt(K_RESOLUCION,  value); AplicarResolucion(value); } }
    public static int   Idioma      { get => PlayerPrefs.GetInt(K_IDIOMA,      0);        set { PlayerPrefs.SetInt(K_IDIOMA,      value); } } // 0=ES 1=EU 2=EN
    public static float SensRaton   { get => PlayerPrefs.GetFloat(K_SENS_RATON, 2f);      set => PlayerPrefs.SetFloat(K_SENS_RATON, value); }
    public static bool  InvertirY   { get => PlayerPrefs.GetInt(K_INVERT_Y, 0) == 1;     set => PlayerPrefs.SetInt(K_INVERT_Y, value ? 1 : 0); }
    public static float FOV         { get => PlayerPrefs.GetFloat(K_FOV, 75f);            set { PlayerPrefs.SetFloat(K_FOV, value); AplicarFOV(value); } }
    public static bool  Subtitulos  { get => PlayerPrefs.GetInt(K_SUBTITULOS, 1) == 1;   set => PlayerPrefs.SetInt(K_SUBTITULOS, value ? 1 : 0); }
    public static bool  VSync       { get => PlayerPrefs.GetInt(K_VSYNC, 1) == 1;        set { PlayerPrefs.SetInt(K_VSYNC, value ? 1 : 0); QualitySettings.vSyncCount = value ? 1 : 0; } }

    // ── Resoluciones disponibles ──────────────────────────────────────────
    public static readonly (int w, int h, string label)[] RESOLUCIONES =
    {
        (1280, 720,  "1280×720  (HD)"),
        (1600, 900,  "1600×900"),
        (1920, 1080, "1920×1080 (Full HD)"),
        (2560, 1440, "2560×1440 (2K)"),
        (3840, 2160, "3840×2160 (4K)"),
    };

    // ── Nombres de calidad ────────────────────────────────────────────────
    public static readonly string[] NIVELES_CALIDAD =
        { "Bajo", "Medio", "Alto", "Ultra", "AAA+" };

    // ── Idiomas ───────────────────────────────────────────────────────────
    public static readonly string[] IDIOMAS = { "Español", "Euskara", "English" };

    // ── Controles (remapeables) ───────────────────────────────────────────
    public static readonly Dictionary<string, string> AccionesDefault = new()
    {
        ["Mover Adelante"]  = "w",
        ["Mover Atrás"]     = "s",
        ["Mover Izquierda"] = "a",
        ["Mover Derecha"]   = "d",
        ["Correr"]          = "leftShift",
        ["Agacharse"]       = "leftCtrl",
        ["Saltar"]          = "space",
        ["Usar/Entrar"]     = "e",
        ["Disparar"]        = "mouse0",
        ["Apuntar"]         = "mouse1",
        ["Graffiti"]        = "g",
        ["Manifestación"]   = "m",
        ["Bomba"]           = "b",
        ["Barricada"]       = "f",
        ["Clima"]           = "leftCtrl+w",
        ["Pausa"]           = "escape",
    };

    // Guardar/cargar control remapeado
    public static string GetControl(string accion)
        => PlayerPrefs.GetString("ctrl_" + accion,
           AccionesDefault.TryGetValue(accion, out var def) ? def : "");

    public static void SetControl(string accion, string tecla)
        => PlayerPrefs.SetString("ctrl_" + accion, tecla);

    public static void ResetControles()
    {
        foreach (var kv in AccionesDefault)
            PlayerPrefs.DeleteKey("ctrl_" + kv.Key);
    }

    // ── Aplicar todo al arrancar ──────────────────────────────────────────
    public static void AplicarTodo()
    {
        AplicarAudio();
        AplicarCalidad(Calidad);
        AplicarResolucion(Resolucion);
        AplicarFOV(FOV);
        QualitySettings.vSyncCount = VSync ? 1 : 0;
        Screen.fullScreen          = Fullscreen;
    }

    static void AplicarAudio()
    {
        AudioListener.volume = VolMaster;
        // SistemaMusica ajusta su volumen propio en Update via VolMusica
        // SonidosAmbiente idem via VolAmbiente
        // Los SFX puntales via VolSFX — ControladorJugador lo lee
    }

    static void AplicarCalidad(int idx)
    {
        idx = Mathf.Clamp(idx, 0, QualitySettings.names.Length - 1);
        QualitySettings.SetQualityLevel(idx, true);
    }

    static void AplicarResolucion(int idx)
    {
        if (idx < 0 || idx >= RESOLUCIONES.Length) return;
        var (w, h, _) = RESOLUCIONES[idx];
        Screen.SetResolution(w, h, Fullscreen);
    }

    static void AplicarFOV(float fov)
    {
        var cam = Camera.main;
        if (cam != null) cam.fieldOfView = fov;
    }

    public static void Guardar() => PlayerPrefs.Save();
}
