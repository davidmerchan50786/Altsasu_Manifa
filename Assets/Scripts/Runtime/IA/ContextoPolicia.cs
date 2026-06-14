// Assets/Scripts/Runtime/IA/ContextoPolicia.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GOAP POLICÍA — ÁTOMOS DEL DOMINIO + CONTEXTO SENSADO
//
//  HechoPol  : los hechos booleanos sobre los que razona la Policía Foral. Cada
//              valor = un bit del EstadoMundo (ulong) → máx 64.
//  ContextoPolicia : la instancia ÚNICA que el cerebro refresca cada tick con
//              lo que "ve" (distancias, apoyo popular, nivel de búsqueda…). Las
//              acciones y metas leen de aquí para sus costes/prioridades
//              dinámicos. Es una clase (referencia) → castear IAgentContext a
//              ContextoPolicia en CalcularCoste NO hace boxing → zero-alloc.
//
//  Capa RUNTIME (depende de Core: Alsasua.GOAP, IWantedSystem, ServiceLocator).
// ═══════════════════════════════════════════════════════════════════════════

using Alsasua.GOAP;
using UnityEngine;
using UnityEngine.AI;

namespace Alsasua.IA
{
    /// <summary>Hechos del mundo de la policía. El valor entero es el índice de bit.</summary>
    public enum HechoPol
    {
        VeAlJugador        = 0,   // línea de visión confirmada
        JugadorEnRango     = 1,   // dentro del radio de arresto
        EnCobertura        = 2,   // atrincherado en un punto de cobertura
        RefuerzosPedidos   = 3,   // ya se llamó a refuerzos
        JugadorNeutralizado = 4,  // arrestado / abatido (meta de captura)
    }

    public sealed class ContextoPolicia : IAgentContext
    {
        // ── Sensores (los rellena CerebroGOAPPolicia cada tick, sin alocar) ──
        public Vector3 posAgente;
        public Vector3 posJugador;
        public Vector3 posCobertura;
        public float   apoyo01;         // apoyo popular normalizado 0..1
        public int     nivelBusqueda;   // 0..5 (IWantedSystem)
        public bool    tieneLOS;        // ve al jugador
        public bool    hayCobertura;    // existe cobertura alcanzable
        public float   radioArresto = 2.2f;

        // ── Actuadores (el cerebro inyecta estas referencias en Awake) ──
        public NavMeshAgent nav;
        public IWantedSystem wanted;
        public System.Action onLlamarRefuerzos;
        public System.Action onArrestar;

        // ── IAgentContext ──
        public Vector3 PosicionAgente      => posAgente;
        public Vector3 PosicionObjetivo    => posJugador;
        public float   DistanciaAlObjetivo => Vector3.Distance(posAgente, posJugador);

        /// <summary>Distancia agente→cobertura más cercana (m).</summary>
        public float   DistanciaACobertura => Vector3.Distance(posAgente, posCobertura);
    }
}
