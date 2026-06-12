# FABLE V: ALTSASU INSURGENTE
## Biblia de Personajes — Edición TMEO (Realismo Sucio AAA+++)

> Documento de diseño narrativo. Tono: sátira underground, feísmo ilustrado, humor negro costumbrista.
> Nada es sagrado: se parodia por igual la épica revolucionaria de barra de bar y la pompa de la Corona ocupante.
> Ganchos técnicos a sistemas ya existentes en el proyecto: `IWantedSystem`, `SistemaApoyoPopular`, `SistemaManifestacion`, `IEconomyService`, `SistemaZonas`.

---

## 1. KOLDO "EL GARRAFÓN" — El Tabernero Neutral, Ex-Preso del Gremio de Ladrones

**Arquetipo rancio:** El superviviente cínico que odia a todos por igual pero cobra a todos por igual, que es lo importante.

### Descripción visual (UE5 aplicado a la decadencia humana)
Sesenta y dos años, cuerpo de barril de sidra con patas. Nariz topográfica: cada cráter es una cosecha de vino peleón documentable por carbono-14. Delantal que fue blanco durante el reinado anterior, ahora con estratigrafía de grasa de chistorra que el shader de subsurface scattering renderiza con un brillo casi orgánico. Tatuaje carcelario del Gremio de Ladrones en el antebrazo, medio borrado con magia barata, que parpadea como un neón fundido cuando miente (es decir, parpadea siempre). Lleva una cota de malla cortada por la cintura a modo de faja lumbar "porque la espalda no me la paga ningún bando, chaval".

### Perfil político y la Gran Hipocresía
**Discurso público:** "Yo aquí no me meto. Esta taberna es territorio neutral, como Suiza pero con más humedad."
**El secreto cochambroso:** Tiene DOS barriles. El de la izquierda, "Sidra del Pueblo Insumiso", se lo vende a los insurgentes con un 40% de recargo patriótico. El del barril de la derecha sale la MISMA sidra, reetiquetada "Reserva Real de Albion", para la Guardia Real con un 60% de recargo imperial. Es el mismo garrafón aguado con agua del Arakil río abajo del lavadero. Su neutralidad es la operación comercial más rentable del valle. Además le pasa cotilleos a ambos bandos, pero siempre con una semana de retraso para que nadie pueda actuar y le espanten la clientela.

### Utilidad mecánica
- **Ventaja de aliado:** La taberna funciona como *hub* de información: desbloquea el tablón de rumores (misiones secundarias dinámicas según `SistemaZonas`). Permite **blanquear notoriedad**: pagando una ronda general, reduce 1 estrella del `IWantedSystem` ("aquí no ha visto nadie nada, estaban todos mirando el fondo del vaso"). Acceso al sótano: piso franco con cooldown de respawn.
- **Castigo de enemigo:** Te declara el "veto del taxista": precios x3 en consumibles en todo el pueblo (los taberneros son un gremio, y el gremio habla). Además filtra tu posición al bando contrario CON SOLO TRES DÍAS de retraso en vez de siete, que en su escala moral es una declaración de guerra.

### Mini-guion de encuentro aleatorio
> *Interior taberna. Huele a fritanga y a derrota. El JUGADOR entra con la ropa llena de hollín de la última barricada.*
>
> **KOLDO:** ¿Qué va a ser? ¿Sidra del Pueblo o Reserva Real?
> **JUGADOR:** ¿Cuál es la diferencia?
> **KOLDO:** Dos coronas y tu conciencia. La resaca es la misma, eso te lo garantizo yo personalmente.
> **JUGADOR:** ¿Y tú de qué lado estás, Koldo?
> **KOLDO:** *(secando un vaso con un trapo que ensucia más de lo que limpia)* Yo estuve siete años en las mazmorras del Gremio, majo. ¿Sabes lo que aprendí? Que las ideologías van y vienen, pero la sed... *(golpea el barril con cariño)* ...la sed es estructural.

---

## 2. JONI RUIPÉREZ — El Antidisturbios Destinado a la Fuerza

**Arquetipo rancio:** Jonathan "Joni" Ruipérez, 23 años, recluta de Albacete del Páramo (provincia profunda de Albion), que está cagado de miedo y solo quiere volver a su pueblo a tiempo para las fiestas.

### Descripción visual
Armadura antidisturbios de la Guardia Real dos tallas grande ("es lo que había en el almacén, ¿vale?"), con el escudo de la Corona repintado encima de un escudo anterior de otra guerra que también perdieron. El casco le baila y se lo sujeta con cinta americana, material que en este juego tiene físicas propias y categoría de recurso estratégico. Debajo de la coraza asoma una camiseta de un grupo de heavy de su pueblo, "Estiércol Eterno", gira del 1247. Acné de estrés renderizado poro a poro. Le tiembla la lanza eléctrica reglamentaria con un sistema de inverse kinematics dedicado exclusivamente a transmitir pánico.

### Perfil político y la Gran Hipocresía
**Discurso público:** "¡Alto en nombre de la Corona! ¡Dispersaos o... o lo digo otra vez más alto!"
**El secreto cochambroso:** Lleva tres meses vendiendo información de los turnos de patrulla a cambio de chistorra y de que la abuela de un insurgente le cosa los calcetines, porque la intendencia real no repone desde hace dos inviernos. No es ideología: es logística sentimental. Escribe cartas a su madre diciéndole que está "en un destino tranquilo de oficinas". Su sueño no es ganar la guerra: es que le concedan el traslado a la garita del faro, donde no hay nadie a quien reprimir salvo gaviotas.

### Utilidad mecánica
- **Ventaja de aliado:** Topo dentro de la Guardia: revela en el mapa estratégico las rutas de patrulla de su compañía 24h antes (overlay en `SistemaZonas`). Una vez por capítulo puede "perder" las llaves del calabozo (rescate de aliados capturados sin combate). Si la amistad llega al máximo, deserta y se une a tu facción como unidad con la habilidad pasiva *"Conozco el manual"*: tus manifestantes ganan +30% de resistencia a cargas policiales porque Joni les chiva por dónde van a cargar.
- **Castigo de enemigo:** El miedo le vuelve impredecible: si lo humillas públicamente, entra en pánico y pide refuerzos POR TODO, escalando cualquier encuentro callejero un nivel de Se Busca extra. Un antidisturbios valiente es peligroso; uno acojonado es peor.

### Mini-guion de encuentro aleatorio
> *Callejón. JONI está en su puesto, solo, comiéndose un bocadillo a escondidas dentro del casco.*
>
> **JONI:** ¡Alto! ¡Quién va! *(se atraganta)* Perdón. ¡Quién va, he dicho!
> **JUGADOR:** Tranquilo, hombre. ¿Eso es chistorra?
> **JONI:** *(bajando la voz y la lanza a la vez)* De la abuela Felisa. Oye... tú eres de los de las barricadas, ¿no? No, no me lo digas. Si no me lo dices, yo no tengo que ponerlo en el parte, y si no lo pongo en el parte, no me hacen quedarme a hacer horas. ¿Lo pillas?
> **JUGADOR:** ¿Y si te ordenan cargar contra nosotros mañana?
> **JONI:** *(mirada de cordero al matadero)* Mañana tengo cita con el físico-mago por lo de la cervical. Me la pedí en cuanto vi el calendario de manifestaciones. En Albacete del Páramo seremos de pueblo, pero tontos no.

---

## 3. AINHOA-BELTZANE VON ETXEBERRIA — La Activista Borrokilla de Salón y Alta Cuna

**Arquetipo rancio:** Hija tercera del Conde de Etxeberria (proveedor oficial de adoquín de la Corona), 26 años, revolucionaria de viernes a domingo, condesa de lunes a jueves.

### Descripción visual
Estética insurgente de catálogo: pañuelo táctico de seda élfica (edición limitada, 200 coronas), sudadera "deliberadamente desgastada" por un sastre-mago de la capital que cobra por agujero, y botas de monte de gama altísima SIN UNA SOLA MANCHA DE BARRO, detalle que el motor gráfico resalta con un shader de limpieza ofensiva en contraste con el feísmo general. Tatuajes rúnicos de "magia de protesta" de alta gama: donde un insurgente de verdad lleva runas de grafiti que se infectan, ella lleva caligrafía dracónica con denominación de origen. Vuelve cada domingo por la noche al castillo familiar en un carruaje con calefacción y suspensión encantada, aparcado siempre a tres calles "por discreción".

### Perfil político y la Gran Hipocresía
**Discurso público:** "El pueblo unido jamás será vencido. Yo estoy aquí, en el barro, con las de abajo."
**El secreto cochambroso:** Su "magia de combate popular" la paga papá: cada bola de fuego que lanza en una manifestación cuesta más que el sueldo anual de los manifestantes a los que cubre. La muralla contra la que protesta los sábados la construye la cantera de su padre de lunes a viernes, y la familia cobra de ambos lados: adoquín para la Corona, y los adoquines sueltos que los manifestantes arrancan y tiran... los repone también la cantera Etxeberria. El ciclo del adoquín es el modelo de negocio familiar más redondo de Albion. Ella lo sabe. Le da una vergüenza que gestiona con un mago-terapeuta carísimo.

### Utilidad mecánica
- **Ventaja de aliada:** Artillería mágica de gama alta en manifestaciones (`SistemaManifestacion`): escudos arcanos que anulan una carga policial completa una vez por evento. Acceso a la red social de la nobleza: desbloquea misiones de infiltración en galas de la Corona (robar sellos, chantajear condes). Financiación culposa: dona dinero negro familiar al mapa estratégico cuando su medidor de remordimiento está alto.
- **Castigo de enemiga:** Si la desenmascaras públicamente sin pruebas, su familia te entierra en pleitos: jueces comprados te suben el Se Busca permanente +1. Y pierdes su escudo arcano, que descubrirás que estaba sosteniendo más manifestaciones de las que tu orgullo quiere admitir.

### Mini-guion de encuentro aleatorio
> *Plaza, post-manifestación. AINHOA-BELTZANE se retoca el pañuelo táctico frente al escaparate de la ferretería.*
>
> **AINHOA-BELTZANE:** ¡Compañere! Brutal la convocatoria de hoy, ¿eh? Yo he estado en primera línea. Bueno, primera línea y media, que el humo me da alergia y el alergólogo-mago me ha dicho que...
> **JUGADOR:** Te he visto lanzar un escudo arcano de los de quinientas coronas.
> **AINHOA-BELTZANE:** Es... expropiado. Expropiado al patriarcado.
> **JUGADOR:** Lleva el sello de tu padre.
> **AINHOA-BELTZANE:** *(pausa larga, mirada al horizonte de quien ensaya esto en el espejo)* La contradicción también es una trinchera, ¿sabes? Lo leí en un panfleto. Bueno, lo escribí yo en un panfleto. Me lo imprimió el escriba de papá. ...¿A que no sabes dónde dan kalimotxo de garrafón? Necesito sentir cosas reales.

---

## 4. SEVERINO "MATUSALÉN" — El Viejo de la Garrota del Banco de la Plaza

**Arquetipo rancio:** Anciano de edad indeterminada entre 89 y 400 años (los registros parroquiales ardieron en una guerra que solo él recuerda y posiblemente empezó él). Conspiranoico titular del banco de piedra de Herriko Plaza, del que no se ha movido desde hace décadas salvo para mear, y hay testigos que lo dudan.

### Descripción visual
Boina fosilizada que ya es parte del cráneo (los técnicos de UE5 la modelaron como hueso). Tres dientes supervivientes, cada uno apuntando a una facción distinta. Chaqueta de pana con parches de pana sobre la pana original, generando un material compuesto que detiene flechas (probado). La garrota: madera de roble del monte comunal, pulida por sesenta años de uso, con balística de proyectil de última generación: Severino tira piedras con efecto Magnus, cálculo de viento y caída parabólica perfecta. Nadie le ha enseñado. Es talento natural destilado en décadas de aburrimiento municipal.

### Perfil político y la Gran Hipocresía
**Discurso público:** "Esto lo mueven los elfos terratenientes desde la sombra. Y el del estanco, que es primo de un elfo. Lo sé porque lo sé."
**El secreto cochambroso:** TIENE RAZÓN EN CASI TODO pero por los motivos equivocados. Sus desvaríos sobre "túneles bajo la plaza" (ciertos: contrabando), "el alcalde cobra de la muralla" (cierto) y "el agua del río baja encantada" (cierto: un mago de la Corona la trata río arriba) son verificables. Pero los mezcla con que los pájaros son espías a cuerda de la Corona, así que nadie le hace caso. Su gran hipocresía: cobra DOS pensiones, una de veterano de la Corona y otra de veterano de la insurgencia anterior, porque sirvió en ambos bandos "para comparar". Lleva 40 años defraudando a los dos imperios a la vez desde un banco de piedra. Es, técnicamente, el mayor estratega financiero del valle.

### Utilidad mecánica
- **Ventaja de aliado:** El "Oráculo del Banco": una vez al día, suelta un desvarío que el juego marca con un 70% de probabilidad de ser inteligencia estratégica real (revela caches de armas, túneles, traidores en el mapa de distritos). Habilidad de apoyo *"Pedrada Censora"*: durante manifestaciones, snipea con la garrota-honda al oficial enemigo que elijas, aturdiéndolo (proyectil con físicas completas, derriba cascos a 80 metros).
- **Castigo de enemigo:** Te incluye en la conspiración. A partir de ese momento, cada desvarío diario te menciona A TI, y como acierta el 70% de las veces, el pueblo empieza a creérselo: -1 nivel de `SistemaApoyoPopular` por semana hasta que te reconcilies trayéndole tabaco de pipa del bueno.

### Mini-guion de encuentro aleatorio
> *Banco de la plaza. SEVERINO mira fijamente a una paloma con desconfianza profesional.*
>
> **SEVERINO:** Tú. Sí, tú. ¿Sabes por qué la fuente lleva tres semanas sin agua?
> **JUGADOR:** ¿Avería?
> **SEVERINO:** *(risa flemática de tres pisos)* "Avería", dice. La desviaron los elfos para regar las viñas del Conde. Lo vi yo. A las cuatro de la mañana.
> **JUGADOR:** ¿Y qué hacía usted despierto a las cuatro de la mañana?
> **SEVERINO:** Vigilar la fuente. *(pausa, se inclina, baja la voz)* Escucha, chaval: bajo el ayuntamiento hay un túnel que llega hasta la vieja herrería. Por ahí entra el vino que tu tabernero llama "Reserva Real". Y por ahí se puede entrar el día del discurso del Alcalde, si alguien tuviera, yo qué sé... motivos.
> **JUGADOR:** ¿Eso es información o desvarío?
> **SEVERINO:** *(volviendo a mirar a la paloma)* Eso lo decides tú cuando estés dentro del túnel. A mí tráeme tabaco.

---

## 5. FAUSTINO ZELAIETA "EL PERCEBE" — El Alcalde Chaquetero y Comisionista

**Arquetipo rancio:** Alcalde desde tiempos geológicos. Lo llaman El Percebe porque está pegado al sillón municipal con una fuerza que la ciencia no explica y porque, como el percebe, cuesta carísimo y alimenta poco.

### Descripción visual
Traje de sastre de la capital, de una lana tan cara que tiene su propio LOD, con una constelación permanente de manchas de chistorra en la solapa que la dirección de arte mantiene en TODOS los outfits: cambia el traje, las manchas persisten, como un estigma. Papada en tres niveles con física de soft-body independiente. Sonrisa de inauguración: 32 dientes de marfil mágico financiados con la partida de "reparación de caminos" de hace nueve años. La vara de mando es en realidad un cetro arcano robado del archivo diocesano que tiene un único hechizo: *Recalificación* — convierte suelo comunal en "urbanizable" con un destello dorado y un olor a azufre que el alcalde tapa con colonia.

### Perfil político y la Gran Hipocresía
**Discurso público:** "Consenso, paz social y bienestar para todos los vecinos y vecinas de esta noble villa de Albion."
**El secreto cochambroso:** Su única ideología es el 12% de comisión. La muralla del pueblo lleva en obras 23 años porque terminarla mataría a la gallina de los huevos de oro: cada bando la derriba por un lado mientras él la reconstruye por el otro, cobrando de la Corona por "defensa estratégica" y de la insurgencia (vía testaferros) por "no mirar dónde ponen los túneles". Tiene preparados DOS discursos para el día que caiga el pueblo: uno de liberador y otro de leal servidor de la Corona, en los dos bolsillos interiores de la chaqueta. A veces, en los plenos, saca el que no toca y improvisa.

### Utilidad mecánica
- **Ventaja de aliado:** Desvío de fondos públicos al mapa estratégico: convierte presupuesto municipal en munición y materiales para barricadas (botón "Modificación presupuestaria" en la interfaz de estrategia, con sello y todo). Limpieza de Se Busca estilo GTA: por un precio, le endosa tus delitos a un cabeza de turco (elige bien: si eliges a alguien querido del pueblo, pierdes `SistemaApoyoPopular`). Acceso al cetro de Recalificación: desbloquea construcción de estructuras de facción en suelo "legalmente flexible".
- **Castigo de enemigo:** Burocracia ofensiva: inspectores de sanidad medievales clausuran tus pisos francos uno a uno ("falta el sello del gremio en esta ballesta"). Te corta el agua, la luz mágica y el padrón: sin padrón no votas en los eventos de distrito y tus mercaderes aliados pierden licencia.

### Mini-guion de encuentro aleatorio
> *Despacho municipal. Olor a puro y a expediente quemado. FAUSTINO firma algo sin leerlo.*
>
> **FAUSTINO:** ¡Pasa, pasa! ¿Vienes por lo de la denuncia, por lo de la subvención o por lo del cadáver? Es por organizarme.
> **JUGADOR:** Vengo a hablar de la muralla.
> **FAUSTINO:** *(se ilumina como un cetro recalificador)* La muralla. Mi gran obra. ¿Sabes que llevamos 23 años de avances continuos?
> **JUGADOR:** Está más derruida que cuando empezasteis.
> **FAUSTINO:** ¡Avances CONTINUOS, he dicho! Si estuviera terminada, ¿qué avanzaríamos? Hay que pensar a largo plazo, hijo. Yo en esto soy muy de izquierdas: reparto. *(baja la voz)* Y muy de derechas: cobro. El equilibrio, que le llaman. ¿Un puro? Son de incautación.

---

## 6. TXERRA "MADRUGADA" — El Bardo de la Radio Clandestina "HALA ALTSASU"

**Arquetipo rancio:** Locutor antisistema, conspiranoico colegiado y fumador de tabaco de contrabando en cantidades que violan tratados internacionales. Emite cada noche desde un sótano con humedades sintientes (la mancha de la pared norte tiene nombre: "La Censora").

### Descripción visual
Cuarenta y muchos años de los cuales treinta son de radio nocturna: piel de pergamino ahumado, ojeras con su propio ambient occlusion, voz rota renderizada con un filtro de audio exclusivo ("Gravilla Premium"). Cascos de cuero medievales conectados por cobre robado a una Piedra de Resonancia rancia que escupe la señal a los altavoces de piedra del pueblo. Sudadera con capucha de un gris arqueológico, mitones de lana con quemaduras de liar tabaco sobre los apuntes del programa. El estudio: cajas de fruta como mobiliario, un cartel de "PROHIBIDO FUMAR" usado de cenicero, y tres velas porque "la luz mágica de la Corona te escucha, eso es sabido".

### Perfil político y la Gran Hipocresía
**Discurso público:** "¡Buenas noches, pueblo insumiso! Aquí Hala Altsasu, la única voz que no se vende. ¡Contrainformación y verdad, sin amos y sin publicidad!"
**El secreto cochambroso:** El programa "sin publicidad" sobrevive gracias a la publicidad encubierta más descarada del valle: el Taller de Herraduras Mendieta (el negocio más agresivamente capitalista de la comarca, precios abusivos, cero escrúpulos) le paga un sobre semanal a cambio de menciones "espontáneas": *"...y os digo una cosa, compañeros, una revolución no se hace con los caballos cojos. Herrad bien. Herrad donde sabéis."* También denuncia el contrabando de tabaco en las ondas mientras se fuma el género decomisado que le pasa, precisamente, su primo contrabandista. Su ética tiene la consistencia del humo que la rodea.

### Utilidad mecánica
- **Ventaja de aliado:** Guerra de información: su propaganda nocturna aumenta +200% la velocidad de reclutamiento de manifestantes en el mapa estratégico (`SistemaManifestacion` + `SistemaApoyoPopular`). Desbloquea misiones de sabotaje informativo: hackear (a martillazo rúnico) los tablones de anuncios del Rey y sustituir los edictos por coplas satíricas, bajando la moral de las guarniciones por distrito. Una vez por capítulo: "El Programa Especial", evento que voltea la opinión pública de un distrito entero.
- **Castigo de enemigo:** Te dedica el programa de la noche. Con nombre, apellidos y una sintonía compuesta para ti. Efectos: la moral de tu facción se hunde, los vecinos te miran con un desprecio renderizado en 4K, los tenderos te cobran el triple y los niños del pueblo te siguen cantando tu sintonía por la calle (audio posicional, no se puede desactivar, el equipo de sonido está muy orgulloso).

### Mini-guion de encuentro aleatorio
> *Sótano de la radio. TXERRA hace el gesto de "silencio" señalando la Piedra de Resonancia: están EN DIRECTO.*
>
> **TXERRA:** *(al micro, voz de gravilla)* ...y recordad: el toque de queda no es por vuestra seguridad, es por su miedo. Pausa musical. *(corta la emisión, se gira)* ¿Tú quién coño eres y cómo has encontrado el zulo?
> **JUGADOR:** Me manda Severino. Dice que los pájaros le han dicho dónde emites.
> **TXERRA:** Ese viejo acojona. Lleva razón en todo, ¿eh? Por eso no le saco en el programa, me haría sombra. *(lía un cigarro sobre el guion)* ¿Qué quieres, propaganda? La propaganda es un arte. Y el arte, amigo mío...
> **JUGADOR:** ...no es gratis.
> **TXERRA:** No es gratis. *(sonrisa amarilla)* Pero para la causa hago precio. Y si me traes lo que te voy a pedir del taller de Mendieta, hago dos precios. No preguntes. Periodismo de investigación, lo llaman.

---

## ANEXO: MECÁNICAS TRANSVERSALES ESTILO TMEO

### El Medidor de Privilegio / "Kalea"
Stat oculta que evalúa tu outfit en tiempo real. Si vistes equipo de gama Corona (sedas, magia de catálogo, botas sin barro estilo Ainhoa-Beltzane) en los barrios populares, los vecinos te tiran cubos de agua de fregar desde los balcones — físicas de fluidos completas, mancha persistente en la ropa, debuff temporal de carisma "Pringao de la Capital". A la inversa: entrar en el barrio noble vestido de barricada activa patrullas de la Guardia con +50% de agresividad.

### Minijuego: "El Kalimotxo de la Victoria"
Alquimia rancia en la taberna de Koldo. Combina vino de tetrabrik (calidad: "técnicamente vino") con cola de contrabando en proporciones que el jugador calibra a pulso. Resultado bien calibrado: buff *"Euforia de Barrikada"* (+fuerza, +carisma, -puntería). Mal calibrado: pantalla borrosa, controles invertidos 30 segundos y una animación de arcada que Koldo te cobra como "limpieza de local". El kalimotxo perfecto (proporción áurea) es un logro oculto: *"Alquimista del Pueblo"*.

### Sinergia de personajes (mapa estratégico)
- Koldo + Joni: el tabernero "ficha" al antidisturbios como cliente → rutas de patrulla con descuento.
- Severino + Txerra: los desvaríos del viejo verificados al 70% se convierten en exclusivas del programa → doble efecto de moral.
- Faustino + Ainhoa-Beltzane: la trama del adoquín (la cantera del Conde + las comisiones del Percebe) es la cadena de misiones de investigación del Acto II: *"El Ciclo del Adoquín"*.
