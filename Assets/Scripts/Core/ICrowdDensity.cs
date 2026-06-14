// Assets/Scripts/Core/ICrowdDensity.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONTRATO — densidad de multitud (capa CORE)
//
//  Lo implementa el sistema de multitud DOTS (SistemaMultitudBRG) y lo consume
//  el audio (SistemaAudioMultitud) para modular el "bed" de cada Audio Cluster
//  según cuántos NPCs hay en la zona — SIN que Systems tenga que referenciar el
//  ensamblado Alsasua.Crowd. Se resuelve por ServiceLocator.Get<ICrowdDensity>().
//
//  La lectura es SEGURA: el implementador cuenta sobre un SNAPSHOT de posiciones
//  que ningún Job toca (se refresca en LateUpdate tras completar la simulación),
//  así no hay carrera con los jobs de flocking.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

public interface ICrowdDensity
{
    /// <summary>Nº de agentes dentro del radio (distancia XZ) del centro.</summary>
    int ContarEnRadio(Vector3 centro, float radio);

    /// <summary>Total de agentes simulados ahora mismo.</summary>
    int TotalAgentes { get; }
}
