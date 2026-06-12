# FABLE V: ALTSASU INSURGENTE
## Biblia de Facciones VOL. 2 — Desarrollo Profundo (Edición TMEO)

> Expande `Narrativa_Facciones_TMEO.md` (Vol. 1) y `Narrativa_Personajes_TMEO.md`.
> Contenido: lore profundo del conflicto in-game, cadenas de misiones por facción, NPCs secundarios,
> matriz de reputación, sistema de Coherencia con números, calendario de eventos, economía del movimiento
> y especificación técnica para implementación (enums, ScriptableObjects, eventos de EventBus).

---

# PARTE I — LORE PROFUNDO: LA GUERRA DE LOS SESENTA INVIERNOS

Historia in-game de Albion que espeja el arco real sin nombrarlo. Todo el pueblo vive en la resaca de esto.

## Cronología (se desbloquea en el códice del juego por fragmentos)

**Hace ~70 años — La Anexión.** La Corona de Albion absorbe el Valle por "tratado" (el tratado se firmó con la firma falsificada de un señor del valle que llevaba muerto tres semanas; el notario que lo certificó fundó la dinastía más rica de la capital). Se prohíbe la Lengua Vieja en escuelas y registros. Severino jura que estuvo en la firma. Nadie le cree. Estuvo.

**Hace 60 años — Nace SUGARRA ("La Llama").** Organización armada clandestina. Primera acción: volar la estatua ecuestre del Gobernador (el caballo sobrevivió y vivió veinte años más en una granja; hay peregrinaciones). Después vinieron décadas de lo otro: atentados, muertos, cárceles, torturas en los sótanos del fuerte, guerra sucia de la Corona con mercenarios encapuchados que "nunca existieron". **Decisión de tono:** el juego no muestra esta época jugable. Se cuenta por sus huellas: las viudas de ambos lados, las sillas vacías en las sociedades, los nombres que no se pronuncian en según qué bares. El humor se detiene en la puerta de este cuarto. TMEO también sabía cuándo no entrar.

**Hace 15 años — El Acuerdo de la Cueva.** Mediadores internacionales (un cónclave de magos neutrales de las Islas Brumosas, con dietas espléndidas) logran el alto el fuego definitivo. SUGARRA sella su arsenal en la Cueva Honda y lo deja bajo custodia de **ERRAUTS**, un dragón anciano pacifista contratado como garante del desarme (los dragones no aceptan sobornos: ya lo tienen todo y no les cabe más). Errauts sigue ahí, jubilado, haciendo crucigramas rúnicos sobre el arsenal sellado, y es uno de los NPCs más queridos del juego: recibe visitas, da consejos no solicitados y se niega rotundamente a devolver una sola ballesta a nadie, de ningún bando, por ningún motivo, desde hace quince años. *"¿Armas? Tengo. ¿Dártelas? Anda, siéntate y cuéntame qué tal tu madre."*

**Hace 8 años — La Disolución.** SUGARRA anuncia su final en un comunicado leído por tres encapuchados que, según toda la comarca, eran dos primos de Lakuntza y un actor contratado porque el tercero estaba con gripe. Fin de la organización. NO fin del conflicto: los presos siguen dispersos en las mazmorras del reino, la guarnición sigue en el fuerte, la muralla de Faustino sigue "en obras", y el duelo de todos sigue sin gestor. En ese vacío es donde engordan los aprovechados del Vol. 1.

**Hace 2 años — El Caso del Frontón.** Una pelea de madrugada entre mozos del pueblo y dos sargentos de la Guardia fuera de servicio (empezó por un codazo en la barra de Koldo, versión de Koldo: "fue por quién pagaba la ronda, como todas las guerras"). La Corona lo instruyó como "rebelión organizada contra el Trono": ocho mozos en mazmorras de máxima seguridad por un ojo morado y una mesa rota. El pueblo entero, hasta los del bando de la Corona, sabe que es una barbaridad. Es la herida ABIERTA del juego, el "Altsasu aske" de Albion: la desproporción como combustible de todo el Acto I. La madre de uno de los mozos, **FELISA** (la de las chistorras de Joni, sí), es el personaje moral del juego: la única persona a la que TODAS las facciones, la Guardia incluida, tratan con respeto absoluto. Cuando Felisa cruza la plaza, el juego baja el volumen de la música.

## Los Dos Mármoles

En extremos opuestos de Herriko Plaza hay dos memoriales: el de los caídos del pueblo y el de los guardias muertos en el Valle. Faustino el Percebe inaugura ofrenda floral en AMBOS cada año, con el mismo discurso cambiando una sola palabra ("víctimas del terror" / "víctimas de la sinrazón"), y los dos bandos lo saben y los dos le aplauden por separado, y esa es posiblemente la imagen más TMEO de todo el juego. Severino se sienta el día de las ofrendas en el banco equidistante con dos flores, una en cada mano, y no se las da a nadie. Si el jugador le pregunta por qué, responde: *"Porque conocí a los dos. Al de ese mármol y al de aquel. Jugaban juntos a pala, ahí, donde ahora está tu barricada. Anda, tráeme tabaco."* Es el único diálogo de Severino sin chiste. Diseñado así.

---

# PARTE II — DESARROLLO PROFUNDO POR FACCIÓN

Cada facción gana: NPCs secundarios con ficha corta, una cadena de misiones de 3 actos, y eventos de mundo propios.

---

## 1. GAZTE SUTEGI — Desarrollo

### NPCs secundarios
- **OLATZ "LA PANCARTERA":** 22 años, caligrafía de monasterio aplicada al agitprop. Sus pancartas son tan hermosas que la Guardia las confisca para decorar el cuartel (hay tres en el despacho del Capitán, enmarcadas). Perfeccionista patológica: una vez retrasó una manifestación 40 minutos por una tilde. *Mecánica:* sus pancartas dan +15% extra de apoyo en manifas, pero cada encargo tiene un minijuego de revisión ortográfica. Si apruebas una falta, la pancarta sale, la ve todo el pueblo, y el debuff "Hazmerreír" dura un capítulo entero.
- **EL TXIKITO DE LOS PETARDOS:** 16 años, pirotécnico autodidacta, cejas pintadas porque las suyas llevan sin crecer desde "el experimento". Hace los petardos de todas las fiestas Y los distractores de todas las acciones. Su madre cree que está en clases de solfeo. *Mecánica:* crafteo de distracciones sonoras; 5% de probabilidad de fallo crítico espectacular (cohete persigue al lanzador, físicas completas, viral en el pueblo).

### Cadena de misiones: "LA DECIMOQUINTA MARCA"
**Acto 1 — La Filtración.** Llega el rumor: la Corona prepara la decimoquinta ilegalización. Pánico organizativo, euforia serigráfica. El jugador debe verificar el expediente robándolo del fuerte (infiltración con ayuda de Joni, que "pierde" la llave del archivo a cambio de chistorra de Felisa, cerrando el círculo logístico-sentimental).
**Acto 2 — El Confidente.** El expediente revela algo gordo: el chivato que pasó la lista de militantes a la Corona es... el propio **Unai "Catorce Logos"**. No por ideología: filtró nombres ya conocidos, inofensivos, lo justo para FORZAR la ilegalización porque tiene la imprenta hipotecada y necesita el rebranding número quince para pagarla. Ha convertido la represión en su plan de negocio circular.
**Acto 3 — La Asamblea del Logo.** Decisión del jugador ante la asamblea juvenil:
- **Destaparlo:** Unai expulsado y humillado; la ilegalización se frena (la Corona pierde su pretexto); GANAS Coherencia pero Gazte Sutegi pierde su imprenta y sus pancartas bajan de calidad un capítulo (Olatz en huelga de duelo).
- **Callarte y negociar:** Unai te corta el 30% del merchandising de la marca nueva como ingreso pasivo en el mapa estratégico. Tu Coherencia BAJA en secreto. Maddi lo apunta. Siempre lo apunta.
- **Tercera vía (oculta, requiere Severino aliado):** el viejo sugiere filtrar a la Corona una lista de militantes COMPLETAMENTE inventada (nombres de difuntos del padrón de 1890). La Corona ilegaliza una organización de fantasmas, el ridículo institucional es histórico, +apoyo popular masivo, y Unai consigue su rebranding sin traicionar a nadie. Logro: *"Los Muertos También Militan"*.

### Evento de mundo
**"Pintada Patrimonial":** cada cierto tiempo, una pintada de Gazte Sutegi aparece en el muro de la iglesia. El párroco no la borra: la TASA. Lleva años vendiendo a la Diputación de la Corona la restauración del muro "vandalizado" y repartiéndose la subvención con... exacto, con Faustino. La pintada es siempre la misma porque el párroco les deja la plantilla.

---

## 2. LA COORDINADORA QUE NO EXISTE — Desarrollo

### NPCs secundarios
- **LOS CINCO SEÑORES:** solo se les conoce por el asiento que ocupan en la sociedad: **El de la Cabecera** (decide), **El del Vino** (financia), **El de la Puerta** (seguridad: lleva 30 años sentado donde ve entrar a todos), **El Callado** (nadie le ha oído hablar; las decisiones importantes se toman cuando asiente) y **La de la Cocina** — porque el quinto señor es señora, **BEGOÑA**, y lleva siéndolo desde el 89, y la Corona lleva 35 años buscando a "los cinco hombres de la sociedad" porque a ningún espía se le ocurrió jamás que quien cocina escucha todo y decide la mitad. Es el chiste más largo del juego y no se explicita nunca.
### La Silla Vacía
En la mesa hay seis sillas. La sexta lleva quince años vacía, con su plato puesto en cada cena. Era del que no llegó al Acuerdo de la Cueva. No se habla de él. El plato se retira frío cada noche y se friega con los demás. El jugador puede preguntar UNA vez; la respuesta de Begoña: *"Era el que mejor cantaba. Pásame el perejil."* No hay más lore. No hace falta.

### Cadena de misiones: "LA CENA DE LOS CINCO"
**Acto 1 — El Pinche.** Begoña se lesiona la muñeca (pelando castañas: la lesión más vasca posible). La Coordinadora necesita pinche de cocina de confianza. El jugador es propuesto... por Koldo, que cobra comisión por el enchufe, naturalmente.
**Acto 2 — Las Tres Cenas.** Tres cenas = tres decisiones del valle escuchadas desde los fogones (minijuego de cocinar SIN quemar nada mientras llegan los diálogos clave; si se quema la txuleta, los señores callan hasta que sale la siguiente: información perdida para siempre). Lo escuchado reescribe el world-state del capítulo siguiente: qué manifa se autoriza internamente, a qué facción se le corta el grifo, qué le van a pedir a Biltzar.
**Acto 3 — La Sexta Silla.** Giro: El de la Puerta descubre que el jugador escucha. En vez de echarte, la Coordinadora te hace LA oferta: ocupar la sexta silla como "correo" oficial. Aceptar = acceso permanente al Mapa de Hilos, PERO todas tus decisiones de facción pasan a necesitar "consulta previa" (cooldown añadido a tus acciones estratégicas: el precio de la silla es la silla). Rechazar = respeto ganado, puerta cerrada, y Begoña te despide con un táper. El táper da +salud máxima permanente. La decisión es mucho más difícil de lo que parece y los foros arderán.

---

## 3. ASKATU BEHARRA — Desarrollo completo de "LAS CUENTAS DE SEBAS"

### NPCs secundarios
- **FELISA:** (ver Parte I). Madre de uno de los mozos del Frontón. Reparte chistorra, dignidad y collejas morales. No es jugable como aliada de facción: está POR ENCIMA de las facciones. Si el jugador hace algo rastrero delante de Felisa, debuff único "La Mirada de Felisa": -10% a TODO durante un día in-game. No hay resistencia posible. No se puede quitar con pociones.
- **LA MULA DE SEBAS ("Trotski"):** la mula mejor alimentada del hemisferio. Pelaje con shader de salud insultante. Personaje clave de la misión (ver abajo) y, tras el final bueno, montura desbloqueable del jugador. Velocidad mediocre, carisma infinito: ir en Trotski por el pueblo da +1 apoyo popular pasivo porque todo el mundo le tiene cariño a la mula, que ella no tiene la culpa.

### Cadena de misiones: "LAS CUENTAS DE SEBAS" (la espina dorsal del Acto II)
**Misión 1 — "El Número Premiado".** Maddi enseña al jugador su archivo: seis boletos del sorteo del jamón, de seis años distintos, MISMO número de serie. El sorteo no solo no se celebra: se reimprime. Objetivo: conseguir el séptimo boleto de este año y el libro de cuentas del bote. Infiltración en la sede del comité durante el homenaje anual (todo el mundo en la plaza, lágrima fácil, el mejor momento para abrir cajones, lo cual ya te hace sentir fatal y es intencionado: la misión incomoda).
**Misión 2 — "Sigue a la Mula".** El libro está limpio: Sebas no es tonto, la contabilidad B no está en la sede. Pero Trotski hace una ruta semanal sola, cargada, y vuelve descargada. Misión de sigilo siguiendo a una mula por el monte (la mula tiene IA de detección: si te ve, se para y te MIRA hasta que te vas; no se la puede engañar, solo mantener distancia; los testers la describen como el boss más estresante del juego). Destino: una borda renovada a todo lujo a nombre del cuñado de Sebas, con cuadra nueva, jamones COLGADOS DEL TECHO (sí: los de los sorteos, todos, como un museo del crimen) y el arcón con la contabilidad B.
**Misión 3 — "La Asamblea".** Confrontación pública. Sebas despliega, en orden, todas las cartas sagradas (sistema de diálogo tipo combate por turnos, cada carta suya exige la respuesta correcta del dossier):
1. *"¿Vas a hacerle el juego a la Corona?"* → contrarrestar con: los recibos no tienen bando.
2. *"Llevo veinte años dándolo todo."* → contrarrestar con: los boletos. Seis años. Mismo número.
3. *"Esto va a hundir la moral del pueblo."* → contrarrestar con: el testimonio de un preso (conseguido vía red de familias, si te las ganaste antes: la misión premia juego previo limpio).
4. La carta final, la peor: *"Piensa en las familias."* → contrarréstala Felisa, si tu Coherencia es alta, levantándose en silencio y poniendo sobre la mesa el táper vacío de un bote al que lleva diez años aportando. Game over dialéctico para Sebas. Si tu Coherencia es baja, Felisa no se levanta por ti, y Sebas sobrevive políticamente (escapa al final malo).

**Finales:**
- **Destapado con todo:** Sebas huye de noche; fondos parcialmente recuperados (+recursos estratégicos); Trotski, liberada, se queda contigo; el comité lo refunda... Maddi, que instaura auditoría anual pública. +Apoyo popular masivo. Los jamones del techo se sortean TODOS en la fiesta más grande del juego.
- **Chantaje (ruta corrupta):** te quedas el 20% del bote a cambio de silencio. Ingresos pasivos altos. Tu retrato de la Coherencia (ver Parte IV) empieza a desarrollar, sesión a sesión, una mancha de chistorra en la solapa. El juego no te lo explica jamás. Los jugadores tardarán semanas en darse cuenta de qué la causa. Cuando lo descubran, los foros arderán por segunda vez.
- **Fracaso (sin pruebas suficientes):** ostracismo, -50% apoyo, y Sebas, intocable ya para siempre, te saluda cada mañana desde Trotski con una sonrisa que los animadores tienen orden de hacer "lo más lentamente posible".

---

## 4. ASKAPEN TOURS — Desarrollo

### NPCs secundarios
- **EL PINTOR DE BRIGADA ("Óleo Mertxe"):** retratista oficial de las brigadas. Cobra por óleo y por "épica añadida" (tarifa extra por puño en alto, tarifa doble por mirada al horizonte). Tiene un almacén de fondos pre-pintados de la Isla Komuna para quien quiera el retrato SIN hacer el viaje. Medio movimiento tiene óleo de brigada; un cuarto del movimiento no ha salido del valle.
- **KAMARADA YOEL:** delegado permanente de la Isla Komuna en Altsasu. Lleva siete años "de visita". Ha aprendido la Lengua Vieja, juega a pala los jueves, está empadronado y tiene huerto. Es, a estas alturas, más del pueblo que la mitad del pueblo, y su trabajo consiste en escribir informes a la Isla diciendo que la revolución del Valle "avanza con paso firme", informes que redacta desde la txosna porque allí el kalimotxo "ayuda al optimismo histórico".

### Cadena de misiones: "LA BRIGADA INVERSA"
**Acto 1 — Vienen Ellos.** La Isla Komuna manda delegación oficial a conocer "la heroica lucha del Valle". Pánico: hay que montar el decorado. Askapen Tours organiza una ruta potemkin: barricada "histórica" reconstruida para la foto (la original la quitó Faustino para asfaltar; la réplica la construye, cobrando, el mismo contratista que la quitó), coro de mozos ensayando espontaneidad, y Koldo etiquetando el garrafón como "Vino de la Resistencia, cosecha del Asedio".
**Acto 2 — El Descarrilamiento.** El jugador escolta a los delegados, que tienen la pésima costumbre de querer ver cosas reales y se salen del guion hacia: la borda de Sebas ("¿y estos jamones colgados, camarada?"), Gorka al teléfono con el virrey ("¿con quién negocia el compañero?") y una asamblea del gaztetxe en su hora séptima ("¿cuándo... vota esto?"). Minijuego de control de daños: desviar, traducir creativamente, sobornar con pintxos.
**Acto 3 — El Informe.** Giro final: los delegados lo habían entendido TODO desde el primer día (en la Isla también tienen Sebas, también tienen Gorkas, lo reconocieron al instante). Su informe va a ser demoledor... salvo que el jugador haga algo real delante de ellos. La misión culmina dándoles a elegir entre el acto potemkin de clausura o llevarlos a la manifa de verdad de Felisa por los mozos del Frontón, sin decorado, con cargas reales. Si eliges lo real: informe favorable, desbloqueo permanente de Observadores Internacionales, y Yoel llorando "de polen, camarada, es el polen". Si eliges el decorado: se van con cortesía glacial y el mercado exterior cierra un acto entero.

---

## 5. MOREA BILGUNEA — Desarrollo

### NPCs secundarios
- **GARBIÑE LA VETERANA:** 74 años. Fundó la primera asamblea de mujeres del valle cuando eso te costaba el trabajo, el matrimonio y dos huesos. No pide nada, no firma nada, teje en todas las reuniones (lo que teje, nadie lo sabe; lleva 40 años con la misma bufanda al 80%). Sabe dónde está enterrada la imprenta clandestina de los 70 (LITERALMENTE enterrada, en el robledal, envuelta en cera) y solo se lo dirá a quien se lo gane. La imprenta desenterrada es el desbloqueo de propaganda más potente del juego: panfletos sin coste de recurso.
- **NEREA "LA DE LOS PERMISOS":** la única persona del movimiento que entiende la burocracia de la Corona. Rellena los formularios de "concentración autorizada" con tal maestría que una vez autorizaron oficialmente "una romería con antorchas frente al fuerte, amenizada con cánticos tradicionales" (un asedio, técnicamente era un asedio). La Guardia la odia con un respeto reverencial. *Mecánica:* convierte manifestaciones ilegales en legales = la Guardia no puede cargar de inicio (necesita provocar primero, y eso da ventana táctica al jugador).

### Cadena de misiones: "EL COMUNICADO"
La gran misión coral del juego. Para el aniversario del Caso del Frontón, las ocho facciones deben firmar UN manifiesto conjunto. Maddi acepta redactarlo con una condición: nada de retórica — compromisos CONCRETOS, con plazo y firma, de cada facción. El jugador es el correo que debe arrancárselos. Es un boss rush de excusas, cada líder con su mecánica de escaqueo:
- **Unai** firma lo que sea si el logo del manifiesto es suyo (cobrar, cobra igual).
- **Los Cinco** no firman nada por escrito jamás; Begoña ofrece "palabra de cocina", y Maddi, tras un silencio histórico, LA ACEPTA — único precedente conocido, los estudiosos del lore debatirán años.
- **Sebas** firma rapidísimo y sin leer, que es exactamente como firma todo, y esa firma alegre es la que luego permite auditarlo (la misión siembra "Las Cuentas de Sebas": detalle para la segunda partida).
- **Itziar** está de brigada; hay que conseguir su firma por paloma mensajera mágica (la paloma vuelve morena).
- **Eneko** exige incorporar al manifiesto una enmienda de 14 páginas; minijuego de negociación: reducirla a una frase que él pueda presentar a sus bases como victoria total ("se incorpora el análisis materialista", la frase. Funciona. Tiene 19 años).
- **La Asanblada** debe aprobarlo en asamblea: ver misión "El Punto 3"; si ya la completaste, Potxolo firma "por consenso fáctico" en 30 segundos.
- **Gorka** firma encantado y luego pide cambiar "exigimos" por "instamos respetuosamente"; pillarle haciéndolo y obligarle a restaurar el verbo original es el check de Coherencia de la misión.
**Recompensa:** el Manifiesto del Frontón, evento de mundo que sube el apoyo popular global +20% permanente y desbloquea el Archivo de Maddi para el resto de la partida. Y la bufanda de Garbiñe avanza al 85%, que los dataminers documentarán con rigor.

---

## 6. KOMUNTZA — Desarrollo

### NPCs secundarios
- **IRATXE "LA TEÓRICA":** 20 años, la pluma de la organización. Escribe los cuadernos de tesis con un dominio del idioma genuinamente brillante puesto al servicio de demostrar que todo el mundo menos ellos es socialdemócrata. Secreto: escribe, bajo seudónimo, los romances por entregas más cursis y exitosos de Albion ("Pasión en el Fuerte: el sargento y la pastora", 11 entregas, récord de ventas), porque la teoría no paga el alquiler. Si el jugador lo descubre y calla, Iratxe se convierte en la mejor propagandista de tu facción: resulta que sabe EXACTAMENTE qué quiere leer la gente.
- **EL COMITÉ DE DISCIPLINA (tres sillas):** tribunal interno permanente. Han expedientado a más militantes de los que tienen. Una vez se expedientaron entre sí, simultáneamente, y la organización funcionó tres semanas sin dirección, que fueron, según las actas, las tres semanas más productivas de su historia. Nadie ha extraído la conclusión evidente.

### Cadena de misiones: "EL CONGRESO"
**Acto 1 — La Coma.** Tercer congreso en dos años. Dos tendencias al borde del cisma por una coma en la tesis 47 que cambia el sentido de "apoyar críticamente, a las luchas parciales" vs "apoyar, críticamente, a las luchas parciales". (Los lingüistas que consultamos confirman que la coma efectivamente cambia el sentido. Los lingüistas también nos pidieron no volver a llamarles.)
**Acto 2 — Los Pasillos.** El congreso es un mapa social: el jugador media entre tendencias (minijuego de diálogo con vocabulario bloqueado: usar la palabra "consenso" resta puntos con AMBAS).
**Acto 3 — La Votación:**
- **Cisma:** nacen "Komuntza (Marxista-Lanista)" y "Komuntza (Reconstituida)", DOS facciones nuevas permanentes en el mapa estratégico, con sedes enfrentadas en la misma calle, que dedican el resto del juego a disputarse el mismo tablón de anuncios. Te odian las dos, pero sus unidades de choque, ahora compitiendo por demostrar pureza, rinden +70% en manifas. Caos rentable.
- **Unidad:** la coma se resuelve con la solución de Iratxe (punto y coma: nadie cede, todos pueden proclamar victoria) y se desbloquea la **HUELGA GENERAL DE DISTRITO**: la mecánica estratégica más potente del juego — congela la economía de la Corona en una zona completa durante un día in-game, parando hasta las obras de la muralla, lo que provoca la única vez en todo el juego que Faustino llama al jugador LLORANDO.

---

## 7. LA ASANBLADA — Desarrollo

### NPCs secundarios
- **POTXOLO (ficha completa):** dos metros, libertario, cocinero del gaztetxe, autoridad moral de un espacio que no reconoce autoridades (la paradoja le quita el sueño; lo compensa friendo). Pacifista total con una excepción documentada: quien toque a la perra. *Mecánica:* la comida de Potxolo es el mejor buff del juego (+30% todo, 1 día) y solo se gana fregando, nunca comprando. La economía del cariño no admite monedero.
- **LA PERRA ANARKA:** perra del gaztetxe. Sin dueño, obviamente. Ignora el 100% de las órdenes, incluido su nombre, que además nadie le puso (emergió por consenso). Duerme en las asambleas y se despierta EXACTAMENTE cuando se va a votar algo importante, habilidad que nadie explica y que el diseño del juego trata como canon mágico. *Mecánica:* aura pasiva +moral en un radio; si la Anarka te sigue voluntariamente por el pueblo (1% de probabilidad diaria, no forzable, no comprable con comida — se ha probado), TODAS las facciones te tratan mejor ese día. Los jugadores harán rituales absurdos para conseguirlo. No hay ritual. Es una perra.
- **"EL OKUPA FANTASMA":** alguien vive en el piso más alto de la torre desde antes de la okupación. Nadie le ha visto. Friega su vaso (aparece fregado). En las votaciones por consenso, su silencio cuenta como asentimiento, y llevan veinte años gobernándose parcialmente por el asentimiento de un señor hipotético. Quest opcional de toda la partida: descubrirle. Resolución (Acto final): es el hermano del de la Sexta Silla. Lleva quince años arriba. Errauts el dragón lo sabe y le sube pan. El lore del juego se cierra en la torre, en silencio, sin chiste, con un vaso fregado.

### Cadena de misiones: "EL PUNTO 3"
Tras una carga brutal de la Guardia (evento del Acto II), hay heridos y el único espacio seguro es la torre. Pero cederla como hospital de campaña requiere... aprobación asamblearia. El jugador debe SOBREVIVIR A LA ASAMBLEA: minijuego de resistencia en tiempo semi-real (gestión de estamina, cafés, turnos de palabra, y el kalimotxo como arma de doble filo: recupera estamina, baja tu elocuencia). Obstáculos: dos infiltrados de la Coordinadora filibusteando (la Coordinadora prefiere que los heridos vayan a SU local, por el rédito), Eneko pidiendo contextualizar la herida en el marco general de la lucha de clases, y el punto 3 del orden del día (la txosna, seis años) bloqueando procedimentalmente todo lo demás.
**La solución ganadora** es de diseño puro: usar la palabra a favor de RESOLVER EL PUNTO 3 PRIMERO ("donativo libre con precio sugerido": la ponencia de Potxolo de hace seis años, que nadie había leído). El desbloqueo del punto 3 produce tal estupor histórico que el punto 47 (los heridos) se aprueba por aclamación en 90 segundos entre lágrimas. El pueblo celebra la resolución del punto 3 con más euforia que cualquier victoria contra la Corona. Txerra abre el programa esa noche: *"Hoy, compañeros, ha caído un imperio: el del orden del día."*

---

## 8. BILTZAR — Desarrollo: LA CAMPAÑA ELECTORAL (Acto III, ruta institucional)

La vía de victoria alternativa completa: ganar la alcaldía a Faustino sin quemar nada.

### NPCs secundarios
- **GORKA "EL MODERADO"** (ficha ampliada): su arco es el más triste del juego si se mira fijamente, y el juego invita a mirarlo: el encapuchado del 92 convertido en señor que dice "marcos de gobernanza". Guarda la capucha vieja planchada en el fondo del armario de la sede. En el final bueno de la campaña, la dona "al museo del pueblo, para que nadie tenga que volver a usarla", y es de las pocas frases del juego escritas completamente en serio.
- **LA ASESORA DE LA CAPITAL ("Spin Doctora Urrutia"):** consultora electoral carísima fichada por Biltzar. Vocabulario: "relato", "ventana de oportunidad", "demoscopia". Propone cosas como "desproblematizar el eje represivo". Nadie la entiende. Cobra por hora. Eneko pide expedientarla y por primera vez en el juego un 90% del pueblo está de acuerdo con Eneko.

### Estructura de la campaña
1. **El Censo (puerta a puerta):** minijuego de canvassing por distritos de `SistemaZonas`: cada portal un micro-diálogo con memoria — los vecinos RECUERDAN todo lo que hiciste en Actos I-II. El juego entero ha sido tu campaña sin que lo supieras. Si seguiste la ruta corrupta, este minijuego es un desfile de puertas en la cara.
2. **El Debate del Frontón:** combate dialéctico por turnos contra Faustino, mecánica "Los Dos Discursos": Faustino alterna respuestas de sus dos bolsillos (populista-pueblo / tecnócrata-Corona) y el jugador debe forzarle, con preguntas trampa, a sacar EL BOLSILLO EQUIVOCADO ante el público equivocado. Tres pifias del Percebe = su papada entra en bucle de física de pánico y el debate cae.
3. **El Dossier:** la noche antes, Maddi ofrece el archivo completo de Faustino: 23 años de comisiones, lo hunde seguro. PERO usarlo filtra también daños colaterales (la trama del párroco, la del contratista, media economía del pueblo salpicada). Elección final de Coherencia: campaña limpia (más difícil: necesitas apoyo popular real acumulado de TODA la partida) o dossier (victoria casi garantizada).
4. **Finales:**
 - **Victoria limpia:** Faustino, derrotado con elegancia inesperada, entrega la vara/cetro y dice la mejor línea de su vida: *"Veintitrés años esperando a alguien que ganara sin mancharse. Ya era hora, cagüen. La silla raspa por la izquierda, ponle un cojín."* Epílogo: presupuestos públicos EN EL TABLÓN, auditados por Maddi. La muralla se termina en seis meses. Resulta que era fácil.
 - **Victoria con dossier:** ganas. La primera mancha de chistorra aparece en tu solapa en la escena post-créditos, con el sonido exacto del sello de Faustino. Círculo cerrado.
 - **Derrota:** Faustino gana su enésimo mandato y, magnánimo, te ofrece... una concejalía. "La Llamada del Sillón", versión jugador. Aceptarla es el final secreto más oscuro del juego.

---

# PARTE III — SISTEMAS GLOBALES

## 3.1 Matriz de Reputación entre Facciones

Ganar reputación con una facción mueve a las demás. Valores de diseño inicial (por punto ganado):

| Subes con → | Sutegi | Coordin. | Askatu B. | Askapen | Morea | Komuntza | Asanblada | Biltzar |
|---|---|---|---|---|---|---|---|---|
| **Gazte Sutegi** | — | +0.2 | +0.2 | +0.3 | 0 | **-0.5** | +0.1 | -0.2 |
| **Coordinadora** | +0.2 | — | +0.3 | +0.1 | -0.1 | **-0.6** | **-0.4** | +0.3 |
| **Askatu Beharra** | +0.2 | +0.3 | — | +0.2 | +0.1 | 0 | +0.1 | +0.1 |
| **Askapen Tours** | +0.2 | +0.1 | +0.2 | — | 0 | -0.2 | +0.1 | 0 |
| **Morea Bilgunea** | +0.1 | -0.1 | +0.1 | 0 | — | -0.1 | +0.2 | 0 |
| **Komuntza** | **-0.5** | **-0.6** | 0 | -0.2 | -0.1 | — | -0.3 | **-0.8** |
| **Asanblada** | +0.1 | **-0.4** | +0.1 | +0.1 | +0.2 | -0.3 | — | -0.3 |
| **Biltzar** | -0.2 | +0.3 | +0.1 | 0 | 0 | **-0.8** | -0.3 | — |

Lecturas de diseño: Komuntza y Biltzar son mutuamente excluyentes (-0.8: la guerra fría interna); la Coordinadora y la Asanblada se repelen (verticalidad vs asamblea); Askatu Beharra es la única facción con la que NADIE penaliza (la causa de los presos es transversal: hasta Komuntza calla); Morea apenas genera efectos cruzados porque las demás la necesitan demasiado para enfadarse en público.

**Excepciones programadas:** Felisa, Errauts y la Perra Anarka están FUERA de la matriz. Su afecto no se compra, no se hereda y no salpica. Diseño con tesis.

## 3.2 Sistema de Coherencia (números)

- Stat oculta del jugador: 0–100, inicia en 50.
- **Sube:** cumplir compromisos firmados en "El Comunicado" (+5), destapar corrupción propia (+10), rechazar la Sexta Silla (+5), campaña limpia (+15), fregar tu vaso sin que nadie mire (+1, sí, está trackeado).
- **Baja:** chantajear a Sebas (-15), pactar con Unai (-5), usar el dossier (-10), prometer en asamblea y no ir (-3 por evento), cada mentira en diálogo marcada [Mentir] (-1).
- **Umbrales visibles SOLO por sus efectos** (nunca hay barra en pantalla):
 - 70+: Felisa te saluda por tu nombre. Garbiñe te enseña dónde cava. La Anarka sube su probabilidad al 3%.
 - 40–69: juego estándar.
 - <40: la mancha de chistorra empieza a renderizarse en tu ropa (1% de opacidad por punto por debajo de 40; a Coherencia 20 ya es inconfundible). Los NPCs te hablan con las animaciones faciales que usan con Faustino. Ningún texto del juego lo menciona JAMÁS.
- **Tesis mecánica:** la corrupción en este juego no es un contador, es una mancha que los demás ven antes que tú.

## 3.3 Calendario de Eventos Estacionales

- **LASTERKA (primavera):** la carrera de relevos por la Lengua Vieja: un testigo de madera cruza TODO el mapa de `SistemaZonas` durante 24h in-game sin parar, portado por relevos de todas las facciones. El único día del año en que la matriz de reputación SE DESACTIVA: todos corren juntos, Komuntza junto a Biltzar, Joni de la Guardia corre un relevo a escondidas con el casco puesto "por si acaso". Los aprovechados venden agua a x5 en el kilómetro 12 (evento: reventarles el chiringuito = +apoyo). Si el jugador corre su relevo sin caerse: buff "Aliento del Valle" todo el acto.
- **OSPA EGUNA (verano):** el día en que el pueblo pide formalmente, festivamente y con charanga que la guarnición SE VAYA. La Guardia tiene orden de no salir del fuerte; el pueblo monta la fiesta EN la puerta del fuerte. El peor día del año para Joni (misión: colarle un bocadillo de chistorra por la tronera, +amistad masiva).
- **FIESTAS DEL PUEBLO (finales de verano):** cinco días de world-state alterado: Se Busca desactivado de facto (la Guardia, desbordada), txosnas a pleno rendimiento (x3 ingresos del movimiento), el pregón de Faustino con el minijuego de tomates con físicas de impacto (puntería + elección de tomate: el cherry humilla, el de pera derriba), y el toro de fuego, que en este universo es un toro de fuego LITERAL alquilado a un mago, con todo lo que eso implica para el tejado del estanco.
- **EL DÍA DE LOS DOS MÁRMOLES (otoño):** ver Parte I. Sin minijuegos. Sin recompensas. El único evento del calendario sin recompensa mecánica, deliberadamente: ese día el juego solo pide estar. Severino, dos flores. Si el jugador se sienta con él los cuatro otoños de la partida, en el último Severino le da UNA de las flores. Los foros no van a arder con esto: van a hacer otra cosa más rara, van a quedarse callados.
- **8 DE MES MORADO (invierno):** la movilización de Morea Bilgunea: el día con la logística más impecable del año y el día en que TODAS las demás facciones descubren, anualmente, cuánto trabajo invisible las sostiene, porque Morea ese día NO les hace el trabajo (huelga de cuidados organizativos: debuff global "Ahora Te Enteras" a todas las facciones: -30% eficacia, 1 día). El debuff es pedagogía jugable.

## 3.4 Economía del Movimiento (el PIB de la lucha)

Flujo circular documentado in-game (el jugador puede destaparlo entero en el códice "Sigue la Corona"):

```
DIPUTACIÓN DE LA CORONA (subvención cultural)
  └→ BILTZAR (pacto presupuestario de Gorka)
       └→ TALLER MENDIETA (adjudicación de herrajes)
            └→ TXERRA (sobre semanal, "publicidad que no existe")
                 └→ PROPAGANDA (recluta manifestantes)
                      └→ MANIFESTACIÓN (desgasta a la Corona)
                           └→ LA CORONA sube el presupuesto "de pacificación cultural"
                                └→ DIPUTACIÓN DE LA CORONA (vuelta a empezar, +12%)
```

Paralelo: la **economía txosna** (la de verdad): donativo libre de la Asanblada + barra de fiestas + turnos de trabajo voluntario = financia gaztetxe, pancartas de Olatz, carretas de las familias y los táperes de Begoña. Dos economías: la de los sobres y la de los vasos fregados. El mapa estratégico las muestra por separado y la partida entera es, en el fondo, la pregunta de cuál de las dos alimentas.

---

# PARTE IV — ESPECIFICACIÓN TÉCNICA (para implementación en el proyecto)

Ganchos concretos a la arquitectura existente (capas WORLD/GAMEPLAY, comunicación vía EventBus/ServiceLocator según CLAUDE.md):

```csharp
// GAMEPLAY — nuevo servicio, registrado en ServiceLocator
public interface IFactionService {
    float GetReputation(FactionId f);
    void ModifyReputation(FactionId f, float delta);   // aplica matriz cruzada internamente
    float Coherencia { get; }                           // 0-100, oculta
    void ModifyCoherencia(float delta, CoherenciaReason reason);
}

public enum FactionId {
    GazteSutegi, Coordinadora, AskatuBeharra, AskapenTours,
    MoreaBilgunea, Komuntza, Asanblada, Biltzar,
    // post-cisma (se activan en runtime si "El Congreso" acaba en escisión):
    KomuntzaML, KomuntzaReconstituida
}

// EventBus — nuevos eventos (capa GAMEPLAY publica, UI/Audio suscribe)
public struct FactionReputationChangedEvent { public FactionId faction; public float oldValue, newValue; }
public struct CoherenciaThresholdEvent { public int threshold; public bool rising; }  // dispara shader de chistorra
public struct SeasonalEventStartedEvent { public SeasonalEventId id; }                 // Lasterka, OspaEguna...
public struct ManchaChistorraEvent { public float opacity; }                           // consumido por el material del jugador

// ScriptableObject por facción (Assets/Data/Factions/)
[CreateAssetMenu(menuName = "Altsasu/FactionDefinition")]
public class FactionDefinition : ScriptableObject {
    public FactionId id;
    public string nombreMostrado;
    [TextArea] public string descripcionCodice;
    public float[] matrizCruzada;            // 8-10 floats, fila de la matriz 3.1
    public MissionChain cadenaPrincipal;     // SO anidado: actos, condiciones, finales
    public SeasonalEventId[] eventosPropios;
    public UnitStats unidadesManifestacion;  // consumido por SistemaManifestacion
}
```

Notas de integración: la matriz vive en datos (SO), no en código; `SistemaApoyoPopular` consume `FactionReputationChangedEvent` con peso por distrito de `SistemaZonas`; la mancha de chistorra es un parámetro de material HDRP (`_ChistorraOpacity`, mapa de máscara en la solapa de cada outfit, actualizado vía `ManchaChistorraEvent` — sin string concat, sin Update, solo evento). La Perra Anarka es un NPC de capa ENTITIES con un random diario sembrado por `AltsasuCore` para que NO sea farmeable recargando partida. Prioridad de implementación sugerida: IFactionService + matriz → Coherencia + shader → "Las Cuentas de Sebas" (vertical slice narrativo) → calendario estacional.

---

*Vol. 2 cierra donde abrió el Vol. 1: el conflicto de verdad no es Corona contra pueblo. Es la economía de los sobres contra la economía de los vasos fregados. Todo lo demás —los catorce logos, las tesis, los jamones colgados, los dos discursos del Percebe— son las distintas maneras que tiene la gente de no mirar eso de frente. El juego sí mira. Por eso es TMEO: porque se ríe mirando.*
