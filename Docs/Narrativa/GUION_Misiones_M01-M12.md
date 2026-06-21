# ALTSASU 207D — guion jugable (M01–M12)

Bajada de la biblia (`HISTORIA_Altsasu.md`) a misiones implementables en `SistemaMisiones`.
Cada misión: premisa · localización · objetivos (mecánica) · sistema · beats · diálogo ·
efecto en **apoyo popular** · siguiente. Ficción satírica, personajes inventados.

> Convención: **APOYO** sube/baja (0–100). Tres umbrales para el final: <40, 40–69, ≥70.
> Localizaciones reales del mapa: calle San Juan, Herriko Plaza, Frontón Burunda, estación,
> polígono Isasia, iglesia/campanario, río Arakil, monte, txosnas.

---

## ACTO I — El muerto y la furgoneta

### M01 · "Azken bidaia" (El último viaje)
- **Premisa**: la 207D del tío Patxi está precintada en el depósito municipal. Hay que
  sacarla… justo cuando sale la comitiva del entierro.
- **Localización**: depósito municipal → cortejo por calle San Juan → Herriko Plaza.
- **Objetivos**: (1) Llegar al depósito sin que te vea la Foral. (2) Forzar la 207D
  (minijuego). (3) Conducirla *dentro* del cortejo fúnebre para que no te paren. (4) Llegar a
  la txosna de Amaia.
- **Sistema**: conducción + sigilo suave + intro `Apoyo` y `Wanted`.
- **Beats**: el cura bendice la furgoneta creyendo que es del muerto → Berrueta la fotografía
  de lejos → Amaia te abre el portón: *"Ya está aquí el sobrino. Y robando. Igualito al tío."*
- **Diálogo**: **AMAIA**: *"A tu tío lo enterramos hoy dos veces: una en el cementerio y otra
  en cuanto se gire la última espalda. Tú decide cuál cuenta."*
- **Apoyo**: +10 (el pueblo flipa con el morro). +5 extra si no rompes nada.
- **Siguiente**: M02.

### M02 · "Zuloa" (El agujero)
- **Premisa**: en el desguace del Rumano descubrís el doble fondo: cintas numeradas + la
  maqueta perdida de *Eskizofrenia Rural*. Nadie sabe qué hay en las cintas.
- **Localización**: desguace (polígono Isasia).
- **Objetivos**: (1) Vaciar el doble fondo. (2) Catalogar 5 cintas. (3) Encontrar una
  casetera que funcione (pista: solo la de la iglesia).
- **Sistema**: exploración + inventario (las cintas son objetos de misión).
- **Beats**: el Rumano reconoce la letra de Patxi en los rótulos y se calla de golpe.
- **Diálogo**: **EL RUMANO**: *"Cintas numeradas y a mano. Tu tío era muchas cosas, chaval,
  pero ordenado el cabrón. Eso sí me da miedo."*
- **Apoyo**: +5.
- **Siguiente**: M03.

### M03 · "Itzalak" (Sombras) — *reinterpreta "Huir de la policía"*
- **Premisa**: Berrueta ya huele algo. Primera persecución por la calle San Juan.
- **Localización**: calle San Juan, callejones, frontón Burunda.
- **Objetivos**: (1) Despistar a la patrulla (3 estrellas). (2) Pintar 2 muros para marcar
  ruta segura (`SistemaGrafitis`). (3) Perderlos en el frontón.
- **Sistema**: **Wanted** + grafitis + conducción/parkour.
- **Beats**: si pintas con gracia, los chavales te cubren; si destrozas, el pueblo se queja.
- **Diálogo**: **BERRUETA** (radio): *"No corras, chaval. En este pueblo no se escapa nadie.
  Solo se tarda más en volver."*
- **Apoyo**: +8 si limpio; −10 si arrasas (coches, escaparates).
- **Siguiente**: M04.

### M04 · "Plazako pintada" (La pintada de la plaza) — *reinterpreta "Pintada Plaza"*
- **Premisa**: sacar a la luz el primer hilo. Goikoetxea ofrece dinero por la furgoneta.
- **Localización**: Herriko Plaza.
- **Objetivos**: (1) Pintar el primer mensaje en el muro grande de la plaza. (2) **Elección**:
  aceptar el sobre de Goikoetxea (dinero, −apoyo) o rechazarlo en público (+apoyo). (3) Volver
  a la txosna.
- **Sistema**: grafitis + primera **decisión moral** (afecta dinero y apoyo).
- **Beats**: el emisario de Goikoetxea sonríe demasiado. Amaia te mira al recibir el sobre.
- **Diálogo**: **EMISARIO**: *"Don Anselmo solo quiere la chatarra de tu tío por nostalgia,
  ¿eh? Faltaría más."* — **AMAIA**: *"Nostalgia. Como el que echa de menos lo que escondió."*
- **Apoyo**: +12 (rechazas) / −15 (aceptas, +dinero).
- **Siguiente**: M05.

---

## ACTO II — Aquí nos conocemos todos

### M05 · "Kanpandorrea" (El campanario)
- **Premisa**: cinta nº1. Para oírla hay que colarse en el campanario, donde está la única
  casetera y Casimiro escondiendo algo.
- **Localización**: iglesia + campanario.
- **Objetivos**: (1) Distraer a Casimiro. (2) Subir al campanario (sigilo vertical).
  (3) Reproducir la cinta nº1. (4) Salir antes de misa.
- **Sistema**: sigilo + audio narrativo. Si te pillan: aviso, no arresto aún.
- **Beats**: la cinta: voz joven de Patxi dando *un nombre* por teléfono → se corta. Primer
  mazazo: *el tío era confidente*.
- **Diálogo**: **DON CASIMIRO**: *"El campanario es la casa de Dios."* (pausa) *"Lo de abajo,
  un trastero. No bajes al trastero, hijo."* (obviamente bajas en M-lateral).
- **Apoyo**: neutro (secreto, aún no público).
- **Siguiente**: M06.

### M06 · "Bostekoa" (La quinta) — *el corazón del juego*
- **Premisa**: el Rumano te lleva por el monte a la casa quemada donde murieron sus amigos.
  El juego respira. Aquí no hay chiste fácil.
- **Localización**: monte sobre Altsasu, casa quemada.
- **Objetivos**: (1) Seguir al Rumano (caminata lenta, sin combate). (2) Escuchar el monólogo
  completo (puedes irte antes, pero…). (3) Dejar algo en la casa (flor / lata / mechero).
- **Sistema**: walking sim + apoyo emocional.
- **Beats**: nombra a los muertos de "la quinta" uno a uno. La heroína, por fin, dicha en voz
  alta. Silencio.
- **Diálogo**: **EL RUMANO**: *"A tus amigos los pierdes dos veces: cuando se van y cuando el
  pueblo decide que mejor no se habla. La segunda duele más, porque esa la elegimos nosotros."*
- **Apoyo**: +20 si escuchas hasta el final (la cuadrilla se entera y te respeta).
- **Siguiente**: M07.

### M07 · "Sarekada" (La redada)
- **Premisa**: la Foral revienta la txosna buscando las cintas "y de paso" droga. El pueblo
  se planta.
- **Localización**: recinto de txosnas + calle San Juan.
- **Objetivos**: (1) Esconder las cintas antes de que entren. (2) Levantar una
  **manifestación** (`SistemaManifestacion`) para frenar el desalojo. (3) Aguantar 3 oleadas.
- **Sistema**: **Manifestación** + **Wanted** alto. Si te detienen → **`PlayerArrestedEvent`**
  (pantalla de detención, pierdes 1 cinta, respawn en comisaría con −apoyo si no había coartada).
- **Beats**: barricada de palés, kalimotxo de molotov de mentira (agua con vino), los Gemelos
  "ayudando" (empeorando).
- **Diálogo**: **BERRUETA** (megáfono): *"¡Desalojen!"* — **AMAIA**: *"¡Esto lo levantamos
  nosotras a las cinco de la mañana, majo, no nos vas a echar tú a las cinco de la tarde!"*
- **Apoyo**: +15 si frenas el desalojo sin que te pillen; −25 si te detienen.
- **Siguiente**: M08.

### M08 · "Bi aldeak" (Los dos bandos)
- **Premisa**: cinta nº2. Patxi informaba a **dos** bandos a la vez. No era traidor: intentaba
  que no mataran a nadie. Giro emocional. Hay que decidir si el pueblo lo sabe… y cómo.
- **Localización**: txosna (de noche) + casa del tío.
- **Objetivos**: (1) Reconstruir el orden de las cintas. (2) **Elección**: contar la verdad
  cruda en la txosna (riesgo: el pueblo le retira el cariño al tío, −apoyo corto plazo) o
  contarla con contexto (cuesta más, +apoyo).
- **Sistema**: decisión + diálogo ramificado.
- **Beats**: foto de Patxi joven con la cuadrilla, todos vivos, ninguno sabía lo que venía.
- **Diálogo**: **KINTTO** (a sí mismo): *"No era un chivato. Era un tío que no quería elegir a
  quién enterrar. Y por eso lo enterraron a él."*
- **Apoyo**: +18 (con contexto) / −10 luego +25 (cruda, si aguantas el bajón).
- **Siguiente**: M09.

### M09 · "Hormigoia" (El hormigón)
- **Premisa**: cinta nº3. La voz que *recibía* los nombres era Goikoetxea, que los revendía a
  "los del coche sin matrícula" a cambio de recalificaciones. El cemento es sangre seca.
- **Localización**: oficina/promotora de Goikoetxea (polígono) + obra parada.
- **Objetivos**: (1) Colarte en la promotora. (2) Cruzar la cinta nº3 con los planos de
  recalificación (puzzle de pruebas). (3) Salir con la copia antes de que llegue seguridad.
- **Sistema**: sigilo + puzzle de evidencias.
- **Beats**: en la maqueta del "Altsasu Premium" faltan justo las casas de los tres chavales.
- **Diálogo**: **GOIKOETXEA** (al teléfono, sin verte): *"Lo viejo, viejo está. El pueblo no
  quiere remover, quiere pádel. Dales pádel."*
- **Apoyo**: +10 (tienes la prueba madre).
- **Siguiente**: M10.

---

## ACTO III — San Juan arde

### M10 · "Herria salgai" (El pueblo en venta)
- **Premisa**: Goikoetxea convoca pleno para quedarse los terrenos de la txosna. Hay que sumar
  apoyo facción por facción antes de la votación.
- **Localización**: ayuntamiento + bares + sociedad gastronómica + sacristía.
- **Objetivos**: (1) Convencer a 3 de 5 facciones (cuadrilla, comerciantes, jubilados, peña,
  Iglesia). (2) Sabotear el discurso de Goikoetxea (pintada/altavoz/cinta). (3) Que el cura
  no se venda (soborno-puja contra Goikoetxea).
- **Sistema**: misiones de persuasión + **Apoyo** como recurso gastable.
- **Beats**: Casimiro cambia de bando según quién pague más → gag recurrente.
- **Diálogo**: **DON CASIMIRO**: *"Yo estoy con el pueblo."* (mira el sobre) *"…que es muy
  grande y cabe mucha gente, hijo."*
- **Apoyo**: condiciona qué final se desbloquea (umbral ≥70 abre el final A).
- **Siguiente**: M11.

### M11 · "San Joan sutan" (San Juan arde) — *el gran set-piece*
- **Premisa**: a las doce de la noche coinciden procesión del santo, manifestación por la
  txosna y la última redada. Caos costumbrista total. Kintto debe huir con las cintas entre
  el humo de la hoguera.
- **Localización**: Herriko Plaza → calle San Juan → hoguera.
- **Objetivos**: (1) Cruzar la plaza con las cintas evitando procesión + Foral + Gemelos.
  (2) Recuperar la 207D (los Gemelos la han robado otra vez). (3) Llegar a la hoguera con
  todas las pruebas intactas.
- **Sistema**: persecución multitudinaria (multitud BRG) + manifestación + wanted máximo.
- **Beats**: el santo en andas esquiva pelotas de goma; la banda toca a destiempo; la 207D
  acaba humeando junto a la hoguera. *Postal del pueblo entero a la vez.*
- **Diálogo**: **GEMELO 1**: *"¡Dale, que es bajada!"* — **GEMELO 2**: *"¡Es una PROCESIÓN!"*
  — **GEMELO 1**: *"Pues la primera que adelantamos, hostia."*
- **Apoyo**: ±20 según cómo cruces (proteger al santo y a la gente sube; atropellar baja).
- **Siguiente**: M12.

### M12 · "Egia" (La verdad) — *final ramificado*
- **Premisa**: Berrueta te corta el paso. No para detenerte: para decidir contigo qué hacer
  con lo que los dos sabéis.
- **Localización**: junto a la hoguera, a solas, el pueblo de fondo.
- **Objetivos**: una sola elección, condicionada por el **apoyo** acumulado.
- **Diálogo**: **BERRUETA**: *"Yo ayudé a tapar esto con veinte años. Llevo cuarenta sin
  dormir. Tú decides si yo duermo… o si por fin esto se sabe. Las dos cosas pesan."*
- **Ramas** (ver árbol abajo).

---

## Árbol Apoyo → Finales (para `SiguienteMision` condicional)

```
M12 elección + APOYO acumulado:
  ├─ APOYO ≥ 70  y  "filtrar"      → FINAL A  "Gora ta gora… y de obra"
  │     El pueblo estalla; Goikoetxea cae… y lo absuelven por defecto de forma. Sale a
  │     hombros y se presenta a alcalde. Amargo. Pero en el muro están los tres nombres y
  │     nadie los borra.  (mensaje: la verdad no basta, pero queda)
  ├─ APOYO 40–69 (cualquiera) o "quedártelas" → FINAL B  "El nuevo intermediario"
  │     Kintto guarda las cintas como seguro de vida y ocupa, sin querer, el sitio del tío.
  │     El ciclo se repite. Nihilista. La 207D arranca. Créditos.
  └─ "quemarlas" (cualquier apoyo)  → FINAL C  "Que arda"
        Todo a la hoguera de San Juan. Catarsis punk. Berrueta, por fin, duerme. Kintto se
        va a pie. El Rumano levanta un kalimotxo: "Por la quinta." Negro.
```

## Misiones laterales (opcionales, suben apoyo / dan color)
- **L1 · El trastero de Casimiro**: lo de abajo del campanario no es Dios (cajas de
  recordatorios… y un sobre de Goikoetxea de 1985).
- **L2 · La maqueta**: remasterizar y pinchar el tema perdido de *Eskizofrenia Rural* en la
  txosna. +apoyo, desbloquea tema de la BSO.
- **L3 · Los Gemelos**: tres encargos absurdos que siempre acaban en hostia. Comic relief puro.
- **L4 · El río**: el Arakil arrastra cosas que el pueblo tira. Pesca de pruebas y de chatarra.

---

### Notas de implementación (`SistemaMisiones`)
- Cada `Mision` con `Objetivos` (lista) y `SiguienteMision` lineal M01→M11; M12 ramifica por
  `Apoyo` (umbral) + flag de elección.
- `Apoyo` ya existe (`SistemaApoyoPopular`); úsalo como gate de finales.
- M07 dispara `PlayerArrestedEvent` (ya implementado) en caso de captura.
- M03/M04 usan `SistemaGrafitis`; M07/M11 usan `SistemaManifestacion`; M11 usa la multitud BRG.
