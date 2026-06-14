// Assets/Scripts/Core/Espacial/OmniGridJobs.cs
// ═══════════════════════════════════════════════════════════════════════════
//  OMNI-GRID — JOBS BURST (capa CORE)
//
//  Fase 1 (esta): InsertarJob — escribe cada entidad en el grid Morton vía
//  ParallelWriter (lock-free, mismo patrón probado que Crowd/HashBoidsJob).
//
//  Fase 3 (futura, ver Docs §4.2): CalcularMortonJob + RadixSortJob + ReordenarJob
//  para la variante de array ordenado (coherencia de caché máxima).
// ═══════════════════════════════════════════════════════════════════════════

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

[BurstCompile]
public struct InsertarJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<SpatialData> datos;
    public float ox, oz;
    public NativeParallelMultiHashMap<uint, int>.ParallelWriter grid;

    public void Execute(int i)
        => grid.Add(OmniGridMath.Morton(OmniGridMath.Celda(datos[i].pos, ox, oz)), i);
}
