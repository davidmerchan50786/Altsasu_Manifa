# Altsasu Manifa — Contexto del Proyecto

## Qué es este proyecto
Juego Unity HDRP de mundo abierto ambientado en Alsasua/Altsasu (Navarra, España). Ciudad procedural generada desde datos reales del IGN, IDENA, LIDAR, OSM y Catastro. 210 scripts C#, ~65.000 líneas.

Documentación de arquitectura: `Docs/grafo_dependencias.html` (grafo interactivo de clases y dependencias) y `Docs/informe_auditoria.md` (auditoría 2026-06). Código deprecado en `Assets/Scripts/_Deprecated~/` (Unity no lo compila): `EventManager`, `SistemaWater` (dup de `SistemaAguaRio`), `SistemaMobiliarioUrbano` (fusionado en `MobiliarioUrbano`), `DiagnosticoArranque` (fusionado en `SistemaDiagnostico`), `SistemaAPV` (dup de `SistemaAPVScenarios`), `CatalogoVivo` y `FaccionDefinition` (SO sin consumidores). Las clases `*Legacy` fueron eliminadas de `SistemasSimulacion.cs`. `OptimizadorTerreno` → renombrado `OptimizadorMallaOBJ`.

Asambleas (asmdef): `Core` ← `Runtime`/`Modules` ← `Systems` ← `Editor`. Runtime NO puede referenciar Systems/Modules — usar detección por nombre o eventos si hace falta cruzar.

Misiones: cadena M00→M12. M00 `Mision_Inicial` (tutorial "Esnatu, Altsasu", en `Runtime/MisionInicial.cs`) la lanza `SistemaMisiones` al arrancar (flag `saltarIntro`). Escena dedicada: menú `Tools/Alsasua/Escena/🎬 Crear Escena Misión Inicial`.

## Rama de trabajo
Trabajar en `main` (única rama; historia limpia sin LFS). Las ramas antiguas están archivadas como tags `archivo/main`, `archivo/cool-planck` y `archivo/misiones`. Scripts huérfanos recuperados de la historia vieja: `Assets/Scripts/_RecuperadosMain~/` (Unity no compila carpetas con `~`).

## Sistema de coordenadas — CRÍTICO
- Origen = Herriko Plaza (centro de Alsasua)
- UTM 30N ETRS89: E=567951, N=4749902
- Unity offset: OX=1918, OZ=8570
- Conversión UTM→Unity: `UnityX = (E - 567951) × 0.93687 + 1918; UnityZ = (N - 4749902) + 8570`
  — **el mundo lleva escala X = 76400/81548 ≈ 0.93687** (herencia del importador OSM;
  verificada empíricamente 2026-06 contra el MDT05 del IGN, mediana 0.19 m).
  Usar SIEMPRE `GeoDataAlsasua.UTMaUnity()/UnityAUTM()`; la identidad desplaza ~25 m a 400 m del centro.
- Escala: 1 unidad Unity = 1 metro real (en Z; en X comprimido 6.3%)
- Altura Unity = altitud_real - Z_min (511.33m)
- Cota real de Herriko Plaza: **531.94 m** (`GeoDataAlsasua.COTA_PLAZA`, validada con LIDAR+IDENA+MDT05+MDT25)
- Alturas en runtime: `TerrenoGlobal.AlturaMundo(pos)` o `GeoDataAlsasua.AlturaTerreno()` (tile-aware);
  NO usar `Terrain.activeTerrain.SampleHeight` (con el mosaico devuelve un tile arbitrario)

## Terreno — MOSAICO V2 (14.4×14.4 km, 48 tiles)
Mosaico multi-resolución estilo GTA en `Assets/AlsasuaData/terrain_tiles_v2/` (manifest_v2.json):
- Anillo 0 (urbano, plaza±1200m): 4 tiles 1200m @2049 = 0.59 m/px (LIDAR 0.5m)
- Anillo 1 (valle, ±3600m): 32 tiles 1200m @1025 = 1.17 m/px (IDENA MDT 2m 2024)
- Anillo 2 (sierras, ±7200m): 12 tiles 3600m @1025 = 3.5 m/px (IGN MDT05) — cumbres reales (Maiza 1182m, Bargagain 1153m…)
- Codificación "lattice 1/64": RAW uint16, cuanto 1/64 m, `alturaMundo = y_tile + q/64`,
  size.y = 1023.984375 en TODOS los tiles → costuras bit-exactas (igualdad de enteros)
- Pipeline: `Tools/DescargarMDT_Mosaico.py` (WCS IGN/IDENA + LAZ E:\567) → `Tools/GenerarMosaicoTerrenoV2.py`
  → `Tools/ValidarMosaicoV2.py` (GATE; no tocar Unity sin verde). Fuentes en `DatosGIS/` (regenerable).
- Unity: bake con `Tools/Alsasua/Mundo/🧩 Construir Mosaico V2` (TerrainData en Assets/Terrenos_V2/),
  auditoría con `🔍 Auditor Terreno Mosaico`; runtime fallback `CargadorMosaicoTerreno` (ServicioTerreno).
- Escritores del terrain: SIEMPRE vía `MultiTileTerrainEdit` (coords mundo, kernels idempotentes min()).
- Datos v1 obsoletos archivados en `Assets/AlsasuaData/_archivo_v1~/` (unity_terrain_info.json,
  dtm/dsm_alsasua_5m.asc, terrain_tiles/ v1 — NO usar).

## Datos de terreno disponibles (de mayor a menor resolución)
| Archivo | Resolución | Descripción |
|---------|-----------|-------------|
| `Assets/AlsasuaData/terrain_tiles_v2/` | 0.59–3.5m/px | **MOSAICO V2 (fuente actual del terreno)** |
| `Assets/AlsasuaData/lidar_dtm_05m.raw` | 0.5m/px | LIDAR PNOA 3ª cobertura, suelo desnudo, 2049×2049, uint16 LE (legacy 1 km²) |
| `Assets/AlsasuaData/lidar_ground.xyz` | puntos XYZ | 587.339 puntos reales de suelo — columnas (x_unity, cota_real, z_unity) |
| `DatosGIS/*.npz` | 0.5–25m/px | Fuentes maestras descargadas (LIDAR ampliado, IDENA 2m, MDT05/25) |
| `Assets/AlsasuaData/dem_unity_1025.raw` | ~5.9m/px | Heightmap fallback legacy 1025×1025 (terreno DEM 6 km) |

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
- `Assets/Scripts/OptimizadorMallaOBJ.cs` — LOD y chunking de la malla OBJ CloudCompare
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

## Cesium — modo híbrido (fondo lejano)
Cesium (Google Photorealistic 3D Tiles, ion ID 2275207) es SOLO fondo lejano; el suelo jugable es siempre el Terrain LIDAR local.
- `Assets/Scripts/Systems/CesiumFondoLejano.cs` — auto-arranca en Play: ancla el `CesiumGeoreference` en (OX, alturaTerreno, OZ)=Herriko Plaza (antes quedaba en 0,0,0 → tiles a 8,8 km del jugador), calibra la altura elipsoidal con `SampleHeightMostDetailed`, pone `createPhysicsMeshes=false` y SSE=32, y añade `ExcluidorTilesCercanos` (agujero de 2,8 km sin tiles alrededor de la plaza).
- La cámara TP NO debe colgar del georeference ni llevar `CesiumGlobeAnchor`/`CesiumCameraController` (pelean con `ControladorJugador`).
- `CesiumSunSky` desactivado — iluminación vía `SistemaVolumenHDRP`. Exposición: Automatic EV 11–15 (nunca fija baja con sol en lux).

## Geografía de referencia
- Alsasua es una cuenca fluvial a ~530m de altitud
- Sierra de Aralar al sur: ~1.400m
- Altzania/Urbasa al norte: ~1.000m
- Río Arakil cruza el valle de este a oeste
- Arquitectura vasca tradicional: arenisca rojiza, balcones de forja, teja árabe terracota/pizarra
