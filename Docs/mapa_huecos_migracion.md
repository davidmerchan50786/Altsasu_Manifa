# Mapa de huecos — migración Unity → Unreal (Alsasua Simulator)

> 2026-06-24 · Estado del puerto C++ del proyecto `Alsasua_Simulator\UnrealProject` (UE 5.8).
> Complementa `plan_migracion_unreal.md`. Sirve de hoja de ruta para "portar todo lo que falta".

## Resumen

| | Unity (.cs) | Notas |
|---|---|---|
| Total scripts | **393** | Core 74 · Runtime 155 · Systems 67 · Editor 74 · Modules 20 · Crowd 3 |
| Ya portado a C++ | **~59 clases** (~15 %) | Sobre todo gameplay (subsistemas) + carga de mundo |
| **A portar de verdad** | **~110** | Lo que requiere C++/Blueprint nuevo (ver §2) |
| **Sustituir por nativo** | **~70** | Render HDRP, terreno clipmap, Cesium, crowd → Lumen/Nanite/Mesh Terrain/Mass (§3) |
| **Descartar (pipeline/editor)** | **~150** | Generadores e importadores: su salida ya está como datos/contenido (§4) |

Idea clave: **no son 334 ports pendientes.** Casi la mitad son herramientas de pipeline cuyo resultado ya importamos, y otro tercio se reemplaza por sistemas nativos de Unreal. El trabajo real de código nuevo son ~110 sistemas, agrupados abajo por prioridad.

---

## 1. Ya portado (no tocar, solo verificar que compila)

Núcleo y mundo: `GeoDataAlsasua`, `ArranqueMundo`, `DirectorArranque` (SceneBootstrapper), `StreamerMundoEstatico`, `GobernadorRender`, `NavMeshAlsasua`, cargadores `Cargador{Arboles,Calles,Edificios,Poligonos,Vias}`, `TrianguladorPoligono`, `PoligonoSuelo`, `EdificioGenerado`, `CalleGenerada`, `ImportadorLandscape`, `Creador*Material` (agua/árbol/edificio/fachada/tejado/terreno).

Jugador y entidades: `AlsasuaCharacter` (ControladorJugador), `AlsasuaNPC` (NPCBase), `AlsasuaPlayerController`, `PoliciaActor/Controller`, `PeatonActor/Controller`, `ManifestanteActor/Controller`, `VehiculoAmbiente`, `NegocioActor`, `PisoFrancoActor`, `ArmasComponent` (parcial).

Gameplay (subsistemas): `Wanted`, `Economia`, `EconomiaCriminal`, `Drogas`, `Progresion`, `ApoyoPopular`, `Consecuencias`, `Disfraz`, `DiaNoche`, `Respawn`, `Refuerzos`, `Poblacion`, `Trafico`, `Dialogo`, `Manifestacion`, `Misiones`, `CicloVisual`, `Clima`, `AudioAmbiente`, `GameMode`, `HUD` (parcial).

---

## 2. A PORTAR — código C++/Blueprint nuevo (el trabajo real)

Ordenado por prioridad/dependencias. Cada lote debe compilar en verde antes del siguiente.

### Lote A — Núcleo que falta (Core) · alta prioridad
Es la base sobre la que se apoya el resto; conviene primero.
- **Mensajería y estado**: `EventBus` → `GameplayMessageSubsystem`; `EstadoMundo`/`DirectorMundo`/`GestorEstadoCiudad`; `AltsasuCore`/`GameManagerAltsasua` (consolidar en GameMode + WorldSubsystem).
- **Orquestación CPU**: `GlobalSimulationOrchestrator` (director de ticks por frecuencia + Sim-LOD) → World Subsystem `Tick`; `Telemetria`/`ServicioTelemetriaFrames`.
- **Grid espacial**: `UnifiedSpatialGrid`/`GridConsultas`/`OmniGrid*`/`PublicadorGrid` → grid de consultas espaciales (Mass o propio).
- **Persistencia**: `PersistenceManager`/`IPersistenceService`/`SistemaGuardado` → SaveGame de Unreal.
- **IA base**: `SistemaIA`/`PlanificadorGOAP`/`IAction`/`IAgente`/`IGoal`/`PoolGhosts` → StateTree/Behavior Tree + (opcional) GOAP plugin.
- **Muestreo de altura**: `TerrenoGlobal`/`IMuestreadorAlturaPrecisa`/`MultiTileTerrainEdit` → adaptador sobre Mesh Terrain/Landscape.

### Lote B — IA de fuerzas del orden y "calor" · alta prioridad (es el corazón del juego)
- Cerebros: `CerebroGOAPPolicia`, `CerebroGuardiaCivil`, `PatrullaGuardiaCivil`, `ControlGuardiaCivil`, `ConvertibleGuardiaCivil`, `AccionesPolicia`, `ContextoPolicia`, `MetasPolicia`.
- Presión y consecuencias: `SistemaParanoiaGuardiaCivil`, `SistemaControlesGC`, `SistemaCargasPoliciales`, `SistemaInterrogatorio`, `SistemaInformantes`, `SistemaTestigos`/`TestigoNPC`, `SistemaCoartada`/`ZonaCoartada`.

### Lote C — Combate y locomoción del jugador · media-alta
- Armas/combate: `SistemaDisparo`, `SistemaArmasExtendido`, `SistemaCombateMelee`, `SistemaImpactos`, `SistemaExplosion`, `RuedaArmas`, `VisualArmaMano`, `SistemaLockOn`, `SistemaHUDCombate`.
- Movilidad: `SistemaCobertura`/`SistemaCoberturasIA`, `SistemaEsquiva`, `SistemaParkour`, `SistemaSaltoTejados`, `SistemaNado`, `SistemaAccionesPersonaje`, `SistemaGameFeel`.

### Lote D — NPCs y vida urbana · media
- `NPCCivil`, `NPCGuard`, `VehiculoNPC`, `SistemaAgendaNPC`, `SistemaReaccionNPCs`, `ReaccionAlJugador`, `VariadorAparienciaNPC`, `SistemaFauna`, `SemaforoNodo`, `SistemaTren`.

### Lote E — Misiones, narrativa y economía criminal · media
- `MisionInicial`, `MisionesSec`, `MisionesAltsasua`, `GuionActoI`/`GuionActoII`, `BootstrapMisiones`, `SistemaTutorial`.
- `MercadoNegro`, `Sabotaje`, `SistemaTerritorio`/`PintadaTerritorio`/`SistemaGrafitis`, `SistemaReparto`/`PuntoReparto`, `Zulo`, `SistemaEventosMundo`.
- Manifestación: `SistemaMoralManifestacion`, `AliadoApoyo`, `ComandoApoyo`.

### Lote F — Vehículos del jugador · media-baja
- `ControladorVehiculoJugador`, `ControladorVehiculoSimple`, `VehiculoBase`, `SistemaDañoFisicoVehiculo`, `IndicadorEntradaVehiculo` → sobre **Chaos Vehicles**.

### Lote G — Meta, UI y progresión · baja (al final)
- `SistemaLogros`, `SistemaRecompensas`, `SistemaLocalizacion`, `SistemaMinimapa`, `PanelInventario`, `SistemaTienda`, `MenuPausa`/`MenuPrincipal`, `PantallaCarga`, `HUDParanoia`, `SintoniaAltsasu` (panel de tuning), `SistemaOpciones`, `SistemaCalidadGrafica`.

---

## 3. SUSTITUIR por sistema nativo (no portar 1:1)

| Unity | Nativo Unreal |
|---|---|
| `SistemaVolumenHDRP`, `ConversorMaterialesHDRP`, `SistemaPostProcesoAAA`, `SistemaReflexiones`, `SistemaOcclusion`, `SistemaNeblina`, `SistemaDecalesHDRP/AAA`, `OptimizadorVisualHDRP`, `SistemaShaderGlobals` | **Lumen + Nanite + Post Process Volume + Virtual Shadow Maps + Decals** nativos |
| `MosaicoV3*`, `Clipmap*`, `GeneradorTerrenoUltraPreciso`, `MuestreadorAltura*`, `ServicioTerreno`, `StreamerColliderTerreno` | **Mesh Terrain** (o Landscape) + World Partition |
| `CesiumFondoLejano`, `CesiumCapasAlsasua` | **Cesium for Unreal** (plugin) |
| `AplicadorOrtofoto`, `DrapeOrtofotoLejana` | **Runtime Virtual Texture** sobre el terreno |
| `AlsasuaTreeStreamer`, `SembradoVegetacionManual`, `SistemaVientoVegetacion`, `MobiliarioUrbano`, `GeneradorRocasProcedurales` | **PCG + Nanite Foliage** |
| `SistemaMultitudBRG`, `RenderizadorMultitudBRG`, `MultitudJobs`, `SistemaMultitud` | **Mass Entity + ISM/HISM** |
| `SistemaImpostores`/`GestorImpostores`/`ImpostorBillboard` | **Nanite** (hace innecesarios casi todos los impostores) |
| `SistemaRagdoll`, `SistemaFootIK`, `SistemaIKProcedural`, `LocomocionAnimatorFallback` | **Control Rig + IK Rig + Motion Matching** |
| `SistemaDestruccion`, `SistemaExplosion` (física) | **Chaos Destruction** |
| `SistemaAmbientParticulas`, `SistemaHumoFabricas`, `SistemaCharcos`, `SistemaNevadasTerreno`, `SistemaClimaEfectos` | **Niagara** |
| `AudioManager`, `SistemaReverbZonas`, `SistemaAudioMultitud`, `SistemaMusicaAdaptativa` | **MetaSounds + Audio Submix/Reverb** |

---

## 4. DESCARTAR — pipeline e importadores (su salida ya es dato/contenido)

No se reescriben: generaban los datos que **ya importamos** o que produce el pipeline Python.
- **Generadores de geometría**: `GeneradorFachadasAAA`, `GeneradorTejadosAAA`, `GeneradorGeometriaPrecisa`, `GeneradorMundoOSM`, `FusionadorEdificiosUltra`, `GeneradorCallesAltsasu`, `GeneradorInteriores*`, `GeneradorRocasProcedurales`.
- **Importadores/procesadores**: `ProcesadorNubePuntos`, `ProcesadorMapillarObjetos`, `AutoImportadorIncoming`, `AplicadorTexturasReales`, `AplicadorManchaChistorra`, `OptimizadorMallaOBJ`, `DecimadorMeshLOD1`, `IntegradorAssets`.
- **Los 74 scripts de `Editor/`**: herramientas de editor (menús `Tools/Alsasua/*`, validadores, auditores). Solo se recrean como Editor Utilities de Unreal **si** hace falta una herramienta concreta; no son parte del runtime.

---

## 5. Orden recomendado (lotes verificados)

1. **Compilar en verde** lo ya portado (requiere el toolchain C++ de Visual Studio instalado).
2. **Lote A** (núcleo) → compilar.
3. **Lote B** (IA policial / calor) → compilar.
4. **Lotes C–G** en ese orden, compilando entre cada uno.
5. En paralelo, ir activando los **sistemas nativos** del §3 (no bloquean el código de gameplay).

Cada lote: portar → `Build Solution` en verde → commit. Nunca acumular varios lotes sin compilar.
