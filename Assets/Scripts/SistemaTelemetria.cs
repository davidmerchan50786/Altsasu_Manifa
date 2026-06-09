// Assets/Scripts/SistemaTelemetria.cs
// ═══════════════════════════════════════════════════════════════════════════
//  TELEMETRÍA DE FRAME-TIME — percentiles p50/p95/p99
//
//  Acumula los últimos N frame-times en un ring buffer (sin alloc en runtime)
//  y publica los percentiles p50, p95 y p99 en ms.
//
//  Uso:
//    • Añadir este componente a cualquier GameObject de la escena.
//    • Leer SistemaTelemetria.Instance.P50Ms / P95Ms / P99Ms en cualquier momento.
//    • DiagnosticoGrafico lee P99Ms para alertar si hay hitches (>33 ms).
//    • En batchmode SmokeTestRunner puede leer los percentiles al final del test.
//
//  Coste: ~0.02 ms/frame (ring buffer + sort por copia sobre array cacheado).
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[DefaultExecutionOrder(-500)]   // antes que todo para capturar cada frame
public class SistemaTelemetria : MonoBehaviour
{
    public static SistemaTelemetria Instance { get; private set; }

    // ── Config ────────────────────────────────────────────────────────────
    [Tooltip("Tamaño del ring buffer en frames (600 = ~10s a 60 fps)")]
    [SerializeField] int ventanaFrames = 600;

    // ── Ring buffer ───────────────────────────────────────────────────────
    float[] _buffer;
    int     _idx;
    int     _llenos;    // frames acumulados (hasta ventanaFrames)

    // ── Percentiles cacheados (recalculados cada segundo) ─────────────────
    float _p50, _p95, _p99;
    float _timerRecalc;
    float[] _sortBuf;   // buffer de copia para sort (sin alloc en Update)

    public float P50Ms => _p50;
    public float P95Ms => _p95;
    public float P99Ms => _p99;

    // ── Spike tracking ────────────────────────────────────────────────────
    public int   SpikesTotal  { get; private set; }   // frames > 33 ms
    public float FrameTimeMin { get; private set; } = float.MaxValue;
    public float FrameTimeMax { get; private set; }

    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _buffer  = new float[ventanaFrames];
        _sortBuf = new float[ventanaFrames];
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime * 1000f;   // ms, sin escalar por Time.timeScale

        // Ring buffer
        _buffer[_idx] = dt;
        _idx = (_idx + 1) % ventanaFrames;
        if (_llenos < ventanaFrames) _llenos++;

        // Spike
        if (dt > 33f) SpikesTotal++;
        if (dt < FrameTimeMin) FrameTimeMin = dt;
        if (dt > FrameTimeMax) FrameTimeMax = dt;

        // Recalcular percentiles una vez por segundo
        _timerRecalc += Time.unscaledDeltaTime;
        if (_timerRecalc >= 1f)
        {
            _timerRecalc = 0f;
            RecalcularPercentiles();
        }
    }

    void RecalcularPercentiles()
    {
        if (_llenos == 0) return;

        // Copiar al buffer de sort (sin alloc)
        System.Array.Copy(_buffer, _sortBuf, _llenos);
        System.Array.Sort(_sortBuf, 0, _llenos);

        _p50 = _sortBuf[Mathf.FloorToInt(_llenos * 0.50f)];
        _p95 = _sortBuf[Mathf.FloorToInt(_llenos * 0.95f)];
        _p99 = _sortBuf[Mathf.Min(Mathf.FloorToInt(_llenos * 0.99f), _llenos - 1)];
    }

    /// <summary>Informe de texto para logs o UI debug.</summary>
    public string Informe() =>
        $"p50={_p50:F1}ms  p95={_p95:F1}ms  p99={_p99:F1}ms  " +
        $"min={FrameTimeMin:F1}ms  max={FrameTimeMax:F1}ms  spikes(>33ms)={SpikesTotal}";

    void OnDestroy() { if (Instance == this) Instance = null; }
}
