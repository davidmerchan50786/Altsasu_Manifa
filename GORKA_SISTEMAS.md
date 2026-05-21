# GORKA_SISTEMAS — Documentación técnica del Vertical Slice
**Proyecto:** Altsasu_Manifa · Unity 6 HDRP  
**Rama:** Traducción de los 4 pilares de Gorka Games (UE5 → Unity C#)

---

## Sistema 1 — ControladorVehiculoJugador

### Qué hace
Implementa la conducción de un vehículo jugable mediante WheelColliders y la Fórmula Mágica de Pacejka para la fricción lateral de los neumáticos. Gestiona la entrada y salida del jugador (tecla E), la transición de cámara al asiento del conductor, el motor con tracción configurable (RWD/4WD) y el freno de mano para derrapar.

### Conexión con el resto del proyecto
| Sistema externo | Cómo se conecta |
|---|---|
| `ControladorJugador` | Se desactiva al entrar al vehículo (CharacterController + enabled=false); se reactiva al salir. La propiedad `CamaraTP` de ControladorJugador se usa para la transición de cámara. |
| `AlsasuaLogger` | Registra eventos de entrada/salida en el log de depuración. |
| `AudioManager` | No directo — usa un AudioSource propio configurado en el Inspector. |
| HUD / GameManager | Expone `VelocidadKmh` y `JugadorDentro` como propiedades públicas para que el HUD muestre el velocímetro y deshabilite acciones del jugador. |

### Setup en el Editor (desde cero)

1. Crea un GameObject vacío llamado `Coche_Montero` (o el nombre del vehículo).
2. Añade un componente **Rigidbody** al GO raíz (mass=1400, drag=0.05, angularDrag=0.2).
3. Añade el componente **ControladorVehiculoJugador** al mismo GO raíz.
4. Crea 4 GOs vacíos hijos llamados `WC_DI`, `WC_DD`, `WC_TI`, `WC_TD`. Añade un componente **WheelCollider** a cada uno. Colócalos en las posiciones de las ruedas (a la altura del eje, con `suspensionDistance=0.2`).
5. En el Inspector de ControladorVehiculoJugador, arrastra cada WheelCollider al campo correspondiente (rDI, rDD, rTI, rTD).
6. (Opcional) Crea 4 GOs con los meshes visuales de las ruedas y asígnalos a los campos meshDI, meshDD, meshTI, meshTD. El script los sincroniza en cada frame.
7. Crea un GameObject vacío hijo llamado `AsientoConductor`. Colócalo en la posición de la cabeza del conductor (aprox. x=-0.4, y=0.45, z=0.05 en local). Asígnalo al campo **Asiento Conductor** en el Inspector.
8. (Opcional) Crea un GO con **AudioSource** e íncluye un AudioClip de motor; asígnalos a los campos **Motor Audio** y **Clip Motor**.
9. (Opcional) Crea 4 sistemas de partículas para humo y asígnalos en el array **Humo Ruedas** (índice 0=DI, 1=DD, 2=TI, 3=TD).
10. Asegúrate de que el GameObject del jugador tiene el componente **ControladorJugador** y el tag `"Player"`.

### Parámetros clave del Inspector

| Nombre | Valor por defecto | Efecto |
|---|---|---|
| `parMaximo` | 300 N·m | Par máximo del motor. Sube para más aceleración. |
| `parFreno` | 800 N·m | Fuerza de frenado de servicio (tecla S). |
| `parFrenoMano` | 1400 N·m | Freno de mano trasero. Alto = derrape controlado. |
| `anguloMaxDireccion` | 35° | Ángulo máximo de giro de ruedas delanteras. |
| `velocidadMax` | 25 m/s (~90 km/h) | Límite de velocidad. 33 m/s ≈ 120 km/h. |
| `cuatroRuedas` | false (RWD) | true = 4WD (40% adelante / 60% atrás). |
| `pacejkaB` | 10 | Rigidez del neumático. Mayor = límite más abrupto. |
| `pacejkaC` | 1.9 | Factor de forma. 1.5 para barro/off-road. |
| `pacejkaD` | 1.0 | Pico de adherencia. 0.65 para asfalto mojado. |
| `pacejkaE` | -1 | Factor de curvatura. Negativo = neumático de carretera. |
| `alturaCentroMasa` | -0.35 m | Más negativo = más estabilidad en curvas. |
| `antiRollForce` | 3500 N | Fuerza anti-vuelco lateral. |
| `rigidezMuelle` | 35000 N/m | Rigidez de la suspensión. SUV típico: 30000-40000. |
| `amortiguacion` | 4500 N·s/m | Amortiguación de la suspensión. |
| `velocidadTransCamara` | 5 | Velocidad del Lerp de cámara al entrar/salir. |

### Teclas y controles (al estar dentro del vehículo)

| Tecla | Acción |
|---|---|
| E | Entrar / salir del vehículo (detecta al jugador en radio 3.5 m) |
| W / Flecha arriba | Acelerar |
| S / Flecha abajo | Frenar / marcha atrás |
| A / Flecha izquierda | Girar izquierda |
| D / Flecha derecha | Girar derecha |
| Espacio | Freno de mano (bloquea ruedas traseras → derrape) |
| Mando — stick izq. | Aceleración y dirección |
| Mando — L1 | Freno de mano |

### Diagrama de flujo

```
Update()
  ├─ Tecla E pulsada
  │     ├─ jugadorDentro=false → IntentarEntrar()
  │     │     └─ OverlapSphere 3.5m → ControladorJugador encontrado → EntraJugador()
  │     │           ├─ Desactivar CharacterController + ControladorJugador.enabled=false
  │     │           ├─ Guardar posición/rotación de cámara original
  │     │           ├─ Parental jugador bajo el coche, ocultar renderers
  │     │           └─ StartCoroutine TransicionCamara(entrando=true) [0.55 s]
  │     └─ jugadorDentro=true → SalirVehiculo()
  │           ├─ Colocar jugador 2.4m a la derecha del coche
  │           ├─ Reactivar CharacterController + ControladorJugador.enabled=true
  │           ├─ Mostrar renderers del jugador
  │           └─ StartCoroutine TransicionCamara(entrando=false) [0.55 s]
  ├─ LeerInput() → inputAcel, inputDir, inputFrenoMano
  ├─ SincronizarMeshesRuedas() → WheelCollider.GetWorldPose() → Transform
  └─ ActualizarMotorSonido() → AudioSource.pitch según vel + inputAcel

FixedUpdate() [solo si jugadorDentro]
  ├─ AplicarMotor()
  │     ├─ par = inputAcel × parMaximo × (1 - velRatio²×0.6)
  │     ├─ RWD → motorTorque a TI+TD | 4WD → 0.4 DI+DD / 0.6 TI+TD
  │     ├─ Freno servicio si inputAcel < -0.05
  │     └─ Freno de mano → brakeTorque=1400 en TI+TD
  ├─ AplicarDireccion() → steerAngle con reducción a alta velocidad
  ├─ AplicarPacejka()
  │     ├─ Por cada rueda: alpha = atan(sidewaysSlip)
  │     ├─ Fy = D·sin(C·atan(B·alpha − E·(B·alpha − atan(B·alpha))))
  │     ├─ AddForceAtPosition(Fy × cargaNormal × sidewaysDir)
  │     └─ Activar/desactivar partículas si |sidewaysSlip| > 0.45
  ├─ AplicarAntiRoll() → barra anti-vuelco delantera + trasera
  └─ LimitarVelocidad() → clamp a velocidadMax
```

---

## Sistema 2 — PoliciaForalIA

### Qué hace
Implementa la IA de los agentes de la Policía Foral mediante una máquina de estados (Patrullando → Sospechoso → Persiguiendo → Atacando → Muerto) con detección por cono de visión real y raycast multi-punto. Sustituye el Overlap Event invisible de Gorka Games por línea de visión física con soporte de cobertura (el jugador puede ocultarse detrás de objetos).

### Conexión con el resto del proyecto
| Sistema externo | Cómo se conecta |
|---|---|
| `ControladorJugador` | Se obtiene por tag `"Player"`. Llama a `RecibirDano()` cuando dispara al jugador. Lee `EstaMuerto` para no disparar a un jugador muerto. |
| `GameManagerAltsasua` | Llama a `AumentarBusqueda(1)` cuando confirma avistamiento del jugador (estado Sospechoso → Persiguiendo). |
| `SistemaAtmosfera` | Consultado para saber si es de noche y usar el cono de linterna en lugar del cono diurno. Se cachea en Awake (O(1)). |
| `NavMeshAgent` | Componente requerido. Gestiona la navegación por waypoints y la persecución. |
| `AlsasuaLogger` | Log de muerte del agente. |

### Setup en el Editor (desde cero)

1. Crea un GameObject `Policia_Foral_01` con la geometría del personaje.
2. Añade los componentes **NavMeshAgent** y **CapsuleCollider** (requeridos por script).
3. Añade el componente **PoliciaForalIA**.
4. Asegúrate de que la escena tiene un **NavMesh horneado** (Window → AI → Navigation → Bake).
5. Crea entre 3 y 8 GOs vacíos en la escena como waypoints de patrulla (ej. `WP_01`, `WP_02`...). Asígnalos al array **Waypoints** en el Inspector.
6. (Opcional — modo nocturno) Añade un GO hijo con un componente **Light** (Spot o Point) y asígnalo al campo **Linterna**.
7. Configura **Capas Obstáculo** en el Inspector: deben incluir las capas de paredes, coches y geometría de ciudad, pero NO la capa `Player` (el raycast necesita atravesar al jugador para detectarlo).
8. Asegúrate de que el GameObject del jugador tiene el tag `"Player"` y la capa `Player`.
9. Añade un `GameManagerAltsasua` en la escena si quieres que la búsqueda aumente al ser detectado.

### Parámetros clave del Inspector

| Nombre | Valor por defecto | Efecto |
|---|---|---|
| `radioVision` | 22 m | Radio máximo de visión diurna. |
| `anguloVision` | 90° (±45°) | Amplitud del cono de visión frontal. |
| `radioEscucha` | 5 m | Detección por proximidad sin LOS (pasos, disparo). |
| `alturasLOS` | 0.1, 0.85, 1.6 m | Alturas de raycast (pies, pecho, cabeza). Más puntos = más robusto ante cobertura parcial. |
| `radioLinterna` | 12 m | Radio del cono de linterna nocturna. |
| `anguloLinterna` | 30° | Ángulo más estrecho pero prioritario de noche. |
| `tiempoEsperaWP` | 3 s | Pausa en cada waypoint antes de continuar. |
| `velPatrulla` | 1.4 m/s | Velocidad en modo patrulla. |
| `velPerseguir` | 5.2 m/s | Velocidad en persecución. |
| `vida` | 120 | Puntos de vida del agente. |
| `danoPorDisparo` | 20 | Daño infligido al jugador por disparo. |
| `cadencia` | 1.4 s | Tiempo entre disparos. |
| `radioAtaque` | 16 m | Distancia máxima de ataque. |
| `dispersion` | 0.05 | Imprecisión del disparo (0 = puntería perfecta). |

### Teclas y controles
El jugador no controla directamente a la IA. Las acciones del jugador que influyen en el comportamiento del agente:

| Acción del jugador | Efecto en el agente |
|---|---|
| Entrar en el campo de visión | Patrullando → Sospechoso (2.8 s de confirmación) |
| Mantenerse visible | Sospechoso → Persiguiendo + `AumentarBusqueda(1)` |
| Esconderse 6 s | Persiguiendo → Patrullando |
| Disparar al agente | Recibe daño; si estaba en patrulla, pasa a Persiguiendo directamente |

### Diagrama de flujo

```
Update()
  ├─ EstadoMuerto → return (no hace nada)
  ├─ ActualizarLinterna() [throttle: solo cada 3 frames]
  │     └─ Activar si EsDeNoche || Persiguiendo || Atacando
  └─ switch(estado)
        ├─ Patrullando → TickPatrulla()
        │     ├─ NavMeshAgent sigue waypoints en bucle con pausa tiempoEsperaWP
        │     └─ JugadorEnVision() || JugadorEnEscucha() → CambiarEstado(Sospechoso)
        │
        ├─ Sospechoso → TickSospecha() [2.8 s]
        │     ├─ Rota suavemente hacia última posición conocida
        │     ├─ JugadorEnVision() → AumentarBusqueda(1) → CambiarEstado(Persiguiendo)
        │     └─ Timer agotado sin LOS → CambiarEstado(Patrullando)
        │
        ├─ Persiguiendo → TickPersecucion()
        │     ├─ NavMeshAgent.SetDestination(jugador.position)
        │     ├─ dist <= radioAtaque → CambiarEstado(Atacando)
        │     └─ Sin visión ni escucha durante 6 s → CambiarEstado(Patrullando)
        │
        └─ Atacando → TickAtaque()
              ├─ NavMeshAgent.ResetPath (se queda parado)
              ├─ Rota hacia el jugador (Slerp)
              ├─ dist > radioAtaque×1.35 → CambiarEstado(Persiguiendo)
              └─ timer cadencia → Disparar()
                    └─ Raycast con dispersión → ControladorJugador.RecibirDano()

JugadorEnVision() [llamado cada frame desde ticks]
  ├─ Calcular radio y ángulo según EsDeNoche()
  ├─ dist > radio → false
  ├─ Angle(forward, dirJugador) > anguloAct/2 → false
  └─ foreach altura in alturasLOS:
        Raycast(ojo→jugador+altura, maskObstaculo)
        Si ningún obstáculo → true (detectado)
```

---

## Sistema 3 — SistemaChunks

### Qué hace
Implementa un sistema de World Partition equivalente al de UE5: divide la ciudad de Alsasua en secciones geográficas (chunks) y las activa o desactiva según la distancia del jugador, con histéresis para evitar parpadeo (pop-in). Incluye control de LOD por distancia y desactivación diferida en un frame separado para evitar picos de CPU.

### Conexión con el resto del proyecto
| Sistema externo | Cómo se conecta |
|---|---|
| `ControladorJugador` | Localiza al jugador por tag `"Player"` en Start(). Si el jugador cambia de posición bruscamente (teletransporte), llamar a `ForzarActualizacion()`. |
| LODGroup | Detectado automáticamente en cada chunk mediante `GetComponentInChildren<LODGroup>()`. Se fuerza LOD 0 (alta calidad) dentro de `radioLOD` y LOD 1 fuera. |
| `AlsasuaLogger` | Log de activación/desactivación de chunks con contador. |
| HUD / Debug | Expone `ChunksActivos` y `ChunksTotales` como propiedades. Muestra overlay en pantalla si `mostrarGUI=true`. |

### Setup en el Editor (desde cero)

1. Organiza la escena en grupos lógicos: selecciona la geometría de cada barrio o zona y métela en un GO padre (ej. `Plaza_Fueros`, `CalleAlsasua`, `PoligonoIndustrial`).
2. Crea un GO vacío en la escena llamado `WorldManager`.
3. Añade el componente **SistemaChunks** al GO `WorldManager`.
4. En el Inspector, expande el array **Chunks** y añade una entrada por cada zona. Arrastra cada GO padre al campo **Go** de su entrada. Rellena **Nombre** con un texto descriptivo. Deja **Centro Auto = true** para que el script calcule el centro desde el pivot del GO.
5. Ajusta **Radio Activacion** (150-200 m recomendado) y **Radio Desactivacion** (siempre mayor, 250-300 m). La diferencia entre ambos es la zona de histéresis.
6. Ajusta **Radio LOD** (100-130 m): los chunks entre este radio y `radioActivacion` se renderizan en LOD 1.
7. Asegúrate de que el jugador tiene el tag `"Player"`.
8. (Opcional) Activa **Mostrar GUI** para ver el contador de chunks en pantalla durante las pruebas.
9. (Opcional) Activa **Desactivacion Diferida** para repartir el coste de SetActive(false) en frames separados.

### Parámetros clave del Inspector

| Nombre | Valor por defecto | Efecto |
|---|---|---|
| `radioActivacion` | 180 m | Chunks dentro de este radio se activan. |
| `radioDesactivacion` | 240 m | Chunks fuera de este radio se desactivan. Diferencia con activación = zona de histéresis (evita pop-in). |
| `radioLOD` | 120 m | Dentro: LOD 0 (máxima calidad). Fuera: LOD 1. |
| `intervaloCheck` | 0.4 s | Frecuencia de comprobación de distancias. No hacerlo cada frame. |
| `desactivacionDiferida` | true | Desactiva en el siguiente frame para evitar picos de CPU. |
| `mostrarGUI` | true | Overlay en pantalla con contador de chunks (solo Editor/Development Build). |
| `Chunk.centroAuto` | true | El centro del chunk se calcula desde el pivot del GO en Start(). |

### Teclas y controles
El sistema es automático y no requiere input del jugador. API pública disponible para otros sistemas:

| Método | Cuándo usarlo |
|---|---|
| `ForzarActualizacion()` | Después de un teletransporte del jugador para activar chunks inmediatamente. |
| `ActivarTodo()` | Para capturas de pantalla o renders del mapa completo. |
| `DesactivarTodo()` | Para liberar memoria antes de cargar otra escena. |

### Diagrama de flujo

```
Start()
  ├─ BuscarJugador() → tag "Player"
  ├─ InicializarChunks()
  │     ├─ Por cada chunk: calcular centro (auto o manual)
  │     ├─ Detectar LODGroup en hijos
  │     └─ SetActive(false) a todos → primera comprobación los activa
  └─ ComprobarChunks() [inmediata]

Update() [cada frame]
  ├─ timerCheck -= deltaTime
  └─ timerCheck <= 0 → ComprobarChunks() + reset timer (0.4 s)

ComprobarChunks() [cada 0.4 s]
  └─ Por cada chunk:
        dist = Distance(jugador, chunk.centro)
        ├─ !activo && dist <= radioActivacion
        │     └─ ActivarChunk() → SetActive(true), LOD 0, log
        ├─ activo && dist > radioDesactivacion
        │     ├─ desactivacionDiferida=true → Coroutine: espera 1 frame → DesactivarChunk()
        │     └─ desactivacionDiferida=false → DesactivarChunk() inmediato
        └─ activo && dist dentro de rangos
              └─ LODGroup.ForceLOD(dist <= radioLOD ? 0 : 1)
```

---

## Cómo probar el Vertical Slice de Gorka (10 minutos)

### Paso 1 — Escena mínima

1. En Unity: **File → New Scene → Basic (Built-in)** o duplica una escena existente.
2. Elimina cualquier cámara que venga por defecto (la crea ControladorJugador en Start).
3. Crea un plano grande: **GameObject → 3D Object → Plane**, escala a (10, 1, 10) para tener 100 × 100 m de suelo.
4. Añade **Window → AI → Navigation** y haz **Bake** del NavMesh sobre el plano (necesario para PoliciaForalIA).

### Paso 2 — GameObjects necesarios

#### Jugador
1. **GameObject → Create Empty** → nombre: `Jugador`.
2. Añade componente **ControladorJugador**. El script crea su cámara y cuerpo procedural automáticamente si no hay prefab asignado.
3. Tag del GO: `Player`. Layer: `Player`.
4. Posición inicial: `(0, 1, 0)`.

#### Vehículo (Gorka Pillar 1 + 2)
1. **GameObject → Create Empty** → nombre: `Coche`.
2. Añade **Rigidbody** (mass=1400).
3. Añade **ControladorVehiculoJugador**.
4. Crea 4 hijos vacíos para los WheelColliders (ver Setup del Sistema 1, pasos 4-5).
5. Crea un hijo vacío `AsientoConductor` en posición local `(-0.4, 0.45, 0.05)`. Asígnalo en el Inspector.
6. Posición del coche: `(5, 0.5, 0)` (cerca del jugador para poder entrar de inmediato).

#### Policía Foral (Gorka Pillar 3)
1. **GameObject → Create Empty** → nombre: `Policia_01`.
2. Añade **CapsuleCollider** (height=1.8, radius=0.4, center=(0, 0.9, 0)).
3. Añade **NavMeshAgent** (speed=1.4, stoppingDistance=1.5).
4. Añade **PoliciaForalIA**.
5. Crea 3 GOs vacíos `WP_01`, `WP_02`, `WP_03` en distintas posiciones del plano. Asígnalos al array Waypoints.
6. Configura **Capas Obstáculo** = `Everything` excepto `Player` e `Ignore Raycast`.
7. Posición del policía: `(-10, 0, 0)`.

#### World Partition (Gorka Pillar 4)
1. Crea 2-3 GOs vacíos en la escena representando zonas: `Zona_A` (pos 0,0,0), `Zona_B` (pos 0,0,80), `Zona_C` (pos 80,0,0). Añade cubos hijos a cada uno como geometría de prueba.
2. **GameObject → Create Empty** → nombre: `WorldManager`.
3. Añade **SistemaChunks**.
4. Añade 3 entradas al array Chunks, arrastra cada zona, activa Centro Auto.
5. Ajusta `radioActivacion=50`, `radioDesactivacion=70` para que sea visible en el plano de prueba.

#### GameManager (requerido por PoliciaForalIA)
1. **GameObject → Create Empty** → nombre: `GameManager`.
2. Añade el componente **GameManagerAltsasua** (debe existir en el proyecto). Si no existe, crea un MonoBehaviour mínimo con el método `AumentarBusqueda(int n)`.

### Paso 3 — Verificación de cada Pillar

#### Pillar 1 + 2 — Vehículo
- Pulsa **Play**.
- Mueve el jugador (WASD) hasta estar a menos de 3.5 m del coche (el Gizmo cian indica el radio en Scene View).
- Pulsa **E**: el jugador desaparece y la cámara transiciona al interior del coche en ~0.55 s.
- Usa W/S/A/D para conducir. Mantén Espacio para derrapar.
- Pulsa **E** de nuevo: el jugador reaparece a la derecha del coche.
- Verificación OK si: la cámara transiciona suavemente, el coche responde al input y el jugador recupera el control al salir.

#### Pillar 3 — Policía Foral
- En Scene View con el policía seleccionado: verifica el gizmo amarillo (cono de visión) y el círculo cyan (radio de escucha).
- Con el juego en Play, acércate al frente del policía dentro del cono. El agente debe pasar a estado Sospechoso (detente y espera 2.8 s) y luego Persiguiendo.
- Escóndete detrás de un cubo: el agente debe ir a tu última posición conocida y volver a patrullar tras 6 s sin visión.
- Verificación OK si: el agente no te detecta desde detrás de un obstáculo aunque estés dentro del radio de visión.

#### Pillar 4 — World Partition
- Con `radioActivacion=50` y el jugador en (0,1,0): Zona_A debe estar activa, Zona_B y Zona_C inactivas.
- El overlay en pantalla (esquina superior izquierda) muestra `CHUNKS: 1 / 3 activos`.
- Mueve el jugador hacia Zona_B (WASD o teleport en el Inspector). Al cruzar los 50 m, Zona_B se activa. Al superar los 70 m de Zona_A, esta se desactiva.
- Verificación OK si: el contador cambia y los GOs de las zonas se activan/desactivan en la jerarquía.

---

*Generado automáticamente — Altsasu_Manifa · Unity 6 HDRP · Mayo 2026*
