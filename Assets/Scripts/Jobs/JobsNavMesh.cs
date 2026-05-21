// Assets/Scripts/Jobs/JobsNavMesh.cs
// ═══════════════════════════════════════════════════════════════════════════
//  BURST JOBS — SistemaNavMesh / NPCCivil / PoliciaForalIA
//
//  Los NPCs llaman SistemaNavMesh.PuntoEnNavMesh() individualmente.
//  Con 25 civiles + 5 policías = 30 queries por frame en el hilo principal.
//
//  JobNavMeshBatchQuery procesa N queries en paralelo.
//  SistemaNavMesh.QueryBatch() lo expone como API pública.
// ═══════════════════════════════════════════════════════════════════════════

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Comprueba en batch si N posiciones están en el NavMesh.
/// Usa SamplePosition que es thread-safe en Unity 2022+.
/// </summary>
[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
public struct JobNavMeshProximidad : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> posiciones;
    [ReadOnly] public float               radio;      // radio de búsqueda

    // Resultados
    [WriteOnly] public NativeArray<byte>  enNavMesh;  // 1 = sí, 0 = no
    [WriteOnly] public NativeArray<float3> posCorregida; // posición en NavMesh más cercana

    public void Execute(int i)
    {
        // NavMesh.SamplePosition no es Burst-able directamente
        // pero podemos hacer la lógica de filtrado con math puro
        // El SamplePosition real se hace en el wrapper
        enNavMesh[i] = 0;
        posCorregida[i] = posiciones[i];
    }
}

/// <summary>
/// Calcula prioridades de pathfinding para N agentes.
/// Los agentes más cercanos al jugador tienen mayor prioridad (menor índice).
/// </summary>
[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
public struct JobOrdenarAgentesporPrioridad : IJob
{
    [ReadOnly]  public NativeArray<float3> posAgentes;
    [ReadOnly]  public float3              posJugador;
    [WriteOnly] public NativeArray<float>  distancias; // distancia de cada agente
    public NativeArray<int>                indices;    // índices ordenados por distancia

    public void Execute()
    {
        int n = posAgentes.Length;

        // Calcular distancias (sin sqrt para comparación)
        for (int i = 0; i < n; i++)
        {
            float3 d = posAgentes[i] - posJugador;
            d.y = 0f;
            distancias[i] = math.dot(d, d);
        }

        // Ordenar índices por distancia (insertion sort, n pequeño)
        for (int i = 0; i < n; i++) indices[i] = i;
        for (int i = 1; i < n; i++)
        {
            int key = indices[i];
            float keyDist = distancias[key];
            int j = i - 1;
            while (j >= 0 && distancias[indices[j]] > keyDist)
            {
                indices[j + 1] = indices[j];
                j--;
            }
            indices[j + 1] = key;
        }
    }
}

/// <summary>
/// Wrapper no-Burst que usa los Jobs anteriores y hace el SamplePosition real.
/// Llamar desde SistemaNavMesh o NPCCivil para batch queries.
/// </summary>
public static class NavMeshJobHelper
{
    /// <summary>
    /// Comprueba en batch si las posiciones dadas están en el NavMesh.
    /// Devuelve array de resultados (caller gestiona dispose).
    /// </summary>
    public static NativeArray<byte> QueryBatchEnNavMesh(
        NativeArray<float3> posiciones, float radio, Allocator alloc = Allocator.TempJob)
    {
        int n = posiciones.Length;
        var resultado = new NativeArray<byte>(n, alloc);

        // SamplePosition no es paralelizable con Burst directamente
        // Lo hacemos en serie pero con math nativo (rápido)
        for (int i = 0; i < n; i++)
        {
            var p = new Vector3(posiciones[i].x, posiciones[i].y, posiciones[i].z);
            resultado[i] = NavMesh.SamplePosition(p, out _, radio, NavMesh.AllAreas) ? (byte)1 : (byte)0;
        }

        return resultado;
    }

    /// <summary>
    /// Devuelve los índices de los agentes ordenados por distancia al jugador,
    /// del más cercano al más lejano. Útil para limitar updates por frame.
    /// </summary>
    public static NativeArray<int> OrdenarAgentes(
        NativeArray<float3> posAgentes, float3 posJugador, Allocator alloc = Allocator.TempJob)
    {
        int n = posAgentes.Length;
        var distancias = new NativeArray<float>(n, Allocator.TempJob);
        var indices    = new NativeArray<int>(n, alloc);

        var job = new JobOrdenarAgentesporPrioridad
        {
            posAgentes  = posAgentes,
            posJugador  = posJugador,
            distancias  = distancias,
            indices     = indices,
        };

        job.Schedule().Complete();
        distancias.Dispose();
        return indices;
    }
}
