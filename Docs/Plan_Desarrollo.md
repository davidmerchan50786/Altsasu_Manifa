# Altsasu Manifa — Plan de Desarrollo (AAA+++ mundo abierto 3ª persona)

Plan completo para llevar el proyecto del estado actual a un AAA+++. Organizado en **fases** (cada una entrega algo jugable) y, dentro, **bloque a bloque**. Cada tarea lleva quién la hace y cuánto cuesta.

## Cómo leer este plan

**Tipo de trabajo** (quién):
- `COD` — código C#: lo puedo hacer yo solo desde aquí.
- `UNITY` — ejecutar un menú/bake/montaje en el editor: lo lanzas tú (o te conduzco en pantalla). No es código nuevo.
- `ARTE` — modelado, texturas, animación/mocap, VFX: assets externos, Asset Store o artista.
- `AUDIO` — música, SFX, doblaje: composición/grabación o librerías.
- `DISEÑO` — escritura, guion, balance: lo hago yo (texto), validas tú.

**Esfuerzo:** S (horas) · M (días) · L (1-2 semanas) · XL (mes+).

**Regla de oro:** nada del mundo (terreno, edificios, calles, V3, navmesh, impostores) avanza visualmente hasta tener **Unity estable + bakes ejecutados**. Ese es el bloqueo nº1.

---

## Fase 0 — Cimientos (desbloquear el resto) · 1-2 semanas

Objetivo: que el proyecto **abra, compile y se pueda hornear** sin caerse, y que el repo sea sano.

- `UNITY` Estabilizar el editor: diagnosticar el crash (Editor.log → RAM o GPU), cerrar apps de fondo, drivers GPU al día. **Bloquea todo lo demás.**
- `UNITY` Reparar git: objeto corrupto (`git fsck`), decidir LFS (data pack o remoto nuevo), push al día.
- `UNITY` Cadena de bakes base: Mosaico V2 en escena → edificios → calles → NavMesh. **Aparece el mundo jugable.**
- `COD` Gate de compilación verde (sin errores rojos) + smoke test de arranque (PantallaCarga → BaselineListo).

Entregable: **caminas por Altsasu con suelo, edificios y calles a su cota real.**

---

## Fase 1 — Mundo jugable sólido · 3-5 semanas

Objetivo: el mundo se ve bien, rinde y se puebla. (Bloques 1, 2, 12, 13)

**Bloque 1 · Mundo y mapa**
- `UNITY` Hornear Mosaico V3 (3 draw calls) + NavMesh V3. *Código hecho; solo ejecutar.* — M
- `UNITY` Capturar impostores de celda + occlusion culling bake. — M
- `COD`+`UNITY` Streaming por chunks afinado (World partition / histéresis de bandas). — L
- `ARTE` Interiores jugables (kit modular de interiores) — del nivel AAA depende mucho de esto. — XL
- `COD` Destrucción de entorno (props rompibles, cristales) — base con físicas. — L

**Bloque 2 · Render y gráficos**
- `UNITY`+`COD` Look cine negro: perfil de grading/niebla/contraste por código + ajuste en Volume. — M
- `UNITY` Bake lightmaps + APV (probe volumes). — M
- `ARTE`+`COD` Agua AAA (olas/espuma/refracción), nubes volumétricas. — L
- `UNITY` Ray tracing (RTGI/RTR/RTAO) — solo HW potente; opcional de gama alta. — M

**Bloque 12 · Rendimiento** (transversal, empieza ya)
- `COD` Presupuestos por frame (CPU/GPU governors ya existen → afinar). — M
- `COD`+`UNITY` Objetivo 60 fps PC; perfilado con el escenario real. — L
- `UNITY` Budgets de memoria/VRAM, objetivos de consola (si aplica). — L

**Bloque 13 · Pipeline**
- `COD` Pipeline de build (perfiles, escenas, defines). — M
- `COD` CI básica (compila + tests) — opcional pero recomendable. — M

Entregable: **una Altsasu que se ve AAA y rinde estable.**

---

## Fase 2 — Personaje y acción (lo que más se nota jugando) · 6-10 semanas

Objetivo: moverse, luchar y conducir se siente bien. (Bloques 3, 4, 5, 6)

**Bloque 3 · Animación y personajes** — el mayor salto de calidad, y el más dependiente de ARTE.
- `ARTE` Esqueleto humanoide estándar + retargeting (base de TODO lo animado). — L
- `ARTE` Set de locomoción (andar/correr/girar) + motion matching o blend trees. — XL
- `ARTE` Animaciones de armas, melee, traversal, entrar/salir coche. — XL
- `ARTE` Facial/lipsync + mocap (o librería tipo Mixamo/Rokoko). — XL
- `COD` Foot IK, ragdoll, control de blend desde el gameplay. — M

**Bloque 4 · Jugador y traversal**
- `COD` Esprint/stamina, nadar, rodar/esquivar. — M
- `COD` Saltar entre tejados, escaleras (sobre el parkour ya hecho). — M
- `COD` Cámara TP con colisión + over-the-shoulder + lock-on. — M

**Bloque 5 · Combate**
- `COD` Combos/contras, bloqueo/parry, ejecuciones (CaC ya tiene base). — L
- `COD` Recarga/munición animada, balística/penetración (disparo ya hecho). — M
- `COD`+`ARTE` Gore/heridas/decals de sangre, físicas de impacto. — M

**Bloque 6 · Vehículos**
- `COD` Modelo de física de conducción (derrapes, suspensión). — L
- `ARTE` Variedad: motos, camiones, bicis (modelos + setup). — L
- `COD` Daño/deformación, persecuciones policiales. — M

Entregable: **gunplay, melee, traversal y conducción con tacto AAA.**

---

## Fase 3 — Vida, misiones y narrativa · 8-12 semanas

Objetivo: el mundo está vivo y hay un juego que jugar de principio a fin. (Bloques 7, 8, 9)

**Bloque 7 · IA y vida urbana**
- `COD` Coberturas/flanqueo de la policía (GOAP ya existe), refuerzos/helicóptero. — L
- `COD` Tráfico de coches + cruces/semáforos sobre NavMesh. — L
- `COD` Pánico/estampida de multitud, barks ambientales. — M
- `COD` Reputación por facción. — M

**Bloque 8 · Misiones y narrativa**
- `DISEÑO` Escaleta detallada M00–M12 + secundarias + eventos de mundo. — L
- `DISEÑO`+`COD` Guion de los 3 actos en el motor de diálogo (Acto I ya hecho). — XL
- `COD` Misiones secundarias/recados, finales múltiples conectados al apoyo popular. — L
- `ARTE`+`COD` Cinemáticas in-engine (con las cámaras cinemáticas ya existentes). — L
- `AUDIO` Doblaje/voces de misiones + subtítulos (subtítulos `COD`). — XL

**Bloque 9 · Audio**
- `AUDIO` BSO original + música adaptativa por estado (calma/manifa/persecución). — XL
- `AUDIO` SFX (armas, ciudad, pisadas, vehículos), radio en coche. — L
- `COD` Mezcla/ducking dinámico, audio espacial (sistema; el contenido es AUDIO). — M

Entregable: **campaña de ~20 h jugable, ciudad viva, con voz y música.**

---

## Fase 4 — UI, sistemas y pulido AAA+++ · 5-8 semanas

Objetivo: que todo se sienta terminado. (Bloques 10, 11, 14)

**Bloque 10 · UI/UX**
- `COD` HUD final, menús, ajustes gráficos/control, iconos de actividades en mapa. — M
- `COD` Accesibilidad: remapeo, daltonismo/escala UI, asistencias de puntería. — M

**Bloque 11 · Sistemas de juego**
- `COD` Progresión/desbloqueos, logros/estadísticas, tiendas/compra. — L
- `COD` Recompensas por misión, traducción de contenido (sistema de localización ya hecho). — M

**Bloque 14 · Pulido AAA+++**
- `COD` Game feel: hitstop, screen shake, feedback de impacto; haptics. — M
- `COD` Photo mode, cámaras de muerte/cine, replays. — L
- `ARTE` Cabello/tela física, destrucción avanzada, detalle visual de gama alta. — L

Entregable: **el juego se siente pulido y "jugoso".**

---

## Fase 5 — Producción y entrega · 4-8 semanas

Objetivo: pasar de "juego" a "producto". (Bloque 15)

- `COD`+`DISEÑO` Plan de testing, tests automatizados, bug tracking. — L
- `DISEÑO` Balanceo de dificultad y curva de progresión (requiere playtesting). — L
- `UNITY` Build y distribución (Steam/itch), localización final. — M
- `UNITY` Certificación de consola (si va a consola) — proceso largo y formal. — XL
- `DISEÑO`+`ARTE` Marketing/trailer. — M

Entregable: **build distribuible.**

---

## Ruta crítica (qué bloquea a qué)

1. **Unity estable** → bakes del mundo → todo lo visual.
2. **Esqueleto + retargeting (ARTE)** → toda la animación → combate/traversal "se sienten" AAA. *Es el cuello de botella de arte más importante.*
3. **NavMesh + tráfico** → IA y vida urbana.
4. **Motor de diálogo (ya hecho)** → guion de los 3 actos → campaña.
5. **Audio/doblaje (AUDIO)** → la capa que más sube la percepción de "AAA" y la más externa.

## Qué puedo hacer yo solo (código), en paralelo y desde ya

Sin esperar a Unity ni a arte: combate avanzado, traversal (nadar/rodar/tejados), cámara (lock-on/over-the-shoulder), física de vehículos, IA de combate/tráfico, sistemas (progresión/tiendas/logros), UI/accesibilidad, game feel, photo mode, guion de los Actos II y III, y todos los "pegamentos" (puentes entre sistemas).

Lo que **no** puedo solo: animación/mocap, modelos, texturas, VFX, música, SFX y doblaje (ARTE/AUDIO), y la ejecución de bakes (UNITY).

## Recomendación de arranque

1. **Fase 0** ya — sin Unity estable no hay AAA posible.
2. En paralelo, yo voy avanzando el **código de Fase 2/3** (combate, traversal, IA, sistemas) y el **guion (Actos II-III)**, que no dependen de Unity.
3. Decide pronto la vía de **animación** (mocap propio vs librería tipo Mixamo/Rokoko vs artista): marca el ritmo de todo lo que "se siente".
