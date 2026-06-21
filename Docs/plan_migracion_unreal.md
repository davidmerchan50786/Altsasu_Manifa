# Plan de Migración: Altsasu Manifa (Unity HDRP) → Alsasua Simulator (Unreal Engine)

> Documento de planificación técnica · 2026-06-19
> Motor destino: **Unreal Engine 5.8** (release estable más reciente, 17-jun-2026). UE6 está anunciado pero su Early Access no llega hasta finales de 2027, así que NO es opción para empezar hoy.
> Scripting destino: **C++ (núcleo de sistemas) + Blueprints (gameplay y ajustes)**.

---

## 0. Resumen ejecutivo

El proyecto se divide en dos mitades con destinos muy distintos:

1. **Pipeline de datos GIS (Python)** — *independiente del motor*. Los scripts `Tools/*.py`, `Descargar*.py`, `Generar*.py` y todos los datos derivados (`Assets/AlsasuaData/*.json/.raw/.geojson`, `terrain_tiles_v2/`, ortofoto, árboles, edificios) **se reutilizan tal cual**. Esto es ~40% del valor del proyecto y migra con coste casi cero. Sólo cambia el *formato de salida final* (lo que Unity bakeaba a `TerrainData`, Unreal lo importará como Landscape/heightmap RAW o como tiles de malla).

2. **Capa Unity (210 scripts C#, ~65k líneas + HDRP)** — *hay que reescribirla*. MonoBehaviour, `ScriptableObject`, `ServiceLocator`, `EventBus`, el render HDRP y el sistema de Terrain de Unity no tienen traducción automática. Se reimplementan con los equivalentes de Unreal (Actor/Component, Subsystem, GameplayMessageRouter, Nanite/Lumen, World Partition). **No existe conversor automático Unity→Unreal**; es reescritura asistida, no traducción línea a línea.

**Conclusión de alcance:** no es un "port", es una **reimplementación de la capa de motor reutilizando el 100% de los datos y la lógica de algoritmos**. La buena noticia: la arquitectura por capas que ya tienes (Core ← World ← Entities ← Gameplay ← UI) mapea casi 1:1 a la arquitectura de Unreal (GameInstance Subsystems / World Subsystems / Actors / GameMode). El diseño no se tira; se traduce.

---

## 1. Equivalencias de motor (la tabla maestra)

| Concepto Unity | Equivalente Unreal 5.8 | Notas de migración |
|---|---|---|
| `MonoBehaviour` | `AActor` + `UActorComponent` | Lógica de mundo → Actor; lógica reutilizable → Component |
| Singleton `Instance` (Awake) | `UGameInstanceSubsystem` / `UWorldSubsystem` | Unreal gestiona el ciclo de vida; elimina los null-guards manuales |
| `ServiceLocator.Get<IServicio>()` | `GetSubsystem<T>()` (tipado, sin registro manual) | Los Subsystems **son** un service locator nativo |
| `EventBus.Publish/Subscribe<T>()` | `GameplayMessageSubsystem` (plugin GameplayMessageRouter) o delegates | Desacoplado, igual que tu EventBus |
| `static event Action<T>` | `DECLARE_MULTICAST_DELEGATE` / Blueprint Event Dispatcher | Equivalente directo |
| `[SerializeField]` | `UPROPERTY(EditAnywhere)` | Igual concepto, anotación distinta |
| `ScriptableObject` (datos) | `UDataAsset` / `UPrimaryDataAsset` / DataTable | SO de catálogo → DataTable o PrimaryDataAsset |
| Coroutines (`IEnumerator`) | `FTimerManager`, `Latent Actions`, o `UE5Coro`/Gameplay Tasks | El patrón "guardar ref y cancelar en OnDestroy" → `FTimerHandle` + `EndPlay` |
| Jobs + Burst | `ParallelFor`, `FRunnable`, o ISPC | Para tus operaciones masivas en arrays (bandas de mundo, kernels de terreno) |
| HDRP Volume | Post Process Volume + Lumen + Nanite | Ciclo día/noche → Sun Position Calc plugin / Ultra Dynamic Sky |
| Unity Terrain (mosaico 48 tiles) | **Mesh Terrain** (UE 5.8, experimental) o malla Nanite de fallback | Ver §3, es la decisión técnica más importante |
| `Terrain.SampleHeight` | `ALandscape::GetHeightAtLocation` / line trace | Igual cuidado que ya documentas: muestreo tile-aware |
| GPU Instancing | Nanite (geometría) + ISM/HISM (props) | Nanite hace obsoleto buena parte de tu LOD manual |
| NavMesh (Unity) | **NavMesh de Unreal** (Recast, nativo) | Migración cómoda; Unreal tiene navegación de serie |
| Cesium for Unity (ion 2275207) | **Cesium for Unreal** (mismo ion, mismo tileset) | Plugin oficial equivalente; tu modo "fondo lejano" se replica |
| `PlayerLoop` injection (orquestador) | `FTickableGameObject` / World Subsystem `Tick` | Tu `GlobalSimulationOrchestrator` → World Subsystem con presupuesto |
| asmdef (Core/Runtime/Systems/Editor) | **Modules** del `.uproject` (`.Build.cs`) | Las reglas "Runtime no referencia Systems" → dependencias de módulo en `Build.cs` |

---

## 2. Qué se reutiliza vs qué se reescribe

### Se reutiliza directamente (coste ~0)
- **Todo `DatosGIS/` y `Assets/AlsasuaData/`**: heightmaps RAW, GeoJSON, JSON de edificios/árboles/agua/puentes, ortofoto, tiles. Son datos, no código.
- **Todos los scripts Python** del pipeline: `DescargarMDT_Mosaico.py`, `GenerarMosaicoTerrenoV2.py`, `ValidarMosaicoV2.py`, `PipelineLIDAR_Completo.py`, etc. Sólo se les añade/ajusta un *exporter* al formato que consuma Unreal.
- **La lógica de algoritmos** dentro de los scripts C#: la *matemática* de `GeoDataAlsasua` (UTM↔mundo), la clasificación por bandas, los kernels idempotentes `min()`, la excavación de ríos, la generación de fachadas. Se reescribe la sintaxis C#→C++, pero el algoritmo es idéntico y ya está validado.
- **Texturas PBR** (`Textures_AAA/`), modelos FBX (`Models/`, `_ExtractedAssets/`): se reimportan a Unreal. Los `.fbx` entran directos; los materiales hay que recrearlos como Material Instances de Unreal.

### Se reescribe (el grueso del trabajo)
- Los 210 `.cs`: lógica de MonoBehaviour, ciclo de vida, comunicación entre sistemas.
- El render completo (HDRP → Lumen/Nanite/PPV).
- El baking de terreno Unity → Mesh Terrain (o malla Nanite de fallback), ver §3.
- La UI (uGUI/Canvas → **UMG**).
- Integraciones de terceros: **Convai** (NPCs IA/LipSync), **LiveKit**, **Gaussian Splatting**, **MapMagic/Vista** (generación de terreno), **BlockadeLabs** (skyboxes), **Reallusion CC**. Cada una necesita su equivalente Unreal o sustituto (ver §6, riesgos).

---

## 3. Decisión crítica: cómo representar el terreno en Unreal

Tu mosaico V2 (14.4×14.4 km, 48 tiles multi-resolución, codificación "lattice 1/64" con costuras bit-exactas) es el corazón del proyecto. Hay cuatro caminos en Unreal y conviene decidirlo **antes** de migrar nada más:

**Opción A — Unreal Landscape + World Partition (recomendada para empezar).**
Importas cada anillo como Landscape desde heightmap RAW uint16 (Unreal lo soporta nativamente). World Partition hace el streaming espacial que hoy hace tu `CargadorMosaicoTerreno`/`StreamerMundoEstatico`. Pro: estándar, herramientas de edición, NavMesh y físicas de serie. Con: el sistema de tiles de Landscape impone su propia resolución/proporción; tu compresión X=0.93687 y la multi-resolución por anillos hay que mapearlas con cuidado (Landscape quiere componentes uniformes).

**Opción B — Malla Nanite por tiles (la que más se parece a tu "Mosaico V3" pendiente).**
Generas mallas (OBJ/glTF) desde los heightmaps y las importas como Static Mesh con Nanite. Encaja con tu plan ya escrito de clipmap GPU (`Docs/arquitectura_mosaico_v3.md`) y con `OptimizadorMallaOBJ`. Pro: Nanite elimina LODs, las costuras bit-exactas se preservan en geometría, multi-resolución trivial. Con: pierdes las herramientas de Landscape (pintado de capas, foliage tool nativo); el agua/ríos excavados hay que hornearlos en la malla.

**Opción C — Cesium for Unreal como terreno jugable.** No recomendada: tú mismo documentas que Cesium es *solo fondo lejano* y el suelo jugable debe ser el LIDAR local. Mantén esa decisión.

**Opción D — Mesh Terrain (plugin experimental de UE 5.8).** Es el sistema nuevo de Epic para mundos grandes basado en malla (no en heightfield). Encaja con tu proyecto casi punto por punto, mejor que ninguna otra opción:
- **Resolución no uniforme nativa**: "puntos de interés con más resolución, paisaje lejano con muy baja". Es *exactamente* tu mosaico de 3 anillos (0.59 / 1.17 / 3.5 m/px) — algo que en Landscape (Opción A) es a contracorriente porque quiere componentes uniformes.
- **Geometría libre**: overhangs, túneles, paredes verticales, acantilados — tu LIDAR tiene cantiles reales (`cliff_side`) que un heightfield no puede representar.
- **Weight Channels** baqueados a un Texture2DArray → tu **splatmap de 8 biomas** entra de forma natural; pintables a mano, por modificadores **o inyectados desde PCG**.
- **PCG integrado** → tu vegetación (2.956+ árboles LIDAR) y props se siembran sobre el terreno con el mismo sistema.
- **Runtime Virtual Textures** → tu **ortofoto** (72 tiles PNOA 25 cm) se proyecta vía RVT en vez de hornearla.
- **Modificadores no destructivos** apilables → flujo iterativo, equivalente "oficial" a tu pipeline de kernels idempotentes `min()`.

En la práctica, **Mesh Terrain es la versión nativa de Epic de tu "Mosaico V3 (clipmap GPU)" pendiente** (`Docs/arquitectura_mosaico_v3.md`). El único pero es serio: **es Experimental** — Epic avisa de usarlo con precaución en proyectos a publicar (API inestable, posibles bugs, cambios entre versiones).

**Decisión: objetivo Opción D (Mesh Terrain), con Opción B (malla Nanite) como red de seguridad estable.**

Por el encaje (multi-resolución, cantiles, biomas vía weight channels, PCG, RVT para la ortofoto) Mesh Terrain es lo más AAA y lo que menos código propio te obliga a reescribir: te ahorra construir a mano el streaming multi-resolución, el pintado de biomas y la proyección de ortofoto. La contrapartida es el estado **Experimental**, así que la estrategia es **prototipar pronto y mantener salida doble**:

1. El exporter Python (`GenerarMosaicoTerrenoV2.py`) saca **dos formatos** desde los mismos heightmaps: (a) el que alimenta Mesh Terrain (malla/secciones por anillo), y (b) malla Nanite por tiles (OBJ/glTF) — el plan B. Así no quedas atado al plugin experimental.
2. **Gate de decisión al final de la Fase 2**: si Mesh Terrain rinde y es estable para tu escala (14.4 km, costuras bit-exactas, NavMesh navegable), se queda. Si te bloquea para publicar, caes a la malla Nanite estable sin rehacer el pipeline de datos.

Implicaciones comunes a vigilar en cualquiera de las dos rutas de malla: (1) el **NavMesh** se genera por colisión de la malla (Recast funciona igual, pero verifica que la colisión simplificada es navegable); (2) **agua y ríos** se resuelven con el Water plugin sobre la malla o se hornean; (3) preservar la codificación "lattice 1/64" al exportar para no perder las costuras bit-exactas.

---

## 4. Arquitectura destino (C++ + Blueprints)

Tu pila de capas se traduce así. La regla "ninguna capa referencia a la superior" se *fuerza por compilador* con dependencias de módulo en los `.Build.cs` (más estricto y fiable que los asmdef):

```
MÓDULO          UNITY (asmdef)        UNREAL (módulo .Build.cs)         Contenido
─────────────────────────────────────────────────────────────────────────────────
AlsasuaCore     Core                  Runtime, sin deps de juego        GeoData, EventBus→MessageSubsystem,
                                                                        ServiceLocator→Subsystems
AlsasuaWorld    Runtime/Modules       depende de Core                   Terreno, Zonas, Cesium, árboles,
                                                                        edificios, clima, streaming
AlsasuaEntities (en Runtime)          depende de Core (NO de World)     NPCBase→ACharacter, Jugador,
                                                                        Policía IA, Vehículos
AlsasuaGameplay Systems               depende de Core+World+Entities    GameManager→GameMode, Misiones,
                                                                        Manifestación, Armas, Apoyo
AlsasuaUI       (Runtime UI)          depende de todo, nadie depende    HUD→UMG, Audio, Polish
AlsasuaEditor   Editor                Editor-only                       Herramientas Tools/Alsasua/*
```

**Reparto C++ vs Blueprints (estándar de producción):**
- **C++**: `AlsasuaCore` entero (coordenadas, subsystems, mensajería), terreno/streaming, IA de NPCs, orquestadores de CPU/GPU, cualquier cosa con bucles por-frame o jobs. Es donde tu rendimiento ya vive y donde C++ paga.
- **Blueprints**: misiones M00→M12 (máquinas de estado visuales encajan muy bien), tuning de gameplay, UI/UMG, props colocados a mano, prototipado rápido. Patrón: clase base en C++ (`UPROPERTY`/`UFUNCTION(BlueprintCallable)`), derivada en Blueprint para iterar sin recompilar.

---

## 5. Orden de migración recomendado (por dependencias)

Como pediste que lo decida yo: el orden sigue las dependencias de tus capas, de abajo arriba, priorizando tener **algo jugable cuanto antes** (un jugador caminando sobre terreno real de Alsasua) y posponiendo lo que tiene más riesgo de terceros.

### Fase 0 — Setup y prueba de concepto (1–2 semanas)
Crear el `.uproject` UE 5.8 con módulos vacíos (§4). Instalar Cesium for Unreal y conectar el mismo ion (asset 2275207). Importar **un solo tile** de terreno (anillo 0, plaza) como malla estática (Static Mesh) desde el RAW existente — lo más rápido para validar; Mesh Terrain llega en la Fase 2. Objetivo: ver Herriko Plaza con su cota real (531.94 m) en Unreal. Esto valida el camino de datos antes de invertir en todo lo demás.

### Fase 1 — AlsasuaCore (2–3 semanas)
Portar `GeoDataAlsasua` a C++ (`UTMaUnity`/`UnityAUTM`, constantes, `COTA_PLAZA`, offsets OX/OZ). **Unidades (decidido): cm nativos de Unreal + Large World Coordinates (LWC).** Unreal trabaja en cm (1 unidad = 1 cm) y toda su física, navegación, Nanite y plugins (incluido Cesium) están calibrados así. Las funciones de conversión salen en cm (UTM→Unreal multiplica por 100 respecto a tu mundo en metros), y las constantes (OX/OZ, `COTA_PLAZA`, escala X=0.93687) se reexpresan en cm. UE 5.8 trae **LWC activado** (coordenadas en double), lo que elimina la pérdida de precisión float a 14,4 km — requisito para que la malla Nanite del mundo entero no "tiemble" en los bordes. Renombrar de paso `UnityX/UnityZ` → coordenadas de mundo Unreal para evitar confusión de unidades. Implementar el `MessageSubsystem` (equivalente a EventBus) y los Subsystems base (equivalentes a ServiceLocator). Es la base de todo: sin esto bien, nada cuadra geográficamente.

### Fase 2 — Terreno completo + streaming (3–5 semanas)
Construir el terreno con **Mesh Terrain** (Opción D, §3): un Mesh Partition por anillo con su resolución (los weight channels llevan los 8 biomas; la ortofoto entra por Runtime Virtual Texture; la vegetación por PCG). En paralelo, mantener la salida de **malla Nanite** del exporter como fallback. Portar `AlturaMundo()` tile-aware y el muestreo correcto (sobre la malla, no `SampleHeight`), y el streaming (`StreamerMundoEstatico` → World Partition + bandas por radio). **Gate al final de la fase**: confirmar que Mesh Terrain es estable y rinde a 14,4 km con costuras bit-exactas y NavMesh navegable; si no, caer al fallback Nanite. Resultado: mundo completo navegable.

### Fase 3 — AlsasuaEntities (2–4 semanas)
`ControladorJugador` → `ACharacter` + Enhanced Input (el sistema de input moderno de Unreal). NavMesh nativo (Recast) sobre el terreno. `NPCBase` → `ACharacter` + Behavior Tree (la IA de Unreal es mucho más potente que la tuya manual aquí). Aquí ya tienes un sandbox jugable.

### Fase 4 — Edificios y vegetación (3–4 semanas)
`SistemaEdificiosAAA` y generadores de fachadas/tejados → generación procedural en C++ desde los mismos JSON (`lidar_buildings.json`, `buildings_osm_rico.json`, catastro). Árboles LIDAR → **Foliage / PCG** (Procedural Content Generation de Unreal, ideal para tus 2.956+ árboles con XYZ real). Ríos y puentes → Water plugin de Unreal o malla horneada.

### Fase 5 — AlsasuaGameplay (4–6 semanas)
`GameManagerAltsasua` → `AGameModeBase` + Subsystems para IWantedSystem/IEconomyService/ISpawnService. Misiones M00→M12 → base C++ + Blueprints por misión, empezando por `Mision_Inicial` (el tutorial). Manifestación, armas, apoyo popular.

### Fase 6 — Render, UI y audio (3–5 semanas)
Lumen + Nanite + Post Process en vez de HDRP. Ciclo día/noche (`SistemaVolumenHDRP` → Sun Position / Ultra Dynamic Sky). HUD → UMG. Audio → MetaSounds + reverb zones. Aquí se decide el "look AAA" final.

### Fase 7 — Integraciones de terceros y pulido (variable, alto riesgo)
Según la ruta nativa/AAA decidida en §6: **migrar** sólo lo que no tiene equivalente nativo (Convai para NPCs conversacionales; LiveKit/Reallusion si se necesitan) y **sustituir** el resto por sistemas nativos de Unreal (MapMagic/Vista → PCG; BlockadeLabs → cielo volumétrico nativo; Gaussian Splatting → opcional sobre Nanite). Optimización final con los dos directores (CPU/GPU) reimplementados como World Subsystems.

**Estimación total muy aproximada:** 6–9 meses para una persona a tiempo completo hasta paridad funcional; el terreno + core + jugable básico (Fases 0–3) es alcanzable en ~2–3 meses y ya da un demo presentable.

---

## 6. Riesgos y ruta de terceros (decidida: siempre la opción nativa más AAA)

Criterio fijado: para cada integración se elige el **sistema nativo de Unreal más AAA** y sólo se porta el plugin de Unity cuando no existe equivalente nativo que iguale o supere su función. Así el resultado se acerca lo más posible a AAA+++.

- **No hay migración automática.** Cualquier herramienta que prometa "Unity→Unreal en un clic" no existe para lógica; sólo assets (FBX/texturas) cruzan. Presupuesta reescritura real.
- **Render / "look AAA" (sustituir, no migrar)**: HDRP → **Lumen** (GI/reflejos en tiempo real) + **Nanite** (geometría) + **Virtual Shadow Maps** + **Temporal Super Resolution**. Es la pila AAA por defecto de UE 5.8; da un salto de calidad sobre HDRP, a cambio de re-calibrar exposición, GI y materiales desde cero.
- **MapMagic / Vista (Pinwheel) → sustituir por PCG.** Son generadores de terreno *de Unity*; el equivalente nativo y más AAA es **PCG** (Procedural Content Generation) de Unreal, que además alimenta la vegetación y los props. No se migran.
- **BlockadeLabs (skyboxes) → sustituir por cielo volumétrico nativo.** Sky Atmosphere + Volumetric Clouds + Sun Position (o Ultra Dynamic Sky) dan un día/noche AAA real, superior a un skybox estático. Prescindible como dependencia.
- **Vegetación → PCG + Nanite Foliage.** Tus 2.956+ árboles LIDAR con XYZ real entran por PCG; Nanite Foliage es la opción más AAA y elimina el LOD manual.
- **Convai (mantener, migrar): NPCs conversacionales.** No hay equivalente nativo; tiene plugin oficial de Unreal. Es la integración más grande (LipSync, Vision, Narrative, RestAPI) — valida pronto que el plugin cubre tus casos. Imprescindible para tu gameplay narrativo.
- **Gaussian Splatting → opcional / evaluar.** En una pila Nanite-first no es imprescindible; los plugins 3DGS de Unreal son más jóvenes. Mantener sólo si aporta algo que Nanite no cubre (p. ej. captura fotorreal puntual). Riesgo medio.
- **LiveKit, Reallusion CC → caso por caso.** LiveKit tiene SDK multiplataforma; Reallusion CC exporta a Unreal vía su pipeline oficial. Migrables si se necesitan.
- **Cesium (mantener): bajo riesgo.** El plugin de Unreal es maduro y replica tu modo "fondo lejano" (georeference anclado en la plaza, exclusión de tiles cercanos).

---

## 7. Estrategia de validación (no romper lo que ya funciona)

Tu proyecto ya tiene una cultura de *gates* de validación (`ValidarMosaicoV2.py`, auditorías, RMSE). Mantenla en Unreal:

1. **Paridad geográfica**: tras Fase 1, un test que compruebe que `UTMaUnity(E,N)` en Unreal da las mismas coordenadas que en Unity para un set de puntos conocidos (plaza, esquinas de tiles). Tolerancia: la mediana de 0.19 m que ya validaste.
2. **Paridad de altura**: muestrear `AlturaMundo()` en N puntos y comparar con `lidar_ground.xyz`. Mismo gate que ya usas.
3. **Costuras de terreno**: verificar que la igualdad bit-exacta entre tiles se preserva tras el import.
4. **Rendimiento**: reimplementar la telemetría de los dos directores y fijar presupuestos (los ~4 ms/frame de poblado, el radio dinámico de GPU) como tests de regresión.

Trabajar en un **repo Unreal nuevo y separado**, no sobre `main` de Unity. Los datos (`DatosGIS/`, `AlsasuaData/`) pueden compartirse vía submódulo o copia, ya que son la fuente común.

---

## 8. Primeros pasos concretos (si das luz verde a empezar)

1. Instalar **Unreal Engine 5.8** y crear proyecto C++ vacío `AlsasuaSimulator`.
2. Plugins: **Cesium for Unreal**, **Water**, **PCG**, **GameplayMessageRouter**, **Enhanced Input** (la mayoría vienen de serie en 5.8).
3. Crear los 6 módulos de §4 con sus `Build.cs` y las dependencias correctas (esto fija la arquitectura de capas desde el día 1).
4. Escribir el *exporter* en `GenerarMosaicoTerrenoV2.py` con **doble salida** desde los mismos heightmaps: (a) malla por anillo para Mesh Terrain, y (b) malla Nanite por tiles (OBJ/glTF) de fallback — ambas preservando la codificación "lattice 1/64".
5. Importar el tile de la plaza y portar `GeoDataAlsasua` a C++ → **hito 1: caminar por Herriko Plaza en Unreal con coordenadas reales.**

---

*Decisiones cerradas:* (a) **terreno → Mesh Terrain (Opción D), con malla Nanite (Opción B) como fallback estable** (§3; gate al final de la Fase 2). (b) **unidades → cm nativos de Unreal + Large World Coordinates (LWC)** (§4, Fase 1). (c) **terceros → ruta nativa/AAA de Unreal**: se prioriza siempre el sistema nativo más AAA frente a portar el plugin de Unity (ver §6). Ninguna queda abierta; el plan está listo para ejecutar.

---

## 9. Stack técnico AAA+++ (decidido, sin ambigüedad)

Criterio: en cada eje se elige la opción más alta de calidad de UE 5.8, asumiendo hardware de gama alta como objetivo. Donde una opción es experimental pero claramente superior, se adopta y se documenta el fallback estable.

| Eje | Decisión AAA+++ | Fallback / nota |
|---|---|---|
| **Terreno** | Mesh Terrain (multi-resolución, cantiles, weight channels, PCG, RVT) | Malla Nanite por tiles (gate fin de Fase 2) |
| **Geometría** | Nanite en todo (edificios, props, terreno, foliage) | — (Nanite es estable y por defecto) |
| **Iluminación global** | Lumen con **Hardware Ray Tracing** | Software Lumen en GPU sin RT |
| **Reflejos** | Lumen Reflections (calidad high) | — |
| **Sombras** | Virtual Shadow Maps | — |
| **Muchas luces dinámicas** (ciudad de noche) | **MegaLights** (UE 5.8) | VSM + culling si MegaLights inestable |
| **Materiales** | **Substrate** (shading avanzado, capas) | Materiales estándar si Substrate da problemas |
| **Anti-aliasing/upscaling** | TSR (Temporal Super Resolution) | DLSS/FSR como plugin opcional |
| **Coordenadas** | cm nativos + LWC (double) | — |
| **Mundo abierto** | World Partition + One File Per Actor + Data Layers + HLOD | — |
| **Texturas** | Virtual Textures (Streaming) + RVT para la ortofoto | — |
| **Input** | **Enhanced Input** (IMC + InputActions) | El bootstrap usa input clásico; se migra en Fase 1 |
| **Animación de personajes** | **Motion Matching** (Game Animation Sample) + Control Rig + IK Rig | Anim Blueprints clásicos |
| **IA de NPCs** | Behavior Tree + StateTree + EQS + Smart Objects + Mass (multitud) | — |
| **VFX** | Niagara | — |
| **Audio** | MetaSounds + Audio Modulation + reverb por submix | — |
| **Físicas/destrucción** | Chaos (+ Chaos Vehicles para coches) | — |
| **Cielo / día-noche** | Sky Atmosphere + Volumetric Clouds + Volumetric Fog + Sun Position | — |
| **Agua** | Water plugin (Single Layer Water) sobre la malla | Malla de agua horneada |
| **NPC conversacional** | Convai (plugin oficial Unreal) | — |
| **Fondo lejano** | Cesium for Unreal (ion 2275207), solo horizonte | — |
| **Streaming/perf** | Reimplementar los dos directores (CPU/GPU) como World Subsystems con presupuesto | — |

Estas elecciones están reflejadas en los ajustes de `Config/DefaultEngine.ini` del proyecto Unreal (Lumen HW RT, VSM, MegaLights, Substrate, Nanite). Los plugins experimentales (Mesh Terrain, MegaLights, Substrate) se activan desde el principio pero cada uno con su fallback estable identificado, para que ninguno pueda bloquear la publicación.
