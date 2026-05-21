// Assets/Scripts/Jobs/MatematicasAlsasua.cs
// ═══════════════════════════════════════════════════════════════════════════
//  MATEMÁTICAS ALSASUA — 5 algoritmos de alto impacto visual/gameplay
//
//  1. POISSON DISK SAMPLING (Bridson 2007)
//     Distribución espacialmente óptima de árboles y props urbanos.
//     Garantiza separación mínima entre puntos → aspecto natural.
//     O(n), Burst-compatible.
//
//  2. FRACTAL BROWNIAN MOTION (fBm) — Simplex Noise multifrecuencia
//     Mezcla de capas de terreno con variación a múltiples escalas.
//     6 octavas: macro (montañas) → micro (piedras, caminos).
//
//  3. REYNOLDS BOIDS (1986) — simulación de bandada/multitud
//     3 reglas: separación, alineación, cohesión.
//     Comportamiento emergente realista para manifestantes y fauna.
//     IJobParallelFor — un job por agente.
//
//  4. CATMULL-ROM SPLINES — suavizado de carreteras OSM
//     Convierte segmentos rectos en curvas suaves.
//     Garantiza continuidad C1 en cada waypoint.
//
//  5. MAPA DE DENSIDAD GAUSSIANA — distribución de NPCs y edificios
//     Suma de Gaussianas centradas en puntos clave (plazas, mercados).
//     Los NPCs se concentran naturalmente cerca de Herriko Plaza.
// ═══════════════════════════════════════════════════════════════════════════

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using System.Collections.Generic;
using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
//  1. POISSON DISK SAMPLING
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Algoritmo de Bridson para Poisson Disk Sampling 2D.
/// Genera N puntos con separación mínima garantizada (minDist) en una región.
///
/// Uso: PoissonDiskSampler.Generar(centro, radio, minDist, maxPuntos)
/// → reemplaza Random.Range en SistemaSueloAAA y AlsasuaTreeStreamer
/// → los bosques tendrán clusters naturales, no dispersión uniforme
/// </summary>
public static class PoissonDiskSampler
{
    const int K = 30; // intentos por punto activo (Bridson recomienda 30)

    /// <summary>
    /// Genera puntos con distribución de Poisson Disk en un círculo.
    /// </summary>
    public static List<Vector3> Generar(Vector3 centro, float radio, float minDist, int maxPuntos,
                                         float altura = 0f, uint semilla = 42)
    {
        var rng      = new Unity.Mathematics.Random(semilla);
        var resultado = new List<Vector3>();
        var activos  = new List<float2>();

        float cellSize = minDist / math.sqrt(2f);
        int   gridW    = Mathf.CeilToInt(radio * 2f / cellSize) + 1;
        var   grid     = new int[gridW, gridW]; // -1 = vacío, >=0 = índice en resultado
        for (int i = 0; i < gridW; i++)
            for (int j = 0; j < gridW; j++)
                grid[i, j] = -1;

        float2 origen = new float2(centro.x - radio, centro.z - radio);

        // Punto inicial en el centro
        float2 p0 = new float2(centro.x, centro.z);
        AnadirPunto(p0, resultado, activos, grid, gridW, cellSize, origen, altura);

        while (activos.Count > 0 && resultado.Count < maxPuntos)
        {
            int idx = (int)(rng.NextFloat() * activos.Count);
            float2 base_ = activos[idx];
            bool encontrado = false;

            for (int k = 0; k < K; k++)
            {
                // Candidato en anillo [minDist, 2*minDist]
                float angulo = rng.NextFloat() * math.PI * 2f;
                float dist   = minDist * (1f + rng.NextFloat());
                float2 cand  = base_ + new float2(math.cos(angulo), math.sin(angulo)) * dist;

                // Dentro del círculo
                if (math.length(cand - new float2(centro.x, centro.z)) > radio) continue;

                // Sin vecinos a < minDist
                if (!TienVecinos(cand, resultado, grid, gridW, cellSize, origen, minDist))
                {
                    AnadirPunto(cand, resultado, activos, grid, gridW, cellSize, origen, altura);
                    encontrado = true;
                    break;
                }
            }

            if (!encontrado) activos.RemoveAt(idx);
        }

        return resultado;
    }

    static void AnadirPunto(float2 p, List<Vector3> res, List<float2> act,
                             int[,] grid, int gridW, float cell, float2 orig, float y)
    {
        int gx = Mathf.Clamp((int)((p.x - orig.x) / cell), 0, gridW - 1);
        int gy = Mathf.Clamp((int)((p.y - orig.y) / cell), 0, gridW - 1);
        grid[gx, gy] = res.Count;
        res.Add(new Vector3(p.x, y, p.y));
        act.Add(p);
    }

    static bool TienVecinos(float2 p, List<Vector3> res,
                             int[,] grid, int gridW, float cell, float2 orig, float minDist)
    {
        int gx = (int)((p.x - orig.x) / cell);
        int gy = (int)((p.y - orig.y) / cell);
        int r  = 2;

        for (int dx = -r; dx <= r; dx++)
        for (int dy = -r; dy <= r; dy++)
        {
            int nx = gx + dx, ny = gy + dy;
            if (nx < 0 || nx >= gridW || ny < 0 || ny >= gridW) continue;
            int idx = grid[nx, ny];
            if (idx < 0) continue;
            float2 vecino = new float2(res[idx].x, res[idx].z);
            if (math.length(vecino - p) < minDist) return true;
        }
        return false;
    }
}

// ════════════════════════════════════════════════════════════════════════════
//  2. FRACTAL BROWNIAN MOTION — Simplex Noise
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// fBm (Fractal Brownian Motion) sobre Simplex Noise 2D.
/// Combina N octavas con amplitud y frecuencia crecientes.
/// Resultado en [-1, 1].
///
/// Parámetros típicos para Alsasua/Navarra:
///   octavas=6, lacunaridad=2.1, persistencia=0.45
/// </summary>
[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
public struct JobFBMTerrain : IJobParallelFor
{
    // Grid de entrada: (x, z) en coordenadas de terreno Unity
    [ReadOnly] public NativeArray<float2> coordenadas;
    [ReadOnly] public int   octavas;
    [ReadOnly] public float frecuenciaBase;    // 0.002 = variación macro
    [ReadOnly] public float lacunaridad;       // 2.1  = multiplicador frec por octava
    [ReadOnly] public float persistencia;      // 0.45 = multiplicador amp por octava
    [ReadOnly] public float2 offset;           // desplazamiento para variedad de semilla

    [WriteOnly] public NativeArray<float> resultado; // valor fBm en [-1,1]

    public void Execute(int i)
    {
        float2 p    = coordenadas[i] * frecuenciaBase + offset;
        float  valor = 0f;
        float  amp   = 1f;
        float  freq  = 1f;
        float  maxVal= 0f;

        for (int o = 0; o < octavas; o++)
        {
            valor  += SimplexNoise2D(p * freq) * amp;
            maxVal += amp;
            amp    *= persistencia;
            freq   *= lacunaridad;
        }

        resultado[i] = valor / maxVal; // normalizar a [-1, 1]
    }

    // Simplex Noise 2D — implementación Burst-compatible
    // Basada en el artículo de Stefan Gustavson (2012)
    static float SimplexNoise2D(float2 p)
    {
        const float F2 = 0.3660254f; // (sqrt(3)-1)/2
        const float G2 = 0.2113249f; // (3-sqrt(3))/6

        float2 i  = math.floor(p + (p.x + p.y) * F2);
        float2 x0 = p - i + (i.x + i.y) * G2;

        float2 i1 = x0.x > x0.y ? new float2(1, 0) : new float2(0, 1);
        float2 x1 = x0 - i1 + G2;
        float2 x2 = x0 - 1f + 2f * G2;

        int2 ii = (int2)(i - math.floor(i / 289f) * 289f);

        float n0 = ContribNoise(x0, Perm(ii.x + Perm(ii.y)));
        float n1 = ContribNoise(x1, Perm(ii.x + (int)i1.x + Perm(ii.y + (int)i1.y)));
        float n2 = ContribNoise(x2, Perm(ii.x + 1        + Perm(ii.y + 1)));

        return 70f * (n0 + n1 + n2);
    }

    static float ContribNoise(float2 x, int hash)
    {
        float t = 0.5f - x.x * x.x - x.y * x.y;
        if (t < 0f) return 0f;
        float2 g = Gradiente(hash & 7);
        return t * t * t * t * math.dot(g, x);
    }

    static float2 Gradiente(int h)
    {
        float2[] grad = {
            new float2(1,1),new float2(-1,1),new float2(1,-1),new float2(-1,-1),
            new float2(1,0),new float2(-1,0),new float2(0,1),new float2(0,-1)
        };
        return grad[h & 7];
    }

    static int Perm(int x)
    {
        // Permutation polynomial (Roberts hash)
        x = ((x >> 8) ^ x) * 0x45d9f3b;
        x = ((x >> 8) ^ x) * 0x45d9f3b;
        return ((x >> 8) ^ x) & 255;
    }
}

// ════════════════════════════════════════════════════════════════════════════
//  3. REYNOLDS BOIDS — simulación de multitud/bandada
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Un tick de simulación Boids para N agentes.
/// 3 reglas: separación (evitar colisiones), alineación (velocidad media),
/// cohesión (ir hacia el centro del grupo).
///
/// Aplicado a: manifestantes, fauna (ovejas, ciervos), vehículos NPC.
/// </summary>
[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
public struct JobBoidsUpdate : IJobParallelFor
{
    // Estado actual
    [ReadOnly] public NativeArray<float3> posiciones;
    [ReadOnly] public NativeArray<float3> velocidades;

    // Parámetros
    [ReadOnly] public float radioSeparacion;  // 2m  — no chocar
    [ReadOnly] public float radioAlineacion;  // 8m  — seguir dirección vecinos
    [ReadOnly] public float radioCohesion;    // 15m — ir hacia el grupo
    [ReadOnly] public float pesoSeparacion;   // 2.0 — más peso que cohesión
    [ReadOnly] public float pesoAlineacion;   // 1.0
    [ReadOnly] public float pesoCohesion;     // 0.8
    [ReadOnly] public float velocidadMax;     // 3.5 m/s manifestante
    [ReadOnly] public float fuerza;           // aceleración máxima por tick
    [ReadOnly] public float deltaTime;
    [ReadOnly] public float3 objetivo;        // punto de atracción (ej: pancarta)
    [ReadOnly] public float pesoObjetivo;     // 0.5

    // Resultados
    [WriteOnly] public NativeArray<float3> nuevasVelocidades;
    [WriteOnly] public NativeArray<float3> nuevasPosiciones;

    public void Execute(int i)
    {
        float3 pos = posiciones[i];
        float3 vel = velocidades[i];

        float3 fSeparacion = float3.zero;
        float3 fAlineacion = float3.zero;
        float3 fCohesion   = float3.zero;
        int    nAlin = 0, nCoh = 0;

        for (int j = 0; j < posiciones.Length; j++)
        {
            if (i == j) continue;
            float3 diff = pos - posiciones[j];
            diff.y = 0f;
            float distSq = math.dot(diff, diff);

            // Separación: fuerza inversamente proporcional a distancia
            if (distSq < radioSeparacion * radioSeparacion && distSq > 0.001f)
                fSeparacion += math.normalize(diff) / math.sqrt(distSq);

            // Alineación: promedio de velocidades de vecinos cercanos
            if (distSq < radioAlineacion * radioAlineacion)
            { fAlineacion += velocidades[j]; nAlin++; }

            // Cohesión: ir hacia el centro de masa del grupo
            if (distSq < radioCohesion * radioCohesion)
            { fCohesion += posiciones[j]; nCoh++; }
        }

        float3 steering = float3.zero;
        steering += fSeparacion                          * pesoSeparacion;
        if (nAlin > 0) steering += (fAlineacion / nAlin - vel) * pesoAlineacion;
        if (nCoh  > 0) steering += (fCohesion  / nCoh  - pos) * pesoCohesion;

        // Atracción hacia objetivo
        float3 haciaObj = objetivo - pos;
        haciaObj.y = 0f;
        if (math.length(haciaObj) > 0.1f)
            steering += math.normalize(haciaObj) * pesoObjetivo;

        // Aplicar steering con límite de fuerza
        steering = LimitarVec(steering, fuerza);
        float3 nuevaVel = LimitarVec(vel + steering * deltaTime, velocidadMax);
        nuevaVel.y = 0f; // no volar

        nuevasVelocidades[i] = nuevaVel;
        nuevasPosiciones[i]  = pos + nuevaVel * deltaTime;
    }

    static float3 LimitarVec(float3 v, float max)
    {
        float len = math.length(v);
        return len > max && len > 0f ? v * (max / len) : v;
    }
}

// ════════════════════════════════════════════════════════════════════════════
//  4. CATMULL-ROM SPLINES — suavizado de carreteras OSM
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Subdivide N waypoints rectos en una spline Catmull-Rom suave.
/// Garantiza continuidad C1 en cada punto de control.
/// Cada segmento [P1, P2] se subdivide en `divisiones` puntos.
/// </summary>
[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
public struct JobCatmullRomSubdividir : IJob
{
    [ReadOnly]  public NativeArray<float3> waypoints;  // puntos de control OSM
    [ReadOnly]  public int                  divisiones; // subdivisiones por segmento (8-16)
    [WriteOnly] public NativeArray<float3>  resultado;  // waypoints * divisiones puntos
    public NativeArray<int>                 conteo;     // [0] = puntos generados

    public void Execute()
    {
        int n = waypoints.Length;
        if (n < 2) { conteo[0] = 0; return; }

        int idx = 0;
        for (int i = 0; i < n - 1; i++)
        {
            // Puntos de control con phantom en extremos
            float3 p0 = i > 0     ? waypoints[i - 1] : waypoints[i] - (waypoints[i + 1] - waypoints[i]);
            float3 p1 = waypoints[i];
            float3 p2 = waypoints[i + 1];
            float3 p3 = i < n - 2 ? waypoints[i + 2] : waypoints[i + 1] + (waypoints[i + 1] - waypoints[i]);

            for (int d = 0; d < divisiones; d++)
            {
                float t  = (float)d / divisiones;
                float t2 = t * t;
                float t3 = t2 * t;

                // Catmull-Rom matrix (alpha=0.5)
                float3 punto =
                    0.5f * (
                        (-    p0 + 3f*p1 - 3f*p2 +    p3) * t3 +
                        ( 2f*p0 - 5f*p1 + 4f*p2 -    p3) * t2 +
                        (-    p0          +    p2         ) * t  +
                         2f*p1
                    );
                resultado[idx++] = punto;
            }
        }
        resultado[idx++] = waypoints[n - 1]; // último punto exacto
        conteo[0] = idx;
    }
}

// ════════════════════════════════════════════════════════════════════════════
//  5. MAPA DE DENSIDAD GAUSSIANA — distribución realista de NPCs
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Calcula la densidad en N posiciones candidatas como suma de Gaussianas.
/// Las zonas de alta densidad (plazas, mercados) atraen más NPCs.
///
/// Densidad(p) = Σ_i exp(-|p - centro_i|² / (2σ_i²)) * peso_i
/// </summary>
[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
public struct JobDensidadGaussiana : IJobParallelFor
{
    // Puntos candidatos a evaluar
    [ReadOnly] public NativeArray<float2> candidatos;

    // Centros de densidad alta (plazas, mercados, paradas de bus...)
    [ReadOnly] public NativeArray<float2> centros;
    [ReadOnly] public NativeArray<float>  sigmas;  // radio de influencia (m)
    [ReadOnly] public NativeArray<float>  pesos;   // importancia relativa

    // Resultado: valor de densidad en [0, 1] por candidato
    [WriteOnly] public NativeArray<float> densidad;

    public void Execute(int i)
    {
        float2 p   = candidatos[i];
        float  val = 0f;

        for (int j = 0; j < centros.Length; j++)
        {
            float2 d     = p - centros[j];
            float  distSq= math.dot(d, d);
            float  s2    = sigmas[j] * sigmas[j] * 2f;
            val += pesos[j] * math.exp(-distSq / s2);
        }

        densidad[i] = math.saturate(val); // clampar a [0,1]
    }
}

// ════════════════════════════════════════════════════════════════════════════
//  API DE ALTO NIVEL — puntos de densidad reales de Alsasua
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Centros de densidad de la vida urbana de Alsasua/Altsasua.
/// Coordenadas en Unity (X, Z) referenciadas al DEM.
/// Centro de referencia: Herriko Plaza = (1918, 8570).
/// </summary>
public static class DensidadAlsasua
{
    // (posX, posZ, sigma, peso)
    public static readonly (float x, float z, float sigma, float peso)[] CENTROS =
    {
        (1918f, 8570f,  80f, 1.0f),   // Herriko Plaza — máxima densidad
        (1860f, 8620f,  50f, 0.7f),   // Mercado
        (1970f, 8510f,  60f, 0.6f),   // Iglesia (Jasokundeko Andre Mariaren Eliza)
        (1920f, 8700f,  70f, 0.5f),   // Barrio Norte — zona residencial
        (1800f, 8450f,  40f, 0.4f),   // Estación de tren
        (2100f, 8600f,  45f, 0.3f),   // Cuartel de la Guardia Civil
        (1750f, 8800f,  90f, 0.5f),   // Polígono Ondarria
        (1900f, 8300f,  60f, 0.4f),   // Barrio Sur
    };

    /// <summary>
    /// Muestrea la densidad en una posición usando las Gaussianas de Alsasua.
    /// Resultado en [0, 1]. Sin necesidad de NativeArrays.
    /// </summary>
    public static float SamplearDensidad(float x, float z)
    {
        float total = 0f;
        foreach (var (cx, cz, sigma, peso) in CENTROS)
        {
            float dx = x - cx, dz = z - cz;
            float distSq = dx * dx + dz * dz;
            total += peso * math.exp(-distSq / (2f * sigma * sigma));
        }
        return math.saturate(total);
    }

    /// <summary>
    /// Samplea un punto aleatorio ponderado por densidad (rejection sampling).
    /// Útil para spawn de NPCs respetando el mapa urbano de Alsasua.
    /// </summary>
    public static Vector3 SamplearPuntoConDensidad(
        Vector3 centro, float radio, ref Unity.Mathematics.Random rng, int maxIntentos = 50)
    {
        for (int i = 0; i < maxIntentos; i++)
        {
            float angle  = rng.NextFloat() * math.PI * 2f;
            float dist   = math.sqrt(rng.NextFloat()) * radio; // distribución uniforme en disco
            float x      = centro.x + math.cos(angle) * dist;
            float z      = centro.z + math.sin(angle) * dist;
            float densid = SamplearDensidad(x, z);

            // Rejection sampling: aceptar con probabilidad proporcional a densidad
            if (rng.NextFloat() < densid + 0.1f) // +0.1 para no rechazar todo
                return new Vector3(x, centro.y, z);
        }
        return centro; // fallback
    }
}
