# ALTSASU 207D — tono, BSO y estética (biblia de dirección)

Dirección de arte y audio. Atada al render real del proyecto (HDRP, `SistemaVolumenHDRP`,
texturas PBR vascas) y a la estructura emocional. Referencias a **movimientos y medios**, nunca
a personas reales. La heroína es luto, no estética molona.

---

## 0. Tesis estética (una frase)
**Un pueblo bonito fotografiado por alguien que sabe lo que hay enterrado debajo de los
geranios.** Costumbrismo cálido por fuera, viñeta sucia de TMEO por dentro. El contraste ES el
mensaje: la fiesta que tapa y el luto que se calla, en la misma calle.

---

## 1. Dirección visual

### Paleta base (atada a las texturas del proyecto)
- **Arenisca rojiza** (fachadas vascas, `Textures_AAA/Fachadas`): ocres, tierras, rosa viejo.
- **Teja árabe terracota / pizarra** (`Tejados`): rojos quemados y grises plomo.
- **Forja negra** (balcones, rejas, `Metal`): el negro que recorta y ensucia.
- **Verde cuenca** (Arakil, prados, Aralar al fondo): verdes húmedos, no saturados.
- **Acentos punk**: el rojo-sangre y el negro de las pintadas; el naranja de la hoguera. Pocos,
  potentes, siempre con intención narrativa.

### Textura de imagen ("sucio-bonito")
- Grano de película fino + leve aberración cromática en sombras (look fanzine fotocopiado).
- Suciedad honesta: musgo en la arenisca, cal saltada, carteles superpuestos en las paredes
  (capas de fiestas viejas = memoria visual del pueblo).
- Líneas: cuando aparezca arte 2D (cómic de transiciones, cinemáticas, ilustración de cartas),
  **trazo grueso e irregular tipo TMEO**, tinta que se sale, tramas de puntos.

### Arquitectura y mundo (ya generado)
- Núcleo: calle San Juan como columna —portales, bares, pintadas en capas—.
- Periferia: polígono Isasia (frío, gris, funcional, el reino de Goikoetxea) y monte (verde,
  silencio, la casa quemada).
- El **campanario** domina todos los planos: lo vigila todo, como el pueblo.

---

## 2. Color script por acto

| Acto | Emoción | Paleta dominante | Luz |
|------|---------|------------------|-----|
| I — El muerto y la furgoneta | Nostalgia con resaca | Ocres cálidos, dorado de tarde | Hora dorada larga |
| II — Aquí nos conocemos todos | Revelación, frío que entra | Azules y grises plomo, verdes húmedos | Días nublados, noches azul-cintas |
| III — San Juan arde | Caos y catarsis | Naranja hoguera vs negro humo | Noche + fuego, contraluces |

Excepción luminosa: **M06 "la quinta"** rompe el esquema de su acto: anochece a azul lavanda y
la casa quemada queda en penumbra cálida de mechero. La belleza más triste del juego.

---

## 3. Iluminación (HDRP — `SistemaVolumenHDRP`)

- Usa el **ciclo día/noche existente** (SSAO/SSR/Bloom/DoF/Fog). Cada misión fija su **hora
  dramática** (el día no es libre durante misión narrativa):
  - M01 entierro: mediodía plomizo (el sol que no consuela).
  - M06 la quinta: del atardecer al azul de noche (transición durante la caminata).
  - M08 el giro: noche interior, una sola bombilla (cabina del flashback en sepia/B-N).
  - M11 San Juan: noche cerrada + fuego (la única fuente cálida es la hoguera).
- **Niebla del valle** (Fog) como recurso: Altsasu es cuenca a ~530 m; la niebla baja del Arakil
  al amanecer = transición entre misiones y metáfora ("lo que no se quiere ver").
- **Exposición** (Automatic EV 11–15, ya fijada para Cesium): cuidado en la hoguera, no quemar.
- Regla: la luz natural es honesta y bonita; la luz artificial (fluorescente del polígono, neón
  del bar, flash de la Foral) es siempre un poco hostil. El conflicto, también en los lúmenes.

---

## 4. UI / HUD — estética fanzine punk

- **Cartel serigrafiado**: tipografías de plantilla y de fotocopia, registro desplazado, tinta
  que pisa. Menús como carteles de concierto de txosna.
- **Apoyo popular** = termómetro/cardiograma pintado a mano en la esquina; sube como una pintada
  que crece, baja como cal que se cae. (Diegético-poético, no barra genérica.)
- **Pintada como verbo**: el `SistemaGrafitis` no es decoración, es la voz del jugador; las
  pintadas persisten en el mundo (memoria de tus actos).
- **Cintas** como inventario físico: casetes con etiqueta a mano; al seleccionarlas suena el clic
  mecánico. Cero menú estéril: todo tiene tacto.
- Notificaciones: estilo octavilla pegada con celo. Las de muerte/detención, sobre negro.

---

## 5. BSO — filosofía y partitura

### Principio
**La mayoría de la música es diegética** (sale de un sitio del mundo: la txosna, el bar, la
radio del coche, la maqueta). El *score* no diegético es escaso y entra solo cuando el pueblo
calla, porque en este pueblo la música es algo que se hace, no que se pone de fondo.

### Géneros / paletas sonoras (referencias de movimiento, no de bandas reales)
1. **Punk vasco / sonido Rock Radical Vasco (RRV) de los 80** → la energía de la calle, la
   txosna, las persecuciones. Guitarra sucia, batería tabernaria, voz rota. Es la voz de la
   cuadrilla y de la juventud que ya no está.
2. **Costumbrista de fiesta**: fanfarre/charanga, **trikitixa** (acordeón diatónico), pandereta,
   **txalaparta** para los momentos rituales (procesión, hoguera). La cara "bonita" del pueblo,
   a veces tocada un pelín a destiempo (humor TMEO).
3. **Drone de luto**: cuerda larga, armónico de campana estirado, casi silencio. SOLO para "la
   quinta" y para los muertos. Nunca se mezcla con chiste.

### Leitmotivs por personaje
- **Kintto**: un riff punk a medio empezar, que nunca resuelve del todo hasta el final elegido.
- **Patxi**: la melodía de la maqueta de *Eskizofrenia Rural*, que va apareciendo a cachos
  (radio, txosna, campanario) y solo suena ENTERA en M12/créditos según el final.
- **Berrueta**: un tic-tac bajo (café, insomnio, reloj) + una nota grave que se sostiene como su
  remordimiento. Sin melodía: el hombre que no se permite una.
- **Goikoetxea**: muzak de inauguración, hilo musical de centro comercial. Lo más siniestro del
  juego es lo más amable de oír.
- **El Rumano / la quinta**: el drone de luto + una caja de ritmos punk parada a media canción.
- **Amaia / la txosna**: trikitixa cálida; cuando ella aparece, el pueblo "suena a casa".

### La banda ficticia: *Eskizofrenia Rural*
El grupo punk del tío. Su maqueta perdida es McGuffin y corazón sonoro. Necesita **un tema
estrella** ("Bostekoa" / "La quinta", o un himno de txosna) que:
- aparezca degradado (cinta vieja, hiss) durante el juego,
- se **remasterice** en la lateral L2,
- suene limpio y entero solo en el desenlace. El estado de ese tema = termómetro emocional.

### Regla de oro de la mezcla
- **M06: silencio casi total.** Apaga la música. Solo viento, pasos, la petaca, una nota larga
  que entra cuando nombra a los muertos. El mayor recurso es no poner nada.
- **M11: cacofonía controlada.** Procesión + mani + redada = tres fuentes diegéticas solapadas
  (charanga a destiempo, consignas, sirenas) que el motor mezcla en tiempo real. A las doce
  campanadas, **todo enmudece un segundo** (la tregua de la costumbre) y vuelve. Ese silencio
  vale por toda la banda sonora.
- El **apoyo popular** modula la mezcla: con apoyo alto, la txosna suena más fuerte y cercana;
  con apoyo bajo, el mundo suena más vacío y hostil.

---

## 6. Diseño de sonido (SFX)

- **Las cintas**: hiss de casete, wow&flutter, clic del play/stop, el rebobinado. La voz de
  Patxi en cinta lleva su "color" de cinta vieja; la voz **doble** (un bando / otro bando) se
  diferencia con EQ sutil, no con efecto obvio.
- **Campanas** del campanario: marcan horas y fases (las doce de San Juan son un evento de audio).
- **Río Arakil**: lecho sonoro constante y bajo; sube en los amaneceres de niebla (transiciones).
- **Txosna**: vasos, generador, el "txotx" de la sidra, kalimotxo, multitud cercana (BRG).
- **Pelotas de goma / sirenas / megáfono**: la Foral suena metálico y plano frente a la calidez
  orgánica del pueblo.
- **La 207D**: motor diésel cansado, puerta corredera que chirría, una de las personalidades
  del juego. Su arranque renqueante = leitmotiv mecánico de Kintto.

---

## 7. Cinemáticas y transiciones
- Transiciones de día: **viñeta TMEO** estática con voz en off (Amaia o el Rumano de narradores
  no fiables), trazo grueso, tinta corrida. Baratas de producir, llenas de carácter.
- Flashbacks (M08): **sepia/blanco y negro** con grano fuerte, como foto vieja revelada de más.
- Pantalla de muerte / detención (`PlayerArrestedEvent`): negro, una octavilla cayendo, el hiss
  de cinta. Sobrio, sin sangre, con frío.

---

## 8. Tabla resumen por misión

| Misión | Luz/hora | Color | Música |
|--------|----------|-------|--------|
| M01 entierro | Mediodía plomizo | Ocre apagado | Charanga fúnebre a destiempo (diegética) |
| M03 huida | Tarde sucia | Rojo pintada | Punk RRV, persecución |
| M04 plaza | Tarde dorada | Ocre cálido | Punk callejero + silencio en la decisión |
| M05 campanario | Penumbra interior | Gris piedra | Órgano desafinado + hiss de cinta |
| **M06 la quinta** | Atardecer→azul | Lavanda/penumbra cálida | **Casi silencio**, drone de luto |
| M08 el giro | Noche, una bombilla | Sepia (flashback) | Sin score; solo la cinta |
| M09 hormigón | Fluorescente frío | Gris polígono | Muzak siniestra de Goikoetxea |
| M10 pleno | Sala neón | Verdoso institucional | Tensión + trikitixa de fondo |
| **M11 San Juan** | Noche + fuego | Naranja vs negro | **Cacofonía** diegética + tregua a las 12 |
| M12 final | Brasas | Según rama | El tema de la maqueta, entero o no |

---

### Síntesis para el equipo
Si solo se recuerda una regla: **en M06 no se pone música y en M11 no se quita ninguna… hasta la
campanada.** Entre esos dos extremos vive todo el juego. La estética es un pueblo precioso que
sabe lo que esconde; la BSO es música que el pueblo se toca a sí mismo para no oír el silencio.
