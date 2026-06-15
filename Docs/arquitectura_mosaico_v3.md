# Arquitectura — Mosaico V3 (GPU-Driven Terrain & JIT Collisions)

> **Prompt Definitivo IV** · Terreno GPU-driven que prescinde del componente
> `Terrain` de Unity, **manteniendo intacto el contrato Core** `ITerrainService` /
> `TerrenoGlobal` (alturas matemáticas) que consume todo el juego.
> Estado (2026-06-14): **diseño**. No implementado. Depende del [Omni-Grid](arquitectura_omnigrid.md)
> (ya en Core, Fase 0/1) para las JIT collisions.

---

## 0. Qué reemplaza y qué se mantiene

| Pieza | Hoy (V2) | V3 |
|---|---|---|
| Render del suelo | 48 `Terrain` (`CargadorMosaicoTerreno`) → 48+ draw calls, memoria de heightmaps en CPU+GPU | **1–2 draw calls** vía clipmap GPU + `RenderMeshIndirect` |
| Colisión | 48 `TerrainCollider` siempre activos | **JIT MeshColliders** sólo bajo entidades físicas (pool) |
| Mezcla de materiales | splatmap 8 capas tope de muestreos | **Texture2DArray + SVT**, capas "infinitas" |
| **Alturas CPU** (`ITerrainService.AlturaMundo`) | `Terrain.SampleHeight` (tile-aware en `ServicioTerreno`) | **igual contrato**, pero resuelto por muestreo matemático del RAW en RAM |
| Contrato Core | [`ITerrainService`](../Assets/Scripts/Core/ITerrainService.cs) / [`TerrenoGlobal`](../Assets/Scripts/Core/TerrenoGlobal.cs) | **SIN CAMBIOS** — los ~20 lectores no se enteran |

**Principio rector**: V3 es un **nuevo proveedor** dentro de [`ServicioTerreno`](../Assets/Scripts/Systems/ServicioTerreno.cs)
(`FuenteTerreno.MosaicoGPU`), no un sistema paralelo. La máquina de estados
(`Inicializando→Generando→Listo`), `TerrenoListoEvent` y la cadena de fallback se conservan;
si la GPU/SVT no está disponible, cae al Mosaico V2 con `Terrain`. Cero regresión por diseño.

---

## 1. Separación física: render (GPU) vs alturas (CPU)

La clave para "mantener intacta la capa Core" es **desacoplar la altura matemática del render**:

```
RAW uint16 (lattice 1/64) en disco  ──┬──► CPU: NativeArray<ushort> por tile (RAM)
  manifest_v2.json (48 tiles)         │        └─ MuestreadorAlturaMosaico → ITerrainService.AlturaMundo
                                      │           (decode: alturaMundo = tile.y + q/64, bilineal)
                                      └──► GPU: Texture2D R16 / heightmap atlas (VRAM)
                                                 └─ clipmap vertex shader (solo VISUAL)
```

- **Alturas (gameplay, IA, spawn, NavMesh, árboles)**: nunca tocan la GPU. Se resuelven
  leyendo el mismo RAW ya cargado en RAM, con el decode lattice que ya documenta
  [`MosaicoManifest`](../Assets/Scripts/Core/MosaicoManifest.cs) (`y_tile + q/64`). Esto es
  **más rápido y más preciso** que `Terrain.SampleHeight` y elimina la dependencia del componente.
- **Render**: el heightmap vive en VRAM; el vertex shader desplaza una malla teselada. Que el
  render exista o no es irrelevante para la simulación.

```csharp
// Systems — implementa ITerrainService sin Terrain. Alturas = matemática pura sobre RAM.
public sealed class MuestreadorAlturaMosaico
{
    NativeArray<ushort>[] _tilesRaw;   // un RAW por tile, Persistent
    MosaicoManifest _man;

    public float AlturaMundo(Vector3 p)
    {
        int t = TileEn(p.x, p.z);                       // O(1): índice por anillo + rejilla
        if (t < 0) return _man.datumYBase;              // fuera → datum
        var tile = _man.tiles[t];
        float u = (p.x - tile.x) / tile.ancho * (tile.res - 1);
        float v = (p.z - tile.z) / tile.ancho * (tile.res - 1);
        ushort q = MuestreoBilineal(_tilesRaw[t], tile.res, u, v);  // lattice
        return tile.y + q / 64f;                        // alturaMundo bit-exacta con el bake
    }
}
```

> El `ITerrainService.Terreno` (que hoy devuelve el tile ancla) puede devolver `null` en V3
> (no hay `Terrain`); los lectores ya hacen null-check. Se añade `FuenteTerreno.MosaicoGPU` al enum.

---

## 2. Pilar 1 — Render: Geometry Clipmap GPU + `RenderMeshIndirect`

### 2.1 Clipmap (anillos concéntricos que siguen a la cámara)

En vez de 48 mallas gigantes, **una sola malla teselada en anillos** centrada en la cámara:
- L niveles (p.ej. 6): el nivel 0 = parche fino (1 m) junto al jugador; cada nivel exterior
  duplica el tamaño de celda (2, 4, 8… m) y cubre 4× área → resolución cae con la distancia
  sin costuras (los anillos encajan por construcción).
- La malla es **estática** (un grid de vértices en espacio local por nivel + "stitching ring"
  para unir niveles); sólo cambia un `_CamPos` y un snap por nivel (cuantizado al tamaño de
  celda → sin shimmer).
- El **vertex shader** muestrea el heightmap atlas (VRAM) y desplaza `y`. La normal se calcula
  por diferencias finitas del heightmap (3 samples) — normales sin malla pesada.

```hlsl
// Vertex (Shader Graph Custom Function o HLSL):  pos.xz mundo → UV atlas → altura
float2 worldXZ = LocalToWorldClip(IN.posOS, _CamSnap, _NivelEscala);
float  h = SampleHeightAtlas(worldXZ);              // R16, lattice decode en GPU
float3 n = NormalFromHeight(worldXZ, _Texel);       // diferencias finitas
posWS.y = h;
```

### 2.2 ¿1–2 draw calls? `RenderMeshIndirect`

- Todos los anillos del clipmap → **una** `Graphics.RenderMeshIndirect` con un
  `GraphicsBuffer.IndirectArguments` (instancia por parche de anillo).
- Para el quadtree adaptativo (LOD por pendiente/distancia), un **compute shader** de culling +
  selección de LOD escribe el buffer indirecto (qué parches dibujar) → la CPU no itera tiles.
  El `OptimizadorVisualHDRP` ya usa `Unity.RenderPipelines.GPUDriven.Runtime` (está en el asmdef Core).
- Reaprovechamos el know-how del [`RenderizadorMultitudBRG`](../Assets/Scripts/Crowd/RenderizadorMultitudBRG.cs):
  buffer crudo + `MetadataValue` + culling en callback. El terreno es el mismo patrón con un
  heightmap en el vertex en lugar de matrices por instancia.

> **Alternativa BRG**: si se quiere unificar con el pipeline de culling del BRG, los parches
> del clipmap se registran como un batch y el `OnPerformCulling` filtra por frustum. Para el
> terreno el clipmap+indirect es más simple; el BRG es preferible si ya hay un cull GPU global.

### 2.3 Heightmap a VRAM
- `ServicioTerreno` (Systems) sube cada RAW a un `Texture2D(R16, mipChain)` y los compone en un
  **atlas** (o `Texture2DArray` por anillo). Multi-resolución V2 (anillo 0 @0.59 m, anillo 2 @3.5 m)
  → mapea a niveles del clipmap directamente.
- Subida asíncrona (`Texture2D.Apply(false, false)` + `GraphicsBuffer.SetData` en hilo de carga)
  escalonada por tile, igual que `CargadorMosaicoTerreno` hace hoy con los `Terrain` (anillo 0 primero).

---

## 3. Pilar 2 — Splatmapping sin límites (Texture2DArray + SVT)

- **Index Map** (splatmap reinterpretado): textura por tile donde cada téxel guarda hasta 4
  índices+pesos de capa (RGBA = id capa / blend). Sustituye al límite de 8 capas del `Terrain`.
- **Texture2DArray** de materiales PBR (Albedo/Normal/MaskMap ARM), todas las texturas de
  `Assets/Textures_AAA/TerrainLayers/` + asfalto + roca/barro como slices. El pixel shader
  muestrea el array por el índice del index map → **N capas con coste fijo** (4 samples, no 8×N).
- **SVT (Streaming Virtual Texturing)**: las slices grandes (orto 0.25 m/px,
  `ortofoto_alsasua_REAL.png`) van por SVT → sólo se residen en VRAM los téxeles visibles.
  Transición fotorrealista asfalto (anillo 0) ↔ roca/barro (anillo 2) mezclando dos índices del
  index map con un factor de pendiente/altura, sin romper el presupuesto de muestreos por pase.
- Implementación: **Shader Graph** con un Custom Function HLSL para el muestreo indexado del
  array (Shader Graph no expone `Texture2DArray.Sample(index)` nativamente con blend de 4).

```hlsl
// Muestreo de 4 capas indexadas desde el array (1 pase):
half4 albedo = 0; half3 nrm = 0;
[unroll] for (int k = 0; k < 4; k++) {
    int  capa = idx[k];                         // del index map
    half w    = pesos[k];
    albedo += w * SAMPLE_TEXTURE2D_ARRAY(_AlbedoArray, sampler_lin, uvTriplanar, capa);
    nrm    += w * UnpackNormal(SAMPLE_TEXTURE2D_ARRAY(_NormalArray, sampler_lin, uvTriplanar, capa)).xyz;
}
```

---

## 4. Pilar 3 — JIT Havok Collisions (consume el Omni-Grid)

Sin `TerrainCollider`, el coche y los NPCs necesitan colisión. La generamos **solo donde hay
entidades físicas**, en celdas de 64×64 m, con pool — y **quién la necesita lo dice el Omni-Grid**.

### 4.1 Quién pide colisión → Omni-Grid
Las entidades físicas (jugador, vehículos, policías, props grandes) ya se inyectan en el
[Omni-Grid](arquitectura_omnigrid.md) con `TipoEspacial.Jugador | Vehiculo | Policia | PropDestruible`.
Cada frame, `ServicioTerreno` consulta:

```csharp
// celdas que necesitan collider = unión de discos alrededor de entidades físicas
grid.QueryRadio(jugador.pos, RADIO_FISICA, TipoEspacial.Jugador | TipoEspacial.Vehiculo, _fisicas);
// → de cada entidad, su celda 64×64 + las 8 vecinas (margen para velocidad)
```

→ el set de "celdas calientes" es pequeño y estable; el grid lo da en O(k).

### 4.2 Pipeline async (Burst + Jobs + `Physics.BakeMesh`)

```
Por celda nueva caliente:
  Job A (Burst, IJobParallelFor): genera vértices+índices del parche 64×64
        muestreando el RAW (lattice) a resolución de física (p.ej. 1 m → 65×65 verts)
  Job B (Burst): Physics.BakeMesh(meshId, ...)   ← BakeMesh SÍ es Burst/Job-safe
  Main thread (barato): meshCollider.sharedMesh = mesh (ya horneado) → sin stall
```

`Physics.BakeMesh` (el paso caro) corre en worker threads; asignar el mesh ya horneado al
`MeshCollider` en el hilo principal es casi gratis → **cero hitch**.

### 4.3 Pool de "Physics Patches" (ciclo de vida en `ServicioTerreno`)

```csharp
sealed class ParchesFisica
{
    readonly Stack<GameObject> _libres = new();              // pool de GO con MeshCollider
    readonly Dictionary<uint, GameObject> _activos = new();  // Morton celda → parche

    public void Sincronizar(NativeList<uint> celdasCalientes)
    {
        // 1. soltar parches que salieron del set (al pool, no Destroy)
        // 2. para cada celda nueva: rentar del pool, lanzar Job A→B, asignar al completar
        // 3. histéresis: una celda aguanta unos frames antes de soltarse (evita thrash al borde)
    }
}
```

- **Sin asignaciones en runtime**: GO + `MeshCollider` + `Mesh` se reciclan; sólo se re-hornea
  el `Mesh` con datos de la nueva celda.
- **Presupuesto**: máximo K bakes en vuelo; el resto espera (el suelo bajo el jugador siempre
  tiene prioridad 0). Encaja con `FactorCarga` del Orquestador (bajo presión, K baja).
- Capa de física dedicada para que los raycasts de Foot IK (Prompt III) golpeen estos parches.

---

## 5. Pilar 4 — Micro-deformación dinámica (roderas, barro, nieve)

### 5.1 Mapa de deformación delta (clipmap toroidal centrado en el jugador)
- Un `RenderTexture`/`GraphicsBuffer` R16 local (p.ej. 1024² @ 0.25 m = 256 m alrededor del
  jugador), direccionado **toroidalmente** (wrap): al moverse el jugador, sólo se limpia la
  franja que entra (no se recopia todo).
- Es un **delta**: `alturaFinal = alturaBase(heightmap) + delta(deformMap)`. El terreno base
  no se modifica (sigue bit-exacto en disco).

### 5.2 Escritura desde las ruedas (compute dispatch)
- `ControladorJugador` / el vehículo, en el punto de contacto de cada rueda, dispara un
  **brush** (compute shader) que resta profundidad en el deform map (rodera) con forma del
  neumático y acumulación (más pasadas → surco más hondo). Nieve = blanco que se desplaza; barro
  = oscurecimiento + normal.

```
Rueda (mundo) → UV del deform map (toroidal) → ComputeDispatch(brush)
   deformMap[uv] = min(deformMap[uv], -profundidad·presion)   // idempotente, acumulativo
```

### 5.3 Lectura: vértices + normales en tiempo real
- El **mismo vertex shader del clipmap** suma `delta` a la altura cuando el vértice cae dentro
  del deform map, y recalcula la normal por diferencias finitas del (base+delta) → roderas con
  sombreado correcto, gratis en el render.
- **Física opcional (avanzado)**: el Job A de las JIT collisions puede sumar el delta al hornear
  el parche bajo el coche → roderas que también se sienten. v1: deformación **visual**; la
  versión física queda como extensión (re-hornear es caro; limitar a la celda bajo la rueda).

---

## 6. Integración y orden de capas
- **Systems** (`ServicioTerreno`): dueño de heightmap VRAM, splat/SVT, pool de física, deform map.
- **Core** (`ITerrainService`/`TerrenoGlobal`): contrato intacto; `AlturaMundo` ahora vía
  `MuestreadorAlturaMosaico` (matemático). El Omni-Grid (Core) alimenta qué celdas necesitan física.
- **Render**: HDRP custom (Shader Graph + clipmap). Usa `Unity.RenderPipelines.GPUDriven.Runtime`
  (ya referenciado).
- `MultiTileTerrainEdit` (escritores del terreno, ríos/excavación): pasa a escribir en el RAW
  en RAM + invalidar el tile en VRAM (en vez de `TerrainData.SetHeights`). Kernels idempotentes `min()` se conservan.

---

## 7. Riesgos y mitigaciones
| Riesgo | Mitigación |
|---|---|
| Clipmap/SVT es mucho shader nuevo | V3 es proveedor opcional; fallback a V2 `Terrain` intacto. Detrás de un toggle. |
| `Physics.BakeMesh` en job: versión de API | Verificar firma Job-safe en la versión de Physics del proyecto; si no, bake en worker `Task` + ensamblar en main. |
| Deform map físico re-hornea caro | v1 sólo visual; física limitada a la celda bajo la rueda. |
| Alturas CPU deben coincidir con el bake al bit | Reusar el decode lattice exacto de `MosaicoManifest` (ya validado por el gate Python). |
| SVT no disponible en la plataforma | Fallback a Texture2DArray con mips (sin streaming). |

## 8. Plan de fases
0. `MuestreadorAlturaMosaico` (alturas CPU desde RAW en RAM) bajo `ITerrainService` — **sustituye `SampleHeight` sin tocar render**. Validar `AlturaMundo` V3 == V2 (RMSE ~0).
   - ✅ **HECHO (scaffold, 2026-06-15)**: [`IMuestreadorAlturaPrecisa.cs`](../Assets/Scripts/Core/IMuestreadorAlturaPrecisa.cs) (Core, contrato `AlturaMundo`/`NormalMundo` independiente de `Terrain`) +
     [`MuestreadorAlturaMosaico.cs`](../Assets/Scripts/Systems/MuestreadorAlturaMosaico.cs) (Systems, carga RAW de los 48 tiles a RAM en background con presupuesto por frame, decode lattice 1/64 bilineal, registra el servicio en `ServiceLocator` cuando está listo).
     Es **opt-in** (`activarEnStart`, no se autoarranca por defecto, ~126 MB en RAM) y **aditivo**: `ITerrainService.AlturaMundo` (vía `Terrain.SampleHeight`) sigue siendo la entrada por defecto; los consumidores que necesiten precisión bit-exacta (Foot IK avanzado, spawn reproducible, NavMesh) hacen `ServiceLocator.Get<IMuestreadorAlturaPrecisa>()?.AlturaMundo(p)` con fallback a `ITerrainService` si el servicio no está registrado. Compila limpio (0 errores, `dotnet build` Core+Systems — verificado tras añadir los `<Compile Include>` que faltaban en los .csproj). Pendiente: validación RMSE V3==V2 en Play y un `BootstrapMuestreadorAltura` que decida cuándo activarlo.
   - ✅ **HECHO (2026-06-15)**: [`SistemaFootIK.cs`](../Assets/Scripts/Core/SistemaFootIK.cs) es el **primer consumidor real** — `ResolverPie()` pide `ServiceLocator.Get<IMuestreadorAlturaPrecisa>()`; si `Listo`, usa su `AlturaMundo`/`NormalMundo` bit-exactos (sin Terrain) como suelo BASE; si no, cae a `TerrenoGlobal.AlturaMundo`/`NormalTerreno` (comportamiento previo, sin cambios). El raycast de detalle fino sigue igual. Compila limpio (0 errores).
   - ✅ **HECHO (2026-06-15)**: [`BootstrapMuestreadorAltura.cs`](../Assets/Scripts/Systems/BootstrapMuestreadorAltura.cs) (Systems) — único punto de decisión para activar `MuestreadorAlturaMosaico`. `SceneBootstrapper.EnsureSistemasAssets()` lo añade al GO "SistemasAssets" con `activar=false` por defecto (cero cambio de comportamiento: sin él, `IMuestreadorAlturaPrecisa` nunca se registra y `SistemaFootIK`/futuros consumidores caen siempre al fallback `TerrenoGlobal`). Marcar `activar=true` en el Inspector crea el GO "MuestreadorAlturaMosaico" y arranca la carga (~126 MB RAM). Compila limpio (0 errores).
1. Heightmap → VRAM (atlas R16) + clipmap básico render-only (sin splat) → comparar contra `Terrain`.
2. Splat index map + Texture2DArray (sin SVT).
3. JIT collisions + pool (consume Omni-Grid). Retirar `TerrainCollider`.
4. SVT para orto/capas grandes.
5. Micro-deformación visual; (6) física opcional de roderas.

> Gate: ninguna fase entra sin que `AlturaMundo` y la silueta visual coincidan con V2.
> El gate Python del terreno (`ValidarMosaicoV2.py`) sigue siendo la fuente de verdad de los datos.
