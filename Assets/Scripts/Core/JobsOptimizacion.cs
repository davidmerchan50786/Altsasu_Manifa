// Assets/Scripts/Jobs/JobsOptimizacion.cs
// ═══════════════════════════════════════════════════════════════════════════
//  BURST JOBS — SistemaOptimizacion / SistemaZonas
//
//  Jobs genéricos de optimización del simulador:
//    JobCullDistancia      → LOD/cull por distancia para N objetos
//    JobActualizarPosLOD   → actualiza LOD groups según distancia
//    JobCalcularZonaJugador → determina en qué zona está el jugador
// ═══════════════════════════════════════════════════════════════════════════

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// Determina el nivel de LOD para N objetos según su distancia al jugador.
/// 0 = full, 1 = medio, 2 = bajo, 3 = culled.
/// </summary>
[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
public struct JobCalcularNivelesLOD : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> posicionesObjetos;
    [ReadOnly] public float3              posJugador;
    [ReadOnly] public float               dist0; // < dist0 = LOD 0 (full)
    [ReadOnly] public float               dist1; // < dist1 = LOD 1
    [ReadOnly] public float               dist2; // < dist2 = LOD 2
    // >= dist2 = culled (LOD 3)

    [WriteOnly] public NativeArray<byte>  nivelLOD;

    public void Execute(int i)
    {
        float3 d   = posicionesObjetos[i] - posJugador;
        d.y        = 0f;
        float distSq = math.dot(d, d);

        if      (distSq < dist0 * dist0) nivelLOD[i] = 0;
        else if (distSq < dist1 * dist1) nivelLOD[i] = 1;
        else if (distSq < dist2 * dist2) nivelLOD[i] = 2;
        else                              nivelLOD[i] = 3;
    }
}

/// <summary>
/// Clasifica N objetos estáticos en 3 bandas según su distancia XZ al jugador, con
/// HISTÉRESIS DIRECCIONAL (el estado actual aguanta un margen extra antes de cambiar →
/// sin parpadeo en el borde). Lo usa StreamerMundoEstatico con el radio dinámico del
/// gobernador de render. Bandas: 0 = Activo (detalle completo), 1 = Impostor (LOD bajo +
/// sin sombras), 2 = Oculto (SetActive false).
/// </summary>
[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
public struct JobBandasMundo : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> posiciones;
    [ReadOnly] public NativeArray<byte>   estadoActual; // 0=Activo 1=Impostor 2=Oculto
    [ReadOnly] public float3              posJugador;
    [ReadOnly] public float               radioActivar;  // < esto = Activo
    [ReadOnly] public float               radioImpostor; // < esto = Impostor; >= = Oculto
    [ReadOnly] public float               histeresis;    // margen pegajoso (m)

    [WriteOnly] public NativeArray<byte>  estadoNuevo;

    public void Execute(int i)
    {
        float3 dlt = posiciones[i] - posJugador;
        dlt.y = 0f;
        float d = math.sqrt(math.dot(dlt, dlt));

        float rA = radioActivar;
        float rI = radioImpostor;
        float h  = histeresis;
        byte cur = estadoActual[i];

        // Para SUBIR de detalle hay que cruzar el umbral menos el margen; para BAJAR,
        // el umbral más el margen. Así un objeto en el borde no oscila de estado.
        byte nuevo;
        if (cur == 0)                                   // Activo: aguanta hasta rA+h
            nuevo = (d <= rA + h) ? (byte)0 : (d <= rI + h) ? (byte)1 : (byte)2;
        else if (cur == 1)                              // Impostor
            nuevo = (d <= rA - h) ? (byte)0 : (d <= rI + h) ? (byte)1 : (byte)2;
        else                                            // Oculto
            nuevo = (d <= rA - h) ? (byte)0 : (d <= rI - h) ? (byte)1 : (byte)2;

        estadoNuevo[i] = nuevo;
    }
}

/// <summary>
/// Filtra N posiciones por frustum (campo de visión de la cámara).
/// Devuelve 1 si el objeto es visible, 0 si está fuera del frustum.
/// </summary>
[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
public struct JobFrustumCull : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3>    posiciones;
    [ReadOnly] public NativeArray<float4>    planosFrustum; // 6 planos: normal.xyz + d
    [ReadOnly] public NativeArray<float>     radios;        // bounding sphere radius

    [WriteOnly] public NativeArray<byte>     visible; // 1 = visible, 0 = culled

    public void Execute(int i)
    {
        float3 c = posiciones[i];
        float  r = radios[i];

        for (int p = 0; p < 6; p++)
        {
            float4 plano = planosFrustum[p];
            float dist   = math.dot(plano.xyz, c) + plano.w;
            if (dist < -r) { visible[i] = 0; return; }
        }
        visible[i] = 1;
    }
}

/// <summary>
/// Determina qué zona (grid cell) corresponde a una posición.
/// Usado por SistemaZonas para el streaming de chunks.
/// </summary>
[BurstCompile]
public struct JobCalcularZonaDeMultiplesPuntos : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3>      posiciones;
    [ReadOnly] public float                    tamañoCelda;
    [ReadOnly] public float2                   origen; // origen del grid en Unity XZ

    [WriteOnly] public NativeArray<int2>       zonas; // (gridX, gridZ) para cada posición

    public void Execute(int i)
    {
        float3 pos = posiciones[i];
        zonas[i] = new int2(
            (int)math.floor((pos.x - origen.x) / tamañoCelda),
            (int)math.floor((pos.z - origen.y) / tamañoCelda)
        );
    }
}

/// <summary>
/// Acumula las posiciones de todos los NPCs/objetos activos en un frame
/// para pasarlas a otros Jobs sin acceder al hilo principal.
/// </summary>
[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
public struct JobAgregarPosiciones : IJob
{
    [ReadOnly]  public NativeArray<float3> origen;
    [ReadOnly]  public NativeArray<byte>   activo;    // 1 = activo, 0 = inactivo
    [WriteOnly] public NativeArray<float3> resultado;
    public NativeArray<int> conteo; // [0] = número de activos

    public void Execute()
    {
        int n = 0;
        for (int i = 0; i < origen.Length; i++)
            if (activo[i] == 1) resultado[n++] = origen[i];
        conteo[0] = n;
    }
}
