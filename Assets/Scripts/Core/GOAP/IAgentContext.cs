// Assets/Scripts/Core/GOAP/IAgentContext.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GOAP — CONTEXTO DEL AGENTE
//
//  El "mundo sentido" que ven las acciones y las metas para calcular COSTES y
//  PRIORIDADES dinámicos (distancia al objetivo, etc.). El planificador nunca
//  crea contextos: el agente reutiliza UNA instancia y solo refresca sus campos
//  cada tick → la fase de planificación no aloca.
//
//  Esta es la base genérica. Cada dominio la extiende con sus propios sensores
//  (p. ej. ContextoPolicia añade apoyoPopular, nivelBusqueda, cobertura…).
//
//  Capa CORE.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

namespace Alsasua.GOAP
{
    public interface IAgentContext
    {
        /// <summary>Posición mundo del agente que planifica.</summary>
        Vector3 PosicionAgente { get; }

        /// <summary>Posición mundo del objetivo principal (jugador / amenaza).</summary>
        Vector3 PosicionObjetivo { get; }

        /// <summary>Distancia agente→objetivo (m). Se consulta en costes dinámicos.</summary>
        float DistanciaAlObjetivo { get; }
    }
}
