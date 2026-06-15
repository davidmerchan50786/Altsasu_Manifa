// Assets/Scripts/Core/Simulacion/GobernadorRender.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GOBERNADOR DE RENDER — el director de GPU (capa CORE)
//
//  El GlobalSimulationOrchestrator gobierna la CPU (ticks de IA + Sim-LOD). Pero nada
//  acotaba draw calls/triángulos: el mundo entero (48 terrain + 1030 edificios + 3311
//  árboles + multitud) se renderizaba desde el frame 1 estuviera donde estuviera el
//  jugador. SistemaOptimizacion solo tocaba sombras/LOD bias → no podía rescatar un
//  baseline 10× pasado de presupuesto.
//
//  Este gobernador cierra ese hueco. Cada frame mira el coste de GPU (telemetría) y,
//  con histéresis, produce un RADIO DE ACTIVACIÓN DEL MUNDO dinámico:
//    · GPU con holgura  → el radio crece despacio (más mundo a la vista)
//    · GPU saturada     → el radio se encoge rápido (menos objetos activos → menos draw calls)
//
//  El streaming estático (StreamerMundoEstatico, capa Runtime) usa RadioActivacion como
//  su único mando. Así "degradamos el baseline (el alcance del mundo) antes de añadir
//  detalle" — el freno correcto, no el LOD bias.
//
//  No es MonoBehaviour ni se inyecta solo en el PlayerLoop: lo ARRANCA y lo TICKEA el
//  orquestador (la autoridad de frame), que ya muestrea la telemetría una vez por frame.
//  Se expone por ServiceLocator<IRenderBudgetGovernor>.
//
//  Layer-safe: Core puro. No conoce edificios, árboles ni multitud.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

public sealed class GobernadorRender : IRenderBudgetGovernor
{
    public static GobernadorRender Instancia { get; private set; }

    public ConfiguracionRender Config { get; } = new();

    float _factor = 1f;        // 0..1 — arranca a tope; baja si la GPU no da
    float _gpuMs;              // GPU ms que vimos en el último tick (0 = sin dato)
    bool  _saturado;

    public float RadioActivacion { get; private set; }
    public float RadioImpostor   { get; private set; }
    public float FactorRender    => _factor;
    public bool  Saturado        => _saturado;
    public float GpuMs           => _gpuMs;

    public event System.Action<float> OnFactorRenderCambia;

    // ════════════════════════════════════════════════════════════════════════
    //  BOOT — lo llama el orquestador en su propio Boot (mismo frame-authority).
    //  Idempotente: re-Play sin domain reload no duplica la instancia.
    // ════════════════════════════════════════════════════════════════════════
    public static GobernadorRender CrearYRegistrar()
    {
        Instancia = new GobernadorRender();
        Instancia.RadioActivacion = Instancia.Config.radioActivacionMax;
        Instancia.RadioImpostor   = Instancia.RadioActivacion + Instancia.Config.radioImpostorExtra;
        ServiceLocator.Registrar<IRenderBudgetGovernor>(Instancia);
        return Instancia;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ACTUALIZAR — una vez por frame, desde el orquestador, con CPU/GPU ya muestreados.
    //  cpuMs/gpuMs son EMAs (suavizados). gpuMs = 0 significa "sin dato de GPU" →
    //  usamos la CPU como señal (un 0 FPS por CPU también debe encoger el mundo).
    // ════════════════════════════════════════════════════════════════════════
    public void Actualizar(float cpuMs, float gpuMs)
    {
        _gpuMs = gpuMs;

        // Señal de coste: la GPU manda (es el hueco diagnosticado). Si no hay dato de GPU,
        // cae a la CPU. Tomamos el máximo para que un pico en cualquiera de las dos frene.
        float coste = gpuMs > 0f ? Mathf.Max(gpuMs, cpuMs) : cpuMs;
        float lim   = Config.presupuestoGpuMs;

        float prev = _factor;
        if      (coste > lim * Config.degradeMul) { _factor -= Config.pasoDegrade;  _saturado = true;  }
        else if (coste < lim * Config.recoverMul) { _factor += Config.pasoRecover;  _saturado = false; }
        else                                        _saturado = false;
        _factor = Mathf.Clamp(_factor, Config.factorMin, 1f);

        // Radio = interpolación entre el suelo y el techo según el factor.
        RadioActivacion = Mathf.Lerp(Config.radioActivacionMin, Config.radioActivacionMax, _factor);
        RadioImpostor   = RadioActivacion + Config.radioImpostorExtra * _factor;

        if (!Mathf.Approximately(prev, _factor))
            OnFactorRenderCambia?.Invoke(_factor);
    }

    // El orquestador se resetea en cada Play (Boot en BeforeSceneLoad) y nos recrea allí,
    // así que el estado de una sesión anterior no se arrastra. Este método sirve por si
    // alguien quiere forzar el reset (tests / teletransporte que quiera el alcance máximo).
    public void Reset()
    {
        _factor = 1f;
        _saturado = false;
        RadioActivacion = Config.radioActivacionMax;
        RadioImpostor   = RadioActivacion + Config.radioImpostorExtra;
    }
}
