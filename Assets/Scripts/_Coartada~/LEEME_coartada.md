# Sistema de coartada — "perderse entre la gente" (staging)

Carpeta `~` → **Unity no la compila**. Válvula de alivio del bucle gato-ratón: esconderse en
un refugio enfría el **wanted** y la **paranoia** si nadie te ve.

## Qué hay
- `ZonaCoartada.cs`: va en un GameObject con Collider (refugio: txosna, sociedad, bar, callejón).
  `calidad` (0–1) = lo bien que tapa. Se fuerza a trigger.
- `SistemaCoartada.cs`: manager. Si el jugador está dentro de una zona y **ninguna autoridad lo
  ve**, baja wanted y paranoia a un ritmo escalado por `calidad × (1 + apoyo/100)`. HUD mínimo.

## Cómo funciona
1. Estás dentro de una `ZonaCoartada` (chequeo por `Collider.ClosestPoint`).
2. No te ve nadie: `OverlapSphere` sobre `capaAutoridad` + `Linecast` contra `capaObstaculos`
   (si hay muro entre el guardia y tú, no te ve). Si alguien te ve → **no hay coartada**.
3. Mientras escondido: `RestarParanoia` y `AumentarBusqueda(-1)` por acumulador (estrellas
   enteras). **Calle alta = enfrías más rápido** (te tapan mejor).

## Sinergia con el sistema de Guardia Civil
Al bajar la paranoia por la coartada, `SistemaParanoiaGuardiaCivil` revierte los tricornios
**solo** (va por paranoia). Esconderte literalmente "apaga" la militarización de la zona. Loop:
wanted ↑ → paranoia ↑ → tricornios → te escondes → wanted/paranoia ↓ → tricornios revierten.

## Activar
1. Mueve los 2 `.cs` a `Assets/Scripts/Runtime/` (o `Systems/`).
2. Crea capas **"Autoridad"** (policía/GC) y asegúrate de tener una capa de **muros/obstáculos**.
   Asigna esas capas a la policía y a la geometría en el inspector.
3. Pon `ZonaCoartada` en cada refugio (la txosna ya tiene volumen; añade un BoxCollider grande).
   Ajusta `calidad` (txosna 0.8, bar 0.6, callejón 0.35).
4. Pon `SistemaCoartada` en la escena y asigna `capaAutoridad` y `capaObstaculos`.

## Balance (sugerido)
- `estrellasPorSeg 0.6`, `paranoiaPorSeg 6` a calidad 1 y apoyo 0 → bajar 3 estrellas ≈ 5 s en
  txosna con apoyo alto; mucho más lento en un callejón con apoyo bajo.
- Que no sea gratis: si un guardia entra al refugio y te ve, se corta el enfriado al instante.
- Narrativo: encaja con M03/M07 (escapar a la txosna) y refuerza el tema "la calle te tapa".

## Notas
- Sin allocs en Update (buffer `OverlapSphereNonAlloc` reutilizado).
- Si no tienes capa de obstáculos aún, el `Linecast` no romperá nada y la detección será solo por
  distancia (más estricta): ajusta `rangoVision` mientras tanto.
