# Informe de auditoría — Altsasu Manifa
**Fecha:** 2026-06-10 · **Alcance:** Assets/Scripts (210 archivos, 429 clases, ~65.000 líneas)

> **Actualización (2ª y 3ª pasada, mismo día):** los duplicados de la sección 2 fueron
> resueltos y los huérfanos de la sección 3 evaluados e integrados/deprecados.
> Ver sección 7 (nueva) al final.

## 1. Cambios aplicados en esta pasada

| Cambio | Motivo | Riesgo |
|---|---|---|
| `Runtime/EventManager.cs` → movido a `_Deprecated~/` | Fachada estática de 241 líneas con **cero referencias** en todo el proyecto. Era "documentación viva" que nadie consumía y generaba acoplamiento WORLD→GAMEPLAY. | Nulo (clase estática, no adjuntable a escenas) |
| `Systems/SistemaWater.cs` → movido a `_Deprecated~/` | Duplicado funcional exacto de `SistemaAguaRio.cs` (ambos conducen una HDRP WaterSurface según el clima, ambos tras `ALSASUA_WATER`). Se conserva `SistemaAguaRio` (mejor documentado, integrado con GeneradorRiosYPuentes). Cero referencias en código, prefabs y escenas (GUID verificado). | Nulo |
| `Runtime/SistemasSimulacion.cs`: eliminadas `SistemaTraficoLegacy`, `SistemaFaunaLegacy`, `SistemaMultitudLegacy` (~330 líneas) | Sustituidas hace tiempo por `SistemaTrafico.cs`, `SistemaFauna.cs` y `SistemaSpawnCiviles.cs`. Cero referencias. Al no coincidir con el nombre del archivo, no pueden estar adjuntas en ninguna escena. | Nulo |

Se conservan en ese archivo: `SistemaVegetacion`, `SistemaAtmosfera` y `SistemaParanoia` (todas con referencias activas).

La carpeta `_Deprecated~/` no la compila Unity (convención `~`, igual que `_RecuperadosMain~`). Para recuperar algo, basta moverlo de vuelta.

## 2. Duplicados detectados — requieren decisión (NO tocados)

| Par | Situación | Recomendación |
|---|---|---|
| `MobiliarioUrbano.cs` (565 loc) vs `SistemaMobiliarioUrbano.cs` (243 loc) | **Ambos en uso**: el primero lo usa `ConductorMundo`, el segundo `SceneBootstrapper`. Dos sistemas colocando street furniture a la vez → posible mobiliario duplicado en runtime. | Consolidar en uno. `MobiliarioUrbano` tiene pooling+LOD (mejor base técnica); `SistemaMobiliarioUrbano` tiene el conocimiento de zonas (Herriko Plaza, Nafarroa Kalea…). Fusionar zonas → pooling. |
| `DiagnosticoArranque.cs` (330 loc) vs `SistemaDiagnostico.cs` (223 loc) | Dos herramientas de diagnóstico de arranque casi idénticas. | Quedarse con `DiagnosticoArranque` (7 secciones, más completo) y deprecar el otro. |
| `SistemaNeblina.cs` vs `SistemaClimaEfectos.cs` | Solapamiento parcial (efectos de clima/niebla). | Revisar si Neblina puede ser un módulo de ClimaEfectos. |
| `HUDSistemas.cs` (288 loc) | Huérfano. Overlay de debug F3, se adjunta a mano. | Mantener (herramienta dev), pero documentarlo en CLAUDE.md. |
| `OptimizadorTerreno` vs `SistemaOptimizacion` | **Falso duplicado**: el primero gestiona la malla OBJ de CloudCompare, el segundo es quality scaling global. | Nada. Renombrar el primero a `OptimizadorMallaOBJ` aclararía. |

## 3. Huérfanos restantes (cero referencias en código)
Son MonoBehaviour/SO con nombre = archivo, así que **podrían** estar adjuntos en `Alsasua_Main.unity` (no verificable: las escenas están en LFS como punteros). No borrar a ciegas; comprobar en el editor con `LimpiarMissingScripts` como red de seguridad:

`AplicadorTexturasReales`, `CatalogoVivo`, `FaccionDefinition`, `SistemaAPV`, `SistemaAPVScenarios`, `ProcesadorMapillaryObjetos`, `SistemaDirectorConsumos`, `SistemaRotulosZona`, `SistemaShaderGlobals`, `TuningFisica`, `GeneradorTejadosAAA`*, `PropsDestruccionManifestacion`*, `SistemaAguaRio`*, `SistemaOcclusion`*, `DiagnosticoArranque`*

\* probablemente legítimos: se adjuntan manualmente o se instancian por editor-tools/AddComponent.

## 4. Convenciones — estado

- **FindObjectOfType fuera de Awake/Start** (22 archivos): la mayoría son llamadas puntuales lazy-init o de herramientas de diagnóstico (`DiagnosticoArranque` ×14, `SceneBootstrapper` ×16 — aceptable: corren una vez al arrancar). Ninguna detectada dentro de `Update()`. Prioridad baja. Los candidatos a cachear: `SistemaClima→SistemaAtmosfera`, `PoliciaForalIA→SistemaAtmosfera` (si se llaman con frecuencia).
- **Allocations/concat en Update**: solo 1 caso (`SistemaTutorial`, concat una vez al cerrar pista, no por frame). OK.
- **Corrutinas sin OnDestroy** (24 archivos): Unity ya detiene corrutinas al destruir el GameObject, así que no hay fuga directa. El riesgo real son corrutinas en bucle que tocan singletons externos: revisar `GeneradorRiosYPuentes` (14), `JuegoManifestacion` (8), `ConversorMaterialesHDRP` (6) si aparecen NullReference al cambiar de escena.

## 5. Estructura — observaciones de arquitectura

1. **CLAUDE.md desactualizado**: dice 119 scripts/37k líneas; reales: 210 scripts/65k líneas. Actualizado.
2. **`Core/` contiene gameplay**: `DirectorMundo`, `SistemaApoyoPopular`, `SistemaDestruccion`, `HUDManifestacion`, `MenuPausa` viven en `Core/` pero pertenecen a GAMEPLAY/UI según la propia tabla de capas. Mover archivos no rompe nada en Unity (los GUID se conservan) — recomendado para que la carpeta refleje la capa.
3. **`AltsasuCore` conoce demasiado**: referencia directamente HUDCanvas, AudioManager, SistemaMisiones, etc. (capas superiores). Para nuevas integraciones, usar EventBus en vez de añadir más referencias ahí.
4. **Cuatro carpetas runtime** (`Core/Modules/Runtime/Systems`) sin criterio claro de pertenencia — `Systems/` y `Runtime/` se solapan. A futuro: una carpeta por capa (Core/World/Entities/Gameplay/UI) alineada con la tabla del CLAUDE.md.

## 6. Grafo de dependencias

`Docs/grafo_dependencias.html` — abrir en navegador. 217 nodos (clases) y 1.194 enlaces:
nodo = clase (tamaño ∝ líneas, color = capa, rombo = evento EventBus); enlace = herencia / ServiceLocator / EventBus publica-suscribe / referencia directa.
Buscador, filtros por capa y tipo, clic = panel de dependencias, doble clic = aislar vecindario. La capa EDITOR arranca oculta.

Regenerable: el grafo se construyó parseando los `.cs`; pedir "regenera el grafo" tras cambios grandes.

## 7. Segunda y tercera pasada — duplicados resueltos e integraciones

### Duplicados resueltos (sin pérdida de funcionalidad)

| Acción | Detalle |
|---|---|
| **Fusión mobiliario** | `SistemaMobiliarioUrbano` → fusionado en `MobiliarioUrbano` (deprecado el primero). El unificado tiene: pooling+LOD de calles **y** props por zonas (plaza/kalea/polígono/ribera), quality tier en ambas rutas, reacción a `DirectorMundo.Disturbio` (volcar props) y registro de farolas en `SistemaVidaNocturna`. La espera ciega de 20s se cambió por espera real a `SistemaAssets`. |
| **Fusión diagnósticos** | `DiagnosticoArranque` → fusionado en `SistemaDiagnostico` (que es el cableado en AltsasuCore/CreadorEscena). Ahora: 7 secciones, chequeo de datos LIDAR/JSON con detección de punteros LFS, lista de "opciones de mejora", `repetirCada`, panel F1. Los tipos de capas superiores (SistemaChunks, InterioresAAA) se detectan por nombre — Runtime no puede referenciar Systems/Modules. |
| **Bug corregido** | `SistemaClimaEfectos.AjustarVelocidadTren` multiplicaba la velocidad ya reducida cada 3s → el tren decaía exponencialmente hacia 0 durante la nieve. Ahora captura la velocidad nominal una vez. |
| **Optimización** | `SistemaClimaExtension.EstadoActual` cacheaba… no: hacía `FindFirstObjectByType` en CADA llamada periódica (Neblina cada 2s + ClimaEfectos cada 3s). Ahora cachea la instancia. |
| **Renombrado** | `OptimizadorTerreno` → `OptimizadorMallaOBJ` (su función real: malla OBJ CloudCompare; evita confusión con `SistemaOptimizacion`). GUID conservado. |
| **No eran duplicados** | `SistemaNeblina` (niebla volumétrica del río) y `SistemaClimaEfectos` (puente clima→humo/tren): responsabilidades distintas, se mantienen ambos. |

### Misión inicial (M00)

- `Runtime/MisionInicial.cs` — `Mision_Inicial` "Esnatu, Altsasu": teleporta al portal, enseña controles (SistemaTutorial), lleva a Herriko Plaza, reunión con el grupo (+apoyo popular). Encadena con `Mision_RobarCoche` → cadena completa M00→M12.
- `SistemaMisiones` arranca ahora en M00 (flag Inspector `saltarIntro` para empezar en M01).
- `Editor/CreadorEscenaMisionInicial.cs` — menú `Tools/Alsasua/Escena/🎬 Crear Escena Misión Inicial` genera `Assets/#Scenes/Mision_Inicial.unity` (bootstrapper + GameManager + AltsasuCore + Misiones) y la añade a Build Settings.

### Huérfanos evaluados

**Integrados al juego** (añadidos a `SceneBootstrapper`, sección 8 — todos defensivos):
`SistemaDirectorConsumos` (sin él los eventos del Director no llegaban a nada), `PropsDestruccionManifestacion` (escombros/barricadas por intensidad — central para la temática), `SistemaRotulosZona`, `SistemaShaderGlobals` (arregla el look mojado/nocturno PBR), `TuningFisica`, `SistemaOcclusion`, `SistemaAguaRio`, `SistemaNeblina`, `SistemaClimaEfectos`, `SistemaAPVScenarios`, `AplicadorTexturasReales`, `HUDSistemas` (F3 debug).

**Integración profunda:** `GeneradorTejadosAAA` movido de Systems/ a Runtime/ (sus deps son solo Core/Runtime) y cableado como **ruta 2** en `GeneradorGeometriaPrecisa.GenerarTejado` (prioridad: LIDAR > kit AAA > procedural antiguo). Añadido al bootstrapper antes de GeometriaPrecisa. Existía completo (579 loc) pero nunca se llamaba.

**Deprecados** (a `_Deprecated~/`): `SistemaAPV` (duplicado de SistemaAPVScenarios), `CatalogoVivo` y `FaccionDefinition` (ScriptableObjects sin ningún consumidor ni .asset instanciado).

**Mantenidos sin integrar (con motivo):**
- `ProcesadorMapillaryObjetos` — coloca props desde detecciones ML; solaparía con el MobiliarioUrbano unificado (props duplicados). Alternativa opcional: activarlo a mano y bajar densidades del mobiliario.
- `AutoImportadorIncoming` — herramienta de menú (`Altsasu/Assets/Importar desde _Incoming`), correcta como está.
- `ArchitectureIndex` — documentación viva, sin código ejecutable.
- `MisionSec_Fotografo` — falso huérfano: lo lanza `GestorMisionesSecundarias` desde el mismo archivo.

### Nota de infraestructura
Las asambleas mandan: `Core ← Runtime/Modules ← Systems ← Editor`. Cualquier código en Runtime que necesite un tipo de Systems/Modules debe usar eventos, interfaces en Core, o detección por nombre (`SistemaDiagnostico.ExisteComponente`).
