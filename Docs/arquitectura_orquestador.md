# Arquitectura — Orquestador de Simulación y Time-Slicing

> **Prompt Definitivo (Orquestador)** · El "Director" en la capa Core que gobierna
> cuándo y cómo se actualizan los sistemas de Runtime/Systems para 60 FPS estables.
> Estado (2026-06-14): **YA IMPLEMENTADO** — 3 de los 4 pilares completos.
> Archivos: [`GlobalSimulationOrchestrator.cs`](../Assets/Scripts/Core/Simulacion/GlobalSimulationOrchestrator.cs),
> [`SimulacionContratos.cs`](../Assets/Scripts/Core/Simulacion/SimulacionContratos.cs),
> [`ServicioTelemetriaFrames.cs`](../Assets/Scripts/Core/Simulacion/ServicioTelemetriaFrames.cs).
> Este documento **verifica lo construido** contra el spec y **diseña el único hueco real**:
> el Nivel 2 (Ghost) como dato puro en `NativeArray` con Job de fondo.

---

## 0. Veredicto por pilar (comprobación contra el spec)

| Pilar del spec | Estado | Dónde |
|---|---|---|
| **1. Time-Slicing** (30/5/1 Hz vía PlayerLoop) | ✅ **completo** | `Frecuencia` enum + `PeriodoFrames()` + striding por fase + inyección en `Update` del PlayerLoop |
| **2. Sim-LOD 3 niveles** (`ISimulable`, dist+oclusión) | ✅ **completo** (2026-06-14) | `ISimulable`/`NivelSim`/`EvaluarLOD()` + pool por desactivación (`PoolNPCSimulacion`) + **deriva del ghost** (`PoolGhosts`+`SimGhostJob`) |
| **3. Throttling dinámico** (ITelemetryService, degrade) | ✅ **completo** (2026-06-14) | `FactorCarga` + `AjustarFactor()` + `ServicioTelemetriaFrames` + helper `AutoPausaPorCarga` |
| **4. Aislamiento + ServiceLocator** | ✅ **completo** | `ServiceLocator.Registrar<IGlobalSimulationOrchestrator>` + registro `ITickable`/`ISimulable` |

**Conclusión**: el Orquestador ya estaba y es sólido; **no se rediseña**. En esta sesión se han
rematado los dos flecos: la **deriva del ghost** (Pilar 2) y la **auto-pausa de productores**
(Pilar 3). Ya no queda hueco arquitectónico; solo validación en editor.

---

## 1. Pilar 1 — Time-Slicing (✅ implementado)

`Frecuencia { PorFrame, Hz30, Hz10, Hz5, Hz1 }` → `PeriodoFrames()` (1/2/6/12/60 @60fps).
Cada `ITickable` declara su `Frecuencia`; el Director, en su tick de PlayerLoop:

```csharp
// GlobalSimulationOrchestrator.DespacharTicks() (resumen del código real)
int periodo = e.t.Frecuencia.PeriodoFrames();
if ((_frame + e.fase) % periodo != 0) continue;     // downsample + striding por FASE
if (sobrePresupuesto && periodo >= umbralDiferible) continue;   // bajo presión, los lentos esperan
e.t.Tick(e.acum); e.acum = 0f;                      // dt acumulado real, no Time.deltaTime
```

- **Striding por fase** (`e.fase` incremental al registrar) → reparte los cientos de NPC entre
  frames; no se acumulan todos en el mismo frame "Hz10".
- **Mapeo del spec**: IA combate = `Hz30` (`frecActor`), pathfinding lejano = `Hz5` (`frecProxy`),
  economía/apoyo = `Hz1` (`frecGhost` / sistemas a `Hz1`).
- **PlayerLoop**: `Instalar()` inserta un `PlayerLoopSystem` tras los `Update` → un único punto de
  entrada por frame, sin GameObject, sin depender del orden de `Update` ajenos.

> Sin cambios necesarios. (El [Omni-Grid](arquitectura_omnigrid.md) se inyecta en `EarlyUpdate`,
> *antes* que el Director, por lo que el Director puede consultar el grid ya construido.)

---

## 2. Pilar 2 — Sim-LOD (🟡 falta el Ghost-as-data)

### 2.1 Lo que ya hay
`EvaluarLOD()` recorre una **ventana rotatoria** de `ISimulable` (no todos cada frame), calcula
nivel por distancia a cámara + **oclusión aproximada** (detrás de la cámara y lejos → baja un
nivel), con **histéresis pegajosa** (sin parpadeo en el borde) y **caps escalados por
`FactorCarga`**. Niveles: `Actor(0) / Proxy(1) / Ghost(2)`.

### 2.2 El hueco vs el spec (resuelto)
El spec define el Nivel 2 como: *"Sin representación visual (GameObjects destruidos). Solo son un
punto en un NativeArray calculando probabilidades de movimiento en un Job en segundo plano."*

Matiz importante (corrige una versión previa de este doc): el GO de un Ghost **ya no se destruía
ni quedaba vivo** — [`PoolNPCSimulacion`](../Assets/Scripts/Runtime/PoolNPCSimulacion.cs) lo
**aparca** bajo un contenedor inactivo (`EntrarGhost`/`SalirGhost` en `NPCBase`), coste ~0. Lo que
faltaba era lo otro: un ghost aparcado quedaba **congelado**, no "calculaba probabilidades de
movimiento". **Eso es lo implementado ahora** (`PoolGhosts` + `SimGhostJob`): mientras el GO duerme,
su posición deriva en un `NativeArray` vía Job Burst @1 Hz; al promocionar, `SalirGhost` lee la
posición simulada y teletransporta el GO ahí → reaparece "donde habría caminado".

### 2.3 Ghost-as-data — ✅ implementado (2026-06-14)
Archivos: [`PoolGhosts.cs`](../Assets/Scripts/Core/Simulacion/PoolGhosts.cs) (Core) +
cableado en [`NPCBase.cs`](../Assets/Scripts/Runtime/NPCBase.cs) (`EntrarGhost`/`SalirGhost`/`OnDestroy`).
Patrón id-opaco, slots **estables** con free-list (sin reindexar), API en `Vector3` (Runtime no
toca `float3`). Reversible: si el servicio `IPoolGhosts` no está, `_slotGhost` queda −1 y el
comportamiento es el de antes. Diseño de referencia (el código real sigue esta forma):

```
NivelSim.Proxy ──promote/demote──► NivelSim.Ghost
   Runtime (NPCBase.AplicarNivel(Ghost)):  capturar {pos, destino, vel, tipo} → PoolGhosts.Registrar
                                            devolver el GO al pool (SetActive(false), NO Destroy)
   Core (PoolGhosts, ITickable @ Hz1):     un SimGhostJob (Burst) avanza TODOS los ghosts
                                            (random-walk sesgado al destino = "probabilidad de mov.")
   Ghost ──promote──► Proxy:                PoolGhosts.Rehidratar(id) → reactivar GO en la pos simulada
```

```csharp
// Core/Simulacion/PoolGhosts.cs  (capa CORE; id OPACO, no sabe qué es un NPC)
public struct GhostData { public float3 pos, destino; public float vel; public int tipo, idEntidad; }

public sealed class PoolGhosts : ITickable
{
    NativeList<GhostData> _ghosts;            // Persistent
    public Frecuencia Frecuencia => Frecuencia.Hz1;   // simulación barata, lejana

    public int Registrar(in GhostData g) { _ghosts.Add(g); return _ghosts.Length - 1; }
    public GhostData Leer(int slot) => _ghosts[slot];
    public void Liberar(int slot) { /* swap-remove + reindex callback */ }

    public void Tick(float dt)
    {
        new SimGhostJob { ghosts = _ghosts.AsArray(), dt = dt, semilla = (uint)Tiempo() }
            .Schedule(_ghosts.Length, 64).Complete();
        // (opcional) Submit de cada ghost al Omni-Grid → IA/streaming los siguen "viendo".
    }
}

[BurstCompile] struct SimGhostJob : IJobParallelFor {
    public NativeArray<GhostData> ghosts; public float dt; public uint semilla;
    public void Execute(int i) {
        var g = ghosts[i];
        float3 haciaDestino = math.normalizesafe(g.destino - g.pos);
        var rnd = new Random(semilla + (uint)i * 747796405u);
        float3 ruido = (rnd.NextFloat3() - 0.5f) * 0.3f;          // deriva estocástica
        g.pos += (haciaDestino * 0.6f + ruido) * g.vel * dt;      // "probabilidad de movimiento"
        ghosts[i] = g;
    }
}
```

- **Aislamiento**: `PoolGhosts` (Core) sólo maneja `GhostData` con `idEntidad` **opaco** — no conoce
  `NPCBase`. `NPCBase` (Runtime) hace el `SetActive(false)`/rehidratado. Mismo contrato id-opaco que
  el Omni-Grid → cero acoplamiento cruzado.
- **Sinergia Omni-Grid**: los ghosts pueden hacer `Submit` al grid → la IA y el streaming siguen
  contándolos aunque no tengan GO (una manifa de 2.000 "existe" aunque solo 70 sean Actores).
- **Registro**: `PoolGhosts` se registra como `ITickable` en el Director (`Hz1`) → ya entra en el
  time-slicing existente, sin tubería nueva.

> Riesgo: tocar el ciclo de vida de `NPCBase` (destruir/rehidratar) cambia comportamiento y necesita
> validación en editor (pop al promover, coste del rent). Por eso va detrás del kill-switch que ya
> existe (`ConfiguracionSimulacion.orquestarNPCs`) y se activa por tipo de entidad.

---

## 3. Pilar 3 — Throttling dinámico (✅ implementado)

`AjustarFactor()` lee `ITelemetryService.FrameMsSuavizado` (EMA) y mueve `FactorCarga` con
**histéresis asimétrica** (degrada rápido `pasoDegrade=0.05`, recupera lento `pasoRecover=0.01`):

```csharp
if      (ema > lim * degradeMul) _factor -= pasoDegrade;   // sobrecarga → reacción rápida
else if (ema < lim * recoverMul) _factor += pasoRecover;   // holgura  → recuperación suave
_factor = clamp(_factor, factorMin=0.5, 1);
```

`FactorCarga` **encoge radios LOD y caps** (`maxActores*_factor`, `maxProxies*_factor`) → bajo
presión, cientos de NPC caen de nivel automáticamente. `presupuestoMs=15.5` degrada **antes** del
tirón real. El despacho de ticks tiene además un **slice de presupuesto blando** (`sliceSimMs`):
al superarlo, los tickables de baja frecuencia esperan a su próxima ventana.

**Pilar 3 — pausa de productores: ✅ helper añadido (2026-06-14)**. El spec pide *"pausar la
generación procedural de escombros"*. En vez de editar cada productor, se añade un componente
DROP-ON reutilizable [`AutoPausaPorCarga`](../Assets/Scripts/Core/Simulacion/AutoPausaPorCarga.cs):
se arrastra al GO del emisor de escombros/partículas/proc-gen, se le asignan los `Behaviour`/
`ParticleSystem` a pausar, y él se suscribe a `OnFactorCargaCambia` y los apaga/enciende con
histéresis (`umbralPausa=0.85` / `umbralReanuda=0.95`). Aditivo, sin tocar los sistemas existentes.

```csharp
// AutoPausaPorCarga (resumen): se suscribe y aplica con histéresis.
void OnEnable()  { _orq = ServiceLocator.Get<IGlobalSimulationOrchestrator>();
                   if (_orq != null) { _orq.OnFactorCargaCambia += AlCambiarCarga; AlCambiarCarga(_orq.FactorCarga); } }
void AlCambiarCarga(float f) {
    if      (!_pausado && f < umbralPausa)   Aplicar(true);    // se va de 16.6 ms → apaga escombros
    else if ( _pausado && f > umbralReanuda) Aplicar(false);   // holgura → reanuda
}
```

---

## 4. Pilar 4 — Aislamiento y ServiceLocator (✅ implementado)

El Director se registra en Core y las entidades se **suscriben al instanciarse** (no se auto-tickean):

```csharp
// ── BOOT (GlobalSimulationOrchestrator.Boot, [RuntimeInitializeOnLoadMethod]) ──
ServiceLocator.Registrar<IGlobalSimulationOrchestrator>(Instancia);
ServiceLocator.Registrar<ITelemetryService>(Instancia._tele);
Instalar(Instancia.TickFrame);     // inyecta en el PlayerLoop

// ── REGISTRO (en Awake/OnEnable del NPC, capa Runtime) — NO hay Update propio ──
void OnEnable() {
    var orq = ServiceLocator.Get<IGlobalSimulationOrchestrator>();
    orq?.Registrar((ITickable)this);     // "actualízame a mi Frecuencia"
    orq?.Registrar((ISimulable)this);    // "tengo 3 niveles de Sim-LOD"
}
void OnDisable() {
    var orq = ServiceLocator.Get<IGlobalSimulationOrchestrator>();
    orq?.Desregistrar((ITickable)this);
    orq?.Desregistrar((ISimulable)this);
}

// ── DESPACHO (el Director, una vez por frame, en el PlayerLoop) ──
void TickFrame() { _tele.Muestrear(); AjustarFactor(); DespacharTicks(); EvaluarLOD(); }

// La entidad solo implementa los contratos; nunca se llama a sí misma:
public Frecuencia Frecuencia => _nivel == NivelSim.Actor ? Frecuencia.Hz30 : Frecuencia.Hz5;
public void Tick(float dt) { /* IA/pathfinding según nivel */ }
public Vector3  Posicion => transform.position;
public NivelSim Nivel    => _nivel;
public void AplicarNivel(NivelSim n) { /* conmutar físicas/anim/pool (incl. Ghost-as-data §2.3) */ }
```

Layer-safe: Core solo conoce `ITickable`/`ISimulable`/`ITelemetryService`; nunca `PoliciaForalIA` & co.

---

## 5. Estado final (resumen accionable)
1. **Ghost-as-data** (§2.3): ✅ **hecho** — `PoolGhosts` + `SimGhostJob` (Core) + handoff en `NPCBase`.
2. **Auto-pausa de productores** (§3): ✅ **hecho** — helper `AutoPausaPorCarga` (drop-on). Pendiente
   (uso, no código): arrastrarlo a los emisores reales (escombros/partículas) y asignar objetivos.
3. **Tuning de radios** (menor): el spec sugiere 0–30 / 30–150 / 150+; el código usa 35 / 140
   (`ConfiguracionSimulacion`, mutable) — ajustar en QA, no es arquitectura.
4. **Sin verificar en editor**: nada se ha probado en Play. El handoff de ghost toca el ciclo de
   vida de `NPCBase`; validar promoción/degradación (pop, drift razonable) antes de darlo por bueno.

> Sinergia con el Omni-Grid: ✅ **hecha** (2026-06-14). `PoolGhosts.Registrar` inscribe cada ghost
> como entidad **retenida** del grid (`SubmitRetenido`, con su `TipoEspacial`) y `PoolGhosts.Tick`
> refresca su posición (`ActualizarRetenido`) → IA y streaming "ven" miles de NPC sin GameObject.
> `Liberar`/`OnDestroy` hacen `QuitarRetenido`. Es el primer productor real del Omni-Grid.
