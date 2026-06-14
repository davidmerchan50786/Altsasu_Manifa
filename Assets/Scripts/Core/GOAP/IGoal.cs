// Assets/Scripts/Core/GOAP/IGoal.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GOAP — META (Goal)
//
//  Un estado del mundo DESEADO + cuánto le importa al agente AHORA. El agente
//  elige cada ciclo la meta relevante de mayor Prioridad y le pide al
//  planificador una cadena de acciones que la satisfaga.
//
//  Igual que las acciones: instancia única, métodos sin alocaciones.
//
//  Capa CORE.
// ═══════════════════════════════════════════════════════════════════════════

namespace Alsasua.GOAP
{
    public interface IGoal
    {
        string Nombre { get; }

        /// <summary>Estado del mundo que se quiere alcanzar.</summary>
        CondicionMundo Objetivo { get; }

        /// <summary>¿Tiene sentido perseguirla en el contexto actual? (p. ej. replegarse
        /// solo si el apoyo popular es alto y hay cobertura).</summary>
        bool EsRelevante(IAgentContext ctx);

        /// <summary>Importancia dinámica. El agente toma la meta relevante de mayor valor.</summary>
        float Prioridad(IAgentContext ctx);
    }
}
