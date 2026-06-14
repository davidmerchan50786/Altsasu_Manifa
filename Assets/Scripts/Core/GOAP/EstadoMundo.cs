// Assets/Scripts/Core/GOAP/EstadoMundo.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GOAP — ESTADO DEL MUNDO SIMBÓLICO (zero-alloc)
//
//  El planificador FEAR/GOAP razona sobre HECHOS booleanos, no sobre floats.
//  Aquí cada hecho es UN BIT de un ulong → hasta 64 átomos por agente. Comparar,
//  aplicar efectos y medir distancia al objetivo son operaciones de bits O(1) sin
//  una sola asignación de heap → planificar miles de veces por segundo no genera
//  basura para el GC.
//
//    EstadoMundo    — asignación completa de los 64 átomos (un ulong de valores).
//    CondicionMundo — (máscara, valores): qué átomos importan y qué valor exigen.
//                     Sirve de precondición, de efecto y de objetivo.
//
//  Los ÍNDICES de los átomos los define cada dominio (p. ej. enum HechoPol en la
//  capa Runtime); Core se mantiene agnóstico.
//
//  Capa CORE: sin dependencias.
// ═══════════════════════════════════════════════════════════════════════════

namespace Alsasua.GOAP
{
    /// <summary>Asignación booleana de hasta 64 átomos. Struct de valor → copiar
    /// un estado es copiar un ulong; cero presión sobre el GC.</summary>
    public struct EstadoMundo
    {
        public ulong hechos;   // bit i = valor booleano del átomo i

        public void Set(int atomo, bool valor)
        {
            ulong bit = 1UL << atomo;
            if (valor) hechos |= bit; else hechos &= ~bit;
        }

        public bool Get(int atomo) => (hechos & (1UL << atomo)) != 0UL;
    }

    /// <summary>
    /// (máscara, valores) sobre los átomos. La máscara marca QUÉ átomos importan;
    /// 'valores' qué valor se exige en esos átomos. Inmutable y readonly → el
    /// compilador la pasa por registro sin copias defensivas.
    /// </summary>
    public readonly struct CondicionMundo
    {
        public readonly ulong mascara;   // átomos relevantes
        public readonly ulong valores;   // valor exigido de esos átomos

        public CondicionMundo(ulong mascara, ulong valores)
        {
            this.mascara = mascara;
            this.valores = valores;
        }

        /// <summary>¿El estado satisface esta condición? (solo cuentan los bits de la máscara)</summary>
        public bool Cumple(in EstadoMundo e) => (e.hechos & mascara) == (valores & mascara);

        /// <summary>Aplica esta condición como EFECTO sobre un estado y devuelve el
        /// nuevo estado. Pisa solo los átomos de la máscara. Sin alocar.</summary>
        public EstadoMundo Aplicar(in EstadoMundo e)
        {
            EstadoMundo r;
            r.hechos = (e.hechos & ~mascara) | (valores & mascara);
            return r;
        }

        /// <summary>Nº de átomos exigidos que el estado AÚN no cumple. Heurística
        /// admisible para A*: cada átomo pendiente necesita ≥1 acción.</summary>
        public int Insatisfechos(in EstadoMundo e) => PopCount((e.hechos ^ valores) & mascara);

        // ── Builder ergonómico (solo en setup; igual es zero-alloc: todo structs) ──
        public static Constructor Nueva() => default;

        public struct Constructor
        {
            ulong _m, _v;
            public Constructor Con(int atomo, bool valor)
            {
                ulong bit = 1UL << atomo;
                _m |= bit;
                if (valor) _v |= bit;
                return this;
            }
            public CondicionMundo Construir() => new CondicionMundo(_m, _v);
        }

        // popcount paralelo de 64 bits (sin System.Numerics: no garantizado en Mono/IL2CPP viejo)
        static int PopCount(ulong x)
        {
            x -=  (x >> 1) & 0x5555555555555555UL;
            x  =  (x & 0x3333333333333333UL) + ((x >> 2) & 0x3333333333333333UL);
            x  =  (x + (x >> 4)) & 0x0f0f0f0f0f0f0f0fUL;
            return (int)((x * 0x0101010101010101UL) >> 56);
        }
    }
}
