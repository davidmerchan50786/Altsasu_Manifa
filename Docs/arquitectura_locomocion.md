# Arquitectura — Locomoción AAA y Motion Matching

> **Prompt Definitivo III** · `AdvancedLocomotionSystem`: movimiento con inercia y
> peso para el jugador y los NPC críticos, Foot IK sobre terreno irregular, y
> degradado a VAT por GPU para la multitud.
> Estado (2026-06-14): **diseño**. Reutiliza lo ya existente (no parte de cero):
> [`SistemaFootIK`](../Assets/Scripts/Core/SistemaFootIK.cs),
> [`Crowd/*BRG`](../Assets/Scripts/Crowd), `ISimulable`/`NivelSim` del Orquestador.
> Se integra con el [Omni-Grid](arquitectura_omnigrid.md) y el [Mosaico V3](arquitectura_mosaico_v3.md).

---

## 0. Qué reutiliza y qué añade

| Capa | Hoy | Añade V-Locomoción |
|---|---|---|
| Jugador | `ControladorJugador` (movimiento+cámara, añade `SistemaFootIK` al Mixamo) | Motion Matching + root motion sincronizado con física |
| Foot IK | `SistemaFootIK` (raycast `Physics.Raycast` por pie en `OnAnimatorIK`) | **fuente de suelo vía `TerrenoGlobal` + `RaycastCommand` batched** (crítico con Mosaico V3) + spine bending |
| NPC crítico (Actor) | FSM/Animator clásico | Motion Matching ligero (pose search compartida) |
| Multitud (lejana) | `SistemaMultitudBRG` (cápsulas, sin esqueleto) | **VAT** (Vertex Animation Texture) por instancia en el mismo BRG |
| Sim-LOD | `GlobalSimulationOrchestrator` (`NivelSim` Actor/Proxy/Ghost) | la locomoción **elige técnica por nivel** |

**Principio**: el nivel de simulación (`NivelSim`) decide el coste de animación. Motion Matching
solo para `Actor`; VAT para lo lejano; nada para `Ghost`. El sistema **se enchufa al Orquestador
que ya existe**, no inventa otro LOD.

---

## 1. Mapeo Sim-LOD → técnica de animación

```
NivelSim.Actor  (0–35 m, IGlobalSimulationOrchestrator.radioActor)
    → Motion Matching + Foot IK + spine bending + root motion físico
NivelSim.Proxy  (35–140 m)
    → Animator simple (1 blend tree locomoción) @ tick reducido, sin IK
NivelSim.Ghost  (>140 m / ocluido)
    → VAT por GPU (BRG) o estático; sin CPU de animación
```

Esto requiere que los NPC implementen `ISimulable` (hoy `NPCBase` ya no se auto-actualiza —
lo gobierna el Orquestador). En `AplicarNivel(NivelSim)` el NPC **conmuta** su animador:
activa/desactiva el componente de Motion Matching, baja a Animator, o se entrega al pool VAT.

```csharp
// En el NPC (Runtime), reaccionando al Orquestador:
public void AplicarNivel(NivelSim n) {
    _motionMatching.enabled = (n == NivelSim.Actor);
    _animatorSimple.enabled = (n == NivelSim.Proxy);
    if (n == NivelSim.Ghost) PoolVAT.Adoptar(this);   // deja de tener SkinnedMeshRenderer activo
    else PoolVAT.Soltar(this);
}
```

---

## 2. Pilar 1 — Motion Matching + Root Motion

### 2.1 Disponibilidad (honestidad técnica)
Unity 6 **no** incluye un Motion Matching de producción en el editor base; existe el paquete
experimental de **Motion Matching** (`com.unity.animation.motionmatching` / muestras DOTS) y la
opción de una implementación propia ligera (pose search sobre una base de features). El diseño
asume el paquete si está disponible y define un **fallback propio** (§2.4) para no bloquear:
detrás de un define `ALSASUA_MOTIONMATCHING` (igual patrón que `ALSASUA_ADDRESSABLES`).

### 2.2 Base de datos de poses (Pose Search)
- Animaciones fuente (idle, walk, jog, run, frenadas, giros, arranques) → se extraen **features**
  por frame: posición/velocidad de pies y cadera, trayectoria futura (root) a 0.2/0.4/0.6 s.
- En runtime, cada N ms se compara el **estado actual + trayectoria deseada** contra la base y se
  salta al frame que minimiza el coste (distancia ponderada de features) → transición orgánica
  sin máquina de estados ni blend trees a mano.

### 2.3 Trajectory Generation (input → "vectores de deseo")
- El stick del mando no controla velocidad directa: genera una **trayectoria deseada** (a dónde
  querría estar el personaje en 0.2–0.6 s), con suavizado tipo spring-damper → inercia y peso.
- Frenar en seco = la trayectoria deseada colapsa al punto actual; el pose search elige la
  animación de frenada. Arrancar = trayectoria que se estira; elige el arranque con empuje.
- `ControladorJugador` deja de mover el `CharacterController` por velocidad fija: pasa los inputs
  como trayectoria al Motion Matching, que produce **root motion**.

### 2.4 Root Motion + física (Havok) — sin desincronizar con la multitud
- El root motion propone un desplazamiento; el `CharacterController`/rigidbody lo **aplica con
  resolución de colisión** (depenetración contra la multitud y edificios). Si la física frena el
  cuerpo (choque con un muro humano), se **realimenta** al Motion Matching el desplazamiento real
  → la siguiente búsqueda parte del estado físico, no del animado → cero "patinaje" ni pies que
  se hunden al empujar contra la masa.
- La multitud (manifestantes Actor cercanos) expone su posición por el **Omni-Grid**
  (`QueryRadio(jugador, r, TipoEspacial.Manifestante)`) → el resolver de empuje sabe contra quién
  depenetra sin `OverlapSphere`.

---

## 3. Pilar 2 — IK procedural de entorno (Foot IK + Spine)

### 3.1 El cambio crítico con Mosaico V3
`SistemaFootIK` hoy hace `Physics.Raycast(... capaSuelo ...)` por pie. **Con Mosaico V3 no hay
`TerrainCollider`** → ese raycast no golpea el suelo salvo donde haya un JIT patch. Solución dual:
- **Altura base**: usar `TerrenoGlobal.AlturaMundo(posPie)` (matemático, siempre disponible,
  tile-aware) como suelo de referencia — más barato y robusto que un raycast.
- **Detalle fino** (bordillos, escalones, props): `RaycastCommand` **batched** (un job de raycasts
  para los dos pies de todos los Actor a la vez) contra la capa de los JIT patches + colisionadores
  de props. Resultado asíncrono → cero hitch.

```csharp
// Foot IK V2: suelo = max(altura matemática, hit de raycast batched contra props/patches)
float yBase = TerrenoGlobal.AlturaMundo(posPie);          // Core, siempre
float yFino = _resultadosRaycast[idxPie].point.y;          // RaycastCommand (job), si golpeó
float ySuelo = math.max(yBase, yFino);
_anim.SetIKPosition(goal, new Vector3(posPie.x, ySuelo + offset, posPie.z));
```

### 3.2 Spine bending y alineación
- Con la pendiente del terreno (normal del muestreo de alturas), inclinar la cadera/columna para
  que el cuerpo "siga" la rampa (Aralar, escaleras) — vía **Animation Rigging** (`MultiAimConstraint`/
  rig de columna) o un job que ajuste rotaciones de huesos tras el Motion Matching.
- Pelvis adaptativa: bajar la cadera a la altura del pie más bajo en rampas (evita la pierna
  estirada flotando).

### 3.3 Asíncrono y por LOD
- Solo `NivelSim.Actor` corre Foot IK. El batch de `RaycastCommand` se dimensiona por el nº de
  Actores (caps del Orquestador → ≤70). Los pies de los manifestantes lejanos no se calculan.

---

## 4. Pilar 3 — Multitud: VAT por GPU (BRG), sin esqueleto

### 4.1 Por qué VAT
Motion Matching para 2.000 NPC es inviable. Los lejanos (`NivelSim.Ghost`/`Proxy` de fondo) se
animan con **Vertex Animation Textures**: la animación (walk cycle) se hornea a una textura
(posición de cada vértice por frame) y el **vertex shader** la reproduce → **sin
`SkinnedMeshRenderer`, sin CPU de animación, sin esqueleto**.

### 4.2 Se monta sobre el BRG que YA existe
[`RenderizadorMultitudBRG`](../Assets/Scripts/Crowd/RenderizadorMultitudBRG.cs) ya sube matrices
por instancia a un `GraphicsBuffer` crudo con `MetadataValue`. VAT añade **un dato más por
instancia**: el *cursor de animación* (frame + fase del ciclo).

```
Layout GraphicsBuffer (extiende el actual):
  [...] objectToWorld, worldToObject, _BaseColor            (ya existen)
  [+]   _VATState (float2: tiempoCiclo, velocidad → frame)  ← nuevo, 8 B × N
Shader (DOTS Instancing): muestrea _VATTex[meshVtx, frame(_VATState)] → posición del vértice
```

- El `BuildMatricesJob` (Burst) ya calcula pos/vel; añade escribir `_VATState[i] = (faseCiclo,
  velocidad)` → la velocidad del boid elige walk vs run en la VAT (blend de dos filas).
- El culling CPU por instancia del BRG (frustum) **se sustituye por la God Query de frustum** del
  Omni-Grid (`QueryFrustum(TipoEspacial.Manifestante)`) → un solo cull global compartido con el
  resto del render, en lugar del bucle por-instancia actual del `OnPerformCulling`.

### 4.3 Transición Actor ↔ VAT sin "pop"
- Al promover un boid VAT a `Actor` (se acerca el jugador), se instancia un GO con
  SkinnedMesh+Motion Matching en la **misma pose/fase** (el `_VATState` da el frame del ciclo) →
  el cambio es continuo. Al revés, se captura la pose y se entrega al pool VAT.
- Gobernado por el Orquestador (`maxPromosFrame` reparte el coste de estos rents al girar la cámara).

---

## 5. Integración con sistemas existentes
- **Orquestador** (`GlobalSimulationOrchestrator`): única autoridad de LOD. La locomoción solo
  implementa `AplicarNivel`. Bajo `FactorCarga<1`, el radio de Actor encoge → menos Motion
  Matching/IK automáticamente.
- **Omni-Grid**: depenetración del jugador contra multitud (radio) y culling de la multitud
  (frustum) — reemplaza `OverlapSphere`/cull por-instancia.
- **Mosaico V3**: Foot IK y root motion leen suelo de `TerrenoGlobal` (matemático) + `RaycastCommand`
  contra JIT patches; nunca dependen de `TerrainCollider`.
- **Crowd**: el `FlockingJob` no cambia; solo se añade el canal `_VATState` y el cull pasa al grid.

## 6. Riesgos y mitigaciones
| Riesgo | Mitigación |
|---|---|
| Motion Matching de Unity 6 inmaduro/ausente | Define `ALSASUA_MOTIONMATCHING`; fallback al Animator/blend tree actual. |
| Root motion peleando con `CharacterController` | Realimentar el desplazamiento físico real a la búsqueda (§2.4). |
| Coste de hornear VAT (autoría) | Pipeline offline (editor) que hornea las 2–3 animaciones de multitud a textura una vez. |
| Pop en transición VAT↔Actor | Transferir fase del ciclo via `_VATState`; rents repartidos por el Orquestador. |
| Foot IK costoso a muchos Actores | Cap por el Orquestador (≤70) + `RaycastCommand` batched. |

## 7. Plan de fases
0. **Foot IK V2** ✅ **HECHO** (2026-06-14, [`SistemaFootIK.cs`](../Assets/Scripts/Core/SistemaFootIK.cs)): suelo BASE vía `TerrenoGlobal.AlturaMundo` + normal por diferencias finitas; raycast ahora solo refina props/escalones/parches (ya no depende de `TerrainCollider` → compatible con Mosaico V3). Pendiente opcional: `RaycastCommand` batched centralizado para todos los Actor (optimización, no bloquea).
   - ✅ **HECHO (2026-06-15)**: suelo BASE ahora pide primero `ServiceLocator.Get<IMuestreadorAlturaPrecisa>()` (Mosaico V3 Fase 0); si está `Listo` usa su muestreo bit-exacto del RAW lattice (`AlturaMundo`/`NormalMundo`), si no cae a `TerrenoGlobal.AlturaMundo`/`NormalTerreno` (comportamiento previo). Ver [arquitectura_mosaico_v3.md §8](arquitectura_mosaico_v3.md).
0.5. **Contratos `ILocomocionAvanzada`** ✅ **HECHO (scaffold, 2026-06-15)**: [`ILocomocionAvanzada.cs`](../Assets/Scripts/Core/ILocomocionAvanzada.cs) (Core) define `TrayectoriaDeseada` (4 muestras p/d/t + velocidad final, blittable), `IProveedorTrayectoria` (quien genera la trayectoria — jugador o NavMeshAgent de NPC) y `ILocomocionAvanzada` (consume el proveedor, produce `Fase`/`VelocidadActual`, recibe `RealimentarDesplazamientoReal` del `CharacterController`).
   - ✅ **HECHO (scaffold, 2026-06-15)**: [`ProveedorTrayectoriaInput.cs`](../Assets/Scripts/Runtime/ProveedorTrayectoriaInput.cs) — `IProveedorTrayectoria` del jugador. Lee `ControladorJugador.DireccionMovimientoDeseada`/`VelocidadObjetivo` (dos getters nuevos, ya relativos a cámara) y aplica spring-damper crítico propio (suavizado exponencial, `frecuenciaHz` configurable) → produce `TrayectoriaDeseada` con inercia/peso por extrapolación lineal a 0.2/0.4/0.6 s.
   - ✅ **HECHO (scaffold, 2026-06-15)**: [`LocomocionAnimatorFallback.cs`](../Assets/Scripts/Runtime/LocomocionAnimatorFallback.cs) — `ILocomocionAvanzada` "detrás de `ALSASUA_MOTIONMATCHING`": deriva `Fase`/`VelocidadActual` de la trayectoria con umbrales simples (Quieto/Andando/Corriendo/Frenando/EnAire vía `ControladorJugador.EstaEnSueloP`), sin pose search ni root motion.
   - Ambos son **puramente aditivos**: no tocan el Animator ni el `CharacterController` — `ControladorJugador.ActualizarAnimaciones()` sigue siendo la única fuente de verdad de las animaciones del jugador. Compilan limpio (0 errores, Core+Runtime+Editor).
   - ✅ **HECHO (2026-06-15)**: `ControladorJugador.Awake()` añade ambos componentes automáticamente (mismo patrón que `SistemaFootIK` en el Mixamo) y llama `LocomocionAnimatorFallback.Conectar(proveedorTrayectoriaInput)`. Expuesto vía `ControladorJugador.Locomocion` (`ILocomocionAvanzada`) para que futuros consumidores lean `Fase`/`VelocidadActual` sin recalcular nada.
   - Pendiente: que algo LEA `Locomocion.Fase`/`VelocidadActual` (p.ej. Foot IK §3.2 spine bending, o el HUD); cuando `ALSASUA_MOTIONMATCHING` exista, un `LocomocionMotionMatching` con el mismo contrato sustituye al fallback sin tocar consumidores.
1. VAT offline bake (editor) de la animación de caminar de multitud + canal `_VATState` en el BRG.
2. Cull de la multitud por la God Query de frustum del Omni-Grid (sustituye el bucle de `OnPerformCulling`).
3. Motion Matching del jugador detrás de `ALSASUA_MOTIONMATCHING` (fallback al sistema actual).
4. Root motion + depenetración contra multitud (Omni-Grid).
5. Spine bending (Animation Rigging) + transición continua VAT↔Actor.

> La Fase 0 (Foot IK desacoplado del collider) es además **prerequisito del Mosaico V3** —
> conviene hacerla aunque Motion Matching se posponga.
