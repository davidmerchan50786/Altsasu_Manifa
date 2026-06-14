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
