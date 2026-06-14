// Assets/Scripts/Core/GOAP/IAction.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GOAP — ACCIÓN
//
//  Un eslabón del plan. El planificador la trata como una transición simbólica:
//      Precondiciones  ─(coste dinámico)→  Efectos
//  y NUNCA la ejecuta: solo lee Precondiciones/Efectos/CalcularCoste/EsViable.
//  La EJECUCIÓN (mover el NavMeshAgent, lanzar el arresto…) ocurre después, en
//  el agente, vía Iniciar()/Ejecutar().
//
//  ZERO-ALLOC: las acciones se instancian UNA vez (en el Awake del agente) y se
//  reutilizan en cada plan. Sus métodos no deben alocar (ni LINQ, ni closures,
//  ni boxing): el coste se calcula leyendo el contexto y el estado por 'in ref'.
//
//  Capa CORE.
// ═══════════════════════════════════════════════════════════════════════════

namespace Alsasua.GOAP
{
    /// <summary>Resultado de un paso de ejecución en runtime.</summary>
    public enum EstadoEjecucion { EnCurso, Exito, Fallo }

    public interface IAction
    {
        string Nombre { get; }

        /// <summary>Qué debe ser cierto en el estado para poder encadenar la acción.</summary>
        CondicionMundo Precondiciones { get; }

        /// <summary>Cómo cambia el estado del mundo tras ejecutarla (predicción del planner).</summary>
        CondicionMundo Efectos { get; }

        /// <summary>
        /// Coste para A* — DINÁMICO. Puede leer:
        ///   · 'estado'  → hechos ya planificados (p. ej. ¿hay refuerzos en este punto del plan?)
        ///   · 'ctx'     → sensores del mundo real (distancia, apoyo popular…)
        /// Debe devolver ≥ 0 (idealmente ≥ 1 para mantener la heurística admisible).
        /// </summary>
        float CalcularCoste(in EstadoMundo estado, IAgentContext ctx);

        /// <summary>Filtro duro de viabilidad REAL (no simbólica): si no hay cobertura
        /// alcanzable, MoverACobertura no entra en el plan aunque encaje por bits.</summary>
        bool EsViable(IAgentContext ctx);

        // ── Ejecución (el planificador no la usa) ──────────────────────────────
        void Iniciar(IAgentContext ctx);
        EstadoEjecucion Ejecutar(IAgentContext ctx);
    }
}
