# Altsasu Manifa

**Juego de mundo abierto ambientado en Alsasua/Altsasu (Navarra, España)**  
Unity HDRP · C# · GIS real · Procedural · Post-apocalíptico

---

## Descripción

*Altsasu Manifa* reconstruye digitalmente la ciudad de Alsasua y su entorno (14,4 × 14,4 km) a partir de datos geográficos reales: LIDAR PNOA, MDT05/MDT25 del IGN, ortofoto 25 cm/px, edificios de Catastro + OSM + Overture, ríos del IDENA y más de 2.950 árboles clasificados por especie.

El mundo es post-apocalíptico: edificios abandonados, barreras, vegetación que recupera las calles. La ciudad generada proceduralmente se enriquece con assets reales (fachadas, tejados, interiores) y se distribuye como cadena de misiones (M00–M12) alrededor de la manifestación en Herriko Plaza.

### Características principales

- **Terreno real** — mosaico multi-resolución 14,4 km (48 tiles Unity Terrain): LIDAR 0,5 m en el casco urbano, MDT 2 m en el valle, MDT05 3,5 m en las sierras (Aralar, Altzania/Urbasa).
- **Ciudad procedural** — footprints de Catastro + OSM → 1.030 edificios con arquetipos vascos, fachadas modulares PBR, tejados con geometría real.
- **Fondo lejano** — ortofoto PNOA real drapeada sobre el relieve (Python + shader), Cesium Google Photorealistic 3D Tiles (solo anillo >7 km, throttleado por GPU).
- **Vegetación real** — 2.950+ árboles LIDAR con XYZ real; impostores billboards por especie para el fondo; AlsasuaTreeStreamer con streaming por radio.
- **IA y simulación** — NPCs con FSM + GOAP opt-in, multitud BRG (1 draw call), SistemaManifestacion con zonas y facciones.
- **Misiones** — cadena M00→M12, tutorial narrado en Alsasua (Mision_Inicial).
- **HDRP** — ciclo día/noche, SSAO, SSR, Bloom, DoF, Fog volumétrico, PBR en todos los materiales.
- **Rendimiento** — gobernador GPU (radio de mundo dinámico), streamer de ciudad (3 bandas: activo/impostor/oculto), 48 Terrain reemplazables por 3 draw calls (MosaicoV3), GPU Instancing en todos los materiales.

---

## Capturas rápidas

> Las capturas se añaden cuando el build esté estabilizado.  
> Para explorar la escena: menú **Tools → Alsasua → 🎬 Crear Escena Misión Inicial** y luego Play.

---

## Requisitos

| Componente | Versión mínima |
|---|---|
| Unity | 6000.0.x (LTS) con HDRP |
| Cesium for Unity | 1.23+ |
| Git | Sin Git LFS (los `.raw` de heightmap están sin LFS) |
| Python (pipelines GIS) | 3.11+ con `numpy`, `scipy`, `requests`, `laspy` |
| Blender (opcional) | 4.x para LOD/fotogrametría |

**GPU recomendada:** RTX 3060 o superior (HDRP + Cesium + GPU Instancing).

---

## Instalación y primeros pasos

```bash
git clone https://github.com/<usuario>/Altsasu_Manifa.git
```

1. Abre el proyecto en Unity Hub con la versión indicada.
2. Unity importará los paquetes automáticamente.
3. **Cesium**: el token de Cesium Ion está en el proyecto (`CesiumSettings/Resources/CesiumIonServers/`). Si necesitas el tuyo, reemplázalo ahí.
4. Abre la escena `Assets/#Scenes/Alsasua_Main.unity`.
5. En Play, `CesiumFondoLejano` arranca automáticamente y `SceneBootstrapper` construye el mundo por fases.

### Primer terreno

Si los Terrain tiles no están bakeados aún:

```
Tools → Alsasua → Mundo → 🧩 Construir Mosaico V2
```

Esto genera los 48 Unity Terrain desde `Assets/AlsasuaData/terrain_tiles_v2/` (manifest_v2.json).

### Primera ciudad

```
Tools → Alsasua → Mundo → 🏗️ Hornear Ciudad
```

Fusiona footprints de Catastro/OSM y coloca assets de edificios horneados en `CiudadHorneada/`.

---

## Arquitectura

### Asambleas (asmdef)

```
Core
 ├── Runtime          (terreno, jugador, entidades)
 ├── Modules          (música, impostores, interiores, IA GOAP)
 └── Systems          (edificios, arrancador de escena, gobernador GPU, terreno mosaico)
      └── Editor      (wizards, importadores, validadores GIS)
```

**Regla estricta:** ninguna capa puede referenciar a una capa superior.  
`Runtime` NO puede referenciar `Systems`/`Modules` — usar `ServiceLocator` o eventos.

### Comunicación entre capas

| Mecanismo | Cuándo usarlo |
|---|---|
| `ServiceLocator.Get<IServicio>()` | Gameplay → Core (Wanted, Economy, Spawn, Terrain) |
| `EventBus.Publish/Subscribe<T>()` | World/Entities → UI/Audio (sin acoplamiento) |
| `static event Action<T>` | Mismo sistema o herencia directa |
| `[SerializeField]` por Inspector | Solo dentro de la misma capa |

### Servicios registrados en ServiceLocator

| Interfaz | Implementación | Descripción |
|---|---|---|
| `IWantedSystem` | `GameManagerAltsasua` | Nivel de búsqueda del jugador |
| `IEconomyService` | `GameManagerAltsasua` | Dinero y puntuación |
| `ISpawnService` | `GameManagerAltsasua` | Spawn de NPCs y vehículos |
| `ITerrainService` | `ServicioTerreno` | Altura del terreno tile-aware |
| `IRenderBudgetGovernor` | `GobernadorRender` | Radio de mundo dinámico por GPU |
| `IMuestreadorAlturaPrecisa` | `MuestreadorAlturaMosaico` (opt-in) | Altura bit-exacta desde RAW del mosaico |

### Eventos EventBus activos

- `PlayerDeathEvent` — publicado por `ControladorJugador.Morir()`.
- `ChunkLoadedEvent` — publicado por `SistemaZonas` al cargar/descargar zonas OSM.
- `ZoneChangedEvent` — publicado por `SistemaZonas.Update()` al detectar cambio de celda.

---

## Sistema de coordenadas

**Origen = Herriko Plaza** (centro de Alsasua)

| Sistema | Valor |
|---|---|
| UTM 30N ETRS89 | E=567951, N=4749902 |
| Unity offset | OX=1918, OZ=8570 |
| Escala X | 0,93687 (herencia importador OSM, verificada contra MDT05) |
| Cota real de Herriko Plaza | **531,94 m** (`GeoDataAlsasua.COTA_PLAZA`) |

**Conversión:**
```csharp
// Siempre via GeoDataAlsasua.UTMaUnity() / UnityAUTM()
float ux = (float)((E - 567951.0) * 0.93687) + 1918f;
float uz = (float)(N - 4749902.0) + 8570f;
```

> No uses la identidad directa: a 400 m del centro produce ~25 m de error.

**Altura en runtime:**
```csharp
TerrenoGlobal.AlturaMundo(pos)              // tile-aware, recomendado
GeoDataAlsasua.AlturaTerreno(ux, uz)        // alias tile-aware
// NO usar Terrain.activeTerrain.SampleHeight (devuelve un tile arbitrario)
```

---

## Terreno — Mosaico V2

Pipeline de terreno multi-resolución en `Assets/AlsasuaData/terrain_tiles_v2/`:

| Anillo | Zona | Tiles | Resolución | Fuente |
|---|---|---|---|---|
| 0 | Casco urbano (plaza ±1200 m) | 4 × 1200 m @ 2049 px | 0,59 m/px | LIDAR PNOA 0,5 m |
| 1 | Valle (±3600 m) | 32 × 1200 m @ 1025 px | 1,17 m/px | IDENA MDT 2 m 2024 |
| 2 | Sierras (±7200 m) | 12 × 3600 m @ 1025 px | 3,5 m/px | IGN MDT05 |

**Codificación:** RAW uint16, cuanto 1/64 m. `size.y = 1023.984375` en TODOS los tiles → costuras bit-exactas.

**Pipeline:**
```bash
Tools/DescargarMDT_Mosaico.py        # descarga WCS IGN/IDENA + LAZ LIDAR
Tools/GenerarMosaicoTerrenoV2.py     # genera los 48 RAW
Tools/ValidarMosaicoV2.py            # GATE: no tocar Unity sin verde
# En Unity:
Tools → Alsasua → Mundo → 🧩 Construir Mosaico V2
Tools → Alsasua → Mundo → 🔍 Auditor Terreno Mosaico
```

---

## Scripts clave

### Terreno
| Script | Descripción |
|---|---|
| `GeneradorTerrenoUltraPreciso.cs` | Heightmap LIDAR, resample bicúbico, validación RMSE |
| `SistemaTerreno.cs` | Splatmap 8 biomas |
| `AplicadorOrtofoto.cs` | Proyección 72 tiles de ortofoto 25 cm/px |
| `AlsasuaTreeStreamer.cs` | Streaming de árboles LIDAR por especie |
| `GeneradorRiosYPuentes.cs` | Excavación de ríos + shader agua + puentes |
| `GeoDataAlsasua.cs` | **Fuente única de verdad** de coordenadas y constantes geográficas |

### Edificios
| Script | Descripción |
|---|---|
| `SistemaEdificiosAAA.cs` | 12 arquetipos vascos |
| `GeneradorFachadasAAA.cs` | Fachadas modulares PBR |
| `GeneradorTejadosAAA.cs` | Tejados con geometría real |
| `FusionadorEdificiosUltra.cs` | Fusión de 11 fuentes de datos (LIDAR, OSM, Catastro…) |

### Gameplay
| Script | Descripción |
|---|---|
| `GameManagerAltsasua.cs` | Director de juego, servicios IWanted/IEconomy/ISpawn |
| `AltsasuCore.cs` | Singleton central, eventos globales |
| `ControladorJugador.cs` | Movimiento, cámara tercera persona |
| `NPCBase.cs` | Base de NPCs con FSM |
| `SistemaMisiones.cs` | Cadena M00–M12 |
| `SistemaManifestacion.cs` | Zonas y facciones de la manifestación |

### Render y rendimiento
| Script | Descripción |
|---|---|
| `SistemaVolumenHDRP.cs` | Ciclo día/noche, SSAO/SSR/Bloom/DoF/Fog |
| `GobernadorRender.cs` | Radio de mundo dinámico según presupuesto GPU |
| `StreamerMundoEstatico.cs` | 3 bandas de LOD para edificios y props |
| `CesiumFondoLejano.cs` | Fondo Cesium: solo anillo >7 km, throttle por GPU |
| `SceneBootstrapper.cs` | Arrancador secuencial: terreno → mundo cercano → jugador |

---

## Datos GIS disponibles

| Archivo | Resolución | Contenido |
|---|---|---|
| `terrain_tiles_v2/` | 0,59–3,5 m/px | Mosaico V2 (fuente actual del terreno) |
| `ortofoto_alsasua_REAL.png` | 25 cm/px | Ortofoto PNOA completa |
| `orto_tiles_meta.json` | — | 72 tiles JPEG con bbox UTM exacto |
| `lidar_trees.json` | puntos XYZ | 2.956+ árboles con coordenadas reales |
| `bosques.geojson` | polígonos | Masas forestales reales |
| `rios_ejes.geojson` | ejes | Río Arakil y afluentes |
| `lidar_buildings.json` | <0,1 m | Alturas reales de edificios (FUENTE PRIMARIA) |
| `buildings_osm_rico.json` | — | 1.030 edificios con 20 tags OSM |
| `catastro_edificios.json` | cm | Footprints de Catastro, año, uso |

---

## Herramientas Python (`Tools/`)

| Script | Función |
|---|---|
| `DescargarMDT_Mosaico.py` | Descarga WCS IGN/IDENA + LAZ LIDAR |
| `GenerarMosaicoTerrenoV2.py` | Genera los 48 tiles RAW del mosaico |
| `ValidarMosaicoV2.py` | Gate de calidad antes de importar en Unity |
| `DescargarOrtofotoFondo.py` | Descarga ortofoto PNOA por bbox |
| `GenerarOrtofotoDrape.py` | Genera el drape UV de ortofoto sobre el terreno |
| `DescargarCapasGIS.py` | Capas IDENA WFS + OSM Overpass para el visor de editor |
| `ReproyectarEdificiosCanonico.py` | Convierte footprints de Catastro a UTM 30N |
| `blender_lod_pipeline.py` | Pipeline de LOD automático para assets FBX |

---

## Estructura del proyecto

```
Assets/
├── #Scenes/                  # Escenas Unity (Alsasua_Main)
├── AlsasuaData/              # Datos GIS: heightmaps, ortofoto, GeoJSON, JSON
│   └── terrain_tiles_v2/     # 48 tiles RAW del mosaico V2
├── Models/Buildings_Extracted/ # Kit modular: tejados, carpintería, chimeneas
├── Scripts/
│   ├── Core/                 # ServiceLocator, EventBus, GeoDataAlsasua*, Jobs
│   ├── Runtime/              # Terreno, jugador, árboles, GeoDataAlsasua, misiones
│   ├── Modules/              # Música, impostores, interiores, IA GOAP
│   ├── Systems/              # Edificios, gobernador GPU, streamer, Cesium
│   ├── Editor/               # Wizards, importadores, pipelines GIS en Unity
│   ├── _Deprecated~/         # Código obsoleto (Unity no compila carpetas ~)
│   └── _RecuperadosMain~/    # Scripts rescatados de ramas antiguas (no compila)
├── Textures_AAA/             # PBR: fachadas, tejados, naturaleza, suelo, metal
├── Settings/                 # Perfiles HDRP, render pipeline asset
└── _ExtractedAssets/         # FBX importados: edificios lisbon, props urbanos

DatosGIS/                     # Fuentes maestras descargadas (regenerable, no en repo)
Docs/                         # Arquitectura, auditoría, planes, narrativa
Tools/                        # Scripts Python y Blender para el pipeline GIS
```

> **Nota:** Los archivos `.raw` de heightmap no usan Git LFS (historia limpia). Los `DatosGIS/` son regenerables con los scripts de `Tools/` y no están en el repositorio.

---

## Convenciones de código

- Singletons con patrón `Instance` null-guarded en `Awake`.
- Sin `FindObjectOfType` fuera de `Awake`/`Start`.
- Sin `new List/HashSet` en `Update` — usar buffers reutilizables.
- Sin string concat en `Update` — usar `StringBuilder` o precalcular.
- Corrutinas: siempre guardar referencia y cancelar en `OnDestroy`.
- Jobs Burst para operaciones masivas en arrays.
- GPU Instancing activado en todos los materiales (`enableInstancing = true`).
- Logging centralizado vía `AlsasuaLogger.Info/Warn/Error("Tag", "mensaje")`.

---

## Documentación técnica

| Documento | Contenido |
|---|---|
| `Docs/informe_auditoria.md` | Auditoría de código 2026-06 |
| `Docs/grafo_dependencias.html` | Grafo interactivo de clases y dependencias |
| `Docs/arquitectura_orquestador.md` | Director de simulación y ticks por frecuencia |
| `Docs/arquitectura_mosaico_v3.md` | Diseño del Mosaico V3 (clipmap GPU, 3 draw calls) |
| `Docs/arquitectura_omnigrid.md` | Partición espacial unificada (UnifiedSpatialGrid) |
| `Docs/plan_render_aaa.md` | Plan de render AAA por fases (HorneadorCiudad, impostores…) |
| `Docs/arquitectura_costuras_terreno.md` | Costuras bit-exactas entre tiles de terreno |

---

## Geografía de referencia

- **Alsasua/Altsasu** — cuenca fluvial a ~530 m de altitud en la Sakana (Navarra)
- **Sierra de Aralar** — sur, ~1.400 m
- **Altzania/Urbasa** — norte, ~1.000 m
- **Río Arakil** — cruza el valle de este a oeste
- **Arquitectura vasca tradicional** — arenisca rojiza, balcones de forja, teja árabe terracota/pizarra

---

## Licencia

Código fuente bajo licencia **MIT**. Los datos GIS (IGN, IDENA, OSM, Catastro, PNOA) están sujetos a sus licencias de origen respectivas; consulta sus fuentes antes de redistribuir.

---

*Proyecto en desarrollo activo. Rama única: `main`.*
