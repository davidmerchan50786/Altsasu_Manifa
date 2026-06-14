// Assets/Scripts/Runtime/IA/AccionesPolicia.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GOAP POLICÍA — ACCIONES con COSTE DINÁMICO
//
//  Tres acciones. Sus precondiciones/efectos (símbolos) son constantes —se
//  construyen UNA vez en campos static readonly—, pero su COSTE se recalcula en
//  cada plan leyendo el contexto y el estado planificado. De ese coste emerge el
//  comportamiento, sin un solo 'if' de comportamiento hardcodeado:
//
//    · Apoyo popular BAJO, jugador en rango → plan = [Arrestar]            (barato)
//    · Apoyo popular ALTO                   → plan = [LlamarRefuerzos,      porque
//                                              Arrestar]  el arresto a pelo dispara
//                                              su coste y los refuerzos lo abaratan
//    · Apoyo popular EXTREMO                → la meta Replegarse gana →
//                                              plan = [MoverACobertura]
//
//  Zero-alloc: instancias creadas una vez; métodos sin LINQ/closures/boxing.
//
//  Capa RUNTIME.
// ═══════════════════════════════════════════════════════════════════════════

using Alsasua.GOAP;
using UnityEngine;

namespace Alsasua.IA
{
    // ════════════════════════════════════════════════════════════════════════
    //  ARRESTAR — neutraliza al jugador. Coste sensible al apoyo popular.
    // ════════════════════════════════════════════════════════════════════════
    public sealed class ArrestarJugadorAction : IAction
    {
        static readonly CondicionMundo _pre = CondicionMundo.Nueva()
            .Con((int)HechoPol.JugadorEnRango,      true)
            .Con((int)HechoPol.JugadorNeutralizado, false)
            .Construir();

        static readonly CondicionMundo _eff = CondicionMundo.Nueva()
            .Con((int)HechoPol.JugadorNeutralizado, true)
            .Construir();

        public string Nombre => "Arrestar Jugador";
        public CondicionMundo Precondiciones => _pre;
        public CondicionMundo Efectos        => _eff;

        public float CalcularCoste(in EstadoMundo estado, IAgentContext ctx)
        {
            var c = (ContextoPolicia)ctx;
            // Arrestar con la calle en contra es políticamente carísimo…
            float coste = 5f + c.apoyo01 * 12f;
            // …pero con refuerzos presentes en ESTE punto del plan, es más seguro/barato.
            if (estado.Get((int)HechoPol.RefuerzosPedidos)) coste -= 6f;
            coste += c.DistanciaAlObjetivo * 0.1f;
            return coste < 1f ? 1f : coste;
        }

        public bool EsViable(IAgentContext ctx) => true;

        public void Iniciar(IAgentContext ctx) { }

        public EstadoEjecucion Ejecutar(IAgentContext ctx)
        {
            var c = (ContextoPolicia)ctx;
            if (c.DistanciaAlObjetivo > c.radioArresto)
            {
                c.nav?.SetDestination(c.posJugador);   // acércate hasta el rango
                return EstadoEjecucion.EnCurso;
            }
            c.onArrestar?.Invoke();
            return EstadoEjecucion.Exito;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  PERSEGUIR — cierra la distancia hasta el rango de arresto. Acción
    //  conectora: sin ella, el plan de captura es irrealizable mientras el
    //  jugador está a rango de tiro pero no de arresto. Coste = distancia.
    // ════════════════════════════════════════════════════════════════════════
    public sealed class PerseguirAction : IAction
    {
        static readonly CondicionMundo _pre = CondicionMundo.Nueva()
            .Con((int)HechoPol.VeAlJugador,    true)
            .Con((int)HechoPol.JugadorEnRango, false)
            .Construir();

        static readonly CondicionMundo _eff = CondicionMundo.Nueva()
            .Con((int)HechoPol.JugadorEnRango, true)
            .Construir();

        public string Nombre => "Perseguir";
        public CondicionMundo Precondiciones => _pre;
        public CondicionMundo Efectos        => _eff;

        public float CalcularCoste(in EstadoMundo estado, IAgentContext ctx)
        {
            float coste = ((ContextoPolicia)ctx).DistanciaAlObjetivo * 0.15f;
            return coste < 1f ? 1f : coste;
        }

        public bool EsViable(IAgentContext ctx) => true;

        public void Iniciar(IAgentContext ctx)
        {
            var c = (ContextoPolicia)ctx;
            c.nav?.SetDestination(c.posJugador);
        }

        public EstadoEjecucion Ejecutar(IAgentContext ctx)
        {
            var c = (ContextoPolicia)ctx;
            if (c.DistanciaAlObjetivo <= c.radioArresto) return EstadoEjecucion.Exito;
            c.nav?.SetDestination(c.posJugador);
            return EstadoEjecucion.EnCurso;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  LLAMAR REFUERZOS — más barato cuanto mayor el apoyo popular y el nivel
    //  de búsqueda (más justificado pedir ayuda).
    // ════════════════════════════════════════════════════════════════════════
    public sealed class LlamarRefuerzosAction : IAction
    {
        static readonly CondicionMundo _pre = CondicionMundo.Nueva()
            .Con((int)HechoPol.RefuerzosPedidos, false)
            .Construir();

        static readonly CondicionMundo _eff = CondicionMundo.Nueva()
            .Con((int)HechoPol.RefuerzosPedidos, true)
            .Construir();

        public string Nombre => "Llamar Refuerzos";
        public CondicionMundo Precondiciones => _pre;
        public CondicionMundo Efectos        => _eff;

        public float CalcularCoste(in EstadoMundo estado, IAgentContext ctx)
        {
            var c = (ContextoPolicia)ctx;
            float coste = 6f - c.apoyo01 * 4f - c.nivelBusqueda * 0.3f;
            return coste < 1.5f ? 1.5f : coste;
        }

        public bool EsViable(IAgentContext ctx) => true;

        public void Iniciar(IAgentContext ctx) { }

        public EstadoEjecucion Ejecutar(IAgentContext ctx)
        {
            var c = (ContextoPolicia)ctx;
            c.onLlamarRefuerzos?.Invoke();
            return EstadoEjecucion.Exito;   // instantánea (radio)
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MOVER A COBERTURA — coste proporcional a la distancia, descontado si el
    //  apoyo popular es alto (más incentivo a atrincherarse).
    // ════════════════════════════════════════════════════════════════════════
    public sealed class MoverACoberturaAction : IAction
    {
        const float kLlegada = 1.0f;   // m a los que se considera "en cobertura"

        static readonly CondicionMundo _pre = CondicionMundo.Nueva()
            .Con((int)HechoPol.EnCobertura, false)
            .Construir();

        static readonly CondicionMundo _eff = CondicionMundo.Nueva()
            .Con((int)HechoPol.EnCobertura, true)
            .Construir();

        public string Nombre => "Mover A Cobertura";
        public CondicionMundo Precondiciones => _pre;
        public CondicionMundo Efectos        => _eff;

        public float CalcularCoste(in EstadoMundo estado, IAgentContext ctx)
        {
            var c = (ContextoPolicia)ctx;
            float coste = c.DistanciaACobertura * 0.2f * (1f - 0.4f * c.apoyo01);
            return coste < 1f ? 1f : coste;
        }

        // Viabilidad REAL: sin punto de cobertura alcanzable, ni se considera.
        public bool EsViable(IAgentContext ctx) => ((ContextoPolicia)ctx).hayCobertura;

        public void Iniciar(IAgentContext ctx)
        {
            var c = (ContextoPolicia)ctx;
            c.nav?.SetDestination(c.posCobertura);
        }

        public EstadoEjecucion Ejecutar(IAgentContext ctx)
        {
            var c = (ContextoPolicia)ctx;
            return c.DistanciaACobertura <= kLlegada
                ? EstadoEjecucion.Exito
                : EstadoEjecucion.EnCurso;
        }
    }
}
