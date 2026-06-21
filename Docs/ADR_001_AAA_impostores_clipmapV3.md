# ADR-001 — Impostores con atlas de billboard + Mosaico V3 (clipmap GPU)

Fecha: 2026-06-21 · Estado: **Propuesto** · Ámbito: render AAA (deuda citada en `CLAUDE.md`)

## Contexto

Hoy el render se gobierna con tres directores (arranque, CPU `GlobalSimulationOrchestrator`,
GPU `GobernadorRender` que produce `RadioActivacion`/`RadioImpostor`) y el streaming estático
`StreamerMundoEstatico` clasifica edificios/props en 3 bandas (Activo / **Impostor-lite** /
Oculto) con un job Burst `JobBandasMundo`. Dos deudas AAA pendientes:

1. **Impostor-lite → impostor real.** Hoy "impostor" = LOD mínimo + sombras OFF. Sigue siendo
   geometría: cuesta vértices y draw calls. AAA = **billboard con atlas pre-horneado** (1 quad
   por edificio, sombra fake), recortando ~90 % del coste de la banda media.
2. **48 Terrain → Mosaico V3 clipmap GPU.** Hoy el suelo son 48 `Terrain` (decenas de draw
   calls, `SetHeights`/colliders pesados). AAA = **geometry clipmap** que dibuja todo el relieve
   en **1–2 draw calls** muestreando un heightmap unificado por vertex-texture-fetch.

Restricciones del proyecto: HDRP; capas asmdef `Core ← Runtime/Modules ← Systems ← Editor`;
coordenadas vía `GeoDataAlsasua` (UTM real isótropo, `ESCALA_UTM_X=1`); escritores del terreno
vía `MultiTileTerrainEdit`; `ServicioTerreno` es la única verdad del suelo.

---

## Decisión 1 — Impostores con atlas de billboard

### Diseño
- **Bake (Editor, fase offline).** `BakeadorImpostores` (capa Editor) renderiza cada edificio
  (o arquetipo) a un **atlas octaédrico** de N×N vistas (típico 8×8 = 64 hemi-vistas) a una
  cámara ortográfica, capturando **albedo+normal+profundidad** en un `RenderTexture` → `Texture2D`
  empaquetado en un atlas por lotes (p. ej. 4096² = 16 edificios de 1024², o 256 de 256²).
  Sombras: se hornea también una "alfa de sombra" proyectada. Se guarda un SO `ImpostorAtlasSO`
  con UVs por id OSM (reutiliza el id que ya usan las FacadeTextures).
- **Runtime (capa Runtime).** `ImpostorBillboard` = un `MeshRenderer` de **1 quad** con material
  `Shader "Alsasua/ImpostorOcta"` que: (a) orienta el quad hacia la cámara (billboard esférico
  o cilíndrico para edificios), (b) elige las 1–3 vistas del atlas más cercanas a la dirección
  cámara→objeto y las mezcla, (c) aplica parallax/profundidad para que no sea "plano". GPU
  Instancing ON + un solo material → las cientos de fachadas lejanas = **1 draw call por atlas**
  vía `Graphics.RenderMeshInstanced`/BRG (igual patrón que la multitud actual).
- **Integración con el streamer.** `StreamerMundoEstatico` ya tiene la banda *Impostor-lite*.
  Se añade un 4.º estado o se sustituye esa banda: al entrar en `[RadioActivacion, RadioImpostor]`
  el objeto **desactiva su `MeshRenderer` real y registra su id+matriz en `LoteImpostores`**
  (un buffer por atlas que BRG dibuja). Al salir hacia *Activo* se revierte. La histéresis del
  `JobBandasMundo` evita parpadeos. El `GobernadorRender` sigue moviendo los radios bajo presión
  de GPU → menos billboards si hace falta.

### Presupuesto / objetivo
- Banda media (cientos–miles de edificios) pasa de *N draw calls + LODs* a **1 draw call/atlas**
  y ~0 coste de vértices. Objetivo: −80–90 % draw calls de mundo estático lejano.

### Plan por fases
1. `ImpostorAtlasSO` + `BakeadorImpostores` (8 vistas, solo albedo) — bake de 10 edificios piloto.
2. Shader `ImpostorOcta` + `ImpostorBillboard` con selección de vista + billboard.
3. Hook en `StreamerMundoEstatico` (registrar/revertir en la banda media) + BRG por atlas.
4. Normales + profundidad (parallax) + sombra fake. 5. Bake masivo (1030 edificios) por atlas.

### Riesgos
- *Pop* al cruzar Activo↔Impostor → mezcla por dither/alpha en 0.25 s + histéresis.
- Atlas grande en VRAM → atlas por lotes y mip-streaming.
- Edificios muy altos/asimétricos: octaedro hemisférico (no esférico) y eje vertical billboard cilíndrico.

---

## Decisión 2 — Mosaico V3 (clipmap GPU)

### Diseño
- **Fuente:** `Tools/GenerarHeightmapUnificadoV3.py` (ya creado) produce
  `Assets/AlsasuaData/terrain_clipmap_v3/heightmap_unificado.r16` + `meta.json` reutilizando el
  muestreador `H()` verificado (datum Z_MIN, cuanto 1/64 m → **intercambiable con V2**).
- **Geometría:** una malla de **clipmap concéntrico** (anillos de rejilla a resolución decreciente,
  estilo Losasso–Hoppe) centrada en el jugador, **toroidalmente actualizada** (solo se desplaza el
  origen UV; los vértices no se regeneran). El **vertex shader** lee la altura del heightmap
  (R16, `SampleLevel` en vertex) y desplaza Y: `worldY = q/64`. 1 material → **1–2 draw calls**.
- **Capa/owner:** `ServicioTerreno` (Systems/World) gana una `FuenteTerreno.ClipmapV3` y expone el
  mismo contrato `ITerrainService` (`AlturaMundo` ahora muestrea el R16 en CPU con bilineal — O(1)).
  Así **edificios, NavMesh, árboles, spawn y Cesium no cambian**: siguen llamando
  `GeoDataAlsasua.AlturaTerreno()` / `ITerrainService`.
- **Colisión:** el clipmap es visual; para física se mantiene un `TerrainCollider` ligero o un
  `MeshCollider` local de baja resolución bajo el jugador (parche que sigue al jugador), no 48.
- **Compatibilidad:** V3 convive con V2 detrás de un flag; `MultiTileTerrainEdit` (excavación de
  ríos, etc.) se reimplementa como **edición del R16 unificado** (un kernel min() idempotente sobre
  la textura) en vez de 48 `SetHeights`.

### Presupuesto / objetivo
- 48 Terrain (≫10 draw calls + colliders) → **1–2 draw calls** + 1 collider-parche. Objetivo:
  suelo a coste casi constante independientemente del radio de vista.

### Plan por fases
1. Generar `heightmap_unificado.r16` (correr el script; res 4097 en máquinas con poca RAM).
2. Malla clipmap + shader de displacement por vertex-texture-fetch (solo visual, sobre V2).
3. `ServicioTerreno.AlturaMundo` muestreando el R16 (CPU) → validar con el gate (<0.5 m vs V2).
4. Collider-parche que sigue al jugador. 5. Portar `MultiTileTerrainEdit` al R16. 6. Retirar V2.

### Riesgos
- Precisión de altura del clipmap vs V2 → **el gate `Validar georreferenciación` debe pasar**
  (cota plaza ≈531.94 m) antes de retirar V2.
- Costuras entre anillos del clipmap → resolución diádica + morphing de borde (geomorphing).
- Edición runtime del R16 (ríos) → mantener kernels idempotentes `min()` como en V2.

---

## Gate de validación (ya implementado)

`Tools ▸ Alsasua ▸ Calidad ▸ ✅ Validar georreferenciación (<0.5 m)`
(`Assets/Scripts/Editor/ValidadorGeorreferencia.cs`) comprueba escala isótropa, ida-vuelta
UTM, iglesia en su sitio OSM (<1 m), autovía presente y censo. **Es el gate que debe pasar tras
cada cambio de terreno** (V3) o de datos, igual que `ValidarMosaicoV2` es el gate del bake V2.
Complementado por los tests EditMode `GeoDataAlsasuaTests` (regresión de la escala).

## Consecuencias
- (+) Mundo estático lejano y suelo a coste casi constante → presupuesto de GPU para densidad AAA.
- (+) `ITerrainService`/`GeoDataAlsasua` aíslan a todos los consumidores: la migración es interna.
- (−) Dos sistemas de render nuevos que escribir y validar; conviven tras flag hasta que el gate
  y los tests pasen. Orden recomendado: **gate (hecho) → impostores fase 1-3 → clipmap fase 1-3**.
