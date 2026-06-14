// Assets/Scripts/Core/Simulacion/ServicioTelemetriaFrames.cs
// ═══════════════════════════════════════════════════════════════════════════
//  TELEMETRÍA DE FRAMES — ITelemetryService sobre FrameTimingManager (Unity 6)
//
//  Da el CPU frame-time REAL (no solo Time.deltaTime, que incluye el VSync wait) y
//  lo suaviza con una EMA. El orquestador lo muestrea una vez por frame al principio
//  de su tick. Si el backend de timings no está disponible (algunas plataformas /
//  primeros frames), cae a Time.unscaledDeltaTime → siempre devuelve algo razonable.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

public sealed class ServicioTelemetriaFrames : ITelemetryService
{
    readonly float _alpha;
    readonly FrameTiming[] _buf = new FrameTiming[1];
    float _ema;

    public float PresupuestoMs    { get; }
    public float FrameMsSuavizado => _ema;

    public ServicioTelemetriaFrames(float alpha, float presupuestoMs)
    {
        _alpha = Mathf.Clamp01(alpha);
        PresupuestoMs = presupuestoMs;
        _ema = presupuestoMs;   // semilla neutra: arranca "en presupuesto"
    }

    /// <summary>Lo llama el orquestador una vez por frame.</summary>
    public void Muestrear()
    {
        FrameTimingManager.CaptureFrameTimings();
        uint n = FrameTimingManager.GetLatestTimings(1, _buf);
        float ms = (n > 0 && _buf[0].cpuFrameTime > 0.0)
            ? (float)_buf[0].cpuFrameTime
            : Time.unscaledDeltaTime * 1000f;   // fallback robusto
        _ema = Mathf.Lerp(_ema, ms, _alpha);
    }
}
