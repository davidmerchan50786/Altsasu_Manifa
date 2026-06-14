// Assets/Scripts/Crowd/MultitudJobs.cs
// ═══════════════════════════════════════════════════════════════════════════
//  MULTITUD — NÚCLEO DATA-ORIENTED (Burst + C# Job System)
//
//  Estructuras de valor + 3 jobs Burst que mueven la multitud sin un solo
//  GameObject por agente:
//    1. HashBoidsJob   — inserta cada agente en una rejilla espacial (hash)
//    2. FlockingJob    — separación + cohesión + alineación + atracción al
//                        objetivo (la plaza), leyendo vecinos O(1) por celda
//    3. BuildMatricesJob — convierte pos/vel → matrices TRS empaquetadas (3×4)
//                        listas para subir tal cual al GraphicsBuffer del BRG
//
//  Doble buffer (boidsIn → boidsOut): el job NUNCA lee y escribe el mismo
//  agente desde hilos distintos → cero condiciones de carrera, determinista.
//
//  El muestreo de suelo (Physics.Raycast) NO es Burst-compatible y se hace en
//  el hilo principal de forma escalonada (ver SistemaMultitudBRG); el job sólo
//  lee la altura ya resuelta desde un NativeArray<float>.
//
//  Capa: Alsasua.Crowd (leaf). Depende sólo de Unity.Mathematics/Collections.
// ═══════════════════════════════════════════════════════════════════════════

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Alsasua.Crowd
{
    /// <summary>Estado mínimo de un agente. struct de valor → array contiguo SoA-friendly.</summary>
    public struct Boid
    {
        public float3 pos;
        public float3 vel;
    }

    /// <summary>
    /// Versión Burst-friendly de EventoInfluencia (Core usa Vector3 en su contrato;
    /// aquí lo pasamos a float3 para el job). Pozo de gravedad social puntual.
    /// </summary>
    public struct EventoBurst
    {
        public float3 pos;
        public float  carga;   // + radicaliza, − enfría
        public float  radio;   // alcance; caída lineal hasta 0 en el borde
    }

    /// <summary>
    /// Matriz 3×4 (12 floats) en el layout exacto que espera el path de DOTS
    /// Instancing del BRG: tres columnas de rotación/escala + una de traslación,
    /// column-major, fila inferior (0,0,0,1) implícita. Empaqueta un float4x4.
    /// </summary>
    public struct PackedMatrix
    {
        public float c0x, c0y, c0z;
        public float c1x, c1y, c1z;
        public float c2x, c2y, c2z;
        public float c3x, c3y, c3z;

        public PackedMatrix(float4x4 m)
        {
            c0x = m.c0.x; c0y = m.c0.y; c0z = m.c0.z;
            c1x = m.c1.x; c1y = m.c1.y; c1z = m.c1.z;
            c2x = m.c2.x; c2y = m.c2.y; c2z = m.c2.z;
            c3x = m.c3.x; c3y = m.c3.y; c3z = m.c3.z;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  1. HASH ESPACIAL — un agente por celda (clave = hash de la celda XZ)
    // ════════════════════════════════════════════════════════════════════════
    [BurstCompile]
    public struct HashBoidsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Boid> boids;
        public float invCellSize;
        public NativeParallelMultiHashMap<int, int>.ParallelWriter grid;

        public void Execute(int i)
        {
            int2 c = (int2)math.floor(new float2(boids[i].pos.x, boids[i].pos.z) * invCellSize);
            grid.Add((int)math.hash(c), i);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  2. FLOCKING + GRAVEDAD SOCIAL — Reynolds (sep/coh/ali) + objetivo, y en el
    //     MISMO recorrido de vecinos 3×3: difusión de opinión + fuerza de
    //     interposición/huida según la opinión del agente. Coste extra marginal:
    //     una suma por vecino ya visitado; cero jobs/recorridos de grid nuevos.
    // ════════════════════════════════════════════════════════════════════════
    [BurstCompile]
    public struct FlockingJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Boid> boidsIn;
        [ReadOnly] public NativeParallelMultiHashMap<int, int> grid;
        [ReadOnly] public NativeArray<float> alturaSuelo;

        [WriteOnly] public NativeArray<Boid> boidsOut;

        // ── Opinión (capa social) ──
        [ReadOnly]  public NativeArray<float> opinionIn;
        [WriteOnly] public NativeArray<float> opinionOut;
        [ReadOnly]  public NativeArray<EventoBurst> eventos;   // capacidad fija; usar solo [0,nEventos)
        public int nEventos;
        [ReadOnly]  public NativeArray<float3> antagonistas;   // policías; usar solo [0,nAntagonistas)
        public int nAntagonistas;

        // Parámetros (valores, no managed → Burst los pone en registros)
        public float3 objetivo;
        public float dt;
        public float invCellSize;
        public float r2Sep, r2Coh, r2Ali;
        public float wSep, wCoh, wAli, wObj;
        public float velCrucero, velMax;

        // Parámetros sociales
        public float r2Social;        // radio² de difusión de opinión entre vecinos
        public float baseOpinion;     // baseline global [-1,1] (apoyo macro)
        public float kDif, kEvt, kDecay;
        public float umbralBloqueo;   // opinión a partir de la cual el agente se interpone
        public float r2Reaccion;      // radio² para detectar al policía más cercano
        public float wBloqueo, wHuida;

        public void Execute(int i)
        {
            Boid  b   = boidsIn[i];
            float opA = opinionIn[i];     // opinión actual (decide comportamiento)
            int2 cell = (int2)math.floor(new float2(b.pos.x, b.pos.z) * invCellSize);

            float3 sep = float3.zero, cohSum = float3.zero, aliSum = float3.zero;
            int nCoh = 0, nAli = 0;
            float opSum = 0f; int nOp = 0;   // difusión de opinión (gravedad social)

            // Vecindario 3×3 (cellSize = radio máximo → cubre todos los radios)
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int key = (int)math.hash(cell + new int2(dx, dz));
                if (!grid.TryGetFirstValue(key, out int j, out var it)) continue;
                do
                {
                    if (j == i) continue;
                    float3 d = b.pos - boidsIn[j].pos;
                    d.y = 0f;
                    float dd = math.lengthsq(d);

                    if (dd < r2Sep && dd > 1e-4f) sep += d * math.rsqrt(dd); // 1/dist → más cerca, más empuje
                    if (dd < r2Coh) { cohSum += boidsIn[j].pos; nCoh++; }
                    if (dd < r2Ali) { aliSum += boidsIn[j].vel; nAli++; }
                    if (dd < r2Social) { opSum += opinionIn[j]; nOp++; }   // humor local
                }
                while (grid.TryGetNextValue(out j, ref it));
            }

            float3 fObj = math.normalizesafe(objetivo - b.pos) * wObj;
            float3 fSep = sep * wSep;
            float3 fCoh = nCoh > 0 ? math.normalizesafe(cohSum / nCoh - b.pos) * wCoh : float3.zero;
            float3 fAli = nAli > 0 ? math.normalizesafe(aliSum / nAli) * wAli : float3.zero;

            float3 accel = fObj + fSep + fCoh + fAli;

            // ── Fuerza social: interponerse (opinión alta) o abrir paso (opinión baja) ──
            if (nAntagonistas > 0)
            {
                float best2 = r2Reaccion; int bi = -1;
                for (int p = 0; p < nAntagonistas; p++)
                {
                    float3 dp = antagonistas[p] - b.pos; dp.y = 0f;
                    float d2 = math.lengthsq(dp);
                    if (d2 < best2) { best2 = d2; bi = p; }
                }
                if (bi >= 0)
                {
                    float3 pol = antagonistas[bi];
                    if (opA > umbralBloqueo)
                    {
                        // muro humano: situarse ENTRE el policía y su objetivo (la marcha)
                        float3 puntoInterp = pol + math.normalizesafe(objetivo - pol) * 1.5f;
                        accel += math.normalizesafe(puntoInterp - b.pos) * (wBloqueo * opA);
                    }
                    else if (opA < -0.1f)
                    {
                        // asustado/hostil: apartarse del policía (abre paso)
                        accel += math.normalizesafe(b.pos - pol) * (wHuida * -opA);
                    }
                }
            }

            accel.y = 0f;
            b.vel += accel * dt;
            b.vel.y = 0f;

            float spd = math.length(b.vel);
            if (spd > velMax) b.vel *= velMax / spd;
            else if (spd < 0.05f) b.vel = math.normalizesafe(objetivo - b.pos) * (velCrucero * 0.5f);

            b.pos += b.vel * dt;

            // Suelo resuelto en el hilo principal (Physics no es Burst-safe).
            float yS = alturaSuelo[i];
            if (yS > -9000f) b.pos.y = yS;

            boidsOut[i] = b;

            // ── Actualización de opinión (difusión + pozos de evento + relajación) ──
            float media = nOp > 0 ? opSum / nOp : opA;
            float dOp = kDif * (media - opA) + kDecay * (baseOpinion - opA);
            for (int e = 0; e < nEventos; e++)
            {
                float3 de = eventos[e].pos - b.pos; de.y = 0f;
                float dist2 = math.lengthsq(de);
                float r = eventos[e].radio;
                if (dist2 < r * r)
                {
                    float caida = 1f - math.sqrt(dist2) / r;   // lineal: 1 en el foco, 0 en el borde
                    dOp += kEvt * eventos[e].carga * caida;
                }
            }
            opinionOut[i] = math.clamp(opA + dOp * dt, -1f, 1f);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  3. MATRICES TRS — pos/vel → objectToWorld + worldToObject empaquetadas
    // ════════════════════════════════════════════════════════════════════════
    [BurstCompile]
    public struct BuildMatricesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Boid> boids;
        public float3 escala;
        public float alturaPivote;   // sube el centro de la cápsula sobre el suelo

        [WriteOnly] public NativeArray<PackedMatrix> objectToWorld;
        [WriteOnly] public NativeArray<PackedMatrix> worldToObject;

        public void Execute(int i)
        {
            Boid b = boids[i];
            float3 fwd = new float3(b.vel.x, 0f, b.vel.z);
            quaternion rot = math.lengthsq(fwd) > 1e-3f
                ? quaternion.LookRotationSafe(fwd, math.up())
                : quaternion.identity;

            float3 pos = b.pos + new float3(0f, alturaPivote, 0f);
            float4x4 m = float4x4.TRS(pos, rot, escala);

            objectToWorld[i] = new PackedMatrix(m);
            worldToObject[i] = new PackedMatrix(math.inverse(m));
        }
    }
}
