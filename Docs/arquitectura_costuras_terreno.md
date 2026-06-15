# Arquitectura — Costuras del Mosaico (Edge Tessellation & Normal Blending)

> **Prompt Costuras** · Resolver artefactos visuales en las uniones del Mosaico V2
> (48 tiles, 14.4 km, multi-resolución): grietas LOD, "línea dura" de iluminación
> y Z-fighting por overlap milimétrico — **sin tocar la lógica de Systems** ni la
> generación matemática del lattice 1/64.
> Estado (2026-06-15): **diseño + HLSL + inyector** listos; integración con el
> Material del Terrain queda como paso de artista (ver §6.4).

---

## 0. Diagnóstico — qué hace ya la CPU y qué no resuelve

En la CPU el mosaico **ya cose bit-exactamente**:
- RAW `uint16` con codificación lattice 1/64 → `alturaMundo = (y64 + q) / 64`, numerador
  entero `< 2²⁴` ⇒ las costuras dependen **solo de la igualdad entera de `q`**, garantizada por
  [`Tools/ValidarMosaicoV2.py`](../Tools/ValidarMosaicoV2.py).
- [`CargadorMosaicoTerreno.ConectarVecinosIntraAnillo()`](../Assets/Scripts/Systems/CargadorMosaicoTerreno.cs#L306)
  llama `SetNeighbors(izq, arriba, der, abajo)` por tile (Unity exige misma resolución
  → solo intra-anillo; los cross-ring son tratados con `heightmapPixelError = PIXEL_ERROR_FRONTERA = 4`).
- [`MultiTileTerrainEdit`](../Assets/Scripts/Editor/) escribe con kernels idempotentes `min()` (sin overlap propio).

**Los vértices coinciden** matemáticamente. El problema vive en la GPU:

| Síntoma | Causa real | Tratamiento |
|---|---|---|
| Grietas/cremallera en cambio de LOD | `Terrain` reduce densidad de malla por distancia y cada tile **decide independientemente** cuándo bajar → vecinos a niveles distintos | **Edge-biased tessellation** (§3): el borde de cada tile mantiene densidad alta siempre |
| Línea dura de luz entre dos tiles vecinos | Normales en el borde calculadas desde la fila/columna del tile A y del tile B con un diferencial finito **asimétrico** (A no ve el sample de B y viceversa) | **Cross-tile normal blending** (§4): muestrear y promediar la normal del vecino |
| Parpadeo en faldas/sierras | Anillo 1 y anillo 2 se tocan con `heightmapPixelError` distinto + el HDRP Terrain hace Z-Prepass | **Edge depth bias** (§5) suave en el último metro del tile |

> **No introducimos overlap nuevo**. El bias es una corrección de profundidad **solo en el render**;
> el lattice y los colliders quedan idénticos.

---

## 1. Decisiones de diseño

| Decisión | Elección | Razón |
|---|---|---|
| Cómo conoce el shader a sus vecinos | **`MaterialPropertyBlock` por Terrain** inyectado desde `InyectorVecinosTerreno` (Systems) | El shader es **agnóstico** a la lógica de carga; el inyector publica `_NbrAlturaN/S/E/W` (Texture2D) y `_NbrPesoN/S/E/W` (float) + 4 IDs. |
| Dónde vive el HLSL | `Assets/Shaders/CosturasMosaico/CostuerasMosaicoAlsasua.hlsl` (include) | Reusable como **Custom Function** en Shader Graph o `#include` en HLSL puro. |
| Material del Terrain | Shader Graph que reemplaza HDRP TerrainLit (artista) | Unity no permite "inyectar" HLSL en HDRP TerrainLit; el override de Material del Terrain SÍ es API soportada. |
| Activación | Toggle por tile via `_CosturasActivas` (uniform) + define `ALSASUA_COSTURAS` | Permite A/B en QA sin recompilar. |
| Latencia | **inmediata**: el inyector publica al instanciar cada tile (en `Cargar` del mosaico) y al refrescar (cambio de Material/Streaming) | No depende del Omni-Grid; las costuras son estáticas. |

**Aislamiento de capas**:
- `InyectorVecinosTerreno` vive en **Systems** (donde están los tiles).
- El HLSL vive en `Assets/Shaders/` (asset, no script).
- Nada en Core ni Runtime; el `CargadorMosaicoTerreno` se extiende con una API pública mínima (`Vecinos(Terrain) → (N,S,E,W)`) para no romper la propiedad de su diccionario privado.

---

## 2. Contrato (datos que el shader recibe por tile)

Inyectados como `MaterialPropertyBlock` por `Terrain` (el inyector hace `terrain.SetSplatMaterialPropertyBlock(mpb)` cada vez que se reconfiguran vecinos):

| Uniform | Tipo | Significado |
|---|---|---|
| `_TileSize` | `float` | Lado del tile en m (1200 o 3600 según anillo) |
| `_TileOriginWS` | `float2` | Esquina suroeste del tile en mundo (X,Z) |
| `_EdgeBandM` | `float` | Banda del borde (m) donde aplica la corrección — default 2.0 |
| `_TessBase` | `float` | Factor de tess interno (depende de distancia) |
| `_TessEdge` | `float` | Factor de tess **forzado** en la banda de borde — default 64 |
| `_OverlapBias` | `float` | Z-bias (m) hacia la cámara solo en el borde — default 0 (activar 0.003 en faldas) |
| `_NbrPesoN/S/E/W` | `float` | 1 si hay vecino del lado, 0 si no (esquinas del mundo / agujero de anillo) |
| `_NbrHeightmapN/S/E/W` | `Texture2D` | El heightmap del vecino (para muestrear su normal en el borde) |
| `_NbrTexelSizeN/S/E/W` | `float2` | (1/res, 1/res) del vecino — diferencial finito correcto |
| `_NbrOrigenN/S/E/W` | `float2` | Esquina SW del vecino en mundo (para mapear pos→UV del vecino) |
| `_NbrEscalaN/S/E/W` | `float2` | Lado del vecino + altura mundo (para decodificar uint16 lattice) |

> Las texturas `_NbrHeightmap*` son las **mismas** `terrainData.heightmapTexture` que Unity expone
> (Texture2D R16, accesible en cualquier API de render). No duplicamos memoria.

---

## 3. Pilar 1 — Edge-Biased Tessellation

### 3.1 Idea
Para que un cambio de LOD no abra grietas en el borde, la **densidad de subdivisión** del último
~2 m de cada lado se mantiene **al máximo, independientemente de la cámara**. Tile A en LOD0 y
tile B en LOD2 se encuentran en un borde donde ambos llevan tess 64 → encajan como cremallera.

### 3.2 HLSL (resumen; ver `CostuerasMosaicoAlsasua.hlsl`)
```hlsl
// Devuelve el factor de tess para el vértice (UV del tile [0..1]).
float TessFactorBordeAlsasua(float2 uv01, float tessBase, float tessEdge, float bordeUV)
{
    // distancia al borde más cercano, en espacio UV [0..0.5]
    float d = min(min(uv01.x, 1 - uv01.x), min(uv01.y, 1 - uv01.y));
    // 0 dentro del borde → 1 ya en el interior
    float w = saturate(d / bordeUV);
    return lerp(tessEdge, tessBase, w);
}
```
`bordeUV = _EdgeBandM / _TileSize` (≈ 2/1200 = 0.00167 para anillo 0; 2/3600 = 0.00056 para anillo 2 —
el factor `tessEdge` cubre la diferencia).

### 3.3 ¿Cómo se enchufa?
En Shader Graph **HDRP/Lit Tessellation**: nodo `Tessellation Factor` → Custom Function `TessFactorBordeAlsasua`.
En HLSL puro (override del TerrainLit, ver §6.4): se sustituye el cálculo del `EdgeTessFactor` por la llamada.

---

## 4. Pilar 2 — Cross-Tile Normal Blending

### 4.1 Idea
La normal del borde del tile A se calcula con diferencias finitas que **no ven** la fila siguiente
del tile B. Resultado: discontinuidad. Solución: cuando el píxel del borde se renderiza, **muestreamos
también la normal del vecino y promediamos con peso suave**.

### 4.2 HLSL
```hlsl
// Devuelve la normal en mundo, mezclada con la del vecino en la banda de borde.
float3 NormalBordeAlsasua(
    float2 uv01, float3 normalLocal,
    Texture2D nbrN, float2 nbrTexelN, float2 nbrOrigenN, float2 nbrEscalaN, float pesoN,
    /* …S, E, W análogo… */
    float bordeUV)
{
    float3 acc = normalLocal;
    float  w  = 1;

    // mezcla N: cae a 0 fuera de la banda; usa la primera fila del heightmap del vecino N.
    float wN = (1 - saturate((1 - uv01.y) / bordeUV)) * pesoN;
    if (wN > 0)
    {
        // muestrear normal del vecino en su borde SUR (porque ese lado limita con nuestro NORTE)
        float3 nN = NormalDesdeHeightmap(nbrN, nbrTexelN, /*v=*/ 0, /*u=*/ uv01.x);
        acc += nN * wN; w += wN;
    }
    // …S, E, W…

    return normalize(acc / w);
}

float3 NormalDesdeHeightmap(Texture2D h, float2 texel, float v, float u)
{
    // diferencias centradas; texel.x = 1/res
    float h0  = h.SampleLevel(sampler_PointClamp, float2(u,           v), 0).r;
    float hX1 = h.SampleLevel(sampler_PointClamp, float2(u + texel.x, v), 0).r;
    float hZ1 = h.SampleLevel(sampler_PointClamp, float2(u,           v + texel.y), 0).r;
    return normalize(float3(h0 - hX1, texel.x, h0 - hZ1));    // sin altura real: solo dirección
}
```

> Si los **vecinos son de distinta resolución** (cross-ring), la mezcla sigue siendo
> correcta porque el muestreo va por UV [0..1]; lo único que cambia es el `texel` del vecino
> (que el inyector publica como `_NbrTexelSize*`).

### 4.3 ¿Cuándo se hace?
Solo dentro de la banda de borde (`saturate(d/bordeUV) < 1`). Fuera de la banda el peso es 1.0
y `normalLocal` pasa íntegra. Coste cero en el interior del tile.

---

## 5. Pilar 3 — Z-Bias suave en overlap milimétrico

### 5.1 Idea
Si por seguridad el editor `MultiTileTerrainEdit` decide ocasionalmente meter overlap < 1 cm en
las faldas (no es lo nominal), el HDRP Terrain hace Z-Prepass → parpadeo. Aplicamos un **bias
hacia la cámara** que decrece linealmente desde el borde hacia el interior:

```hlsl
float DepthBiasBordeAlsasua(float2 uv01, float bordeUV, float biasM)
{
    float d = min(min(uv01.x, 1 - uv01.x), min(uv01.y, 1 - uv01.y));
    float w = 1 - saturate(d / bordeUV);    // 1 justo en el borde, 0 en el interior
    return -biasM * w;                       // sumar a posWS.z en clip (cámara hacia +Z)
}
```

`biasM = 0.003` (3 mm) es suficiente y **no rompe sombras** (queda muy por debajo del shadow bias
típico). Se aplica solo si `_OverlapBias > 0`.

### 5.2 Alternativa nativa HDRP
Si el proyecto añade un **Z-Prepass custom** (Renderer Feature) podríamos hacerlo allí en lugar
de en el vertex; en v1 mantenemos el bias por-vértice porque es **local al material del Terrain**
y no requiere tocar el pipeline.

---

## 6. Integración limpia con la arquitectura

### 6.1 Inyector C# (Systems)
[`InyectorVecinosTerreno.cs`](../Assets/Scripts/Systems/InyectorVecinosTerreno.cs) — componente
que se monta junto a `ServicioTerreno`/`CargadorMosaicoTerreno` y, tras la carga, recorre los
tiles, calcula vecinos (intra-anillo via `Terrain.leftNeighbor/...` que Unity ya rellena; inter-
anillo via `CargadorMosaicoTerreno.TerrainEn(centro+ancho/2)`) y publica un `MaterialPropertyBlock`
por tile vía `Terrain.SetSplatMaterialPropertyBlock(mpb)`. Re-aplica al cambiar el Material.

### 6.2 Asíncrono y sin coste runtime
La publicación es **una vez** por carga (anillo 0 → anillos 1-2). Si en runtime `MultiTileTerrainEdit`
toca un tile, el inyector expone `Reactualizar(Terrain)` para refrescar solo ese.

### 6.3 Toggle
`InyectorVecinosTerreno.activo` y `Shader.SetGlobalFloat("_CosturasActivas", 0|1)` → A/B en QA.

### 6.4 Material del Terrain (paso de artista)
El HDRP `TerrainLit` no es modificable, pero el componente `Terrain` permite:
1. En el Inspector del Terrain → **Material Template** → asignar uno custom (Shader Graph).
2. El Shader Graph debe basarse en **HDRP/TerrainLit** (template) y añadir:
   - Nodo `Custom Function` que `#include "Assets/Shaders/CosturasMosaico/CostuerasMosaicoAlsasua.hlsl"`.
   - En **Vertex stage**: aplicar `TessFactorBordeAlsasua` y sumar `DepthBiasBordeAlsasua` a `posWS`.
   - En **Fragment stage**: pasar la normal por `NormalBordeAlsasua` antes de escribirla al G-Buffer.
3. El inyector se ocupa de publicar los uniforms; el artista solo expone los Properties.

Si no se quiere tocar Shader Graph aún, el inyector **no rompe nada**: publica al MaterialPropertyBlock
y el shader actual ignora los uniforms desconocidos.

---

## 7. Riesgos y mitigaciones

| Riesgo | Mitigación |
|---|---|
| HDRP TerrainLit no soporta tess fácilmente | Shader Graph propio basado en `TerrainLit` (template HDRP) — soportado por API. |
| Diferencias finitas con resolución del vecino distinta | El inyector publica `_NbrTexelSize*` por vecino; la fórmula es invariante a resolución. |
| Coste de muestrear 4 normales del vecino | Solo dentro de la banda (`w > 0`); en la práctica < 5 % de los píxeles del Terrain. |
| `terrainData.heightmapTexture` puede ser null antes del bake | Inyector se ejecuta tras `CargadorMosaicoTerreno.Cargar()` (post-instancia). |
| Bias rompe alineación con colliders | El bias es **visual**; los colliders y el lattice no cambian. |

---

## 8. Validación

- **Test visual A/B**: cámara orbital a 5 distancias (1/30/100/300/1000 m) en una unión anillo 0↔1 y otra anillo 1↔2; toggle `_CosturasActivas` 0/1, screenshot comparativo.
- **Métrica**: porcentaje de píxeles con discontinuidad de normal > 5° en una banda de 2 m a ambos lados de la unión (script editor que muestrea el G-Buffer).
- **Perf gate**: `ProfilerMarker "Terreno.RenderCosturas" < 0.1 ms` por frame en `ServicioTelemetriaFrames`.

---

## 9. Archivos del entregable

```
Assets/Shaders/CosturasMosaico/CostuerasMosaicoAlsasua.hlsl   ← HLSL puro, reutilizable
Assets/Scripts/Systems/InyectorVecinosTerreno.cs              ← componente Systems
Docs/arquitectura_costuras_terreno.md                          ← este documento
```

Y un cambio mínimo aditivo:
- [`CargadorMosaicoTerreno.cs`](../Assets/Scripts/Systems/CargadorMosaicoTerreno.cs):
  expone `bool VecinoCrossRing(Terrain, Cardinal, out Terrain)` para que el inyector pueda
  resolver bordes anillo↔anillo (intra-anillo lo da Unity vía `Terrain.leftNeighbor` etc).
