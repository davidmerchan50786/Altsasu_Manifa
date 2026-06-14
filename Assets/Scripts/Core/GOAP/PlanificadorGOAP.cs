// Assets/Scripts/Core/GOAP/PlanificadorGOAP.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GOAP — PLANIFICADOR A* (zero-alloc)
//
//  Búsqueda A* PROGRESIVA: del estado actual hacia el objetivo, encadenando
//  acciones cuyas precondiciones cumple el estado y aplicando sus efectos.
//
//    g(n) = Σ coste dinámico de las acciones del camino
//    h(n) = nº de átomos del objetivo aún insatisfechos  (admisible: cada uno
//           necesita ≥1 acción, y por convención toda acción cuesta ≥1)
//    f(n) = g + h
//
//  ── ZERO-ALLOC ───────────────────────────────────────────────────────────────
//  TODO se preasigna en el constructor y se REUTILIZA en cada Planificar():
//    · _nodos  — pool de nodos (struct, sin GC)
//    · _heap   — cola de prioridad (índices, min-heap binario por f)
//  El plan se escribe en un buffer del LLAMANTE (IAction[]). No hay List,
//  Dictionary, LINQ, closures ni boxing en el bucle → 0 B de basura por plan.
//
//  La detección de estados repetidos es un escaneo lineal: el dominio de un
//  agente GTA-like es pequeño (un puñado de acciones), así que es más rápido y
//  más barato en memoria que una tabla hash.
//
//  Capa CORE.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;

namespace Alsasua.GOAP
{
    public sealed class PlanificadorGOAP
    {
        struct Nodo
        {
            public EstadoMundo estado;
            public float g;        // coste acumulado real
            public float f;        // g + heurística
            public int   accion;   // índice (en la lista) de la acción que llevó aquí; -1 raíz
            public int   padre;    // índice del nodo padre; -1 raíz
            public bool  cerrado;
        }

        readonly Nodo[] _nodos;    // pool de nodos
        readonly int[]  _heap;     // min-heap de índices a _nodos, ordenado por f
        readonly int    _maxNodos;
        int _numNodos, _heapLen;

        /// <summary>Diagnóstico: nodos expandidos en el último Planificar().</summary>
        public int NodosExplorados { get; private set; }

        public PlanificadorGOAP(int maxNodos = 256)
        {
            _maxNodos = maxNodos;
            _nodos = new Nodo[maxNodos];
            _heap  = new int[maxNodos * 4];   // holgura para re-aperturas (push duplicado)
        }

        /// <summary>
        /// Planifica una cadena de acciones de 'inicial' a 'meta'. Devuelve la
        /// LONGITUD del plan (acciones escritas en 'planSalida' en orden de
        /// ejecución), 0 si la meta ya se cumple, o -1 si no hay plan.
        /// </summary>
        public int Planificar(in EstadoMundo inicial, in CondicionMundo meta,
                              IReadOnlyList<IAction> acciones, IAgentContext ctx,
                              IAction[] planSalida)
        {
            _numNodos = 0;
            _heapLen  = 0;
            NodosExplorados = 0;

            int raiz = CrearNodo(inicial, 0f, meta.Insatisfechos(inicial), -1, -1);
            HeapPush(raiz);

            int nAcc = acciones.Count;

            while (_heapLen > 0)
            {
                int actual = HeapPop();
                if (_nodos[actual].cerrado) continue;
                _nodos[actual].cerrado = true;
                NodosExplorados++;

                EstadoMundo estA = _nodos[actual].estado;
                float       gA   = _nodos[actual].g;

                if (meta.Cumple(estA))
                    return Reconstruir(actual, acciones, planSalida);

                for (int a = 0; a < nAcc; a++)
                {
                    IAction acc = acciones[a];
                    if (!acc.Precondiciones.Cumple(estA)) continue;
                    if (!acc.EsViable(ctx))                continue;

                    EstadoMundo sig = acc.Efectos.Aplicar(estA);
                    if (sig.hechos == estA.hechos) continue;   // efecto nulo → no progresa

                    float coste = acc.CalcularCoste(estA, ctx);
                    if (coste < 0f) coste = 0f;
                    float g = gA + coste;

                    int existente = Buscar(sig.hechos);
                    if (existente >= 0)
                    {
                        if (_nodos[existente].g <= g) continue;   // ya hay un camino igual o mejor
                        // Encontrado un camino mejor a un estado ya visto → reabrir.
                        _nodos[existente].g       = g;
                        _nodos[existente].f       = g + meta.Insatisfechos(sig);
                        _nodos[existente].accion  = a;
                        _nodos[existente].padre   = actual;
                        _nodos[existente].cerrado = false;
                        HeapPush(existente);
                    }
                    else
                    {
                        int idx = CrearNodo(sig, g, g + meta.Insatisfechos(sig), a, actual);
                        if (idx < 0) break;          // pool agotado → abandonamos esta rama
                        HeapPush(idx);
                    }
                }
            }
            return -1;   // sin plan
        }

        // ── Pool de nodos ───────────────────────────────────────────────────────
        int CrearNodo(in EstadoMundo e, float g, float f, int accion, int padre)
        {
            if (_numNodos >= _maxNodos) return -1;
            int i = _numNodos++;
            _nodos[i].estado  = e;
            _nodos[i].g       = g;
            _nodos[i].f       = f;
            _nodos[i].accion  = accion;
            _nodos[i].padre   = padre;
            _nodos[i].cerrado = false;
            return i;
        }

        int Buscar(ulong hechos)
        {
            for (int i = 0; i < _numNodos; i++)
                if (_nodos[i].estado.hechos == hechos) return i;
            return -1;
        }

        int Reconstruir(int nodo, IReadOnlyList<IAction> acciones, IAction[] salida)
        {
            int len = 0;
            for (int i = nodo; _nodos[i].padre >= 0; i = _nodos[i].padre) len++;

            int n = len <= salida.Length ? len : salida.Length;
            int w = n - 1;
            for (int i = nodo; _nodos[i].padre >= 0 && w >= 0; i = _nodos[i].padre)
                salida[w--] = acciones[_nodos[i].accion];   // de la meta hacia atrás → orden de ejecución
            return n;
        }

        // ── Min-heap binario por f (sobre índices de _nodos) ──────────────────────
        void HeapPush(int nodoIdx)
        {
            if (_heapLen >= _heap.Length) return;   // degradación elegante (no debería ocurrir)
            int i = _heapLen++;
            _heap[i] = nodoIdx;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (_nodos[_heap[p]].f <= _nodos[_heap[i]].f) break;
                Intercambiar(p, i);
                i = p;
            }
        }

        int HeapPop()
        {
            int cima = _heap[0];
            _heapLen--;
            if (_heapLen > 0)
            {
                _heap[0] = _heap[_heapLen];
                int i = 0;
                while (true)
                {
                    int l = (i << 1) + 1, r = l + 1, s = i;
                    if (l < _heapLen && _nodos[_heap[l]].f < _nodos[_heap[s]].f) s = l;
                    if (r < _heapLen && _nodos[_heap[r]].f < _nodos[_heap[s]].f) s = r;
                    if (s == i) break;
                    Intercambiar(i, s);
                    i = s;
                }
            }
            return cima;
        }

        void Intercambiar(int a, int b)
        {
            int t = _heap[a]; _heap[a] = _heap[b]; _heap[b] = t;
        }
    }
}
