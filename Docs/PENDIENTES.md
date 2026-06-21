# Backlog de pendientes — Altsasu_Manifa (2026-06-21)

Lista escaneada del código (no genérica). Priorizada por valor/riesgo/esfuerzo.
Para el estado general ver `ESTADO_PROYECTO.md`.

| # | Pendiente | Valor | Riesgo sin Unity | Esfuerzo |
|---|-----------|-------|------------------|----------|
| 1 | Reconstruir escena UTM real (`▶▶ APLICAR TODO`) | Alto | — (solo editor) | 1 clic |
| 2 | Fases 3 impostores + clipmap (shaders HDRP/BRG) | Alto | Alto | Media-alta |
| 3 | Migrar API HDRP obsoleta (CS0618, 18 ficheros) | Medio | Alto | Media |
| 4 | TODO gameplay: arresto → pantalla detención/game over | Medio | Medio | Baja |
| 5 | TODO: registrar edificios FBX en `SistemaZonas` | Bajo | Medio | Baja |
| 6 | Limpiar campos muertos (CS0414, 13) | Bajo | Bajo-medio | Baja |

---

## 1. Reconstruir escena en UTM real  *(bloqueado: reiniciar PC)*
Todo el dato/terreno está corregido en disco. Falta ejecutar en el editor
`Tools ▸ Alsasua ▸ ▶▶ APLICAR TODO (UTM real)` y validar con el gate. Ver `ESTADO_PROYECTO.md`.

## 2. Fases 3 de la deuda AAA  *(necesita editor para validar)*
Diseño + scaffolding hechos (`_Impostores~`, `_ClipmapV3~`, ADR). Falta lo que solo se
valida con Unity delante:
- ShaderGraph HDRP: octaédrico del impostor + displacement del clipmap (vertex texture fetch del R16).
- Batching BRG de impostores (1 draw call/atlas).
- Cablear `MuestreadorHeightmapV3` (ya hecho y validado) a `ServicioTerreno` (`FuenteTerreno.ClipmapV3`).
- Collider-parche que sigue al jugador; portar `MultiTileTerrainEdit` al R16.

## 3. Migración de API HDRP/Unity obsoleta (CS0618)
18 ficheros suprimen el warning en vez de migrar (sobre todo `Light.intensity` →
`HDAdditionalLightData`/unidades físicas). **Funciona, pero es deuda.** Riesgo alto a ciegas:
las unidades de intensidad cambian (lumen/lux) y un valor mal puesto rompe la iluminación.
Hacerlo **con el editor** y verificando visualmente. Ficheros:
`Core/SistemaDestruccion`, `Editor/ImportadorEdificiosFBX`, `Runtime/{PosicionadorPrecisionUrbana,
SistemaExplosion, SistemaPolish, SistemaDiagnostico, SistemaEdificiosAAA, SistemaVolumenHDRP,
SistemaClima}`, `Systems/{PropsDestruccionManifestacion, SistemaVidaNocturna, SceneBootstrapper,
ProcesadorMapillaryObjetos}`, `Modules/{GeneradorInterioresSimples, AplicadorTexturasReales,
InterioresExplorables}`, `Crowd/RenderizadorMultitudBRG`.
Plan: 1 fichero piloto (p.ej. `SistemaPolish`) → confirmar look → extender. Quitar el pragma al migrar.

## 4. Gameplay: arresto del jugador → pantalla de detención / game over
`Assets/Scripts/Runtime/IA/CerebroGOAPPolicia.cs` → `Arrestar()` solo loguea. Falta encadenar
con el flujo de fin de partida. Lo natural: publicar un `PlayerArrestedEvent` por `EventBus` que
`HUDCanvas`/`GameManagerAltsasua` consuman (fade + pantalla), reutilizando el patrón de
`PlayerDeathEvent`. Bajo esfuerzo pero conviene probar el flujo en Play.

## 5. Unificar modelo de datos FBX ↔ OSM y registrar en SistemaZonas
`Assets/Scripts/Editor/ImportadorEdificiosFBX.cs` importa FBX pero no construye un `EdificioData`
completo ni lo registra en `SistemaZonas` (solo loguea). Requiere unificar `BuildingData`
(importador) con `EdificioData` (`GeneradorMundoOSM`). Valor bajo (camino FBX es secundario).

## 6. Limpieza de campos muertos (CS0414)
13 campos privados asignados y nunca leídos. Cuidado: algunos pueden ser `[SerializeField]`
(usados por el inspector) o reservados. Revisar uno a uno antes de borrar; no es batch ciego.

---

### Recomendación de orden
Tras reiniciar y abrir el editor: **1 → gate → 4** (rápido y visible) → **2** (las fases AAA,
con el editor para iterar shaders) → **3** (migración HDRP, pilotada) → **5/6** (menores).
