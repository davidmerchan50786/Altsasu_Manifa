// Assets/Scripts/Core/ILocomocionAvanzada.cs
// ═══════════════════════════════════════════════════════════════════════════
//  LOCOMOCIÓN AVANZADA — contratos Core (Motion Matching ready)
//
//  Diseño completo: Docs/arquitectura_locomocion.md
//
//  El input del jugador NO se traduce directamente a velocidad/posición. Se
//  expone como una "trayectoria deseada" futura (a dónde querría estar el
//  personaje en 0.2/0.4/0.6 s) — es la entrada estándar de Motion Matching
//  y se puede consumir por cualquier sistema de locomoción (incluido el
//  Animator clásico, que la convierte a velocidad).
//
//  Esto permite:
//    · Mantener ControladorJugador estable (sigue produciendo CharacterController
//      moves) mientras el sistema de locomoción consume la trayectoria.
//    · Sustituir el implementador de ILocomocionAvanzada por uno con Motion
//      Matching real cuando el paquete esté disponible — sin tocar consumidores.
//    · Que NPCs Actor (Sim-LOD Nivel 0) compartan el mismo proveedor con un
//      stub IA que genera trayectorias desde el destino del NavMeshAgent.
//
//  Capa Core: solo contratos + structs blittables. Las implementaciones
//  reales viven en Runtime (jugador) / Crowd (NPC). Cero acoplamiento.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

/// <summary>Muestras de la trayectoria deseada del personaje en N puntos
/// temporales futuros (orden ascendente de t). Blittable → válido en jobs.</summary>
public struct TrayectoriaDeseada
{
    /// <summary>Posición mundo deseada en cada t (Y dejada al solver de IK; aquí
    /// solo XZ tiene significado). Capacidad fija = 4 muestras.</summary>
    public Vector3 p0, p1, p2, p3;
    /// <summary>Dirección de avance (forward) deseada en cada t. Normalizada.</summary>
    public Vector3 d0, d1, d2, d3;
    /// <summary>Tiempos relativos al instante actual (s). Convención: 0, 0.2, 0.4, 0.6.</summary>
    public float t0, t1, t2, t3;
    /// <summary>Velocidad deseada en la muestra final (m/s) — útil para elegir
    /// walk/run en motion matching o blend trees.</summary>
    public float velocidadFinal;
}

/// <summary>Quien sabe a dónde quiere ir el personaje en el futuro próximo.
/// El jugador genera trayectoria desde el stick; un NPC desde su NavMeshAgent.</summary>
public interface IProveedorTrayectoria
{
    /// <summary>Posición actual del personaje (origen de la trayectoria).</summary>
    Vector3 PosicionActual { get; }
    /// <summary>Forward actual del personaje (origen de la dirección).</summary>
    Vector3 ForwardActual  { get; }
    /// <summary>Genera la trayectoria deseada **AHORA** (el implementador puede
    /// suavizar con spring-damper internamente para producir "inercia y peso").</summary>
    TrayectoriaDeseada MuestrearAhora();
}

/// <summary>Estado de pisada — útil para Foot IK, root motion y selección de animación.</summary>
public enum FaseLocomocion
{
    Quieto,
    Andando,
    Corriendo,
    Frenando,
    Saltando,
    EnAire
}

/// <summary>Sistema de locomoción avanzado (Motion Matching o Animator clásico).
/// Consume `IProveedorTrayectoria`, produce desplazamiento por frame y publica
/// la fase actual. La implementación elige entre Motion Matching (define
/// ALSASUA_MOTIONMATCHING) o un fallback Animator.</summary>
public interface ILocomocionAvanzada
{
    /// <summary>Transform del personaje (para que IK y cámara lo encuentren).</summary>
    Transform Transformacion { get; }
    /// <summary>Fase actual; publicada para HUD/audio/foot IK.</summary>
    FaseLocomocion Fase { get; }
    /// <summary>Velocidad horizontal real (m/s) tras resolver root motion + física.</summary>
    float VelocidadActual { get; }
    /// <summary>Conecta un nuevo proveedor de trayectoria (jugador o NPC).</summary>
    void Conectar(IProveedorTrayectoria proveedor);
    /// <summary>Realimentación: el `CharacterController` chocó y el desplazamiento
    /// real fue distinto al propuesto → el solver de Motion Matching parte del
    /// estado físico, no del animado. Llamada típica desde `ControladorJugador`.</summary>
    void RealimentarDesplazamientoReal(Vector3 desplazamientoMundo);
}
