# Arquitectura — Omni-Grid (`UnifiedSpatialGrid`)

> **Prompt Definitivo II** · Partición espacial unificada en la capa **Core**.
> Estado (2026-06-14): **Fase 0 + Fase 1 IMPLEMENTADAS** (`Assets/Scripts/Core/Espacial/`,
> aditivo, sin tocar lógica existente). Pendientes: migrar consumidores (Fase 2+),
> variante de array ordenado/radix (Fase 3). Antes existían dos hashes espaciales
> locales e incompatibles (`SistemaMultitud` managed, `Crowd/MultitudJobs.HashBoidsJob`
> Burst) y **~140 consultas de distancia dispersas en 73 archivos** que este sistema
> viene a unificar.

> ### Estado de implementación
> | Archivo (`Assets/Scripts/Core/Espacial/`) | Contenido | Estado |
> |---|---|---|
> | `SpatialContratos.cs` | `SpatialData`, `TipoEspacial`, `IUnifiedSpatialGrid` | ✅ |
> | `OmniGridMatematicas.cs` | Morton/Celda/CentroCelda (+ gemelo HLSL en comentario) | ✅ |
> | `OmniGridJobs.cs` | `InsertarJob` (Burst, `ParallelWriter`) | ✅ |
> | `UnifiedSpatialGrid.cs` | doble buffer N-1 + las 4 queries | ✅ |
> | `OmniGridLoop.cs` | arranque + inyección en `EarlyUpdate` del PlayerLoop | ✅ |
> | `GridConsultas.cs` | wrapper ergonómico para MonoBehaviours | ✅ |
> | Vía de entidades **retenidas** (`SubmitRetenido`/`ActualizarRetenido`/`QuitarRetenido`) | productores de baja frecuencia/estáticos: ghosts, anclas de streaming, proxies | ✅ (2026-06-14) |
> | **Primer productor real**: ghosts del Sim-LOD (`PoolGhosts`) publican al grid | IA/streaming "ven" miles de NPC sin GameObject | ✅ (2026-06-14) |
> | **Productores activos**: `NPCBase` (cada frame, Actor/Proxy) + `PublicadorGrid` (drop-on: jugador, vehículos, props) | el grid pasa de vacío a poblado con todo el mundo | ✅ (2026-06-14) |
> | **Primer consumidor**: el crowd saca los policías del grid (`QueryRadioPos`, conciencia cruzada) | gated `usarGridParaPolicias` (default OFF) | ✅ (2026-06-14) |
> | Variante array ordenado + radix sort (Fase 3) | coherencia de caché máxima | ⏳ diseñado |
> | Migración de `MultitudJobs` y los ~140 call-sites (Fase 2+) | — | ⏳ |
>
> **Nota immediate-mode vs retenido**: `Submit` es por-frame (reenviar cada frame o desaparece);
> `SubmitRetenido` persiste hasta `Quitar` → resuelve el desajuste con productores lentos como
> `PoolGhosts` (1 Hz) y es la vía natural para `ChunkAncla` (streaming) y proxies estáticos.
>
> **Sin verificar en el editor de Unity** (no compilado aún): revisar consola y un Play
> antes de migrar consumidores. La latencia N-1 del grid lo hace inadecuado para físicas
> reactivas instantáneas — para eso queda el `Raycast` directo.

---

## 0. Veredicto del estado actual (lo que reemplaza)

| Pieza existente | Qué es | Por qué no sirve como Omni-Grid |
|---|---|---|
| [`SistemaMultitud._gridBuckets`](../Assets/Scripts/Core/SistemaMultitud.cs#L102) | Rejilla `96×96` `int[][]`, sigue el centroide | Managed (GC), no-Burst, 2D, exclusiva de esa multitud |
| [`HashBoidsJob`](../Assets/Scripts/Crowd/MultitudJobs.cs#L72) | `NativeParallelMultiHashMap<int,int>`, Burst | 2D, clave `math.hash` (no Morton), se descarta cada frame, sólo conoce `Boid`, vive en `Alsasua.Crowd` (leaf) |
| ~140 `OverlapSphere`/`Vector3.Distance`/`FindObjectsOfType` | Consultas O(n) ad-hoc | Lo que el Omni-Grid debe absorber |

El precedente Burst (`HashBoidsJob` + `ParallelWriter` + doble buffer) es **correcto en patrón**;
el diseño lo **eleva a Core** y lo generaliza a todas las entidades del juego.

---

## 1. Objetivos y no-objetivos

**Objetivos**
1. Una **única** estructura nativa en `Core` con consciencia espacial de TODO el mundo (manifestantes, policías, coches, props destructibles, proxy-meshes).
2. Reconstrucción **< 0.5 ms** por frame con miles de entidades, thread-safe.
3. Tres "God Queries" (radio, frustum, delta de celdas) en O(k) / O(log N).
4. **Cero acoplamiento cruzado**: Core no sabe qué es un "Policía"; sólo maneja `int id` + bits de tipo + `float3 pos`. Las capas de arriba inyectan y consumen vía contrato.
5. Integración limpia con el `GlobalSimulationOrchestrator` y el `FactorCarga` (degrade dinámico).

**No-objetivos (v1)**
- No es un broadphase de físicas (eso lo hace Havok/PhysX). El grid sirve a IA, gráficos y streaming, no resuelve colisiones.
- No reemplaza el NavMesh ni el pathfinding; alimenta la *separación* (flocking), no las rutas.
- No persiste entre frames como "verdad" — es un índice volátil reconstruido (con doble buffer de coherencia N-1, ver §5).

---

## 2. Decisiones de diseño clave (y su porqué)

| Decisión | Elección | Razón |
|---|---|---|
| Dimensionalidad | **2D (XZ) Morton de 32 bits**, `Y` guardado en `SpatialData` | Altsasu es un mundo 2.5D (ciudad sobre terreno). Las consultas de IA/streaming/culling son de plano-suelo. 3D volumétrico (64-bit) queda como extensión §9. |
| Estructura | **Array ordenado por código Morton + búsqueda binaria** (primaria) · `NativeParallelMultiHashMap` (v1 puente) | El array ordenado es donde Morton *paga*: vecinos espaciales quedan contiguos en RAM → coherencia de caché real. Lookup O(log N), sin asignación densa de 3M celdas. |
| Construcción | **Radix sort Burst** de claves uint | Sub-0.5 ms para 5–20k entidades; determinista; paralelizable. |
| Inserción paralela | `ParallelWriter` (v1) / histograma atómico (radix) | Lock-free, ya probado en `HashBoidsJob`. |
| Coherencia temporal | **Doble buffer, lectura N-1** | Elimina por completo los hazards de orden de `Update` entre productores y consumidores (ver §5). 16 ms de latencia es invisible para IA/streaming/culling. |
| Ubicación | `Assets/Scripts/Core/Espacial/` (capa Core) | Todas las capas pueden referenciar Core; nadie rompe la dirección de dependencias. |
| Disparo | Sistema inyectado en **`EarlyUpdate`** del PlayerLoop | Igual técnica que `GlobalSimulationOrchestrator` (que se inyecta en `Update`). Construye ANTES de los `Update` → consumidores leen un grid estable todo el frame. |

### ¿Por qué Morton garantiza coherencia de caché?

El código Morton (Z-order curve) **entrelaza los bits** de las coordenadas de celda
`(cx, cz)`. Dos entidades cercanas en el mundo producen códigos Morton numéricamente
cercanos. Si **ordenamos el array de entidades por ese código**, las entidades vecinas
en el espacio quedan **físicamente contiguas en memoria RAM**.

Consecuencia práctica: cuando el `FlockingJob` recorre el vecindario 3×3 de un agente,
los datos que toca caen casi siempre en la **misma línea de caché** (o líneas
prefetcheadas), en vez de saltar por toda la heap como hace un `int[][]` de buckets
o un hashmap (cuyos cubos están dispersos). Eso es la diferencia entre ~5 % y ~40 % de
*cache-hit* en el bucle más caliente del juego.

```
Coords mundo XZ ──quantize──► celda (cx,cz) ──interleave bits──► Morton uint
                                                                     │ sort
   memoria:  [e0][e1][e2][e3]...   ◄── entidades contiguas == vecinas espaciales
```

---

## 3. Pilar 1 — Estructura matemática (Morton / Z-curve)

### 3.1 Quantización mundo → celda

```csharp
// Core/Espacial/OmniGridMatematicas.cs   (Burst-compatible, sin estado)
using Unity.Mathematics;

public static class OmniGridMath
{
    // Origen = Herriko Plaza (OX, OZ de GeoDataAlsasua). BIAS mantiene la celda en [0, 65535].
    public const float  CELL_SIZE = 16f;          // m. Configurable. Ver tradeoff §3.4.
    public const float  INV_CELL  = 1f / CELL_SIZE;
    public const int    BIAS      = 1 << 15;       // 32768 → centro del rango de 16 bits

    public static uint2 Celda(float3 p, float ox, float oz)
    {
        int cx = (int)math.floor((p.x - ox) * INV_CELL) + BIAS;
        int cz = (int)math.floor((p.z - oz) * INV_CELL) + BIAS;
        return (uint2)math.clamp(new int2(cx, cz), 0, 0xFFFF);
    }

    // Entrelazado 16+16 → 32 bits (Morton 2D).
    public static uint Morton(uint2 c) => Part1By1(c.x) | (Part1By1(c.y) << 1);

    static uint Part1By1(uint x)        // "spread": separa 16 bits con un hueco entre cada uno
    {
        x &= 0x0000FFFF;
        x = (x | (x << 8)) & 0x00FF00FF;
        x = (x | (x << 4)) & 0x0F0F0F0F;
        x = (x | (x << 2)) & 0x33333333;
        x = (x | (x << 1)) & 0x55555555;
        return x;
    }
}
```

El mismo `Part1By1` portado a HLSL sirve para el lado GPU (culling en compute, §8.3) →
una sola definición matemática del espacio compartida CPU/GPU.

### 3.2 Estructura de datos (variante primaria: array ordenado por Morton)

```
Por frame, sobre el snapshot N-1:
  keys[]      : NativeArray<uint>      Morton de cada entidad
  orden[]     : NativeArray<int>       índices ordenados por keys (radix sort)
  datos[]     : NativeArray<SpatialData> reordenado físicamente según `orden`  ← contiguo
  cellKeys[]  : NativeArray<uint>      Morton del primer elemento de cada run (para binary search)
```

Consulta de una celda → `BinarySearch(keys, mortonCelda)` localiza el inicio del *run*
contiguo de esa celda; se itera mientras `keys[i] == mortonCelda`. **O(log N)** para
encontrar la celda, **O(ocupación)** para leerla, **contiguo en memoria**.

### 3.3 Variante v1 puente (hashmap)

Para migrar consumidores YA sin esperar al radix sort:

```csharp
NativeParallelMultiHashMap<uint, int> grid;   // Morton → índice en `datos[]`
```

Idéntico patrón a `HashBoidsJob`, sólo cambia la clave (`Morton` en vez de `math.hash`)
y que es **compartido** (no se descarta). Lookup amortizado O(1). Es el escalón 1 del
plan de migración (§7); la variante ordenada es el escalón 3.

### 3.4 Tradeoff del tamaño de celda

`CELL_SIZE = 16 m` elegido contra los radios reales del proyecto:
- `PoliciaForalIA.radioVision = 22` → escaneo de **2 anillos** (5×5 celdas).
- Separación de multitud (~2–4 m) → 1 anillo (3×3), sobre-incluye poco.
- Streaming 500 m → `ceil(500/16) = 32` anillos = 65×65 celdas (consulta 1×/frame, asumible) — o usar el *grid grueso* espejo (§9).

Celda más pequeña = sets de vecinos más ajustados pero más celdas escaneadas por
consulta grande. `CELL_SIZE` es `const` por rendimiento Burst; cambiarlo es recompilar,
no runtime.

---

## 4. Pilar 2 — Población asíncrona (zero-cost updates, thread-safe)

### 4.1 Pipeline de construcción (3 jobs Burst encadenados)

```
[productores escriben en buffer append durante su Update]
        │  (frame N-1)
        ▼  EarlyUpdate del frame N:
   ┌────────────────────────────────────────────────────────────┐
   │ Job A  CalcularMortonJob  (IJobParallelFor)                  │
   │   in : datos[] (snapshot)     out: keys[]                    │
   ├────────────────────────────────────────────────────────────┤
   │ Job B  RadixSortJob  (LSD 4×8-bit, Burst)                    │
   │   in : keys[]                 out: orden[]  (índices)        │
   ├────────────────────────────────────────────────────────────┤
   │ Job C  ReordenarJob  (IJobParallelFor)                       │
   │   in : datos[], orden[]       out: datosOrdenados[], cellKeys[]│
   └────────────────────────────────────────────────────────────┘
        ▼  JobHandle.Complete() al final de EarlyUpdate (o async, §4.4)
   grid listo y estable para todo el frame N
```

### 4.2 Escrituras paralelas thread-safe

Dos mecanismos, ambos lock-free:

**(a) v1 — `ParallelWriter`** (idéntico a [`HashBoidsJob`](../Assets/Scripts/Crowd/MultitudJobs.cs#L77)):
```csharp
[BurstCompile]
struct InsertarJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<SpatialData> datos;
    public float ox, oz;
    public NativeParallelMultiHashMap<uint, int>.ParallelWriter grid;
    public void Execute(int i)
        => grid.Add(OmniGridMath.Morton(OmniGridMath.Celda(datos[i].pos, ox, oz)), i);
}
```
El `ParallelWriter` reparte la escritura en bloques sin contención (cada hilo escribe en
su segmento interno); no hay dos hilos tocando el mismo cubo a la vez.

**(b) radix (variante ordenada)** — histograma con incremento atómico:
```csharp
// count[256] por pasada; Interlocked sobre puntero nativo (NativeDisableParallelForRestriction)
unsafe { Interlocked.Increment(ref ((int*)pCount)[byteRadix]); }
```
Prefijo exclusivo (scan) → posiciones de scatter. El scatter escribe cada índice en su
hueco único → sin colisiones por construcción.

### 4.3 Presupuesto < 0.5 ms — justificación

Para N = 10 000 entidades:
- Job A (Morton): 10k ops triviales, multihilo → ~20–40 µs.
- Job B (radix 4 pasadas): ~4·10k = 40k toques, multihilo → ~150–250 µs.
- Job C (reordenar): 10k copias de struct (~32 B) → ~60–100 µs.

Total objetivo **< 0.4 ms** en 8 hilos. Si crece, dos válvulas: (1) saltar el reordenado
físico y quedarse en hashmap; (2) reconstruir cada 2 frames bajo `FactorCarga < umbral`
(el grid ya es N-1 coherente, N-2 sigue siendo aceptable para culling/streaming).

### 4.4 ¿Bloquea el hilo principal?

No. El `JobHandle` se programa en `EarlyUpdate` y se `Complete()` justo antes del primer
`Update`. Alternativa async (§9): programar al final del frame N, `Complete()` al inicio
del N+1 → el grid jamás toca el hilo principal salvo el swap de punteros.

---

## 5. Coherencia temporal y orden de ejecución (clave del aislamiento)

El problema clásico: los productores (`Update` de Policía, Coche, Crowd) y los
consumidores (otros `Update`) corren en orden arbitrario. Si el grid se construyera
"a mitad", unos leerían datos frescos y otros viejos.

**Solución: doble buffer + lectura N-1.**

```
Frame N-1:  productores → Submit(SpatialData)  → buffer_append (NativeList ParallelWriter)
Frame N (EarlyUpdate): swap buffers; construir grid DESDE el snapshot de N-1; limpiar append
Frame N (Update...):   consumidores Query(...) sobre un grid 100 % estable
Frame N (productores):  vuelven a Submit para el N+1
```

- **Ningún hazard de orden**: el grid del frame N es de sólo-lectura para todos los `Update`.
- **Latencia 1 frame (16 ms)**: irrelevante para separación de multitud, percepción de IA, culling y streaming. (Para físicas reactivas instantáneas se usa el `Raycast` directo, no el grid — el grid no es para eso.)
- Encaja con la inyección PlayerLoop ya usada por el Orquestador:

```csharp
// Core/Espacial/OmniGridLoop.cs  (mismo patrón de Instalar/Desinstalar que el Orquestador)
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
static void Boot()
{
    var grid = new UnifiedSpatialGrid();
    ServiceLocator.Registrar<IUnifiedSpatialGrid>(grid);
    InstalarEn<EarlyUpdate>(grid.ConstruirFrame);   // ANTES que los Update (Orquestador va DESPUÉS)
}
```

Orden por frame resultante:
```
EarlyUpdate ── OmniGrid.ConstruirFrame()        (grid N listo desde snapshot N-1)
Update      ── Policía/Coche/Crowd .Update()    (Query sobre grid N + Submit para N+1)
Update      ── GlobalSimulationOrchestrator     (EvaluarLOD puede consultar el grid)
```

---

## 6. Pilar 4 — Contrato de aislamiento (Core ↔ capas de arriba)

Core no conoce `PoliciaForalIA`, `VehiculoBase`, ni `ProxyMesh`. Sólo este struct y esta interfaz.

### 6.1 El contrato (datos genéricos)

```csharp
// Core/Espacial/SpatialContratos.cs
using Unity.Mathematics;

/// <summary>Tipos de entidad como bitmask → las queries filtran por capa sin saber clases.</summary>
[System.Flags]
public enum TipoEspacial : uint
{
    Ninguno      = 0,
    Manifestante = 1 << 0,
    Policia      = 1 << 1,
    Vehiculo     = 1 << 2,
    Jugador      = 1 << 3,
    PropDestruible = 1 << 4,
    ProxyMesh    = 1 << 5,   // para culling gráfico
    ChunkAncla   = 1 << 6,   // para streaming
    Todos        = 0xFFFFFFFF
}

/// <summary>El "contrato" que las capas de arriba inyectan. Blittable → Burst/Jobs.</summary>
public struct SpatialData
{
    public float3       pos;     // posición mundo (Y incluido)
    public int          id;      // ID OPACO definido por el productor (índice en SU array)
    public TipoEspacial tipo;    // bits de capa para filtrar
    public float        radio;   // tamaño/influencia (para AABB y rangos)
}
```

> **Clave del desacoplamiento**: `id` es opaco. La Policía registra sus agentes con
> `id = índice en su propio NativeArray`; cuando una query devuelve `id=42`, *la Policía*
> sabe que es su agente 42. Core nunca resuelve la identidad. Cero acoplamiento cruzado.

### 6.2 La interfaz de servicio (en Core, vía `ServiceLocator`)

```csharp
public interface IUnifiedSpatialGrid
{
    // ── INYECCIÓN (productores, durante su Update; va al buffer N+1) ──
    void Submit(in SpatialData d);                          // 1 entidad
    void SubmitBatch(NativeArray<SpatialData> lote);        // lote (crowd, tráfico)

    // ── GOD QUERIES (consumidores; sobre el grid estable del frame) ──
    void QueryRadio(float3 centro, float radio, TipoEspacial filtro, NativeList<int> resultado);
    int  QueryRadioContar(float3 centro, float radio, TipoEspacial filtro);
    void QueryFrustum(NativeArray<float4> planos6, TipoEspacial filtro, NativeList<int> resultado);
    void QueryCeldasNuevas(float3 antes, float3 ahora, float radio, NativeList<uint> celdasEntrantes);

    // ── Acceso Burst directo (para que el FlockingJob lea el grid dentro de un job) ──
    OmniGridLectura ObtenerLectura();   // struct de NativeArrays [ReadOnly] pasable a un IJob

    int Conteo { get; }
}
```

`OmniGridLectura` es un struct ligero con los `NativeArray` `[ReadOnly]` (`datosOrdenados`,
`cellKeys`, `keys`) + el método `ParaCadaVecino(...)` — así el `FlockingJob` itera vecinos
**dentro del job**, sin saltar al hilo principal (como hoy ya hace `HashBoidsJob` con su grid local).

### 6.3 Inyección al inicio / consumo al final

```csharp
// PRODUCTOR (ej. el sistema de policía, capa Runtime) — durante su Update
for (int i = 0; i < policias.Count; i++)
    grid.Submit(new SpatialData { pos = policias[i].pos, id = i, tipo = TipoEspacial.Policia, radio = 0.5f });

// CONSUMIDOR (ej. flocking de multitud) — lee el grid estable de este frame
grid.QueryRadio(agentePos, 20f, TipoEspacial.Policia, _bufferHostiles);
foreach (int idPolicia in _bufferHostiles) { /* el crowd resuelve idPolicia en SU array */ }
```

---

## 7. Pilar 3 — Las tres God Queries

| # | Consumidor | Consulta | Complejidad | Filtro |
|---|---|---|---|---|
| 1 | **IA** — `SistemaMultitud`/`FlockingJob` (Runtime) | "Agentes hostiles en radio 20 m para separación/flocking" | O(celdas en radio · ocupación) ≈ O(1) con densidad acotada | `Policia` / `Manifestante` |
| 2 | **Gráficos** — `OptimizadorVisual` (Modules) | "IDs de Proxy-Meshes dentro del frustum → BatchRendererGroup" | O(celdas del AABB del frustum) + test de planos | `ProxyMesh` |
| 3 | **Streaming** — `SistemaChunks` (Systems) | "Celdas que ENTRARON en el radio de 500 m del jugador → Addressables" | O(Δ celdas) por diferencia de conjuntos | `ChunkAncla` |

### 7.1 Query radio (flocking) — el camino caliente, dentro del job

```csharp
// Patrón calcado de FlockingJob actual, pero contra el grid UNIFICADO y con filtro de tipo.
int2 c0 = (int2)OmniGridMath.Celda(centro, ox, oz);
int anillos = (int)math.ceil(radio * OmniGridMath.INV_CELL);
float r2 = radio * radio;
for (int dz = -anillos; dz <= anillos; dz++)
for (int dx = -anillos; dx <= anillos; dx++)
{
    uint key = OmniGridMath.Morton((uint2)(c0 + new int2(dx, dz)));
    int run = lectura.BuscarRun(key);                  // binary search O(log N)
    for (int i = run; i < lectura.keys.Length && lectura.keys[i] == key; i++)
    {
        var d = lectura.datos[i];
        if ((d.tipo & filtro) == 0) continue;          // filtro de capa, sin saber clases
        if (math.distancesq(d.pos, centro) <= r2) resultado.Add(d.id);
    }
}
```

### 7.2 Query frustum (culling) → BRG

Se calcula el AABB del frustum en XZ, se recorren sus celdas Morton y se testean los 6
planos contra el `radio` de cada proxy. Devuelve los `id` que el `OptimizadorVisual` mete
en el `GraphicsBuffer` del `BatchRendererGroup`. Mismo grid que la IA → un solo barrido
del mundo sirve a render y a simulación.

### 7.3 Query celdas nuevas (streaming)

`QueryCeldasNuevas(posAnterior, posActual, 500)` calcula el set de celdas del disco de 500 m
en ambas posiciones y devuelve la **diferencia** (las que acaban de entrar) → cada celda
nueva dispara su Addressable. El `SistemaChunks` deja de hacer su barrido propio.

---

## 8. Integración con sistemas existentes

### 8.1 Orquestador de simulación
`GlobalSimulationOrchestrator.EvaluarLOD()` hoy hace `Vector3.magnitude` por entidad contra
la cámara. Puede seguir igual (distancia a cámara es trivial), pero el **conteo de vecinos**
para densidad de simulación y la oclusión aproximada se pueden mover a `QueryRadioContar`.
El grid corre en `EarlyUpdate`, el Orquestador en `Update` → datos listos. Comparten
`ServiceLocator` y el patrón de inyección PlayerLoop.

### 8.2 Crowd (`MultitudJobs` / `SistemaMultitudBRG`)
`HashBoidsJob` desaparece: el `FlockingJob` recibe `OmniGridLectura` en vez de construir su
propio `NativeParallelMultiHashMap`. Beneficio inmediato: la multitud "ve" a policías,
coches y al jugador (hoy sólo se ve a sí misma). El `BuildMatricesJob` no cambia.

### 8.3 Gráficos (`OptimizadorVisualHDRP` / BRG)
La query frustum sustituye el culling por-objeto. El `Part1By1` portado a HLSL permite, en
fase 2, hacer el culling en un compute shader leyendo el mismo `GraphicsBuffer` del grid.

### 8.4 Los ~140 call-sites
Wrapper ergonómico para MonoBehaviours que no quieren tocar Jobs:
```csharp
public static class GridConsultas   // Core/Espacial/
{
    static readonly /*pooled*/ NativeList<int> _buf;
    public static int Vecinos(Vector3 p, float r, TipoEspacial t, List<int> salida) { ... }
}
```
Reemplaza `Physics.OverlapSphere(p, r)` + filtrado por tag por `GridConsultas.Vecinos(p, r, tipo, buf)`.

---

## 9. Extensiones futuras (fuera de v1)
- **3D Morton 64-bit** (`ulong`, 21 bits/eje) para queries volumétricas (interiores multi-planta).
- **Grid grueso espejo** (celda 256 m) sólo para streaming → query de 500 m en pocas celdas.
- **Construcción totalmente async** (Complete en N+1) → grid nunca toca el hilo principal.
- **Culling en GPU** vía compute leyendo el buffer del grid.
- **Niveles jerárquicos** (quadtree de celdas) si el mundo crece más allá de 14 km.

---

## 10. Plan de migración por fases

| Fase | Entregable | Riesgo |
|---|---|---|
| **0** | Contratos en Core: `SpatialData`, `TipoEspacial`, `IUnifiedSpatialGrid`, `OmniGridMath` (+ tests de Morton) | Nulo (sólo añade) |
| **1** | `UnifiedSpatialGrid` variante **hashmap** + inyección PlayerLoop + `ServiceLocator` | Bajo (patrón probado) |
| **2** | ✅ **Producir, no migrar el flocking**: NPCs+jugador publican al grid; el crowd lo consume para CONCIENCIA CRUZADA (policías). **Decisión**: el flocking intra-multitud NO migra al grid — celda 16 m vs ~2 m y sin velocidad lo harían peor; `HashBoidsJob` (local, 2 m) es el tool correcto y se queda. | Bajo (productores aditivos; consumidor gated OFF) |
| **3** | Variante **array ordenado + radix** (coherencia de caché) tras `OmniGridLectura` estable | Medio (perf, no API) |
| **4** | Query frustum → `OptimizadorVisualHDRP`/BRG | Medio |
| **5** | Query streaming → `SistemaChunks`; retirar su barrido propio | Medio |
| **6** | Barrido de los ~140 call-sites vía `GridConsultas` (hot-paths primero: Policía, Manifestación, Explosión) | Largo, incremental |

**Orden de ataque por impacto**: Policía (12 call-sites) → Crowd → Manifestación (5) →
Explosión/Destrucción (9) → resto.

---

## 11. Validación y métricas
- **Test unitario** de `Morton`/`Celda` (round-trip, monotonía Z-order) — Burst y managed dan el mismo resultado.
- **Correctitud**: `QueryRadio` vs `Physics.OverlapSphere` sobre 1000 posiciones aleatorias → mismos sets.
- **Perf gate**: `ProfilerMarker "OmniGrid.Construir" < 0.5 ms` @10k entidades en `ServicioTelemetriaFrames`.
- **Regresión flocking**: la multitud se comporta igual tras quitar `HashBoidsJob` (vídeo A/B).
- Exponer contadores (`Conteo`, ms de build) al overlay de debug del Orquestador.

---

## 12. Archivos a crear (capa Core — `Assets/Scripts/Core/Espacial/`)

```
SpatialContratos.cs          SpatialData, TipoEspacial, IUnifiedSpatialGrid, OmniGridLectura
OmniGridMatematicas.cs       Morton/Celda/Part1By1 (Burst + HLSL gemelo)
UnifiedSpatialGrid.cs        implementación (hashmap → radix)
OmniGridJobs.cs              CalcularMortonJob, RadixSortJob, ReordenarJob, InsertarJob
OmniGridLoop.cs              Boot + Instalar/Desinstalar en EarlyUpdate (patrón Orquestador)
GridConsultas.cs             wrapper ergonómico para MonoBehaviours
```
Sin tocar lógica existente hasta la Fase 2. La capa Core no gana dependencias hacia arriba.
