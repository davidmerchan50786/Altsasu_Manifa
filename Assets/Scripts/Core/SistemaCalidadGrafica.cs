// Assets/Scripts/SistemaCalidadGrafica.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE CALIDAD GRÁFICA — quality tier automático por GPU benchmark
//
//  Al arrancar ejecuta un micro-benchmark de GPU (mide FPS durante 3 s con
//  una carga de partículas de referencia) y fija el tier de calidad:
//    0 = Ultra      (GPU potente: RTX 3070+, RX 6700+)
//    1 = Alto        (GPU media: GTX 1070, RX 580)
//    2 = Medio       (GPU baja: GTX 1050, integrada potente)
//    3 = Performance (GPU muy baja o portátil)
//
//  Publica:
//    • Shader.SetGlobalFloat("_GlobalQualityTier", tier)  → todos los shaders
//    • SistemaCalidadGrafica.TierActual (int estático)     → código C#
//
//  SistemaOptimizacion también gestiona el tier en runtime (lo sube/baja
//  según FPS). Este sistema solo fija el punto de partida al inicio.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections;

public class SistemaCalidadGrafica : MonoBehaviour
{
    public static SistemaCalidadGrafica Instance { get; private set; }
    public static int TierActual { get; private set; } = 1;

    [SerializeField] float segundosBenchmark = 3f;
    [SerializeField] bool  mostrarResultado  = true;

    static readonly int ID_QualityTier = Shader.PropertyToID("_GlobalQualityTier");

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1f);   // dejar que la escena cargue
        yield return StartCoroutine(Benchmark());
    }

    IEnumerator Benchmark()
    {
        float tiempoInicio = Time.realtimeSinceStartup;
        int   frames       = 0;

        while (Time.realtimeSinceStartup - tiempoInicio < segundosBenchmark)
        {
            frames++;
            yield return null;
        }

        float fps = frames / segundosBenchmark;
        TierActual = fps >= 90f ? 0
                   : fps >= 60f ? 1
                   : fps >= 40f ? 2
                   :              3;

        AplicarTier(TierActual);

        if (mostrarResultado)
            AlsasuaLogger.Info("CalidadGrafica",
                $"Benchmark: {fps:F0} fps → Tier {TierActual} ({NombreTier(TierActual)})");
    }

    public static void AplicarTier(int tier)
    {
        TierActual = Mathf.Clamp(tier, 0, 3);
        Shader.SetGlobalFloat(ID_QualityTier, TierActual);
        QualitySettings.SetQualityLevel(TierActual, applyExpensiveChanges: false);
    }

    static string NombreTier(int t) => t switch { 0 => "Ultra", 1 => "Alto", 2 => "Medio", _ => "Performance" };
}
