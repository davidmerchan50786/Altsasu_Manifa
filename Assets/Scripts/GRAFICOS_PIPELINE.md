# Pipeline Gráfico — Alsasua Simulator
**Unity 6000.3.10f1 · HDRP 17 · C# 9**

---

## Visión general

El pipeline gráfico de Alsasua está dividido en capas de responsabilidad única que se ejecutan en orden determinista gracias a `[DefaultExecutionOrder]`. Ningún sistema accede directamente a otro — la comunicación es vía **Shader.SetGlobalFloat** y **eventos estáticos**.

```
Frame timeline (orden de ejecución):
  -80  SistemaVolumenHDRP     — volúmenes HDRP (día/noche/transición) + _GlobalNightLevel
  -70  SistemaTerreno         — splatmap 8 biomas
  -70  SistemaPostProcesoAAA  — post-proceso dinámico por estado (flashes, grading)
  -65  SistemaSueloAAA        — calles, aceras, mobiliario
  -62  SistemaNevadasTerreno  — nieve en splatmap + _GlobalSnowLevel
  -60  SistemaReflexiones     — reflection probes procedurales
  -55  GeneradorRocasProcedurales — GPU instanced rocks
  -55  SistemaDecalesHDRP     — pool de 128 DecalProjectors
  -50  SistemaDetalleTerreno  — ground cover GPU instanced
  -45  SistemaHuellasAsfalto  — pool de 64 decals de neumático
  -40  SistemaVientoVegetacion — WindZone + partículas de hojas
  -35  SistemaCharcos         — charcos + wetness globals
  -35  SistemaAmbientParticulas — 8 zonas de partículas ambiente
   50  SistemaPolish          — post-procesado, DoF, vignette, decales
   90  SistemaFachadasDinamicas — MPB de clima/suciedad/nieve en edificios
  100  SistemaShaderGlobals   — aplica MPB de smoothness a suelos/fachadas
  200  DiagnosticoGrafico     — QA automático (solo Play, 8s delay)
```

---

## Sistemas y responsabilidades

### SistemaVolumenHDRP
**Propietario de:** day/night blend, hora dorada, fog, SSAO, SSR, bloom, TAA, lens dirt, farola flicker, `_GlobalNightLevel`, `_GlobalFocusDist`

| Volumen | Priority | Activo cuando |
|---------|----------|---------------|
| Volume_Dia_HDRP | 10 | siempre |
| Volume_Noche_HDRP | 11 | `_blendNoche > 0` |
| Volume_Transicion_HDRP | 12 | 6-9h y 17-21h |

**API pública:**
```csharp
SistemaVolumenHDRP.SetTormenta(bool);        // HDRI tormenta + fog densa
SistemaVolumenHDRP.SetDoFSniper(bool, float); // DoF francotirador
SistemaVolumenHDRP.SetFocusDistance(float);   // llamado por SistemaPolish cada 0.2s
SistemaVolumenHDRP.SetLensDirt(float);        // 0-1, llamado por SistemaPolish.ExplosionBloom
```

---

### SistemaPolish
**Propietario de:** screen shake, chromatic aberration, vignette unificada, motion blur, DoF auto-focus, sprint blur, explosion bloom/light, rain screen, mouse chromatic, aiming vignette

**Regla de vignette:** Tres contribuyentes (daño, apuntado, lluvia) NO escriben directamente al Volume. Todos actualizan estado interno y `AplicarVignetteUnificada()` hace la escritura una sola vez por frame. Esto previene la race condition anterior donde dos Override() se pisaban.

**API pública:**
```csharp
SistemaPolish.Shake(float intensidad);         // 0-1
SistemaPolish.FlashDano(float intensidad);     // daño recibido
SistemaPolish.HitStop(float duracion);         // freeze frame
SistemaPolish.ExplosionBloom(float intensidad); // bloom + light + lens dirt
SistemaPolish.ExplosionLight(Vector3 posicion); // solo la luz de punto
SistemaPolish.SlowMoEntradaVehiculo();
SistemaPolish.SetSirena(bool);                 // wanted ≥ 2
```

---

### SistemaCharcos
**Propietario de:** `_GlobalWetness`, `_GlobalWetnessSSR`, `_GlobalRippleTime`

Crea un pool de 36 quads-charco posicionados por raycast. Anima escala (ripple rings) y UV offset del material. Lee `SistemaClima.climaActual` para determinar el objetivo de humedad.

**Nota:** `_charcoEscalaBase[]` guarda la escala asignada en `RepartirCharcos`. El loop de ripple usa esta base, no `t.localScale.x`, para evitar drift acumulativo (Bug #1 corregido).

---

### SistemaShaderGlobals
**Propietario de:** `_GlobalWetSmoothness`, `_GlobalEmissiveNight`, MPB de smoothness en suelos/fachadas

Itera los renderers de suelo y fachada cada 3 segundos y aplica `MaterialPropertyBlock` con `_Smoothness` ajustado por `_GlobalWetness`. No instancia materiales — zero alloc.

**Cómo añadir un surface más:**
```csharp
// En BuscarRenderers(), añadir:
foreach (var r in GameObject.Find("MiNuevoObjeto").GetComponentsInChildren<Renderer>())
    _suelos.Add(r);  // o _fachadas.Add(r)
```

---

### SistemaReflexiones
**Propietario de:** ReflectionProbe placement procedural

8 probes colocados en puntos clave de Alsasua (plaza, cuartel, N-1 norte/sur, estación, monte Aralar, barrio norte, polígono). Solo el probe más cercano al jugador está en modo real-time (renderiza cada ciclo). El resto renderiza periódicamente con `intervaloBake`.

**Añadir un probe nuevo:**
```csharp
AnadirProbe(parent, "NombreZona",
    new Vector3(unityX, alturaTerreno + 3f, unityZ),
    size: new Vector3(ancho, alto, profundo),
    esInterior: false,
    realtimeInterval: 15f);
```

---

### SistemaDecalesHDRP
**Propietario de:** pool de 128 DecalProjectors

5 tipos de decal: BalaConcreto, BalaMetalica, BalaAsfalto, Sangre, GrafitiSmall. El pool recicla el slot más antiguo al llenarse. Fade-out en el último 20% de `vidaDecal`.

**Conectar con SistemaDisparo:**
```csharp
// En SistemaDisparo, al detectar impacto:
SistemaDecalesHDRP.SpawnDecal(
    DecalTipo.BalaConcreto,
    hit.point,
    hit.normal);
```

**Conectar con SistemaGrafitis:**
```csharp
SistemaDecalesHDRP.SpawnGrafiti(pos, normal, color, new Vector2(0.5f, 0.5f));
```

---

### SistemaDetalleTerreno
**Propietario de:** ground cover GPU instanced (piedrecitas, champiñones, ramillas, musgo)

Lee alphamap del terreno **una sola vez en Start** y lo cachea. Se regenera cuando el jugador se mueve >8m. Llama `td.GetAlphamaps()` solo en `InicializarTras()` o al llamar `InvalidarCacheAlpha()`.

**Invalidar cache cuando SistemaTerreno repinta:**
```csharp
// En SistemaTerreno, al final de AplicarBarroCoroutine:
SistemaDetalleTerreno.Instance?.InvalidarCacheAlpha();
```

---

## Shader globals disponibles

| Global | Tipo | Rango | Propietario | Uso |
|--------|------|-------|-------------|-----|
| `_GlobalNightLevel` | float | 0-1 | SistemaVolumenHDRP | Emissive ventanas, LOD nocturno |
| `_GlobalWetness` | float | 0-1 | SistemaCharcos | Albedo mojado, charcos |
| `_GlobalWetnessSSR` | float | 0-0.95 | SistemaCharcos | Smoothness SSR en superficies |
| `_GlobalRippleTime` | float | 0-∞ | SistemaCharcos | UV animation agua |
| `_GlobalWetSmoothness` | float | 0-0.30 | SistemaShaderGlobals | MPB extra smoothness |
| `_GlobalEmissiveNight` | float | 0-1 | SistemaShaderGlobals | Mult. emissive nocturno |
| `_GlobalSnowLevel` | float | 0-1 | SistemaNevadasTerreno | Nieve en terreno y fachadas (MPB) |
| `_GlobalFocusDist` | float | 1-500 | SistemaVolumenHDRP | Distancia foco DoF (debug) |
| `_Wind` | Vector4 | xyz=dir w=fuerza | SistemaVientoVegetacion | Shader vegetación |
| `_WindStrength` | float | 0-10 | SistemaVientoVegetacion | Partículas viento |

---

## Bugs corregidos en esta iteración

| # | Sistema | Bug | Fix |
|---|---------|-----|-----|
| 1 | SistemaCharcos | Ripple drift: `t.localScale.x` multiplicaba la oscilación del frame anterior | `_charcoEscalaBase[]` guarda escala inmutable |
| 2 | SistemaPolish | Race condition: dos métodos escribían `_vignette.intensity.Override()` | `AplicarVignetteUnificada()` = única escritura |
| 3 | SistemaDetalleTerreno | `GetAlphamaps()` cada 8m (2-8ms) | Cache en Start, `InvalidarCacheAlpha()` explícito |
| 4 | SistemaVientoVegetacion | `new MinMaxCurve()` per-frame (GC 11KB/s) | `_psVel.x = float` directo, módulos cacheados |
| 5 | SistemaPolish | `GetComponent<CharacterController>()` cada frame | `_ccCache` precacheado con el jugador |
| 6 | ControladorJugador | Camera bob no compilaba: `estaEnVehiculo` inexistente y `_cc` (campo es `cc`) | Quitado el guard (el controlador ya está disabled en vehículo) + `cc.velocity` |
| 7 | SistemaAmbientParticulas | `ParticleSystemRenderer` sin material → render magenta | Material unlit compartido (alfa / aditivo para chispas) |
| 8 | SistemaPostProcesoAAA | Clase duplicada top-level (CS0101) con el stub de `SistemasInfraestructura` | Eliminado el stub; la versión completa expone superconjunto de la API |
| 9 | ControladorJugador / Huellas | Materiales (`new Material`) sin liberar → fuga | Blob shadow añadido a `_matsCreados`; Huellas destruye sus 4 mats en `OnDestroy` |

---

## Cómo añadir un nuevo efecto de post-procesado

1. Añadir el campo al Volume en `SistemaVolumenHDRP.CrearVolumenDia()` o `.CrearVolumenNoche()`
2. Opcionalmente, añadir un método estático en `SistemaVolumenHDRP` para controlarlo desde fuera
3. Si es un efecto de respuesta (daño, explosión), añadir el estado en `SistemaPolish` y conectarlo a un evento
4. Añadir un test en `DiagnosticoGrafico.EjecutarDiagnostico()`

---

## DiagnosticoGrafico — cómo interpretar los resultados

Se ejecuta automáticamente 8 segundos después del Play. Los resultados van al log.

```
✅ APROBADO       — todos los sistemas inicializados, FPS > umbral
⚠ ADVERTENCIAS   — 1-2 fallos no críticos (sistema opcional ausente)
❌ FALLOS CRÍTICOS — 3+ fallos o FPS por debajo del umbral
```

Para añadir un nuevo test:
```csharp
Verificar("Descripción del test",
    condicion_booleana,
    "Mensaje si falla");
```

---

## Sistemas — Terreno, Edificios, Carreteras, Escenario, Jugador

### SistemaNevadasTerreno
Conecta `SistemaClima.NieveLigera` con el splatmap del terreno. Añade `capaNieve` (TerrainLayer) progresivamente en zonas planas (pendiente < `umbralPendienteNieve`). Emite `_GlobalSnowLevel` (0-1) para shaders de edificios/vegetación. Velocidad de acumulación y fusión configurable.

```csharp
// Conexión automática — solo añadir el componente a la escena.
// Para forzar nieve: SistemaClima.Instance.CambiarClima(EstadoClima.NieveLigera)
```

### SistemaFachadasDinamicas
Aplica `MaterialPropertyBlock` a todos los renderers de edificios cada 10s:
- Humedad: `_Smoothness` +0.28, albedo ×0.82 cuando `_GlobalWetness` > 0
- Suciedad: variación determinista por índice (Perlin seed), 0-35% de oscurecimiento
- Grafiti nocturno: tint emissive leve en fachadas con `_GlobalNightLevel` > 0.5
- **Nieve** (cableada): empolva el albedo hacia blanco (`0.92,0.94,0.97`) y baja `_Smoothness` a 0.05 según `_GlobalSnowLevel × 0.55`. Sustituye al shader de nieve inexistente reutilizando el mismo global del terreno. El ciclo se dispara también ante cambios de nieve (no solo lluvia/noche).

### SistemaHuellasAsfalto
Pool de `DecalProjector` para marcas de neumáticos. API:
```csharp
SistemaHuellasAsfalto.RegistrarFrenada(posRuedaIzq, posRuedaDer, rot, intensidad);
SistemaHuellasAsfalto.RegistrarDerrape(posicion, rot, intensidad, izquierda);
SistemaHuellasAsfalto.RegistrarAceite(posicion);
```
**Cableado (hecho)** en `ControladorVehiculoJugador.FixedUpdate → ActualizarHuellas()`:
- Frenada fuerte (`inputAcel < -0.05 || freno de mano`, `speed > 8 m/s`) → `RegistrarFrenada` bajo el eje trasero usando los `WheelHit.point` de `rTI`/`rTD`.
- Derrape lateral (`abs(WheelHit.sidewaysSlip) > 0.5`) → `RegistrarDerrape` por rueda.
- Throttle por rueda (`_huellaTimer[5]`, ~0.09-0.10 s) para no agotar el pool de 64 decals.

### SistemaAmbientParticulas
8 zonas de efecto en coordenadas reales de Alsasua. Efectos activos cuando jugador < `radio`:
| Zona | Tipo | Radio |
|------|------|-------|
| HerrikoPlaza, Estación | Vapor alcantarilla | 12-18m |
| Monte Aralar, Barrio Norte | Polen flotante | 25-40m |
| Interior Plaza | Polvo en luz | 8m |
| Polígono Isasia | Chispas soldadura | 20m |
| Bosques Aralar | Hojas girando | 30m |

### SistemaChunks (mejorado)
Campo `fadeActivacion = true`: al activar un chunk, arranca en LOD1 (baja calidad) durante `duracionFade/2` segundos, luego libera a automático. Elimina el pop-in visual de la transición dura `SetActive(true)`.

### ControladorJugador (mejorado)
- **Camera bob**: componente de respiración (0.5 Hz, 8mm) siempre activo + componente de paso (2.2 Hz, 12mm) cuando el jugador anda en suelo. Aplicado al `pivotCam.localPosition` sin afectar al `transform` del jugador.
- **Blob shadow**: quad negro semitransparente proyectado al suelo por raycast. Se escala inversamente con la altura (0.9→0.35 entre 0-3m). Se desactiva automáticamente si no hay suelo a 4m.

---

## Reparación de compilación (sesión de auditoría)

El proyecto había quedado **sin compilar** tras la sesión de gráficos. Causas encontradas y resueltas:

| Rotura | Detalle | Reparación |
|--------|---------|------------|
| Identificadores camera bob | `estaEnVehiculo` / `_cc` inexistentes en `ControladorJugador` | Corregidos (bug #6) |
| Clase duplicada | `SistemaPostProcesoAAA` definida 2× top-level (CS0101) | Stub eliminado (bug #8) |
| **Carpeta `Core/` borrada** | `EventBus`, `ServiceLocator`, `ISpawnService`, `IWantedSystem`, `IEconomyService`, `PlayerDeathEvent`, eventos — usados por 12+ archivos | Restaurados 9 `.cs` desde HEAD (`git show`) |
| **Carpeta `Jobs/` borrada** | structs de Burst (`JobBoidsUpdate`, `JobFBMTerrain`, `JobComprobarOcupacion`…) + `PoissonDiskSampler`, `DensidadAlsasua` — usados en `SistemaManifestacion`, `SistemaOptimizacion`, `AlsasuaTreeStreamer`, `IntegradorMatematicas` | Restaurados 5 `.cs` desde HEAD |
| **Todos los `.cs.meta` de `Assets/Scripts/` borrados** | 103 scripts sin `.meta` → GUIDs inestables, referencias de escenas/prefabs en riesgo de romperse | Restaurados 103 metas desde HEAD (GUID original) + generados 15 metas para scripts nuevos sin versión en git |

`Editor/` (39 herramientas) — **restaurado** desde HEAD. Verificado: sus referencias a tipos de proyecto inexistentes son todas falsos positivos (literales de `Debug.Log`, comentarios, un método local), 0 roturas a nivel de tipo. `Gameplay/NPC/NPCGuard.cs` se mantiene borrado (sin uso real, solo comentarios).

> Verificación: análisis estático (no hay editor de Unity en el entorno). Tras la reparación, **0 referencias colgantes** a tipos no definidos y **0 tipos top-level duplicados** en el ensamblado de runtime. Los posibles desajustes a nivel de *miembro* entre las herramientas de Editor restauradas y el runtime modificado no son detectables sin compilar — confirmar abriendo en Unity.

---

## Arquitectura — estado y notas

**Patrón de desacoplamiento (sano):** los sistemas no se referencian directamente. Comunicación por tres vías:
- `Shader.SetGlobalFloat` para estado gráfico compartido (tabla de globals arriba).
- **EventBus** (`Publish<T>`/`Subscribe<T>`, structs en `Core/Events/`) para eventos de juego: `PlayerDeathEvent`, `ZoneChangedEvent`, `ChunkLoadedEvent`.
- **ServiceLocator** (`Registrar<T>`/`Get<T>`) para servicios: `GameManagerAltsasua` se registra como `ISpawnService`, `IWantedSystem`, `IEconomyService` en `Awake` y se desregistra en `OnDestroy`.

**Orden de ejecución:** gestionado con `[DefaultExecutionOrder]` y comentarios que justifican cada número (rango −200 `SceneBootstrapper` … 200 diagnósticos). Frágil pero documentado.

**Riesgo estructural — mitigado:** la capa `Core/` es crítica (12+ dependientes) pero no estaba aislada, por lo que un borrado accidental rompió todo el build sin aviso. **Aplicado:** se creó `Alsasua.Core.asmdef` (ensamblado propio para `Core/`). Es una capa hoja pura — su código solo depende de `UnityEngine`/`System`, todas las referencias a tipos del proyecto están en comentarios — así que el aislamiento es seguro. Con `autoReferenced: true`, `Assembly-CSharp` y `Assembly-CSharp-Editor` siguen viendo `EventBus`/`ServiceLocator`/interfaces sin cambios. Beneficio: el ensamblado de Core compila aislado y errores de dependencia se detectan antes.

**Consistencia de los sistemas nuevos:** Nevadas, Fachadas, Huellas y Ambient siguen los patrones establecidos (singleton `Instance`, `DefaultExecutionOrder`, init por corrutina con delay, sin instanciar materiales salvo lo imprescindible). Encajan bien con el resto del pipeline.

