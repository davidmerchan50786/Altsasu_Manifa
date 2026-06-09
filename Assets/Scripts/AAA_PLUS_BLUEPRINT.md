# Blueprint AAA+++ — Alsasua Simulator
**Unity 6000.3.10f1 · HDRP 17 · Objetivo: calidad de referencia (GTA/RDR-tier) en una ciudad real (Alsasua/Altsasu, Navarra)**

> Este documento es el **rediseño**: define el listón AAA+++, audita lo que hay, y traza un roadmap concreto con técnicas Unity específicas y puntos de integración con los sistemas existentes. Está pensado para ejecutarse por fases sin romper el build actual (que ya compila). Cada propuesta indica **[NUEVO]** (sistema nuevo autocontenido), **[EXT]** (extiende un sistema existente) o **[CFG]** (configuración/assets, sin código).

---

## 0. Definición del listón "AAA+++"

| Eje | Mínimo AAA | AAA+++ (objetivo) |
|-----|-----------|-------------------|
| Frame budget | 60 fps estable | 60 fps con techo de 8.0 ms CPU + 8.0 ms GPU, dynamic res como red de seguridad |
| Render | PBR + post | GI dinámica (APV) + volumétricos + reflexiones híbridas + water system |
| Mundo | NPCs con rutas | rutinas de agenda 24h, multitudes boids, tráfico que respeta semáforos, reacción a eventos |
| Game feel | input responsivo | anticipación/seguimiento de cámara, hit-stop, IK de pies/manos, feedback háptico |
| Audio | 3D básico | mezcla por capas, reverb por zona, oclusión, música adaptativa por tensión |
| Estabilidad | compila | 0 GC spikes en gameplay, streaming sin hitches, tests de humo en CI |

---

## 1. Estado actual (auditoría)

**Fortalezas ya presentes** (no tocar, construir encima):
- Pipeline gráfico por capas con `[DefaultExecutionOrder]` determinista y globals de shader (`_GlobalWetness`, `_GlobalNightLevel`, `_GlobalSnowLevel`…).
- Desacoplamiento limpio: `EventBus` + `ServiceLocator` (capa `Core/` ahora en su propio asmdef).
- Job System + Burst ya en uso (`Jobs/`: FBM terreno, boids, frustum cull, filtrado de árboles).
- Streaming de zonas (`SistemaZonas`/`SistemaChunks`) con histéresis y fade.
- Clima, día/noche (`SistemaVolumenHDRP`), charcos, nieve, fachadas dinámicas, partículas ambiente.
- IA: `PoliciaForalIA`, `SistemaManifestacion` (boids), `SistemaAgendaNPC`, `SistemaDeteccionIA` (raycast batch).
- Post-proceso dual: `SistemaVolumenHDRP` (base) + `SistemaPolish` + `SistemaPostProcesoAAA` (reactivo).

**Carencias para AAA+++** (lo que falta):
- Sin GI dinámica: la iluminación indirecta no responde a hora/clima → escenas planas a media tarde.
- Reflexiones solo por probes periódicos; sin SSR híbrido ni RT opcional.
- Sin water system real (ríos/charcos son quads animados).
- Sin frame-budget director: la calidad es estática, no se adapta a la carga.
- Sin impostors/HLOD para la silueta urbana lejana.
- Game feel: cámara sin anticipación, sin IK de contacto con suelo, feedback de impacto limitado.
- Audio sin oclusión ni música adaptativa.
- Sin capa de tests de humo automatizada más allá de `DiagnosticoGrafico`.

---

## 2. Pilar 1 — Render / HDRP

### 2.1 Adaptive Probe Volumes (APV) — GI dinámica **[CFG + código listo]**
El mayor salto visual. HDRP 17 trae APV: probes de GI por volumen, bakeables por "lighting scenario" (día/atardecer/noche).
- **Código entregado:** `SistemaAPVScenarios` mezcla en runtime los scenarios "Day"↔"Night" con `ProbeReferenceVolume.instance.BlendLightingScenario()`, leyendo el global `_GlobalNightLevel` que ya publica `SistemaVolumenHDRP` (totalmente desacoplado, sin editar nada existente). **Envuelto en `#if ALSASUA_APV`** → por defecto es un no-op que compila; se activa añadiendo el símbolo tras bakear.
- **Pendiente (solo editor):** activar APV en el HDRP Asset, colocar un Adaptive Probe Volume, crear un Baking Set y bakear 3 lighting scenarios, y añadir el define `ALSASUA_APV`. Pasos exactos en la cabecera de `SistemaAPVScenarios.cs`.
- **Impacto:** rebotes de luz coherentes con la hora; interiores con bounce real. Coste: bake offline, runtime ~0.3 ms.

### 2.2 Reflexiones híbridas **[EXT]**
- Mantener `SistemaReflexiones` (probes) como fallback; añadir **SSR** en el Volume (ya hay `_GlobalWetnessSSR`).
- Opción RT: `ScreenSpaceReflection` con `RayTracing = true` detrás de un toggle de calidad (lo gobierna el Director de Calidad, §4.4).

### 2.3 Volumétricos y atmósfera **[EXT]**
- `Fog` con volumetric lighting + `Local Volumetric Fog` en zonas (niebla de río Burunda al amanecer, humo del polígono).
- God-rays por `Volumetric Clouds` + sol; ya hay hora dorada en `SistemaVolumenHDRP` → enganchar densidad de niebla al clima (`SistemaClima`).

### 2.4 Water System **[CFG + código listo]**
- HDRP Water System para el río Burunda y la regata: caustics, foam, deformación.
- **Código entregado:** `SistemaAguaRio` conduce una `WaterSurface` reaccionando al clima (`SistemaClima`): en tormenta sube viento/oleaje y espuma, en calma queda tersa. Reutiliza la geometría de cauce de `GeneradorRiosYPuentes` (no la duplica). **Envuelto en `#if ALSASUA_WATER`** → no-op seguro hasta activarlo; verificado que con el símbolo OFF no hay ninguna referencia a la API HDRP Water.
- **Pendiente (solo editor):** activar Water en el HDRP Asset, crear la WaterSurface (River) sobre el Burunda, asignarla y añadir el define `ALSASUA_WATER`. Pasos en la cabecera de `SistemaAguaRio.cs`.
- `SistemaCharcos` se mantiene para charcos pequeños; el río pasa a Water Surface real.

### 2.5 Materiales y detalle **[CFG]**
- Migrar fachadas a materiales con **detail maps** + **POM** (parallax) en sillería/mampostería vasca.
- **Decal layers** para suciedad/musgo dirigido (ya hay `SistemaDecalesHDRP`); separar por `DecalLayerMask`.

### 2.6 Post-proceso cinético **[EXT SistemaPostProcesoAAA]**
- Lens flare procedural (sol), bloom anamórfico opcional, chromatic aberration solo en daño/velocidad (ya parcialmente).
- Color grading por LUT por zona (industrial frío, casco histórico cálido) — enganchar a `ZoneChangedEvent`.

---

## 3. Pilar 2 — Mundo y simulación

### 3.1 Densidad y LOD de agentes **[EXT]**
- Presupuesto de agentes por anillo de distancia: full-sim cerca, "billboard crowd" lejos (boids ya existen en `SistemaManifestacion`).
- `SistemaAgendaNPC`: rutinas 24h (casa→trabajo→bar→casa) con horarios reales; los NPC lejanos se simulan "en bajo coste" (LOD de IA: solo posición, sin animación).

### 3.2 Tráfico vivo **[EXT VehiculoNPC]**
- Grafo de calles desde `roads_unity.json` (ya cargado) → semáforos, cesión de paso, adelantamiento, claxon.
- Spawning por densidad horaria (hora punta vs noche). Reacción a la policía y a barricadas.

### 3.3 Eventos dinámicos y director **[implementado]**
- **`DirectorMundo`** (AI Director estilo L4D): calcula una intensidad 0..1 desde `IWantedSystem.NivelBusqueda` + `SistemaApoyoPopular` (apoyo bajo/paranoia alta = más tensión), sigue un pacing calma→pico→relajación con cooldowns, y **difunde** eventos (`MercadoDia`, `PatrullaRefuerzo`, `ControlPolicial`, `Disturbio`, `Redada`) por `DirectorMundo.OnEvento`. No spawnea nada — los consumidores (policía, manifestación, audio) se suscriben → cero acoplamiento. Expone `IntensidadActual`/`EstadoActual`. Pendiente (opcional): que `PoliciaForalIA`/`SistemaManifestacion` se suscriban a `OnEvento` para materializar cada evento.

### 3.4 Clima y estaciones **[EXT SistemaClima]**
- Estaciones que afectan vegetación (`SistemaVientoVegetacion`, árboles), nieve persistente en invierno (ya hay `SistemaNevadasTerreno`), charcos tras lluvia.
- Transiciones meteorológicas suaves con frentes (no flip instantáneo).

### 3.5 Vida urbana creíble **[EXT]**
- Ventanas con luz por agenda (ya hay `_GlobalNightLevel`); persianas, ropa tendida, gatos, palomas (reuso de `SistemaAmbientParticulas` + props).
- Audio ambiental por zona y hora (ver §5).

---

## 4. Pilar 3 — Rendimiento / streaming

### 4.1 Frame budget y profiling **[EXT]**
- **`SistemaOptimizacion`** ya es un director adaptativo (mide FPS, ajusta lodBias/shadowDistance, hace culling por distancia con Burst). En esta sesión se extendió para **publicar `_GlobalQualityTier` (0..3)** como global de shader — la señal que el resto del pipeline consulta para modular coste. Ver §8. *(Se descartó crear un `SistemaDirectorCalidad` nuevo: habría duplicado y peleado con éste por `QualitySettings`.)*

### 4.2 Job System + Burst a fondo **[EXT Jobs/]**
- Mover a jobs: actualización de boids (ya), culling de props, sincronización de meshes de rueda, batch de raycasts de IA (ya en `SistemaDeteccionIA`).
- `IJobParallelForTransform` para los huecos de NPCs/props lejanos.

### 4.3 GPU instancing, LOD e impostors **[EXT]**
- `SistemaDetalleTerreno` ya hace ground cover instanciado. Añadir **impostors** (billboards 8-ángulos) para edificios y árboles del anillo lejano → silueta urbana sin coste de malla.
- HLOD: fusionar manzanas lejanas en un solo mesh+atlas (`FusionadorEdificiosUltra` ya fusiona; extender a HLOD por anillo).

### 4.4 Occlusion y streaming **[EXT SistemaChunks/Zonas]**
- Occlusion culling horneado para el casco histórico (calles estrechas = mucho occludee). **Herramienta entregada:** `Editor/UtilOcclusionEstatica.cs` añade el menú *Alsasua ▸ Occlusion ▸ Marcar geometría estática*, que marca los contenedores de mundo (edificios, suelo, muros, túneles) como Occluder+Occludee+Batching Static de una pasada (con Undo). El bake en sí es manual (Window ▸ Rendering ▸ Occlusion Culling ▸ Bake) — pasos en la cabecera del script.
- Migrar chunks pesados a **Addressables** con carga async (el propio `SistemaChunks` ya documenta esta vía).
- Presupuesto de hitch: nunca instanciar >N objetos/frame (time-slicing ya usado en varios sistemas).

### 4.5 Memoria y GC **[EXT]**
- Auditar allocs en Update/FixedUpdate (ya se han cacheado varios: `MaterialPropertyBlock`, arrays de ruedas, LayerMasks). Objetivo: 0 GC en gameplay estable.
- Pools para todo lo efímero (decals, partículas, proyectiles, NPCs) — varios pools ya existen.

---

## 5. Pilar 4 — Game feel y audio

### 5.1 Cámara cinética **[EXT ControladorJugador]**
- Ya hay camera bob + spring arm. **Implementado:** **FOV kick** (+7°) y **pullback** (+0.45 m) al esprintar, suavizados (`_sprintFeel`, attack 0.35 s), activos solo corriendo en suelo sin apuntar y con velocidad real → sensación de velocidad sin coste. Campos en el header "CÁMARA CINÉTICA".
- **Anticipación (implementada):** la mirada de la cámara se adelanta hacia el movimiento horizontal (`lookAheadFactor`, clamp 0.8 m, suavizado), aplicada solo al objetivo de mirada (no al centro de órbita → no se siente a la deriva) y anulada al apuntar. Pendiente: framing assist al apuntar. Screen-shake por trauma ya en `SistemaPolish`.

### 5.2 Animación procedural / IK
- **Jugador humano automático (implementado):** el cuerpo procedural de `ControladorJugador` se reescribió de **cubos a humanoide de cápsulas** (torso/extremidades cápsula, articulaciones esfera, proporciones humanas) con **animación procedural de caminar** (piernas y brazos en contrafase balanceando según velocidad, cadencia mayor al correr). Es automático, sin assets ni rig — soluciona el "no bloques" sin depender del editor.
- **Foot IK (implementado):** `SistemaFootIK` planta los pies en el terreno (raycast por pie, alineado a la normal). `ControladorJugador` lo **añade automáticamente** al personaje Mixamo cuando hay Animator; se auto-desactiva si el avatar no es Humanoid (entonces aplica la animación procedural de cápsulas). Requisito editor: marcar "IK Pass" en la capa del Animator Controller.
- Pendiente: **Look-at IK** de cabeza/torso, **Hand IK** al empuñar arma/volante. Ragdoll (`SistemaRagdoll`) con blend desde animación para impactos.

> Para un personaje totalmente animado (locomoción Mixamo + Foot IK), asigna un FBX humanoide de mixamo.com en `prefabPersonaje` (pasos en la cabecera de `ControladorJugador.cs`). Sin él, el humanoide procedural de cápsulas es el fallback automático.

### 5.3 Feedback de impacto / juiciness **[EXT]**
- Hit-stop (ya en `SistemaPolish.HitStop`), partículas y decals de impacto por material (ya en `SistemaImpactos`), rumble de gamepad, flash de UI.
- Time-dilation breve en entradas a vehículo (ya hay `SlowMoEntradaVehiculo`) y en derribos.

### 5.4 Audio AAA **[NUEVO/EXT AudioManager + SistemaReverbZonas]**
- **Oclusión**: raycast oído→fuente; atenúa y filtra (low-pass) tras paredes.
- **Reverb por zona** ya existe; añadir capas de ambiente (tráfico lejano, pájaros, viento por altura).
- **Música adaptativa** ✅ *(implementado: `SistemaMusicaAdaptativa`)*: 3 capas (calma/tensión/persecución) que cruzan según `IWantedSystem.NivelBusqueda`, con crossfade en bandas suaves. Autocontenido, respeta `SistemaOpciones.VolMusica`, degrada sin clips. Expone `TensionActual` (0..1) para que otros sistemas reaccionen (p. ej. grading). Pendiente: stingers en escaladas.
- Mezcla con `AudioMixer` snapshots por estado (calma/persecución/interior).

### 5.5 UI/UX **[EXT HUDCanvas]**
- HUD diegético/minimalista, marcadores suaves, minimapa ya throttled. Transiciones con easing, sin pops.

---

## 6. Cross-cutting

- **Capas de ensamblado:** `Core/` ya aislado (`Alsasua.Core.asmdef`). Siguiente paso: `Alsasua.Gameplay`, `Alsasua.World`, `Alsasua.Render` con dependencias explícitas → builds incrementales más rápidos y dependencias controladas.
- **Tests:** ampliar `DiagnosticoGrafico` a un `SmokeTestRunner` que arranque la escena, espere estabilización y valide invariantes (fps, nulos, rangos). Ejecutable en batchmode para CI.
- **Telemetría:** logger de frame-time percentiles (p50/p95/p99) para detectar hitches.

---

## 7. Roadmap priorizado (impacto × esfuerzo)

| Fase | Entregable | Pilar | Impacto | Esfuerzo | Riesgo |
|------|-----------|-------|---------|----------|--------|
| **0 (hecha)** | Build compila + APIs cableadas + asmdef Core | — | — | — | — |
| **1** | `_GlobalQualityTier` (ext. `SistemaOptimizacion`) | Perf | Alto | Bajo | Bajo ✅ implementado |
| **2** | APV scenario blending (`SistemaAPVScenarios`) | Render | Muy alto | Medio | Código ✅ / bake pendiente (editor) |
| **1c** | Cámara cinética: FOV kick + pullback al esprintar | Feel | Medio | Bajo | Bajo ✅ implementado |
| **3** | Humanoide procedural + walk + Foot IK (`SistemaFootIK`) | Feel | Alto | Medio | ✅ implementado (Foot IK auto-noop sin rig) |
| **1b** | Música adaptativa por tensión (`SistemaMusicaAdaptativa`) | Audio | Alto | Bajo | Bajo ✅ implementado |
| **4** | Oclusión de audio low-pass (`SistemaReverbZonas` ext.) | Audio | Alto | Medio | ✅ AudioLowPassFilter dinámico, cutoff 800 Hz tras pared |
| 5 | Impostors + HLOD anillo lejano | Perf/Render | Alto | Alto | Medio |
| **6** | `DirectorMundo` + `SistemaDirectorConsumos` | Mundo | Alto | Alto | ✅ director + consumidores implementados |
| 7 | Water System (río Burunda) | Render | Medio | Medio | Bajo (CFG) |
| 8 | Tráfico con semáforos/cesión | Mundo | Medio | Alto | Medio |
| 9 | Volumétricos + niebla de río | Render | Medio | Bajo | Bajo |
| 10 | Capas asmdef Gameplay/World/Render | Cross | Medio | Alto | Medio |

**Regla:** cada fase entra detrás de un toggle de calidad y con un test en `DiagnosticoGrafico`. Nada se mergea sin verificar que la escena sigue arrancando.

---

## 8. Cornerstone implementado esta sesión — `_GlobalQualityTier`

**Decisión de integración (clave):** `SistemaOptimizacion` ya era un director adaptativo. Crear un sistema nuevo habría duplicado lógica y dos sistemas pelearían por `QualitySettings.lodBias`/`shadowDistance`. En su lugar, se **extendió el existente**.

Cambios aplicados (`SistemaOptimizacion`):
- Nuevo campo `_tierCalidad` (0 Ultra … 3 Performance) que ahora **sí se mantiene** (el viejo `_nivelCalidad` quedaba muerto tras el arranque).
- `SubirNivelCalidad`/`BajarNivelCalidad` mueven el tier junto a los ajustes finos de lodBias/shadowDistance, con clamp e histéresis heredada de los umbrales de FPS existentes.
- `FijarTier()` publica `Shader.SetGlobalFloat("_GlobalQualityTier", tier)` solo cuando cambia (sin writes redundantes).
- API pública `SistemaOptimizacion.TierCalidad` (static int) para consumidores en C#.

Primer consumidor cableado (`SistemaAmbientParticulas`): en tier 3 (Performance) las partículas ambiente (puro eye-candy) no se activan → se recupera frame bajo carga.

Verificación (`DiagnosticoGrafico`, sección 10): `SistemaOptimizacion` activo y `_GlobalQualityTier` en rango [0,3].

**Por qué es el cornerstone:** es la red de seguridad de los 60 fps y el *gate* común. Cada mejora pesada del roadmap (APV, SSR/RT, volumétricos, impostors) se activará solo si `_GlobalQualityTier` lo permite — el coste visual nunca podrá romper el frame budget. Patrón a replicar: feature pesada lee el tier, no se acopla al director.
