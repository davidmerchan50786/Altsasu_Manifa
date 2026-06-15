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
    float _emaGpu;

    public float PresupuestoMs    { get; }
    public float FrameMsSuavizado => _ema;
    // GPU frame time suavizado. 0 mientras el backend no lo reporte (primeros frames,
    // o plataforma/Player Settings sin "GPU timing"). El gobernador de render trata el
    // 0 como "GPU sin dato" y cae a la señal de CPU/frame → nunca degrada por un cero.
    public float GpuMsSuavizado   => _emaGpu;

    public ServicioTelemetriaFrames(float alpha, float presupuestoMs)
    {
        _alpha = Mathf.Clamp01(alpha);
        PresupuestoMs = presupuestoMs;
        _ema = presupuestoMs;   // semilla neutra: arranca "en presupuesto"
        _emaGpu = 0f;           // 0 = aún sin dato de GPU
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

        // GPU frame time: solo lo integramos cuando el backend da un valor real (>0).
        // Si nunca llega, _emaGpu se queda en 0 y el gobernador usa la señal de CPU.
        if (n > 0 && _buf[0].gpuFrameTime > 0.0)
        {
            float gpu = (float)_buf[0].gpuFrameTime;
            _emaGpu = _emaGpu > 0f ? Mathf.Lerp(_emaGpu, gpu, _alpha) : gpu;
        }
    }
}
