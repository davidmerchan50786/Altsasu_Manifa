// Assets/Scripts/Systems/GestorStreamingTexturas.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GESTOR DE STREAMING DE TEXTURAS — VRAM estable + sin hitch al paneo rápido
//
//  Ataca los DOS síntomas del enunciado:
//   (A) Picos de VRAM → presupuesto duro de Mipmap Streaming (las texturas de
//       terreno/edificios cargan solo los mips que la cámara necesita).
//   (B) Caídas de FPS al mover la cámara rápido → cuando la velocidad de cámara
//       supera un umbral, subimos la "reducción de mip" y bajamos el nº de
//       peticiones de IO por frame: durante el barrido se ve un instante más
//       borroso pero NO hay tirón; al frenar, se restaura la nitidez.
//
//  Esto funciona HOY con los materiales actuales (HDRP/Lit + Terrain), sin
//  Shader Graph. Es la "Capa 1" complementaria al SVT (Capa 2, solo ortofoto).
//
//  Caché CPU de SVT (si está activado): se ajusta una sola vez por API runtime.
//
//  Capa: Alsasua.Systems. Sin dependencias hacia capas superiores.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[DefaultExecutionOrder(-70)]
public sealed class GestorStreamingTexturas : MonoBehaviour
{
    [Header("Mipmap Streaming — presupuesto VRAM")]
    [Tooltip("Presupuesto de texturas en streaming (MB). Pico de VRAM acotado a esto.")]
    [SerializeField] float presupuestoMB = 1536f;
    [Tooltip("Reducción de mip en reposo (0 = full-res permitido).")]
    [SerializeField] int reduccionReposo = 0;
    [Tooltip("Reducción de mip durante paneo rápido (1-2 = carga mips más bajos → sin hitch).")]
    [SerializeField] int reduccionPaneo = 2;
    [SerializeField] int ioRequestsReposo = 1024;
    [SerializeField] int ioRequestsPaneo  = 128;
    [SerializeField] int renderersPorFrame = 512;

    [Header("Detección de paneo")]
    [Tooltip("Velocidad de cámara (m/s) a partir de la cual se entra en modo paneo.")]
    [SerializeField] float velPaneo = 35f;
    [Tooltip("Velocidad por debajo de la cual se vuelve a reposo (histéresis).")]
    [SerializeField] float velReposo = 12f;

    [Header("SVT (Streaming Virtual Texturing)")]
    [Tooltip("Caché CPU de SVT en MB (solo si VT está activado en Player Settings).")]
    [SerializeField] int cacheCPU_VT_MB = 256;

    Transform _cam;
    Vector3   _posPrev;
    bool      _enPaneo;

    // ── Auto-pausa por sobrecarga de frame (Director de Simulación) ──────────
    // Productor OPCIONAL de IO: bajo sobrecarga reducimos las peticiones de IO de
    // mipmap streaming (igual que en paneo) para no encadenar cargas de textura
    // mientras el motor recupera presupuesto. Histéresis para no parpadear; si no
    // hay orquestador (null), _degradado nunca se activa → comportamiento normal.
    IGlobalSimulationOrchestrator _orquestador;
    System.Action<float>          _onFactorCarga;
    bool                          _degradado;

    void Awake()
    {
        // (A) Presupuesto de mipmap streaming — esto es lo que acota el pico de VRAM.
        QualitySettings.streamingMipmapsActive          = true;
        QualitySettings.streamingMipmapsMemoryBudget    = presupuestoMB;
        QualitySettings.streamingMipmapsAddAllCameras   = true;
        QualitySettings.streamingMipmapsRenderersPerFrame = renderersPorFrame;
        AplicarEstado();

        // SVT: caché CPU. Por REFLEXIÓN a propósito — la firma de
        // VirtualTexturing.Streaming.SetCPUCacheSize varía entre módulos/versiones del
        // engine; resolverla en runtime evita romper la COMPILACIÓN si difiere, y degrada
        // a un log si VT no está activado. (No es un hot-path: se llama una vez.)
        FijarCacheCPU_VT(cacheCPU_VT_MB);

        AlsasuaLogger.Info("StreamingTex",
            $"Mipmap streaming ON · presupuesto {presupuestoMB:F0} MB · histéresis paneo {velReposo}/{velPaneo} m/s.");
    }

    void LateUpdate()
    {
        if (_cam == null)
        {
            var c = Camera.main;
            if (c == null) return;
            _cam = c.transform;
            _posPrev = _cam.position;
            return;
        }

        float vel = (_cam.position - _posPrev).magnitude / Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
        _posPrev = _cam.position;

        // Histéresis: no oscilar en el umbral.
        if (!_enPaneo && vel > velPaneo)      { _enPaneo = true;  AplicarEstado(); }
        else if (_enPaneo && vel < velReposo) { _enPaneo = false; AplicarEstado(); }
    }

    // El presupuesto bajo de IO se aplica si hay paneo rápido O si el Director
    // ha declarado sobrecarga de frame (_degradado). En reposo y sin degrade,
    // se restaura el presupuesto completo.
    void AplicarEstado()
    {
        bool reducir = _enPaneo || _degradado;
        QualitySettings.streamingMipmapsMaxLevelReduction = reducir ? reduccionPaneo : reduccionReposo;
        QualitySettings.streamingMipmapsMaxFileIORequests = reducir ? ioRequestsPaneo : ioRequestsReposo;
    }

    void OnEnable()
    {
        _orquestador = ServiceLocator.Get<IGlobalSimulationOrchestrator>();
        if (_orquestador == null) return;   // sin director → comportamiento normal

        var cfg = GlobalSimulationOrchestrator.Instancia?.Config;
        float pausa   = cfg?.productoresPausaFactor   ?? 0.85f;
        float reanuda = cfg?.productoresReanudaFactor ?? 0.95f;

        _onFactorCarga = factor =>
        {
            bool prev = _degradado;
            if (!_degradado && factor < pausa)        _degradado = true;
            else if (_degradado && factor > reanuda)  _degradado = false;
            if (_degradado != prev) AplicarEstado();   // reaccionar solo al cruzar el umbral
        };
        _orquestador.OnFactorCargaCambia += _onFactorCarga;
        _onFactorCarga(_orquestador.FactorCarga);      // estado inicial coherente
    }

    void OnDisable()
    {
        if (_orquestador != null && _onFactorCarga != null)
            _orquestador.OnFactorCargaCambia -= _onFactorCarga;
        _orquestador   = null;
        _onFactorCarga = null;
    }

    static void FijarCacheCPU_VT(int mb)
    {
        try
        {
            var t = System.Type.GetType("UnityEngine.Rendering.VirtualTexturing.Streaming, UnityEngine.VirtualTexturingModule")
                 ?? System.Type.GetType("UnityEngine.Rendering.VirtualTexturing.Streaming, UnityEngine");
            var m = t?.GetMethod("SetCPUCacheSize",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null, new[] { typeof(long) }, null);
            if (m != null)
            {
                m.Invoke(null, new object[] { (long)mb * 1024 * 1024 });
                AlsasuaLogger.Info("StreamingTex", $"SVT: caché CPU fijada a {mb} MB.");
            }
            else
            {
                AlsasuaLogger.Info("StreamingTex", "SVT: API de caché CPU no encontrada (VT desactivado o versión distinta).");
            }
        }
        catch (System.Exception e)
        {
            AlsasuaLogger.Info("StreamingTex", $"SVT: caché CPU omitida ({e.Message}).");
        }
    }
}
