// Assets/Scripts/_ParanoiaGC~/SistemaParanoiaGuardiaCivil.cs  (STAGING/DRAFT — carpeta ~ no se compila)
// ─────────────────────────────────────────────────────────────────────────────
//  Orquesta la conversión NPC→Guardia Civil y coche→patrulla según la paranoia.
//  Se suscribe a SistemaApoyoPopular.OnParanoiaCambia. Convierte/revierte GRADUAL
//  (ritmoPorSegundo) y preferentemente OFF-SCREEN para que no se vea el morph.
//  No instancia ni destruye nada: solo conmuta convertibles ya presentes.
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections.Generic;
using UnityEngine;

public class SistemaParanoiaGuardiaCivil : MonoBehaviour
{
    public static SistemaParanoiaGuardiaCivil Instance { get; private set; }
    public ParanoiaGCConfig config;

    static readonly List<ConvertibleGuardiaCivil> _todos = new();
    public static void Registrar(ConvertibleGuardiaCivil c)   { if (!_todos.Contains(c)) _todos.Add(c); }
    public static void Desregistrar(ConvertibleGuardiaCivil c) => _todos.Remove(c);

    float _paranoia;
    float _acum;   // acumulador de ritmo

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnEnable()
    {
        SistemaApoyoPopular.OnParanoiaCambia += OnParanoia;
        SistemaApoyoPopular.OnParanoiaCritica += OnCritica;
        // estado inicial
        if (SistemaApoyoPopular.Instance != null) _paranoia = SistemaApoyoPopular.Instance.paranoia;
    }

    void OnDisable()
    {
        SistemaApoyoPopular.OnParanoiaCambia -= OnParanoia;
        SistemaApoyoPopular.OnParanoiaCritica -= OnCritica;
    }

    void OnParanoia(float p) => _paranoia = p;
    void OnCritica() => AlsasuaLogger.Info("ParanoiaGC", "Paranoia crítica → oleada de tricornios.");

    void Update()
    {
        if (config == null || _todos.Count == 0) return;

        int objNpc    = config.Objetivo(_paranoia, config.maxNpc);
        int objCoche  = config.Objetivo(_paranoia, config.maxCoches);

        int curNpc = 0, curCoche = 0;
        for (int i = 0; i < _todos.Count; i++)
            if (_todos[i] && _todos[i].Convertido) { if (_todos[i].esCoche) curCoche++; else curNpc++; }

        // Ritmo: como mucho N pasos por frame según ritmoPorSegundo.
        _acum += config.ritmoPorSegundo * Time.deltaTime;
        int pasos = Mathf.FloorToInt(_acum);
        if (pasos <= 0) return;
        _acum -= pasos;

        for (int s = 0; s < pasos; s++)
        {
            bool hizo = PasoConversion(false, curNpc, objNpc) || PasoConversion(true, curCoche, objCoche);
            if (!hizo) break;
            // recontar barato: ajustamos contadores locales
            curNpc = 0; curCoche = 0;
            for (int i = 0; i < _todos.Count; i++)
                if (_todos[i] && _todos[i].Convertido) { if (_todos[i].esCoche) curCoche++; else curNpc++; }
        }
    }

    /// <summary>Da un paso hacia el objetivo para NPCs (esCoche=false) o coches (true).</summary>
    bool PasoConversion(bool coche, int actual, int objetivo)
    {
        if (actual < objetivo)   // convertir uno (preferir off-screen)
        {
            var c = ElegirNoConvertidoOffscreen(coche);
            if (c != null) { c.Convertir(config); return true; }
        }
        else if (actual > objetivo)  // revertir uno (preferir off-screen)
        {
            var c = ElegirConvertidoOffscreen(coche);
            if (c != null) { c.Revertir(); return true; }
        }
        return false;
    }

    ConvertibleGuardiaCivil ElegirNoConvertidoOffscreen(bool coche)
    {
        ConvertibleGuardiaCivil visibleFallback = null;
        foreach (var c in _todos)
            if (c && c.esCoche == coche && !c.Convertido)
            {
                if (!c.VisibleEnCamara()) return c;   // ideal: fuera de cámara
                visibleFallback ??= c;
            }
        return visibleFallback;   // si no hay off-screen, último recurso
    }

    ConvertibleGuardiaCivil ElegirConvertidoOffscreen(bool coche)
    {
        ConvertibleGuardiaCivil visibleFallback = null;
        foreach (var c in _todos)
            if (c && c.esCoche == coche && c.Convertido)
            {
                if (!c.VisibleEnCamara()) return c;
                visibleFallback ??= c;
            }
        return visibleFallback;
    }
}
