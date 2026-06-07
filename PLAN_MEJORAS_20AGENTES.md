# Plan Maestro de Mejoras — 20 agentes (Altsasu Manifa)

> 14 subsistemas auditados. Hallazgos = PISTAS a verificar antes de aplicar.

## 🔴 Bugs gravedad ALTA
- **[Terreno LIDAR]** Incoherencia critica de sistema de coordenadas: ProcesadorNubePuntos.cs trata dtm_alsasua_5m.asc como grados (EPSG:4326) con M_PER_DEG_LON=76400 y LAT0/LON0 propios, mientras GeneradorTerrenoUltraPreciso.SampleASCUTM y S
- **[Terreno LIDAR]** Desajuste de rango vertical: las normalizaciones (altM-Z_MIN)/terY asumen terY = rango real (~57m) pero la escena/documentacion usan size.y~900m. Si terY=900, todo el relieve queda comprimido al ~6% y el terreno se ve ca
- **[Terreno LIDAR]** Bug de coste/hitch: SistemaTerreno.Update relanza PintarAlphamap8Biomas() (recalculo y SetAlphamaps de TODO el alphamap, incluido recomputo de mascaras de rios/bosques/carreteras con bucles O(pixeles*segmentos)) cada vez
- **[Subsistema Edificios]** LODGroup mal configurado: los thresholds 1f-40f/700f etc. tratan metros como screen-relative-height. En la practica LOD0 ocupa casi todo el rango y los impostores LOD3 (transicion a 0f) practicamente nunca se activan a l
- **[Subsistema Edificios]** Explosion de draw calls: ConstruirEdificio (ruta real desde SistemaZonas) crea cientos de GameObject.CreatePrimitive(Cube) por edificio (cada ventana = vierteaguas+marco+vidrio+division+dintel, cada balcon = N barras...)
- **[Subsistema Edificios]** Material unico por edificio: el cache de MaterialFachadaPorArquetipo se indexa por id, creando un Material distinto por edificio. Rompe GPU instancing y static batching (pese a enableInstancing=true) y dispara el uso de 
- **[Subsistema Edificios]** Ventanas con z-fighting y sin vidrio en el pipeline preciso: GenerarHuecoVentana anade un quad coplanar a la pared en el mismo submesh/material; nunca se aplica material de vidrio ni inset. Visualmente las ventanas parpa
- **[Vegetacion]** DevolverArbol siempre se invoca sin el arg 'especie' (AlsasuaTreeStreamer.cs ~701), por lo que los pools por especie (Roble/Pino/Ribera) nunca reciben de vuelta sus instancias: se vacian permanentemente y el streamer cae
- **[Vegetacion]** Duplicacion de arboles: PosicionadorPrecisionUrbana coloca arboles LIDAR estaticos (<600m del centro) y AlsasuaTreeStreamer hace streaming del MISMO lidar_trees.json en 30-800m sin excluir la zona ya cubierta -> arboles 
- **[Clima y atmósfera]** La reflexión busca las propiedades "HoraActual", "hora" y "Hour", pero SistemaAtmosfera expone la hora como "HoraDelDia". El lookup siempre devuelve null, _propHoraActual queda null y todo el ciclo HDRP cae al fallback d
- **[Clima y atmósfera]** SistemaClima controla la niebla por RenderSettings.fogDensity/fogColor (sistema legacy/built-in). En HDRP la niebla la gobierna el VolumeComponent Fog; RenderSettings.fog es inerte. Por tanto los 6 climas NO modifican la
- **[Clima y atmósfera]** Tres sistemas escriben la MISMA Light direccional cada frame con modelos incompatibles: SistemaAtmosfera usa solDireccional.intensity=80000 (no Lux HDRP) y azimut fijo 30° cada frame; SistemaVolumenHDRP usa SetIntensity 
- **[Clima y atmósfera]** Physics.gravity = new Vector3(fuerzaViento*0.1f, -9.81f, 0) se asigna CADA frame y de forma global. (a) Mete gravedad lateral global permanente que afecta a TODO rigidbody (personajes, vehículos, ragdolls), no sólo proye
- **[NPC IA y agenda]** Slots de SistemaDeteccionIA nunca se liberan: Registrar() solo hace _slotSiguiente++ y no hay Liberar(). Cuando mueren/respawnean policías se agotan los 32 slots; los siguientes reciben -1 y JugadorEnVision() devuelve si
- **[Policía y Wanted]** Agotamiento permanente de slots de detección: SistemaDeteccionIA._slotSiguiente solo incrementa y nunca se libera. PoliciaForalIA no libera su _slotDeteccion al morir ni sobreescribe OnDestroy. Tras 32 policías acumulado
- **[Policía y Wanted]** Máscara de obstáculos por defecto incorrecta: capasObstaculo = ~0 incluye al propio policía, a otros NPC y a triggers/decoración. La única exclusión aplicada es Player. Resultado: falsos negativos de LOS (autobloqueo y b
- **[Trafico y vehiculos]** ConfiguradorTrafico.cs esta roto: GetNestedType('Carril') y GetField('carriles') sobre SistemaTrafico devuelven null (esos miembros no existen en la clase actual). La herramienta hace early-return con LogError 'No se enc
- **[Trafico y vehiculos]** SistemaTrafico no produce trafico en movimiento: SpawnCochesIniciales() fuerza rb.isKinematic=true en cada coche y nunca añade VehiculoNPC ni waypoints. Son props estaticos. El comentario en IntegradorAssets.cs:231 'traf
- **[Subsistema Manifestacion]** La salud del jugador en la manifestacion es inalcanzable de reducir: JuegoManifestacion.RecibirDaño(float) no tiene ningun llamador externo (la GC/PoliciaForalIA dana ControladorJugador.RecibirDano, otra salud distinta).
- **[Subsistema Manifestacion]** La Guardia Civil spawneada no hace nada: en SpawnGuardiaCivil se activa el NavMeshAgent pero nunca se le da SetDestination ni se le adjunta una IA de combate/persecucion. Los agentes quedan inmoviles, vaciando de conteni
- **[Combate y armas]** Munición infinita: SistemaArmasExtendido.CambiarArma() reasigna _municion = MUNICION_INICIAL[tipo] en cada cambio de arma, así que alternar arma con la rueda/teclas recarga gratis tirachinas, molotov y bomba lapa. Rompe 
- **[Audio]** RegistrarFuente() en SistemaReverbZonas (linea 163) no se llama desde ningun script del proyecto. El sistema de oclusion completo (_fuentesRegistradas, ActualizarOclusion en linea 139, _maskOclusion, _timerOclusion) es c
- **[Audio]** VolMaster se aplica dos veces: AudioListener.volume = VolMaster en SistemaOpciones.AplicarAudio (linea 109) y de nuevo *SistemaOpciones.VolMaster en VolumenEfectivo() (linea 481). El volumen master efectivo queda elevado
- **[Audio]** Los AudioSources del pool (CrearPool) y los persistentes nunca fijan minDistance/maxDistance/rolloffMode/dopplerLevel. Play() solo toca spatialBlend, volume, position y pitch. En escala de mundo abierto el rolloff logari
- **[HUD UI y menus]** Indicador de direccion de dano siempre apunta al origen del mundo: HUDCanvas.OnDano (linea 713) hace MostrarDano(Vector3.zero) ignorando de donde viene el golpe; el arco rojo nunca refleja al atacante real y toda la trig
- **[HUD UI y menus]** Minimapa renderiza la escena completa (cullingMask = ~0) cada frame a una RenderTexture en HDRP de mundo abierto (HUDCanvas linea 560 + ActualizarMinimap en Update sin throttle). Es un segundo render full-scene por frame
- **[Subsistema "Mundo Vivo nuevo"]** El tren no tiene colision: SistemaTren.ConstruirUnidad() hace Destroy(...GetComponent<Collider>()) en cuerpo, ventanas y bogies, y morro; la raiz _tren tampoco tiene collider. El convoy atraviesa jugador, peatones y vehi

## ⚡ Mejoras de IMPACTO ALTO
- **[Terreno LIDAR]** Unificar el sistema de coordenadas de ProcesadorNubePuntos con el resto del pipeline (UTM metros) (esf medio)
- **[Terreno LIDAR]** Resolver el conflicto de rango de altitud (terY ~900m vs Z_MAX-Z_MIN=57.26m) (esf medio)
- **[Terreno LIDAR]** Quitar el repintado completo del alphamap en SistemaTerreno.Update (hitch por nieve estacional) (esf medio)
- **[Subsistema Edificios]** Unificar en un solo pipeline y eliminar la ruta de primitivos sueltos (esf bajo)
- **[Subsistema Edificios]** Combinar los detalles de fachada en un mesh por edificio (no cubos sueltos) (esf alto)
- **[Subsistema Edificios]** Dejar de crear un Material unico por edificio (cache por id rompe instancing) (esf bajo)
- **[Subsistema Edificios]** Corregir las transiciones del LODGroup (formula erronea) (esf medio)
- **[Subsistema Edificios]** Vidrio real con material e inset, eliminando z-fighting de ventanas (esf medio)
- **[Vegetacion]** Pasar la especie al devolver arboles al pool (esf bajo)
- **[Vegetacion]** Evitar duplicado de arboles Posicionador vs Streamer (esf bajo)
- **[Vegetacion]** Corregir longitud de ocupacion en JobComprobarOcupacion (esf bajo)
- **[Vegetacion]** Anadir variacion de escala/tinte y LOD crossfade al streaming para feel AAA (esf medio)
- **[Clima y atmósfera]** Unificar autoridad sobre el sol y la niebla en SistemaVolumenHDRP (esf alto)
- **[Clima y atmósfera]** Corregir el puente de hora real (reflexión) (esf bajo)
- **[Clima y atmósfera]** Mover la niebla por clima a HDRP Fog y borrar las escrituras legacy muertas (esf medio)
- **[Clima y atmósfera]** Arreglar el viento físico para que no sea gravedad lateral global (esf medio)
- **[NPC IA y agenda]** Resolver el conflicto Agenda vs FSM civil: dar prioridad a la agenda como capa de destino (esf medio)
- **[NPC IA y agenda]** Agenda data-driven y con histéresis horaria en vez de FindObjectsByType cada minuto (esf medio)
- **[NPC IA y agenda]** Escalonar/cullear el Update de IA por distancia al jugador (LOD de comportamiento) (esf medio)
- **[NPC IA y agenda]** Reaprovechar slots de SistemaDeteccionIA y permitir crecer más allá de 32 policías (esf medio)
- **[Policía y Wanted]** Liberar el slot de SistemaDeteccionIA al morir/destruir el policía (esf bajo)
- **[Policía y Wanted]** Conectar el nivel de búsqueda (estrellas) con el comportamiento del policía a pie (esf medio)
- **[Policía y Wanted]** Corregir la máscara de obstáculos por defecto (capasObstaculo = ~0) (esf bajo)
- **[Policía y Wanted]** Spawnear PoliciaForalIA reales desde el wanted system (no solo coches vacíos) (esf medio)
- **[Trafico y vehiculos]** Reparar la integracion: SistemaTrafico debe conducir VehiculoNPC reales sobre los carriles (esf alto)
- **[Trafico y vehiculos]** Arreglar o jubilar ConfiguradorTrafico (esta roto contra el codigo actual) (esf medio)
- **[Trafico y vehiculos]** Sustituir el parche AplicarGravedadTerrenoVehiculo por fisica estable (esf medio)
- **[Trafico y vehiculos]** Dar feel AAA al VehiculoNPC: marcha atras/desatasco, separacion entre coches y velocidad por carril (esf alto)
- **[Jugador y camara]** Damping asimetrico del spring-arm para eliminar el popping de camara (esf medio)
- **[Jugador y camara]** Extraer un CamaraOrbital compartido (a pie + vehiculo) y cablear shake real (esf alto)
- **[Subsistema Manifestacion]** Conectar el dano del jugador al combate real (esf bajo)
- **[Subsistema Manifestacion]** Dar IA y destino a la Guardia Civil tras el spawn (esf medio)
- **[Subsistema Manifestacion]** Config de Boids por agente y no por el primer agente del snapshot (esf medio)
- **[Misiones y tutorial]** Cachear la lista de Objetivos por mision (eliminar alloc por frame) (esf medio)
- **[Misiones y tutorial]** Waypoints/marcadores diegeticos en el mundo para cada objetivo (esf alto)
- **[Misiones y tutorial]** Persistir el progreso de la mision principal (esf medio)
- **[Combate y armas]** Enganchar ragdoll, wanted y reacciones al matar con balas (esf medio)
- **[Combate y armas]** Arreglar la economía de munición (munición infinita al cambiar de arma) (esf bajo)
- **[Combate y armas]** Mejorar realismo de la explosión: closest point, line-of-sight y techo de buffer (esf medio)
- **[Audio]** Configurar atenuacion 3D por clip en el pool (rolloff/min/maxDistance/doppler) (esf medio)
- **[Audio]** Conectar el sistema de oclusion (hoy codigo muerto) (esf alto)
- **[Audio]** Eliminar doble aplicacion de VolMaster (esf bajo)
- **[Audio]** Routing real por AudioMixerGroup + ducking + lowpass de oclusion (esf alto)
- **[HUD UI y menus]** Minimapa: no renderizar la escena completa cada frame (esf medio)
- **[HUD UI y menus]** Indicador de dano direccional roto: siempre apunta al origen (0,0,0) (esf medio)
- **[HUD UI y menus]** Unificar la pila de UI: eliminar OnGUI/IMGUI y datos duplicados (esf alto)
- **[Subsistema "Mundo Vivo nuevo"]** Batchear traviesas y carriles con GPU instancing / mesh combinado en vez de miles de GameObjects (esf medio)
- **[Subsistema "Mundo Vivo nuevo"]** Dar presencia fisica y atropello al tren (collider + aviso) (esf medio)

## 📋 Resumen por subsistema
### Terreno LIDAR
_Los 4 archivos existen y el subsistema está sorprendentemente maduro para un proyecto de este tipo: pipeline de fuentes en cascada (RAW 0.5m -> XYZ -> ASC 5m -> fallback RAW), resample bicúbico Catmull-Rom desde NativeArray persistente, validación RMSE por zon_
- Unificar el sistema de coordenadas de ProcesadorNubePuntos con el resto del pipeline (UTM metros) (imp alto/esf medio)
- Resolver el conflicto de rango de altitud (terY ~900m vs Z_MAX-Z_MIN=57.26m) (imp alto/esf medio)
- Quitar el repintado completo del alphamap en SistemaTerreno.Update (hitch por nieve estacional) (imp alto/esf medio)
- Hacer que OptimizadorTerreno haga LOD real (o renombrarlo y delegar en HLOD/Terrain) (imp medio/esf alto)
- Corregir el datum de altitud en AplicarDesdeXYZ (fill-holes y coherencia con RAW) (imp medio/esf medio)
- Dar variacion de bioma a hierba/arboles y usar normal real para aspecto norte (imp medio/esf medio)

### Subsistema Edificios
_Los 5 archivos existen y están bien documentados, con un FusionadorEdificiosUltra serio (13 fuentes: LIDAR, INSPIRE, DSM, Overture, OSM, mapillary) y jerarquías de prioridad correctas para altura/forma/color. Pero el subsistema tiene un problema estructural gr_
- Unificar en un solo pipeline y eliminar la ruta de primitivos sueltos (imp alto/esf bajo)
- Combinar los detalles de fachada en un mesh por edificio (no cubos sueltos) (imp alto/esf alto)
- Dejar de crear un Material unico por edificio (cache por id rompe instancing) (imp alto/esf bajo)
- Corregir las transiciones del LODGroup (formula erronea) (imp alto/esf medio)
- Vidrio real con material e inset, eliminando z-fighting de ventanas (imp alto/esf medio)
- Reactivar GeneradorTejadosAAA o borrarlo; conectar el tejado bueno al pipeline canonico (imp medio/esf medio)

### Vegetacion
_Encontrados todos los archivos. El subsistema esta razonablemente bien arquitecturado para un proyecto academico/indie: AlsasuaTreeStreamer.cs usa Burst+IJobParallelFor con NativeArrays persistentes (zero-alloc en Update), pooling por especie, clasificacion po_
- Pasar la especie al devolver arboles al pool (imp alto/esf bajo)
- Evitar duplicado de arboles Posicionador vs Streamer (imp alto/esf bajo)
- Corregir longitud de ocupacion en JobComprobarOcupacion (imp alto/esf bajo)
- Anadir variacion de escala/tinte y LOD crossfade al streaming para feel AAA (imp alto/esf medio)
- Histeresis y spawn progresivo para eliminar popping de arboles (imp medio/esf medio)
- Hacer util SistemaVegetacion o eliminarlo (imp medio/esf medio)

### Clima y atmósfera
_Encontré los 4 scripts en E:/Desk/DAM/Altsasu_Manifa/Assets/Scripts: SistemaClima.cs, SistemaVolumenHDRP.cs, SistemaCharcos.cs y SistemaVientoVegetacion.cs (este último cubre el "viento" que SistemaClima sólo dejaba en Physics.gravity). El subsistema tiene bue_
- Unificar autoridad sobre el sol y la niebla en SistemaVolumenHDRP (imp alto/esf alto)
- Corregir el puente de hora real (reflexión) (imp alto/esf bajo)
- Mover la niebla por clima a HDRP Fog y borrar las escrituras legacy muertas (imp alto/esf medio)
- Arreglar el viento físico para que no sea gravedad lateral global (imp alto/esf medio)
- Hacer el blend día/noche y la transición de clima dependientes del tiempo real transcurrido (imp medio/esf bajo)
- Crear partículas de nieve procedurales y dar vida/variación a los charcos (imp medio/esf medio)

### NPC IA y agenda
_Subsistema localizado y leído completo en E:/Desk/DAM/Altsasu_Manifa/Assets/Scripts. La arquitectura base es sólida y notablemente cuidada para un proyecto académico: NPCBase centraliza NavMeshAgent/Animator/jugador con activación diferida a NavMesh; SistemaIA_
- Resolver el conflicto Agenda vs FSM civil: dar prioridad a la agenda como capa de destino (imp alto/esf medio)
- Agenda data-driven y con histéresis horaria en vez de FindObjectsByType cada minuto (imp alto/esf medio)
- Escalonar/cullear el Update de IA por distancia al jugador (LOD de comportamiento) (imp alto/esf medio)
- Eliminar la repulsión brusca del jugador y darle feel de multitud (imp medio/esf bajo)
- VariadorAparienciaNPC: variar también la Y del NavMeshAgent y desincronizar animación correctamente (imp medio/esf bajo)
- Reaprovechar slots de SistemaDeteccionIA y permitir crecer más allá de 32 policías (imp alto/esf medio)

### Policía y Wanted
_Subsistema encontrado y revisado en su totalidad. Archivos clave: E:/Desk/DAM/Altsasu_Manifa/Assets/Scripts/PoliciaForalIA.cs, GameManagerAltsasua.cs, SistemaDeteccionIA.cs, NPCBase.cs, Core/IWantedSystem.cs, Core/ServiceLocator.cs.

La arquitectura base es só_
- Liberar el slot de SistemaDeteccionIA al morir/destruir el policía (imp alto/esf bajo)
- Conectar el nivel de búsqueda (estrellas) con el comportamiento del policía a pie (imp alto/esf medio)
- Corregir la máscara de obstáculos por defecto (capasObstaculo = ~0) (imp alto/esf bajo)
- Spawnear PoliciaForalIA reales desde el wanted system (no solo coches vacíos) (imp alto/esf medio)
- Persecución con last-known-position e investigación, no teletransporte de estado (imp medio/esf medio)
- Robustez del disparo: filtrar capas y telegrafiar el ataque (imp medio/esf medio)

### Trafico y vehiculos
_Encontre los 5 archivos. La jerarquia de clases esta bien diseñada: VehiculoBase (abstracta, IDamageable + Rigidbody) con dos subclases que comparten salud/daño via hooks OnDanoRecibido/IniciarDestruccion. ControladorVehiculoJugador es la pieza mas solida y AA_
- Reparar la integracion: SistemaTrafico debe conducir VehiculoNPC reales sobre los carriles (imp alto/esf alto)
- Arreglar o jubilar ConfiguradorTrafico (esta roto contra el codigo actual) (imp alto/esf medio)
- Sustituir el parche AplicarGravedadTerrenoVehiculo por fisica estable (imp alto/esf medio)
- Dar feel AAA al VehiculoNPC: marcha atras/desatasco, separacion entre coches y velocidad por carril (imp alto/esf alto)
- Profundidad de conduccion del jugador: RPM/marchas, derrape audible y reposicionar coche volcado (imp medio/esf medio)
- Unificar el radio/UI de entrada al vehiculo (datos duplicados y OnGUI) (imp medio/esf bajo)

### Jugador y camara
_Subsistema encontrado y revisado: E:/Desk/DAM/Altsasu_Manifa/Assets/Scripts/ControladorJugador.cs (892 lineas, 3a persona spring-arm + cuerpo Mixamo/procedural), ControladorVehiculoJugador.cs (vehiculo WheelCollider + Pacejka + transicion de camara) y SistemaD_
- Damping asimetrico del spring-arm para eliminar el popping de camara (imp alto/esf medio)
- Extraer un CamaraOrbital compartido (a pie + vehiculo) y cablear shake real (imp alto/esf alto)
- Sustituir AudioSource.PlayClipAtPoint de pasos por un pool/AudioSource fijo (imp medio/esf bajo)
- Camera-relative aiming con retIcula proyectada y recentrado tras inactividad (imp medio/esf medio)
- Robustecer el grounding con Cesium en vez de heuristicas de signo de velVert (imp medio/esf medio)
- Vida del coche en el motor: limp-mode y feedback de conduccion por daño (imp medio/esf bajo)

### Subsistema Manifestacion
_Los cuatro archivos pedidos existen y estan en E:/Desk/DAM/Altsasu_Manifa/Assets/Scripts: SistemaManifestacion.cs, JuegoManifestacion.cs, SistemaApoyoPopular.cs, HUDManifestacion.cs (mas PropsDestruccionManifestacion.cs). El subsistema esta razonablemente avan_
- Conectar el dano del jugador al combate real (imp alto/esf bajo)
- Dar IA y destino a la Guardia Civil tras el spawn (imp alto/esf medio)
- Config de Boids por agente y no por el primer agente del snapshot (imp alto/esf medio)
- Reemplazar BroadcastMessage de victoria por evento tipado (imp medio/esf bajo)
- Mover dibujo del HUD de IMGUI/OnGUI a UI Toolkit o Canvas (imp medio/esf alto)
- Anadir vida a la masa: consignas sincronizadas, humo y reaccion al jugador (imp medio/esf medio)

### Misiones y tutorial
_Subsistema localizado y completo en E:/Desk/DAM/Altsasu_Manifa/Assets/Scripts. Existe una cadena lineal de 12 misiones principales encadenadas por SiguienteMision (M01 RobarCoche -> ... -> M12 ManifaFinal) mas 5 secundarias gestionadas por GestorMisionesSecund_
- Cachear la lista de Objetivos por mision (eliminar alloc por frame) (imp alto/esf medio)
- Waypoints/marcadores diegeticos en el mundo para cada objetivo (imp alto/esf alto)
- Persistir el progreso de la mision principal (imp alto/esf medio)
- Validar que las pintadas/fotos/coches cuentan en la zona correcta (imp medio/esf bajo)
- Tutorial reactivo en vez de temporizado a ciegas (imp medio/esf medio)
- Limpiar suscripciones a eventos al iniciar/abandonar mision (imp medio/esf medio)

### Combate y armas
_Los 6 archivos existen en E:/Desk/DAM/Altsasu_Manifa/Assets/Scripts (SistemaArmasExtendido.cs, SistemaDisparo.cs, SistemaImpactos.cs, SistemaExplosion.cs, SistemaDestruccion.cs, SistemaRagdoll.cs). El subsistema está sorprendentemente maduro para un proyecto a_
- Enganchar ragdoll, wanted y reacciones al matar con balas (imp alto/esf medio)
- Arreglar la economía de munición (munición infinita al cambiar de arma) (imp alto/esf bajo)
- Selección directa de todas las armas y HUD de munición coherente (imp medio/esf bajo)
- Sacar la detección de material del hot path de impacto (imp medio/esf medio)
- Mejorar realismo de la explosión: closest point, line-of-sight y techo de buffer (imp alto/esf medio)
- Evitar autodetonación y physics-glitch del molotov reciclado (imp medio/esf bajo)

### Audio
_El subsistema existe y arranca correctamente: AudioManager (singleton I) tiene pool de 32 AudioSources, registro por enum Clip con categoria/volumen/flag 3D, sintesis procedural de relleno solida (GenDisparo/GenExplosion/GenSirena/GenChirrido, etc.), fade in/o_
- Configurar atenuacion 3D por clip en el pool (rolloff/min/maxDistance/doppler) (imp alto/esf medio)
- Conectar el sistema de oclusion (hoy codigo muerto) (imp alto/esf alto)
- Eliminar doble aplicacion de VolMaster (imp alto/esf bajo)
- Routing real por AudioMixerGroup + ducking + lowpass de oclusion (imp alto/esf alto)
- Pool con prioridad y proteccion de colas largas (imp medio/esf medio)
- Suavizar saltos de zona acustica y respetar is3D en loops (imp medio/esf medio)

### HUD UI y menus
_Los 6 archivos existen en E:/Desk/DAM/Altsasu_Manifa/Assets/Scripts y estan razonablemente completos y funcionales para un proyecto academico/indie. HUDCanvas es el sistema principal: construye todo el Canvas uGUI por codigo (vida, armadura, dinero animado, wa_
- Minimapa: no renderizar la escena completa cada frame (imp alto/esf medio)
- Indicador de dano direccional roto: siempre apunta al origen (0,0,0) (imp alto/esf medio)
- Unificar la pila de UI: eliminar OnGUI/IMGUI y datos duplicados (imp alto/esf alto)
- Feedback de vida baja en el HUD (campos _fillVida/_fillArmadura sin uso) (imp medio/esf bajo)
- FOV e Idioma no se aplican de forma robusta (imp medio/esf medio)
- Persistencia de opciones: faltan Save tras remapear y en sliders (imp medio/esf bajo)

### Subsistema "Mundo Vivo nuevo"
_Todos los archivos existen y estan razonablemente bien estructurados para un prototipo: codigo limpio, comentado, con patron singleton (Instance/OnDestroy), DefaultExecutionOrder coherente, respaldos a color plano cuando faltan las texturas CC0 (TexturasVivo.C_
- Batchear traviesas y carriles con GPU instancing / mesh combinado en vez de miles de GameObjects (imp alto/esf medio)
- Dar presencia fisica y atropello al tren (collider + aviso) (imp alto/esf medio)
- Audio de tren real (bocina + traqueteo rodante) en vez de loop de naturaleza (imp medio/esf bajo)
- Ligar dia/noche de faros y luces de tunel al SistemaClima real con histeresis (imp medio/esf bajo)
- Presupuesto de luces realtime en los tuneles (HDRP es caro en point lights) (imp medio/esf medio)
- Construccion diferida/streaming por distancia para evitar hitch de Start() (imp medio/esf alto)
