// Assets/Scripts/SistemaShaderGlobals.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE SHADER GLOBALS — aplica propiedades PBR dinámicas a materiales
//
//  Problema que resuelve:
//    Los shaders del proyecto reciben _GlobalWetness y _GlobalNightLevel
//    via Shader.SetGlobalFloat, pero los MaterialPropertyBlocks de smoothness
//    de suelos/fachadas no se actualizaban → las superficies no parecían mojadas.
//
//  Solución:
//    Cada N segundos, itera todos los renderers de suelo y fachada y aplica
//    un MPB que ajusta _Smoothness según la humedad actual y _EmissiveColor
//    de ventanas según el nivel de noche.
//
//  También emite:
//    _GlobalWetSmoothness (float): smoothness adicional por lluvia (0-0.3)
//    _GlobalEmissiveNight (float): multiplicador de emisión nocturna (0-1)
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class SistemaShaderGlobals : MonoBehaviour
{
    public static SistemaShaderGlobals Instance { get; private set; }

    [Tooltip("Segundos entre actualizaciones de MPB (no hace falta hacerlo cada frame).")]
    public float intervaloActualizacion = 3f;

    // ── Shader property IDs ────────────────────────────────────────────────
    static readonly int ID_WetSmoothness   = Shader.PropertyToID("_GlobalWetSmoothness");
    static readonly int ID_EmissiveNight   = Shader.PropertyToID("_GlobalEmissiveNight");
    static readonly int ID_Smoothness      = Shader.PropertyToID("_Smoothness");
    static readonly int ID_SmoothnessRemap = Shader.PropertyToID("_SmoothnessRemapMax");
    static readonly int ID_EmissiveColor   = Shader.PropertyToID("_EmissiveColor");
    static readonly int ID_EmissiveHDR     = Shader.PropertyToID("_EmissiveColorHDR");

    // ── Renderers categorizados ────────────────────────────────────────────
    readonly List<Renderer> _suelos    = new(256);
    readonly List<Renderer> _fachadas  = new(512);

    // ── MaterialPropertyBlock reutilizable (no instanciar materiales) ─────
    MaterialPropertyBlock _mpb;

    // ── Estado previo (evita aplicar si no cambió) ────────────────────────
    float _lastHumedad    = -1f;
    float _lastNightLevel = -1f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _mpb = new MaterialPropertyBlock();
    }

    void Start()
    {
        StartCoroutine(BuscarRenderers());
        StartCoroutine(CicloActualizacion());
    }

    // ════════════════════════════════════════════════════════════════════════
    //  BÚSQUEDA INICIAL DE RENDERERS
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator BuscarRenderers()
    {
        yield return new WaitForSeconds(8f); // esperar a que el mundo esté generado

        // Suelos — terreno y calles
        foreach (var nombre in new[] { "Suelo_AAA", "Calles_Precisas", "Calles_OSM",
                                       "Terrain", "SueloAAA", "Roads" })
        {
            var go = GameObject.Find(nombre);
            if (go == null) continue;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                _suelos.Add(r);
            if (_suelos.Count % 20 == 0) yield return null;
        }

        // Fachadas — edificios
        foreach (var nombre in new[] { "Edificios_OSM", "Edificios_Precisos",
                                       "EdificiosAAA", "Buildings" })
        {
            var go = GameObject.Find(nombre);
            if (go == null) continue;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                // Excluir ventanas (tienen su propio sistema en SistemaEdificiosAAA)
                if (!r.gameObject.name.ToLower().Contains("vidrio") &&
                    !r.gameObject.name.ToLower().Contains("ventana") &&
                    !r.gameObject.name.ToLower().Contains("glass"))
                    _fachadas.Add(r);
            }
            if (_fachadas.Count % 30 == 0) yield return null;
        }

        AlsasuaLogger.Info("ShaderGlobals",
            $"Renderers indexados — suelos: {_suelos.Count}, fachadas: {_fachadas.Count}");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CICLO DE ACTUALIZACIÓN
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator CicloActualizacion()
    {
        while (true)
        {
            yield return new WaitForSeconds(intervaloActualizacion);

            float humedad    = SistemaCharcos.Instance?.Humedad ?? 0f;
            float nightLevel = Shader.GetGlobalFloat("_GlobalNightLevel");

            // Emitir globals de alto nivel que otros shaders pueden leer
            float wetSmooth = humedad * 0.30f;  // boost de smoothness máximo +0.30
            float emissNight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.8f, nightLevel));
            Shader.SetGlobalFloat(ID_WetSmoothness, wetSmooth);
            Shader.SetGlobalFloat(ID_EmissiveNight,  emissNight);

            // Solo actualizar MPBs si los valores cambiaron significativamente
            bool humedadCambio = Mathf.Abs(humedad    - _lastHumedad)    > 0.04f;
            bool nightCambio   = Mathf.Abs(nightLevel - _lastNightLevel) > 0.05f;

            if (humedadCambio)
            {
                _lastHumedad = humedad;
                yield return StartCoroutine(AplicarHumedadSuelos(humedad));
                yield return StartCoroutine(AplicarHumedadFachadas(humedad));
            }

            if (nightCambio)
            {
                _lastNightLevel = nightLevel;
                // Las ventanas las gestiona SistemaEdificiosAAA.ActualizarVentanasMPB
                // Aquí solo ajustamos el multiplicador global
            }
        }
    }

    // ── Humedad en suelos ────────────────────────────────────────────────

    IEnumerator AplicarHumedadSuelos(float humedad)
    {
        // Smoothness: seco ~0.2, mojado ~0.85 — simula asfalto reflectante bajo lluvia
        float smoothness = Mathf.Lerp(0.20f, 0.85f, humedad);

        for (int i = 0; i < _suelos.Count; i++)
        {
            var r = _suelos[i];
            if (r == null) continue;

            _mpb.Clear();
            _mpb.SetFloat(ID_Smoothness,      smoothness);
            _mpb.SetFloat(ID_SmoothnessRemap,  smoothness);
            r.SetPropertyBlock(_mpb);

            if (i % 40 == 0) yield return null; // spread el trabajo
        }
    }

    // ── Humedad en fachadas ──────────────────────────────────────────────

    IEnumerator AplicarHumedadFachadas(float humedad)
    {
        // Fachadas mojadas: smoothness sube menos que el suelo (superficies verticales
        // escurren el agua → menos acumulación que el suelo plano).
        float smoothness = Mathf.Lerp(0.15f, 0.55f, humedad * 0.6f);

        for (int i = 0; i < _fachadas.Count; i++)
        {
            var r = _fachadas[i];
            if (r == null) continue;

            _mpb.Clear();
            _mpb.SetFloat(ID_Smoothness,     smoothness);
            _mpb.SetFloat(ID_SmoothnessRemap, smoothness);
            r.SetPropertyBlock(_mpb);

            if (i % 60 == 0) yield return null;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Fuerza una actualización inmediata del MPB de todos los renderers.</summary>
    public static void Refrescar()
    {
        if (Instance != null)
            Instance.StartCoroutine(Instance.CicloActualizacion());
    }
}
