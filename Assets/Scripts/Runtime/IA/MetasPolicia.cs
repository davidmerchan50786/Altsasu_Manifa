// Assets/Scripts/Runtime/IA/MetasPolicia.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GOAP POLICÍA — METAS (selección por prioridad dinámica)
//
//  El cerebro elige cada ciclo la meta RELEVANTE de mayor Prioridad. Las dos
//  metas compiten según el apoyo popular, produciendo el cambio de actitud:
//
//    apoyo bajo/medio → Capturar gana  → la poli va a por el jugador
//    apoyo muy alto   → Replegarse gana → la poli se atrinchera y evita el roce
//
//  Capa RUNTIME.
// ═══════════════════════════════════════════════════════════════════════════

using Alsasua.GOAP;

namespace Alsasua.IA
{
    /// <summary>Capturar al jugador. Pierde fuelle conforme sube el apoyo popular.</summary>
    public sealed class MetaCapturarJugador : IGoal
    {
        static readonly CondicionMundo _obj = CondicionMundo.Nueva()
            .Con((int)HechoPol.JugadorNeutralizado, true)
            .Construir();

        public string Nombre => "Capturar Jugador";
        public CondicionMundo Objetivo => _obj;

        public bool EsRelevante(IAgentContext ctx) => ((ContextoPolicia)ctx).nivelBusqueda > 0;

        public float Prioridad(IAgentContext ctx)
        {
            var c = (ContextoPolicia)ctx;
            return 10f - c.apoyo01 * 6f;     // 10 (calle vacía) → 4 (calle hostil)
        }
    }

    /// <summary>Replegarse a cobertura. Solo cobra sentido con apoyo popular alto.</summary>
    public sealed class MetaReplegarse : IGoal
    {
        static readonly CondicionMundo _obj = CondicionMundo.Nueva()
            .Con((int)HechoPol.EnCobertura, true)
            .Construir();

        public string Nombre => "Replegarse";
        public CondicionMundo Objetivo => _obj;

        public bool EsRelevante(IAgentContext ctx)
        {
            var c = (ContextoPolicia)ctx;
            return c.hayCobertura && c.apoyo01 > 0.6f;
        }

        public float Prioridad(IAgentContext ctx)
        {
            var c = (ContextoPolicia)ctx;
            return c.apoyo01 * 12f - 4f;     // ~3.2 (0.6) → 8 (1.0): supera a Capturar arriba
        }
    }
}
