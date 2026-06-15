// Assets/Scripts/Core/Simulacion/SimulacionContratos.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ORQUESTADOR DE SIMULACIÓN — contratos (capa CORE)
//
//  El "reloj" del juego. Core SOLO conoce estas abstracciones; nunca tipos de
//  Runtime/Systems → las dependencias siguen siendo unidireccionales. Los sistemas
//  de arriba IMPLEMENTAN estas interfaces y se REGISTRAN en el orquestador (igual
//  que ya hacen con ServiceLocator / SistemaIA).
//
//  · ITickable   → "actualízame a esta frecuencia" (time-slicing por buckets+fase)
//  · ISimulable  → "tengo 3 niveles de simulación según distancia/oclusión"
//  · ITelemetryService → frame-time suavizado (degrade dinámico)
//  · IGlobalSimulationOrchestrator → el servicio en sí
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

/// <summary>Nivel de simulación de una entidad (Sim-LOD).</summary>
public enum NivelSim { Actor = 0, Proxy = 1, Ghost = 2 }

/// <summary>Bucket de frecuencia de tick. El periodo en frames asume ~60 fps.</summary>
public enum Frecuencia { PorFrame = 0, Hz30, Hz10, Hz5, Hz1 }

public static class FrecuenciaExt
{
    /// <summary>Periodo en frames @60fps (downsample + base del striding por fase).</summary>
    public static int PeriodoFrames(this Frecuencia f) => f switch
    {
        Frecuencia.PorFrame => 1,
        Frecuencia.Hz30     => 2,
        Frecuencia.Hz10     => 6,
        Frecuencia.Hz5      => 12,
        Frecuencia.Hz1      => 60,
        _                   => 2
    };
}

/// <summary>Lo actualiza el orquestador, NO un Update propio. dtAcumulado = tiempo
/// real transcurrido desde el último tick de ESTA entidad (no Time.deltaTime).</summary>
public interface ITickable
{
    Frecuencia Frecuencia { get; }     // puede cambiar en runtime (lo lee el orquestador)
    void Tick(float dtAcumulado);
}

/// <summary>Entidad con 3 niveles de simulación según distancia/oclusión a la cámara.</summary>
public interface ISimulable
{
    Vector3  Posicion { get; }
    NivelSim Nivel    { get; }
    void AplicarNivel(NivelSim n);     // el sistema reacciona (físicas, anim, pool…)
}

/// <summary>Frame-time suavizado (EMA) para el throttling dinámico.</summary>
public interface ITelemetryService
{
    float FrameMsSuavizado { get; }    // EMA del CPU frame time (ms)
    float GpuMsSuavizado   { get; }    // EMA del GPU frame time (ms); 0 si el backend no lo reporta
    float PresupuestoMs    { get; }    // objetivo (p.ej. 15.5 ms con headroom)
}

public interface IGlobalSimulationOrchestrator
{
    void Registrar(ITickable t);
    void Desregistrar(ITickable t);
    void Registrar(ISimulable s);
    void Desregistrar(ISimulable s);

    /// <summary>1 = todo a tope; &lt;1 = degradado. Lo leen los productores opcionales
    /// (escombros, streaming) para auto-pausarse. Encoge radios LOD y caps.</summary>
    float FactorCarga { get; }
    event System.Action<float> OnFactorCargaCambia;
}

/// <summary>
/// Gobernador de RENDER (GPU). Hermano del orquestador (que gobierna CPU/IA): vigila el
/// coste de GPU (y CPU como respaldo) y produce un RADIO DE ACTIVACIÓN DEL MUNDO dinámico.
/// El streaming estático (edificios/árboles/props) usa ese radio como su único mando: bajo
/// presión de GPU el radio se encoge → menos draw calls/triángulos en vuelo → "degrada el
/// baseline antes de añadir detalle". Capa CORE: nadie de arriba lo conoce por tipo concreto.
/// </summary>
public interface IRenderBudgetGovernor
{
    /// <summary>Metros: dentro de este radio el mundo se renderiza a detalle completo.</summary>
    float RadioActivacion { get; }
    /// <summary>Metros: entre RadioActivacion y este, impostor/LOD bajo; más allá, apagado.</summary>
    float RadioImpostor { get; }
    /// <summary>0..1 — 1 = alcance máximo; &lt;1 = recortado por presión de render.</summary>
    float FactorRender { get; }
    /// <summary>True si AHORA mismo vamos por encima del presupuesto de render.</summary>
    bool Saturado { get; }
    /// <summary>GPU ms suavizado que el gobernador está viendo (0 si el backend no lo da).</summary>
    float GpuMs { get; }
    /// <summary>Se dispara cuando FactorRender cambia (para reescalar consumidores caros).</summary>
    event System.Action<float> OnFactorRenderCambia;
}

/// <summary>
/// Parámetros del gobernador de render. Metros (1 u = 1 m). Mutable para tunear en vivo.
/// Defaults pensados para Alsasua: a tope se ve ~260 m a detalle + ~300 m de impostores
/// (cubre la cuenca jugable); bajo máxima presión cae a 90 m (una manzana alrededor).
/// </summary>
public sealed class ConfiguracionRender
{
    // ── Radios (m) ──
    public float radioActivacionMax = 260f;  // alcance a detalle completo cuando hay holgura
    public float radioActivacionMin = 90f;   // suelo bajo máxima presión de GPU
    public float radioImpostorExtra = 300f;  // ancho del anillo impostor sobre el de activación
    public float histeresisM        = 12f;   // margen pegajoso para evitar parpadeo en el borde

    // ── Presupuesto / degrade (ms) ──
    public float presupuestoGpuMs = 13.5f;   // objetivo GPU con headroom (bajo 16.6 de 60 fps)
    public float degradeMul       = 1.05f;   // coste > presupuesto·1.05 → encoger radio
    public float recoverMul       = 0.80f;   // coste < presupuesto·0.80 → ampliar radio
    public float pasoDegrade      = 0.06f;   // rápido (un pico de GPU no puede esperar)
    public float pasoRecover      = 0.012f;  // lento (anti-oscilación, ~5× más suave)
    public float factorMin        = 0.0f;    // permite llegar al radio mínimo si hace falta
}

/// <summary>
/// Parámetros del Director — defaults AFINADOS contra los valores reales del proyecto
/// (PoliciaForalIA.radioVision=22, radioAtaque=16; SistemaChunks 180/240; crowd @30Hz).
/// Mutable: un componente de debug puede tocarlos en vivo para tunear.
/// </summary>
public sealed class ConfiguracionSimulacion
{
    // ── Sim-LOD (metros; 1 unidad Unity = 1 m) ──
    public float radioActor   = 35f;    // 0–35  : full IA + físicas + anim full
    public float radioProxy   = 140f;   // 35–140: kinemático, FSM-lite, anim ½ rate
    public float histActor    = 5f;     // margen pegajoso Actor↔Proxy
    public float histProxy    = 15f;    // margen mayor Proxy↔Ghost (promover de Ghost es caro)
    public float oclusionDesde = 25f;   // a partir de aquí, lo que está tras la cámara baja un nivel

    // ── Caps (los mandos reales del throttle; se escalan por FactorCarga) ──
    public int maxActores    = 70;
    public int maxProxies    = 350;
    public int maxPromosFrame = 12;     // rents del pool por frame (reparte el coste al girar la cámara)

    // ── Presupuesto / degrade dinámico ──
    public float sliceSimMs   = 4.5f;   // slice de HILO PRINCIPAL para dispatch+LOD
    public float presupuestoMs = 15.5f; // objetivo con headroom (degrada ANTES del tirón real)
    public float emaAlpha     = 0.10f;  // suavizado del frame-time (~media de 10 frames)
    public float degradeMul   = 1.05f;  // ema > presupuesto·1.05 → degradar
    public float recoverMul   = 0.85f;  // ema < presupuesto·0.85 → recuperar
    public float pasoDegrade  = 0.05f;  // rápido
    public float pasoRecover  = 0.01f;  // lento (anti-oscilación, 5× más suave)
    public float factorMin    = 0.50f;  // nunca menos: una manifa vacía es peor que perder 1 frame
    public float productoresPausaFactor   = 0.85f;
    public float productoresReanudaFactor = 0.95f;

    // ── Mapeo nivel → frecuencia de tick ──
    public Frecuencia frecActor = Frecuencia.Hz30;
    public Frecuencia frecProxy = Frecuencia.Hz5;
    public Frecuencia frecGhost = Frecuencia.Hz1;

    // ── LOD sweep (ventana rotatoria: set completo re-evaluado en N frames) ──
    public int ventanaLODFrames = 30;   // ~0.5 s @60fps

    /// <summary>Kill-switch: si false, los NPC vuelven a auto-actualizarse en su Update.</summary>
    public bool orquestarNPCs = true;
}
