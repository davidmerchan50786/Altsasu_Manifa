# Controles de carretera de la Guardia Civil (staging)

Carpeta `~` → **Unity no la compila**. A paranoia alta, la GC monta **controles** en los
puntos de paso de Altsasu (calle San Juan, puentes del Arakil, salidas de la N-1). Si los
cruzas con búsqueda activa, te dan el alto. El **apoyo** te puede colar.

## Qué hay
- `ControlGuardiaCivil.cs`: un puesto. Trigger en un chokepoint + barrera visual. Cuando está
  activo y el jugador entra:
  - búsqueda 0 → pasas (no eres nadie).
  - apoyo alto → `probPasar = lerp(0.05, 0.6, apoyo/100)` → te hacen señas y pasas.
  - búsqueda < `umbralArresto` (def. 3) → **cacheo**: +8 paranoia y +1 búsqueda.
  - búsqueda ≥ `umbralArresto` → **arresto** (`PlayerArrestedEvent`, lo coge `HUDCanvas`).
  - anti-spam: un alto cada 4 s.
- `SistemaControlesGC.cs`: manager. Nº de controles activos ∝ paranoia (0 bajo `umbralActivacion`
  = 70, hasta `maxControles` = 4 a paranoia 100). Los enciende/apaga **de uno en uno y fuera de
  cámara** (`VisibleEnCamara()`), para que no haya pop.

## Activar
1. Mueve los 2 `.cs` a `Assets/Scripts/Runtime/`.
2. Coloca GameObjects con `ControlGuardiaCivil` en los chokepoints (un BoxCollider que tape la
   calzada + un hijo `barrera` con cono/foco/jersey). Etiqueta al jugador con `ControladorJugador`
   (ya lo lleva).
3. Pon `SistemaControlesGC` en la escena (autodetecta los controles).
4. Asigna las capas/colisionador del trigger para que solo dispare con el jugador/vehículo.

## Encaje en el ecosistema (ver INTEGRACION_Sistemas.md)
- **Lee**: paranoia (cuántos controles) + apoyo (probabilidad de colarte) + búsqueda (cacheo vs arresto).
- **Escribe**: paranoia (+), búsqueda (+ en cacheo), arresto vía EventBus.
- Complementa la conversión a tricornios: los `SistemaParanoiaGC` son móviles (patrullan); los
  controles son **estáticos en los pasos** → te cierran las rutas de huida. La coartada (refugios)
  sigue siendo la vía de escape; el apoyo, el comodín.

## Punto de integración (cuando abras Unity)
- Compartir `umbralActivacion`/`maxControles` con `ParanoiaGCConfig` para un único panel de tuning.
- Sustituir el `VisibleEnCamara()` propio por el del gobernador de render si se quiere una única
  prueba de visibilidad en todo el proyecto.
