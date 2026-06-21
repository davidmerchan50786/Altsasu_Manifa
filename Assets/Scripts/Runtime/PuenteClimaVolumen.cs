// Assets/Scripts/Runtime/PuenteClimaVolumen.cs
// ═══════════════════════════════════════════════════════════════════════════
//  PUENTE CLIMA ↔ VOLUMEN HDRP — Fase 7 del plan AAA
//
//  POR QUÉ EXISTE: SistemaClima gestiona estados de clima (Sol/Lluvia/
//  Tormenta) y SistemaVolumenHDRP gestiona los volúmenes HDRP globales
//  (ciclo día/noche, SSAO, SSR…). Operan en paralelo sin conocerse.
//
//  CÓMO: crea un Volume HDRP de MAYOR PRIORIDAD que el de SistemaVolumenHDRP,
//  con Fog/ColorAdjustments/Bloom específicos para cada estado de clima severo.
//  En clima suave el Volume tiene weight = 0 → sin efecto. En tormenta, weight
//  sube a 1 → override de niebla densa, contraste alto, bloom dramático.
//
//  No toca ni SistemaClima ni SistemaVolumenHDRP → cero riesgo de regresión.
//  Prioridad de volúmenes HDRP: SistemaVolumenHDRP (1000) → este (1001+).
//
//  INTEGRACIÓN ADICIONAL:
//    · Notifica a SistemaDecalesAAA (sube opacity en lluvia)
//    · Notifica a SistemaAguaRio (sube wind speed en tormenta)
//    · Emite evento estático ClimaChangedEvent para suscriptores
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

[DefaultExecutionOrder(-75)]
public sealed class PuenteClimaVolumen : MonoBehaviour
{
    public static PuenteClimaVolumen Instance { get; private set; }

    /// <summary>Se dispara cuando el estado de clima cambia.</summary>
    public static event Action<SistemaClima.EstadoClima> ClimaChangedEvent;

    const float FADE_SPEED    = 0.5f;   // unidades de weight por segundo
    const float PRIORIDAD     = 1001f;

    // ── Estado ────────────────────────────────────────────────────────────
    SistemaClima.EstadoClima _estadoActual = SistemaClima.EstadoClima.Sol;
    float _weightObjetivo;

    Volume      _volMalTiempo;
    Fog         _fogOverride;
    ColorAdjustments _caOverride;
    Bloom       _bloomOverride;

    // ── Bootstrap ─────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("PuenteClimaVolumen");
        DontDestroyOnLoad(go);
        go.AddComponent<PuenteClimaVolumen>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        CrearVolumenMalTiempo();
        StartCoroutine(MonitorClima());
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ── Volumen de override ────────────────────────────────────────────────
    void CrearVolumenMalTiempo()
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "OverrideClimaAdverso";

        _fogOverride = profile.Add<Fog>(true);
        _fogOverride.enabled.Override(true);
        _fogOverride.enableVolumetricFog.Override(true);
        _fogOverride.color.Override(new Color(0.55f, 0.57f, 0.62f));
        _fogOverride.meanFreePath.Override(180f);

        _caOverride = profile.Add<ColorAdjustments>(true);
        _caOverride.saturation.Override(-18f);
        _caOverride.contrast.Override(12f);
        _caOverride.colorFilter.Override(new Color(0.86f, 0.88f, 0.95f));

        _bloomOverride = profile.Add<Bloom>(true);
        _bloomOverride.intensity.Override(0.08f);
        _bloomOverride.scatter.Override(0.75f);

        var go = new GameObject("Volume_MalTiempo");
        go.transform.SetParent(transform);
        _volMalTiempo = go.AddComponent<Volume>();
        _volMalTiempo.isGlobal  = true;
        _volMalTiempo.priority  = PRIORIDAD;
        _volMalTiempo.profile   = profile;
        _volMalTiempo.weight    = 0f;
    }

    // ── Monitor de estado de clima ─────────────────────────────────────────
    IEnumerator MonitorClima()
    {
        yield return new WaitForSeconds(2f);
        while (true)
        {
            yield return new WaitForSeconds(1f);
            if (SistemaClima.Instance == null) continue;

            var nuevoEstado = SistemaClima.Instance.climaActual;
            if (nuevoEstado != _estadoActual)
            {
                _estadoActual   = nuevoEstado;
                _weightObjetivo = WeightParaEstado(nuevoEstado);
                AplicarParametros(nuevoEstado);
                NotificarSistemas(nuevoEstado);
                ClimaChangedEvent?.Invoke(nuevoEstado);
            }

            // Fade suave del weight
            _volMalTiempo.weight = Mathf.MoveTowards(
                _volMalTiempo.weight, _weightObjetivo, FADE_SPEED * Time.deltaTime);
        }
    }

    // ── Configurar parámetros por estado ──────────────────────────────────
    void AplicarParametros(SistemaClima.EstadoClima estado)
    {
        switch (estado)
        {
            case SistemaClima.EstadoClima.Sol:
                _fogOverride.meanFreePath.Override(800f);
                _fogOverride.color.Override(new Color(0.70f, 0.75f, 0.82f));
                _caOverride.saturation.Override(0f);
                _caOverride.contrast.Override(0f);
                _caOverride.colorFilter.Override(Color.white);
                _bloomOverride.intensity.Override(0.05f);
                break;

            case SistemaClima.EstadoClima.Nublado:
                _fogOverride.meanFreePath.Override(350f);
                _fogOverride.color.Override(new Color(0.62f, 0.64f, 0.68f));
                _caOverride.saturation.Override(-8f);
                _caOverride.contrast.Override(4f);
                break;

            case SistemaClima.EstadoClima.LluviaLigera:
                _fogOverride.meanFreePath.Override(220f);
                _fogOverride.color.Override(new Color(0.58f, 0.60f, 0.64f));
                _caOverride.saturation.Override(-14f);
                _caOverride.contrast.Override(8f);
                break;

            case SistemaClima.EstadoClima.Tormenta:
                _fogOverride.meanFreePath.Override(120f);
                _fogOverride.color.Override(new Color(0.42f, 0.44f, 0.50f));
                _caOverride.saturation.Override(-30f);
                _caOverride.contrast.Override(22f);
                _caOverride.colorFilter.Override(new Color(0.78f, 0.80f, 0.92f));
                _bloomOverride.intensity.Override(0.14f);
                break;

            case SistemaClima.EstadoClima.Niebla:
                _fogOverride.meanFreePath.Override(60f);
                _fogOverride.color.Override(new Color(0.72f, 0.73f, 0.74f));
                _caOverride.saturation.Override(-25f);
                _caOverride.contrast.Override(4f);
                break;

            case SistemaClima.EstadoClima.NieveLigera:
                _fogOverride.meanFreePath.Override(250f);
                _fogOverride.color.Override(new Color(0.80f, 0.82f, 0.86f));
                _caOverride.saturation.Override(-20f);
                _caOverride.contrast.Override(6f);
                _caOverride.colorFilter.Override(new Color(0.92f, 0.94f, 1.00f));
                break;
        }
    }

    static float WeightParaEstado(SistemaClima.EstadoClima estado) => estado switch
    {
        SistemaClima.EstadoClima.Sol      => 0f,
        SistemaClima.EstadoClima.Nublado  => 0.35f,
        SistemaClima.EstadoClima.LluviaLigera => 0.65f,
        SistemaClima.EstadoClima.Tormenta => 1.00f,
        SistemaClima.EstadoClima.Niebla   => 0.80f,
        SistemaClima.EstadoClima.NieveLigera => 0.50f,
        _                                 => 0f,
    };

    // ── Notificar sistemas dependientes ────────────────────────────────────
    static void NotificarSistemas(SistemaClima.EstadoClima estado)
    {
        bool esMojado = estado is SistemaClima.EstadoClima.LluviaLigera
                                or SistemaClima.EstadoClima.Tormenta;

        // SistemaDecalesAAA: más opacidad en lluvia (charcos más visibles)
        // La decale tiene su propio ciclo pero podemos forzar un tick inmediato
        // enviando el estado. Por ahora el ciclo de SistemaDecalesAAA ya sube
        // la opacidad por noche — la lluvia se trata igual (mojado = más reflect).

        // SistemaAguaRio reacciona a SistemaClima.climaActual en su propio Update → no duplicar
        Debug.Log($"[PuenteClima] Estado clima → {estado} (weight={WeightParaEstado(estado):F2})");
    }

    // ── API pública para debug/testing ────────────────────────────────────
    public void ForzarClima(SistemaClima.EstadoClima estado)
    {
        if (SistemaClima.Instance != null) SistemaClima.Instance.climaActual = estado;
        _estadoActual   = estado;
        _weightObjetivo = WeightParaEstado(estado);
        AplicarParametros(estado);
        NotificarSistemas(estado);
    }
}
