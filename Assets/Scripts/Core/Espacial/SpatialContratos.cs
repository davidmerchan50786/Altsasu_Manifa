// Assets/Scripts/Core/Espacial/SpatialContratos.cs
// ═══════════════════════════════════════════════════════════════════════════
//  OMNI-GRID — CONTRATOS (capa CORE)
//
//  Partición espacial unificada. Core SOLO conoce datos genéricos: una posición,
//  un ID OPACO (índice que define el productor en SU propio array) y unos bits de
//  tipo para filtrar. Core NUNCA sabe qué es un "Policía" o un "Edificio" → las
//  capas de arriba inyectan SpatialData y consumen ints; cero acoplamiento cruzado.
//
//  Implementación: UnifiedSpatialGrid (esta misma capa Core, plano, sin GameObject).
//  Se expone por ServiceLocator<IUnifiedSpatialGrid> y se arranca en OmniGridLoop.
//
//  Diseño completo: Docs/arquitectura_omnigrid.md
// ═══════════════════════════════════════════════════════════════════════════

using Unity.Collections;
using Unity.Mathematics;

/// <summary>Tipos de entidad como bitmask → las queries filtran por capa sin
/// conocer ninguna clase concreta. Combinables con OR.</summary>
[System.Flags]
public enum TipoEspacial : uint
{
    Ninguno        = 0,
    Manifestante   = 1u << 0,
    Policia        = 1u << 1,
    Vehiculo       = 1u << 2,
    Jugador        = 1u << 3,
    PropDestruible = 1u << 4,
    ProxyMesh      = 1u << 5,   // culling gráfico → BatchRendererGroup
    ChunkAncla     = 1u << 6,   // streaming → Addressables
    Todos          = 0xFFFFFFFF
}

/// <summary>El "contrato" que las capas de arriba inyectan cada frame. Blittable
/// (sólo tipos de valor) → válido en Jobs/Burst y en NativeArray contiguos.</summary>
public struct SpatialData
{
    public float3       pos;    // posición mundo (Y incluido aunque el grid indexe XZ)
    public int          id;     // ID OPACO definido por el productor (índice en SU array)
    public TipoEspacial tipo;   // bits de capa para filtrar
    public float        radio;  // tamaño/influencia (para tests de frustum y rangos)
}

/// <summary>
/// Índice espacial unificado del mundo. API inmediata: los productores hacen
/// Submit CADA frame (el grid se reconstruye); los consumidores leen el grid
/// ESTABLE del frame (coherencia N-1, ver Docs/arquitectura_omnigrid.md §5).
/// </summary>
public interface IUnifiedSpatialGrid
{
    // ── INYECCIÓN (productores, durante su Update; alimenta el grid del PRÓXIMO frame) ──
    /// <summary>Encola una entidad para el grid del próximo frame. Hilo principal.</summary>
    void Submit(in SpatialData d);
    /// <summary>Encola un lote (crowd, tráfico). Hilo principal, tras completar sus jobs.</summary>
    void SubmitBatch(NativeArray<SpatialData> lote);

    // ── GOD QUERIES (consumidores; sobre el grid estable de este frame) ──
    /// <summary>IDs de entidades del tipo dado en un radio. Llena `resultado` (se limpia).</summary>
    void QueryRadio(float3 centro, float radio, TipoEspacial filtro, NativeList<int> resultado);
    /// <summary>Sólo cuenta (sin asignar lista). Para densidad de simulación / heurísticas.</summary>
    int  QueryRadioContar(float3 centro, float radio, TipoEspacial filtro);
    /// <summary>POSICIONES de las entidades del tipo en el radio (no ids). Para fuerzas
    /// que necesitan la posición del vecino (interposición, depenetración). Llena `resultado`.</summary>
    void QueryRadioPos(float3 centro, float radio, TipoEspacial filtro, NativeList<float3> resultado);
    /// <summary>IDs dentro del frustum (6 planos float4 = (normal.xyz, distancia), inward).
    /// `centroCam`/`alcance` acotan la región barrida (típicamente cámara + far plane).</summary>
    void QueryFrustum(NativeArray<float4> planos6, float3 centroCam, float alcance,
                      TipoEspacial filtro, NativeList<int> resultado);
    /// <summary>Códigos Morton de las celdas que acaban de ENTRAR en el disco de `radio`
    /// (están en `ahora` pero no estaban en `antes`) → disparar streaming. No usa el grid.</summary>
    void QueryCeldasNuevas(float3 antes, float3 ahora, float radio, NativeList<uint> celdasEntrantes);

    // ── ENTIDADES RETENIDAS (productores de BAJA frecuencia o estáticos: ghosts,
    //    anclas de streaming, proxies estáticos). A diferencia de Submit (immediate-
    //    mode, hay que reenviar cada frame), estas PERSISTEN entre frames hasta Quitar. ──
    /// <summary>Inscribe una entidad persistente; devuelve un handle estable (guárdalo).</summary>
    int  SubmitRetenido(in SpatialData d);
    /// <summary>Actualiza la posición de una entidad retenida (barato; sin reinscribir).</summary>
    void ActualizarRetenido(int handle, float3 pos);
    /// <summary>Da de baja una entidad retenida.</summary>
    void QuitarRetenido(int handle);

    // ── ACCESO BURST DIRECTO (consumidores avanzados, p.ej. FlockingJob) ──
    /// <summary>Mapa Morton→índice del grid de este frame. SOLO LECTURA; no disponer.
    /// Cópialo a un campo [ReadOnly] de tu job para iterar vecinos dentro de Burst.</summary>
    NativeParallelMultiHashMap<uint, int> GridMorton { get; }
    /// <summary>Datos del frame, en el MISMO índice que referencia GridMorton.
    /// SOLO LECTURA; no disponer.</summary>
    NativeArray<SpatialData> Datos { get; }

    /// <summary>True cuando hay un grid construido y consultable.</summary>
    bool Listo { get; }
    /// <summary>Nº de entidades en el grid de este frame.</summary>
    int Conteo { get; }
}
