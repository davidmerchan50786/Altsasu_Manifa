# Mecánica: paranoia alta → NPCs en Guardia Civil + coches en patrulla

Cómo hacerlo bien en este proyecto, sin reventar el presupuesto de CPU/GPU ni meter pop-in.

## Principio: CONVERTIR, no spawnear
A paranoia alta NO instancias guardias nuevos (caro, pop-in, descuadra el streaming). En vez de
eso, **conviertes** una fracción de los NPCs y coches que YA existen en el mundo: les cambias el
skin, el cerebro y la facción, y guardas su estado original para **revertirlos** cuando la
paranoia baja. Es la misma filosofía que el ghosting de `NPCBase` y los directores de presupuesto.

> Metáfora de diseño que encaja con la trama (la "guerra sucia", la sospecha): la paranoia no
> trae guardias de fuera; **transforma a tus vecinos**. El paisano de la esquina, de repente,
> lleva tricornio. Eso da más miedo que una patrulla nueva.

## Enganche (lo que ya tienes)
- **Señal**: `SistemaApoyoPopular.OnParanoiaCambia(float)` y `OnParanoiaCritica()`
  (umbral 70, máx 90). Te suscribes a eso. (Hay también un `SistemaParanoia` simple en
  `SistemasSimulacion.cs`; usa el de `SistemaApoyoPopular`, que tiene eventos y umbrales.)
- **Cerebro policía**: `CerebroGOAPPolicia` (+ `AccionesPolicia/ContextoPolicia/MetasPolicia`).
- **NPC**: `NPCBase` cachea `Renderer[]` → reskin barato. Tiene IA por tick (orquestador).
- **Wanted/arresto**: ya integrados (`PlayerArrestedEvent`).

## Curva de conversión
```
paranoia < 70           → 0 conversiones (paisanos normales)
70 ≤ paranoia < 90      → ramp lineal: convertidos = lerp(0, MAX/2, (p-70)/20)
paranoia ≥ 90 (crítica) → convertidos = MAX ; OnParanoiaCritica dispara "oleada"
al bajar               → revertir gradualmente al ritmo `ritmoPorSegundo`
```
`MAX` ≈ 8–15 NPCs + 3–6 coches según densidad de la zona. Conversión **gradual** (1 cada X s),
no de golpe: la sensación es "el pueblo se va militarizando", no "boom, todo Guardia Civil".

## Selección (clave para que no se note)
Convierte preferentemente a quien el jugador **no está mirando** (fuera del frustum o tras
esquina) y dentro del radio de streaming. Determinista por `id` para que sea estable entre
frames. Prioriza NPCs/coches cercanos a focos de wanted. Así el morph nunca ocurre en pantalla.

## Qué cambia cada conversión (y cómo se revierte)

### NPC → Guardia Civil
1. **Skin**: sustituir el material de los `Renderer` por `uniformeMaterial` (verde GC) y
   activar el `tricornioPrefab` (un hijo que enciendes). *Guardar los materiales originales.*
2. **Cerebro**: desactivar el componente de IA civil del NPC y `AddComponent<CerebroGOAPPolicia>()`
   (o activar uno pre-añadido y dormido). *Guardar referencia para revertir.*
3. **Facción/tag**: marcarlo como hostil/autoridad para que el `SistemaDeteccionIA` y el wanted
   lo traten como policía.
4. **Revertir**: restaurar materiales, apagar tricornio, quitar/parar el cerebro GOAP, reactivar
   la IA civil, restaurar facción.

### Coche civil → patrulla GC
1. **Librea**: swap del material de carrocería por `libreaPatrullaMaterial` (verde/blanco GC).
2. **Luces/sirena**: encender `luzPatrullaPrefab` (rotativo) + audio de sirena.
3. **Comportamiento**: cambiar el `VehiculoBase`/IA de tráfico a patrulla (persigue si hay wanted).
4. **Revertir**: librea original, apagar rotativo/sirena, volver a tráfico normal.

> Todo son **swaps de material + enable/disable de componentes**, cero `Instantiate/Destroy`.
> Reversible y compatible con GPU instancing (usa materiales compartidos por variante).

## Pooling y rendimiento
- No crees guardias: reutilizas los NPCs del `SistemaMultitud`/agentes ya activos.
- Tricornio y rotativo: hijos pre-creados y desactivados (no se instancian en caliente).
- El `GobernadorRender`/orquestador siguen mandando: si hay presión de GPU, baja `MAX`.
- Cuando un convertido sale del radio de streaming o se recicla → revertir antes de reciclar.

## Componentes a crear (ver scaffold staged `Assets/Scripts/_ParanoiaGC~/`)
- `ParanoiaGCConfig` (SO): materiales, prefabs, umbrales, MAX, ritmo.
- `ConvertibleGuardiaCivil` (en cada NPC/coche convertible): cachea original, `Convertir()`/`Revertir()`.
- `SistemaParanoiaGuardiaCivil` (manager): se suscribe a `OnParanoiaCambia`, mantiene el censo de
  convertibles, calcula objetivo por la curva y convierte/revierte gradualmente, off-screen.

## Pasos para activarlo
1. Marca los NPC y coches convertibles con `ConvertibleGuardiaCivil` (o el manager los detecta por
   capa/tag al entrar en streaming).
2. Crea un `ParanoiaGCConfig` con el uniforme, el tricornio, la librea y el rotativo.
3. Pon `SistemaParanoiaGuardiaCivil` en la escena con ese config.
4. Sube la paranoia (wanted alto la sube sola: `if (nivelWanted>=3) SumarParanoia(...)`) y míralo.

## Encaje narrativo
- En M07 (redada) y M11 (San Juan) la paranoia se dispara → el pueblo se llena de tricornios que
  antes eran vecinos. Refuerza el tema: la sospecha transforma a los tuyos.
- Apoyo alto puede **frenar** la conversión (la calle te tapa); apoyo bajo la acelera.
