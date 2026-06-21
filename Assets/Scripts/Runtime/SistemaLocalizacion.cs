// Assets/Scripts/Runtime/SistemaLocalizacion.cs
// ═══════════════════════════════════════════════════════════════════════════
//  LOCALIZACIÓN — ES (base) / EU (euskera) / EN.
//
//  · API estática:  SistemaLocalizacion.L("hud_dinero")  → texto en el idioma
//    actual (con fallback a ES, y a la propia clave si no existe).
//  · Tabla base embebida (ES) para que funcione sin assets. Se puede ampliar
//    con Resources/Localizacion/strings.json (formato: lista de {clave,es,eu,en}).
//  · Idioma persistente en PlayerPrefs. Evento AlCambiarIdioma para refrescar UI.
//  · Componente TextoLocalizado: pega una clave a un UGUI Text y se traduce solo.
//
//  Capa RUNTIME. Auto-arranque del singleton; sin montaje en escena.
// ═══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using UnityEngine;

public enum Idioma { ES = 0, EU = 1, EN = 2 }

[DefaultExecutionOrder(-150)]
public sealed class SistemaLocalizacion : MonoBehaviour
{
    public static SistemaLocalizacion I { get; private set; }
    public static event Action AlCambiarIdioma;
    public Idioma Actual { get; private set; } = Idioma.ES;

    const string PREF = "idioma";

    [Serializable] struct Fila { public string clave, es, eu, en; }
    [Serializable] class Tabla { public List<Fila> filas = new(); }

    readonly Dictionary<string, string[]> _t = new();   // clave → [es, eu, en]

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (I != null) return;
        var go = new GameObject("SistemaLocalizacion");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaLocalizacion>();
    }

    void Awake()
    {
        if (I != null) { Destroy(gameObject); return; }
        I = this;
        CargarBaseES();
        CargarDesdeResources();
        Actual = (Idioma)PlayerPrefs.GetInt(PREF, (int)Idioma.ES);
    }

    // ── API ───────────────────────────────────────────────────────────────
    public static string L(string clave)
    {
        if (I == null || string.IsNullOrEmpty(clave)) return clave;
        if (!I._t.TryGetValue(clave, out var v)) return clave;
        int idx = (int)I.Actual;
        string s = (idx < v.Length) ? v[idx] : null;
        if (string.IsNullOrEmpty(s)) s = v.Length > 0 ? v[0] : clave;   // fallback ES
        return string.IsNullOrEmpty(s) ? clave : s;
    }

    public void CambiarIdioma(Idioma idioma)
    {
        if (Actual == idioma) return;
        Actual = idioma;
        PlayerPrefs.SetInt(PREF, (int)idioma);
        AlCambiarIdioma?.Invoke();
    }

    public void Registrar(string clave, string es, string eu = null, string en = null)
        => _t[clave] = new[] { es, eu, en };

    // ── Carga ─────────────────────────────────────────────────────────────
    void CargarDesdeResources()
    {
        var ta = Resources.Load<TextAsset>("Localizacion/strings");
        if (ta == null) return;
        try
        {
            var tabla = JsonUtility.FromJson<Tabla>(ta.text);
            if (tabla?.filas != null)
                foreach (var f in tabla.filas)
                    if (!string.IsNullOrEmpty(f.clave)) _t[f.clave] = new[] { f.es, f.eu, f.en };
        }
        catch (Exception e) { Debug.LogWarning($"[Localizacion] strings.json ilegible: {e.Message}"); }
    }

    void CargarBaseES()
    {
        // clave, ES, EU, EN
        Reg("hud_dinero",      "Dinero",            "Dirua",            "Money");
        Reg("hud_apoyo",       "Apoyo popular",     "Herri babesa",     "Public support");
        Reg("hud_busqueda",    "Búsqueda",          "Bilaketa",         "Wanted");
        Reg("hud_salud",       "Salud",             "Osasuna",          "Health");
        Reg("ui_guardado",     "Partida guardada",  "Partida gordeta",  "Game saved");
        Reg("ui_cargado",      "Partida cargada",   "Partida kargatuta","Game loaded");
        Reg("ui_continuar",    "Continuar",         "Jarraitu",         "Continue");
        Reg("ui_salir",        "Salir",             "Irten",            "Quit");
        Reg("ui_ajustes",      "Ajustes",           "Ezarpenak",        "Settings");
        Reg("ui_inventario",   "Inventario",        "Inbentarioa",      "Inventory");
        Reg("ui_objetivo",     "Objetivo",          "Helburua",         "Objective");
        Reg("ui_interactuar",  "Interactuar",       "Elkarreragin",     "Interact");
        Reg("ui_entrar_coche", "Entrar",            "Sartu",            "Enter");
        Reg("misc_pausa",      "Pausa",             "Pausa",            "Paused");
    }
    void Reg(string c, string es, string eu, string en) => _t[c] = new[] { es, eu, en };
}

/// <summary>Pega una clave de localización a un UGUI Text; se traduce y refresca al cambiar idioma.</summary>
[RequireComponent(typeof(UnityEngine.UI.Text))]
public sealed class TextoLocalizado : MonoBehaviour
{
    [SerializeField] string clave;
    UnityEngine.UI.Text _txt;

    void Awake() => _txt = GetComponent<UnityEngine.UI.Text>();
    void OnEnable()  { SistemaLocalizacion.AlCambiarIdioma += Refrescar; Refrescar(); }
    void OnDisable() => SistemaLocalizacion.AlCambiarIdioma -= Refrescar;
    public void SetClave(string c) { clave = c; Refrescar(); }
    void Refrescar() { if (_txt != null) _txt.text = SistemaLocalizacion.L(clave); }
}
