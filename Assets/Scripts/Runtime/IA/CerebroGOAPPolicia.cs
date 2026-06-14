// Assets/Scripts/Runtime/IA/CerebroGOAPPolicia.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CEREBRO GOAP DE LA POLICÍA — sensar → elegir meta → planificar → ejecutar
//
//  Sustituto data-driven de la máquina de estados rígida de PoliciaForalIA. En
//  vez de transiciones hardcodeadas, cada ~0.5 s:
//    1. Sensa el mundo en ContextoPolicia (instancia reutilizada → 0 GC).
//    2. Elige la meta relevante de mayor Prioridad.
//    3. Pide a PlanificadorGOAP (A*, zero-alloc) la cadena de acciones óptima
//       según los COSTES DINÁMICOS (distancia, apoyo popular, refuerzos…).
//    4. Ejecuta el plan paso a paso; si una acción falla, replanifica.
//
//  Integración: añadir este componente al GameObject del policía (junto a
//  NavMeshAgent). Para migrar PoliciaForalIA, basta con que su Update delegue
//  aquí (o desactivar su FSM y dejar que este cerebro conduzca el NavMeshAgent).
//
//  Capa RUNTIME — usa Core (Alsasua.GOAP, IWantedSystem, SistemaApoyoPopular,
//  ServiceLocator). No referencia Systems/Modules.
// ═══════════════════════════════════════════════════════════════════════════

using Alsasua.GOAP;
using UnityEngine;
using UnityEngine.AI;

namespace Alsasua.IA
{
    [RequireComponent(typeof(NavMeshAgent))]
    [AddComponentMenu("Alsasua/IA/Cerebro GOAP Policía")]
    public sealed class CerebroGOAPPolicia : MonoBehaviour
    {
        [Header("═══ GOAP ═══")]
        [Tooltip("Cada cuántos segundos se re-planifica.")]
        [SerializeField] float replanCada = 0.5f;
        [Tooltip("Radio (m) al que se considera al jugador 'en rango de arresto'.")]
        [SerializeField] float radioArresto = 2.2f;
        [Tooltip("Punto de cobertura alcanzable (opcional). Si es null, la acción de cobertura no entra en el plan.")]
        [SerializeField] Transform coberturaMasCercana;
        [Tooltip("Capacidad del pool de nodos del planificador. 64 sobra para este dominio.")]
        [SerializeField] int maxNodosPlan = 64;

        // ── GOAP (todo se aloca UNA vez aquí; planificar luego no genera basura) ──
        PlanificadorGOAP _planificador;
        ContextoPolicia  _ctx;
        IAction[]        _acciones;
        IGoal[]          _metas;
        IAction[]        _planBuf;            // buffer de salida del plan, reutilizado
        int   _planLen, _planPaso;
        float _timer;

        // ── Hechos persistentes entre frames (los que no se re-sensan) ──
        bool _refuerzosPedidos;
        bool _enCobertura;

        NavMeshAgent  _nav;
        Transform     _jugador;
        IWantedSystem _wanted;
        ISpawnService _spawn;
        IGoal         _metaActual;

        void Awake()
        {
            _nav = GetComponent<NavMeshAgent>();

            _planificador = new PlanificadorGOAP(maxNodosPlan);
            _ctx          = new ContextoPolicia { nav = _nav, radioArresto = radioArresto };
            _ctx.onLlamarRefuerzos = LlamarRefuerzos;
            _ctx.onArrestar        = Arrestar;

            _acciones = new IAction[]
            {
                new PerseguirAction(),
                new ArrestarJugadorAction(),
                new LlamarRefuerzosAction(),
                new MoverACoberturaAction(),
            };
            _metas = new IGoal[]
            {
                new MetaCapturarJugador(),
                new MetaReplegarse(),
            };
            _planBuf = new IAction[16];
        }

        void Start()
        {
            _wanted     = ServiceLocator.Get<IWantedSystem>();
            _spawn      = ServiceLocator.Get<ISpawnService>();
            _ctx.wanted = _wanted;

            // Misma resolución de jugador que NPCBase: AltsasuCore.Jugador (O(1)),
            // con evento de respaldo si el policía se crea antes del boot completo.
            _jugador = AltsasuCore.Jugador;
            if (_jugador == null)
                AltsasuCore.OnJugadorSpawned += CacharJugador;
        }

        void OnDestroy() => AltsasuCore.OnJugadorSpawned -= CacharJugador;

        void CacharJugador(Transform t)
        {
            _jugador = t;
            AltsasuCore.OnJugadorSpawned -= CacharJugador;
        }

        void Update()
        {
            Sensar();

            _timer += Time.deltaTime;
            if (_timer >= replanCada)
            {
                _timer = 0f;
                Replanificar();
            }

            EjecutarPlan();
        }

        // ── 1. SENSAR (refresca el contexto, sin alocar) ─────────────────────────
        void Sensar()
        {
            _ctx.posAgente = transform.position;
            if (_jugador != null) _ctx.posJugador = _jugador.position;

            _ctx.hayCobertura = coberturaMasCercana != null;
            _ctx.posCobertura = _ctx.hayCobertura ? coberturaMasCercana.position : _ctx.posAgente;

            _ctx.nivelBusqueda = _wanted != null ? _wanted.NivelBusqueda : 0;

            var sap = SistemaApoyoPopular.Instance;
            _ctx.apoyo01 = sap != null ? Mathf.Clamp01(sap.apoyo / 100f) : 0.5f;

            // LOS: PoliciaForalIA ya hace raycasts multi-punto reales; aquí, si
            // este cerebro la conduce, basta con "hay jugador conocido".
            _ctx.tieneLOS = _jugador != null;
        }

        EstadoMundo LeerEstado()
        {
            EstadoMundo e = default;
            e.Set((int)HechoPol.VeAlJugador,         _ctx.tieneLOS);
            e.Set((int)HechoPol.JugadorEnRango,      _ctx.DistanciaAlObjetivo <= _ctx.radioArresto);
            e.Set((int)HechoPol.EnCobertura,         _enCobertura);
            e.Set((int)HechoPol.RefuerzosPedidos,    _refuerzosPedidos);
            e.Set((int)HechoPol.JugadorNeutralizado, false);
            return e;
        }

        // ── 2 + 3. ELEGIR META Y PLANIFICAR ──────────────────────────────────────
        void Replanificar()
        {
            IGoal mejor = null;
            float mejorP = float.NegativeInfinity;
            for (int i = 0; i < _metas.Length; i++)
            {
                var m = _metas[i];
                if (!m.EsRelevante(_ctx)) continue;
                float p = m.Prioridad(_ctx);
                if (p > mejorP) { mejorP = p; mejor = m; }
            }

            _metaActual = mejor;
            if (mejor == null) { _planLen = 0; return; }

            EstadoMundo inicial = LeerEstado();
            _planLen  = _planificador.Planificar(inicial, mejor.Objetivo, _acciones, _ctx, _planBuf);
            _planPaso = 0;
            if (_planLen > 0) _planBuf[0].Iniciar(_ctx);
        }

        // ── 4. EJECUTAR EL PLAN ───────────────────────────────────────────────────
        void EjecutarPlan()
        {
            if (_planLen <= 0 || _planPaso >= _planLen) return;

            var acc = _planBuf[_planPaso];
            switch (acc.Ejecutar(_ctx))
            {
                case EstadoEjecucion.Exito:
                    _planPaso++;
                    if (_planPaso < _planLen) _planBuf[_planPaso].Iniciar(_ctx);
                    break;
                case EstadoEjecucion.Fallo:
                    _planLen = 0;   // fuerza replanificación el próximo ciclo
                    break;
                // EnCurso → seguir el próximo frame
            }
        }

        // ── Actuadores reales (los invocan las acciones vía el contexto) ──────────
        void LlamarRefuerzos()
        {
            _refuerzosPedidos = true;   // un intento por enfrentamiento; el cooldown global hace el anti-spam
            _spawn ??= ServiceLocator.Get<ISpawnService>();
            int llegan = _spawn?.SolicitarRefuerzosPolicia(transform.position, 1) ?? 0;
            if (llegan > 0)
            {
                _wanted?.AumentarBusqueda(1);
                AlsasuaLogger.Info("GOAP", $"{name}: oleada de refuerzos → {llegan} en camino.");
            }
        }

        void Arrestar()
        {
            AlsasuaLogger.Info("GOAP", $"{name}: jugador arrestado.");
            // TODO gameplay: encadenar con la pantalla de detención / game over.
        }

        // Permite a otra cobertura marcar a este policía como atrincherado.
        public void FijarEnCobertura(bool valor) => _enCobertura = valor;

        /// <summary>Diagnóstico para el inspector/HUD: meta y nodos del último plan.</summary>
        public string Diagnostico =>
            _metaActual != null
                ? $"{_metaActual.Nombre} · {_planLen} acc · {_planificador.NodosExplorados} nodos"
                : "sin meta";
    }
}
