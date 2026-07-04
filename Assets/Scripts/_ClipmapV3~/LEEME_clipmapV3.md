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

## Fase 3 — displacement GPU: PIEZAS ESCRITAS ✓ (solo falta cablear el grafo)
Ya no hay que escribir HLSL ni C# a ciegas. Lo escrito y listo:
- `ClipmapDisplacement.hlsl` — Custom Function (VERTEX): muestrea el R16, decodifica
  `Y = Base + q/64 - ZMin` (idéntico al CPU y al `.py`) y **reconstruye la normal**
  por diferencias centrales (4 taps) → sombreado AAA sin bake de normalmap.
- `CargadorTexturaHeightmapV3.cs` — sube el `.r16` a `Texture2D R16` (lineal, clamp,
  bilineal) y fija las constantes del material desde `meta.json`. Gemelo GPU del
  muestreador CPU → la malla y `AlturaMundo()` coinciden bit a bit.

### Receta del Shader Graph (HDRP/Lit, ~5 min en el editor)
1. Crea un *Lit Shader Graph*. Propiedades expuestas (Reference EXACTO):
   `_Height` (Texture2D), `_ClipmapOrigen` (Vector2), y Floats `_Half _OX _OZ _Base
   _ZMin _Res`. Marca `_Height` como **Linear**, wrap **Clamp**, filter **Bilinear**.
2. Añade un nodo **Custom Function** → *File* → `ClipmapDisplacement.hlsl`, nombre
   `ClipmapDisplace`. Entradas en este orden: `PosOS`(Position **Object**),
   `OrigenXZ`(_ClipmapOrigen), `Height`(Texture2D), `SS`(Sampler State), `_Half _OX
   _OZ _Base _ZMin _Res`. Salidas: `OutPosOS`(Vector3), `OutNormalOS`(Vector3).
3. Conecta `OutPosOS` → **Vertex Position**, `OutNormalOS` → **Vertex Normal**.
4. Crea un material de ese shader y asígnalo a `ClipmapTerrenoV3.material`.

### Cableado runtime (2 líneas en `ClipmapTerrenoV3`)
- En `OnEnable`, tras asignar el material:
  `GetComponent<CargadorTexturaHeightmapV3>()?.Configurar(material);`
- En `Recolocar`, descomenta:
  `material.SetVector("_ClipmapOrigen", new Vector4(x, 0, z, 0));`
  (el shader solo usa .xy = (x,z), que es el origen del GameObject).

## Pendiente (fase 4-6, en el ADR) — necesita Unity para validar
- **ServicioTerreno**: nueva `FuenteTerreno.ClipmapV3` cuyo `AlturaMundo` delega en
  `MuestreadorHeightmapV3` (ya hecho) → mismo contrato `ITerrainService`, edificios /
  NavMesh / árboles / Cesium **no cambian**.
- **Validación**: el gate `✅ Validar georreferenciación` y la cota de plaza
  (≈531.94 m) deben pasar antes de retirar el Mosaico V2.
- **Collider-parche** que sigue al jugador (física) en vez de 48 TerrainColliders.
- Portar `MultiTileTerrainEdit` (excavación de ríos) a edición del R16 con kernels
  idempotentes `min()`.

## Por qué staged
La integración con ServicioTerreno y el grafo HDRP hay que **validarlos** en el editor.
Pero el HLSL, el cargador GPU, el muestreador CPU y la geometría son deterministas y
están escritos de forma correcta y verificable sin Unity; la decodificación es la
misma fórmula en los tres sitios (`.py`, CPU, GPU), así que no pueden desincronizarse.
