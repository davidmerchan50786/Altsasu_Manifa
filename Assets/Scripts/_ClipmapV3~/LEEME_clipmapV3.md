# Mosaico V3 — clipmap GPU (staging)

Carpeta `~` → **Unity NO la compila**. Código real listo para activar, aislado del
build. Diseño en `Docs/ADR_001_AAA_impostores_clipmapV3.md`.

## Qué hay (fase 2, hecho)
- `ConstructorMallaClipmap.cs` — genera la malla del clipmap: anillos concéntricos
  de rejilla, cada nivel con el doble de tamaño de celda y un hueco central
  (cubierto por el nivel más fino). Geometría pura, determinista, 1 sola malla.
- `ClipmapTerrenoV3.cs` — holder runtime: construye la malla, la engancha bajo el
  jugador con **snap a rejilla** (sin swimming) y la dibuja en 1 draw call.

## Dependencia previa — YA GENERADO ✓
`Assets/AlsasuaData/terrain_clipmap_v3/heightmap_unificado.r16` (4097², 1.76 m/px)
+ `meta.json` ya están creados y **validados**: datum dinámico BASE=495 m, 0 %
de recortes, cota de Herriko Plaza **531.97 m** (esperado 531.94, +3 cm).
Para regenerar a otra resolución: `python Tools/GenerarHeightmapUnificadoV3.py 3600 8193`.

## Validación V3 vs V2 — ✓ cumple condición de retiro
Comparado el heightmap unificado V3 (1.76 m/px) contra el mosaico V2 fino
(ring-0, 0.586 m/px) en el núcleo urbano (n=66 564 muestras):
**mediana 0.008 m · p95 0.050 m · p99 0.093 m · máx 0.542 m · 100 % < 0.5 m.**
→ El clipmap V3 es intercambiable con V2 a <0.5 m. (Para apurar el máx en sierras,
subir a res 8193.)

## Muestreador CPU — HECHO ✓ (validado)
`MuestreadorHeightmapV3.cs`: lee el R16 + meta y da `AlturaMundo(x,z)` bilineal O(1)
(altura mundo Unity, datum Z_MIN — idéntico a `AlturaTerreno`). Lógica verificada
contra los datos: plaza 531.97 m, estación 530.40 m (≈ 530.65 del manifest V2).
Es la pieza que respaldará `ServicioTerreno.AlturaMundo` con el clipmap.

## Cómo ACTIVAR (fase 2, ver geometría)
1. Mueve los 2 `.cs` a `Assets/Scripts/Runtime/ClipmapV3/` (capa Runtime).
2. Crea un GameObject vacío, añade `ClipmapTerrenoV3`, asigna `jugador`.
3. Play: verás la malla del clipmap siguiendo al jugador (plana hasta la fase 3).

## Pendiente (fase 3-6, en el ADR) — necesita Unity para validar
- **Shader de displacement** (ShaderGraph HDRP): en VERTEX hace `SampleLevel` del
  R16 con `worldXZ = vertex.xz + _ClipmapOrigen.xz` → `altitud = BASE + q/64`,
  `Y = altitud - Z_MIN`. Importar el `.r16` como Texture2D R16 (point, clamp).
- **ServicioTerreno**: nueva `FuenteTerreno.ClipmapV3` cuyo `AlturaMundo` delega en
  `MuestreadorHeightmapV3` (ya hecho) → mismo contrato `ITerrainService`, edificios /
  NavMesh / árboles / Cesium **no cambian**.
- **Validación**: el gate `✅ Validar georreferenciación` y la cota de plaza
  (≈531.94 m) deben pasar antes de retirar el Mosaico V2.
- **Collider-parche** que sigue al jugador (física) en vez de 48 TerrainColliders.
- Portar `MultiTileTerrainEdit` (excavación de ríos) a edición del R16 con kernels
  idempotentes `min()`.

## Por qué staged
El displacement HDRP por vertex-texture-fetch y la integración con ServicioTerreno
hay que probarlos en el editor; escritos a ciegas llegarían rotos. La geometría
(esto) es determinista y sí puede darse correcta sin Unity.
