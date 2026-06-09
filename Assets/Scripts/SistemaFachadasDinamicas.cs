// Assets/Scripts/SistemaFachadasDinamicas.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE FACHADAS DINÁMICAS — envejecimiento y clima en edificios
//
//  El Arquitecto: los edificios son estáticos visualmente. Rockastar hace
//  que cada superficie reaccione al clima (lluvia oscurece y abrillantar
//  las fachadas) y envejezca gradualmente (suciedad acumulada en salientes).
//
//  Implementa tres efectos via MaterialPropertyBlock (sin instanciar mats):
//    1. Humedad (lluvia):  smoothness +0.3, albedo ×0.82 (más oscuro/brillante)
//    2. Suciedad (tiempo): albedo hacia gris en los últimos 15% de cada fachada
//    3. Grafiti visible:   emissive tint en fachadas con _GlobalNightLevel > 0.5
//
//  Lee _GlobalWetness, _GlobalNightLevel y SistemaClima cada 10 segundos.
//  Opera sobre los renderers de SistemaEdificiosAAA.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(90)]
public class SistemaFachadasDinamicas : MonoBehaviour
{
    public static SistemaFachadasDinamicas Instance { get; private set; }

    [Range(0f, 1f)] public float intensidadSuciedad   = 0.35f;
    [Range(0f, 1f)] public float intensidadHumedad     = 0.28f;
    public float intervaloActualizacion                = 10f;

    // IDs de propiedades
    static readonly int ID_BaseColor  = Shader.PropertyToID("_BaseColor");
    static readonly int ID_Smoothness = Shader.PropertyToID("_Smoothness");
    static readonly int ID_Emissive   = Shader.PropertyToID("_EmissiveColor");

    readonly List<Renderer> _fachadasRend = new(512);
    readonly MaterialPropertyBlock _mpb   = new();

    // Estado por fachada (Color original + smoothness original)
    struct FachadaInfo { public Color colorBase; public float smoothBase; }
    FachadaInfo[] _info;

    float _lastWet   = -1f;
    float _lastNight = -1f;
    float _lastSnow  = -1f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start() => StartCoroutine(InicializarTras(9f));

    IEnumerator InicializarTras(float delay)
    {
        yield return new WaitForSeconds(delay);
        yield return StartCoroutine(IndexarFachadas());
        StartCoroutine(CicloActualizacion());
    }

    IEnumerator IndexarFachadas()
    {
        string[] padres = { "Edificios_OSM", "Edificios_Precisos", "EdificiosAAA" };
        foreach (var nombre in padres)
        {
            var go = GameObject.Find(nombre);
            if (go == null) continue;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                // Excluir ventanas (tienen emissive propio) y objetos muy pequeños
                string n = r.gameObject.name.ToLower();
                if (n.Contains("vidrio") || n.Contains("ventana") ||
                    n.Contains("glass")  || n.Contains("roof"))
                    continue;
                _fachadasRend.Add(r);
            }
            yield return null;
        }

        // Capturar color/smoothness base de cada renderer
        _info = new FachadaInfo[_fachadasRend.Count];
        for (int i = 0; i < _fachadasRend.Count; i++)
        {
            var mat = _fachadasRend[i].sharedMaterial;
            float sm = mat != null && mat.HasProperty(ID_Smoothness)
                     ? mat.GetFloat(ID_Smoothness) : 0.2f;
            Color col = mat != null && mat.HasProperty(ID_BaseColor)
                      ? mat.GetColor(ID_BaseColor) : Color.white;
            _info[i] = new FachadaInfo { colorBase = col, smoothBase = sm };
            if (i % 100 == 0) yield return null;
        }

        AlsasuaLogger.Info("FachadasDin", $"{_fachadasRend.Count} fachadas indexadas.");
    }

    IEnumerator CicloActualizacion()
    {
        while (true)
        {
            yield return new WaitForSeconds(intervaloActualizacion);

            float wetness   = Shader.GetGlobalFloat("_GlobalWetness");
            float nightLevel= Shader.GetGlobalFloat("_GlobalNightLevel");
            float snowLevel = Shader.GetGlobalFloat("_GlobalSnowLevel");

            bool wChanged = Mathf.Abs(wetness    - _lastWet)   > 0.06f;
            bool nChanged = Mathf.Abs(nightLevel - _lastNight) > 0.05f;
            bool sChanged = Mathf.Abs(snowLevel  - _lastSnow)  > 0.05f;
            if (!wChanged && !nChanged && !sChanged) continue;

            _lastWet   = wetness;
            _lastNight = nightLevel;
            _lastSnow  = snowLevel;

            yield return StartCoroutine(AplicarEfectos(wetness, nightLevel, snowLevel));
        }
    }

    IEnumerator AplicarEfectos(float wetness, float nightLevel, float snowLevel)
    {
        if (_info == null) yield break;

        // Suciedad: acumulada de forma determinista por índice (simula envejecimiento)
        // No usamos Random — el patrón debe ser estable entre frames.
        for (int i = 0; i < _fachadasRend.Count; i++)
        {
            var r = _fachadasRend[i];
            if (r == null) continue;

            var info = _info[i];

            // Suciedad: valor 0-1 seeded por índice
            float suciedad = (Mathf.PerlinNoise(i * 0.13f, i * 0.07f)) * intensidadSuciedad;

            // Humedad: oscurece + abrillantar al llover
            float humedadFactor = wetness * intensidadHumedad;

            // Color final: base → más oscuro por suciedad y humedad
            Color col = info.colorBase;
            col.r = Mathf.Max(0f, col.r * (1f - suciedad * 0.25f) * (1f - humedadFactor * 0.20f));
            col.g = Mathf.Max(0f, col.g * (1f - suciedad * 0.25f) * (1f - humedadFactor * 0.18f));
            col.b = Mathf.Max(0f, col.b * (1f - suciedad * 0.20f) * (1f - humedadFactor * 0.15f));

            // Smoothness: más alta cuando está mojado (refleja el entorno)
            float sm = Mathf.Clamp01(info.smoothBase + wetness * 0.28f);

            // Nieve: empolva la fachada hacia blanco y la vuelve mate.
            // Las superficies verticales acumulan poco → factor 0.55. Sustituye al
            // shader de nieve que no existe; usa el mismo _GlobalSnowLevel del terreno.
            if (snowLevel > 0.01f)
            {
                float nieve = Mathf.Clamp01(snowLevel) * 0.55f;
                col = Color.Lerp(col, new Color(0.92f, 0.94f, 0.97f), nieve);
                sm  = Mathf.Lerp(sm, 0.05f, nieve);   // la nieve es mate, no brilla
            }

            // Emissive grafiti de noche: leve tinte rojo en fachadas con grafiti (cada ~7)
            Color emissive = Color.black;
            if (nightLevel > 0.5f && i % 7 == 0)
            {
                float intensNoche = Mathf.InverseLerp(0.5f, 1.0f, nightLevel);
                emissive = new Color(0.08f, 0.02f, 0.02f) * intensNoche;
            }

            _mpb.Clear();
            _mpb.SetColor(ID_BaseColor,  col);
            _mpb.SetFloat(ID_Smoothness, sm);
            if (emissive != Color.black) _mpb.SetColor(ID_Emissive, emissive);
            r.SetPropertyBlock(_mpb);

            if (i % 80 == 0) yield return null;
        }
    }

    void OnDestroy() { if (Instance == this) Instance = null; }
}
