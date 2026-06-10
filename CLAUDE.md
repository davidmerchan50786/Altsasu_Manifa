# Altsasu Manifa — Contexto del Proyecto

## Qué es este proyecto
Juego Unity HDRP de mundo abierto ambientado en Alsasua/Altsasu (Navarra, España). Ciudad procedural generada desde datos reales del IGN, IDENA, LIDAR, OSM y Catastro. 119 scripts C#, ~37.000 líneas.

## Rama de trabajo
Trabajar en `main` (única rama; historia limpia sin LFS). Las ramas antiguas están archivadas como tags `archivo/main`, `archivo/cool-planck` y `archivo/misiones`. Scripts huérfanos recuperados de la historia vieja: `Assets/Scripts/_RecuperadosMain~/` (Unity no compila carpetas con `~`).

## Sistema de coordenadas — CRÍTICO
- Origen = Herriko Plaza (centro de Alsasua)
- UTM 30N ETRS89: E=567951, N=4749902
- Unity offset: OX=1918, OZ=8570
- Conversión UTM→Unity: `UnityX = (E - 567951) + 1918; UnityZ = (N - 4749902) + 8570`
- Escala: 1 unidad Unity = 1 metro real
- Altura Unity = altitud_real - Z_min (511.33m)

## Datos de terreno disponibles (de mayor a menor resolución)
| Archivo | Resolución | Descripción |
|---------|-----------|-------------|
| `Assets/AlsasuaData/lidar_dtm_05m.raw` | 0.5m/px | LIDAR PNOA 3ª cobertura, suelo desnudo, 2049×2049, uint16 LE |
| `Assets/AlsasuaData/lidar_ground.xyz` | puntos XYZ | 587.339 puntos reales de suelo |
| `Assets/AlsasuaData/dtm_alsasua_5m.asc` | 5m/px | DTM IGN para montañas |
| `Assets/AlsasuaData/dsm_alsasua_5m.asc` | 5m/px | DSM IGN (incluye edificios y vegetación) |
| `Assets/AlsasuaData/dem_unity_1025.raw` | ~1m/px | Heightmap preprocesado 1025×1025 |

Meta LIDAR (`lidar_dtm_meta.json`):
- heightmapResolution: 2049
- terrainWidth/Length: 1024m
- terrainHeight: 57.26m
- Z_min: 511.33m, Z_max: 568.59m

## Datos de ortofoto
- `Assets/AlsasuaData/ortofoto_alsasua_REAL.png` — ortofoto completa PNOA 25cm/px
- `Assets/AlsasuaData/orto_tiles_meta.json` — 72 tiles JPEG 1320×1320px a 0.25m/px con bbox UTM exacto (ux_min/uz_min/ux_max/uz_max en coordenadas Unity)
- `Assets/AlsasuaData/ortofoto_unity.png` — versión comprimida para Unity

## Datos de vegetación y agua
- `Assets/AlsasuaData/lidar_trees.json` + `trees_unity.json` — 2.956+ árboles con XYZ real (LIDAR clase 5)
- `Assets/AlsasuaData/bosques.geojson` + `masas_forestales.geojson` — polígonos exactos de masa forestal
- `Assets/AlsasuaData/rios_ejes.geojson` — ejes de ríos Arakil y afluentes
- `Assets/AlsasuaData/lidar_agua.json` — láminas de agua LIDAR (clase 9)
- `Assets/AlsasuaData/lidar_puentes.json` — puentes detectados LIDAR (clase 17)

## Datos de edificios
- `Assets/AlsasuaData/lidar_buildings.json` — alturas reales LIDAR, error <0.1m (FUENTE PRIMARIA)
- `Assets/AlsasuaData/buildings_osm_rico.json` — 1.030 edificios con 20 tags OSM
- `Assets/AlsasuaData/catastro_edificios.json` + `catastro_parcelas.json` — footprints cm, año, uso
- `Assets/AlsasuaData/buildings_final.json` + `buildings_unity.json` — fusión procesada
- `Assets/AlsasuaData/overture_buildings.geojson` + `microsoft_footprints.json` — footprints ML
- `Assets/AlsasuaData/mapillary_imagenes.json` + `mapillary_objetos.json` — fotos reales + detecciones ML

## Scripts principales de terreno
- `Assets/Scripts/GeneradorTerrenoUltraPreciso.cs` — heightmap LIDAR, resample bicúbico, validación RMSE
- `Assets/Scripts/SistemaTerreno.cs` — splatmap 8 biomas
- `Assets/Scripts/AplicadorOrtofoto.cs` — proyección 72 tiles
- `Assets/Scripts/AlsasuaTreeStreamer.cs` — streaming árboles LIDAR
- `Assets/Scripts/GeneradorRiosYPuentes.cs` — excavación ríos + shader agua + puentes
- `Assets/Scripts/OptimizadorTerreno.cs` — LOD y chunking
- `Assets/Scripts/GeoDataAlsasua.cs` — constantes y coordenadas centralizadas

## Scripts principales de edificios
- `Assets/Scripts/SistemaEdificiosAAA.cs` — 12 arquetipos vascos
- `Assets/Scripts/GeneradorFachadasAAA.cs` — fachadas modulares
- `Assets/Scripts/GeneradorTejadosAAA.cs` — tejados con forma real
- `Assets/Scripts/GeneradorGeometriaPrecisa.cs` — footprints exactos
- `Assets/Scripts/FusionadorEdificiosUltra.cs` — fusión 11 fuentes de datos

## Scripts principales de gameplay
- `Assets/Scripts/GameManagerAltsasua.cs` — implementa IWantedSystem, IEconomyService, ISpawnService
- `Assets/Scripts/Core/ServiceLocator.cs` — registro de servicios por interfaz
- `Assets/Scripts/AltsasuCore.cs` — singleton central, eventos globales
- `Assets/Scripts/ControladorJugador.cs` — movimiento y cámara
- `Assets/Scripts/NPCBase.cs` — base de NPCs con IA
- `Assets/Scripts/SistemaChunks.cs` — streaming de chunks del mundo
- `Assets/Scripts/SistemaZonas.cs` — zonas de manifestación y gameplay

## Assets de render
- Pipeline: **HDRP** (High Definition Render Pipeline)
- Perfil Volume: `Assets/Settings/HDRP Balanced`
- `Assets/Scripts/SistemaVolumenHDRP.cs` — ciclo día/noche, SSAO/SSR/Bloom/DoF/Fog
- `Assets/Scripts/ConversorMaterialesHDRP.cs` — conversión materiales legacy → HDRP
- `Assets/Scripts/OptimizadorVisualHDRP.cs` — GPU instancing, LOD, occlusion

## Texturas PBR disponibles
- `Assets/Textures_AAA/Naturaleza/` — cliff_side, bicolour_gravel, bark_brown_01/02, clean_pebbles, bark_willow (Albedo+Normal+ARM+Displacement)
- `Assets/Textures_AAA/Fachadas/` — texturas fachada PBR
- `Assets/Textures_AAA/Tejados/` — teja árabe con Displacement
- `Assets/Textures_AAA/Madera/` — madera para balcones y carpintería
- `Assets/Textures_AAA/Metal/` — forja, canalones, rejas
- `Assets/Textures_AAA/Suelo/` — asfalto, adoquín, portal
- `Assets/Textures_AAA/TerrainLayers/` — capas terrain ya configuradas

## Kit modular edificios
- `Assets/Models/Buildings_Extracted/` — Roof_Straight/Convex/Concave (14 piezas tejado), Canopy (10 piezas), Kit_Window_Upper_Convex+Straight, Chimney_Stone+Pipe, Props
- `Assets/_ExtractedAssets/Buildings/` — lisbon_building.fbx, lisbon_building_2.fbx
- `Assets/_ExtractedAssets/Textures/` — wall_cobblestone_cracks, wall_bricks_old, brick7
- `Assets/_ExtractedAssets/Props/Urban/` — bridge_roads.fbx, electrical_panel.fbx, playground_fbx.fbx
- `Assets/_ExtractedAssets/Props/PolyHaven/` — Lantern_01.fbx, Megaphone_01.fbx, Shelf_01.fbx

## Convenciones de código
- Singletons con patrón `Instance` null-guarded en Awake
- Comunicación entre sistemas vía `ServiceLocator.Get<IInterfaz>()` o eventos ScriptableObject
- Sin `FindObjectOfType` fuera de Awake/Start
- Sin `new List/HashSet` en Update — usar buffers reutilizables
- Sin string concat en Update — usar StringBuilder o precalcular
- Corrutinas: siempre guardar referencia y cancelar en OnDestroy
- Jobs Burst para operaciones masivas en arrays
- GPU Instancing activado en todos los materiales (`enableInstancing = true`)

## Arquitectura de capas

Regla estricta: ninguna capa puede referenciar directamente a la capa superior.

```
CORE      AltsasuCore · ServiceLocator · EventBus · GeoDataAlsasua
            ↑ nadie la rompe desde abajo
WORLD     GeneradorMundoOSM · SistemaZonas · SistemaTerreno · SistemaNavMesh
            · AlsasuaTreeStreamer · SistemaEdificiosAAA · SistemaClima
            → publica: ChunkLoadedEvent · ZoneChangedEvent (EventBus)
ENTITIES  NPCBase · PoliciaForalIA · ControladorJugador · VehiculoBase
            → publica: PlayerDeathEvent (EventBus)
GAMEPLAY  GameManagerAltsasua · SistemaMisiones · SistemaArmasExtendido
            · SistemaManifestacion · SistemaApoyoPopular
            → consume servicios vía ServiceLocator<IWantedSystem/IEconomyService>
UI/AUDIO  HUDCanvas · AudioManager · SistemaPolish · SistemaReverbZonas
            → suscribe a eventos EventBus de todas las capas
```

### Comunicación entre capas
| Mecanismo | Uso |
|-----------|-----|
| `ServiceLocator.Get<IServicio>()` | Gameplay → Core (Wanted, Economy, Spawn) |
| `EventBus.Publish/Subscribe<T>()` | World/Entities → UI/Audio (sin acoplamiento) |
| `static event Action<T>` | Mismo sistema o herencia directa |
| Inspector `[SerializeField]` | Solo dentro de la misma capa |

### Eventos EventBus activos
- `PlayerDeathEvent` — publicado por `ControladorJugador.Morir()` y `GameManagerAltsasua.JugadorMuerto()`. Suscriptor: `HUDCanvas` (fade negro).
- `ChunkLoadedEvent` — publicado por `SistemaZonas` al cargar/descargar zonas OSM.
- `ZoneChangedEvent` — publicado por `SistemaZonas.Update()` al detectar cambio de celda.

### Deuda técnica — RESUELTA

Todas las deudas anteriores están corregidas:
- `SembrarArboles` extraído a `SembradoVegetacionManual.cs` (GameManager llama fallback legacy si el componente no existe)
- `GameManagerAltsasua` no tiene referencias a `Text` — publica `OnEconomiaCambia(dinero, puntuacion)` via evento estático; `HUDCanvas` suscribe
- `PuntosAlsasua` en `MisionesAltsasua.cs` es ahora wrapper delgado que delega a `GeoDataAlsasua`; `GeoDataAlsasua` es la única fuente de verdad para coordenadas
- `SistemaChunks.ComprobarChunks()` usa posición del vehículo raíz cuando `ISpawnService.JugadorEnVehiculo == true`
- `AlsasuaTreeStreamer.InicializarAsync()` espera hasta 30s a que `Terrain.activeTerrain != null` antes de clasificar especies
- `GeoDataAlsasua` expone `JugadorPos()`, `CarreteraN1Norte/Sur`, `HerrikoPlaza` con `OX/OZ` como origen

## Geografía de referencia
- Alsasua es una cuenca fluvial a ~530m de altitud
- Sierra de Aralar al sur: ~1.400m
- Altzania/Urbasa al norte: ~1.000m
- Río Arakil cruza el valle de este a oeste
- Arquitectura vasca tradicional: arenisca rojiza, balcones de forja, teja árabe terracota/pizarra
